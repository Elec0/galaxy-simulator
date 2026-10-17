using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Derived system-local bounds with outward integer rounding and no coordinate overflow.</summary>
internal sealed record InteractionSweptBounds(SystemId System, BigInteger MinX, BigInteger MinY, BigInteger MaxX, BigInteger MaxY)
{
    /// <summary>
    /// Rejects different systems or boxes whose minimum Euclidean separation
    /// exceeds the range. A surviving pair still requires exact narrow-phase
    /// evaluation because bounds discard timing and trajectory correlation.
    /// </summary>
    internal bool MayContact(InteractionSweptBounds other, ulong rangeUnits)
    {
        if (System != other.System) return false;
        BigInteger x = BigInteger.Max(BigInteger.Zero, BigInteger.Max(MinX - other.MaxX, other.MinX - MaxX));
        BigInteger y = BigInteger.Max(BigInteger.Zero, BigInteger.Max(MinY - other.MaxY, other.MinY - MaxY));
        BigInteger radius = rangeUnits;
        return x * x + y * y <= radius * radius;
    }
}

/// <summary>Conservative geometry for future broad-phase discovery, independent of index structure.</summary>
internal static class InteractionSweptBoundsQuery
{
    /// <summary>Bounds exact quadratic extrema and target holds within the known phase overlap; never extrapolates.</summary>
    internal static InteractionSweptBounds? Find(InteractionPhaseCurve curve, SimulationTime from, SimulationTime through)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));
        ulong start = Math.Max(from.Milliseconds, curve.StartsAt.Milliseconds);
        ulong end = Math.Min(through.Milliseconds, curve.EndsAt.Milliseconds);
        if (end < start) return null;
        var origin = new SimulationTime(start);
        InteractionFraction lower = InteractionFraction.Integer(start);
        InteractionFraction upper = InteractionFraction.Integer(end);
        InteractionSweptBounds? bounds = null;
        foreach (InteractionCurvePiece piece in curve.Pieces(origin))
        {
            InteractionFraction a = piece.From > lower ? piece.From : lower;
            InteractionFraction b = piece.Through < upper ? piece.Through : upper;
            if (b < a) continue;
            (BigInteger minX, BigInteger maxX) = Axis(piece.X, piece.Denominator, a - lower, b - lower);
            (BigInteger minY, BigInteger maxY) = Axis(piece.Y, piece.Denominator, a - lower, b - lower);
            bounds = Union(bounds, new InteractionSweptBounds(curve.System, minX, minY, maxX, maxY));
        }
        return bounds;
    }

    /// <summary>
    /// Unions original phase geometry and committed boundary resets. Inserted
    /// schedule events do not reset the curve. Closed bounds may include a
    /// pre-reset endpoint limit, which is safe extra broad-phase coverage.
    /// </summary>
    internal static InteractionSweptBounds? Find(ExecutableBoundedTerminalManeuverPlan plan, SimulationTime from, SimulationTime through)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));
        var start = new SimulationTime(Math.Max(from.Milliseconds, plan.StartsAt.Milliseconds));
        var end = new SimulationTime(Math.Min(through.Milliseconds, plan.EndsAt.Milliseconds));
        if (end < start) return null;
        SimulationTime[] boundaries = plan.InteractionCurves.Select(curve => curve.StartsAt).Append(plan.EndsAt).Distinct().ToArray();
        InteractionSweptBounds? bounds = null;
        foreach (InteractionPhaseCurve curve in plan.InteractionCurves)
        {
            var a = new SimulationTime(Math.Max(start.Milliseconds, curve.StartsAt.Milliseconds));
            var b = new SimulationTime(Math.Min(end.Milliseconds, curve.EndsAt.Milliseconds));
            if (b < a || (a == b && boundaries.Contains(a))) continue;
            if (Find(curve, a, b) is { } phase) bounds = Union(bounds, phase);
        }
        foreach (SimulationTime boundary in boundaries.Where(time => time >= start && time <= end))
        {
            SystemPosition position = plan.StateAt(boundary).Position;
            bounds = Union(bounds, new InteractionSweptBounds(position.SystemId,
                position.Position.X.Units, position.Position.Y.Units, position.Position.X.Units, position.Position.Y.Units));
        }
        return bounds;
    }

    /// <summary>Endpoints and the derivative's single zero bound each exact quadratic axis, including fractional extrema.</summary>
    private static (BigInteger Min, BigInteger Max) Axis(BigInteger[] coefficients, BigInteger denominator,
        InteractionFraction from, InteractionFraction through)
    {
        InteractionFraction first = At(coefficients, denominator, from);
        InteractionFraction last = At(coefficients, denominator, through);
        InteractionFraction min = first < last ? first : last;
        InteractionFraction max = first > last ? first : last;
        if (!coefficients[2].IsZero)
        {
            BigInteger n = -coefficients[1];
            BigInteger d = 2 * coefficients[2];
            if (d.Sign < 0) { n = -n; d = -d; }
            var vertex = new InteractionFraction(n, d);
            if (vertex >= from && vertex <= through)
            {
                InteractionFraction value = At(coefficients, denominator, vertex);
                if (value < min) min = value;
                if (value > max) max = value;
            }
        }
        return (Floor(min), -Floor(new InteractionFraction(-max.Numerator, max.Denominator)));
    }

    private static InteractionFraction At(BigInteger[] coefficients, BigInteger denominator, InteractionFraction time) =>
        new(coefficients[0] * time.Denominator * time.Denominator
            + coefficients[1] * time.Numerator * time.Denominator
            + coefficients[2] * time.Numerator * time.Numerator,
            denominator * time.Denominator * time.Denominator);

    /// <summary>Corrects truncation toward zero so negative fractional coordinates expand outward too.</summary>
    private static BigInteger Floor(InteractionFraction value)
    {
        BigInteger quotient = BigInteger.DivRem(value.Numerator, value.Denominator, out BigInteger remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    /// <summary>Combines derived coverage without narrowing any phase or boundary.</summary>
    private static InteractionSweptBounds Union(InteractionSweptBounds? first, InteractionSweptBounds second) =>
        first is null ? second : new InteractionSweptBounds(second.System,
            BigInteger.Min(first.MinX, second.MinX), BigInteger.Min(first.MinY, second.MinY),
            BigInteger.Max(first.MaxX, second.MaxX), BigInteger.Max(first.MaxY, second.MaxY));
}
