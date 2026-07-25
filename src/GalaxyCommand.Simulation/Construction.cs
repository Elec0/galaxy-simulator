using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable material and work requirements shared by all construction designs.
/// </summary>
public sealed class ConstructionRecipe
{
    private readonly ReadOnlyDictionary<MaterialId, Quantity> _inputs;

    public ConstructionRecipe(
        IEnumerable<KeyValuePair<MaterialId, Quantity>> inputs,
        Work requiredWork)
    {
        var sortedInputs = new SortedDictionary<MaterialId, Quantity>(
            EntityIdComparer<MaterialId>.Instance);
        foreach ((MaterialId materialId, Quantity quantity) in inputs)
        {
            sortedInputs.Add(materialId, quantity);
        }

        _inputs = new ReadOnlyDictionary<MaterialId, Quantity>(sortedInputs);
        RequiredWork = requiredWork;
    }

    public IReadOnlyDictionary<MaterialId, Quantity> Inputs => _inputs;

    public Work RequiredWork { get; }
}

/// <summary>
/// Product-neutral definition consumed by the construction pipeline.
/// </summary>
public abstract class ConstructionDesign
{
    protected ConstructionDesign(
        ConstructionDesignId id,
        string name,
        ConstructionRecipe recipe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
    }

    public ConstructionDesignId Id { get; }

    public string Name { get; }

    public ConstructionRecipe Recipe { get; }
}

/// <summary>
/// Stable lookup shared by every kind of constructible design.
/// </summary>
public sealed class ConstructionDesignCatalog
{
    private readonly SortedDictionary<ConstructionDesignId, ConstructionDesign> _designs =
        new(EntityIdComparer<ConstructionDesignId>.Instance);

    public IEnumerable<ConstructionDesign> Designs => _designs.Values;

    public void Add(ConstructionDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (!_designs.TryAdd(design.Id, design))
        {
            throw new InvalidOperationException(
                $"Duplicate construction design {design.Id}.");
        }
    }

    public ConstructionDesign? Get(ConstructionDesignId designId) =>
        _designs.GetValueOrDefault(designId);
}

public enum ConstructionOrderStatus
{
    WaitingForInputs,
    Running,
    Completed,
}

/// <summary>
/// Product-neutral runtime state for one finite construction request.
/// </summary>
public sealed class ConstructionOrder
{
    private readonly SortedDictionary<MaterialId, List<ReservationId>> _reservationIds =
        new(EntityIdComparer<MaterialId>.Instance);

    internal ConstructionOrder(ConstructionOrderId id, ConstructionDesign design)
    {
        Id = id;
        Design = design;
    }

    public ConstructionOrderId Id { get; }

    public ConstructionDesign Design { get; }

    public ConstructionDesignId DesignId => Design.Id;

    public ConstructionOrderStatus Status { get; internal set; } =
        ConstructionOrderStatus.WaitingForInputs;

    public SimulationTime? CompletesAt { get; internal set; }

    public EventGeneration Generation { get; } = new(0);

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

public sealed class ConstructionIdSequences
{
    private readonly IdSequence<ConstructionOrderId> _orders = new();

    internal ConstructionOrderId AllocateOrder() => _orders.Allocate();
}

/// <summary>
/// Shared FIFO construction lifecycle. Product-specific owners materialize the
/// completed product through the completion callback.
/// </summary>
public sealed class ConstructionProcess
{
    private readonly Throughput _throughput;
    private readonly Queue<ConstructionOrder> _queued = new();
    private readonly SortedDictionary<ConstructionOrderId, ConstructionOrder> _completed =
        new(EntityIdComparer<ConstructionOrderId>.Instance);

    public ConstructionProcess(
        FacilityId facilityId,
        InventoryId inventoryId,
        Throughput throughput)
    {
        FacilityId = facilityId;
        InventoryId = inventoryId;
        _throughput = throughput;
    }

    public FacilityId FacilityId { get; }

    public InventoryId InventoryId { get; }

    public ConstructionOrder? ActiveOrder { get; private set; }

    public int QueuedOrderCount => _queued.Count;

    public ConstructionOrder? GetCompletedOrder(ConstructionOrderId orderId) =>
        _completed.GetValueOrDefault(orderId);

    public ConstructionOrderId Enqueue(
        ConstructionIdSequences ids,
        ConstructionDesign design)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(design);
        ConstructionOrderId id = ids.AllocateOrder();
        var order = new ConstructionOrder(id, design);
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

    public IReadOnlyDictionary<MaterialId, Quantity> UnmetInputs(Inventory inventory)
    {
        RequireConfiguredInventory(inventory);
        var unmet = new SortedDictionary<MaterialId, Quantity>(
            EntityIdComparer<MaterialId>.Instance);
        if (ActiveOrder is not { Status: ConstructionOrderStatus.WaitingForInputs } order)
        {
            return unmet;
        }

        foreach ((MaterialId materialId, Quantity required) in order.Design.Recipe.Inputs)
        {
            Quantity missing = required.Subtract(order.ReservedInput(inventory, materialId));
            if (missing > Quantity.Zero)
            {
                unmet.Add(materialId, missing);
            }
        }

        return unmet;
    }

    public SimulationTime? PrepareActive(
        IdSequence<ReservationId> reservationIds,
        Inventory inventory,
        SimulationTime now)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveOrder is not { Status: ConstructionOrderStatus.WaitingForInputs } order)
        {
            return null;
        }

        foreach ((MaterialId materialId, Quantity required) in order.Design.Recipe.Inputs)
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
                new ReservationOwner.ConstructionOrder(order.Id));
            order.AddReservation(materialId, reservationId);
        }

        bool allReserved = order.Design.Recipe.Inputs.All(input =>
            order.ReservedInput(inventory, input.Key) == input.Value);
        if (!allReserved)
        {
            return null;
        }

        inventory.ConsumeReservations(
            order.AllReservationIds(),
            new ReservationOwner.ConstructionOrder(order.Id));
        order.ClearReservations();
        SimulationTime completesAt =
            now.Add(_throughput.DurationFor(order.Design.Recipe.RequiredWork));
        order.Status = ConstructionOrderStatus.Running;
        order.CompletesAt = completesAt;
        return completesAt;
    }

    public ConstructionOrder? CompleteActive(
        SimulationTime now,
        Action<ConstructionOrder> materializeProduct)
    {
        ArgumentNullException.ThrowIfNull(materializeProduct);
        if (ActiveOrder is not
            {
                Status: ConstructionOrderStatus.Running,
                CompletesAt: { } completesAt,
            } order
            || now < completesAt)
        {
            return null;
        }

        materializeProduct(order);
        ActiveOrder = null;
        order.Status = ConstructionOrderStatus.Completed;
        _completed.Add(order.Id, order);
        if (_queued.TryDequeue(out ConstructionOrder? next))
        {
            ActiveOrder = next;
        }

        return order;
    }

    private void RequireConfiguredInventory(Inventory inventory)
    {
        if (inventory.Id != InventoryId)
        {
            throw new ArgumentException(
                $"Construction process expected inventory {InventoryId}, but received {inventory.Id}.",
                nameof(inventory));
        }
    }
}
