namespace GalaxyCommand.Simulation;

public enum ShipConstructionOrderStatus
{
    WaitingForInputs,
    Running,
    Completed,
}

public sealed class ShipConstructionOrder
{
    private readonly SortedDictionary<MaterialId, List<ReservationId>> _reservationIds =
        new(EntityIdComparer<MaterialId>.Instance);

    internal ShipConstructionOrder(
        ShipConstructionOrderId id,
        ShipBlueprint blueprint,
        IEnumerable<KeyValuePair<MaterialId, Quantity>> inputs,
        Work requiredWork)
    {
        Id = id;
        Blueprint = blueprint;
        var sortedInputs = new SortedDictionary<MaterialId, Quantity>(
            EntityIdComparer<MaterialId>.Instance);
        foreach ((MaterialId materialId, Quantity quantity) in inputs)
        {
            sortedInputs.Add(materialId, quantity);
        }

        Inputs = sortedInputs;
        RequiredWork = requiredWork;
    }

    public ShipConstructionOrderId Id { get; }
    public ShipBlueprint Blueprint { get; }
    public IReadOnlyDictionary<MaterialId, Quantity> Inputs { get; }
    public Work RequiredWork { get; }
    public ShipConstructionOrderStatus Status { get; internal set; } = ShipConstructionOrderStatus.WaitingForInputs;
    public SimulationTime? CompletesAt { get; internal set; }
    public ShipId? ConstructedShipId { get; internal set; }

    internal Quantity ReservedInput(Inventory inventory, MaterialId materialId)
    {
        if (!_reservationIds.TryGetValue(materialId, out List<ReservationId>? reservationIds))
        {
            return Quantity.Zero;
        }

        ulong units = 0;
        foreach (ReservationId reservationId in reservationIds)
        {
            if (inventory.GetReservation(reservationId) is { } reservation)
            {
                units = checked(units + reservation.Quantity.Units);
            }
        }

        return new Quantity(units);
    }

    internal void AddReservation(MaterialId materialId, ReservationId reservationId)
    {
        if (!_reservationIds.TryGetValue(materialId, out List<ReservationId>? reservations))
        {
            reservations = [];
            _reservationIds.Add(materialId, reservations);
        }

        reservations.Add(reservationId);
    }

    internal IReadOnlyList<ReservationId> AllReservationIds() =>
        _reservationIds.Values.SelectMany(ids => ids).ToArray();

    internal void ClearReservations() => _reservationIds.Clear();
}

public sealed class ShipyardIdSequences
{
    private readonly IdSequence<ShipConstructionOrderId> _orders = new();

    internal ShipConstructionOrderId AllocateOrder() => _orders.Allocate();
}

/// <summary>
/// Finite FIFO construction capability that creates persistent freighters.
/// </summary>
public sealed class Shipyard
{
    private readonly Throughput _throughput;
    private readonly Queue<ShipConstructionOrder> _queued = new();
    private readonly SortedDictionary<ShipConstructionOrderId, ShipConstructionOrder> _completed =
        new(EntityIdComparer<ShipConstructionOrderId>.Instance);

    public Shipyard(
        FacilityId facilityId,
        OrganizationId organizationId,
        LocationId locationId,
        InventoryId inventoryId,
        Throughput throughput)
    {
        FacilityId = facilityId;
        OrganizationId = organizationId;
        LocationId = locationId;
        InventoryId = inventoryId;
        _throughput = throughput;
    }

    public FacilityId FacilityId { get; }
    public OrganizationId OrganizationId { get; }
    public LocationId LocationId { get; }
    public InventoryId InventoryId { get; }
    public ShipConstructionOrder? ActiveOrder { get; private set; }

    public ShipConstructionOrder? GetCompletedOrder(ShipConstructionOrderId orderId) =>
        _completed.GetValueOrDefault(orderId);

    public IReadOnlyDictionary<MaterialId, Quantity> UnmetInputs(Inventory inventory)
    {
        var unmet = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        if (ActiveOrder is not { Status: ShipConstructionOrderStatus.WaitingForInputs } order)
        {
            return unmet;
        }

        foreach ((MaterialId materialId, Quantity required) in order.Inputs)
        {
            Quantity missing = required.Subtract(order.ReservedInput(inventory, materialId));
            if (missing > Quantity.Zero)
            {
                unmet.Add(materialId, missing);
            }
        }

        return unmet;
    }

    public ShipConstructionOrderId Enqueue(
        ShipyardIdSequences ids,
        ShipBlueprint blueprint,
        IEnumerable<KeyValuePair<MaterialId, Quantity>> inputs,
        Work requiredWork)
    {
        ShipConstructionOrderId id = ids.AllocateOrder();
        var order = new ShipConstructionOrder(id, blueprint, inputs, requiredWork);
        if (ActiveOrder is null)
        {
            ActiveOrder = order;
        }
        else
        {
            _queued.Enqueue(order);
        }

        return id;
    }

    public SimulationTime? PrepareActive(
        IdSequence<ReservationId> reservationIds,
        Inventory inventory,
        SimulationTime now)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveOrder is not { Status: ShipConstructionOrderStatus.WaitingForInputs } order)
        {
            return null;
        }

        foreach ((MaterialId materialId, Quantity required) in order.Inputs)
        {
            Quantity missing = required.Subtract(order.ReservedInput(inventory, materialId));
            Quantity toReserve = missing.Min(inventory.Available(materialId));
            if (toReserve == Quantity.Zero)
            {
                continue;
            }

            ReservationId reservationId = reservationIds.Allocate();
            inventory.Reserve(
                reservationId,
                materialId,
                toReserve,
                new ReservationOwner.ShipConstructionOrder(order.Id));
            order.AddReservation(materialId, reservationId);
        }

        bool allReserved = order.Inputs.All(input =>
            order.ReservedInput(inventory, input.Key) == input.Value);
        if (!allReserved)
        {
            return null;
        }

        inventory.ConsumeReservations(
            order.AllReservationIds(),
            new ReservationOwner.ShipConstructionOrder(order.Id));
        order.ClearReservations();
        SimulationTime completesAt = now.Add(_throughput.DurationFor(order.RequiredWork));
        order.Status = ShipConstructionOrderStatus.Running;
        order.CompletesAt = completesAt;
        return completesAt;
    }

    public ShipId? CompleteActive(
        IdSequence<ShipId> shipIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        ShipRegistry ships,
        SimulationTime now)
    {
        if (ActiveOrder is not
            {
                Status: ShipConstructionOrderStatus.Running,
                CompletesAt: { } completesAt,
            } order
            || now < completesAt)
        {
            return null;
        }

        ShipId shipId = shipIds.Allocate();
        InventoryId cargoInventoryId = inventoryIds.Allocate();
        inventories.Add(new Inventory(cargoInventoryId, order.Blueprint.CargoCapacity));
        ships.AddFreighter(new Ship(
            shipId,
            OrganizationId,
            order.Blueprint.Id,
            LocationId,
            cargoInventoryId));

        ActiveOrder = null;
        order.Status = ShipConstructionOrderStatus.Completed;
        order.ConstructedShipId = shipId;
        _completed.Add(order.Id, order);
        if (_queued.TryDequeue(out ShipConstructionOrder? next))
        {
            ActiveOrder = next;
        }

        return shipId;
    }

    private void RequireConfiguredInventory(Inventory inventory)
    {
        if (inventory.Id != InventoryId)
        {
            throw new ArgumentException(
                $"Shipyard expected inventory {InventoryId}, but received {inventory.Id}.",
                nameof(inventory));
        }
    }
}
