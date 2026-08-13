using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public enum EntityKind
{
    Ship,
}

public enum InitialShipOrderPolicy
{
    NoInitialOrder,
}

/// <summary>
/// Authoritative facility-owned choices for materializing completed ship
/// construction into the clean game session.
/// </summary>
public sealed class ShipMaterializationPolicy
{
    private readonly ReadOnlyDictionary<ConstructionDesignId, ShipDesign> _designs;

    /// <summary>
    /// Creates authoritative materialization policy for one facility and owner.
    /// </summary>
    public ShipMaterializationPolicy(
        FacilityId facilityId,
        PrincipalId principalId,
        SystemPosition position,
        ActorController baseController,
        InitialShipOrderPolicy initialOrderPolicy,
        IEnumerable<ShipDesign> allowedDesigns)
    {
        ArgumentOutOfRangeException.ThrowIfZero(facilityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(principalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        ArgumentNullException.ThrowIfNull(baseController);
        ArgumentNullException.ThrowIfNull(allowedDesigns);
        if (baseController.Kind == ActorControllerKind.Script)
        {
            throw new ArgumentException(
                "A script cannot be a materialized ship's persistent base controller.",
                nameof(baseController));
        }

        if (initialOrderPolicy != InitialShipOrderPolicy.NoInitialOrder)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialOrderPolicy),
                initialOrderPolicy,
                "Unknown initial ship order policy.");
        }

        var designs = new SortedDictionary<ConstructionDesignId, ShipDesign>(
            EntityIdComparer<ConstructionDesignId>.Instance);
        foreach (ShipDesign design in allowedDesigns)
        {
            ArgumentNullException.ThrowIfNull(design);
            if (!designs.TryAdd(design.Id, design))
            {
                throw new ArgumentException(
                    $"Duplicate allowed ship design {design.Id}.",
                    nameof(allowedDesigns));
            }
        }

        if (designs.Count == 0)
        {
            throw new ArgumentException(
                "A ship materialization policy requires at least one allowed design.",
                nameof(allowedDesigns));
        }

        FacilityId = facilityId;
        PrincipalId = principalId;
        Position = position;
        BaseController = baseController;
        InitialOrderPolicy = initialOrderPolicy;
        _designs = new ReadOnlyDictionary<ConstructionDesignId, ShipDesign>(designs);
    }

    public FacilityId FacilityId { get; }

    public PrincipalId PrincipalId { get; }

    public SystemPosition Position { get; }

    public ActorController BaseController { get; }

    public InitialShipOrderPolicy InitialOrderPolicy { get; }

    public IReadOnlyDictionary<ConstructionDesignId, ShipDesign> AllowedDesigns => _designs;

    public ShipDesign? GetDesign(ConstructionDesignId designId) =>
        _designs.GetValueOrDefault(designId);
}

public sealed record GameSessionShip(
    ShipId Id,
    PrincipalId PrincipalId,
    ConstructionDesignId DesignId,
    InventoryId CargoInventoryId);

internal enum ConstructionMaterializationDeferredReason
{
    SourceFacilityMismatch,
    MissingPendingMaterialization,
    MismatchedPendingMaterialization,
    MissingPolicy,
    DesignNotAllowed,
    DesignMismatch,
    CompletionInFuture,
    IdentifierCapacityExhausted,
    OwnerConflict,
}

internal abstract record ConstructionEntityMaterializationResult
{
    private ConstructionEntityMaterializationResult()
    {
    }

    public sealed record Materialized(
        ConstructionMaterializationEffect Effect,
        EntityId EntityId,
        ShipId ShipId,
        InventoryId CargoInventoryId) : ConstructionEntityMaterializationResult;

    public sealed record Deferred(
        ConstructionMaterializationEffect Effect,
        ConstructionMaterializationDeferredReason Reason)
        : ConstructionEntityMaterializationResult;
}

internal sealed record ConstructionMaterializationCommit(
    ConstructionEntityMaterializationResult Result,
    bool WasApplied);

public enum EntityRemovalReason
{
    Despawned,
    Destroyed,
}

public enum EntityCargoDisposition
{
    DiscardCargo,
}

public sealed record EntityRemovalRequest
{
    public EntityRemovalRequest(
        EntityId entityId,
        EntityRemovalReason reason,
        EntityCargoDisposition cargoDisposition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown entity removal reason.");
        }

        if (!Enum.IsDefined(cargoDisposition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cargoDisposition),
                cargoDisposition,
                "Unknown entity cargo disposition.");
        }

        EntityId = entityId;
        Reason = reason;
        CargoDisposition = cargoDisposition;
    }

    public EntityId EntityId { get; }

    public EntityRemovalReason Reason { get; }

    public EntityCargoDisposition CargoDisposition { get; }
}

