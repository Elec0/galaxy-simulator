namespace GalaxyCommand.Simulation;

/// <summary>
/// Read-only execution view over one concrete bounded terminal selection.
/// It preserves the selected plan's exact analytic schedule and evaluator.
/// </summary>
public sealed class ExecutableBoundedTerminalManeuverPlan
{
    private readonly Func<SimulationTime, ShipKinematicState> stateAt;

    /// <summary>
    /// Freezes one complete phase projection and rejects gaps, overlap, or
    /// endpoints that diverge from the wrapped analytic plan.
    /// </summary>
    private ExecutableBoundedTerminalManeuverPlan(
        BoundedTerminalPlanKind kind,
        SimulationTime startsAt,
        SimulationTime endsAt,
        Func<SimulationTime, ShipKinematicState> stateAt,
        IEnumerable<ManeuverScheduledPhase> phases)
    {
        Kind = kind;
        StartsAt = startsAt;
        EndsAt = endsAt;
        this.stateAt = stateAt;
        ManeuverScheduledPhase[] phaseArray = phases.ToArray();
        if (phaseArray.Length == 0
            || phaseArray[0].StartsAt != startsAt
            || phaseArray[^1].EndsAt != endsAt
            || phaseArray.Zip(
                    phaseArray.Skip(1),
                    static (previous, next) =>
                        previous.EndsAt == next.StartsAt)
                .Any(static contiguous => !contiguous))
        {
            throw new InvalidOperationException(
                "An executable maneuver requires a complete contiguous phase schedule.");
        }

        Phases = Array.AsReadOnly(phaseArray);
    }

    public BoundedTerminalPlanKind Kind { get; }

    public SimulationTime StartsAt { get; }

    public SimulationTime EndsAt { get; }

    public IReadOnlyList<ManeuverScheduledPhase> Phases { get; }

    internal static ExecutableBoundedTerminalManeuverPlan TerminalSettle(
        SimulationTime at,
        ShipKinematicState settled) =>
        new(
            BoundedTerminalPlanKind.TerminalSettle,
            at,
            at,
            _ => settled,
            ManeuverPhaseSchedules.TerminalSettle(at));

    /// <summary>
    /// Adapts orderless passive drift into the shared analytic scheduler so a
    /// cancelled ship keeps moving until drag brings it to rest.
    /// </summary>
    internal static ExecutableBoundedTerminalManeuverPlan FromPassiveDrag(
        DecelerationManeuverSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.Kind != ManeuverDecelerationKind.PassiveDrag)
        {
            throw new ArgumentException(
                "An orderless drift plan requires passive drag.",
                nameof(segment));
        }

