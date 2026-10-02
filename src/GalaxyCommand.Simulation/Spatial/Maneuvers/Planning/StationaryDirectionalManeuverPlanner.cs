namespace GalaxyCommand.Simulation;

public enum StationaryDirectionalPlanKind
{
    TerminalSettle,
    PrimarySubCruise,
    TurnThenPrimary,
    TurnThenPrecisionSubCruise,
    PrecisionSubCruise,
    DirectionalPlanner,
}

/// <summary>
/// Typed result from stationary directional candidate construction and
/// objective-based reduction. Concrete outcomes carry exactly one plan.
/// </summary>
public sealed record StationaryDirectionalPlanSelection
{
    private StationaryDirectionalPlanSelection(
        StationaryDirectionalPlanKind kind,
        ShipKinematicState? settledState,
        PrimarySubCruiseManeuverPlan? primarySubCruisePlan,
        TurnThenSubCruiseManeuverPlan? turnThenPrimaryPlan,
        TurnThenPrecisionSubCruiseManeuverPlan? turnThenPrecisionPlan,
        PrecisionSubCruiseManeuverPlan? precisionSubCruisePlan,
        StationaryDirectionalManeuverPlan? completePlan,
        ManeuverCandidateRank? selectedRank)
    {
        Kind = kind;
        SettledState = settledState;
        PrimarySubCruisePlan = primarySubCruisePlan;
        TurnThenPrimaryPlan = turnThenPrimaryPlan;
        TurnThenPrecisionPlan = turnThenPrecisionPlan;
        PrecisionSubCruisePlan = precisionSubCruisePlan;
        CompletePlan = completePlan;
        SelectedRank = selectedRank;
    }

    public StationaryDirectionalPlanKind Kind { get; }

    public ShipKinematicState? SettledState { get; }

    public PrimarySubCruiseManeuverPlan? PrimarySubCruisePlan { get; }

    public TurnThenSubCruiseManeuverPlan? TurnThenPrimaryPlan { get; }

    public TurnThenPrecisionSubCruiseManeuverPlan? TurnThenPrecisionPlan { get; }

    public PrecisionSubCruiseManeuverPlan? PrecisionSubCruisePlan { get; }

    public StationaryDirectionalManeuverPlan? CompletePlan { get; }

    public ManeuverCandidateRank? SelectedRank { get; }

    internal static StationaryDirectionalPlanSelection TerminalSettle(
        ShipKinematicState settled) =>
        new(
            StationaryDirectionalPlanKind.TerminalSettle,
            settled,
            primarySubCruisePlan: null,
            turnThenPrimaryPlan: null,
            turnThenPrecisionPlan: null,
            precisionSubCruisePlan: null,
            completePlan: null,
            selectedRank: null);

    internal static StationaryDirectionalPlanSelection PrimarySubCruise(
        StationaryDirectionalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            StationaryDirectionalPlanKind.PrimarySubCruise,
            settledState: null,
            plan.PrimarySubCruisePlan,
            turnThenPrimaryPlan: null,
            turnThenPrecisionPlan: null,
            precisionSubCruisePlan: null,
            plan,
            rank);

    internal static StationaryDirectionalPlanSelection TurnThenPrimary(
        StationaryDirectionalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            StationaryDirectionalPlanKind.TurnThenPrimary,
            settledState: null,
            primarySubCruisePlan: null,
            plan.TurnThenPrimaryPlan,
            turnThenPrecisionPlan: null,
            precisionSubCruisePlan: null,
            plan,
            rank);

    internal static StationaryDirectionalPlanSelection TurnThenPrecision(
        StationaryDirectionalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            StationaryDirectionalPlanKind.TurnThenPrecisionSubCruise,
            settledState: null,
            primarySubCruisePlan: null,
            turnThenPrimaryPlan: null,
            plan.TurnThenPrecisionPlan,
            precisionSubCruisePlan: null,
            plan,
            rank);

    internal static StationaryDirectionalPlanSelection PrecisionSubCruise(
        StationaryDirectionalManeuverPlan plan,
        ManeuverCandidateRank rank) =>
        new(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            settledState: null,
            primarySubCruisePlan: null,
            turnThenPrimaryPlan: null,
            turnThenPrecisionPlan: null,
            plan.PrecisionSubCruisePlan,
            plan,
            rank);

    internal static StationaryDirectionalPlanSelection DirectionalPlanner() =>
        new(
            StationaryDirectionalPlanKind.DirectionalPlanner,
            settledState: null,
            primarySubCruisePlan: null,
            turnThenPrimaryPlan: null,
            turnThenPrecisionPlan: null,
            precisionSubCruisePlan: null,
            completePlan: null,
            selectedRank: null);
}

/// <summary>
/// Constructs bounded stationary candidates from one stable state, ranks them
/// under the requested objective, and returns one typed deterministic result.
/// </summary>
public static class StationaryDirectionalManeuverPlanner
{

    // Temporary presentation and maneuver policy until a future ship-size
    // model supplies the course-turn threshold.
    private const long MinimumCourseTurnDistanceMeters = 50;

    /// <summary>
    /// Selects terminal settle, exact-course primary travel, turn-then-primary
    /// travel, or heading-preserving precision travel from exact rest. Primary
    /// candidates accelerate at the primary rate and brake at the precision
    /// rate. Unsupported discrete schedules retain an explicit handoff.
    /// </summary>
    public static StationaryDirectionalPlanSelection Select(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective) =>
        Select(
            startsAt,
            start,
            destination,
            requestedHeading: null,
            capability,
            objective);

