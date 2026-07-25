namespace GalaxyCommand.Simulation;

/// <summary>
/// Domain activity that owns an inventory reservation.
/// </summary>
public abstract record ReservationOwner
{
    private ReservationOwner() { }

    public sealed record TransportJob(TransportJobId JobId) : ReservationOwner;

    public sealed record ProductionJob(ProductionJobId JobId) : ReservationOwner;

    public sealed record ConstructionOrder(ConstructionOrderId OrderId) : ReservationOwner;
}

/// <summary>
/// Material held aside for one domain activity.
/// </summary>
public sealed record Reservation(
    ReservationId Id,
    InventoryId InventoryId,
    MaterialId MaterialId,
    Quantity Quantity,
    ReservationOwner Owner);

/// <summary>
/// Empty inventory capacity held for one future transfer.
/// </summary>
public sealed record CapacityReservation(
    CapacityReservationId Id,
    InventoryId InventoryId,
    Quantity Quantity,
    ReservationOwner Owner);

/// <summary>
/// Capacity-limited storage with explicit material reservations.
/// </summary>
public sealed class Inventory
{
    private readonly Dictionary<MaterialId, Quantity> _stored = [];
    private readonly Dictionary<MaterialId, Quantity> _reservedByMaterial = [];
    private readonly Dictionary<ReservationId, Reservation> _reservations = [];
    private readonly Dictionary<CapacityReservationId, CapacityReservation> _capacityReservations = [];

    public Inventory(InventoryId id, Quantity capacity)
    {
        Id = id;
        Capacity = capacity;
    }

    public InventoryId Id { get; }

    public Quantity Capacity { get; }

    public Quantity TotalStored { get; private set; }

    public Quantity ReservedCapacity { get; private set; }

    public Quantity RemainingCapacity =>
        new(Capacity.Units - TotalStored.Units - ReservedCapacity.Units);

    public Quantity Stored(MaterialId materialId) =>
        _stored.GetValueOrDefault(materialId, Quantity.Zero);

    public Quantity Reserved(MaterialId materialId) =>
        _reservedByMaterial.GetValueOrDefault(materialId, Quantity.Zero);

    public Quantity Available(MaterialId materialId) =>
        new(Stored(materialId).Units - Reserved(materialId).Units);

    public Reservation? GetReservation(ReservationId reservationId) =>
        _reservations.GetValueOrDefault(reservationId);

    public CapacityReservation? GetCapacityReservation(CapacityReservationId reservationId) =>
        _capacityReservations.GetValueOrDefault(reservationId);

    public void Add(MaterialId materialId, Quantity quantity)
    {
        if (!TryAdd(materialId, quantity))
        {
            throw new InvalidOperationException(
                $"Inventory capacity {Capacity.Units} exceeded by adding {quantity.Units} to {TotalStored.Units} stored units.");
        }
    }

    public bool TryAdd(MaterialId materialId, Quantity quantity)
    {
        if (quantity > RemainingCapacity)
        {
            return false;
        }

        _stored[materialId] = Stored(materialId).Add(quantity);
        TotalStored = TotalStored.Add(quantity);
        return true;
    }

    public CapacityReservation ReserveCapacity(
        CapacityReservationId reservationId,
        Quantity quantity,
        ReservationOwner owner)
    {
        if (_capacityReservations.ContainsKey(reservationId))
        {
            throw new InvalidOperationException($"Duplicate capacity reservation {reservationId}.");
        }

        RequirePositiveReservation(quantity);
        if (quantity > RemainingCapacity)
        {
            throw new InvalidOperationException(
                $"Inventory {Id} does not have {quantity.Units} units of uncommitted capacity.");
        }

        var reservation = new CapacityReservation(reservationId, Id, quantity, owner);
        ReservedCapacity = ReservedCapacity.Add(quantity);
        _capacityReservations.Add(reservationId, reservation);
        return reservation;
    }

    public CapacityReservation ReleaseCapacity(CapacityReservationId reservationId)
    {
        if (!_capacityReservations.Remove(reservationId, out CapacityReservation? reservation))
        {
            throw new KeyNotFoundException($"Unknown capacity reservation {reservationId}.");
        }

        ReservedCapacity = ReservedCapacity.Subtract(reservation.Quantity);
        return reservation;
    }

