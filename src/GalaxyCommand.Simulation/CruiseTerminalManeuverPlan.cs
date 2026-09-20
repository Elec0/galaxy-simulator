namespace GalaxyCommand.Simulation;

/// <summary>
/// Complete straight cruise candidate from rest: optional course turn,
/// acceleration to maneuver speed, moving spool, cruise, planned dropout,
/// terminal braking, and an optional final stationary turn.
/// </summary>
public sealed record CruiseTerminalManeuverPlan
{
    private const long MillimetersPerMeter = 1_000;
    private const ulong MillisecondsPerSecond = 1_000;

    private CruiseTerminalManeuverPlan(
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ShipHeading courseHeading,
        StationaryTurnManeuverPlan? courseTurnPlan,
        AnalyticManeuverSegment? accelerationPhase,
        AnalyticManeuverSegment movingSpoolPhase,
        AnalyticManeuverSegment cruisePhase,
        DecelerationManeuverSegment brakingPhase,
        StationaryTurnManeuverPlan? finalTurnPlan)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        CourseHeading = courseHeading;
        CourseTurnPlan = courseTurnPlan;
        AccelerationPhase = accelerationPhase;
        MovingSpoolPhase = movingSpoolPhase;
        CruisePhase = cruisePhase;
        BrakingPhase = brakingPhase;
        FinalTurnPlan = finalTurnPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public ShipHeading CourseHeading { get; }

    public StationaryTurnManeuverPlan? CourseTurnPlan { get; }

    public AnalyticManeuverSegment? AccelerationPhase { get; }

    public AnalyticManeuverSegment MovingSpoolPhase { get; }

    public AnalyticManeuverSegment CruisePhase { get; }

    public DecelerationManeuverSegment BrakingPhase { get; }

    public StationaryTurnManeuverPlan? FinalTurnPlan { get; }

    public SimulationTime StartsAt =>
        CourseTurnPlan?.StartsAt
            ?? AccelerationPhase?.StartsAt
            ?? MovingSpoolPhase.StartsAt;

    public SimulationTime? AccelerationStartsAt => AccelerationPhase?.StartsAt;

    public SimulationTime SpoolStartsAt => MovingSpoolPhase.StartsAt;

    public SimulationTime CruiseStartsAt => CruisePhase.StartsAt;

    public SimulationTime DropoutAt => CruisePhase.EndsAt;

    public SimulationTime TranslationEndsAt => BrakingPhase.EndsAt;

    public SimulationTime EndsAt =>
        FinalTurnPlan?.EndsAt ?? TranslationEndsAt;