    /// <summary>
    /// Selects and ranks complete stationary directional candidates including
    /// any final stationary turn required by the terminal heading. Candidate
    /// ranking therefore compares complete arrival schedules.
    /// </summary>
    public static StationaryDirectionalPlanSelection Select(
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
                "Stationary directional selection requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "Stationary directional selection requires zero starting velocity.",
                nameof(start));
        }

        if (ManeuverArrival.TrySettleTerminal(
                start,
                destination,
                requestedHeading,
                out ShipKinematicState settled))
        {
            return StationaryDirectionalPlanSelection.TerminalSettle(settled);
        }

        PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
            startsAt,
            start,
            destination,
            capability,
            out PrecisionSubCruiseManeuverPlan? precision);
        PrimarySubCruiseManeuverPlan.TryCreateFromRest(
            startsAt,
            start,
            destination,
            capability,
            out PrimarySubCruiseManeuverPlan? primary);

        TurnThenSubCruiseManeuverPlan? turnThenPrimary = null;
        if (primary is null)
        {
            TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
                startsAt,
                start,
                destination,
                capability.PrimaryAcceleration,
                capability.PrecisionAcceleration,
                capability.MaximumSubCruiseSpeed,
                capability.TurnRate,
                out turnThenPrimary);
        }

        StationaryDirectionalManeuverPlan? primaryComplete = null;
        if (primary is not null)
        {
            StationaryDirectionalManeuverPlan.TryCreate(
                primary,
                destination,
                requestedHeading,
                capability.TurnRate,
                out primaryComplete);
        }
        else if (turnThenPrimary is not null)
        {
            StationaryDirectionalManeuverPlan.TryCreate(
                turnThenPrimary,
                destination,
                requestedHeading,
                capability.TurnRate,
                out primaryComplete);
        }

        StationaryDirectionalManeuverPlan? precisionComplete = null;
        if (precision is not null)
        {
            StationaryDirectionalManeuverPlan.TryCreate(
                precision,
                destination,
                requestedHeading,
                capability.TurnRate,
                out precisionComplete);
        }

        StationaryDirectionalManeuverPlan? turnThenPrecisionComplete = null;
        if (RequiresCourseTurn(start, destination)
            && primaryComplete is null
            && TurnThenPrecisionSubCruiseManeuverPlan.TryCreateFromRest(
                startsAt,
                start,
                destination,
                capability,
                out TurnThenPrecisionSubCruiseManeuverPlan? turnThenPrecision)
            && turnThenPrecision is not null)
        {
            StationaryDirectionalManeuverPlan.TryCreate(
                turnThenPrecision,
                destination,
                requestedHeading,
                capability.TurnRate,
                out turnThenPrecisionComplete);
        }

        if (RequiresCourseTurn(start, destination))
        {
            if (primaryComplete is not null)
            {
                ManeuverCandidateRank requiredTurnRank = ManeuverPlanRanking.Rank(
                    primaryComplete);
                return primaryComplete.Kind
                    == StationaryDirectionalPlanKind.PrimarySubCruise
                    ? StationaryDirectionalPlanSelection.PrimarySubCruise(
                        primaryComplete,
                        requiredTurnRank)
                    : StationaryDirectionalPlanSelection.TurnThenPrimary(
                        primaryComplete,
                        requiredTurnRank);
            }

            if (turnThenPrecisionComplete is not null)
            {
                return StationaryDirectionalPlanSelection.TurnThenPrecision(
                    turnThenPrecisionComplete,
                    ManeuverPlanRanking.Rank(turnThenPrecisionComplete));
            }

            return StationaryDirectionalPlanSelection.DirectionalPlanner();
        }

        ManeuverCandidateRank? primaryRank = primaryComplete is not null
            ? ManeuverPlanRanking.Rank(primaryComplete)
            : null;
        ManeuverCandidateRank? precisionRank = precisionComplete is not null
            ? ManeuverPlanRanking.Rank(precisionComplete)
            : null;
        if (primaryRank is null && precisionRank is null)
        {
            return StationaryDirectionalPlanSelection.DirectionalPlanner();
        }

        ManeuverCandidateRank preferred = primaryRank is null
            ? precisionRank!
            : precisionRank is null
                ? primaryRank
                : ManeuverCandidateSelection.SelectPreferred(
                    objective,
                    [primaryRank, precisionRank]);
        if (primaryRank is not null && preferred == primaryRank)
        {
            return primaryComplete!.Kind
                == StationaryDirectionalPlanKind.PrimarySubCruise
                ? StationaryDirectionalPlanSelection.PrimarySubCruise(
                    primaryComplete,
                    primaryRank)
                : StationaryDirectionalPlanSelection.TurnThenPrimary(
                    primaryComplete,
                    primaryRank);
        }

        return StationaryDirectionalPlanSelection.PrecisionSubCruise(
            precisionComplete!,
            precisionRank!);
    }

    /// <summary>
    /// Keeps sub-50-meter precision adjustments heading-preserving while
    /// requiring longer off-heading moves to establish a visible course.
    /// </summary>
    private static bool RequiresCourseTurn(
        ShipKinematicState start,
        SystemPosition destination)
    {
        Int128 x = (Int128)destination.Position.X.Units
            - start.Position.Position.X.Units;
        Int128 y = (Int128)destination.Position.Y.Units
            - start.Position.Position.Y.Units;
        Int128 thresholdSquared = (Int128)MinimumCourseTurnDistanceMeters
            * MinimumCourseTurnDistanceMeters;
        if ((x * x) + (y * y) < thresholdSquared)
        {
            return false;
        }

        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            checked((long)(x * 1_000)),
            checked((long)(y * 1_000)));
        return start.Heading != course;
    }
}