    public void RemoveAvailable(MaterialId materialId, Quantity quantity)
    {
        Quantity available = Available(materialId);
        if (quantity > available)
        {
            throw new InvalidOperationException(
                $"Material {materialId} has {available.Units} available units, but {quantity.Units} were requested.");
        }

        SetMaterialQuantity(materialId, Stored(materialId).Subtract(quantity));
        TotalStored = TotalStored.Subtract(quantity);
    }

    public Reservation Reserve(
        ReservationId reservationId,
        MaterialId materialId,
        Quantity quantity,
        ReservationOwner owner)
    {
        if (_reservations.ContainsKey(reservationId))
        {
            throw new InvalidOperationException($"Duplicate reservation {reservationId}.");
        }

        RequirePositiveReservation(quantity);
        Quantity available = Available(materialId);
        if (quantity > available)
        {
            throw new InvalidOperationException(
                $"Material {materialId} has {available.Units} available units, but {quantity.Units} were requested.");
        }

        var reservation = new Reservation(reservationId, Id, materialId, quantity, owner);
        _reservations.Add(reservationId, reservation);
        _reservedByMaterial[materialId] = Reserved(materialId).Add(quantity);
        return reservation;
    }

    public Reservation Release(ReservationId reservationId)
    {
        if (!_reservations.Remove(reservationId, out Reservation? reservation))
        {
            throw new KeyNotFoundException($"Unknown reservation {reservationId}.");
        }

        SetReservedQuantity(
            reservation.MaterialId,
            Reserved(reservation.MaterialId).Subtract(reservation.Quantity));
        return reservation;
    }

    public IReadOnlyList<Reservation> ConsumeReservations(
        IReadOnlyCollection<ReservationId> reservationIds,
        ReservationOwner expectedOwner)
    {
        var selected = new List<Reservation>(reservationIds.Count);
        var seen = new HashSet<ReservationId>();
        foreach (ReservationId reservationId in reservationIds)
        {
            if (!seen.Add(reservationId))
            {
                throw new InvalidOperationException($"Reservation {reservationId} was requested twice.");
            }

            Reservation reservation = GetReservation(reservationId)
                ?? throw new KeyNotFoundException($"Unknown reservation {reservationId}.");
            if (reservation.Owner != expectedOwner)
            {
                throw new InvalidOperationException(
                    $"Reservation {reservationId} belongs to {reservation.Owner}, not {expectedOwner}.");
            }

            selected.Add(reservation);
        }

        var materialTotals = new Dictionary<MaterialId, Quantity>();
        foreach (Reservation reservation in selected)
        {
            materialTotals[reservation.MaterialId] = materialTotals
                .GetValueOrDefault(reservation.MaterialId, Quantity.Zero)
                .Add(reservation.Quantity);
        }

        foreach ((MaterialId materialId, Quantity quantity) in materialTotals)
        {
            _ = Stored(materialId).Subtract(quantity);
            _ = Reserved(materialId).Subtract(quantity);
        }

        foreach (Reservation reservation in selected)
        {
            _reservations.Remove(reservation.Id);
        }

        foreach ((MaterialId materialId, Quantity quantity) in materialTotals)
        {
            SetMaterialQuantity(materialId, Stored(materialId).Subtract(quantity));
            SetReservedQuantity(materialId, Reserved(materialId).Subtract(quantity));
            TotalStored = TotalStored.Subtract(quantity);
        }

        return selected;
    }

    private static void RequirePositiveReservation(Quantity quantity)
    {
        if (quantity == Quantity.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Reservation quantity must be positive.");
        }
    }

    private void SetMaterialQuantity(MaterialId materialId, Quantity quantity)
    {
        if (quantity == Quantity.Zero)
        {
            _stored.Remove(materialId);
        }
        else
        {
            _stored[materialId] = quantity;
        }
    }

    private void SetReservedQuantity(MaterialId materialId, Quantity quantity)
    {
        if (quantity == Quantity.Zero)
        {
            _reservedByMaterial.Remove(materialId);
        }
        else
        {
            _reservedByMaterial[materialId] = quantity;
        }
    }
}

