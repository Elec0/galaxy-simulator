namespace GalaxyCommand.Simulation;

/// <summary>
/// Analytic aligned sub-cruise plan with optional acceleration, positive
/// constant-speed travel, and terminal active braking.
/// </summary>
public sealed record CappedSpeedManeuverPlan
{
    private const long MillimetersPerMeter = 1_000;
    private const ulong MillisecondsPerSecond = 1_000;

    private CappedSpeedManeuverPlan(
        SystemPosition destination,
        CappedSpeedManeuverProfile profile,
        AnalyticManeuverSegment? accelerationPhase,
        AnalyticManeuverSegment cappedTravelPhase,
        DecelerationManeuverSegment brakingPhase)
    {
        Destination = destination;
        Profile = profile;
        AccelerationPhase = accelerationPhase;
        CappedTravelPhase = cappedTravelPhase;
        BrakingPhase = brakingPhase;
    }

    public SystemPosition Destination { get; }

    public CappedSpeedManeuverProfile Profile { get; }

    public AnalyticManeuverSegment? AccelerationPhase { get; }

    public AnalyticManeuverSegment CappedTravelPhase { get; }

    public DecelerationManeuverSegment BrakingPhase { get; }

    public SimulationTime StartsAt =>
        AccelerationPhase?.StartsAt ?? CappedTravelPhase.StartsAt;

    public SimulationTime TravelStartsAt => CappedTravelPhase.StartsAt;

    public SimulationTime BrakingStartsAt => BrakingPhase.StartsAt;

    public SimulationTime EndsAt => BrakingPhase.EndsAt;

    /// <summary>
    /// Builds an aligned same-system capped-speed plan from rest or speed at or
    /// below the cap. The acceleration phase is omitted when already at the
    /// cap. Returns false when another direction, prior braking, triangular
    /// motion, or a different discrete schedule is required to satisfy arrival.
    /// </summary>
    public static bool TryCreateAligned(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out CappedSpeedManeuverPlan? plan)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A capped-speed plan requires a destination in the starting system.",
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
        if (!CappedSpeedManeuverProfile.TryCreate(
                distance,
                initialSpeed,
                acceleration,
                braking,
                maximumSpeed,
                out CappedSpeedManeuverProfile? candidateProfile)
            || candidateProfile is not { } profile)
        {
            plan = null;
            return false;
        }

        ShipAcceleration accelerationVector = new(
            DirectionComponent(deltaX, distance, acceleration),
            DirectionComponent(deltaY, distance, acceleration));
        AnalyticManeuverSegment? accelerationPhase = null;
        ShipKinematicState atCap = start;
        SimulationTime travelStartsAt = startsAt;
        if (initialSpeed < maximumSpeed.MillimetersPerSecond)
        {
            SimulationDuration accelerationDuration = RoundedDuration(
                maximumSpeed.MillimetersPerSecond - initialSpeed,
                acceleration.MillimetersPerSecondSquared);
            travelStartsAt = startsAt.Add(accelerationDuration);
            accelerationPhase = new AnalyticManeuverSegment(
                startsAt,
                travelStartsAt,
                start,
                accelerationVector,
                ShipAngularRate.Zero);
            atCap = accelerationPhase.StateAt(travelStartsAt);
        }

        if (ManeuverVector.SpeedMagnitude(atCap.Velocity)
            != maximumSpeed.MillimetersPerSecond)
        {
            plan = null;
            return false;
        }

        SimulationDuration travelDuration = RoundedDuration(
            profile.CappedTravelDistanceMillimeters,
            maximumSpeed.MillimetersPerSecond);
        SimulationTime brakingStartsAt = travelStartsAt.Add(travelDuration);
        var cappedTravelPhase = new AnalyticManeuverSegment(
            travelStartsAt,
            brakingStartsAt,
            atCap,
            ShipAcceleration.Zero,
            ShipAngularRate.Zero);
        ShipKinematicState brakingStart =
            cappedTravelPhase.StateAt(brakingStartsAt);
        var brakingPhase = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            brakingStartsAt,
            brakingStart,
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

        plan = new CappedSpeedManeuverPlan(
            destination,
            profile,
            accelerationPhase,
            cappedTravelPhase,
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
                $"Capped-speed time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        if (AccelerationPhase is { } accelerationPhase
            && time <= accelerationPhase.EndsAt)
        {
            return accelerationPhase.StateAt(time);
        }

        return time <= CappedTravelPhase.EndsAt
            ? CappedTravelPhase.StateAt(time)
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
    /// Requires exact collinearity and a positive dot product so this bounded
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
    /// Publishes a positive phase duration with normal rounding. A positive
    /// interval below one millisecond remains one millisecond so consecutive
    /// event boundaries never share a timestamp.
    /// </summary>
    private static SimulationDuration RoundedDuration(
        ulong numeratorValue,
        ulong denominatorValue)
    {
        UInt128 numerator = (UInt128)numeratorValue * MillisecondsPerSecond;
        UInt128 denominator = denominatorValue;
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
                "A capped-speed phase duration exceeds simulation range.");
        }

        return new SimulationDuration(Math.Max(1, (ulong)quotient));
    }
}
