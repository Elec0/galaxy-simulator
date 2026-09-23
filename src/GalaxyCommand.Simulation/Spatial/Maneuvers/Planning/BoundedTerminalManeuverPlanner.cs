namespace GalaxyCommand.Simulation;

public enum BoundedTerminalPlanKind
{
    TerminalSettle,
    StationaryTurn,
    StationaryDirectional,
    CruiseTerminal,
    ForcedCruiseDropoutTerminal,
    MovingAlignedTerminal,
    BrakeThenStationaryDirectional,
    AlignedSubCruise,
    ReducedThrustTerminal,
    WaypointRoute,
    DirectionalPlanner,
    PassiveDrag,
}

/// <summary>
/// Typed result from bounded terminal-goal selection. Concrete outcomes carry
/// exactly one payload; directional handoff carries none.
/// </summary>
public sealed record BoundedTerminalPlanSelection
{
    private BoundedTerminalPlanSelection(
        BoundedTerminalPlanKind kind,
        ShipKinematicState? settledState,
        StationaryTurnManeuverPlan? stationaryTurnPlan,
        StationaryDirectionalPlanSelection? stationaryDirectionalPlan,
        MovingAlignedTerminalManeuverPlan? movingAlignedTerminalPlan,
        BrakeThenStationaryDirectionalManeuverPlan?
            brakeThenStationaryDirectionalPlan,
        AlignedSubCruisePlanSelection? alignedSubCruisePlan,
        ManeuverCandidateRank? selectedRank,
        SimulationTime? terminalSettlesAt,
        CruiseTerminalManeuverPlan? cruiseTerminalPlan = null,
        ForcedCruiseDropoutTerminalManeuverPlan?
            forcedCruiseDropoutTerminalPlan = null,
        ReducedThrustTerminalManeuverPlan? reducedThrustTerminalPlan = null)
    {
        Kind = kind;
        SettledState = settledState;
        StationaryTurnPlan = stationaryTurnPlan;
        StationaryDirectionalPlan = stationaryDirectionalPlan;
        MovingAlignedTerminalPlan = movingAlignedTerminalPlan;
        BrakeThenStationaryDirectionalPlan =
            brakeThenStationaryDirectionalPlan;
        AlignedSubCruisePlan = alignedSubCruisePlan;
        CruiseTerminalPlan = cruiseTerminalPlan;
        ForcedCruiseDropoutTerminalPlan = forcedCruiseDropoutTerminalPlan;
        ReducedThrustTerminalPlan = reducedThrustTerminalPlan;
        SelectedRank = selectedRank;
        TerminalSettlesAt = terminalSettlesAt;
    }

    public BoundedTerminalPlanKind Kind { get; }

    public ShipKinematicState? SettledState { get; }

    public StationaryTurnManeuverPlan? StationaryTurnPlan { get; }

    public StationaryDirectionalPlanSelection? StationaryDirectionalPlan { get; }

    public MovingAlignedTerminalManeuverPlan? MovingAlignedTerminalPlan { get; }

    public BrakeThenStationaryDirectionalManeuverPlan?
        BrakeThenStationaryDirectionalPlan
    { get; }

    public AlignedSubCruisePlanSelection? AlignedSubCruisePlan { get; }

    public CruiseTerminalManeuverPlan? CruiseTerminalPlan { get; }

    public ForcedCruiseDropoutTerminalManeuverPlan?
        ForcedCruiseDropoutTerminalPlan
    { get; }

    public ReducedThrustTerminalManeuverPlan? ReducedThrustTerminalPlan
    { get; }

    public ManeuverCandidateRank? SelectedRank { get; }

    private SimulationTime? TerminalSettlesAt { get; }