public enum EntityRemovalRejectionReason
{
    MissingEntity,
    CargoHasCommitments,
    OwnerConflict,
    PendingMovementEventMissing,
    PendingMovementEventMismatch,
}

public abstract record EntityRemovalResult
{
    private EntityRemovalResult()
    {
    }

    public sealed record Removed(
        EntityRemovalRequest Request,
        ShipId ShipId,
        InventoryId CargoInventoryId) : EntityRemovalResult;

    public sealed record Rejected(
        EntityRemovalRequest Request,
        EntityRemovalRejectionReason Reason) : EntityRemovalResult;
}

internal sealed class GameSessionShipRegistry
{
    private readonly SortedDictionary<ShipId, GameSessionShip> _ships =
        new(EntityIdComparer<ShipId>.Instance);

    internal bool Contains(ShipId shipId) => _ships.ContainsKey(shipId);

    internal GameSessionShip? Get(ShipId shipId) =>
        _ships.GetValueOrDefault(shipId);

    internal IEnumerable<GameSessionShip> Ships => _ships.Values;

    internal void ApplyAdd(GameSessionShip ship) =>
        _ships.Add(ship.Id, ship);

    internal bool ApplyRemove(ShipId shipId) =>
        _ships.Remove(shipId);
}

/// <summary>
/// Session-wide live identity mappings. Typed subsystem identifiers remain
/// authoritative inside their owning domains.
/// </summary>
public sealed class EntityRegistry
{
    private readonly SortedDictionary<EntityId, ShipId> _shipsByEntity =
        new(EntityIdComparer<EntityId>.Instance);
    private readonly SortedDictionary<ShipId, EntityId> _entitiesByShip =
        new(EntityIdComparer<ShipId>.Instance);

    public int Count => _shipsByEntity.Count;

    public EntityKind? GetKind(EntityId entityId) =>
        _shipsByEntity.ContainsKey(entityId)
            ? EntityKind.Ship
            : null;

    public ShipId? GetShipId(EntityId entityId) =>
        _shipsByEntity.TryGetValue(entityId, out ShipId shipId)
            ? shipId
            : null;

    public EntityId? GetEntityId(ShipId shipId) =>
        _entitiesByShip.TryGetValue(shipId, out EntityId entityId)
            ? entityId
            : null;

    internal bool CanAddShip(EntityId entityId, ShipId shipId) =>
        !_shipsByEntity.ContainsKey(entityId)
        && !_entitiesByShip.ContainsKey(shipId);

    internal void ApplyAddShip(EntityId entityId, ShipId shipId)
    {
        _shipsByEntity.Add(entityId, shipId);
        _entitiesByShip.Add(shipId, entityId);
    }

    internal bool ApplyRemoveShip(EntityId entityId, ShipId shipId)
    {
        if (!_shipsByEntity.TryGetValue(entityId, out ShipId registeredShip)
            || registeredShip != shipId
            || !_entitiesByShip.TryGetValue(shipId, out EntityId registeredEntity)
            || registeredEntity != entityId)
        {
            return false;
        }

        _shipsByEntity.Remove(entityId);
        _entitiesByShip.Remove(shipId);
        return true;
    }
}

internal sealed record PreparedEntityRemoval(
    EntityRemovalRequest Request,
    ShipId ShipId,
    InventoryId CargoInventoryId,
    IReadOnlyList<TargetedShipOrder> InboundOrders);

internal abstract record EntityRemovalPreparation
{
    private EntityRemovalPreparation()
    {
    }

    internal sealed record Prepared(PreparedEntityRemoval Value)
        : EntityRemovalPreparation;

    internal sealed record Resolved(EntityRemovalResult Value)
        : EntityRemovalPreparation;
}

/// <summary>
/// Deterministic cross-owner registration and identity allocation boundary.
/// Runtime materialization and removal extend this owner in later TASK-011
/// slices.
/// </summary>
internal sealed class EntityLifecycleOwner
{
    private readonly ActorControlRegistry _control;
    private readonly EntityRegistry _entities = new();
    private readonly IdSequence<EntityId> _entityIds = new();
    private readonly IdSequence<InventoryId> _inventoryIds = new();
    private readonly InventoryRegistry _inventories = new();
    private readonly SpatialMovement _movement;
    private readonly ShipOrderCoordinator _orders;
    private readonly SortedDictionary<FacilityId, ShipMaterializationPolicy> _policies;
    private readonly SortedDictionary<MaterializationKey, ConstructionEntityMaterializationResult.Materialized>
        _receipts = new();
    private readonly SortedDictionary<EntityId, EntityRemovalResult.Removed> _removalReceipts =
        new(EntityIdComparer<EntityId>.Instance);
    private readonly GameSessionShipRegistry _ships = new();
    private readonly IdSequence<ShipId> _shipIds = new();

