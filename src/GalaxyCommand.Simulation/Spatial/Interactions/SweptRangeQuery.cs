using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Derived first-contact evidence. A swept contact may have ended before its
/// rounded timestamp; it does not establish gameplay eligibility or effects.
/// </summary>
public sealed record SweptRangeContact(
    SimulationTime Timestamp,
    bool InRangeAtTimestamp,
    bool AlreadyInRange);

/// <summary>
/// Read-only range evaluation for bounded, system-local linear trajectories.
/// </summary>
public static class SweptRangeQuery
{
    /// <summary>
    /// Returns first contact within the closed horizon and both known motion
    /// intervals, or null for no contact. Entry rounds upward to a millisecond.
    /// Different systems cannot contact; a reversed horizon is rejected.
    /// </summary>
    public static SweptRangeContact? FindFirstContact(
        LocalMotionSegment first, LocalMotionSegment second,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ValidateHorizon(from, through);
        if (first.Origin.SystemId != second.Origin.SystemId)
        {
            return null;
        }

        ulong start = Math.Max(from.Milliseconds,
            Math.Max(first.DepartedAt.Milliseconds, second.DepartedAt.Milliseconds));
        ulong end = Math.Min(through.Milliseconds,
            Math.Min(first.ArrivesAt.Milliseconds, second.ArrivesAt.Milliseconds));
        if (end < start)
        {
            return null;
        }

        return Evaluate(TrajectoryAt(first, start), TrajectoryAt(second, start),
            start, end, rangeUnits);
    }

    /// <summary>
    /// Returns first contact with a stationary position within the closed
    /// horizon and known motion interval, without extrapolating the motion.
    /// Different systems cannot contact; a reversed horizon is rejected.
    /// </summary>
    public static SweptRangeContact? FindFirstContact(
        LocalMotionSegment motion, SystemPosition stationary,
        SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentOutOfRangeException.ThrowIfZero(stationary.SystemId.Value);
        ValidateHorizon(from, through);
        if (motion.Origin.SystemId != stationary.SystemId)
        {
            return null;
        }

        ulong start = Math.Max(from.Milliseconds, motion.DepartedAt.Milliseconds);
        ulong end = Math.Min(through.Milliseconds, motion.ArrivesAt.Milliseconds);
        if (end < start)
        {
            return null;
        }

        var fixedTrajectory = new ScaledTrajectory(
            stationary.Position.X.Units, stationary.Position.Y.Units, 0, 0, 1);
        return Evaluate(TrajectoryAt(motion, start), fixedTrajectory,
            start, end, rangeUnits);
    }

    /// <summary>Rejects an inverted interval rather than silently swapping query intent.</summary>
    private static void ValidateHorizon(SimulationTime from, SimulationTime through)
    {
        if (through < from)
        {
            throw new ArgumentOutOfRangeException(nameof(through),
                "A swept-query horizon cannot precede its start.");
        }
    }

    /// <summary>
    /// Retains rational position and velocity at the clipped start without
    /// materializing rounded coordinates or overflowing coordinate differences.
    /// </summary>
    private static ScaledTrajectory TrajectoryAt(LocalMotionSegment motion, ulong start)
    {
        BigInteger duration = motion.ArrivesAt.Milliseconds - motion.DepartedAt.Milliseconds;
        BigInteger elapsed = start - motion.DepartedAt.Milliseconds;
        BigInteger dx = (BigInteger)motion.Destination.Position.X.Units - motion.Origin.Position.X.Units;
        BigInteger dy = (BigInteger)motion.Destination.Position.Y.Units - motion.Origin.Position.Y.Units;
        return new ScaledTrajectory(
            motion.Origin.Position.X.Units * duration + dx * elapsed,
            motion.Origin.Position.Y.Units * duration + dy * elapsed,
            dx, dy, duration);
    }

    /// <summary>
    /// Finds the first integer timestamp whose continuous prefix contains
    /// contact. Prefix contact is monotone even when instantaneous range is not.
    /// </summary>
    private static SweptRangeContact? Evaluate(
        ScaledTrajectory first, ScaledTrajectory second,
        ulong start, ulong end, ulong rangeUnits)
    {
        BigInteger x = first.X * second.Denominator - second.X * first.Denominator;
        BigInteger y = first.Y * second.Denominator - second.Y * first.Denominator;
        BigInteger vx = first.Vx * second.Denominator - second.Vx * first.Denominator;
        BigInteger vy = first.Vy * second.Denominator - second.Vy * first.Denominator;
        BigInteger radius = rangeUnits * first.Denominator * second.Denominator;
        var distance = new RangePolynomial(
            vx * vx + vy * vy, x * vx + y * vy,
            x * x + y * y - radius * radius);
        if (distance.C <= 0)
        {
            return new SweptRangeContact(new SimulationTime(start), true, true);
        }

        ulong upper = end - start;
        if (!distance.HasContactThrough(upper))
        {
            return null;
        }

        // Search continuous prefixes, not sampled positions: a fast pass or
        // tangent can enter and leave range entirely between two milliseconds.
        ulong lower = 0;
        while (lower < upper)
        {
            ulong middle = lower + (upper - lower) / 2;
            if (distance.HasContactThrough(middle))
            {
                upper = middle;
            }
            else
            {
                lower = middle + 1;
            }
        }

        return new SweptRangeContact(new SimulationTime(start + lower),
            distance.At(lower) <= 0, false);
    }

    private readonly record struct ScaledTrajectory(
        BigInteger X, BigInteger Y, BigInteger Vx, BigInteger Vy, BigInteger Denominator);

    // Squared distance minus squared range, scaled by a shared denominator:
    // q(t) = A*t*t + 2*B*t + C. A is nonnegative by construction.
    private readonly record struct RangePolynomial(BigInteger A, BigInteger B, BigInteger C)
    {
        public BigInteger At(ulong elapsed) => (A * elapsed + 2 * B) * elapsed + C;

        /// <summary>
        /// Tests the exact minimum on [0, elapsed]. When the vertex is inside
        /// the interval, compare its rational value without division or roots.
        /// </summary>
        public bool HasContactThrough(ulong elapsed)
        {
            if (C <= 0)
            {
                return true;
            }

            if (A.IsZero || B >= 0)
            {
                return false;
            }

            return -B <= A * elapsed
                ? A * C <= B * B
                : At(elapsed) <= 0;
        }
    }
}
