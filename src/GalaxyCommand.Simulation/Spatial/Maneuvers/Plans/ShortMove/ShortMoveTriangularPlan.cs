namespace GalaxyCommand.Simulation;

/// <summary>
/// Two-phase analytic terminal move with velocity already aligned toward the
/// destination: constant acceleration followed by active braking to zero.
/// </summary>
public sealed record ShortMoveTriangularPlan
{
    private const long MillimetersPerMeter = 1_000;
    private const ulong MillisecondsPerSecond = 1_000;

    private ShortMoveTriangularPlan(
        SystemPosition destination,
        ShortMoveTriangularProfile profile,
        AnalyticManeuverSegment accelerationPhase,
        DecelerationManeuverSegment brakingPhase)
    {
        Destination = destination;
        Profile = profile;
        AccelerationPhase = accelerationPhase;
        BrakingPhase = brakingPhase;
    }

    public SystemPosition Destination { get; }

    public ShortMoveTriangularProfile Profile { get; }

    public AnalyticManeuverSegment AccelerationPhase { get; }

    public DecelerationManeuverSegment BrakingPhase { get; }

    public SimulationTime StartsAt => AccelerationPhase.StartsAt;

    public SimulationTime SwitchesAt => AccelerationPhase.EndsAt;

    public SimulationTime EndsAt => BrakingPhase.EndsAt;

    /// <summary>
    /// Builds a same-system triangular terminal plan from exact rest. The
    /// selected scalar acceleration is normalized toward the destination and
    /// heading remains unchanged. Returns false when no move is needed, the
    /// speed cap requires another profile, or millisecond scheduling cannot
    /// stop within the ordinary terminal tolerance.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out ShortMoveTriangularPlan? plan)
    {
        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "A triangular plan from rest requires zero starting velocity.",
                nameof(start));
        }

        return TryCreateAligned(
            startsAt,
            start,
            destination,
            acceleration,
            braking,
            maximumSpeed,
            out plan);
    }

    /// <summary>
    /// Builds a same-system triangular terminal plan from zero or exactly
    /// destination-aligned velocity before the switch boundary. Returns false
    /// when arrival is already satisfied, another direction policy is needed,
    /// braking must begin immediately, the cap requires another profile, or
    /// millisecond scheduling cannot stop within terminal tolerance.
    /// </summary>
    public static bool TryCreateAligned(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out ShortMoveTriangularPlan? plan)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A triangular plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (ManeuverArrival.EvaluateTerminal(start, destination, null).IsSatisfied)
        {
            plan = null;
            return false;
        }

        long deltaX = DeltaMillimeters(
            start.Position.Position.X,
            destination.Position.X);
        long deltaY = DeltaMillimeters(
            start.Position.Position.Y,
            destination.Position.Y);
        ulong distance = ManeuverVector.SpeedMagnitude(
            new ShipVelocity(deltaX, deltaY));
        if (distance == 0
            || start.Velocity != ShipVelocity.Zero
            && !IsAlignedToward(deltaX, deltaY, start.Velocity))
        {
            plan = null;
            return false;
        }

        ulong initialSpeed = ManeuverVector.SpeedMagnitude(start.Velocity);
        if (!ShortMoveTriangularProfile.TryCreate(
                distance,
                initialSpeed,
                acceleration,
                braking,
                maximumSpeed,
                out ShortMoveTriangularProfile? candidateProfile)
            || candidateProfile is not { BeginsWithBraking: false } profile)
        {
            plan = null;
            return false;
        }

        SimulationDuration accelerationDuration = AccelerationDuration(
            initialSpeed,
            profile.SwitchSpeedMillimetersPerSecond,
            acceleration);
        SimulationTime switchesAt = startsAt.Add(accelerationDuration);
        var accelerationPhase = new AnalyticManeuverSegment(
            startsAt,
            switchesAt,
            start,
            new ShipAcceleration(
                DirectionComponent(deltaX, distance, acceleration),
                DirectionComponent(deltaY, distance, acceleration)),
            ShipAngularRate.Zero);
        ShipKinematicState switchState = accelerationPhase.StateAt(switchesAt);
        if (ManeuverVector.SpeedMagnitude(switchState.Velocity)
            > maximumSpeed.MillimetersPerSecond)
        {
            plan = null;
            return false;
        }

        var brakingPhase = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            switchesAt,
            switchState,
            braking);
        ShipKinematicState stopped = brakingPhase.StateAt(brakingPhase.EndsAt);
        if (!ManeuverArrival.EvaluateTerminal(
                stopped,
                destination,
                requestedHeading: null).IsSatisfied)
        {
            plan = null;
            return false;
        }

        plan = new ShortMoveTriangularPlan(
            destination,
            profile,
            accelerationPhase,
            brakingPhase);
        return true;
    }

    /// <summary>
    /// Evaluates the appropriate immutable phase at an inclusive scheduled
    /// time. Times outside the complete plan are rejected.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Short-move time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return time <= SwitchesAt
            ? AccelerationPhase.StateAt(time)
            : BrakingPhase.StateAt(time);
    }

    private static long DeltaMillimeters(
        SpatialCoordinate start,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - start.Units)
            * MillimetersPerMeter));

    private static long DirectionComponent(
        long displacement,
        ulong distance,
        ManeuverAcceleration acceleration)
    {
        Int128 numerator = checked(
            (Int128)displacement
            * acceleration.MillimetersPerSecondSquared);
        return checked((long)ManeuverKinematics.RoundSignedRatio(
            numerator,
            distance));
    }

    /// <summary>
    /// Requires exact collinearity and a positive dot product so this narrow
    /// plan never chooses lateral correction or turning behavior.
    /// </summary>
    private static bool IsAlignedToward(
        long deltaX,
        long deltaY,
        ShipVelocity velocity)
    {
        Int128 cross = checked(
            (Int128)deltaX * velocity.MillimetersPerSecondY
            - (Int128)deltaY * velocity.MillimetersPerSecondX);
        Int128 dot = checked(
            (Int128)deltaX * velocity.MillimetersPerSecondX
            + (Int128)deltaY * velocity.MillimetersPerSecondY);
        return cross == 0 && dot > 0;
    }

    /// <summary>
    /// Publishes the switch timestamp with normal rounding. A positive phase
    /// that falls below the one-millisecond representation remains one
    /// millisecond so the event boundary stays strictly later than its start.
    /// </summary>
    private static SimulationDuration AccelerationDuration(
        ulong initialSpeed,
        ulong switchSpeed,
        ManeuverAcceleration acceleration)
    {
        ulong speedIncrease = checked(switchSpeed - initialSpeed);
        UInt128 numerator = (UInt128)speedIncrease * MillisecondsPerSecond;
        UInt128 denominator = acceleration.MillimetersPerSecondSquared;
        UInt128 quotient = numerator / denominator;
        UInt128 remainder = numerator % denominator;
        UInt128 half = denominator / 2;
        if (remainder > half
            || denominator % 2 == 0 && remainder == half)
        {
            quotient++;
        }

        if (quotient > ulong.MaxValue)
        {
            throw new OverflowException(
                "The triangular acceleration duration exceeds simulation range.");
        }

        return new SimulationDuration(Math.Max(1, (ulong)quotient));
    }
}
