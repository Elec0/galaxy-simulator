namespace GalaxyCommand.Simulation;

/// <summary>
/// Signed system-local acceleration in integer millimeters per simulated
/// second squared.
/// </summary>
public readonly record struct ShipAcceleration(
    long MillimetersPerSecondSquaredX,
    long MillimetersPerSecondSquaredY)
{
    public static ShipAcceleration Zero { get; } = new(0, 0);
}

/// <summary>
/// Signed angular rate in integer millidegrees per simulated second. Positive
/// values rotate clockwise.
/// </summary>
public readonly record struct ShipAngularRate(long MillidegreesPerSecond)
{
    public static ShipAngularRate Zero { get; } = new(0);
}

/// <summary>
/// Exact published kinematic state at one authoritative simulation timestamp.
/// </summary>
public readonly record struct ShipKinematicState(
    SystemPosition Position,
    ShipVelocity Velocity,
    ShipHeading Heading);

/// <summary>
/// One immutable bounded analytic segment with constant translational
/// acceleration and angular rate.
/// </summary>
public sealed record AnalyticManeuverSegment
{
    /// <summary>
    /// Creates a positive-duration segment. Evaluation is valid only from the
    /// inclusive start through the inclusive scheduled end boundary.
    /// </summary>
    public AnalyticManeuverSegment(
        SimulationTime startsAt,
        SimulationTime endsAt,
        ShipKinematicState start,
        ShipAcceleration acceleration,
        ShipAngularRate angularRate)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                endsAt,
                "An analytic maneuver segment must have positive duration.");
        }

        StartsAt = startsAt;
        EndsAt = endsAt;
        Start = start;
        Acceleration = acceleration;
        AngularRate = angularRate;
    }

    public SimulationTime StartsAt { get; }

    public SimulationTime EndsAt { get; }

    public ShipKinematicState Start { get; }

    public ShipAcceleration Acceleration { get; }

    public ShipAngularRate AngularRate { get; }

    /// <summary>
    /// Evaluates this segment directly from its immutable start state. A time
    /// outside the committed segment is rejected rather than extrapolated.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Maneuver time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return ManeuverKinematics.Evaluate(
            Start,
            Acceleration,
            AngularRate,
            new SimulationDuration(time.Milliseconds - StartsAt.Milliseconds));
    }
}

/// <summary>
/// Shared fixed-point equations for analytic phases and future fine-grained
/// interaction steps.
/// </summary>
public static class ManeuverKinematics
{
    private const ulong MillisecondsPerSecond = 1_000;
    private const ulong PositionDenominator = 2_000_000_000;

    /// <summary>
    /// Evaluates constant translational acceleration and angular rate directly
    /// from a phase start. Published components use nearest-unit rounding with
    /// exact half units rounded away from zero. Overflow rejects the result.
    /// </summary>
    public static ShipKinematicState Evaluate(
        ShipKinematicState start,
        ShipAcceleration acceleration,
        ShipAngularRate angularRate,
        SimulationDuration elapsed)
    {
        if (elapsed == SimulationDuration.Zero)
        {
            return start;
        }

        return new ShipKinematicState(
            new SystemPosition(
                start.Position.SystemId,
                new SpatialPosition(
                    EvaluatePosition(
                        start.Position.Position.X,
                        start.Velocity.MillimetersPerSecondX,
                        acceleration.MillimetersPerSecondSquaredX,
                        elapsed.Milliseconds),
                    EvaluatePosition(
                        start.Position.Position.Y,
                        start.Velocity.MillimetersPerSecondY,
                        acceleration.MillimetersPerSecondSquaredY,
                        elapsed.Milliseconds))),
            new ShipVelocity(
                EvaluateVelocity(
                    start.Velocity.MillimetersPerSecondX,
                    acceleration.MillimetersPerSecondSquaredX,
                    elapsed.Milliseconds),
                EvaluateVelocity(
                    start.Velocity.MillimetersPerSecondY,
                    acceleration.MillimetersPerSecondSquaredY,
                    elapsed.Milliseconds)),
            EvaluateHeading(start.Heading, angularRate, elapsed.Milliseconds));
    }

    /// <summary>
    /// Evaluates one position component from the unrounded phase-start terms so
    /// intermediate velocity publication cannot alter analytic displacement.
    /// </summary>
    private static SpatialCoordinate EvaluatePosition(
        SpatialCoordinate start,
        long startVelocity,
        long acceleration,
        ulong elapsedMilliseconds)
    {
        Int128 elapsed = elapsedMilliseconds;
        Int128 velocityTerm = checked(
            (Int128)startVelocity * elapsed * 2_000);
        Int128 accelerationTerm = checked(
            (Int128)acceleration * elapsed * elapsed);
        Int128 displacement = RoundSignedRatio(
            checked(velocityTerm + accelerationTerm),
            PositionDenominator);
        return new SpatialCoordinate(checked(
            (long)((Int128)start.Units + displacement)));
    }

    /// <summary>
    /// Evaluates one velocity component and rejects values outside its signed
    /// fixed-point representation.
    /// </summary>
    private static long EvaluateVelocity(
        long startVelocity,
        long acceleration,
        ulong elapsedMilliseconds)
    {
        Int128 delta = RoundSignedRatio(
            checked((Int128)acceleration * elapsedMilliseconds),
            MillisecondsPerSecond);
        return checked((long)((Int128)startVelocity + delta));
    }

    /// <summary>
    /// Evaluates signed rotation and canonicalizes the published heading only
    /// after rounding the phase-relative angular displacement.
    /// </summary>
    private static ShipHeading EvaluateHeading(
        ShipHeading start,
        ShipAngularRate angularRate,
        ulong elapsedMilliseconds)
    {
        Int128 delta = RoundSignedRatio(
            checked((Int128)angularRate.MillidegreesPerSecond
                * elapsedMilliseconds),
            MillisecondsPerSecond);
        Int128 canonical = ((Int128)start.Millidegrees + delta)
            % ShipHeading.MillidegreesPerRevolution;
        if (canonical < 0)
        {
            canonical += ShipHeading.MillidegreesPerRevolution;
        }

        return new ShipHeading((uint)canonical);
    }

    /// <summary>
    /// Rounds a signed exact ratio to nearest, with half-unit ties away from
    /// zero, without negating <see cref="Int128.MinValue"/>.
    /// </summary>
    internal static Int128 RoundSignedRatio(
        Int128 numerator,
        UInt128 denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);

        bool negative = numerator < 0;
        UInt128 magnitude = negative
            ? (UInt128)(-(numerator + 1)) + 1
            : (UInt128)numerator;
        UInt128 quotient = magnitude / denominator;
        UInt128 remainder = magnitude % denominator;
        UInt128 half = denominator / 2;
        if (remainder > half
            || denominator % 2 == 0 && remainder == half)
        {
            quotient++;
        }

        UInt128 negativeLimit = (UInt128)Int128.MaxValue + 1;
        if (!negative && quotient > (UInt128)Int128.MaxValue
            || negative && quotient > negativeLimit)
        {
            throw new OverflowException("The rounded fixed-point result exceeds Int128 range.");
        }

        if (!negative)
        {
            return (Int128)quotient;
        }

        return quotient == negativeLimit
            ? Int128.MinValue
            : -(Int128)quotient;
    }
}
