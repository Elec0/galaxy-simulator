namespace GalaxyCommand.Simulation;

/// <summary>
/// Two-phase terminal translation that lowers all-direction precision thrust
/// when a full-rate millisecond schedule cannot represent the requested move.
/// </summary>
public sealed record ReducedThrustTerminalManeuverPlan
{
    private const ulong MillisecondsPerSecond = 1_000;
    private const ulong ArrivalSearchWindowMilliseconds = 60_000;

    private ReducedThrustTerminalManeuverPlan(
        SystemPosition destination,
        ShipHeading? requestedHeading,
        AnalyticManeuverSegment accelerationPhase,
        AnalyticManeuverSegment brakingPhase,
        StationaryTurnManeuverPlan? finalTurnPlan)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        AccelerationPhase = accelerationPhase;
        BrakingPhase = brakingPhase;
        FinalTurnPlan = finalTurnPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public AnalyticManeuverSegment AccelerationPhase { get; }

    public AnalyticManeuverSegment BrakingPhase { get; }

    public StationaryTurnManeuverPlan? FinalTurnPlan { get; }

    public SimulationTime StartsAt => AccelerationPhase.StartsAt;

    public SimulationTime SwitchesAt => AccelerationPhase.EndsAt;

    public SimulationTime TranslationEndsAt => BrakingPhase.EndsAt;

    public SimulationTime EndsAt => FinalTurnPlan?.EndsAt ?? TranslationEndsAt;