/// <summary>
/// World ownership of all physical inventories.
/// </summary>
public sealed class InventoryRegistry
{
    private readonly Dictionary<InventoryId, Inventory> _inventories = [];

    public void Add(Inventory inventory)
    {
        if (!_inventories.TryAdd(inventory.Id, inventory))
        {
            throw new InvalidOperationException($"Duplicate inventory {inventory.Id}.");
        }
    }

    public Inventory? Get(InventoryId inventoryId) => _inventories.GetValueOrDefault(inventoryId);

    public Reservation TransferReserved(
        InventoryId sourceId,
        InventoryId destinationId,
        ReservationId reservationId,
        ReservationOwner owner)
    {
        RequireDifferentInventories(sourceId, destinationId);
        Inventory source = GetRequired(sourceId);
        Inventory destination = GetRequired(destinationId);
        Reservation reservation = source.GetReservation(reservationId)
            ?? throw new KeyNotFoundException($"Unknown reservation {reservationId}.");
        if (reservation.Owner != owner)
        {
            throw new InvalidOperationException(
                $"Reservation {reservationId} belongs to {reservation.Owner}, not {owner}.");
        }

        EnsureDestinationCapacity(destination, reservation.Quantity);
        source.ConsumeReservations([reservationId], owner);
        destination.Add(reservation.MaterialId, reservation.Quantity);
        return reservation;
    }

    public void TransferAvailable(
        InventoryId sourceId,
        InventoryId destinationId,
        MaterialId materialId,
        Quantity quantity)
    {
        RequireDifferentInventories(sourceId, destinationId);
        Inventory source = GetRequired(sourceId);
        Inventory destination = GetRequired(destinationId);
        EnsureAvailable(source, materialId, quantity);
        EnsureDestinationCapacity(destination, quantity);
        source.RemoveAvailable(materialId, quantity);
        destination.Add(materialId, quantity);
    }

    public void TransferIntoReservedCapacity(
        InventoryId sourceId,
        InventoryId destinationId,
        MaterialId materialId,
        Quantity quantity,
        CapacityReservationId reservationId,
        ReservationOwner owner)
    {
        RequireDifferentInventories(sourceId, destinationId);
        Inventory source = GetRequired(sourceId);
        Inventory destination = GetRequired(destinationId);
        EnsureAvailable(source, materialId, quantity);
        CapacityReservation reservation = destination.GetCapacityReservation(reservationId)
            ?? throw new KeyNotFoundException($"Unknown capacity reservation {reservationId}.");
        if (reservation.Owner != owner)
        {
            throw new InvalidOperationException(
                $"Capacity reservation {reservationId} belongs to {reservation.Owner}, not {owner}.");
        }

        if (reservation.Quantity != quantity)
        {
            throw new InvalidOperationException(
                $"Capacity reservation {reservationId} holds {reservation.Quantity.Units}, not {quantity.Units} units.");
        }

        source.RemoveAvailable(materialId, quantity);
        destination.ReleaseCapacity(reservationId);
        destination.Add(materialId, quantity);
    }

    private Inventory GetRequired(InventoryId inventoryId) =>
        Get(inventoryId) ?? throw new KeyNotFoundException($"Unknown inventory {inventoryId}.");

    private static void RequireDifferentInventories(InventoryId sourceId, InventoryId destinationId)
    {
        if (sourceId == destinationId)
        {
            throw new InvalidOperationException($"Inventory {sourceId} cannot transfer to itself.");
        }
    }

    private static void EnsureAvailable(
        Inventory inventory,
        MaterialId materialId,
        Quantity quantity)
    {
        Quantity available = inventory.Available(materialId);
        if (quantity > available)
        {
            throw new InvalidOperationException(
                $"Material {materialId} has {available.Units} available units, but {quantity.Units} were requested.");
        }
    }

    private static void EnsureDestinationCapacity(Inventory inventory, Quantity incoming)
    {
        if (incoming > inventory.RemainingCapacity)
        {
            throw new InvalidOperationException(
                $"Inventory {inventory.Id} does not have capacity for {incoming.Units} units.");
        }
    }
}
