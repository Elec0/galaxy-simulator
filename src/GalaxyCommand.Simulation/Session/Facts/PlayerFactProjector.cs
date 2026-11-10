namespace GalaxyCommand.Simulation;

/// <summary>Read-only evaluation over one completed observation boundary.</summary>
internal sealed class PlayerFactProjector(CommandSource playerCommandSource, GameSnapshot world,
    Dictionary<ShipId, GameShipSnapshot> ships, PlayerCoverageBoundary coverage)
{
    private readonly Dictionary<StationId, StationSnapshot> _stations = world.Stations.ToDictionary(station => station.Id);

    /// <summary>Applies the approved explicit policy for every currently supported fact kind.</summary>
    internal GameFact? Project(GameFact fact)
    {
        PrincipalId observer = world.Relationships.PlayerPrincipalId;
        PrincipalId? subjectPrincipal = fact is SensorDeployedFact deployed ? deployed.Sensor.PrincipalId
            : fact is SensorPickedUpFact pickedUp ? pickedUp.Sensor.PrincipalId
            : fact is SensorRemovedFact sensorRemoval ? sensorRemoval.Sensor.PrincipalId
            : fact is EntityMaterializedFact materialized ? materialized.PrincipalId
            : fact is StationRemovedFact removedStation ? removedStation.Station.PrincipalId
            : fact is StationCommitmentInvalidatedFact invalidated ? invalidated.PrincipalId
            : GetStationId(fact) is { } station ? _stations.GetValueOrDefault(station)?.PrincipalId
            : GamePresentationSnapshotFactory.GetShipId(fact) is { } subject
                ? ships.GetValueOrDefault(subject)?.PrincipalId : null;
        if (TryProjectWithoutCoverage(fact, observer, playerCommandSource, subjectPrincipal, out GameFact? direct)) return direct;
        GameFact? Spatial(ShipId ship, SystemPosition? position, ObservedShipChangeKind kind) =>
            position is { } anchor && coverage.Contains(anchor)
                ? new ObservedShipChangeFact(ship, kind, anchor) : null;
        SystemPosition? Position(ShipId ship) => ships.GetValueOrDefault(ship)?.Position;
        GameFact? StationChange(StationId id, ObservedStationChangeKind kind) =>
            _stations.GetValueOrDefault(id) is { } station && coverage.Contains(station.Position)
                ? new ObservedStationChangeFact(id, station.EntityId, station.DefinitionKey,
                    station.PrincipalId, station.Position, kind) : null;

        return fact switch
        {
            StationCreatedFact created => coverage.Contains(created.Position)
                ? new ObservedStationChangeFact(created.StationId, created.EntityId, created.DefinitionKey,
                    created.PrincipalId, created.Position, ObservedStationChangeKind.Appeared) : null,
            StationProjectCompletedFact completed when _stations.GetValueOrDefault(completed.StationId)?.Projects
                .Any(project => project.Id == completed.ProjectId && project.Kind == StationProjectKind.BaseConstruction) == true
                => StationChange(completed.StationId, ObservedStationChangeKind.Changed),
            StationModuleChangedFact changed => StationChange(changed.StationId, ObservedStationChangeKind.Changed),
            StationModuleRemovedFact removed => StationChange(removed.StationId, ObservedStationChangeKind.Changed),
            StationRemovedFact removed => coverage.Contains(removed.Station.Position)
                ? new ObservedStationChangeFact(removed.Station.Id, removed.Station.EntityId,
                    removed.Station.DefinitionKey, removed.Station.PrincipalId, removed.Station.Position,
                    ObservedStationChangeKind.Disappeared) : null,
            CargoObjectAppearedFact appearance => coverage.Contains(appearance.Object.Position)
                ? new ObservedCargoAppearanceFact(appearance.Object) : null,
            CargoObjectDisappearedFact disappearance => coverage.Contains(disappearance.Position)
                ? new ObservedCargoDisappearanceFact(disappearance.EntityId, disappearance.Position) : null,
            SensorDeployedFact appearance => coverage.Contains(appearance.Sensor.Position)
                ? new ObservedSensorChangeFact(appearance.Sensor.EntityId, appearance.Sensor.DefinitionKey,
                    appearance.Sensor.PrincipalId, appearance.Sensor.Position, ObservedSensorChangeKind.Appeared) : null,
            SensorPickedUpFact disappearance => coverage.Contains(disappearance.Sensor.Position)
                ? new ObservedSensorChangeFact(disappearance.Sensor.EntityId, disappearance.Sensor.DefinitionKey,
                    disappearance.Sensor.PrincipalId, disappearance.Sensor.Position, ObservedSensorChangeKind.Disappeared) : null,
            SensorRemovedFact disappearance => coverage.Contains(disappearance.Sensor.Position)
                ? new ObservedSensorChangeFact(disappearance.Sensor.EntityId, disappearance.Sensor.DefinitionKey,
                    disappearance.Sensor.PrincipalId, disappearance.Sensor.Position, ObservedSensorChangeKind.Disappeared) : null,
            ShipLocalMotionStartedFact started => Spatial(started.ShipId, started.Motion.Origin, ObservedShipChangeKind.MotionChanged),
            ShipLocalMotionEndedFact ended => Spatial(ended.ShipId, ended.FinalPosition, ObservedShipChangeKind.MotionChanged),
            ShipCruiseEnteredFact entered => Spatial(entered.ShipId, entered.Position, ObservedShipChangeKind.MotionChanged),
            ShipCruiseDroppedOutFact dropped => Spatial(dropped.ShipId, dropped.Position, ObservedShipChangeKind.MotionChanged),
            ShipConnectorTransitStartedFact departed => Spatial(departed.ShipId, departed.Transit.Source, ObservedShipChangeKind.Departed),
            ShipConnectorTransitCompletedFact arrived => Spatial(arrived.ShipId, arrived.Transit.Destination, ObservedShipChangeKind.Arrived),
            EntityMaterializedFact appeared => appeared.PrincipalId == observer ? fact
                : coverage.Contains(appeared.InitialPosition)
                    ? new ObservedShipChangeFact(appeared.ShipId, ObservedShipChangeKind.Appeared,
                        appeared.InitialPosition, appeared.EntityId, appeared.PrincipalId, appeared.DesignId) : null,
            EntityRemovedFact removed => Spatial(removed.ShipId, Position(removed.ShipId), ObservedShipChangeKind.Disappeared),
            RelationshipGrantRevokedFact revoked => world.Relationships.Grants.Any(grant =>
                grant.Id == revoked.Id && grant.HolderPrincipalId == observer) ? fact : null,
            // Future domains must opt in with an explicit safe payload policy.
            _ => null,
        };
    }

    /// <summary>
    /// Evaluates policies whose complete input is attribution and subject ownership.
    /// Owned traffic and private order changes need no galaxy snapshot or sensor
    /// geometry. False requires the caller's stable spatial/relationship view.
    /// </summary>
    internal static bool TryProjectWithoutCoverage(GameFact fact, PrincipalId observer,
        CommandSource playerSource, PrincipalId? subjectPrincipal, out GameFact? result)
    {
        if (subjectPrincipal == observer && IsOwnedAssetFact(fact))
        {
            result = fact;
            return true;
        }
        switch (fact)
        {
            case CargoCollectedFact collected:
                // Receiving cargo is the owned ship's evidence. Target visibility,
                // remaining contents and retirement do not authorize a world notice.
                result = subjectPrincipal == observer ? new PlayerCargoCollectedFact(collected.ShipId,
                    collected.OrderId, collected.Outcome.DefinitionKey, collected.Outcome.Quantity, collected.Outcome.InstanceId) : null;
                return true;
            case CommandAcceptedFact accepted:
                result = accepted.Source == playerSource ? new PlayerCommandOutcomeFact(accepted.CommandSequence,
                    accepted.CommandKind, CommandResultStatus.Accepted, null) : null;
                return true;
            case CommandRejectedFact rejected:
                result = rejected.Source == playerSource ? new PlayerCommandOutcomeFact(rejected.CommandSequence,
                    rejected.CommandKind, CommandResultStatus.Rejected, rejected.RejectionCode) : null;
                return true;
            case ShipOrderTransitionFact or ShipWaypointArrivedFact:
                result = null;
                return true;
            case StandingChangedFact standing:
                result = standing.SubjectPrincipalId == observer ? fact : null;
                return true;
            case DiplomaticConditionChangedFact:
                result = fact;
                return true;
            case RelationshipGrantIssuedFact grant:
                result = grant.HolderPrincipalId == observer ? fact : null;
                return true;
            default:
                result = null;
                return false;
        }
    }

    /// <summary>Closed owned-asset payload vocabulary; adding a subject identity alone never grants disclosure.</summary>
    internal static bool IsOwnedAssetFact(GameFact fact) => fact is
        ShipOrderTransitionFact or ShipWaypointArrivedFact or ShipLocalMotionStartedFact or ShipLocalMotionEndedFact
        or ShipCruiseEnteredFact or ShipCruiseDroppedOutFact or ShipConnectorTransitStartedFact
        or ShipConnectorTransitCompletedFact or EntityMaterializedFact or EntityRemovedFact
        or SensorDeployedFact or SensorPickedUpFact or SensorRemovedFact
        or StationCreatedFact or StationProjectChangedFact or StationProjectCompletedFact or StationModuleChangedFact or StationModuleRemovedFact
        or StationRemovedFact or StationCommitmentInvalidatedFact or StationShipConstructionQueuedFact;

    internal static StationId? GetStationId(GameFact fact) => fact switch
    {
        StationCreatedFact created => created.StationId,
        StationProjectChangedFact changed => changed.StationId,
        StationProjectCompletedFact completed => completed.StationId,
        StationModuleChangedFact changed => changed.StationId,
        StationShipConstructionQueuedFact queued => queued.StationId,
        StationModuleRemovedFact removed => removed.StationId,
        StationRemovedFact removed => removed.Station.Id,
        StationCommitmentInvalidatedFact invalidated => invalidated.StationId,
        _ => null,
    };
}