    /// <summary>
    /// Builds a complete cruise candidate from exact rest. The outbound course
    /// and forward acceleration use deterministic CORDIC. Unsupported rounded
    /// directions, nonpositive cruise intervals, or terminal misses return
    /// false without publishing a partial plan.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        EffectiveShipManeuverCapability capability,
        out CruiseTerminalManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A cruise plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "A cruise plan from rest requires zero starting velocity.",
                nameof(start));
        }

        long deltaX = DeltaMillimeters(
            start.Position.Position.X,
            destination.Position.X);
        long deltaY = DeltaMillimeters(
            start.Position.Position.Y,
            destination.Position.Y);
        if (deltaX == 0 && deltaY == 0)
        {
            plan = null;
            return false;
        }

        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            deltaX,
            deltaY);
        StationaryTurnManeuverPlan? courseTurn = null;
        ShipKinematicState aligned = start;
        SimulationTime accelerationStartsAt = startsAt;
        if (start.Heading != course)
        {
            if (!StationaryTurnManeuverPlan.TryCreate(
                    startsAt,
                    start,
                    start.Position,
                    course,
                    capability.TurnRate,
                    out courseTurn)
                || courseTurn is null)
            {
                plan = null;
                return false;
            }

            aligned = courseTurn.StateAt(courseTurn.EndsAt);
            if (aligned.Heading != course)
            {
                plan = null;
                return false;
            }

            accelerationStartsAt = courseTurn.EndsAt;
        }

        ShipAcceleration forwardAcceleration =
            ManeuverHeadingProjection.ProjectForward(
                course,
                capability.PrimaryAcceleration);
        ShipVelocity maneuverVelocity =
            ManeuverHeadingProjection.ProjectForwardVelocity(
                course,
                capability.MaximumSubCruiseSpeed);
        ShipVelocity cruiseVelocity =
            ManeuverHeadingProjection.ProjectForwardVelocity(
                course,
                capability.CruiseSpeed);
        if (!IsExactForwardVelocity(
                deltaX,
                deltaY,
                maneuverVelocity,
                capability.MaximumSubCruiseSpeed)
            || !IsExactForwardVelocity(
                deltaX,
                deltaY,
                cruiseVelocity,
                capability.CruiseSpeed))
        {
            plan = null;
            return false;
        }

        SimulationDuration accelerationDuration = RoundedPositiveDuration(
            capability.MaximumSubCruiseSpeed.MillimetersPerSecond,
            capability.PrimaryAcceleration.MillimetersPerSecondSquared);
        var accelerationPhase = new AnalyticManeuverSegment(
            accelerationStartsAt,
            accelerationStartsAt.Add(accelerationDuration),
            aligned,
            forwardAcceleration,
            ShipAngularRate.Zero);
        ShipKinematicState atManeuverSpeed = accelerationPhase.StateAt(
            accelerationPhase.EndsAt);
        if (atManeuverSpeed.Velocity != maneuverVelocity)
        {
            plan = null;
            return false;
        }

        return TryCreateAligned(
            accelerationPhase.EndsAt,
            destination,
            requestedHeading,
            course,
            courseTurn,
            accelerationPhase,
            atManeuverSpeed,
            maneuverVelocity,
            cruiseVelocity,
            capability,
            out plan);
    }

    /// <summary>
    /// Builds a complete cruise candidate for a ship already moving straight
    /// at its maximum sub-cruise speed. The candidate always schedules the
    /// full authored spool duration, so interrupted spool progress is never
    /// inherited by a replacement generation.
    /// </summary>
    public static bool TryCreateFromManeuverSpeed(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        EffectiveShipManeuverCapability capability,
        out CruiseTerminalManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A cruise plan requires a destination in the starting system.",
                nameof(destination));
        }

        long deltaX = DeltaMillimeters(
            start.Position.Position.X,
            destination.Position.X);
        long deltaY = DeltaMillimeters(
            start.Position.Position.Y,
            destination.Position.Y);
        if (deltaX == 0 && deltaY == 0)
        {
            plan = null;
            return false;
        }

        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            deltaX,
            deltaY);
        ShipVelocity maneuverVelocity =
            ManeuverHeadingProjection.ProjectForwardVelocity(
                course,
                capability.MaximumSubCruiseSpeed);
        ShipVelocity cruiseVelocity =
            ManeuverHeadingProjection.ProjectForwardVelocity(
                course,
                capability.CruiseSpeed);
        if (start.Heading != course
            || start.Velocity != maneuverVelocity
            || !IsExactForwardVelocity(
                deltaX,
                deltaY,
                maneuverVelocity,
                capability.MaximumSubCruiseSpeed)
            || !IsExactForwardVelocity(
                deltaX,
                deltaY,
                cruiseVelocity,
                capability.CruiseSpeed))
        {
            plan = null;
            return false;
        }

        return TryCreateAligned(
            startsAt,
            destination,
            requestedHeading,
            course,
            courseTurn: null,
            accelerationPhase: null,
            start,
            maneuverVelocity,
            cruiseVelocity,
            capability,
            out plan);
    }

    /// <summary>
    /// Completes a validated aligned cruise candidate from its authoritative
    /// maneuver-speed state. Optional acceleration contributes distance and a
    /// phase only for rest-start plans.
    /// </summary>
    private static bool TryCreateAligned(
        SimulationTime spoolStartsAt,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ShipHeading course,
        StationaryTurnManeuverPlan? courseTurn,
        AnalyticManeuverSegment? accelerationPhase,
        ShipKinematicState atManeuverSpeed,
        ShipVelocity maneuverVelocity,
        ShipVelocity cruiseVelocity,
        EffectiveShipManeuverCapability capability,
        out CruiseTerminalManeuverPlan? plan)
    {
        var movingSpoolPhase = new AnalyticManeuverSegment(
            spoolStartsAt,
            spoolStartsAt.Add(capability.MovingSpoolDuration),
            atManeuverSpeed,
            ShipAcceleration.Zero,
            ShipAngularRate.Zero);
        SystemPosition routeStart = accelerationPhase?.Start.Position
            ?? atManeuverSpeed.Position;
        long deltaX = DeltaMillimeters(
            routeStart.Position.X,
            destination.Position.X);
        long deltaY = DeltaMillimeters(
            routeStart.Position.Y,
            destination.Position.Y);
        ulong distance = ManeuverVector.SpeedMagnitude(
            new ShipVelocity(deltaX, deltaY));
        ulong accelerationDistance = accelerationPhase is null
            ? 0
            : ManeuverBraking.RequiredDistanceMillimeters(
                maneuverVelocity,
                ShipVelocity.Zero,
                capability.PrimaryAcceleration);
        ulong spoolDistance = RoundedPositiveRatio(
            checked((UInt128)capability.MaximumSubCruiseSpeed
                .MillimetersPerSecond
                * capability.MovingSpoolDuration.Milliseconds),
            MillisecondsPerSecond);
        ulong brakingDistance = ManeuverBraking.RequiredDistanceMillimeters(
            maneuverVelocity,
            ShipVelocity.Zero,
            capability.PrecisionAcceleration);
        UInt128 nonCruiseDistance = checked(
            (UInt128)accelerationDistance + spoolDistance + brakingDistance);
        if (nonCruiseDistance >= distance)
        {
            plan = null;
            return false;
        }

        ulong cruiseDistance = (ulong)(distance - nonCruiseDistance);
        SimulationDuration cruiseDuration = RoundedPositiveDuration(
            cruiseDistance,
            capability.CruiseSpeed.MillimetersPerSecond);
        ShipKinematicState spoolEnd = movingSpoolPhase.StateAt(
            movingSpoolPhase.EndsAt);
        var cruiseStart = spoolEnd with { Velocity = cruiseVelocity };
        var cruisePhase = new AnalyticManeuverSegment(
            movingSpoolPhase.EndsAt,
            movingSpoolPhase.EndsAt.Add(cruiseDuration),
            cruiseStart,
            ShipAcceleration.Zero,
            ShipAngularRate.Zero);
        ShipKinematicState cruiseEnd = cruisePhase.StateAt(cruisePhase.EndsAt);
        var dropout = cruiseEnd with { Velocity = maneuverVelocity };
        var brakingPhase = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            cruisePhase.EndsAt,
            dropout,
            capability.PrecisionAcceleration);
        ShipKinematicState stopped = brakingPhase.StateAt(brakingPhase.EndsAt);
        ManeuverArrivalEvaluation arrival = ManeuverArrival.EvaluateTerminal(
            stopped,
            destination,
            requestedHeading);
        StationaryTurnManeuverPlan? finalTurn = null;
        if (!arrival.IsSatisfied)
        {
            if (!arrival.PositionSatisfied
                || !arrival.VelocitySatisfied
                || requestedHeading is not { } heading
                || !StationaryTurnManeuverPlan.TryCreate(
                    brakingPhase.EndsAt,
                    stopped,
                    stopped.Position,
                    heading,
                    capability.TurnRate,
                    out finalTurn)
                || finalTurn is null
                || !ManeuverArrival.EvaluateTerminal(
                    finalTurn.StateAt(finalTurn.EndsAt),
                    destination,
                    requestedHeading).IsSatisfied)
            {
                plan = null;
                return false;
            }
        }

        plan = new CruiseTerminalManeuverPlan(
            destination,
            requestedHeading,
            course,
            courseTurn,
            accelerationPhase,
            movingSpoolPhase,
            cruisePhase,
            brakingPhase,
            finalTurn);
        return true;
    }

    /// <summary>
    /// Evaluates the complete cruise schedule. Cruise entry and dropout publish
    /// their post-transition velocities at the exact shared phase boundary.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Cruise terminal time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        if (CourseTurnPlan is { } turn && time <= turn.EndsAt)
        {
            return turn.StateAt(time);
        }

        if (AccelerationPhase is { } acceleration
            && time < acceleration.EndsAt)
        {
            return acceleration.StateAt(time);
        }

        if (time < MovingSpoolPhase.EndsAt)
        {
            return MovingSpoolPhase.StateAt(time);
        }

        if (time < CruisePhase.EndsAt)
        {
            return CruisePhase.StateAt(time);
        }

        if (time <= BrakingPhase.EndsAt)
        {
            return BrakingPhase.StateAt(time);
        }

        return FinalTurnPlan!.StateAt(time);
    }

    private static long DeltaMillimeters(
        SpatialCoordinate start,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - start.Units)
            * MillimetersPerMeter));

    /// <summary>
    /// Requires the CORDIC projection to retain the authored magnitude and
    /// point generally toward the destination. Final arrival validation owns
    /// any accumulated fixed-point lateral error.
    /// </summary>
    private static bool IsExactForwardVelocity(
        long deltaX,
        long deltaY,
        ShipVelocity velocity,
        ManeuverSpeed expectedSpeed)
    {
        Int128 dot = checked(
            (Int128)deltaX * velocity.MillimetersPerSecondX
            + (Int128)deltaY * velocity.MillimetersPerSecondY);
        return dot > 0
            && ManeuverVector.SpeedMagnitude(velocity)
                == expectedSpeed.MillimetersPerSecond;
    }

    /// <summary>
    /// Converts a positive distance or velocity ratio to a positive
    /// millisecond phase using normal rounding.
    /// </summary>
    private static SimulationDuration RoundedPositiveDuration(
        ulong numeratorValue,
        ulong denominatorValue)
    {
        UInt128 numerator = checked(
            (UInt128)numeratorValue * MillisecondsPerSecond);
        ulong rounded = RoundedPositiveRatio(numerator, denominatorValue);
        return new SimulationDuration(Math.Max(1, rounded));
    }

    /// <summary>
    /// Publishes one positive exact ratio with nearest-unit rounding and upward
    /// half ties, rejecting values outside the unsigned duration range.
    /// </summary>
    private static ulong RoundedPositiveRatio(
        UInt128 numerator,
        UInt128 denominator)
    {
        UInt128 quotient = numerator / denominator;
        UInt128 remainder = numerator % denominator;
        UInt128 half = denominator / 2;
        if (remainder > half
            || denominator % 2 == 0 && remainder == half)
        {
            quotient++;
        }

        return quotient <= ulong.MaxValue
            ? (ulong)quotient
            : throw new OverflowException(
                "A cruise maneuver ratio exceeds the published range.");
    }
}
