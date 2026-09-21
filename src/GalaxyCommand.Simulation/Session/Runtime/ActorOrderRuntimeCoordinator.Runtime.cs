namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    public void Reconcile(SimulationTime now, EventAgenda<GameEvent> agenda)
    {
        if (_economy is null)
        {
            return;
        }

        EconomicReconciliationResult reconciliation = _economy.Runtime.Reconcile(
            now,
            _economy.TransportTiming);
        var proposals = new List<AgendaEventProposal<GameEvent>>();
        foreach (ProductionCompletionProposal completion in
                 reconciliation.Production.Commit.CompletionProposals)
        {
            proposals.Add(new AgendaEventProposal<GameEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ProductionReadiness,
                    completion.FacilityId.Value,
                    completion.JobId.Value,
                    EffectKind: 0,
                    LocalOrdinal: 0),
                completion.Timestamp,
                EventPhase.PhysicalCompletion,
                completion.Generation,
                new GameEvent.Economic(new EconomicEvent.ProductionComplete(
                    completion.FacilityId,
                    completion.JobId))));
        }

        foreach (ConstructionCompletionProposal completion in
                 reconciliation.Construction.Commit.CompletionProposals)
        {
            proposals.Add(new AgendaEventProposal<GameEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ConstructionReadiness,
                    completion.FacilityId.Value,
                    completion.OrderId.Value,
                    EffectKind: 0,
                    LocalOrdinal: 0),
                completion.Timestamp,
                EventPhase.PhysicalCompletion,
                completion.Generation,
                new GameEvent.Economic(new EconomicEvent.ConstructionComplete(
                    completion.FacilityId,
                    completion.OrderId))));
        }

        AddTransportEventProposals(
            reconciliation.TransportAdvance.Commit.EventProposals,
            proposals);
        if (proposals.Count > 0)
        {
            _ = AgendaCommitOwner.Commit(agenda, proposals);
        }
    }

    public void AccrueTo(SimulationTime now)
    {
    }

    /// <inheritdoc/>
    public ScheduledEventDisposition HandleEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        SimulationTime now,
        EventAgenda<GameEvent> agenda)
    {
        if (simulationEvent.Payload is GameEvent.Economic economic)
        {
            return HandleEconomicEvent(simulationEvent, economic.Event, now, agenda);
        }

        if (simulationEvent.Payload is not GameEvent.SpatialMovement spatial)
        {
            throw new InvalidOperationException(
                $"Unsupported game event {simulationEvent.Payload.GetType().Name}.");
        }

        LocalMotionSegment? endingMotion = spatial.Event switch
        {
            SpatialMovementEvent.Arrive arrive
                when _movement.GetState(arrive.ShipId)
                    is ShipSpatialState.Moving moving
                    && moving.Motion.Id == arrive.MotionId =>
                moving.Motion,
            _ => null,
        };
        ConnectorTransitSegment? completingTransit = spatial.Event switch
        {
            SpatialMovementEvent.Emerge emerge
                when _movement.GetState(emerge.ShipId)
                    is ShipSpatialState.ConnectorTransit traversing
                    && traversing.Transit.Id == emerge.TransitId =>
                traversing.Transit,
            _ => null,
        };
        ScheduledTerminalManeuver? endingManeuver = spatial.Event switch
        {
            SpatialMovementEvent.Maneuver maneuver
                when maneuver.Event is ManeuverScheduleEvent.Complete
                    && _movement.GetState(maneuver.ShipId)
                        is ShipSpatialState.AnalyticManeuver active
                    && active.Maneuver.MotionId == maneuver.Event.MotionId =>
                active.Maneuver,
            _ => null,
        };
        ScheduledTerminalManeuver? phaseBoundaryManeuver = spatial.Event switch
        {
            SpatialMovementEvent.Maneuver maneuver
                when maneuver.Event is ManeuverScheduleEvent.PhaseBoundary
                    && _movement.GetState(maneuver.ShipId)
                        is ShipSpatialState.AnalyticManeuver active
                    && active.Maneuver.MotionId == maneuver.Event.MotionId =>
                active.Maneuver,
            _ => null,
        };
        ScheduledTerminalManeuver? waypointManeuver =
            phaseBoundaryManeuver is { } boundaryManeuver
                && spatial.Event is SpatialMovementEvent.Maneuver
                {
                    Event: ManeuverScheduleEvent.PhaseBoundary waypointBoundary,
                }
                && boundaryManeuver.IsWaypointBoundary(
                    waypointBoundary.PhaseIndex)
                    ? boundaryManeuver
                    : null;
        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        ScheduledEventDisposition disposition = _movement.HandleEvent(
            spatial.Event,
            simulationEvent.Generation,
            now);
        if (disposition == ScheduledEventDisposition.Applied)
        {
            bool continueOrders = true;
            switch (spatial.Event)
            {
                case SpatialMovementEvent.Arrive arrive:
                    {
                        ShipOrder active = _orders.GetActive(arrive.ShipId)
                            ?? throw new InvalidOperationException(
                                $"Ship {arrive.ShipId} completed local motion without an active order.");
                        LocalMotionSegment motion = endingMotion
                            ?? throw new InvalidOperationException(
                                $"Applied arrival for ship {arrive.ShipId} had no matching motion.");
                        factProposals.Add(PhysicalWorkEndedProposal(
                            arrive.ShipId,
                            motion.Id.Value,
                            new ShipLocalMotionEndedFact(
                                arrive.ShipId,
                                Snapshot(motion),
                                motion.Destination,
                                now,
                                LocalMotionEndReason.Arrived,
                                active.Id)));
                        _orders.CompleteLeg(
                            arrive.ShipId,
                            active.Id,
                            arrive.MotionId);
                        break;
                    }
                case SpatialMovementEvent.Maneuver
                {
                    Event: ManeuverScheduleEvent.PhaseBoundary boundary,
                }:
                    if (phaseBoundaryManeuver is { } transitioning)
                    {
                        AddCruiseTransitionProposal(
                            spatial.Event.ShipId,
                            boundary,
                            transitioning,
                            now,
                            factProposals);
                    }

                    if (waypointManeuver is { } continuing)
                    {
                        ShipId shipId = spatial.Event.ShipId;
                        ShipOrder active = _orders.GetActive(shipId)
                            ?? throw new InvalidOperationException(
                                $"Ship {shipId} reached a waypoint without an active order.");
                        TravelLeg.Local waypointLeg = _orders.NextLeg(
                            shipId,
                            active.Id) as TravelLeg.Local
                            ?? throw new InvalidOperationException(
                                $"Ship {shipId} reached a local waypoint without a local route leg.");
                        ShipKinematicState reached = continuing.Plan.StateAt(now);
                        if (!ManeuverArrival.IsFlyThroughWaypointReached(
                            reached,
                            waypointLeg.Destination))
                        {
                            throw new InvalidOperationException(
                                $"Ship {shipId} waypoint boundary did not satisfy its positional tolerance.");
                        }

                        _orders.CompleteLeg(
                            shipId,
                            active.Id,
                            continuing.MotionId);
                        _orders.BindMotion(
                            shipId,
                            active.Id,
                            continuing.MotionId);
                        factProposals.Add(WaypointArrivedProposal(
                            shipId,
                            continuing.MotionId.Value,
                            boundary.PhaseIndex,
                            new ShipWaypointArrivedFact(
                                shipId,
                                continuing.MotionId,
                                waypointLeg.Destination,
                                reached.Position,
                                now,
                                active.Id)));
                    }

                    // Other physical boundaries update spatial state only;
                    // the bound order owns the complete terminal maneuver.
                    continueOrders = false;
                    break;
                case SpatialMovementEvent.Maneuver maneuver
                    when maneuver.Event is ManeuverScheduleEvent.Complete:
                    {
                        ShipOrder active = _orders.GetActive(maneuver.ShipId)
                            ?? throw new InvalidOperationException(
                                $"Ship {maneuver.ShipId} completed a maneuver without an active order.");
                        ScheduledTerminalManeuver completed = endingManeuver
                            ?? throw new InvalidOperationException(
                                $"Applied completion for ship {maneuver.ShipId} had no matching maneuver.");
                        ShipKinematicState final = completed.Plan.StateAt(now);
                        factProposals.Add(PhysicalWorkEndedProposal(
                            maneuver.ShipId,
                            completed.MotionId.Value,
                            new ShipLocalMotionEndedFact(
                                maneuver.ShipId,
                                Snapshot(completed),
                                final.Position,
                                now,
                                LocalMotionEndReason.Arrived,
                                active.Id)));
                        _orders.CompleteLeg(
                            maneuver.ShipId,
                            active.Id,
                            completed.MotionId);
                        break;
                    }
                case SpatialMovementEvent.Emerge emerge:
                    {
                        ConnectorTransitSegment transit = completingTransit
                            ?? throw new InvalidOperationException(
                                $"Applied emergence for ship {emerge.ShipId} had no matching transit.");
                        ShipOrderId? transitOrderId = null;
                        if (_orders.GetActive(emerge.ShipId) is { } active
                            && _orders.IsBoundTransit(
                                emerge.ShipId,
                                emerge.TransitId))
                        {
                            transitOrderId = active.Id;
                            _orders.CompleteTransit(
                                emerge.ShipId,
                                active.Id,
                                emerge.TransitId);
                        }

                        factProposals.Add(PhysicalWorkEndedProposal(
                            emerge.ShipId,
                            transit.Id.Value,
                            new ShipConnectorTransitCompletedFact(
                                emerge.ShipId,
                                Snapshot(transit),
                                now,
                                transitOrderId)));
                        break;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported spatial event {spatial.Event.GetType().Name}.");
            }

            if (continueOrders)
            {
                StartOrContinueOrders(
                    spatial.Event.ShipId,
                    transitions,
                    factProposals);
                AddOrderTransitionProposals(transitions, factProposals);
            }

            if (factProposals.Count > 0)
            {
                _facts.Commit(
                    now,
                    new ScheduledEventFactCause(simulationEvent.Key),
                    factProposals);
            }
        }

        return disposition;
    }

    public void RecordEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        ScheduledEventDisposition disposition)
    {
        GameEventKind kind = simulationEvent.Payload switch
        {
            GameEvent.SpatialMovement spatial =>
                new GameEventKind.SpatialMovement(spatial.Event),
            GameEvent.Economic economic => new GameEventKind.Economic(economic.Event),
            _ => throw new InvalidOperationException(
                $"Unsupported game event {simulationEvent.Payload.GetType().Name}."),
        };
        _eventRecords.Add(new GameEventRecord(
            simulationEvent.Key.Timestamp,
            simulationEvent.Key.Phase,
            simulationEvent.Key.CreationSequence,
            simulationEvent.Generation,
            disposition,
            kind));
    }

    /// <summary>
    /// Commits one private economic completion, acknowledges completed ship
    /// construction through lifecycle, and schedules any transport continuation.
    /// </summary>
    private ScheduledEventDisposition HandleEconomicEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        EconomicEvent economicEvent,
        SimulationTime now,
        EventAgenda<GameEvent> agenda)
    {
        if (_economy is null)
        {
            return ScheduledEventDisposition.IgnoredMissingReference;
        }

        EconomicEventCommitResult result = _economy.Runtime.CommitEvent(
            economicEvent,
            simulationEvent.Key,
            simulationEvent.Generation,
            _economy.TransportTiming,
            now);
        if (result is EconomicEventCommitResult.Construction
            {
                Result.Materialization: { } materialization,
            })
        {
            ConstructionProcess source = _economy.GetRequiredConstructionProcess(
                materialization.FacilityId);
            ConstructionMaterializationCommit commit = _lifecycle.MaterializeConstruction(
                source,
                materialization,
                now);
            CommitMaterializationFact(commit);
        }

        if (result is EconomicEventCommitResult.Transport transport)
        {
            var proposals = new List<AgendaEventProposal<GameEvent>>();
            AddTransportEventProposals(transport.Result.Continuation.EventProposals, proposals);
            if (proposals.Count > 0)
            {
                _ = AgendaCommitOwner.Commit(agenda, proposals);
            }
        }

        return result.Disposition;
    }

    /// <summary>
    /// Converts committed transport continuation proposals into the session's
    /// deterministic agenda order without exposing its agenda to the owner.
    /// </summary>
    private static void AddTransportEventProposals(
        IEnumerable<TransportEventProposal> transportEvents,
        List<AgendaEventProposal<GameEvent>> destination)
    {
        foreach (TransportEventProposal transportEvent in transportEvents)
        {
            destination.Add(new AgendaEventProposal<GameEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.LogisticsAssignment,
                    transportEvent.ShipId.Value,
                    transportEvent.JobId.Value,
                    EffectKind: TransportEventKindOrder(transportEvent.Event),
                    LocalOrdinal: 0),
                transportEvent.Timestamp,
                EventPhase.PhysicalCompletion,
                transportEvent.Generation,
                new GameEvent.Economic(new EconomicEvent.Transport(
                    transportEvent.Event))));
        }
    }

    /// <summary>
    /// Provides a fixed local ordering for distinct same-job transport events.
    /// </summary>
    private static int TransportEventKindOrder(TransportEvent transportEvent) =>
        transportEvent switch
        {
            TransportEvent.Arrive => 0,
            TransportEvent.FinishLoading => 1,
            TransportEvent.FinishUnloading => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(transportEvent)),
        };

    /// <summary>
    /// Evaluates and commits one ordinary move through the per-ship order
    /// lifecycle, buffering all resulting semantic facts for command commit.
    /// </summary>

}
