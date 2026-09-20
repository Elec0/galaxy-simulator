namespace GalaxyCommand.Simulation;

/// <summary>
/// Independent terminal-arrival checks retained for diagnostics and planner
/// decisions.
/// </summary>
public readonly record struct ManeuverArrivalEvaluation(
    bool PositionSatisfied,
    bool VelocitySatisfied,
    bool HeadingSatisfied)
{
    public bool IsSatisfied =>
        PositionSatisfied && VelocitySatisfied && HeadingSatisfied;
}

/// <summary>
/// Deterministic arrival and terminal-settle policy for ordinary movement.
/// </summary>
public static class ManeuverArrival
{
    public const ulong ArrivalPositionToleranceMeters = 1;
    public const ulong ArrivalSpeedToleranceMetersPerSecond = 1;
    public const int ArrivalHeadingToleranceDegrees = 1;

    private const ulong SpeedToleranceMillimetersPerSecond =
        ArrivalSpeedToleranceMetersPerSecond * 1_000;
    private const int HeadingToleranceMillidegrees =
        ArrivalHeadingToleranceDegrees * 1_000;

    /// <summary>
    /// Evaluates terminal position, zero-velocity, and optional heading
    /// tolerances without changing authoritative state.
    /// </summary>
    public static ManeuverArrivalEvaluation EvaluateTerminal(
        ShipKinematicState current,
        SystemPosition destination,
        ShipHeading? requestedHeading)
    {
        bool positionSatisfied = IsPositionWithinArrivalTolerance(
            current.Position,
            destination);
        bool velocitySatisfied = IsVectorWithinTolerance(
            current.Velocity.MillimetersPerSecondX,
            current.Velocity.MillimetersPerSecondY,
            SpeedToleranceMillimetersPerSecond);
        bool headingSatisfied = requestedHeading is not { } heading
            || Math.Abs(current.Heading.ShortestTurnTo(heading))
                <= HeadingToleranceMillidegrees;
        return new ManeuverArrivalEvaluation(
            positionSatisfied,
            velocitySatisfied,
            headingSatisfied);
    }

    /// <summary>
    /// Commits exact zero velocity only after all terminal tolerances pass.
    /// Position and heading remain at their materialized values. Failure
    /// returns the original state unchanged.
    /// </summary>
    public static bool TrySettleTerminal(
        ShipKinematicState current,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        out ShipKinematicState settled)
    {
        if (!EvaluateTerminal(
                current,
                destination,
                requestedHeading).IsSatisfied)
        {
            settled = current;
            return false;
        }

        settled = current with { Velocity = ShipVelocity.Zero };
        return true;
    }

    /// <summary>
    /// Evaluates a queued fly-through waypoint using only its positional
    /// tolerance. Velocity and heading do not gate continuation.
    /// </summary>
    public static bool IsFlyThroughWaypointReached(
        ShipKinematicState current,
        SystemPosition destination) =>
        IsPositionWithinArrivalTolerance(current.Position, destination);

    /// <summary>
    /// Evaluates only the ordinary Euclidean position tolerance. This supports
    /// boundaries such as connector entry that consume a completed terminal
    /// approach without requiring velocity or heading constraints again.
    /// </summary>
    public static bool IsPositionWithinArrivalTolerance(
        SystemPosition current,
        SystemPosition destination) =>
        current.SystemId == destination.SystemId
        && IsVectorWithinTolerance(
            (Int128)current.Position.X.Units - destination.Position.X.Units,
            (Int128)current.Position.Y.Units - destination.Position.Y.Units,
            ArrivalPositionToleranceMeters);

    /// <summary>
    /// Compares squared Euclidean magnitude after a component bound prevents
    /// overflow and quickly rejects vectors already outside the tolerance.
    /// </summary>
    private static bool IsVectorWithinTolerance(
        Int128 x,
        Int128 y,
        ulong tolerance)
    {
        UInt128 xMagnitude = Magnitude(x);
        UInt128 yMagnitude = Magnitude(y);
        if (xMagnitude > tolerance || yMagnitude > tolerance)
        {
            return false;
        }

        UInt128 limit = tolerance;
        return xMagnitude * xMagnitude + yMagnitude * yMagnitude
            <= limit * limit;
    }

    /// <summary>
    /// Produces an unsigned magnitude without negating the minimum signed
    /// value, preserving checked behavior across the full input range.
    /// </summary>
    private static UInt128 Magnitude(Int128 value) =>
        value < 0
            ? (UInt128)(-(value + 1)) + 1
            : (UInt128)value;
}
