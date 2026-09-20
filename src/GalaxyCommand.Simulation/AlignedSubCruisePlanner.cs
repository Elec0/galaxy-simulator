namespace GalaxyCommand.Simulation;

public enum AlignedSubCruisePlanKind
{
    TerminalSettle,
    ImmediateBraking,
    Triangular,
    CappedSpeed,
    DirectionalPlanner,
}

/// <summary>
/// Typed outcome from aligned sub-cruise selection. Exactly one payload is
/// populated for a concrete plan outcome.
/// </summary>
public sealed record AlignedSubCruisePlanSelection
{
    private AlignedSubCruisePlanSelection(
        AlignedSubCruisePlanKind kind,
        ShipKinematicState? settledState,
        ShortMoveImmediateBrakingPlan? immediateBrakingPlan,
        ShortMoveTriangularPlan? triangularPlan,
        CappedSpeedManeuverPlan? cappedSpeedPlan)
    {
        Kind = kind;
        SettledState = settledState;
        ImmediateBrakingPlan = immediateBrakingPlan;
        TriangularPlan = triangularPlan;
        CappedSpeedPlan = cappedSpeedPlan;
    }

    public AlignedSubCruisePlanKind Kind { get; }

    public ShipKinematicState? SettledState { get; }

    public ShortMoveImmediateBrakingPlan? ImmediateBrakingPlan { get; }

    public ShortMoveTriangularPlan? TriangularPlan { get; }

    public CappedSpeedManeuverPlan? CappedSpeedPlan { get; }

    internal static AlignedSubCruisePlanSelection TerminalSettle(
        ShipKinematicState settled) =>
        new(
            AlignedSubCruisePlanKind.TerminalSettle,
            settled,
            immediateBrakingPlan: null,
            triangularPlan: null,
            cappedSpeedPlan: null);

    internal static AlignedSubCruisePlanSelection ImmediateBraking(
        ShortMoveImmediateBrakingPlan plan) =>
        new(
            AlignedSubCruisePlanKind.ImmediateBraking,
            settledState: null,
            plan,
            triangularPlan: null,
            cappedSpeedPlan: null);

    internal static AlignedSubCruisePlanSelection Triangular(
        ShortMoveTriangularPlan plan) =>
        new(
            AlignedSubCruisePlanKind.Triangular,
            settledState: null,
            immediateBrakingPlan: null,
            plan,
            cappedSpeedPlan: null);

    internal static AlignedSubCruisePlanSelection CappedSpeed(
        CappedSpeedManeuverPlan plan) =>
        new(
            AlignedSubCruisePlanKind.CappedSpeed,
            settledState: null,
            immediateBrakingPlan: null,
            triangularPlan: null,
            plan);

    internal static AlignedSubCruisePlanSelection DirectionalPlanner() =>
        new(
            AlignedSubCruisePlanKind.DirectionalPlanner,
            settledState: null,
            immediateBrakingPlan: null,
            triangularPlan: null,
            cappedSpeedPlan: null);
}

/// <summary>
/// Deterministic selector for aligned sub-cruise motion with an explicit
/// handoff when directional planning is required.
/// </summary>
public static class AlignedSubCruisePlanner
{
    /// <summary>
    /// Selects terminal settle, immediate braking, triangular motion, or
    /// capped-speed motion in that order for a same-system destination.
    /// Requests outside those aligned contracts explicitly require the
    /// directional planner.
    /// </summary>
    public static AlignedSubCruisePlanSelection Select(
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
                "Aligned sub-cruise selection requires a destination in the starting system.",
                nameof(destination));
        }

        ShortMovePlanSelection shortMove = ShortMovePlanner.SelectAligned(
            startsAt,
            start,
            destination,
            acceleration,
            braking,
            maximumSpeed);
        switch (shortMove)
        {
            case
            {
                Kind: ShortMovePlanKind.TerminalSettle,
                SettledState: { } settled,
            }:
                return AlignedSubCruisePlanSelection.TerminalSettle(settled);
            case
            {
                Kind: ShortMovePlanKind.ImmediateBraking,
                ImmediateBrakingPlan: { } immediate,
            }:
                return AlignedSubCruisePlanSelection.ImmediateBraking(immediate);
            case
            {
                Kind: ShortMovePlanKind.Triangular,
                TriangularPlan: { } triangular,
            }:
                return AlignedSubCruisePlanSelection.Triangular(triangular);
            case { Kind: ShortMovePlanKind.GeneralPlanner }:
                break;
            default:
                throw new InvalidOperationException(
                    "Short-move selection returned an invalid result payload.");
        }

        // Capped-speed motion fills only the aligned long-move case left by
        // short-move selection. Every other request retains an explicit handoff.
        return CappedSpeedManeuverPlan.TryCreateAligned(
                startsAt,
                start,
                destination,
                acceleration,
                braking,
                maximumSpeed,
                out CappedSpeedManeuverPlan? cappedSpeed)
            && cappedSpeed is not null
            ? AlignedSubCruisePlanSelection.CappedSpeed(cappedSpeed)
            : AlignedSubCruisePlanSelection.DirectionalPlanner();
    }
}