    /// <summary>
    /// Creates a common execution view for concrete outcomes. An explicit
    /// directional-planner handoff has no executable schedule.
    /// </summary>
    public ExecutableBoundedTerminalManeuverPlan? ExecutablePlan =>
        Kind switch
        {
            BoundedTerminalPlanKind.TerminalSettle
                when TerminalSettlesAt is { } at
                    && SettledState is { } settled =>
                ExecutableBoundedTerminalManeuverPlan.TerminalSettle(
                    at,
                    settled),
            BoundedTerminalPlanKind.StationaryTurn
                when StationaryTurnPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.StationaryDirectional
                when StationaryDirectionalPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.CruiseTerminal
                when CruiseTerminalPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.ForcedCruiseDropoutTerminal
                when ForcedCruiseDropoutTerminalPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.MovingAlignedTerminal
                when MovingAlignedTerminalPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.BrakeThenStationaryDirectional
                when BrakeThenStationaryDirectionalPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.AlignedSubCruise
                when AlignedSubCruisePlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.ReducedThrustTerminal
                when ReducedThrustTerminalPlan is { } plan =>
                ExecutableBoundedTerminalManeuverPlan.From(plan),
            BoundedTerminalPlanKind.DirectionalPlanner => null,
            _ => throw new InvalidOperationException(
                $"The {Kind} bounded terminal outcome has an invalid payload."),
        };

    internal static BoundedTerminalPlanSelection TerminalSettle(
        SimulationTime at,
        ShipKinematicState settled) =>
        new(
            BoundedTerminalPlanKind.TerminalSettle,
            settled,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            selectedRank: null,
            terminalSettlesAt: at);

    internal static BoundedTerminalPlanSelection StationaryTurn(
        StationaryTurnManeuverPlan plan) =>
        new(
            BoundedTerminalPlanKind.StationaryTurn,
            settledState: null,
            plan,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            selectedRank: null,
            terminalSettlesAt: null);

    internal static BoundedTerminalPlanSelection ReducedThrustTerminal(
        ReducedThrustTerminalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            BoundedTerminalPlanKind.ReducedThrustTerminal,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            rank,
            terminalSettlesAt: null,
            cruiseTerminalPlan: null,
            forcedCruiseDropoutTerminalPlan: null,
            plan);

    internal static BoundedTerminalPlanSelection StationaryDirectional(
        StationaryDirectionalPlanSelection plan) =>
        new(
            BoundedTerminalPlanKind.StationaryDirectional,
            settledState: null,
            stationaryTurnPlan: null,
            plan,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            plan.SelectedRank,
            terminalSettlesAt: null);

    internal static BoundedTerminalPlanSelection MovingAlignedTerminal(
        MovingAlignedTerminalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            BoundedTerminalPlanKind.MovingAlignedTerminal,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            plan,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            rank,
            terminalSettlesAt: null);

    internal static BoundedTerminalPlanSelection CruiseTerminal(
        CruiseTerminalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            BoundedTerminalPlanKind.CruiseTerminal,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            rank,
            terminalSettlesAt: null,
            plan);

    internal static BoundedTerminalPlanSelection ForcedCruiseDropoutTerminal(
        ForcedCruiseDropoutTerminalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            BoundedTerminalPlanKind.ForcedCruiseDropoutTerminal,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            rank,
            terminalSettlesAt: null,
            cruiseTerminalPlan: null,
            plan);

    internal static BoundedTerminalPlanSelection
        BrakeThenStationaryDirectional(
            BrakeThenStationaryDirectionalManeuverPlan plan,
            ManeuverCandidateRank rank) =>
        new(
            BoundedTerminalPlanKind.BrakeThenStationaryDirectional,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            plan,
            alignedSubCruisePlan: null,
            rank,
            terminalSettlesAt: null);

    internal static BoundedTerminalPlanSelection AlignedSubCruise(
        AlignedSubCruisePlanSelection plan) =>
        new(
            BoundedTerminalPlanKind.AlignedSubCruise,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            plan,
            selectedRank: null,
            terminalSettlesAt: null);

    internal static BoundedTerminalPlanSelection DirectionalPlanner() =>
        new(
            BoundedTerminalPlanKind.DirectionalPlanner,
            settledState: null,
            stationaryTurnPlan: null,
            stationaryDirectionalPlan: null,
            movingAlignedTerminalPlan: null,
            brakeThenStationaryDirectionalPlan: null,
            alignedSubCruisePlan: null,
            selectedRank: null,
            terminalSettlesAt: null);
}

/// <summary>
/// Selects the currently implemented bounded terminal plans and makes every
/// unsupported requested-heading or directional case an explicit handoff.
/// </summary>
public static class BoundedTerminalManeuverPlanner
{
    private const long MillimetersPerMeter = 1_000;