    internal EntityLifecycleOwner(
        SpatialMovement movement,
        ActorControlRegistry control,
        ShipOrderCoordinator orders,
        IEnumerable<ShipMaterializationPolicy> policies)
    {
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        ArgumentNullException.ThrowIfNull(policies);
        _policies = new SortedDictionary<FacilityId, ShipMaterializationPolicy>(
            EntityIdComparer<FacilityId>.Instance);
        foreach (ShipMaterializationPolicy policy in policies)
        {
            _policies.Add(policy.FacilityId, policy);
        }
    }

    private EntityLifecycleOwner(
        SpatialMovement movement,
        ActorControlRegistry control,
        ShipOrderCoordinator orders,
        IEnumerable<ShipMaterializationPolicy> policies,
        EntityRegistry entities,
        IdSequence<EntityId> entityIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        SortedDictionary<MaterializationKey, ConstructionEntityMaterializationResult.Materialized>
            receipts,
        SortedDictionary<EntityId, EntityRemovalResult.Removed> removalReceipts,
        GameSessionShipRegistry ships,
        IdSequence<ShipId> shipIds)
        : this(movement, control, orders, policies)
    {
        _entities = entities;
        _entityIds = entityIds;
        _inventoryIds = inventoryIds;
        _inventories = inventories;
        _receipts = receipts;
        _removalReceipts = removalReceipts;
        _ships = ships;
        _shipIds = shipIds;
    }

    internal EntityRegistry Entities => _entities;

    internal InventoryRegistry Inventories => _inventories;

    /// <summary>
    /// Captures live identity, inventory ownership, exact allocators, and both
    /// durable lifecycle receipt sets in stable key order.
    /// </summary>
    internal EntityLifecycleCheckpoint CaptureCheckpoint() =>
        new(
            _entityIds.CaptureCheckpoint(),
            _shipIds.CaptureCheckpoint(),
            _inventoryIds.CaptureCheckpoint(),
            _inventories.CaptureCheckpoint(),
            _ships.Ships
                .Select(ship => new EntityLifecycleShipCheckpoint(
                    _entities.GetEntityId(ship.Id)
                        ?? throw new InvalidOperationException(
                            $"Live ship {ship.Id} has no entity mapping."),
                    ship.Id,
                    ship.PrincipalId,
                    ship.DesignId,
                    ship.CargoInventoryId))
                .OrderBy(ship => ship.EntityId.Value),
            _receipts.Values.Select(receipt =>
                new EntityMaterializationReceiptCheckpoint(
                    receipt.Effect,
                    receipt.EntityId,
                    receipt.ShipId,
                    receipt.CargoInventoryId)),
            _removalReceipts.Values.Select(receipt =>
                new EntityRemovalReceiptCheckpoint(
                    receipt.Request,
                    receipt.ShipId,
                    receipt.CargoInventoryId)));

