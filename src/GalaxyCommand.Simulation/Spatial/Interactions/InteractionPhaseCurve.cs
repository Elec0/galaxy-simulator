using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Immutable rational phase geometry, optionally followed by an exact target-position hold.</summary>
internal sealed class InteractionPhaseCurve
{
    private readonly BigInteger[] _x;
    private readonly BigInteger[] _y;
    private readonly BigInteger _denominator;
    private readonly InteractionFraction? _holdAt;

    private InteractionPhaseCurve(SystemId system, SimulationTime start, SimulationTime end,
        BigInteger[] x, BigInteger[] y, BigInteger denominator, InteractionFraction? holdAt = null)
    {
        System = system;
        StartsAt = start;
        EndsAt = end;
        _x = x;
        _y = y;
        _denominator = denominator;
        _holdAt = holdAt;
    }

    internal SystemId System { get; }
    internal SimulationTime StartsAt { get; }
    internal SimulationTime EndsAt { get; }

    internal static InteractionPhaseCurve From(AnalyticManeuverSegment segment) => new(
        segment.Start.Position.SystemId, segment.StartsAt, segment.EndsAt,
        Accelerating(segment.Start.Position.Position.X.Units, segment.Start.Velocity.MillimetersPerSecondX,
            segment.Acceleration.MillimetersPerSecondSquaredX),
        Accelerating(segment.Start.Position.Position.Y.Units, segment.Start.Velocity.MillimetersPerSecondY,
            segment.Acceleration.MillimetersPerSecondSquaredY), 2_000_000_000);

    internal static InteractionPhaseCurve From(DecelerationManeuverSegment segment)
    {
        BigInteger speed = segment.InitialSpeedMillimetersPerSecond;
        BigInteger rate = segment.Deceleration.MillimetersPerSecondSquared;
        BigInteger denominator = 2 * speed * 1_000_000_000;
        return new InteractionPhaseCurve(segment.Start.Position.SystemId, segment.StartsAt, segment.EndsAt,
            Braking(segment.Start.Position.Position.X.Units, segment.Start.Velocity.MillimetersPerSecondX, speed, rate, denominator),
            Braking(segment.Start.Position.Position.Y.Units, segment.Start.Velocity.MillimetersPerSecondY, speed, rate, denominator),
            denominator, new InteractionFraction((speed - segment.TargetSpeedMillimetersPerSecond) * 1_000, rate));
    }

    internal static InteractionPhaseCurve From(LocalMotionSegment segment)
    {
        BigInteger duration = segment.ArrivesAt.Milliseconds - segment.DepartedAt.Milliseconds;
        return new InteractionPhaseCurve(segment.Origin.SystemId, segment.DepartedAt, segment.ArrivesAt,
            [(BigInteger)segment.Origin.Position.X.Units * duration,
                (BigInteger)segment.Destination.Position.X.Units - segment.Origin.Position.X.Units, 0],
            [(BigInteger)segment.Origin.Position.Y.Units * duration,
                (BigInteger)segment.Destination.Position.Y.Units - segment.Origin.Position.Y.Units, 0], duration);
    }

    internal static InteractionPhaseCurve Fixed(SystemPosition position, SimulationTime start, SimulationTime end) => new(
        position.SystemId, start, end, [position.Position.X.Units, 0, 0], [position.Position.Y.Units, 0, 0], 1);

    private static BigInteger[] Accelerating(long position, long velocity, long acceleration) =>
        [(BigInteger)position * 2_000_000_000, (BigInteger)velocity * 2_000, acceleration];

    private static BigInteger[] Braking(long position, long velocity, BigInteger speed, BigInteger rate, BigInteger denominator) =>
        [(BigInteger)position * denominator, (BigInteger)velocity * 2 * speed * 1_000, -(BigInteger)velocity * rate];

    /// <summary>Splits only at the exact target time, keeping the final fractional hold in the known phase.</summary>
    internal IReadOnlyList<InteractionCurvePiece> Pieces(SimulationTime origin)
    {
        InteractionFraction start = InteractionFraction.Integer(StartsAt.Milliseconds);
        InteractionFraction end = InteractionFraction.Integer(EndsAt.Milliseconds);
        var curve = new InteractionCurvePiece(start, end,
            Shift(_x, (BigInteger)origin.Milliseconds - StartsAt.Milliseconds),
            Shift(_y, (BigInteger)origin.Milliseconds - StartsAt.Milliseconds), _denominator);
        if (_holdAt is not { } cutoff) return [curve];
        InteractionFraction target = start + cutoff;
        if (target >= end) return [curve];
        BigInteger n = cutoff.Numerator;
        BigInteger d = cutoff.Denominator;
        var hold = new InteractionCurvePiece(target, end,
            [At(_x, n, d), 0, 0], [At(_y, n, d), 0, 0], _denominator * d * d);
        return [curve with { Through = target }, hold];
    }

