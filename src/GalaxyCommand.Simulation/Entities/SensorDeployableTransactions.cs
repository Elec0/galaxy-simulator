using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Authoritative stationary sensor state; inventory custody and private action intent are excluded.</summary>
public sealed record DeployedSensorSnapshot(EntityId EntityId, QualifiedContentKey DefinitionKey,
    PrincipalId PrincipalId, SystemPosition Position);

internal enum SensorActionFailure
{
    MissingActor,
    ItemUnavailable,
    ItemReserved,
    TargetRemoved,
    DefinitionUnavailable,
    Unauthorized,
    OutOfRange,
    InsufficientCapacity,
    IdentifierExhausted,
    CommandIdentityConflict,
    InvalidSpatialState,
}

internal abstract record SensorActionRequest(CommandSequence Command, ShipId Ship)
{
    internal sealed record Deploy(CommandSequence Command, ShipId Ship, ItemInstanceId Item, SystemPosition Position)
        : SensorActionRequest(Command, Ship);
    internal sealed record Pickup(CommandSequence Command, ShipId Ship, EntityId Target)
        : SensorActionRequest(Command, Ship);
}

internal sealed record SensorActionResult(bool WasApplied, SensorActionFailure? Failure = null,
    DeployedSensorSnapshot? Deployed = null, DiscreteItemInstance? Item = null)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record SensorActionReceipt(SensorActionRequest Request, SimulationTime ExecutedAt, SensorActionResult Result);
internal sealed record SensorRemovalReceipt(EntityRemovalResult.SensorRemoved Result, SimulationTime RemovedAt);

internal sealed partial class EntityLifecycleOwner
{
    private readonly Dictionary<QualifiedContentKey, SensorDeployableDefinition> _sensorDefinitions;
    private readonly Dictionary<ConstructionDesignId, ShipDesign> _operationalShipDefinitions;
    private readonly SortedDictionary<EntityId, DeployedSensorSnapshot> _deployedSensors = new(EntityIdComparer<EntityId>.Instance);
    private readonly Dictionary<CommandSequence, SensorActionReceipt> _sensorActionReceipts = [];
    private readonly Dictionary<EntityId, SensorRemovalReceipt> _sensorRemovalReceipts = [];

    internal bool IsSensorRemovalReference(EntityId id) => _deployedSensors.ContainsKey(id) || _sensorRemovalReceipts.ContainsKey(id);

    /// <summary>Withdraws a real stationary entity with an idempotent removal receipt and no ship/cargo cleanup.</summary>
    internal EntityRemovalResult RemoveSensor(EntityRemovalRequest request, SimulationTime now)
    {
        if (_sensorRemovalReceipts.TryGetValue(request.EntityId, out SensorRemovalReceipt? prior))
            return prior.Result.Request == request ? prior.Result
                : new EntityRemovalResult.Rejected(request, EntityRemovalRejectionReason.MissingEntity);
        if (!_deployedSensors.Remove(request.EntityId, out DeployedSensorSnapshot? sensor))
            return new EntityRemovalResult.Rejected(request, EntityRemovalRejectionReason.MissingEntity);
        if (!_entities.ApplyRemoveSensorDeployable(request.EntityId))
            throw new InvalidOperationException("Removed sensor has no live entity registration.");
        var result = new EntityRemovalResult.SensorRemoved(request, sensor);
        _sensorRemovalReceipts.Add(request.EntityId, new SensorRemovalReceipt(result, now));
        return result;
    }

    internal IReadOnlyList<DeployedSensorSnapshot> CaptureDeployedSensors() => Array.AsReadOnly(_deployedSensors.Values.ToArray());
    internal DeployedSensorSnapshot? GetDeployedSensor(EntityId id) => _deployedSensors.GetValueOrDefault(id);

    internal SensorDeployableDefinition? GetSensorDefinition(QualifiedContentKey key) => _sensorDefinitions.GetValueOrDefault(key);
    internal ShipDesign? GetOperationalShipDefinition(ConstructionDesignId id) => _operationalShipDefinitions.GetValueOrDefault(id);

    /// <summary>Copies the committed identity-ordered roster and exact retained source capabilities at one boundary.</summary>
    internal SensorDeployableObservationSnapshot CaptureSensorObservation(SimulationTime now) => new(now,
        Array.AsReadOnly(_deployedSensors.Values.Select(sensor => new SensorDeployableViewSnapshot(sensor.EntityId,
            sensor.DefinitionKey, sensor.PrincipalId, sensor.Position, now)).ToArray()),
        Array.AsReadOnly(_deployedSensors.Values.Select(sensor => new DeployedSensorSourceSnapshot(sensor.EntityId,
            sensor.PrincipalId, sensor.Position, _sensorDefinitions[sensor.DefinitionKey].SensorRadiusUnits)).ToArray()));