    /// <summary>
    /// Validates and directly restores lifecycle identity, inventories,
    /// allocators, and receipts without registering setup, materializing, or
    /// removing an entity through gameplay APIs.
    /// </summary>
    internal static CheckpointResult<EntityLifecycleOwner> RestoreCheckpoint(
        EntityLifecycleCheckpoint checkpoint,
        SpatialMovement movement,
        ActorControlRegistry control,
        ShipOrderCoordinator orders,
        IEnumerable<ShipMaterializationPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(policies);
        CheckpointResult<IdSequence<EntityId>> entityIds =
            IdSequence<EntityId>.RestoreCheckpoint(checkpoint.EntityIds);
        if (!entityIds.IsSuccess)
        {
            return Rejected(
                "$.checkpoint.lifecycle.entityIds.nextValue",
                entityIds.Failure!.Message);
        }

        CheckpointResult<IdSequence<ShipId>> shipIds =
            IdSequence<ShipId>.RestoreCheckpoint(checkpoint.ShipIds);
        if (!shipIds.IsSuccess)
        {
            return Rejected(
                "$.checkpoint.lifecycle.shipIds.nextValue",
                shipIds.Failure!.Message);
        }

        CheckpointResult<IdSequence<InventoryId>> inventoryIds =
            IdSequence<InventoryId>.RestoreCheckpoint(checkpoint.InventoryIds);
        if (!inventoryIds.IsSuccess)
        {
            return Rejected(
                "$.checkpoint.lifecycle.inventoryIds.nextValue",
                inventoryIds.Failure!.Message);
        }

        CheckpointResult<InventoryRegistry> inventories =
            InventoryRegistry.RestoreCheckpoint(checkpoint.Inventories);
        if (!inventories.IsSuccess)
        {
            return CheckpointResult<EntityLifecycleOwner>.Rejected(
                inventories.Failure!);
        }

        // The lifecycle allocator also owns economy inventories, so validate
        // every restored inventory rather than only cargo referenced by ships.
        for (int index = 0; index < checkpoint.Inventories.Inventories.Count; index++)
        {
            InventoryCheckpoint inventory = checkpoint.Inventories.Inventories[index]!;
            if (!WasAllocated(inventory.Id.Value, checkpoint.InventoryIds))
            {
                return Rejected(
                    $"$.checkpoint.lifecycle.inventories[{index}].id",
                    "The inventory identifier was not allocated by the saved sequence.");
            }
        }

        var entities = new EntityRegistry();
        var ships = new GameSessionShipRegistry();
        var liveCargoIds = new HashSet<InventoryId>();
        CheckpointValidationFailure? liveFailure = RestoreLiveShips(
            checkpoint,
            entities,
            ships,
            inventories.Value!,
            liveCargoIds);
        if (liveFailure is not null)
        {
            return CheckpointResult<EntityLifecycleOwner>.Rejected(liveFailure);
        }

        var removalReceipts =
            new SortedDictionary<EntityId, EntityRemovalResult.Removed>(
                EntityIdComparer<EntityId>.Instance);
        CheckpointValidationFailure? removalFailure = RestoreRemovalReceipts(
            checkpoint,
            entities,
            inventories.Value!,
            removalReceipts);
        if (removalFailure is not null)
        {
            return CheckpointResult<EntityLifecycleOwner>.Rejected(removalFailure);
        }

        var receipts =
            new SortedDictionary<MaterializationKey, ConstructionEntityMaterializationResult.Materialized>();
        CheckpointValidationFailure? materializationFailure =
            RestoreMaterializationReceipts(
                checkpoint,
                entities,
                ships,
                removalReceipts,
                receipts);
        if (materializationFailure is not null)
        {
            return CheckpointResult<EntityLifecycleOwner>.Rejected(
                materializationFailure);
        }

        return CheckpointResult<EntityLifecycleOwner>.Success(
            new EntityLifecycleOwner(
                movement,
                control,
                orders,
                policies,
                entities,
                entityIds.Value!,
                inventoryIds.Value!,
                inventories.Value!,
                receipts,
                removalReceipts,
                ships,
                shipIds.Value!));
    }

    internal GameSessionShip GetRequiredShip(ShipId shipId) =>
        _ships.Get(shipId)
        ?? throw new InvalidOperationException($"Ship {shipId} has no live ship record.");

