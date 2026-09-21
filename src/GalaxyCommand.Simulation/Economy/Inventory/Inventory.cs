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
public sealed partial class Inventory
{
    private readonly MaterialInventoryCompatibilityMap? _materialCompatibility;
    private readonly Dictionary<MaterialId, Quantity> _stored = [];
    private readonly Dictionary<MaterialId, Quantity> _reservedByMaterial = [];
    private readonly Dictionary<ReservationId, Reservation> _reservations = [];
    private readonly Dictionary<CapacityReservationId, CapacityReservation> _capacityReservations = [];

    /// <summary>
    /// Creates a material-compatibility inventory without generalized custody
    /// metadata. Existing production and transport callers use this boundary
    /// until their explicit migration.
    /// </summary>
    public Inventory(InventoryId id, Quantity capacity)
    {
        Id = id;
        Capacity = capacity;
    }

    /// <summary>
    /// Creates a generalized physical inventory with explicit custody and a
    /// controlling principal.
    /// </summary>
    public Inventory(
        InventoryId id,
        InventoryCustody custody,
        Quantity capacity)
        : this(id, capacity)
    {
        ArgumentNullException.ThrowIfNull(custody);
        Custody = custody;
    }

    /// <summary>
    /// Creates a generalized inventory whose legacy material facade resolves
    /// through one explicit, validated compatibility mapping.
    /// </summary>
    public Inventory(
        InventoryId id,
        InventoryCustody custody,
        Quantity capacity,
        MaterialInventoryCompatibilityMap materialCompatibility)
        : this(id, custody, capacity)
    {
        ArgumentNullException.ThrowIfNull(materialCompatibility);
        _materialCompatibility = materialCompatibility;
    }

    public InventoryId Id { get; }

    public Quantity Capacity { get; }

    public InventoryCustody? Custody { get; }

    public Quantity TotalStored { get; private set; }

    public Quantity ReservedCapacity { get; private set; }

    internal bool HasCommitments =>
        _reservations.Count > 0 || _capacityReservations.Count > 0 ||
        _physicalReservations.Count > 0;