    /// <summary>
    /// Revalidates cargo, custody, exact range and identifier capacity before consuming an
    /// item and publishing its fresh entity. The caller owns controller/order authorization.
    /// A matching successful command replay returns its receipt without changing current state.
    /// </summary>
    internal SensorActionResult DeploySensor(CommandSequence command, ShipId shipId, ItemInstanceId itemId,
        SystemPosition target, InventoryCommitOwner inventoryCommit, SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(inventoryCommit);
        ArgumentOutOfRangeException.ThrowIfZero(command.Value);
        var request = new SensorActionRequest.Deploy(command, shipId, itemId, target);
        if (Replay(request) is { } replay) return replay;
        if (_ships.Get(shipId) is not { } ship) return Failed(SensorActionFailure.MissingActor);
        Inventory cargo = GetRequiredCargo(shipId);
        if (cargo.Custody?.ControllingPrincipalId != ship.PrincipalId) return Failed(SensorActionFailure.Unauthorized);
        if (cargo.GetDiscrete(itemId) is not { } item) return Failed(SensorActionFailure.ItemUnavailable);
        if (!_sensorDefinitions.TryGetValue(item.DefinitionKey, out SensorDeployableDefinition? definition))
            return Failed(SensorActionFailure.DefinitionUnavailable);
        if (cargo.IsDiscreteReserved(itemId)) return Failed(SensorActionFailure.ItemReserved);
        if (!InSensorActionRange(shipId, target, definition.DeploymentRangeUnits, now)) return Failed(SensorActionFailure.OutOfRange);
        if (!_entityIds.TryPeek(out EntityId entityId)) return Failed(SensorActionFailure.IdentifierExhausted);
        if (!_entities.CanAddSensorDeployable(entityId)) throw new InvalidOperationException("Prepared sensor identity is already live.");

        var sensor = new DeployedSensorSnapshot(entityId, definition.Key, ship.PrincipalId, target);
        // No expected rejection remains after this point. All three owners publish in one
        // deterministic commit; unexpected failure must invalidate the enclosing session.
        if (_entityIds.Allocate() != entityId) throw new InvalidOperationException("Prepared sensor identity changed before commit.");
        cargo.ApplyRemoveDiscrete(itemId, definition.Physical.CapacityCost);
        _entities.ApplyAddSensorDeployable(entityId);
        _deployedSensors.Add(entityId, sensor);
        var result = new SensorActionResult(true, Deployed: sensor);
        _sensorActionReceipts.Add(command, new SensorActionReceipt(request, now, result));
        return result;
    }

    /// <summary>
    /// Revalidates live ownership, exact range and uncommitted cargo capacity before creating
    /// a fresh equivalent item and withdrawing the stationary entity. No capacity is reserved.
    /// </summary>
    internal SensorActionResult PickupSensor(CommandSequence command, ShipId shipId, EntityId targetId,
        InventoryCommitOwner inventoryCommit, SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(inventoryCommit);
        ArgumentOutOfRangeException.ThrowIfZero(command.Value);
        var request = new SensorActionRequest.Pickup(command, shipId, targetId);
        if (Replay(request) is { } replay) return replay;
        if (_ships.Get(shipId) is not { } ship) return Failed(SensorActionFailure.MissingActor);
        if (!_deployedSensors.TryGetValue(targetId, out DeployedSensorSnapshot? sensor)) return Failed(SensorActionFailure.TargetRemoved);
        if (!_sensorDefinitions.TryGetValue(sensor.DefinitionKey, out SensorDeployableDefinition? definition)
            || !_operationalShipDefinitions.TryGetValue(ship.DesignId, out ShipDesign? shipDefinition))
            return Failed(SensorActionFailure.DefinitionUnavailable);
        Inventory cargo = GetRequiredCargo(shipId);
        if (sensor.PrincipalId != ship.PrincipalId || cargo.Custody?.ControllingPrincipalId != ship.PrincipalId)
            return Failed(SensorActionFailure.Unauthorized);
        if (!InSensorActionRange(shipId, sensor.Position, shipDefinition.PickupRangeUnits, now)) return Failed(SensorActionFailure.OutOfRange);
        if (!inventoryCommit.TryPeekItemIdentity(out ItemInstanceId itemId)) return Failed(SensorActionFailure.IdentifierExhausted);
        if (!cargo.CanCreateDiscrete(definition.Physical, itemId)) return Failed(SensorActionFailure.InsufficientCapacity);
        if (_entities.GetKind(targetId) != EntityKind.SensorDeployable)
            throw new InvalidOperationException("Live sensor has no matching entity registration.");

        DiscreteItemInstance item = inventoryCommit.ApplyPreparedDiscreteCreation(cargo, definition.Physical, itemId);
        if (!_entities.ApplyRemoveSensorDeployable(targetId) || !_deployedSensors.Remove(targetId))
            throw new InvalidOperationException("Prepared sensor pickup failed during commit.");
        var result = new SensorActionResult(true, Deployed: sensor, Item: item);
        _sensorActionReceipts.Add(command, new SensorActionReceipt(request, now, result));
        return result;
    }

    /// <summary>Checks an exact committed trajectory point rather than a rounded published ship coordinate.</summary>
    private bool InSensorActionRange(ShipId ship, SystemPosition target, ulong radius, SimulationTime now)
    {
        InteractionSpatialReadView view = _movement.CaptureInteractionReadView(now, [ship]);
        PreparedInteractionPoint? source = InteractionProximityQuery.Prepare(view).Get(ship);
        PreparedInteractionPoint destination = PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(target, now, now), now);
        return source is not null && source.InRange(destination, radius);
    }

    /// <summary>Never reexecutes an earlier success or lets a command identity acquire different action meaning.</summary>
    private SensorActionResult? Replay(SensorActionRequest request) =>
        _sensorActionReceipts.TryGetValue(request.Command, out SensorActionReceipt? prior)
            ? prior.Request == request ? prior.Result with { WasApplied = false } : Failed(SensorActionFailure.CommandIdentityConflict)
            : null;

    private static SensorActionResult Failed(SensorActionFailure reason) => new(false, reason);
}
