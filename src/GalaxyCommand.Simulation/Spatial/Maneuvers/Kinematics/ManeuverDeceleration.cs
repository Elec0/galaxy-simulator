namespace GalaxyCommand.Simulation;

public enum ManeuverDecelerationKind
{
    PassiveDrag,
    ActiveBrake,
}

/// <summary>
/// Deterministic vector operations used by maneuver planning and evaluation.
/// </summary>
public static class ManeuverVector
{
    /// <summary>
    /// Returns the complete velocity-vector magnitude in millimeters per
    /// second, rounded to nearest with the integer square-root remainder.
    /// </summary>
    public static ulong SpeedMagnitude(ShipVelocity velocity)
    {
        UInt128 x = Magnitude(velocity.MillimetersPerSecondX);
        UInt128 y = Magnitude(velocity.MillimetersPerSecondY);
        UInt128 squared = x * x + y * y;
        ulong floor = IntegerSquareRoot(squared);
        if ((UInt128)floor * floor == squared)
        {
            return floor;
        }

        ulong upper = checked(floor + 1);
        UInt128 lowerDistance = squared - (UInt128)floor * floor;
        UInt128 upperDistance = (UInt128)upper * upper - squared;
        return lowerDistance < upperDistance ? floor : upper;
    }

    /// <summary>
    /// Produces a signed-component magnitude without overflowing at the
    /// minimum signed value.
    /// </summary>
    private static UInt128 Magnitude(long value) =>
        value < 0
            ? (UInt128)(-(Int128)value)
            : (UInt128)value;

    /// <summary>
    /// Finds the greatest unsigned integer whose square does not exceed the
    /// input, using a fixed-width binary search with no floating-point state.
    /// </summary>
    private static ulong IntegerSquareRoot(UInt128 value)
    {
        ulong low = 0;
        ulong high = ulong.MaxValue;
        ulong result = 0;
        while (low <= high)
        {
            ulong midpoint = low + ((high - low) >> 1);
            UInt128 squared = (UInt128)midpoint * midpoint;
            if (squared <= value)
            {
                result = midpoint;
                if (midpoint == ulong.MaxValue)
                {
                    break;
                }

                low = midpoint + 1;
            }
            else
            {
                high = midpoint - 1;
            }
        }

        return result;
    }
}

/// <summary>
/// One passive-drag or active-braking segment that decelerates directly
/// opposite its starting velocity without changing heading or reversing.
/// </summary>
public sealed record DecelerationManeuverSegment
{
    private const ulong MillisecondsPerSecond = 1_000;
    private const ulong MillimetersPerMeter = 1_000;

    /// <summary>
    /// Builds the exact scheduled stop boundary for one nonzero velocity and
    /// one positive deceleration rate. Passive drag and active braking remain
    /// distinct instructions, so their rates are never implicitly stacked.
    /// </summary>
    public DecelerationManeuverSegment(
        ManeuverDecelerationKind kind,
        SimulationTime startsAt,
        ShipKinematicState start,
        ManeuverAcceleration deceleration)
        : this(
            kind,
            startsAt,
            start,
            deceleration,
            targetSpeed: default)
    {
    }

    /// <summary>
    /// Builds one bounded deceleration to a lower target speed. A zero target
    /// retains the ordinary exact-stop contract; a positive target publishes
    /// the direction-preserving target vector at the rounded-up boundary.
    /// </summary>
    public DecelerationManeuverSegment(
        ManeuverDecelerationKind kind,
        SimulationTime startsAt,
        ShipKinematicState start,
        ManeuverAcceleration deceleration,
        ManeuverSpeed targetSpeed)
    {
        if (kind is not ManeuverDecelerationKind.PassiveDrag
            and not ManeuverDecelerationKind.ActiveBrake)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown deceleration kind.");
        }