    /// <summary>
    /// Selects the shortest admitted equal-duration accelerate-and-brake pair
    /// from exact rest. Both system-local acceleration vectors stay within the
    /// precision cap, the midpoint stays within the sub-cruise speed cap, and
    /// the endpoint must satisfy terminal arrival without position snapping.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration precisionLimit,
        ManeuverSpeed maximumSpeed,
        out ReducedThrustTerminalManeuverPlan? plan)
        => TryCreateFromRest(
            startsAt,
            start,
            destination,
            precisionLimit,
            maximumSpeed,
            requestedHeading: null,
            turnRate: null,
            out plan);

    /// <summary>
    /// Selects the shortest admitted reduced-thrust translation and appends a
    /// stationary terminal turn when the requested heading is not already met.
    /// Failure publishes no partial translation.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration precisionLimit,
        ManeuverSpeed maximumSpeed,
        ShipHeading? requestedHeading,
        ManeuverTurnRate turnRate,
        out ReducedThrustTerminalManeuverPlan? plan)
        => TryCreateFromRest(
            startsAt,
            start,
            destination,
            precisionLimit,
            maximumSpeed,
            requestedHeading,
            (ManeuverTurnRate?)turnRate,
            out plan);

    /// <summary>
    /// Builds the translation first, then validates the optional final-heading
    /// continuation against the authoritative stopped position.
    /// </summary>
    private static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration precisionLimit,
        ManeuverSpeed maximumSpeed,
        ShipHeading? requestedHeading,
        ManeuverTurnRate? turnRate,
        out ReducedThrustTerminalManeuverPlan? plan)
    {
        plan = null;
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A reduced-thrust terminal plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero
            || ManeuverArrival.EvaluateTerminal(
                start,
                destination,
                requestedHeading: null).IsSatisfied)
        {
            return false;
        }

        ulong maximumHalfDuration = (ulong.MaxValue - startsAt.Milliseconds) / 2;
        if (maximumHalfDuration == 0)
        {
            return false;
        }

        ulong upper = 1;
        while (upper < maximumHalfDuration
            && !TryBuildCandidate(
                startsAt,
                start,
                destination,
                precisionLimit,
                maximumSpeed,
                upper,
                requireArrival: false,
                out _,
                out _))
        {
            upper = upper > maximumHalfDuration / 2
                ? maximumHalfDuration
                : upper * 2;
        }

        if (!TryBuildCandidate(
                startsAt,
                start,
                destination,
                precisionLimit,
                maximumSpeed,
                upper,
                requireArrival: false,
                out _,
                out _))
        {
            return false;
        }

        ulong lower = 1;
        while (lower < upper)
        {
            ulong midpoint = lower + ((upper - lower) / 2);
            if (TryBuildCandidate(
                    startsAt,
                    start,
                    destination,
                    precisionLimit,
                    maximumSpeed,
                    midpoint,
                    requireArrival: false,
                    out _,
                    out _))
            {
                upper = midpoint;
            }
            else
            {
                lower = midpoint + 1;
            }
        }

        ulong finalCandidate = Math.Min(
            maximumHalfDuration,
            checked(lower + Math.Min(
                ArrivalSearchWindowMilliseconds,
                maximumHalfDuration - lower)));
        for (ulong halfDuration = lower;
            halfDuration <= finalCandidate;
            halfDuration++)
        {
            if (TryBuildCandidate(
                    startsAt,
                    start,
                    destination,
                    precisionLimit,
                    maximumSpeed,
                    halfDuration,
                    requireArrival: true,
                    out AnalyticManeuverSegment? acceleration,
                    out AnalyticManeuverSegment? braking))
            {
                ShipKinematicState stopped = braking!.StateAt(braking.EndsAt);
                ManeuverArrivalEvaluation arrival =
                    ManeuverArrival.EvaluateTerminal(
                        stopped,
                        destination,
                        requestedHeading);
                StationaryTurnManeuverPlan? finalTurn = null;
                if (!arrival.IsSatisfied)
                {
                    if (!arrival.PositionSatisfied
                        || !arrival.VelocitySatisfied
                        || requestedHeading is not { } heading
                        || turnRate is not { } effectiveTurnRate
                        || !StationaryTurnManeuverPlan.TryCreate(
                            braking.EndsAt,
                            stopped,
                            stopped.Position,
                            heading,
                            effectiveTurnRate,
                            out finalTurn)
                        || finalTurn is null
                        || !ManeuverArrival.EvaluateTerminal(
                            finalTurn.StateAt(finalTurn.EndsAt),
                            destination,
                            requestedHeading).IsSatisfied)
                    {
                        continue;
                    }
                }

                plan = new ReducedThrustTerminalManeuverPlan(
                    destination,
                    requestedHeading,
                    acceleration!,
                    braking,
                    finalTurn);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates acceleration through its inclusive switch boundary, then the
    /// active-braking phase. Times outside the complete schedule reject.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Reduced-thrust time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        if (time <= SwitchesAt)
        {
            return AccelerationPhase.StateAt(time);
        }

        return time <= TranslationEndsAt
            ? BrakingPhase.StateAt(time)
            : FinalTurnPlan!.StateAt(time);
    }

    /// <summary>
    /// Publishes one candidate from the exact-rest boundary equations. Speed
    /// and acceleration checks are monotonic search predicates; the optional
    /// arrival check admits only a complete no-snap terminal schedule.
    /// </summary>
    private static bool TryBuildCandidate(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration precisionLimit,
        ManeuverSpeed maximumSpeed,
        ulong halfDuration,
        bool requireArrival,
        out AnalyticManeuverSegment? accelerationPhase,
        out AnalyticManeuverSegment? brakingPhase)
    {
        accelerationPhase = null;
        brakingPhase = null;
        if (halfDuration == 0)
        {
            return false;
        }

        try
        {
            ShipAcceleration acceleration = new(
                ResolveAcceleration(
                    DeltaMillimeters(
                        start.Position.Position.X,
                        destination.Position.X),
                    halfDuration),
                ResolveAcceleration(
                    DeltaMillimeters(
                        start.Position.Position.Y,
                        destination.Position.Y),
                    halfDuration));
            if (!WithinAccelerationLimit(acceleration, precisionLimit))
            {
                return false;
            }

            SimulationTime switchesAt = new(checked(
                startsAt.Milliseconds + halfDuration));
            SimulationTime endsAt = new(checked(
                switchesAt.Milliseconds + halfDuration));
            accelerationPhase = new AnalyticManeuverSegment(
                startsAt,
                switchesAt,
                start,
                acceleration,
                ShipAngularRate.Zero);
            ShipKinematicState midpoint = accelerationPhase.StateAt(switchesAt);
            ShipAcceleration braking = new(
                checked(-acceleration.MillimetersPerSecondSquaredX),
                checked(-acceleration.MillimetersPerSecondSquaredY));
            if (!WithinAccelerationLimit(braking, precisionLimit)
                || !WithinSpeedLimit(midpoint.Velocity, maximumSpeed))
            {
                accelerationPhase = null;
                return false;
            }

            brakingPhase = new AnalyticManeuverSegment(
                switchesAt,
                endsAt,
                midpoint,
                braking,
                ShipAngularRate.Zero);
            ShipKinematicState stopped = brakingPhase.StateAt(endsAt);
            if (stopped.Velocity != ShipVelocity.Zero
                || requireArrival
                && !ManeuverArrival.EvaluateTerminal(
                    stopped,
                    destination,
                    requestedHeading: null).IsSatisfied)
            {
                accelerationPhase = null;
                brakingPhase = null;
                return false;
            }

            return true;
        }
        catch (OverflowException)
        {
            accelerationPhase = null;
            brakingPhase = null;
            return false;
        }
    }

    private static long ResolveAcceleration(
        long displacementMillimeters,
        ulong durationMilliseconds)
    {
        Int128 numerator = checked(
            (Int128)displacementMillimeters
            * MillisecondsPerSecond * MillisecondsPerSecond);
        UInt128 denominator = checked(
            (UInt128)durationMilliseconds * durationMilliseconds);
        return checked((long)ManeuverKinematics.RoundSignedRatio(
            numerator,
            denominator));
    }

    private static bool WithinAccelerationLimit(
        ShipAcceleration acceleration,
        ManeuverAcceleration limit)
    {
        Int128 x = acceleration.MillimetersPerSecondSquaredX;
        Int128 y = acceleration.MillimetersPerSecondSquaredY;
        Int128 magnitudeSquared = checked(x * x + y * y);
        Int128 limitValue = limit.MillimetersPerSecondSquared;
        return magnitudeSquared <= checked(limitValue * limitValue);
    }

    private static bool WithinSpeedLimit(
        ShipVelocity velocity,
        ManeuverSpeed limit)
    {
        Int128 x = velocity.MillimetersPerSecondX;
        Int128 y = velocity.MillimetersPerSecondY;
        Int128 speedSquared = checked(x * x + y * y);
        Int128 limitValue = limit.MillimetersPerSecond;
        return speedSquared <= checked(limitValue * limitValue);
    }

    private static long DeltaMillimeters(
        SpatialCoordinate origin,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - origin.Units)
            * MillisecondsPerSecond));
}
