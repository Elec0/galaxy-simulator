namespace GalaxyCommand.Simulation;

public enum ShortMovePlanKind
{
    TerminalSettle,
    Triangular,
    ImmediateBraking,
    GeneralPlanner,
}

/// <summary>
/// Typed outcome from the bounded aligned short-move planner. Exactly one
/// result payload is populated for a concrete short-move outcome.
/// </summary>
public sealed record ShortMovePlanSelection
{
    private ShortMovePlanSelection(
        ShortMovePlanKind kind,
        ShipKinematicState? settledState,
        ShortMoveTriangularPlan? triangularPlan,
        ShortMoveImmediateBrakingPlan? immediateBrakingPlan)
    {
        Kind = kind;
        SettledState = settledState;
        TriangularPlan = triangularPlan;
        ImmediateBrakingPlan = immediateBrakingPlan;
    }

    public ShortMovePlanKind Kind { get; }

    public ShipKinematicState? SettledState { get; }

    public ShortMoveTriangularPlan? TriangularPlan { get; }

    public ShortMoveImmediateBrakingPlan? ImmediateBrakingPlan { get; }

    internal static ShortMovePlanSelection TerminalSettle(
        ShipKinematicState settled) =>
        new(
            ShortMovePlanKind.TerminalSettle,
            settled,
            triangularPlan: null,
            immediateBrakingPlan: null);

    internal static ShortMovePlanSelection Triangular(
        ShortMoveTriangularPlan plan) =>
        new(
            ShortMovePlanKind.Triangular,
            settledState: null,
            plan,
            immediateBrakingPlan: null);

    internal static ShortMovePlanSelection ImmediateBraking(
        ShortMoveImmediateBrakingPlan plan) =>
        new(
            ShortMovePlanKind.ImmediateBraking,
            settledState: null,
            triangularPlan: null,
            plan);

    internal static ShortMovePlanSelection GeneralPlanner() =>
        new(
            ShortMovePlanKind.GeneralPlanner,
            settledState: null,
            triangularPlan: null,
            immediateBrakingPlan: null);
}

/// <summary>
/// Bounded deterministic selector for aligned terminal moves that can settle,
/// use a triangular profile, or begin braking without general plan search.
/// </summary>
public static class ShortMovePlanner
{
    /// <summary>
    /// Selects one short-move outcome for a same-system destination. Selection
    /// order is terminal settle, immediate braking, then acceleration-first
    /// triangular motion. Requests outside those bounded contracts explicitly
    /// return <see cref="ShortMovePlanKind.GeneralPlanner"/>.
    /// </summary>
    public static ShortMovePlanSelection SelectAligned(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "Short-move selection requires a destination in the starting system.",
                nameof(destination));
        }

        // Settle first so a state inside every tolerance cannot be pushed back
        // into motion by a later profile check.
        if (ManeuverArrival.TrySettleTerminal(
                start,
                destination,
                requestedHeading: null,
                out ShipKinematicState settled))
        {
            return ShortMovePlanSelection.TerminalSettle(settled);
        }

        if (ShortMoveImmediateBrakingPlan.TryCreateAligned(
                startsAt,
                start,
                destination,
                acceleration,
                braking,
                maximumSpeed,
                out ShortMoveImmediateBrakingPlan? immediate)
            && immediate is not null)
        {
            return ShortMovePlanSelection.ImmediateBraking(immediate);
        }

        if (ShortMoveTriangularPlan.TryCreateAligned(
                startsAt,
                start,
                destination,
                acceleration,
                braking,
                maximumSpeed,
                out ShortMoveTriangularPlan? triangular)
            && triangular is not null)
        {
            return ShortMovePlanSelection.Triangular(triangular);
        }

        return ShortMovePlanSelection.GeneralPlanner();
    }
}
