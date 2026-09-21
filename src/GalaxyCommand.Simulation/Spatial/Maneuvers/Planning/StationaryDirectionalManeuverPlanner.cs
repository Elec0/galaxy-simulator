namespace GalaxyCommand.Simulation;

public enum StationaryDirectionalPlanKind
{
    TerminalSettle,
    PrimarySubCruise,
    TurnThenPrimary,
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
        PrecisionSubCruiseManeuverPlan? precisionSubCruisePlan,
        StationaryDirectionalManeuverPlan? completePlan,
        ManeuverCandidateRank? selectedRank)
    {
        Kind = kind;
        SettledState = settledState;
        PrimarySubCruisePlan = primarySubCruisePlan;
        TurnThenPrimaryPlan = turnThenPrimaryPlan;
        PrecisionSubCruisePlan = precisionSubCruisePlan;
        CompletePlan = completePlan;
        SelectedRank = selectedRank;
    }

    public StationaryDirectionalPlanKind Kind { get; }

    public ShipKinematicState? SettledState { get; }

    public PrimarySubCruiseManeuverPlan? PrimarySubCruisePlan { get; }

    public TurnThenSubCruiseManeuverPlan? TurnThenPrimaryPlan { get; }

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
            plan.PrecisionSubCruisePlan,
            plan,
            rank);

    internal static StationaryDirectionalPlanSelection DirectionalPlanner() =>
        new(
            StationaryDirectionalPlanKind.DirectionalPlanner,
            settledState: null,
            primarySubCruisePlan: null,
            turnThenPrimaryPlan: null,
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
}