    private static BigInteger At(BigInteger[] coefficients, BigInteger numerator, BigInteger denominator) =>
        coefficients[0] * denominator * denominator + coefficients[1] * numerator * denominator + coefficients[2] * numerator * numerator;

    private static BigInteger[] Shift(BigInteger[] coefficients, BigInteger offset) =>
        [coefficients[0] + coefficients[1] * offset + coefficients[2] * offset * offset,
            coefficients[1] + 2 * coefficients[2] * offset, coefficients[2]];
}

internal sealed record InteractionCurvePiece(InteractionFraction From, InteractionFraction Through,
    BigInteger[] X, BigInteger[] Y, BigInteger Denominator);

/// <summary>Read-only pair evaluation across rational curve pieces and target-position holds.</summary>
internal static class InteractionPhaseRangeQuery
{
    /// <summary>Clips known phases, searches their exact piece overlaps, and samples the full held curves at the due timestamp.</summary>
    internal static SweptRangeContact? Find(InteractionPhaseCurve first, InteractionPhaseCurve second,
        SimulationTime from, SimulationTime through, ulong range, bool includeThrough = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));
        if (first.System != second.System) return null;
        ulong start = Math.Max(from.Milliseconds, Math.Max(first.StartsAt.Milliseconds, second.StartsAt.Milliseconds));
        ulong end = Math.Min(through.Milliseconds, Math.Min(first.EndsAt.Milliseconds, second.EndsAt.Milliseconds));
        if (end < start) return null;
        var origin = new SimulationTime(start);
        IReadOnlyList<InteractionCurvePiece> a = first.Pieces(origin);
        IReadOnlyList<InteractionCurvePiece> b = second.Pieces(origin);
        SweptRangeContact? earliest = null;
        foreach (InteractionCurvePiece left in a)
        foreach (InteractionCurvePiece right in b)
        {
            InteractionFraction lower = Max(Max(left.From, right.From), InteractionFraction.Integer(start));
            InteractionFraction upper = Min(Min(left.Through, right.Through), InteractionFraction.Integer(end));
            if (upper < lower) continue;
            BigInteger[] predicate = Predicate(left, right, range);
            SweptRangeContact? contact = PolynomialRangeQuery.FindFirstContact(predicate, origin,
                lower - InteractionFraction.Integer(start), upper - InteractionFraction.Integer(start),
                includeThrough || upper < InteractionFraction.Integer(end));
            if (contact is not null && (earliest is null || contact.Timestamp < earliest.Timestamp)) earliest = contact;
        }
        if (earliest is null) return null;
        return earliest with
        {
            AlreadyInRange = InRange(a, b, start, start, range),
            InRangeAtTimestamp = InRange(a, b, earliest.Timestamp.Milliseconds, start, range),
        };
    }

    /// <summary>Uses the target hold once its exact cutoff has been reached.</summary>
    private static bool InRange(IReadOnlyList<InteractionCurvePiece> first, IReadOnlyList<InteractionCurvePiece> second,
        ulong timestamp, ulong origin, ulong range)
    {
        InteractionFraction time = InteractionFraction.Integer(timestamp);
        InteractionCurvePiece a = first.Last(piece => piece.From <= time);
        InteractionCurvePiece b = second.Last(piece => piece.From <= time);
        BigInteger offset = timestamp - origin;
        BigInteger value = 0;
        BigInteger[] predicate = Predicate(a, b, range);
        for (int index = predicate.Length - 1; index >= 0; index--) value = value * offset + predicate[index];
        return value <= 0;
    }

    /// <summary>Eliminates both positive rational denominators before squared-distance comparison.</summary>
    private static BigInteger[] Predicate(InteractionCurvePiece first, InteractionCurvePiece second, ulong range)
    {
        BigInteger[] x = first.X.Zip(second.X, (a, b) => a * second.Denominator - b * first.Denominator).ToArray();
        BigInteger[] y = first.Y.Zip(second.Y, (a, b) => a * second.Denominator - b * first.Denominator).ToArray();
        var coefficients = new BigInteger[5];
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
            coefficients[row + column] += x[row] * x[column] + y[row] * y[column];
        BigInteger radius = (BigInteger)range * first.Denominator * second.Denominator;
        coefficients[0] -= radius * radius;
        return coefficients;
    }

    private static InteractionFraction Min(InteractionFraction a, InteractionFraction b) => a < b ? a : b;
    private static InteractionFraction Max(InteractionFraction a, InteractionFraction b) => a > b ? a : b;
}
