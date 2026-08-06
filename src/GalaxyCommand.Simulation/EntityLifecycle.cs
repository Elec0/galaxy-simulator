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

    public ShipMaterializationPolicy(
        FacilityId facilityId,
        OrganizationId organizationId,
        SystemPosition position,
        ActorController baseController,
        InitialShipOrderPolicy initialOrderPolicy,
        IEnumerable<ShipDesign> allowedDesigns)
    {
        ArgumentOutOfRangeException.ThrowIfZero(facilityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(organizationId.Value);
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
        OrganizationId = organizationId;
        Position = position;
        BaseController = baseController;
        InitialOrderPolicy = initialOrderPolicy;
        _designs = new ReadOnlyDictionary<ConstructionDesignId, ShipDesign>(designs);
    }

    public FacilityId FacilityId { get; }

    public OrganizationId OrganizationId { get; }

    public SystemPosition Position { get; }

    public ActorController BaseController { get; }

    public InitialShipOrderPolicy InitialOrderPolicy { get; }

    public IReadOnlyDictionary<ConstructionDesignId, ShipDesign> AllowedDesigns => _designs;

    public ShipDesign? GetDesign(ConstructionDesignId designId) =>
        _designs.GetValueOrDefault(designId);
}

public sealed record GameSessionShip(
    ShipId Id,
    OrganizationId OrganizationId,
    ConstructionDesignId DesignId,
    InventoryId CargoInventoryId);

public enum ConstructionMaterializationDeferredReason
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

public abstract record ConstructionEntityMaterializationResult
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

internal sealed class GameSessionShipRegistry
{
    private readonly SortedDictionary<ShipId, GameSessionShip> _ships =
        new(EntityIdComparer<ShipId>.Instance);

    internal bool Contains(ShipId shipId) => _ships.ContainsKey(shipId);

    internal GameSessionShip? Get(ShipId shipId) =>
        _ships.GetValueOrDefault(shipId);

    internal void ApplyAdd(GameSessionShip ship) =>
        _ships.Add(ship.Id, ship);
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

    internal EntityRegistry Entities => _entities;

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
                ship.OrganizationId,
                ship.DesignId,
                ship.CargoInventoryId));
            _movement.Add(ship.ShipId, ship.Position);
            _control.Add(ship.ShipId, ship.BaseController);
            _orders.Add(ship.ShipId);
            _entities.ApplyAddShip(ship.EntityId, ship.ShipId);
        }
    }

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
            ship.OrganizationId,
            ship.Design.Id,
            ship.Design.CargoCapacity,
            ship.Position,
            ship.BaseController);
    }

    internal ConstructionEntityMaterializationResult MaterializeConstruction(
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
            return receipt.Effect == effect
                ? receipt
                : new ConstructionEntityMaterializationResult.Deferred(
                    effect,
                    ConstructionMaterializationDeferredReason.MismatchedPendingMaterialization);
        }

        ConstructionMaterializationDeferredReason? rejection =
            ValidateMaterialization(source, effect, now, out ShipMaterializationPolicy? policy, out ShipDesign? design);
        if (rejection is { } reason)
        {
            return new ConstructionEntityMaterializationResult.Deferred(effect, reason);
        }

        if (!_entityIds.TryPeek(out EntityId entityId)
            || !_shipIds.TryPeek(out ShipId shipId)
            || !_inventoryIds.TryPeek(out InventoryId inventoryId))
        {
            return new ConstructionEntityMaterializationResult.Deferred(
                effect,
                ConstructionMaterializationDeferredReason.IdentifierCapacityExhausted);
        }

        if (!_entities.CanAddShip(entityId, shipId)
            || _movement.Contains(shipId)
            || _control.Contains(shipId)
            || _orders.Contains(shipId)
            || _ships.Contains(shipId)
            || _inventories.Contains(inventoryId))
        {
            return new ConstructionEntityMaterializationResult.Deferred(
                effect,
                ConstructionMaterializationDeferredReason.OwnerConflict);
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
            policy!.OrganizationId,
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
        return result;
    }

    internal IReadOnlyList<ConstructionEntityMaterializationResult>
        MaterializePendingConstruction(
            IEnumerable<ConstructionProcess> sources,
            SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var orderedSources = new SortedDictionary<FacilityId, ConstructionProcess>(
            EntityIdComparer<FacilityId>.Instance);
        foreach (ConstructionProcess source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (orderedSources.TryGetValue(
                    source.FacilityId,
                    out ConstructionProcess? existing))
            {
                if (!ReferenceEquals(existing, source))
                {
                    throw new ArgumentException(
                        $"Multiple construction processes claim facility {source.FacilityId}.",
                        nameof(sources));
                }

                continue;
            }

            orderedSources.Add(source.FacilityId, source);
        }

        var results = new List<ConstructionEntityMaterializationResult>();
        foreach (ConstructionProcess source in orderedSources.Values)
        {
            foreach (ConstructionMaterializationEffect effect in
                     source.PendingMaterializations)
            {
                results.Add(MaterializeConstruction(source, effect, now));
            }
        }

        return results.AsReadOnly();
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
        OrganizationId OrganizationId,
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
