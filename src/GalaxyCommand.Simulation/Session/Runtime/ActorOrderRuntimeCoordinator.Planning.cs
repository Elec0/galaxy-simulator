namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private static EventKey AssertSingleEventKey(AgendaCommitResult commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return commit.EventKeys.Count == 1
            ? commit.EventKeys[0]
            : throw new InvalidOperationException(
                "A movement completion proposal must create exactly one agenda event.");
    }

    private static void AddOrderTransitionProposals(
        IEnumerable<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        int ordinal = 0;
        foreach (ShipOrderTransition transition in transitions)
        {
            factProposals.Add(new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.OrderTransition,
                    transition.ShipId.Value,
                    transition.OrderId.Value,
                    ordinal),
                new ShipOrderTransitionFact(
                    transition.ShipId,
                    transition.OrderId,
                    transition.Source,
                    transition.Destination,
                    transition.PreviousStatus,
                    transition.NextStatus,
                    transition.Reason,
                    transition.RequestedHeading)));
            ordinal = checked(ordinal + 1);
        }
    }

    private static GameFactProposal PhysicalWorkEndedProposal(
        ShipId shipId,
        ulong activityId,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalWorkEnded,
                shipId.Value,
                activityId,
                0),
            fact);

    private static GameFactProposal PhysicalWorkStartedProposal(
        ShipId shipId,
        ulong activityId,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalWorkStarted,
                shipId.Value,
                activityId,
                0),
            fact);

    private static GameFactProposal WaypointArrivedProposal(
        ShipId shipId,
        ulong activityId,
        int phaseIndex,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalWaypoint,
                shipId.Value,
                activityId,
                phaseIndex),
            fact);

    /// <summary>
    /// Emits only the two semantic cruise transitions. Boundaries inserted for
    /// waypoints inside a spool or cruise phase retain the same adjacent phase
    /// kind and therefore remain diagnostic-only.
    /// </summary>
    private void AddCruiseTransitionProposal(
        ShipId shipId,
        ManeuverScheduleEvent.PhaseBoundary boundary,
        ScheduledTerminalManeuver maneuver,
        SimulationTime now,
        List<GameFactProposal> factProposals)
    {
        int nextIndex = boundary.PhaseIndex + 1;
        if (nextIndex >= maneuver.Plan.Phases.Count)
        {
            return;
        }

        ManeuverPhaseKind ending = maneuver.Plan.Phases[boundary.PhaseIndex].Kind;
        ManeuverPhaseKind starting = maneuver.Plan.Phases[nextIndex].Kind;
        bool entersCruise = ending == ManeuverPhaseKind.MovingSpool
            && starting == ManeuverPhaseKind.CruiseTravel;
        bool dropsOut = ending == ManeuverPhaseKind.CruiseTravel
            && starting == ManeuverPhaseKind.ActiveBrake;
        if (!entersCruise && !dropsOut)
        {
            return;
        }

        ShipOrder order = _orders.GetActive(shipId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} crossed a cruise boundary without an active order.");
        ShipKinematicState state = maneuver.Plan.StateAt(now);
        GameFact fact = entersCruise
            ? new ShipCruiseEnteredFact(
                shipId,
                maneuver.MotionId,
                state.Position,
                state.Velocity,
                now,
                order.Id)
            : new ShipCruiseDroppedOutFact(
                shipId,
                maneuver.MotionId,
                state.Position,
                state.Velocity,
                now,
                order.Id);
        factProposals.Add(PhysicalCruiseTransitionProposal(
            shipId,
            maneuver.MotionId.Value,
            boundary.PhaseIndex,
            fact));
    }

    private static GameFactProposal PhysicalCruiseTransitionProposal(
        ShipId shipId,
        ulong activityId,
        int phaseIndex,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalCruiseTransition,
                shipId.Value,
                activityId,
                phaseIndex),
            fact);

    private static LocalMotionSnapshot Snapshot(LocalMotionSegment motion) =>
        new(
            motion.Id,
            motion.Generation,
            motion.Origin,
            motion.Destination,
            motion.DepartedAt,
            motion.ArrivesAt,
            motion.CompletionEventKey);

    /// <summary>
    /// Projects the complete analytic schedule through the existing local-work
    /// fact contract. Internal phase keys remain diagnostic-only; the final key
    /// represents completion of the complete local movement.
    /// </summary>
    private static LocalMotionSnapshot Snapshot(
        ScheduledTerminalManeuver maneuver)
    {
        EventKey? completionKey = maneuver.PendingEvents is { Count: > 0 } pending
            ? pending[^1].EventKey
            : null;
        return new LocalMotionSnapshot(
            maneuver.MotionId,
            maneuver.Generation,
            maneuver.Plan.StateAt(maneuver.Plan.StartsAt).Position,
            maneuver.Plan.StateAt(maneuver.Plan.EndsAt).Position,
            maneuver.Plan.StartsAt,
            maneuver.Plan.EndsAt,
            completionKey);
    }

    private static ConnectorTransitSnapshot Snapshot(
        ConnectorTransitSegment transit) =>
        new(
            transit.Id,
            transit.Generation,
            transit.ConnectionId,
            transit.Source,
            transit.Destination,
            transit.DepartedAt,
            transit.ArrivesAt,
            transit.CompletionEventKey);

    /// <summary>
    /// Builds one stable maneuver-capability catalog from initial and
    /// materializable ship definitions. Reusing an identifier with different
    /// authored movement behavior is rejected before any command can plan.
    /// </summary>
    private static Dictionary<ConstructionDesignId, ShipManeuverCapability>
        BuildManeuverCapabilities(IEnumerable<ShipDesign> designs)
    {
        ArgumentNullException.ThrowIfNull(designs);
        var capabilities = new Dictionary<
            ConstructionDesignId,
            ShipManeuverCapability>();
        foreach (ShipDesign design in designs)
        {
            ArgumentNullException.ThrowIfNull(design);
            if (capabilities.TryGetValue(
                    design.Id,
                    out ShipManeuverCapability? existing)
                && existing != design.ManeuverCapability)
            {
                throw new InvalidOperationException(
                    $"Ship design {design.Id} has conflicting maneuver capabilities.");
            }

            capabilities[design.Id] = design.ManeuverCapability;
        }

        return capabilities;
    }

    private NavigationPlanResult Plan(
        ShipId shipId,
        SystemPosition origin,
        NavigationDestination destination)
    {
        NavigationDestination planningDestination = destination;
        if (destination is NavigationDestination.Entity entity)
        {
            ShipId? targetShipId = _lifecycle.Entities.GetShipId(entity.EntityId);
            SystemPosition? targetPosition = targetShipId is { } target
                ? _movement.PositionAt(target, CurrentTime)
                : null;
            if (targetPosition is null)
            {
                return new NavigationPlanResult.Unreachable(
                    NavigationFailureReason.EntityUnavailable);
            }

            planningDestination = new NavigationDestination.Position(
                targetPosition.Value);
        }

        NavigationPlanResult result = _navigation.Plan(new NavigationRequest(
            shipId,
            origin,
            planningDestination,
            CurrentTime));
        return result is NavigationPlanResult.Planned planned
            && planningDestination != destination
            ? new NavigationPlanResult.Planned(
                new TravelPlan(destination, planned.Plan.Legs))
            : result;
    }

    private void ValidateExecutablePlan(
        SystemPosition origin,
        NavigationDestination destination,
        TravelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Destination != destination)
        {
            throw new InvalidOperationException(
                "Navigation returned a plan for a different destination.");
        }

        SystemPosition expectedOrigin = origin;
        foreach (TravelLeg travelLeg in plan.Legs)
        {
            switch (travelLeg)
            {
                case TravelLeg.Local local:
                    if (local.Origin != expectedOrigin)
                    {
                        throw new InvalidOperationException(
                            $"Travel plan leg begins at {local.Origin}, expected {expectedOrigin}.");
                    }

                    expectedOrigin = local.Destination;
                    break;
                case TravelLeg.Connector connector:
                    ValidateConnectorLeg(expectedOrigin, connector);
                    expectedOrigin = connector.Destination;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The current runtime cannot execute {travelLeg.GetType().Name}.");
            }
        }

        bool destinationSatisfied = DestinationSatisfied(
            expectedOrigin,
            destination);
        if (!destinationSatisfied)
        {
            throw new InvalidOperationException(
                $"Travel plan ends at {expectedOrigin}, which does not satisfy {destination}.");
        }
    }

    private void ValidateConnectorLeg(
        SystemPosition expectedOrigin,
        TravelLeg.Connector leg)
    {
        TransitConnection connection = _worldTopology.Connectors.GetConnection(
            leg.ConnectionId);
        ConnectorEndpoint source = _worldTopology.Connectors.GetEndpoint(
            connection.SourceEndpointId);
        ConnectorEndpoint destination = _worldTopology.Connectors.GetEndpoint(
            connection.DestinationEndpointId);
        if (leg.Origin != expectedOrigin
            || leg.Origin != source.Position
            || leg.Destination != destination.Position
            || leg.Duration != connection.Duration)
        {
            throw new InvalidOperationException(
                $"Connector leg {leg.ConnectionId} does not match authoritative topology.");
        }
    }

    private bool DestinationSatisfied(
        SystemPosition current,
        NavigationDestination destination) =>
        destination switch
        {
            NavigationDestination.Position position =>
                current == position.Value,
            NavigationDestination.System system =>
                current.SystemId == system.SystemId,
            NavigationDestination.Entity entity =>
                _lifecycle.Entities.GetShipId(entity.EntityId) is { } targetShipId
                && _movement.PositionAt(targetShipId, CurrentTime) == current,
            _ => false,
        };

    /// <summary>
    /// Applies the maneuver contract's terminal position and velocity
    /// tolerances after an analytic leg, while retaining exact legacy rules
    /// for system and entity destinations.
    /// </summary>
    private bool RuntimeDestinationSatisfied(
        ShipId shipId,
        SystemPosition current,
        NavigationDestination destination,
        ShipHeading? requestedHeading) =>
        destination switch
        {
            NavigationDestination.Position position =>
                _movement.KinematicStateAt(shipId, CurrentTime) is { } state
                && ManeuverArrival.EvaluateTerminal(
                    state,
                    position.Value,
                    requestedHeading).IsSatisfied,
            _ => DestinationSatisfied(current, destination)
                && (requestedHeading is null
                    || _movement.KinematicStateAt(shipId, CurrentTime) is { } state
                    && ManeuverArrival.EvaluateTerminal(
                        state,
                        current,
                        requestedHeading).IsSatisfied),
        };

    private CommandResult? RejectIneligible(
        ShipId shipId,
        CommandSource source) =>
        _control.CheckCommand(shipId, source) switch
        {
            ActorCommandEligibility.Eligible => null,
            ActorCommandEligibility.MissingActor => CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {shipId}."),
            ActorCommandEligibility.ActorOverridden => CommandResult.Rejected(
                CommandRejectionCodes.ActorOverridden,
                $"Ship {shipId} is under a temporary scripted override."),
            ActorCommandEligibility.IneligibleSource => CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                $"Command source {source.Kind}:{source.Id} does not control ship {shipId}."),
            _ => throw new InvalidOperationException("Unknown actor command eligibility."),
        };

    private static CommandResult? RejectInvalidOverride(
        ActorOverrideValidation validation,
        ShipId shipId) =>
        validation switch
        {
            ActorOverrideValidation.Valid => null,
            ActorOverrideValidation.MissingActor => CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {shipId}."),
            ActorOverrideValidation.InvalidSource => CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                $"Command source cannot change the override for ship {shipId}."),
            ActorOverrideValidation.Conflict => CommandResult.Rejected(
                CommandRejectionCodes.Conflict,
                $"Ship {shipId} has conflicting override state."),
            ActorOverrideValidation.StaleRevision => CommandResult.Rejected(
                CommandRejectionCodes.StaleControlRevision,
                $"Ship {shipId} control revision has changed."),
            _ => throw new InvalidOperationException("Unknown actor override validation."),
        };


}