    /// <summary>
    /// Captures stored material and explicit commitments in stable identity
    /// order. Derived totals are deliberately excluded.
    /// </summary>
    internal InventoryCheckpoint CaptureCheckpoint() =>
        new(
            Id,
            Custody,
            Capacity,
            _stored
                .OrderBy(entry => entry.Key.Value)
                .Select(entry => new InventoryMaterialCheckpoint(
                    entry.Key,
                    entry.Value)),
            _reservations.Values.OrderBy(reservation => reservation.Id.Value),
            _capacityReservations.Values.OrderBy(reservation => reservation.Id.Value),
            _fungibleStored
                .OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal)
                .Select(entry => new InventoryFungibleCheckpoint(
                    entry.Key,
                    entry.Value)),
            _discreteItems.Values
                .OrderBy(instance => instance.Id.Value)
                .Select(instance => new InventoryDiscreteItemCheckpoint(
                    instance.Id,
                    instance.DefinitionKey)),
            _physicalReservations.Values.OrderBy(
                reservation => reservation.Id.Value));

    /// <summary>
    /// Validates and privately restores one inventory without allocating new
    /// identities or publishing intermediate state.
    /// </summary>
    internal static CheckpointResult<Inventory> RestoreCheckpoint(
        InventoryCheckpoint checkpoint,
        string path = "$.checkpoint.inventories") =>
        RestoreCheckpointCore(
            checkpoint,
            definitions: null,
            materialCompatibility: null,
            path);

    /// <summary>
    /// Restores generalized holdings by resolving every saved qualified key
    /// through the compatible immutable definition catalog.
    /// </summary>
    internal static CheckpointResult<Inventory> RestoreCheckpoint(
        InventoryCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        string path = "$.checkpoint.inventories")
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return RestoreCheckpointCore(
            checkpoint,
            definitions,
            materialCompatibility: null,
            path);
    }

    /// <summary>
    /// Restores generalized holdings and preserves the validated legacy
    /// material facade used by mapped economy sessions.
    /// </summary>
    internal static CheckpointResult<Inventory> RestoreCheckpoint(
        InventoryCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        MaterialInventoryCompatibilityMap materialCompatibility,
        string path = "$.checkpoint.inventories")
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(materialCompatibility);
        return RestoreCheckpointCore(
            checkpoint,
            definitions,
            materialCompatibility,
            path);
    }

    /// <summary>
    /// Restores the compatibility and generalized state through one validation
    /// path while making the content catalog optional only for legacy state.
    /// </summary>
    private static CheckpointResult<Inventory> RestoreCheckpointCore(
        InventoryCheckpoint checkpoint,
        PhysicalDefinitionCatalog? definitions,
        MaterialInventoryCompatibilityMap? materialCompatibility,
        string path)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (checkpoint.Id.Value == 0)
        {
            return Rejected(path, "id", "An inventory identity must be non-zero.");
        }

        var stored = new SortedDictionary<MaterialId, Quantity>(
            EntityIdComparer<MaterialId>.Instance);
        UInt128 totalStored = 0;
        for (int index = 0; index < checkpoint.StoredMaterials.Count; index++)
        {
            InventoryMaterialCheckpoint? material = checkpoint.StoredMaterials[index];
            if (material is null)
            {
                return Rejected(
                    path,
                    $"storedMaterials[{index}]",
                    "A stored material entry is missing.");
            }

            if (material.MaterialId.Value == 0 || material.Quantity == Quantity.Zero)
            {
                return Rejected(
                    path,
                    $"storedMaterials[{index}]",
                    "Stored material requires a non-zero identity and quantity.");
            }

            if (!stored.TryAdd(material.MaterialId, material.Quantity))
            {
                return Rejected(
                    path,
                    $"storedMaterials[{index}].materialId",
                    "Stored material identities must be unique within an inventory.");
            }

            totalStored += material.Quantity.Units;
            if (totalStored > checkpoint.Capacity.Units)
            {
                return Rejected(
                    path,
                    "storedMaterials",
                    "Stored material exceeds inventory capacity.");
            }
        }

        var reservations = new SortedDictionary<ReservationId, Reservation>(
            EntityIdComparer<ReservationId>.Instance);
        var reservedByMaterial = new SortedDictionary<MaterialId, UInt128>(
            EntityIdComparer<MaterialId>.Instance);
        for (int index = 0; index < checkpoint.Reservations.Count; index++)
        {
            Reservation? reservation = checkpoint.Reservations[index];
            CheckpointValidationFailure? failure = ValidateReservation(
                checkpoint,
                stored,
                materialCompatibility,
                reservations,
                reservedByMaterial,
                reservation,
                index,
                path);
            if (failure is not null)
            {
                return CheckpointResult<Inventory>.Rejected(failure);
            }
        }

        var capacityReservations =
            new SortedDictionary<CapacityReservationId, CapacityReservation>(
                EntityIdComparer<CapacityReservationId>.Instance);
        UInt128 reservedCapacity = 0;
        for (int index = 0; index < checkpoint.CapacityReservations.Count; index++)
        {
            CapacityReservation? reservation = checkpoint.CapacityReservations[index];
            CheckpointValidationFailure? failure = ValidateCapacityReservation(
                checkpoint,
                capacityReservations,
                reservation,
                index,
                path,
                ref reservedCapacity);
            if (failure is not null)
            {
                return CheckpointResult<Inventory>.Rejected(failure);
            }
        }

        if (totalStored + reservedCapacity > checkpoint.Capacity.Units)
        {
            return Rejected(
                path,
                "capacityReservations",
                "Stored material and reserved capacity exceed inventory capacity.");
        }

        if (materialCompatibility is not null && checkpoint.Custody is null)
        {
            return Rejected(
                path,
                "custody",
                "A mapped material inventory requires explicit custody.");
        }

        if (materialCompatibility is not null && stored.Count > 0)
        {
            return Rejected(
                path,
                "storedMaterials",
                "A mapped material inventory must save materials as generalized holdings.");
        }

        if (definitions is not null && materialCompatibility is not null)
        {
            foreach (PhysicalDefinition mapped in materialCompatibility.Mappings.Values)
            {
                if (definitions.Get(mapped.Key) != mapped)
                {
                    return Rejected(
                        path,
                        "materialCompatibility",
                        $"Mapped definition {mapped.Key} does not match the restore catalog.");
                }
            }
        }

        Inventory restored = materialCompatibility is not null
            ? new Inventory(
                checkpoint.Id,
                checkpoint.Custody!,
                checkpoint.Capacity,
                materialCompatibility)
            : checkpoint.Custody is null
                ? new Inventory(checkpoint.Id, checkpoint.Capacity)
                : new Inventory(checkpoint.Id, checkpoint.Custody, checkpoint.Capacity);
        // Direct assignment preserves the saved commitments without emitting
        // new reserve, transfer, or consumption operations during load.
        foreach ((MaterialId materialId, Quantity quantity) in stored)
        {
            restored._stored.Add(materialId, quantity);
        }

        foreach ((ReservationId reservationId, Reservation reservation) in reservations)
        {
            restored._reservations.Add(reservationId, reservation);
        }

        foreach ((MaterialId materialId, UInt128 quantity) in reservedByMaterial)
        {
            restored._reservedByMaterial.Add(
                materialId,
                new Quantity((ulong)quantity));
        }

        foreach ((CapacityReservationId reservationId, CapacityReservation reservation)
                 in capacityReservations)
        {
            restored._capacityReservations.Add(reservationId, reservation);
        }

        restored.TotalStored = new Quantity((ulong)totalStored);
        restored.ReservedCapacity = new Quantity((ulong)reservedCapacity);
        CheckpointValidationFailure? physicalFailure = RestorePhysicalState(
            restored,
            checkpoint,
            definitions,
            path);
        if (physicalFailure is not null)
        {
            return CheckpointResult<Inventory>.Rejected(physicalFailure);
        }

        return CheckpointResult<Inventory>.Success(restored);
    }

    public Quantity RemainingCapacity =>
        new(Capacity.Units - UsedCapacity.Units - ReservedCapacity.Units);

    public Quantity Stored(MaterialId materialId)
    {
        if (_materialCompatibility is null)
        {
            return _stored.GetValueOrDefault(materialId, Quantity.Zero);
        }

        PhysicalDefinition? definition = _materialCompatibility.Get(materialId);
        return definition is null
            ? Quantity.Zero
            : FungibleStored(definition.Key);
    }

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
                $"Inventory capacity {Capacity.Units} exceeded by adding {quantity.Units} to {UsedCapacity.Units} stored units.");
        }
    }

    public bool TryAdd(MaterialId materialId, Quantity quantity)
    {
        if (_materialCompatibility is not null)
        {
            if (quantity == Quantity.Zero)
            {
                return true;
            }

            return StoreFungible(
                _materialCompatibility.GetRequired(materialId),
                quantity).IsAccepted;
        }

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

        if (quantity == Quantity.Zero)
        {
            return;
        }

        RemoveStoredMaterial(materialId, quantity);
    }

    public Reservation Reserve(
        ReservationId reservationId,
        MaterialId materialId,
        Quantity quantity,
        ReservationOwner owner)
    {
        if (_reservations.ContainsKey(reservationId) ||
            _physicalReservations.ContainsKey(reservationId))
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
            RemoveStoredMaterial(materialId, quantity);
            SetReservedQuantity(materialId, Reserved(materialId).Subtract(quantity));
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

    private void RemoveStoredMaterial(MaterialId materialId, Quantity quantity)
    {
        if (_materialCompatibility is { } compatibility)
        {
            PhysicalDefinition definition = compatibility.GetRequired(materialId);
            ApplyRemoveFungible(definition.Key, quantity, quantity);
            return;
        }

        SetMaterialQuantity(materialId, Stored(materialId).Subtract(quantity));
        TotalStored = TotalStored.Subtract(quantity);
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

    /// <summary>
    /// Validates one material reservation and accumulates its derived material
    /// total without mutating the restored inventory.
    /// </summary>
    private static CheckpointValidationFailure? ValidateReservation(
        InventoryCheckpoint checkpoint,
        SortedDictionary<MaterialId, Quantity> stored,
        MaterialInventoryCompatibilityMap? materialCompatibility,
        SortedDictionary<ReservationId, Reservation> reservations,
        SortedDictionary<MaterialId, UInt128> reservedByMaterial,
        Reservation? reservation,
        int index,
        string path)
    {
        string reservationPath = $"reservations[{index}]";
        if (reservation is null)
        {
            return Failure(path, reservationPath, "A material reservation is missing.");
        }

        if (reservation.Id.Value == 0 ||
            reservation.InventoryId != checkpoint.Id ||
            reservation.MaterialId.Value == 0 ||
            reservation.Quantity == Quantity.Zero ||
            !IsValidOwner(reservation.Owner))
        {
            return Failure(
                path,
                reservationPath,
                "A material reservation has invalid identity, quantity, inventory, material, or owner data.");
        }

        if (!reservations.TryAdd(reservation.Id, reservation))
        {
            return Failure(
                path,
                $"{reservationPath}.id",
                "Material reservation identities must be unique within an inventory.");
        }

        if (!TryGetReservationHolding(
                checkpoint,
                stored,
                materialCompatibility,
                reservation.MaterialId,
                out Quantity storedQuantity))
        {
            return Failure(
                path,
                $"{reservationPath}.materialId",
                "A material reservation references material that is not stored.");
        }

        _ = reservedByMaterial.TryGetValue(
            reservation.MaterialId,
            out UInt128 existingReserved);
        UInt128 materialReserved = existingReserved + reservation.Quantity.Units;
        if (materialReserved > storedQuantity.Units)
        {
            return Failure(
                path,
                reservationPath,
                "Material reservations exceed the stored quantity.");
        }

        reservedByMaterial[reservation.MaterialId] = materialReserved;
        return null;
    }

    private static bool TryGetReservationHolding(
        InventoryCheckpoint checkpoint,
        SortedDictionary<MaterialId, Quantity> stored,
        MaterialInventoryCompatibilityMap? materialCompatibility,
        MaterialId materialId,
        out Quantity quantity)
    {
        if (materialCompatibility is null)
        {
            return stored.TryGetValue(materialId, out quantity);
        }

        PhysicalDefinition? definition = materialCompatibility.Get(materialId);
        if (definition is not null)
        {
            foreach (InventoryFungibleCheckpoint? holding in checkpoint.FungibleHoldings)
            {
                if (holding is not null && holding.DefinitionKey == definition.Key)
                {
                    quantity = holding.Quantity;
                    return true;
                }
            }
        }

        quantity = Quantity.Zero;
        return false;
    }

    /// <summary>
    /// Validates one capacity reservation and accumulates total committed
    /// capacity without mutating the restored inventory.
    /// </summary>
    private static CheckpointValidationFailure? ValidateCapacityReservation(
        InventoryCheckpoint checkpoint,
        SortedDictionary<CapacityReservationId, CapacityReservation> reservations,
        CapacityReservation? reservation,
        int index,
        string path,
        ref UInt128 reservedCapacity)
    {
        string reservationPath = $"capacityReservations[{index}]";
        if (reservation is null)
        {
            return Failure(path, reservationPath, "A capacity reservation is missing.");
        }

        if (reservation.Id.Value == 0 ||
            reservation.InventoryId != checkpoint.Id ||
            reservation.Quantity == Quantity.Zero ||
            !IsValidOwner(reservation.Owner))
        {
            return Failure(
                path,
                reservationPath,
                "A capacity reservation has invalid identity, quantity, inventory, or owner data.");
        }

        if (!reservations.TryAdd(reservation.Id, reservation))
        {
            return Failure(
                path,
                $"{reservationPath}.id",
                "Capacity reservation identities must be unique within an inventory.");
        }

        reservedCapacity += reservation.Quantity.Units;
        return null;
    }

    private static bool IsValidOwner(ReservationOwner? owner) =>
        owner switch
        {
            ReservationOwner.TransportJob value => value.JobId.Value != 0,
            ReservationOwner.ProductionJob value => value.JobId.Value != 0,
            ReservationOwner.ConstructionOrder value => value.OrderId.Value != 0,
            _ => false,
        };

    private static CheckpointResult<Inventory> Rejected(
        string path,
        string field,
        string message) =>
        CheckpointResult<Inventory>.Rejected(Failure(path, field, message));

    private static CheckpointValidationFailure Failure(
        string path,
        string field,
        string message) =>
        new($"{path}.{field}", message);
}

/// <summary>
/// World ownership of all physical inventories.
/// </summary>
public sealed partial class InventoryRegistry
{
    private readonly Dictionary<InventoryId, Inventory> _inventories = [];

    public void Add(Inventory inventory)
    {
        if (!_inventories.TryAdd(inventory.Id, inventory))
        {
            throw new InvalidOperationException($"Duplicate inventory {inventory.Id}.");
        }
    }

    public bool Contains(InventoryId inventoryId) =>
        _inventories.ContainsKey(inventoryId);

    public Inventory? Get(InventoryId inventoryId) => _inventories.GetValueOrDefault(inventoryId);

    /// <summary>
    /// Captures every inventory in stable identity order.
    /// </summary>
    internal InventoryRegistryCheckpoint CaptureCheckpoint() =>
        new(_inventories.Values
            .OrderBy(inventory => inventory.Id.Value)
            .Select(inventory => inventory.CaptureCheckpoint()));

    /// <summary>
    /// Validates and directly restores all inventories without transferring
    /// material or replaying reservation operations.
    /// </summary>
    internal static CheckpointResult<InventoryRegistry> RestoreCheckpoint(
        InventoryRegistryCheckpoint checkpoint) =>
        RestoreCheckpointCore(
            checkpoint,
            definitions: null,
            materialCompatibility: null);

    /// <summary>
    /// Restores every inventory against one compatible immutable definition
    /// catalog before publishing the registry.
    /// </summary>
    internal static CheckpointResult<InventoryRegistry> RestoreCheckpoint(
        InventoryRegistryCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return RestoreCheckpointCore(
            checkpoint,
            definitions,
            materialCompatibility: null);
    }

    internal static CheckpointResult<InventoryRegistry> RestoreCheckpoint(
        InventoryRegistryCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        MaterialInventoryCompatibilityMap materialCompatibility)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(materialCompatibility);
        return RestoreCheckpointCore(
            checkpoint,
            definitions,
            materialCompatibility);
    }

    private static CheckpointResult<InventoryRegistry> RestoreCheckpointCore(
        InventoryRegistryCheckpoint checkpoint,
        PhysicalDefinitionCatalog? definitions,
        MaterialInventoryCompatibilityMap? materialCompatibility)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var restored = new InventoryRegistry();
        for (int index = 0; index < checkpoint.Inventories.Count; index++)
        {
            InventoryCheckpoint? inventory = checkpoint.Inventories[index];
            if (inventory is null)
            {
                return CheckpointResult<InventoryRegistry>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.inventories[{index}]",
                        "An inventory checkpoint is missing."));
            }

            if (restored.Contains(inventory.Id))
            {
                return CheckpointResult<InventoryRegistry>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.inventories[{index}].id",
                        "Inventory identities must be unique."));
            }

            CheckpointResult<Inventory> result = definitions is null
                ? Inventory.RestoreCheckpoint(
                    inventory,
                    $"$.checkpoint.inventories[{index}]")
                : materialCompatibility is not null
                    ? Inventory.RestoreCheckpoint(
                        inventory,
                        definitions,
                        materialCompatibility,
                        $"$.checkpoint.inventories[{index}]")
                : Inventory.RestoreCheckpoint(
                    inventory,
                    definitions,
                    $"$.checkpoint.inventories[{index}]");
            if (!result.IsSuccess)
            {
                return CheckpointResult<InventoryRegistry>.Rejected(
                    result.Failure!);
            }

            restored.Add(result.Value!);
        }

        return CheckpointResult<InventoryRegistry>.Success(restored);
    }

    internal bool ApplyRemove(InventoryId inventoryId) =>
        _inventories.Remove(inventoryId);

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
