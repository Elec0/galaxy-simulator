namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private void StartOrContinueOrders(
        ShipId shipId,
        ICollection<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        while (_orders.GetActive(shipId) is { } active)
        {
            SystemPosition? availablePosition = _movement.PositionAt(
                shipId,
                CurrentTime);
            if (availablePosition is null)
            {
                if (_movement.GetState(shipId)
                    is not ShipSpatialState.ConnectorTransit)
                {
                    throw new InvalidOperationException(
                        $"Order actor {shipId} has no spatial state.");
                }

                _orders.WaitForTransitCompletion(
                    shipId,
                    active.Id,
                    transitions);
                return;
            }

            SystemPosition current = availablePosition.Value;
            if (active.Plan is null)
            {
                NavigationPlanResult result = Plan(
                    shipId,
                    current,
                    active.Destination);
                if (result is NavigationPlanResult.Unreachable)
                {
                    _orders.FailActive(
                        shipId,
                        active.Id,
                        transitions);
                    continue;
                }

                TravelPlan plan = ((NavigationPlanResult.Planned)result).Plan;
                ValidateExecutablePlan(current, active.Destination, plan);
                _orders.SetPlan(
                    shipId,
                    active.Id,
                    plan,
                    transitions);
            }

            TravelLeg? nextLeg = _orders.NextLeg(shipId, active.Id);
            if (nextLeg is null)
            {
                if (RuntimeDestinationSatisfied(
                        shipId,
                        current,
                        active.Destination,
                        active.RequestedHeading))
                {
                    _orders.CompleteActive(
                        shipId,
                        active.Id,
                        transitions);
                    continue;
                }

                // A route can legitimately contain no local leg when its
                // destination is already reached. The remaining heading goal
                // still owns a physical stationary maneuver before completion.
                if (active.RequestedHeading is { } heading
                    && DestinationSatisfied(current, active.Destination))
                {
                    ShipKinematicState start = _movement.KinematicStateAt(
                        shipId,
                        CurrentTime)
                        ?? throw new InvalidOperationException(
                            $"Ship {shipId} has no system-local kinematic state.");
                    EffectiveShipManeuverCapability capability =
                        ResolveManeuverCapability(shipId);
                    BoundedTerminalPlanSelection headingSelection =
                        BoundedTerminalManeuverPlanner.Select(
                            CurrentTime,
                            start,
                            current,
                            heading,
                            capability,
                            ManeuverObjective.FastestArrival);
                    ExecutableBoundedTerminalManeuverPlan headingPlan =
                        headingSelection.ExecutablePlan
                        ?? throw new InvalidOperationException(
                            $"Terminal heading for ship {shipId} produced no executable analytic plan.");
                    StartTerminalManeuver(
                        shipId,
                        active,
                        headingPlan,
                        waypointPhaseIndices: [],
                        factProposals);
                    return;
                }

                throw new InvalidOperationException(
                    $"Order {active.Id} exhausted its plan before reaching its destination.");
            }

            switch (nextLeg)
            {
                case TravelLeg.Local local:
                    {
                        if (local.Duration == SimulationDuration.Zero
                            || local.Origin == local.Destination)
                        {
                            _orders.CompleteLeg(shipId, active.Id, null);
                            continue;
                        }

                        ShipKinematicState start = _movement.KinematicStateAt(
                            shipId,
                            CurrentTime)
                            ?? throw new InvalidOperationException(
                                $"Ship {shipId} has no system-local kinematic state.");
                        EffectiveShipManeuverCapability capability =
                            ResolveManeuverCapability(shipId);
                        TravelPlan route = active.Plan
                            ?? throw new InvalidOperationException(
                                $"Order {active.Id} has no travel plan.");
                        TravelLeg[] remaining = route.Legs
                            .Skip(active.NextLegIndex)
                            .ToArray();
                        TravelLeg.Local[] remainingLocal = remaining
                            .OfType<TravelLeg.Local>()
                            .ToArray();
                        bool hasMultipleLocalLegs = remaining.Length > 1
                            && remainingLocal.Length == remaining.Length;
                        ShipHeading? terminalHeading = hasMultipleLocalLegs
                            || remaining.Length == 1
                                ? active.RequestedHeading
                                : null;
                        // A connector boundary requires an exact terminal local
                        // state at its source endpoint. Plan that approach as an
                        // ordinary maneuver instead of treating the complete
                        // cross-system route as one local fly-through path.
                        SystemPosition terminalDestination = hasMultipleLocalLegs
                            ? remainingLocal[^1].Destination
                            : local.Destination;
                        BoundedTerminalPlanSelection selection =
                            BoundedTerminalManeuverPlanner.Select(
                                CurrentTime,
                                start,
                                terminalDestination,
                                terminalHeading,
                                capability,
                                ManeuverObjective.FastestArrival);
                        if (selection.ExecutablePlan is not { } plan)
                        {
                            throw new InvalidOperationException(
                                $"Local route for ship {shipId} produced no executable analytic plan.");
                        }

                        IReadOnlyList<int> waypointPhaseIndices = [];
                        if (hasMultipleLocalLegs)
                        {
                            SystemPosition[] waypoints = remainingLocal[..^1]
                                .Select(static leg => leg.Destination)
                                .ToArray();
                            if (!WaypointManeuverPlan.TryCreate(
                                    plan,
                                    start,
                                    terminalDestination,
                                    waypoints,
                                    out WaypointManeuverPlan? waypointPlan))
                            {
                                SystemPosition[] destinations = remainingLocal
                                    .Select(static leg => leg.Destination)
                                    .ToArray();
                                if (!WaypointManeuverPlan.TryCreateRoute(
                                        CurrentTime,
                                        start,
                                        destinations,
                                        capability,
                                        ManeuverObjective.FastestArrival,
                                        terminalHeading,
                                        out waypointPlan))
                                {
                                    throw new InvalidOperationException(
                                        $"Waypoint route for ship {shipId} produced no executable analytic plan.");
                                }
                            }

                            plan = waypointPlan!.Plan;
                            waypointPhaseIndices = waypointPlan.WaypointPhaseIndices;
                        }

                        StartTerminalManeuver(
                            shipId,
                            active,
                            plan,
                            waypointPhaseIndices,
                            factProposals);
                        return;
                    }
                case TravelLeg.Connector connector:
                    {
                        ConnectorTransitCommit<GameEvent> commit =
                            _movement.CommitStartConnector(
                                shipId,
                                connector,
                                CurrentTime,
                                movement =>
                                    (GameEvent)new GameEvent.SpatialMovement(movement));
                        ConnectorTransitSegment transit = commit.Transit;
                        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
                            _agenda,
                            [commit.EventProposal]);
                        _movement.BindCompletionEvent(
                            shipId,
                            AssertSingleEventKey(agendaCommit));
                        _orders.BindTransit(shipId, active.Id, transit.Id);
                        factProposals.Add(PhysicalWorkStartedProposal(
                            shipId,
                            transit.Id.Value,
                            new ShipConnectorTransitStartedFact(
                                shipId,
                                Snapshot(transit),
                                active.Id)));
                        return;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported travel leg {nextLeg.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Commits and binds one already-selected analytic schedule, retaining the
    /// active order link and any route waypoint phase identities atomically.
    /// </summary>
    private void StartTerminalManeuver(
        ShipId shipId,
        ShipOrder active,
        ExecutableBoundedTerminalManeuverPlan plan,
        IReadOnlyList<int> waypointPhaseIndices,
        List<GameFactProposal> factProposals)
    {
        TerminalManeuverCommit<GameEvent> commit =
            _movement.CommitStartTerminalManeuver(
                shipId,
                plan,
                ManeuverObjective.FastestArrival,
                CurrentTime,
                movement =>
                    (GameEvent)new GameEvent.SpatialMovement(movement),
                waypointPhaseIndices);
        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
            _agenda,
            commit.EventProposals);
        _movement.BindTerminalManeuverEvents(
            shipId,
            agendaCommit.EventKeys);
        _orders.BindMotion(
            shipId,
            active.Id,
            commit.Maneuver.MotionId);
        factProposals.Add(PhysicalWorkStartedProposal(
            shipId,
            commit.Maneuver.MotionId.Value,
            new ShipLocalMotionStartedFact(
                shipId,
                Snapshot(commit.Maneuver),
                active.Id)));
    }

    /// <summary>
    /// Resolves the current base design at its authored mass. Typed equipment
    /// contributions remain owned by TASK-068 and can replace this lookup
    /// without changing maneuver planning or schedule ownership.
    /// </summary>
    private EffectiveShipManeuverCapability ResolveManeuverCapability(
        ShipId shipId)
    {
        GameSessionShip ship = _lifecycle.GetRequiredShip(shipId);
        ShipManeuverCapability capability = _maneuverCapabilities.GetValueOrDefault(
            ship.DesignId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no maneuver capability for design {ship.DesignId}.");
        return capability.ResolveForMass(capability.BaseMassKilograms);
    }

    private void EndActiveLocalMotion(
        ShipId shipId,
        LocalMotionEndReason reason,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(factProposals);
        ShipSpatialState? state = _movement.GetState(shipId);
        if (state is not ShipSpatialState.Moving
            && state is not ShipSpatialState.AnalyticManeuver)
        {
            return;
        }

        ShipOrder active = _orders.GetActive(shipId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has local motion without an active order.");
        LocalMotionSnapshot motion;
        switch (state)
        {
            case ShipSpatialState.Moving moving:
                motion = Snapshot(moving.Motion);
                if (!_movement.CommitCancel(shipId, CurrentTime))
                {
                    throw new InvalidOperationException(
                        $"Ship {shipId} local motion disappeared before cancellation commit.");
                }

                break;
            case ShipSpatialState.AnalyticManeuver analytic:
                motion = Snapshot(analytic.Maneuver);
                bool wasCruising = analytic.Maneuver.CurrentPhase?.Kind
                    == ManeuverPhaseKind.CruiseTravel;
                AgendaCancellationCheck cancellation =
                    _movement.TryInterruptTerminalManeuver(
                        shipId,
                        CurrentTime,
                        _agenda,
                        movement =>
                            (GameEvent)new GameEvent.SpatialMovement(movement),
                        out ManeuverInterruption? interruption);
                if (cancellation != AgendaCancellationCheck.Matches
                    || interruption is null)
                {
                    throw new InvalidOperationException(
                        $"Ship {shipId} maneuver cancellation did not match its pending agenda events.");
                }

                if (wasCruising
                    && reason == LocalMotionEndReason.ReplacedByCommand)
                {
                    ShipKinematicState dropout = interruption.MaterializedState;
                    factProposals.Add(PhysicalCruiseTransitionProposal(
                        shipId,
                        analytic.Maneuver.MotionId.Value,
                        analytic.Maneuver.CurrentPhaseIndex,
                        new ShipCruiseDroppedOutFact(
                            shipId,
                            analytic.Maneuver.MotionId,
                            dropout.Position,
                            dropout.Velocity,
                            CurrentTime,
                            active.Id)));
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported local movement state {state.GetType().Name}.");
        }

        SystemPosition finalPosition = _movement.PositionAt(shipId, CurrentTime)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no materialized position after cancelling local motion.");
        factProposals.Add(PhysicalWorkEndedProposal(
            shipId,
            motion.Id.Value,
            new ShipLocalMotionEndedFact(
                shipId,
                motion,
                finalPosition,
                CurrentTime,
                reason,
                active.Id)));
    }

    /// <summary>
    /// Prepares exact cancellation records for the removed actor's active
    /// movement and verifies their identity against the agenda without
    /// changing either owner.
    /// </summary>
    private EntityRemovalResult.Rejected? PrepareMovementCancellations(
        PreparedEntityRemoval removal,
        out PreparedMovementCancellation[] cancellations)
    {
        if (_movement.GetState(removal.ShipId)
            is ShipSpatialState.AnalyticManeuver analytic)
        {
            IReadOnlyList<PendingManeuverScheduleEvent> pending =
                analytic.Maneuver.PendingEvents
                ?? throw new InvalidOperationException(
                    $"Ship {removal.ShipId} has an unbound analytic maneuver.");
            var prepared = new PreparedMovementCancellation[pending.Count];
            for (int index = 0; index < pending.Count; index++)
            {
                PendingManeuverScheduleEvent boundary = pending[index];
                GameEvent expected = new GameEvent.SpatialMovement(
                    new SpatialMovementEvent.Maneuver(
                        removal.ShipId,
                        boundary.Payload));
                AgendaCancellationCheck boundaryCheck = _agenda.CheckCancellation(
                    boundary.EventKey,
                    boundary.Generation,
                    expected);
                if (boundaryCheck != AgendaCancellationCheck.Matches)
                {
                    cancellations = [];
                    EntityRemovalRejectionReason reason =
                        boundaryCheck == AgendaCancellationCheck.Missing
                            ? EntityRemovalRejectionReason.PendingMovementEventMissing
                            : EntityRemovalRejectionReason.PendingMovementEventMismatch;
                    return new EntityRemovalResult.Rejected(
                        removal.Request,
                        reason);
                }

                prepared[index] = new PreparedMovementCancellation(
                    boundary.EventKey,
                    boundary.Generation,
                    expected);
            }

            Array.Sort(prepared, static (left, right) =>
                left.EventKey.CompareTo(right.EventKey));
            cancellations = prepared;
            return null;
        }

        PendingMovementCompletion? completion =
            _movement.GetPendingCompletion(removal.ShipId);
        if (completion is null)
        {
            cancellations = [];
            return null;
        }

        GameEvent expectedEvent = completion switch
        {
            PendingMovementCompletion.Arrival arrival =>
                new GameEvent.SpatialMovement(new SpatialMovementEvent.Arrive(
                    arrival.ShipId,
                    arrival.MotionId,
                    arrival.Generation)),
            PendingMovementCompletion.Emergence emergence =>
                new GameEvent.SpatialMovement(new SpatialMovementEvent.Emerge(
                    emergence.ShipId,
                    emergence.TransitId,
                    emergence.Generation)),
            _ => throw new InvalidOperationException(
                $"Unsupported pending movement completion {completion.GetType().Name}."),
        };
        AgendaCancellationCheck check = _agenda.CheckCancellation(
            completion.EventKey,
            completion.Generation,
            expectedEvent);
        if (check != AgendaCancellationCheck.Matches)
        {
            cancellations = [];
            EntityRemovalRejectionReason reason = check == AgendaCancellationCheck.Missing
                ? EntityRemovalRejectionReason.PendingMovementEventMissing
                : EntityRemovalRejectionReason.PendingMovementEventMismatch;
            return new EntityRemovalResult.Rejected(removal.Request, reason);
        }

        cancellations =
        [
            new PreparedMovementCancellation(
                completion.EventKey,
                completion.Generation,
                expectedEvent),
        ];
        Array.Sort(cancellations, static (left, right) =>
            left.EventKey.CompareTo(right.EventKey));
        return null;
    }

    /// <summary>
    /// Extracts the one key created for a single movement completion proposal.
    /// </summary>

}