        return new ExecutableBoundedTerminalManeuverPlan(
            BoundedTerminalPlanKind.PassiveDrag,
            segment.StartsAt,
            segment.EndsAt,
            segment.StateAt,
            [
                new ManeuverScheduledPhase(
                    ManeuverPhaseKind.CoastUnderPassiveDrag,
                    segment.StartsAt,
                    segment.EndsAt),
            ]);
    }

    internal static ExecutableBoundedTerminalManeuverPlan From(
        StationaryTurnManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.StationaryTurn,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(plan));

    /// <summary>
    /// Adapts the selected complete directional candidate and rejects any
    /// selection that lost its sole executable payload.
    /// </summary>
    internal static ExecutableBoundedTerminalManeuverPlan From(
        StationaryDirectionalPlanSelection selection)
    {
        StationaryDirectionalManeuverPlan plan = selection.CompletePlan
            ?? throw InvalidPayload(selection.Kind);
        return new(
            BoundedTerminalPlanKind.StationaryDirectional,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(selection));
    }

    internal static ExecutableBoundedTerminalManeuverPlan From(
        MovingAlignedTerminalManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.MovingAlignedTerminal,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(plan));

    internal static ExecutableBoundedTerminalManeuverPlan From(
        CruiseTerminalManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.CruiseTerminal,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(plan));

    internal static ExecutableBoundedTerminalManeuverPlan From(
        ForcedCruiseDropoutTerminalManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.ForcedCruiseDropoutTerminal,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(plan));

    internal static ExecutableBoundedTerminalManeuverPlan From(
        BrakeThenStationaryDirectionalManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.BrakeThenStationaryDirectional,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(plan));

    internal static ExecutableBoundedTerminalManeuverPlan From(
        ReducedThrustTerminalManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.ReducedThrustTerminal,
            plan.StartsAt,
            plan.EndsAt,
            plan.StateAt,
            ManeuverPhaseSchedules.From(plan));

    /// <summary>
    /// Creates one execution view over prevalidated corner-transition segments
    /// followed by a complete terminal plan. Every segment boundary remains
    /// observable so waypoint facts and checkpoint cursors stay deterministic.
    /// </summary>
    internal static ExecutableBoundedTerminalManeuverPlan WaypointRoute(
        IReadOnlyList<AnalyticManeuverSegment> transitions,
        ExecutableBoundedTerminalManeuverPlan terminalPlan)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(terminalPlan);
        if (transitions.Count == 0
            || transitions[^1].EndsAt != terminalPlan.StartsAt
            || transitions[^1].StateAt(transitions[^1].EndsAt)
                != terminalPlan.StateAt(terminalPlan.StartsAt))
        {
            throw new ArgumentException(
                "Waypoint transitions must join the terminal plan at one exact state and timestamp.",
                nameof(transitions));
        }

        ManeuverScheduledPhase[] transitionPhases = transitions
            .Select(static segment => new ManeuverScheduledPhase(
                ManeuverPhaseKind.Accelerate,
                segment.StartsAt,
                segment.EndsAt))
            .ToArray();
        ManeuverScheduledPhase[] phases = transitionPhases
            .Concat(terminalPlan.Phases)
            .ToArray();
        return new ExecutableBoundedTerminalManeuverPlan(
            BoundedTerminalPlanKind.WaypointRoute,
            transitions[0].StartsAt,
            terminalPlan.EndsAt,
            time => StateAtWaypointRoute(transitions, terminalPlan, time),
            phases);
    }

    /// <summary>
    /// Adapts the sole concrete aligned payload and rejects a broken union
    /// invariant rather than publishing a schedule with missing behavior.
    /// </summary>
    internal static ExecutableBoundedTerminalManeuverPlan From(
        AlignedSubCruisePlanSelection selection) =>
        selection switch
        {
            {
                Kind: AlignedSubCruisePlanKind.ImmediateBraking,
                ImmediateBrakingPlan: { } plan,
            } => new(
                BoundedTerminalPlanKind.AlignedSubCruise,
                plan.StartsAt,
                plan.EndsAt,
                plan.StateAt,
                ManeuverPhaseSchedules.From(selection)),
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } plan,
            } => new(
                BoundedTerminalPlanKind.AlignedSubCruise,
                plan.StartsAt,
                plan.EndsAt,
                plan.StateAt,
                ManeuverPhaseSchedules.From(selection)),
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } plan,
            } => new(
                BoundedTerminalPlanKind.AlignedSubCruise,
                plan.StartsAt,
                plan.EndsAt,
                plan.StateAt,
                ManeuverPhaseSchedules.From(selection)),
            _ => throw InvalidPayload(selection.Kind),
        };

    /// <summary>
    /// Evaluates the selected analytic schedule at an inclusive timestamp.
    /// Times outside the complete schedule reject before payload dispatch.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Executable bounded terminal time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return stateAt(time);
    }

    /// <summary>
    /// Preserves the selected analytic evaluator while inserting additional
    /// observable boundaries into its immutable phase schedule.
    /// </summary>
    internal ExecutableBoundedTerminalManeuverPlan WithAdditionalBoundaries(
        IEnumerable<SimulationTime> boundaries)
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        SimulationTime[] ordered = boundaries
            .Distinct()
            .OrderBy(static boundary => boundary)
            .ToArray();
        if (ordered.Any(boundary => boundary <= StartsAt || boundary >= EndsAt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundaries),
                "Additional maneuver boundaries must be strictly inside the complete plan.");
        }

        var split = new List<ManeuverScheduledPhase>(
            Phases.Count + ordered.Length);
        int boundaryIndex = 0;
        foreach (ManeuverScheduledPhase phase in Phases)
        {
            SimulationTime cursor = phase.StartsAt;
            while (boundaryIndex < ordered.Length
                && ordered[boundaryIndex] < phase.EndsAt)
            {
                SimulationTime boundary = ordered[boundaryIndex++];
                if (boundary > cursor)
                {
                    split.Add(new ManeuverScheduledPhase(
                        phase.Kind,
                        cursor,
                        boundary));
                    cursor = boundary;
                }
            }

            split.Add(new ManeuverScheduledPhase(
                phase.Kind,
                cursor,
                phase.EndsAt));
            if (boundaryIndex < ordered.Length
                && ordered[boundaryIndex] == phase.EndsAt)
            {
                boundaryIndex++;
            }
        }

        if (boundaryIndex != ordered.Length)
        {
            throw new InvalidOperationException(
                "An additional maneuver boundary did not intersect the phase schedule.");
        }

        return new ExecutableBoundedTerminalManeuverPlan(
            Kind,
            StartsAt,
            EndsAt,
            stateAt,
            split);
    }

    private static InvalidOperationException InvalidPayload<TKind>(
        TKind kind) =>
        new($"The {kind} bounded terminal outcome has no executable payload.");

    /// <summary>
    /// Dispatches an inclusive boundary to the preceding corner segment, then
    /// delegates every later timestamp to the unchanged terminal evaluator.
    /// </summary>
    private static ShipKinematicState StateAtWaypointRoute(
        IReadOnlyList<AnalyticManeuverSegment> transitions,
        ExecutableBoundedTerminalManeuverPlan terminalPlan,
        SimulationTime time)
    {
        foreach (AnalyticManeuverSegment transition in transitions)
        {
            if (time <= transition.EndsAt)
            {
                return transition.StateAt(time);
            }
        }

        return terminalPlan.StateAt(time);
    }
}