        if (deceleration.MillimetersPerSecondSquared == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deceleration),
                deceleration,
                "Deceleration must be positive.");
        }

        ulong speed = ManeuverVector.SpeedMagnitude(start.Velocity);
        if (speed == 0)
        {
            throw new ArgumentException(
                "A deceleration segment requires nonzero starting velocity.",
                nameof(start));
        }

        if (targetSpeed.MillimetersPerSecond >= speed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSpeed),
                targetSpeed,
                "Target speed must be below the nonzero starting speed.");
        }

        ulong speedChange = speed - targetSpeed.MillimetersPerSecond;
        UInt128 durationNumerator =
            (UInt128)speedChange * MillisecondsPerSecond;
        UInt128 rate = deceleration.MillimetersPerSecondSquared;
        UInt128 duration = (durationNumerator + rate - 1) / rate;
        if (duration > ulong.MaxValue)
        {
            throw new OverflowException("The deceleration duration exceeds simulation range.");
        }

        Kind = kind;
        StartsAt = startsAt;
        Start = start;
        Deceleration = deceleration;
        InitialSpeedMillimetersPerSecond = speed;
        TargetSpeedMillimetersPerSecond = targetSpeed.MillimetersPerSecond;
        EndsAt = startsAt.Add(new SimulationDuration((ulong)duration));
    }

    public ManeuverDecelerationKind Kind { get; }

    public SimulationTime StartsAt { get; }

    public SimulationTime EndsAt { get; }

    public ShipKinematicState Start { get; }

    public ManeuverAcceleration Deceleration { get; }

    public ulong InitialSpeedMillimetersPerSecond { get; }

    public ulong TargetSpeedMillimetersPerSecond { get; }

    /// <summary>
    /// Evaluates this deceleration from its immutable start. The scheduled end
    /// publishes exact zero velocity and the analytically rounded stop position;
    /// other times preserve the starting velocity direction without reversal.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Deceleration time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        if (time == StartsAt)
        {
            return Start;
        }

        if (time == EndsAt)
        {
            return new ShipKinematicState(
                PositionAtTarget(),
                TargetSpeedMillimetersPerSecond == 0
                    ? ShipVelocity.Zero
                    : new ShipVelocity(
                        ScaleVelocity(
                            Start.Velocity.MillimetersPerSecondX,
                            TargetSpeedMillimetersPerSecond,
                            InitialSpeedMillimetersPerSecond),
                        ScaleVelocity(
                            Start.Velocity.MillimetersPerSecondY,
                            TargetSpeedMillimetersPerSecond,
                            InitialSpeedMillimetersPerSecond)),
                Start.Heading);
        }

        ulong elapsed = time.Milliseconds - StartsAt.Milliseconds;
        UInt128 speedMilliseconds =
            (UInt128)InitialSpeedMillimetersPerSecond * MillisecondsPerSecond;
        UInt128 decelerationElapsed =
            (UInt128)Deceleration.MillimetersPerSecondSquared * elapsed;
        UInt128 remaining = speedMilliseconds - decelerationElapsed;
        return new ShipKinematicState(
            PositionAt(elapsed),
            new ShipVelocity(
                ScaleVelocity(
                    Start.Velocity.MillimetersPerSecondX,
                    remaining,
                    speedMilliseconds),
                ScaleVelocity(
                    Start.Velocity.MillimetersPerSecondY,
                    remaining,
                    speedMilliseconds)),
            Start.Heading);
    }

    /// <summary>
    /// Evaluates one pre-stop position component from the constant opposing
    /// acceleration without publishing a long-lived fractional direction.
    /// </summary>
    private SystemPosition PositionAt(ulong elapsedMilliseconds) =>
        new(
            Start.Position.SystemId,
            new SpatialPosition(
                PositionComponentAt(
                    Start.Position.Position.X,
                    Start.Velocity.MillimetersPerSecondX,
                    elapsedMilliseconds),
                PositionComponentAt(
                    Start.Position.Position.Y,
                    Start.Velocity.MillimetersPerSecondY,
                    elapsedMilliseconds)));

    private SpatialCoordinate PositionComponentAt(
        SpatialCoordinate start,
        long startVelocity,
        ulong elapsedMilliseconds)
    {
        Int128 speed = InitialSpeedMillimetersPerSecond;
        Int128 elapsed = elapsedMilliseconds;
        Int128 rate = Deceleration.MillimetersPerSecondSquared;
        Int128 remainingFactor = checked(
            2 * speed * MillisecondsPerSecond - rate * elapsed);
        Int128 numerator = checked(
            (Int128)startVelocity * elapsed * remainingFactor);
        UInt128 denominator = checked(
            (UInt128)2
            * InitialSpeedMillimetersPerSecond
            * MillisecondsPerSecond
            * MillisecondsPerSecond
            * MillimetersPerMeter);
        Int128 displacement = ManeuverKinematics.RoundSignedRatio(
            numerator,
            denominator);
        return new SpatialCoordinate(checked(
            (long)((Int128)start.Units + displacement)));
    }

    /// <summary>
    /// Materializes the exact continuous target-speed point before rounding it
    /// to the authoritative integer-meter position.
    /// </summary>
    private SystemPosition PositionAtTarget() =>
        new(
            Start.Position.SystemId,
            new SpatialPosition(
                TargetPositionComponent(
                    Start.Position.Position.X,
                    Start.Velocity.MillimetersPerSecondX),
                TargetPositionComponent(
                    Start.Position.Position.Y,
                    Start.Velocity.MillimetersPerSecondY)));

    private SpatialCoordinate TargetPositionComponent(
        SpatialCoordinate start,
        long startVelocity)
    {
        UInt128 initial = InitialSpeedMillimetersPerSecond;
        UInt128 target = TargetSpeedMillimetersPerSecond;
        UInt128 squaredSpeedChange = checked(
            initial * initial - target * target);
        Int128 numerator = checked(
            (Int128)startVelocity * (Int128)squaredSpeedChange);
        UInt128 denominator = checked(
            (UInt128)2
            * InitialSpeedMillimetersPerSecond
            * Deceleration.MillimetersPerSecondSquared
            * MillimetersPerMeter);
        Int128 displacement = ManeuverKinematics.RoundSignedRatio(
            numerator,
            denominator);
        return new SpatialCoordinate(checked(
            (long)((Int128)start.Units + displacement)));
    }

    private static long ScaleVelocity(
        long component,
        UInt128 remaining,
        UInt128 initial)
    {
        Int128 numerator = checked((Int128)component * (Int128)remaining);
        return checked((long)ManeuverKinematics.RoundSignedRatio(
            numerator,
            initial));
    }
}
