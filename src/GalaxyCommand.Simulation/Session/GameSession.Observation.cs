namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    private readonly CommandSource _playerCommandSource;
    private readonly Dictionary<ShipId, GameShipSnapshot> _removedObservationShips = [];

    /// <summary>
    /// Completes disclosure after a direct operation, or lets the engine's
    /// timestamp barrier join all same-time events before choosing visibility.
    /// </summary>
    private void CompletePlayerDisclosureIfIdle()
    {
        if (_runtime.IsAtCompletedBoundary) CompletePlayerDisclosure();
    }

    /// <summary>
    /// Freezes eligibility and safe payloads using one completed boundary view.
    /// Reads never recompute history from later coverage or relationship state.
    /// Ownership-only policies read only pending subjects; spatial inputs are
    /// prepared once when needed. All receipt proposals precede publication.
    /// </summary>
    private void CompletePlayerDisclosure()
    {
        if (!_facts.HasPendingPlayerDisclosure)
        {
            _removedObservationShips.Clear();
            return;
        }
        PrincipalId observer = _runtime.PlayerObserverPrincipalId;
        IReadOnlyList<GameFactEnvelope> pending = _facts.CapturePendingPlayerDisclosure();
        var principals = new Dictionary<ShipId, PrincipalId?>();
        foreach (GameFactEnvelope envelope in pending)
        {
            if (GamePresentationSnapshotFactory.GetShipId(envelope.Fact) is not { } subject || principals.ContainsKey(subject)) continue;
            principals.Add(subject, _removedObservationShips.GetValueOrDefault(subject)?.PrincipalId
                ?? _runtime.CaptureSubjectPrincipal(subject)
                ?? (envelope.Fact as EntityMaterializedFact)?.PrincipalId);
        }

        var proposals = new Dictionary<GameFactSequence, PlayerFactDisclosure>();
        var spatial = new List<(GameFactEnvelope Envelope, PrincipalId? Principal)>();
        foreach (GameFactEnvelope envelope in pending)
        {
            PrincipalId? principal = GamePresentationSnapshotFactory.GetShipId(envelope.Fact) is { } subject
                ? principals[subject] : null;
            principal = envelope.Fact switch
            {
                SensorDeployedFact deployed => deployed.Sensor.PrincipalId,
                SensorPickedUpFact pickedUp => pickedUp.Sensor.PrincipalId,
                SensorRemovedFact removed => removed.Sensor.PrincipalId,
                _ => principal,
            };
            if (PlayerFactProjector.TryProjectWithoutCoverage(envelope.Fact, observer, _playerCommandSource, principal, out GameFact? direct))
                proposals.Add(envelope.Sequence, new PlayerFactDisclosure(observer, direct, principal));
            else spatial.Add((envelope, principal));
        }
        if (spatial.Count > 0)
        {
            GameSnapshot world = _runtime.CaptureSnapshot();
            PlayerCoverageBoundary coverage = _runtime.CaptureCoverageBoundary(observer);
            Dictionary<ShipId, GameShipSnapshot> ships = world.Ships.ToDictionary(ship => ship.Id);
            foreach (var removed in _removedObservationShips) ships.TryAdd(removed.Key, removed.Value);
            var projector = new PlayerFactProjector(_playerCommandSource, world, ships, coverage);
            foreach (var item in spatial)
                proposals.Add(item.Envelope.Sequence, new PlayerFactDisclosure(observer, projector.Project(item.Envelope.Fact), item.Principal));
        }
        _facts.CompletePlayerDisclosure(envelope => proposals[envelope.Sequence]);
        _removedObservationShips.Clear();
    }

    /// <summary>
    /// Validates saved projection shape and ownership evidence without granting
    /// historical visibility from current coverage. Complete causes never form
    /// part of a player notice, and non-owned notices must match their source.
    /// </summary>
    private static bool IsValidPlayerDisclosure(GameFactEnvelope envelope,
        GameSessionCheckpoint checkpoint, PlayerObservationCheckpoint observation)
    {
        if (envelope.PlayerDisclosure is not { } receipt
            || receipt.ObserverPrincipalId != observation.ObserverPrincipalId
            || envelope.Timestamp > checkpoint.Engine.Agenda.CurrentTime) return false;
        ShipId? subject = GamePresentationSnapshotFactory.GetShipId(envelope.Fact);
        EntityLifecycleShipCheckpoint? live = subject is { } id
            ? checkpoint.Lifecycle.LiveShips.FirstOrDefault(ship => ship?.ShipId == id) : null;
        if (live is not null && receipt.SubjectPrincipalId != live.PrincipalId) return false;
        if (receipt.Fact is null) return true;

        PrincipalId observer = observation.ObserverPrincipalId;
        if (receipt.Fact is PlayerCommandOutcomeFact command)
        {
            return envelope.Fact switch
            {
                CommandAcceptedFact accepted => accepted.Source == observation.PlayerCommandSource
                    && command.CommandSequence == accepted.CommandSequence && command.CommandKind == accepted.CommandKind
                    && command.Status == CommandResultStatus.Accepted && command.RejectionCode is null,
                CommandRejectedFact rejected => rejected.Source == observation.PlayerCommandSource
                    && command.CommandSequence == rejected.CommandSequence && command.CommandKind == rejected.CommandKind
                    && command.Status == CommandResultStatus.Rejected && command.RejectionCode == rejected.RejectionCode,
                _ => false,
            };
        }
        if (receipt.Fact == envelope.Fact)
        {
            return envelope.Fact switch
            {
                CommandAcceptedFact or CommandRejectedFact => false,
                StandingChangedFact standing => standing.SubjectPrincipalId == observer,
                DiplomaticConditionChangedFact => true,
                RelationshipGrantIssuedFact grant => grant.HolderPrincipalId == observer,
                RelationshipGrantRevokedFact revoked => checkpoint.Relationships.Grants.Any(grant =>
                    grant?.Id == revoked.Id && grant.HolderPrincipalId == observer),
                EntityMaterializedFact ownedAppearance => ownedAppearance.PrincipalId == observer && receipt.SubjectPrincipalId == observer,
                SensorDeployedFact deployed => deployed.Sensor.PrincipalId == observer && receipt.SubjectPrincipalId == observer,
                SensorPickedUpFact pickedUp => pickedUp.Sensor.PrincipalId == observer && receipt.SubjectPrincipalId == observer,
                SensorRemovedFact removed => removed.Sensor.PrincipalId == observer && receipt.SubjectPrincipalId == observer,
                ObservedShipChangeFact => false,
                _ => PlayerFactProjector.IsOwnedAssetFact(envelope.Fact)
                    && subject is not null && receipt.SubjectPrincipalId == observer,
            };
        }

        if (receipt.Fact is ObservedSensorChangeFact sensorNotice)
        {
            DeployedSensorSnapshot? sensor = envelope.Fact switch
            {
                SensorDeployedFact deployed when sensorNotice.Kind == ObservedSensorChangeKind.Appeared => deployed.Sensor,
                SensorPickedUpFact pickedUp when sensorNotice.Kind == ObservedSensorChangeKind.Disappeared => pickedUp.Sensor,
                SensorRemovedFact removed when sensorNotice.Kind == ObservedSensorChangeKind.Disappeared => removed.Sensor,
                _ => null,
            };
            return sensor is not null && sensor.PrincipalId != observer
                && receipt.SubjectPrincipalId == sensor.PrincipalId && sensorNotice.EntityId == sensor.EntityId
                && sensorNotice.DefinitionKey == sensor.DefinitionKey && sensorNotice.PrincipalId == sensor.PrincipalId
                && sensorNotice.Position == sensor.Position
                && checkpoint.WorldTopology.Systems.Any(system => system?.Id == sensor.Position.SystemId);
        }
        if (receipt.Fact is not ObservedShipChangeFact notice || notice.ShipId != subject
            || receipt.SubjectPrincipalId is null || receipt.SubjectPrincipalId == observer
            || !checkpoint.WorldTopology.Systems.Any(system => system?.Id == notice.Position.SystemId)) return false;
        if (envelope.Fact is EntityMaterializedFact appeared)
            return notice.Kind == ObservedShipChangeKind.Appeared && notice.Position == appeared.InitialPosition
                && notice.EntityId == appeared.EntityId && notice.PrincipalId == appeared.PrincipalId
                && notice.DesignId == appeared.DesignId && receipt.SubjectPrincipalId == appeared.PrincipalId;
        if (notice.EntityId is not null || notice.PrincipalId is not null || notice.DesignId is not null) return false;
        return envelope.Fact switch
        {
            ShipLocalMotionStartedFact started => notice.Kind == ObservedShipChangeKind.MotionChanged && notice.Position == started.Motion.Origin,
            ShipLocalMotionEndedFact ended => notice.Kind == ObservedShipChangeKind.MotionChanged && notice.Position == ended.FinalPosition,
            ShipCruiseEnteredFact entered => notice.Kind == ObservedShipChangeKind.MotionChanged && notice.Position == entered.Position,
            ShipCruiseDroppedOutFact dropped => notice.Kind == ObservedShipChangeKind.MotionChanged && notice.Position == dropped.Position,
            ShipConnectorTransitStartedFact departed => notice.Kind == ObservedShipChangeKind.Departed && notice.Position == departed.Transit.Source,
            ShipConnectorTransitCompletedFact arrived => notice.Kind == ObservedShipChangeKind.Arrived && notice.Position == arrived.Transit.Destination,
            EntityRemovedFact => notice.Kind == ObservedShipChangeKind.Disappeared,
            _ => false,
        };
    }
}