    /// <summary>
    /// Selects complete stationary, aligned-moving, or precision-brake
    /// continuation candidates from the resolved capability and objective.
    /// Terminal settle and pure stationary turns retain precedence; moving
    /// states hand off only when no complete bounded schedule is available.
    /// </summary>
    public static BoundedTerminalPlanSelection Select(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (objective is not ManeuverObjective.FastestArrival
            and not ManeuverObjective.ShortestPath)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objective),
                objective,
                "Unknown maneuver objective.");
        }

        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "Terminal maneuver selection requires a destination in the starting system.",
                nameof(destination));
        }

        if (ManeuverArrival.TrySettleTerminal(
                start,
                destination,
                requestedHeading,
                out ShipKinematicState settled))
        {
            return BoundedTerminalPlanSelection.TerminalSettle(
                startsAt,
                settled);
        }

        if (requestedHeading is { } heading
            && StationaryTurnManeuverPlan.TryCreate(
                startsAt,
                start,
                destination,
                heading,
                capability.TurnRate,
                out StationaryTurnManeuverPlan? stationaryTurn)
            && stationaryTurn is not null)
        {
            return BoundedTerminalPlanSelection.StationaryTurn(stationaryTurn);
        }

        if (start.Velocity == ShipVelocity.Zero)
        {
            StationaryCruisePlanSelection comparison =
                StationaryCruiseManeuverPlanner.Select(
                    startsAt,
                    start,
                    destination,
                    requestedHeading,
                    capability,
                    objective);
            if (comparison.Kind == StationaryCruisePlanKind.DirectionalPlanner
                && ReducedThrustTerminalManeuverPlan.TryCreateFromRest(
                    startsAt,
                    start,
                    destination,
                    capability.PrecisionAcceleration,
                    capability.MaximumSubCruiseSpeed,
                    requestedHeading,
                    capability.TurnRate,
                    out ReducedThrustTerminalManeuverPlan? reduced)
                && reduced is not null)
            {
                return BoundedTerminalPlanSelection.ReducedThrustTerminal(
                    reduced,
                    ManeuverPlanRanking.Rank(reduced));
            }

            return comparison.Kind switch
            {
                StationaryCruisePlanKind.TerminalSettle
                    when comparison.SubCruisePlan.SettledState
                        is { } directionalSettled =>
                    BoundedTerminalPlanSelection.TerminalSettle(
                        startsAt,
                        directionalSettled),
                StationaryCruisePlanKind.Cruise
                    when comparison.CruisePlan is { } cruise
                        && comparison.SelectedRank is { } cruiseRank =>
                    BoundedTerminalPlanSelection.CruiseTerminal(
                        cruise,
                        cruiseRank),
                StationaryCruisePlanKind.SubCruise =>
                    BoundedTerminalPlanSelection.StationaryDirectional(
                        comparison.SubCruisePlan),
                StationaryCruisePlanKind.DirectionalPlanner =>
                    BoundedTerminalPlanSelection.DirectionalPlanner(),
                _ => throw new InvalidOperationException(
                    $"The {comparison.Kind} stationary comparison has an invalid payload."),
            };
        }

        if (ForcedCruiseDropoutTerminalManeuverPlan.TryCreate(
                startsAt,
                start,
                destination,
                requestedHeading,
                capability,
                objective,
                out ForcedCruiseDropoutTerminalManeuverPlan? forcedDropout)
            && forcedDropout is not null)
        {
            return BoundedTerminalPlanSelection.ForcedCruiseDropoutTerminal(
                forcedDropout,
                ManeuverPlanRanking.Rank(forcedDropout));
        }

        ManeuverAcceleration acceleration = MovingAlignedAcceleration(
            start,
            destination,
            capability);
        AlignedSubCruisePlanSelection aligned = AlignedSubCruisePlanner.Select(
            startsAt,
            start,
            destination,
            acceleration,
            capability.PrecisionAcceleration,
            capability.MaximumSubCruiseSpeed);
        if (!MovingAlignedTerminalManeuverPlan.TryCreate(
                aligned,
                destination,
                requestedHeading,
                capability.TurnRate,
                out MovingAlignedTerminalManeuverPlan? complete)
            || complete is null)
        {
            return BrakeThenStationaryDirectionalManeuverPlan.TryCreate(
                    startsAt,
                    start,
                    destination,
                    requestedHeading,
                    capability,
                    objective,
                    out BrakeThenStationaryDirectionalManeuverPlan?
                        brakeThenStationary)
                && brakeThenStationary is not null
                ? BoundedTerminalPlanSelection.BrakeThenStationaryDirectional(
                    brakeThenStationary,
                    ManeuverPlanRanking.Rank(brakeThenStationary))
                : BoundedTerminalPlanSelection.DirectionalPlanner();
        }

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(complete);
        if (CruiseTerminalManeuverPlan.TryCreateFromManeuverSpeed(
                startsAt,
                start,
                destination,
                requestedHeading,
                capability,
                out CruiseTerminalManeuverPlan? movingCruise)
            && movingCruise is not null
            && movingCruise.EndsAt < complete.EndsAt)
        {
            return BoundedTerminalPlanSelection.CruiseTerminal(
                movingCruise,
                ManeuverPlanRanking.Rank(movingCruise));
        }

        return BoundedTerminalPlanSelection.MovingAlignedTerminal(
            complete,
            rank);
    }

    /// <summary>
    /// Admits forward primary acceleration only for an exact nonzero CORDIC
    /// course match. Every other heading uses all-direction precision thrust;
    /// a zero-distance moving goal likewise has no course to authorize primary.
    /// </summary>
    private static ManeuverAcceleration MovingAlignedAcceleration(
        ShipKinematicState start,
        SystemPosition destination,
        EffectiveShipManeuverCapability capability)
    {
        if (start.Position == destination)
        {
            return capability.PrecisionAcceleration;
        }

        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            DeltaMillimeters(
                start.Position.Position.X,
                destination.Position.X),
            DeltaMillimeters(
                start.Position.Position.Y,
                destination.Position.Y));
        return start.Heading == course
            ? capability.PrimaryAcceleration
            : capability.PrecisionAcceleration;
    }

    private static long DeltaMillimeters(
        SpatialCoordinate start,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - start.Units)
            * MillimetersPerMeter));

    /// <summary>
    /// Selects terminal settle, an exact-rest stationary turn, or a heading-free
    /// aligned sub-cruise plan for a same-system goal. Combined translation and
    /// requested-heading goals require the directional planner.
    /// </summary>
    public static BoundedTerminalPlanSelection Select(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        ManeuverTurnRate turnRate)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "Terminal maneuver selection requires a destination in the starting system.",
                nameof(destination));
        }

        // Settle uses the complete optional-heading contract before any plan
        // is admitted, preventing an already-satisfied goal from moving again.
        if (ManeuverArrival.TrySettleTerminal(
                start,
                destination,
                requestedHeading,
                out ShipKinematicState settled))
        {
            return BoundedTerminalPlanSelection.TerminalSettle(
                startsAt,
                settled);
        }

        if (requestedHeading is ShipHeading heading)
        {
            return StationaryTurnManeuverPlan.TryCreate(
                    startsAt,
                    start,
                    destination,
                    heading,
                    turnRate,
                    out StationaryTurnManeuverPlan? stationaryTurn)
                && stationaryTurn is not null
                ? BoundedTerminalPlanSelection.StationaryTurn(stationaryTurn)
                : BoundedTerminalPlanSelection.DirectionalPlanner();
        }

        AlignedSubCruisePlanSelection aligned =
            AlignedSubCruisePlanner.Select(
                startsAt,
                start,
                destination,
                acceleration,
                braking,
                maximumSpeed);
        return aligned switch
        {
            {
                Kind: AlignedSubCruisePlanKind.TerminalSettle,
                SettledState: { } alignedSettled,
            } => BoundedTerminalPlanSelection.TerminalSettle(
                startsAt,
                alignedSettled),
            { Kind: AlignedSubCruisePlanKind.DirectionalPlanner } =>
                BoundedTerminalPlanSelection.DirectionalPlanner(),
            _ => BoundedTerminalPlanSelection.AlignedSubCruise(aligned),
        };
    }
}
