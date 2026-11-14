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
    /// Commits persistent observations even when there are no pending facts,
    /// then freezes eligibility and safe payloads using one completed boundary view.
    /// Reads never recompute history from later coverage or relationship state.
    /// Ownership-only policies read only pending subjects; spatial inputs are
    /// prepared once when needed. All receipt proposals precede publication.
    /// </summary>
    private void CompletePlayerDisclosure()
    {
        CommitDeployableObservations();
        CommitStationObservations();
        CompleteDialogueBoundary();
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
                StationCreatedFact created => created.PrincipalId,
                StationRemovedFact removed => removed.Station.PrincipalId,
                StationCommitmentInvalidatedFact invalidated => invalidated.PrincipalId,
                _ when PlayerFactProjector.GetStationId(envelope.Fact) is { } station => _runtime.CaptureStationPrincipal(station),
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
    /// Compares independently restored historical payloads by complete value. Station removal embeds
    /// collection-bearing snapshots whose record equality otherwise depends on array identity.
    /// Standing contributions and field-order priorities likewise retain their complete ordered values.
    /// </summary>
    private static bool SamePlayerFactPayload(GameFact left, GameFact right) => (left, right) switch
    {
        (DialogueFact first, DialogueFact second) => SameDialogueFactPayload(first, second),
        (StationRemovedFact first, StationRemovedFact second) => first.Reason == second.Reason
            && StationSnapshotValueEquality.Equal(first.Station, second.Station),
        (StandingChangedFact first, StandingChangedFact second) => first.AssessingPrincipalId == second.AssessingPrincipalId
            && first.SubjectPrincipalId == second.SubjectPrincipalId && first.PriorValue == second.PriorValue
            && first.PriorBand == second.PriorBand && first.CombinedDelta == second.CombinedDelta
            && first.ResultingValue == second.ResultingValue && first.ResultingBand == second.ResultingBand
            && first.Contributions.SequenceEqual(second.Contributions),
        (ShipOrderTransitionFact { Action: ShipActionIntent.CollectField firstAction } first,
            ShipOrderTransitionFact { Action: ShipActionIntent.CollectField secondAction } second) =>
            first.ShipId == second.ShipId && first.OrderId == second.OrderId && first.Source == second.Source
            && first.Destination == second.Destination && first.RequestedHeading == second.RequestedHeading
            && first.PreviousStatus == second.PreviousStatus && first.NextStatus == second.NextStatus && first.Reason == second.Reason
            && firstAction.OriginCommandSequence == secondAction.OriginCommandSequence
            && firstAction.CargoInventoryId == secondAction.CargoInventoryId && firstAction.TargetFieldId == secondAction.TargetFieldId
            && firstAction.LastObservedObjectEntityId == secondAction.LastObservedObjectEntityId
            && firstAction.LastObservedPosition == secondAction.LastObservedPosition
            && firstAction.Priorities.SequenceEqual(secondAction.Priorities),
        _ => left == right,
    };

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
        if (envelope.Fact is CargoObjectAppearedFact or CargoObjectDisappearedFact
            && receipt.SubjectPrincipalId is not null) return false;
        StationId? stationSubject = PlayerFactProjector.GetStationId(envelope.Fact);
        StationStateCheckpoint? station = stationSubject is { } stationId
            ? checkpoint.Stations?.Stations.FirstOrDefault(item => item.Id == stationId) : null;
        StationRemovalReceipt? removal = stationSubject is { } removedStationId
            ? checkpoint.Stations?.Removals.FirstOrDefault(item => item.Station.Id == removedStationId) : null;
        StationSnapshot? removedStation = removal?.Station;
        PrincipalId? stationPrincipal = station?.Principal ?? removedStation?.PrincipalId;
        if (stationPrincipal is not null && receipt.SubjectPrincipalId != stationPrincipal) return false;
        if (envelope.Fact is StationRemovedFact removedSource && (removal is null
            || !StationSnapshotValueEquality.Equal(removedSource.Station, removal.Station) || removedSource.Reason != removal.Reason
            || envelope.Timestamp != removal.RemovedAt)) return false;
        if (receipt.Fact is null) return true;

        PrincipalId observer = observation.ObserverPrincipalId;
        if (receipt.Fact is ObservedCargoAppearanceFact cargoAppearance)
            return envelope.Fact is CargoObjectAppearedFact source && cargoAppearance.Cargo == source.Object
                && checkpoint.WorldTopology.Systems.Any(system => system?.Id == cargoAppearance.Cargo.Position.SystemId);
        if (receipt.Fact is ObservedCargoDisappearanceFact cargoDisappearance)
            return envelope.Fact is CargoObjectDisappearedFact source && cargoDisappearance.EntityId == source.EntityId
                && cargoDisappearance.Position == source.Position
                && checkpoint.WorldTopology.Systems.Any(system => system?.Id == cargoDisappearance.Position.SystemId);
        if (receipt.Fact is PlayerCargoCollectedFact collected)
        {
            // Preserve the saved owned result without granting new disclosure
            // from target coverage or hidden lifecycle state at restoration.
            return receipt.SubjectPrincipalId == observer && envelope.Fact is CargoCollectedFact source
                && collected == new PlayerCargoCollectedFact(source.ShipId, source.OrderId,
                    source.Outcome.DefinitionKey, source.Outcome.Quantity, source.Outcome.InstanceId);
        }
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
        if (receipt.Fact is ObservedStationChangeFact stationNotice)
        {
            bool kindMatches = envelope.Fact switch
            {
                StationCreatedFact => stationNotice.Kind == ObservedStationChangeKind.Appeared,
                StationProjectCompletedFact completed => stationNotice.Kind == ObservedStationChangeKind.Changed
                    && (station?.Projects.Any(project => project.Id == completed.ProjectId && project.Kind == StationProjectKind.BaseConstruction)
                        ?? removedStation?.Projects.Any(project => project.Id == completed.ProjectId && project.Kind == StationProjectKind.BaseConstruction)) == true,
                StationModuleChangedFact or StationModuleRemovedFact => stationNotice.Kind == ObservedStationChangeKind.Changed,
                StationRemovedFact => stationNotice.Kind == ObservedStationChangeKind.Disappeared,
                _ => false,
            };
            return kindMatches && stationNotice.Id == stationSubject && stationNotice.PrincipalId != observer
                && receipt.SubjectPrincipalId == stationNotice.PrincipalId
                && (station?.Entity ?? removedStation?.EntityId) == stationNotice.EntityId
                && (station?.DefinitionKey ?? removedStation?.DefinitionKey) == stationNotice.DefinitionKey
                && stationPrincipal == stationNotice.PrincipalId && (station?.Position ?? removedStation?.Position) == stationNotice.Position;
        }
        if (SamePlayerFactPayload(receipt.Fact, envelope.Fact))
        {
            return envelope.Fact switch
            {
                DialogueFact => receipt.SubjectPrincipalId is null,
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
                StationShipConstructionQueuedFact queued => stationPrincipal == observer && receipt.SubjectPrincipalId == observer
                    && checkpoint.Stations?.ShipOrders.Any(order => order.Station == queued.StationId && order.Module == queued.Module
                        && order.Facility == queued.Facility && order.Order == queued.Order && order.Design == queued.Design) == true,
                StationCreatedFact or StationProjectChangedFact or StationProjectCompletedFact or StationModuleChangedFact or StationModuleRemovedFact or StationRemovedFact or StationCommitmentInvalidatedFact =>
                    stationPrincipal == observer && receipt.SubjectPrincipalId == observer,
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