    internal Inventory GetRequiredCargo(ShipId shipId)
    {
        GameSessionShip ship = GetRequiredShip(shipId);
        return _inventories.Get(ship.CargoInventoryId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no cargo inventory {ship.CargoInventoryId}.");
    }

    /// <summary>
    /// Registers an economy-owned setup inventory and advances the shared
    /// inventory allocator so later materialization cannot reuse its identity.
    /// </summary>
    internal void RegisterEconomyInventory(Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (_inventories.Contains(inventory.Id))
        {
            throw new InvalidOperationException(
                $"Inventory {inventory.Id} is already registered.");
        }

        _inventoryIds.AdvancePast(inventory.Id);
        _inventories.Add(inventory);
    }

    internal void RegisterSetup(IReadOnlyList<InitialShipSetup> ships)
    {
        ArgumentNullException.ThrowIfNull(ships);
        PreparedSetupShip[] prepared = ships
            .Select(PrepareSetupShip)
            .ToArray();

        foreach (PreparedSetupShip ship in prepared)
        {
            _entityIds.AdvancePast(ship.EntityId);
            _shipIds.AdvancePast(ship.ShipId);
            _inventoryIds.AdvancePast(ship.CargoInventoryId);
        }

        foreach (PreparedSetupShip ship in prepared)
        {
            _inventories.Add(new Inventory(ship.CargoInventoryId, ship.CargoCapacity));
            _ships.ApplyAdd(new GameSessionShip(
                ship.ShipId,
                ship.PrincipalId,
                ship.DesignId,
                ship.CargoInventoryId));
            _movement.Add(ship.ShipId, ship.Position);
            _control.Add(ship.ShipId, ship.BaseController);
            _orders.Add(ship.ShipId);
            _entities.ApplyAddShip(ship.EntityId, ship.ShipId);
        }
    }

    /// <summary>
    /// Reconstructs live bidirectional mappings and ship records while proving
    /// that each cargo inventory and identity was restored exactly once.
    /// </summary>
    private static CheckpointValidationFailure? RestoreLiveShips(
        EntityLifecycleCheckpoint checkpoint,
        EntityRegistry entities,
        GameSessionShipRegistry ships,
        InventoryRegistry inventories,
        HashSet<InventoryId> liveCargoIds)
    {
        const string path = "$.checkpoint.lifecycle.liveShips";
        for (int index = 0; index < checkpoint.LiveShips.Count; index++)
        {
            EntityLifecycleShipCheckpoint? ship = checkpoint.LiveShips[index];
            if (ship is null)
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A live ship checkpoint is missing.");
            }

            if (!WasAllocated(ship.EntityId.Value, checkpoint.EntityIds))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].entityId",
                    "The live entity identifier was not allocated by the saved sequence.");
            }

            if (!WasAllocated(ship.ShipId.Value, checkpoint.ShipIds))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].shipId",
                    "The live ship identifier was not allocated by the saved sequence.");
            }

            if (!WasAllocated(ship.CargoInventoryId.Value, checkpoint.InventoryIds))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].cargoInventoryId",
                    "The cargo inventory identifier was not allocated by the saved sequence.");
            }

            if (ship.PrincipalId.Value == 0)
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].principalId",
                    "A live ship principal identifier must be nonzero.");
            }

            if (ship.DesignId.Value == 0)
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].designId",
                    "A live ship design identifier must be nonzero.");
            }

            if (!entities.CanAddShip(ship.EntityId, ship.ShipId))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].entityId",
                    "A live entity or ship identity is duplicated.");
            }

            if (!liveCargoIds.Add(ship.CargoInventoryId))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].cargoInventoryId",
                    "A cargo inventory cannot belong to more than one live ship.");
            }

            if (!inventories.Contains(ship.CargoInventoryId))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}].cargoInventoryId",
                    "A live ship cargo inventory is missing.");
            }

            ships.ApplyAdd(new GameSessionShip(
                ship.ShipId,
                ship.PrincipalId,
                ship.DesignId,
                ship.CargoInventoryId));
            entities.ApplyAddShip(ship.EntityId, ship.ShipId);
        }

        return null;
    }

    /// <summary>
    /// Restores removed-entity receipts only when their identities are absent
    /// from live registries and their discarded cargo is no longer retained.
    /// </summary>
    private static CheckpointValidationFailure? RestoreRemovalReceipts(
        EntityLifecycleCheckpoint checkpoint,
        EntityRegistry entities,
        InventoryRegistry inventories,
        SortedDictionary<EntityId, EntityRemovalResult.Removed> receipts)
    {
        var removedShipIds = new HashSet<ShipId>();
        var removedCargoIds = new HashSet<InventoryId>();
        const string path = "$.checkpoint.lifecycle.removalReceipts";
        for (int index = 0; index < checkpoint.RemovalReceipts.Count; index++)
        {
            EntityRemovalReceiptCheckpoint? receipt =
                checkpoint.RemovalReceipts[index];
            if (receipt is null || !IsValidRemovalRequest(receipt.Request))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "An entity removal receipt is missing or invalid.");
            }

            EntityRemovalRequest request = receipt.Request!;
            if (!WasAllocated(request.EntityId.Value, checkpoint.EntityIds)
                || !WasAllocated(receipt.ShipId.Value, checkpoint.ShipIds)
                || !WasAllocated(
                    receipt.CargoInventoryId.Value,
                    checkpoint.InventoryIds))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A removal receipt identity was not allocated by the saved sequences.");
            }

            if (entities.GetShipId(request.EntityId) is not null
                || entities.GetEntityId(receipt.ShipId) is not null
                || inventories.Contains(receipt.CargoInventoryId))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A removed entity, ship, or discarded cargo inventory is still live.");
            }

            if (!removedShipIds.Add(receipt.ShipId)
                || !removedCargoIds.Add(receipt.CargoInventoryId)
                || receipts.ContainsKey(request.EntityId))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A removal receipt identity is duplicated.");
            }

            receipts.Add(
                request.EntityId,
                new EntityRemovalResult.Removed(
                    request,
                    receipt.ShipId,
                    receipt.CargoInventoryId));
        }

        return null;
    }

    /// <summary>
    /// Restores durable construction materialization receipts and requires
    /// every identity to resolve to the same live or removed lifecycle record.
    /// </summary>
    private static CheckpointValidationFailure? RestoreMaterializationReceipts(
        EntityLifecycleCheckpoint checkpoint,
        EntityRegistry entities,
        GameSessionShipRegistry ships,
        SortedDictionary<EntityId, EntityRemovalResult.Removed> removalReceipts,
        SortedDictionary<MaterializationKey, ConstructionEntityMaterializationResult.Materialized>
            receipts)
    {
        var materializedEntityIds = new HashSet<EntityId>();
        var materializedShipIds = new HashSet<ShipId>();
        var materializedCargoIds = new HashSet<InventoryId>();
        const string path = "$.checkpoint.lifecycle.materializationReceipts";
        for (int index = 0; index < checkpoint.MaterializationReceipts.Count; index++)
        {
            EntityMaterializationReceiptCheckpoint? receipt =
                checkpoint.MaterializationReceipts[index];
            if (receipt is null || !IsValidMaterializationEffect(receipt.Effect))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A materialization receipt or its construction effect is invalid.");
            }

            ConstructionMaterializationEffect effect = receipt.Effect!;
            if (!WasAllocated(receipt.EntityId.Value, checkpoint.EntityIds)
                || !WasAllocated(receipt.ShipId.Value, checkpoint.ShipIds)
                || !WasAllocated(
                    receipt.CargoInventoryId.Value,
                    checkpoint.InventoryIds))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A materialization receipt identity was not allocated by the saved sequences.");
            }

            var key = new MaterializationKey(effect.FacilityId, effect.OrderId);
            if (receipts.ContainsKey(key)
                || !materializedEntityIds.Add(receipt.EntityId)
                || !materializedShipIds.Add(receipt.ShipId)
                || !materializedCargoIds.Add(receipt.CargoInventoryId))
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A materialization receipt key or identity is duplicated.");
            }

            ShipId? liveShipId = entities.GetShipId(receipt.EntityId);
            if (liveShipId is { } liveId)
            {
                GameSessionShip? liveShip = ships.Get(liveId);
                if (liveId != receipt.ShipId
                    || liveShip is null
                    || liveShip.CargoInventoryId != receipt.CargoInventoryId
                    || liveShip.DesignId != effect.DesignId)
                {
                    return new CheckpointValidationFailure(
                        $"{path}[{index}]",
                        "A materialization receipt disagrees with its live ship.");
                }
            }
            else if (!removalReceipts.TryGetValue(
                         receipt.EntityId,
                         out EntityRemovalResult.Removed? removed)
                     || removed.ShipId != receipt.ShipId
                     || removed.CargoInventoryId != receipt.CargoInventoryId)
            {
                return new CheckpointValidationFailure(
                    $"{path}[{index}]",
                    "A materialization receipt has no matching live or removed entity.");
            }

            receipts.Add(
                key,
                new ConstructionEntityMaterializationResult.Materialized(
                    effect,
                    receipt.EntityId,
                    receipt.ShipId,
                    receipt.CargoInventoryId));
        }

        return null;
    }

    /// <summary>
    /// Validates the exact completion identity required for a committed
    /// session-owned construction materialization.
    /// </summary>
    private static bool IsValidMaterializationEffect(
        ConstructionMaterializationEffect? effect) =>
        effect is not null
        && effect.FacilityId.Value != 0
        && effect.OrderId.Value != 0
        && effect.DesignId.Value != 0
        && effect.CompletionEventKey is { } key
        && key.Timestamp == effect.CompletedAt
        && key.Phase == EventPhase.PhysicalCompletion;

    /// <summary>
    /// Validates the stable request fields retained for idempotent removal.
    /// </summary>
    private static bool IsValidRemovalRequest(EntityRemovalRequest? request) =>
        request is not null
        && request.EntityId.Value != 0
        && Enum.IsDefined(request.Reason)
        && request.CargoDisposition == EntityCargoDisposition.DiscardCargo;

    /// <summary>
    /// Accepts a nonzero identity below the exact next allocator position, or
    /// any nonzero identity after exhaustion.
    /// </summary>
    private static bool WasAllocated(
        ulong value,
        IdSequenceCheckpoint sequence) =>
        value != 0
        && (sequence.NextValue is not { } next || value < next);

    private static CheckpointResult<EntityLifecycleOwner> Rejected(
        string path,
        string message) =>
        CheckpointResult<EntityLifecycleOwner>.Rejected(
            new CheckpointValidationFailure(path, message));

    private PreparedSetupShip PrepareSetupShip(InitialShipSetup ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        if (!_entities.CanAddShip(ship.EntityId, ship.Id)
            || _movement.Contains(ship.Id)
            || _control.Contains(ship.Id)
            || _orders.Contains(ship.Id)
            || _ships.Contains(ship.Id)
            || _inventories.Contains(ship.CargoInventoryId))
        {
            throw new InvalidOperationException(
                $"Entity {ship.EntityId} or ship {ship.Id} is already registered.");
        }

        return new PreparedSetupShip(
            ship.EntityId,
            ship.Id,
            ship.CargoInventoryId,
            ship.PrincipalId,
            ship.Design.Id,
            ship.Design.CargoCapacity,
            ship.Position,
            ship.BaseController);
    }

    /// <summary>
    /// Validates and atomically commits one durable construction effect, or
    /// resolves it to its prior receipt without applying a second entity.
    /// </summary>
    internal ConstructionMaterializationCommit MaterializeConstruction(
        ConstructionProcess source,
        ConstructionMaterializationEffect effect,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(effect);
        var key = new MaterializationKey(effect.FacilityId, effect.OrderId);
        if (_receipts.TryGetValue(
                key,
                out ConstructionEntityMaterializationResult.Materialized? receipt))
        {
            ConstructionEntityMaterializationResult repeated = receipt.Effect == effect
                ? receipt
                : new ConstructionEntityMaterializationResult.Deferred(
                    effect,
                    ConstructionMaterializationDeferredReason.MismatchedPendingMaterialization);
            return new ConstructionMaterializationCommit(repeated, WasApplied: false);
        }

        ConstructionMaterializationDeferredReason? rejection =
            ValidateMaterialization(source, effect, now, out ShipMaterializationPolicy? policy, out ShipDesign? design);
        if (rejection is { } reason)
        {
            return new ConstructionMaterializationCommit(
                new ConstructionEntityMaterializationResult.Deferred(effect, reason),
                WasApplied: false);
        }

        if (!_entityIds.TryPeek(out EntityId entityId)
            || !_shipIds.TryPeek(out ShipId shipId)
            || !_inventoryIds.TryPeek(out InventoryId inventoryId))
        {
            return new ConstructionMaterializationCommit(
                new ConstructionEntityMaterializationResult.Deferred(
                    effect,
                    ConstructionMaterializationDeferredReason.IdentifierCapacityExhausted),
                WasApplied: false);
        }

        if (!_entities.CanAddShip(entityId, shipId)
            || _movement.Contains(shipId)
            || _control.Contains(shipId)
            || _orders.Contains(shipId)
            || _ships.Contains(shipId)
            || _inventories.Contains(inventoryId))
        {
            return new ConstructionMaterializationCommit(
                new ConstructionEntityMaterializationResult.Deferred(
                    effect,
                    ConstructionMaterializationDeferredReason.OwnerConflict),
                WasApplied: false);
        }

        EntityId allocatedEntityId = _entityIds.Allocate();
        ShipId allocatedShipId = _shipIds.Allocate();
        InventoryId allocatedInventoryId = _inventoryIds.Allocate();
        if (allocatedEntityId != entityId
            || allocatedShipId != shipId
            || allocatedInventoryId != inventoryId)
        {
            throw new InvalidOperationException(
                "Prepared materialization identifiers changed before commit.");
        }

        _inventories.Add(new Inventory(inventoryId, design!.CargoCapacity));
        _ships.ApplyAdd(new GameSessionShip(
            shipId,
            policy!.PrincipalId,
            design.Id,
            inventoryId));
        _movement.Add(shipId, policy.Position);
        _control.Add(shipId, policy.BaseController);
        _orders.Add(shipId);
        _entities.ApplyAddShip(entityId, shipId);

        var identity = new ConstructionMaterializationIdentity.Ship(
            entityId,
            shipId,
            inventoryId);
        ConstructionMaterializationAcknowledgement acknowledgement =
            source.AcknowledgeMaterialization(effect, identity);
        if (acknowledgement != ConstructionMaterializationAcknowledgement.Applied)
        {
            throw new InvalidOperationException(
                $"Construction materialization acknowledgement returned {acknowledgement} after entity commit.");
        }

        var result = new ConstructionEntityMaterializationResult.Materialized(
            effect,
            entityId,
            shipId,
            inventoryId);
        _receipts.Add(key, result);
        return new ConstructionMaterializationCommit(result, WasApplied: true);
    }

    internal EntityRemovalPreparation PrepareRemoval(
        EntityRemovalRequest request,
        bool permitOwnerReleasedCommitments)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_removalReceipts.TryGetValue(request.EntityId, out EntityRemovalResult.Removed? receipt))
        {
            return receipt.Request == request
                ? new EntityRemovalPreparation.Resolved(receipt)
                : new EntityRemovalPreparation.Resolved(
                    new EntityRemovalResult.Rejected(
                        request,
                        EntityRemovalRejectionReason.MissingEntity));
        }

        ShipId? resolvedShipId = _entities.GetShipId(request.EntityId);
        if (resolvedShipId is not { } shipId)
        {
            return new EntityRemovalPreparation.Resolved(
                new EntityRemovalResult.Rejected(
                    request,
                    EntityRemovalRejectionReason.MissingEntity));
        }

        GameSessionShip? ship = _ships.Get(shipId);
        Inventory? cargo = ship is null
            ? null
            : _inventories.Get(ship.CargoInventoryId);
        if (ship is null
            || cargo is null
            || !_movement.Contains(shipId)
            || !_control.Contains(shipId)
            || !_orders.Contains(shipId)
            || _entities.GetEntityId(shipId) != request.EntityId)
        {
            return new EntityRemovalPreparation.Resolved(
                new EntityRemovalResult.Rejected(
                    request,
                    EntityRemovalRejectionReason.OwnerConflict));
        }

        if (cargo.HasCommitments && !permitOwnerReleasedCommitments)
        {
            return new EntityRemovalPreparation.Resolved(
                new EntityRemovalResult.Rejected(
                    request,
                    EntityRemovalRejectionReason.CargoHasCommitments));
        }

        return new EntityRemovalPreparation.Prepared(
            new PreparedEntityRemoval(
                request,
                shipId,
                ship.CargoInventoryId,
                _orders.PrepareTargetRemoval(request.EntityId, shipId)));
    }

    internal EntityRemovalResult ApplyRemoval(
        PreparedEntityRemoval removal,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(removal);
        Inventory cargo = _inventories.Get(removal.CargoInventoryId)
            ?? throw new InvalidOperationException(
                $"Prepared entity removal {removal.Request.EntityId} lost cargo inventory {removal.CargoInventoryId}.");
        if (cargo.HasCommitments)
        {
            throw new InvalidOperationException(
                $"Prepared entity removal {removal.Request.EntityId} still has cargo commitments.");
        }

        if (!_movement.CommitRemove(removal.ShipId, now)
            || !_orders.Remove(removal.ShipId)
            || !_control.Remove(removal.ShipId)
            || !_inventories.ApplyRemove(removal.CargoInventoryId)
            || !_ships.ApplyRemove(removal.ShipId)
            || !_entities.ApplyRemoveShip(removal.Request.EntityId, removal.ShipId))
        {
            throw new InvalidOperationException(
                $"Prepared entity removal {removal.Request.EntityId} failed during apply; the session is no longer valid.");
        }

        var result = new EntityRemovalResult.Removed(
            removal.Request,
            removal.ShipId,
            removal.CargoInventoryId);
        _removalReceipts.Add(removal.Request.EntityId, result);
        return result;
    }

    private ConstructionMaterializationDeferredReason? ValidateMaterialization(
        ConstructionProcess source,
        ConstructionMaterializationEffect effect,
        SimulationTime now,
        out ShipMaterializationPolicy? policy,
        out ShipDesign? design)
    {
        policy = null;
        design = null;
        if (source.FacilityId != effect.FacilityId)
        {
            return ConstructionMaterializationDeferredReason.SourceFacilityMismatch;
        }

        ConstructionMaterializationEffect? pending =
            source.GetPendingMaterialization(effect.OrderId);
        if (pending is null)
        {
            return ConstructionMaterializationDeferredReason.MissingPendingMaterialization;
        }

        if (pending != effect)
        {
            return ConstructionMaterializationDeferredReason.MismatchedPendingMaterialization;
        }

        if (effect.CompletedAt > now)
        {
            return ConstructionMaterializationDeferredReason.CompletionInFuture;
        }

        if (!_policies.TryGetValue(effect.FacilityId, out policy))
        {
            return ConstructionMaterializationDeferredReason.MissingPolicy;
        }

        design = policy.GetDesign(effect.DesignId);
        if (design is null)
        {
            return ConstructionMaterializationDeferredReason.DesignNotAllowed;
        }

        ConstructionOrder? order = source.GetOrder(effect.OrderId);
        return order is not null && ReferenceEquals(order.Design, design)
            ? null
            : ConstructionMaterializationDeferredReason.DesignMismatch;
    }

    private sealed record PreparedSetupShip(
        EntityId EntityId,
        ShipId ShipId,
        InventoryId CargoInventoryId,
        PrincipalId PrincipalId,
        ConstructionDesignId DesignId,
        Quantity CargoCapacity,
        SystemPosition Position,
        ActorController BaseController);

    private readonly record struct MaterializationKey(
        FacilityId FacilityId,
        ConstructionOrderId OrderId) : IComparable<MaterializationKey>
    {
        public int CompareTo(MaterializationKey other)
        {
            int facilityComparison = FacilityId.Value.CompareTo(other.FacilityId.Value);
            return facilityComparison != 0
                ? facilityComparison
                : OrderId.Value.CompareTo(other.OrderId.Value);
        }
    }
}
