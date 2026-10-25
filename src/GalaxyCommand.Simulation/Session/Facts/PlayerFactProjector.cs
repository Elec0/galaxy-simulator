namespace GalaxyCommand.Simulation;

/// <summary>Read-only evaluation over one completed observation boundary.</summary>
internal sealed class PlayerFactProjector(CommandSource playerCommandSource, GameSnapshot world,
    Dictionary<ShipId, GameShipSnapshot> ships, PlayerCoverageBoundary coverage)
{
    /// <summary>Applies the approved explicit policy for every currently supported fact kind.</summary>
    internal GameFact? Project(GameFact fact)
    {
        PrincipalId observer = world.Relationships.PlayerPrincipalId;
        PrincipalId? subjectPrincipal = fact is SensorDeployedFact deployed ? deployed.Sensor.PrincipalId
            : fact is SensorPickedUpFact pickedUp ? pickedUp.Sensor.PrincipalId
            : fact is SensorRemovedFact sensorRemoval ? sensorRemoval.Sensor.PrincipalId
            : fact is EntityMaterializedFact materialized ? materialized.PrincipalId
            : GamePresentationSnapshotFactory.GetShipId(fact) is { } subject
                ? ships.GetValueOrDefault(subject)?.PrincipalId : null;
        if (TryProjectWithoutCoverage(fact, observer, playerCommandSource, subjectPrincipal, out GameFact? direct)) return direct;
        GameFact? Spatial(ShipId ship, SystemPosition? position, ObservedShipChangeKind kind) =>
            position is { } anchor && coverage.Contains(anchor)
                ? new ObservedShipChangeFact(ship, kind, anchor) : null;
        SystemPosition? Position(ShipId ship) => ships.GetValueOrDefault(ship)?.Position;

        return fact switch
        {
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
        or SensorDeployedFact or SensorPickedUpFact or SensorRemovedFact;
}
