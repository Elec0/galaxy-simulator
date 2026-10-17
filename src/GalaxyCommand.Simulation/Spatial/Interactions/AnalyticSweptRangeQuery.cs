using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Read-only exact range queries over known acceleration and braking phase curves.</summary>
public static class AnalyticSweptRangeQuery
{
    /// <summary>Searches rational braking and its exact final target-position hold against a stationary ship.</summary>
    public static SweptRangeContact? FindFirstContact(DecelerationManeuverSegment motion, SystemPosition stationary,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentOutOfRangeException.ThrowIfZero(stationary.SystemId.Value);
        ValidateHorizon(from, through);
        return InteractionPhaseRangeQuery.Find(InteractionPhaseCurve.From(motion),
            InteractionPhaseCurve.Fixed(stationary, from, through), from, through, rangeUnits);
    }
    /// <summary>Searches exact braking against acceleration, including contacts during the fractional target-position hold.</summary>
    public static SweptRangeContact? FindFirstContact(DecelerationManeuverSegment first, AnalyticManeuverSegment second,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return InteractionPhaseRangeQuery.Find(InteractionPhaseCurve.From(first), InteractionPhaseCurve.From(second),
            from, through, rangeUnits);
    }
    /// <summary>Searches two rational braking phases without extending either beyond its exact target-speed point.</summary>
    public static SweptRangeContact? FindFirstContact(DecelerationManeuverSegment first, DecelerationManeuverSegment second,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return InteractionPhaseRangeQuery.Find(InteractionPhaseCurve.From(first), InteractionPhaseCurve.From(second),
            from, through, rangeUnits);
    }
    private const long PositionDenominator = 2_000_000_000;

    /// <summary>
    /// Searches the closed overlap of two phase curves and the requested horizon.
    /// Contact follows unrounded kinematics and rounds its entry time upward.
    /// Different systems return no contact; no phase is extrapolated. This query
    /// does not apply committed endpoint resets or infer later maneuver phases.
    /// </summary>
    public static SweptRangeContact? FindFirstContact(AnalyticManeuverSegment first, AnalyticManeuverSegment second,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ValidateHorizon(from, through);
        if (first.Start.Position.SystemId != second.Start.Position.SystemId) return null;
        ulong start = Math.Max(from.Milliseconds, Math.Max(first.StartsAt.Milliseconds, second.StartsAt.Milliseconds));
        ulong end = Math.Min(through.Milliseconds, Math.Min(first.EndsAt.Milliseconds, second.EndsAt.Milliseconds));
        if (end < start) return null;
        return Evaluate(CurveAt(first, start), CurveAt(second, start), start, end, rangeUnits);
    }

    /// <summary>
    /// Searches one unrounded phase curve against a fixed system-local position.
    /// The horizon is clipped to the known phase and fractional entry rounds up,
    /// retaining swept evidence even when contact ends before that millisecond.
    /// </summary>
    public static SweptRangeContact? FindFirstContact(AnalyticManeuverSegment motion, SystemPosition stationary,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentOutOfRangeException.ThrowIfZero(stationary.SystemId.Value);
        ValidateHorizon(from, through);
        if (motion.Start.Position.SystemId != stationary.SystemId) return null;
        ulong start = Math.Max(from.Milliseconds, motion.StartsAt.Milliseconds);
        ulong end = Math.Min(through.Milliseconds, motion.EndsAt.Milliseconds);
        if (end < start) return null;
        var fixedCurve = new ScaledCurve(
            [(BigInteger)stationary.Position.X.Units * PositionDenominator, 0, 0],
            [(BigInteger)stationary.Position.Y.Units * PositionDenominator, 0, 0]);
        return Evaluate(CurveAt(motion, start), fixedCurve, start, end, rangeUnits);
    }

    private static void ValidateHorizon(SimulationTime from, SimulationTime through) =>
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));

    private static ScaledCurve CurveAt(AnalyticManeuverSegment motion, ulong start) => new(
        Component(motion.Start.Position.Position.X.Units, motion.Start.Velocity.MillimetersPerSecondX,
            motion.Acceleration.MillimetersPerSecondSquaredX, start - motion.StartsAt.Milliseconds),
        Component(motion.Start.Position.Position.Y.Units, motion.Start.Velocity.MillimetersPerSecondY,
            motion.Acceleration.MillimetersPerSecondSquaredY, start - motion.StartsAt.Milliseconds));

    private static BigInteger[] Component(long position, long velocity, long acceleration, ulong elapsed)
    {
        BigInteger time = elapsed;
        BigInteger rate = acceleration;
        BigInteger speed = (BigInteger)velocity * 2_000;
        return [(BigInteger)position * PositionDenominator + speed * time + rate * time * time,
            speed + 2 * rate * time, rate];
    }

    /// <summary>Squares relative quadratic coordinates to form an exact quartic range predicate.</summary>
    private static SweptRangeContact? Evaluate(ScaledCurve first, ScaledCurve second, ulong start, ulong end, ulong rangeUnits)
    {
        BigInteger[] x = first.X.Zip(second.X, static (a, b) => a - b).ToArray();
        BigInteger[] y = first.Y.Zip(second.Y, static (a, b) => a - b).ToArray();
        var predicate = new BigInteger[5];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
                predicate[row + column] += x[row] * x[column] + y[row] * y[column];
        }
        BigInteger radius = (BigInteger)rangeUnits * PositionDenominator;
        predicate[0] -= radius * radius;
        var origin = new SimulationTime(start);
        return PolynomialRangeQuery.FindFirstContact(predicate, origin, origin, new SimulationTime(end));
    }

    private sealed record ScaledCurve(BigInteger[] X, BigInteger[] Y);
}
