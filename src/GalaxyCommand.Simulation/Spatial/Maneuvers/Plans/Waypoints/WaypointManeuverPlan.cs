using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// One complete terminal trajectory whose admitted fly-through waypoints are
/// represented by additional non-terminal phase boundaries. Collinear routes
/// split an existing plan; noncollinear routes add bounded corner transitions.
/// </summary>
public sealed record WaypointManeuverPlan
{
    private const ulong MillisecondsPerSecond = 1_000;
    private const ulong ArrivalSearchWindowMilliseconds = 60_000;

    private WaypointManeuverPlan(
        ExecutableBoundedTerminalManeuverPlan plan,
        IEnumerable<int> waypointPhaseIndices)
    {
        Plan = plan;
        WaypointPhaseIndices = new ReadOnlyCollection<int>(
            waypointPhaseIndices.ToArray());
    }

    public ExecutableBoundedTerminalManeuverPlan Plan { get; }

    public IReadOnlyList<int> WaypointPhaseIndices { get; }

    /// <summary>
    /// Admits strictly ordered waypoints on the selected trajectory's straight
    /// start-to-terminal line and locates their nearest millisecond boundary.
    /// </summary>
    public static bool TryCreate(
        ExecutableBoundedTerminalManeuverPlan terminalPlan,
        ShipKinematicState start,
        SystemPosition terminalDestination,
        IReadOnlyList<SystemPosition> waypoints,
        out WaypointManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(terminalPlan);
        ArgumentNullException.ThrowIfNull(waypoints);
        plan = null;
        if (waypoints.Count == 0
            || terminalPlan.StateAt(terminalPlan.StartsAt) != start)
        {
            return false;
        }

        if (terminalDestination.SystemId != start.Position.SystemId
            || !ManeuverArrival.EvaluateTerminal(
                terminalPlan.StateAt(terminalPlan.EndsAt),
                terminalDestination,
                requestedHeading: null).PositionSatisfied)
        {
            return false;
        }

        Int128 pathX = (Int128)terminalDestination.Position.X.Units
            - start.Position.Position.X.Units;
        Int128 pathY = (Int128)terminalDestination.Position.Y.Units
            - start.Position.Position.Y.Units;
        Int128 pathLengthSquared = checked(pathX * pathX + pathY * pathY);
        if (pathLengthSquared == 0)
        {
            return false;
        }

        var boundaries = new SimulationTime[waypoints.Count];
        Int128 previousProgress = 0;
        SimulationTime previousBoundary = terminalPlan.StartsAt;
        for (int index = 0; index < waypoints.Count; index++)
        {
            SystemPosition waypoint = waypoints[index];
            if (waypoint.SystemId != start.Position.SystemId)
            {
                return false;
            }

            Int128 waypointX = (Int128)waypoint.Position.X.Units
                - start.Position.Position.X.Units;
            Int128 waypointY = (Int128)waypoint.Position.Y.Units
                - start.Position.Position.Y.Units;
            Int128 cross = checked(pathX * waypointY - pathY * waypointX);
            Int128 progress = checked(pathX * waypointX + pathY * waypointY);
            if (cross != 0
                || progress <= previousProgress
                || progress >= pathLengthSquared
                || !TryLocateBoundary(
                    terminalPlan,
                    start.Position,
                    pathX,
                    pathY,
                    progress,
                    waypoint,
                    out SimulationTime boundary)
                || boundary <= previousBoundary)
            {
                return false;
            }

            boundaries[index] = boundary;
            previousBoundary = boundary;
            previousProgress = progress;
        }

        ExecutableBoundedTerminalManeuverPlan split =
            terminalPlan.WithAdditionalBoundaries(boundaries);
        int[] waypointPhaseIndices = boundaries
            .Select(boundary => FindBoundaryPhase(split, boundary))
            .ToArray();
        plan = new WaypointManeuverPlan(split, waypointPhaseIndices);
        return true;
    }

    /// <summary>
    /// Builds a complete analytic route through one or more nonterminal
    /// waypoints. Each corner is crossed within arrival tolerance with a
    /// nonzero velocity aligned to its outgoing leg; the existing bounded
    /// planner owns the final terminal approach. Failure publishes no plan.
    /// </summary>
    public static bool TryCreateRoute(
        SimulationTime startsAt,
        ShipKinematicState start,
        IReadOnlyList<SystemPosition> destinations,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective,
        out WaypointManeuverPlan? plan)
        => TryCreateRoute(
            startsAt,
            start,
            destinations,
            capability,
            objective,
            requestedHeading: null,
            out plan);

    /// <summary>
    /// Builds a complete analytic route and applies the optional heading only
    /// to its terminal plan. Intermediate waypoint crossing remains governed
    /// solely by position and outgoing velocity.
    /// </summary>
    public static bool TryCreateRoute(
        SimulationTime startsAt,
        ShipKinematicState start,
        IReadOnlyList<SystemPosition> destinations,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective,
        ShipHeading? requestedHeading,
        out WaypointManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(capability);
        plan = null;
        if (destinations.Count < 2
            || destinations.Any(destination =>
                destination.SystemId != start.Position.SystemId))
        {
            return false;
        }

        var transitions = new List<AnalyticManeuverSegment>();
        var waypointPhaseIndices = new List<int>(destinations.Count - 1);
        ShipKinematicState current = start;
        SimulationTime cursor = startsAt;
        for (int index = 0; index < destinations.Count - 1; index++)
        {
            SystemPosition waypoint = destinations[index];
            SystemPosition outgoingDestination = destinations[index + 1];
            if (!TrySelectOutgoingVelocity(
                    cursor,
                    waypoint,
                    outgoingDestination,
                    current.Heading,
                    capability,
                    objective,
                    out ShipVelocity outgoingVelocity)
                || !TryCreateCornerTransition(
                    cursor,
                    current,
                    waypoint,
                    outgoingVelocity,
                    capability.PrecisionAcceleration,
                    capability.MaximumSubCruiseSpeed,
                    out AnalyticManeuverSegment[]? corner))
            {
                return false;
            }

            transitions.AddRange(corner!);
            waypointPhaseIndices.Add(transitions.Count - 1);
            cursor = corner![^1].EndsAt;
            current = corner[^1].StateAt(cursor);
        }

        SystemPosition terminalDestination = destinations[^1];
        BoundedTerminalPlanSelection terminalSelection =
            BoundedTerminalManeuverPlanner.Select(
                cursor,
                current,
                terminalDestination,
                requestedHeading,
                capability,
                objective);
        if (terminalSelection.ExecutablePlan is not { } terminalPlan)
        {
            return false;
        }

        ExecutableBoundedTerminalManeuverPlan complete =
            ExecutableBoundedTerminalManeuverPlan.WaypointRoute(
                transitions,
                terminalPlan);
        plan = new WaypointManeuverPlan(complete, waypointPhaseIndices);
        return true;
    }

    /// <summary>
    /// Uses the outgoing leg's ordinary from-rest terminal candidate to choose
    /// the crossing velocity. This keeps corner speed under the same objective,
    /// capability, CORDIC direction, and discrete phase policy as local travel.
    /// </summary>
    private static bool TrySelectOutgoingVelocity(
        SimulationTime startsAt,
        SystemPosition waypoint,
        SystemPosition outgoingDestination,
        ShipHeading heading,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective,
        out ShipVelocity velocity)
    {
        velocity = ShipVelocity.Zero;
        var rest = new ShipKinematicState(
            waypoint,
            ShipVelocity.Zero,
            heading);
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                startsAt,
                rest,
                outgoingDestination,
                requestedHeading: null,
                capability,
                objective);
        if (selection.ExecutablePlan is not { } outgoingPlan)
        {
            return false;
        }

        foreach (ManeuverScheduledPhase phase in outgoingPlan.Phases)
        {
            ShipVelocity candidate = outgoingPlan.StateAt(phase.EndsAt).Velocity;
            if (candidate != ShipVelocity.Zero)
            {
                velocity = candidate;
                return IsVelocityAligned(candidate, waypoint, outgoingDestination);
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves two equal-duration constant precision-thrust phases that meet
    /// the waypoint and outgoing-velocity boundary. The shortest representable
    /// admitted duration wins; nearby durations absorb fixed-point rounding.
    /// </summary>
    private static bool TryCreateCornerTransition(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition waypoint,
        ShipVelocity outgoingVelocity,
        ManeuverAcceleration precisionLimit,
        ManeuverSpeed maximumSpeed,
        out AnalyticManeuverSegment[]? transition)
    {
        transition = null;
        ulong maximumHalfDuration = (ulong.MaxValue - startsAt.Milliseconds) / 2;
        if (maximumHalfDuration == 0)
        {
            return false;
        }

        ulong upper = 1;
        while (upper < maximumHalfDuration
            && !TryResolveCornerAccelerations(
                start,
                waypoint,
                outgoingVelocity,
                upper,
                precisionLimit,
                out _,
                out _))
        {
            upper = upper > maximumHalfDuration / 2
                ? maximumHalfDuration
                : upper * 2;
        }

        if (!TryResolveCornerAccelerations(
                start,
                waypoint,
                outgoingVelocity,
                upper,
                precisionLimit,
                out _,
                out _))
        {
            return false;
        }

        ulong lower = 1;
        while (lower < upper)
        {
            ulong midpoint = lower + ((upper - lower) / 2);
            if (TryResolveCornerAccelerations(
                    start,
                    waypoint,
                    outgoingVelocity,
                    midpoint,
                    precisionLimit,
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

        ulong speedUpper = lower;
        while (speedUpper < maximumHalfDuration
            && !TryBuildCornerSegments(
                startsAt,
                start,
                waypoint,
                outgoingVelocity,
                precisionLimit,
                maximumSpeed,
                speedUpper,
                out _,
                out _,
                out _))
        {
            speedUpper = speedUpper > maximumHalfDuration / 2
                ? maximumHalfDuration
                : speedUpper * 2;
        }

        if (!TryBuildCornerSegments(
                startsAt,
                start,
                waypoint,
                outgoingVelocity,
                precisionLimit,
                maximumSpeed,
                speedUpper,
                out _,
                out _,
                out _))
        {
            return false;
        }

        ulong speedLower = lower;
        while (speedLower < speedUpper)
        {
            ulong midpoint = speedLower + ((speedUpper - speedLower) / 2);
            if (TryBuildCornerSegments(
                    startsAt,
                    start,
                    waypoint,
                    outgoingVelocity,
                    precisionLimit,
                    maximumSpeed,
                    midpoint,
                    out _,
                    out _,
                    out _))
            {
                speedUpper = midpoint;
            }
            else
            {
                speedLower = midpoint + 1;
            }
        }

        ulong finalCandidate = Math.Min(
            maximumHalfDuration,
            checked(speedLower + Math.Min(
                ArrivalSearchWindowMilliseconds,
                maximumHalfDuration - speedLower)));
        for (ulong halfDuration = speedLower;
            halfDuration <= finalCandidate;
            halfDuration++)
        {
            if (!TryBuildCornerSegments(
                    startsAt,
                    start,
                    waypoint,
                    outgoingVelocity,
                    precisionLimit,
                    maximumSpeed,
                    halfDuration,
                    out AnalyticManeuverSegment? first,
                    out AnalyticManeuverSegment? second,
                    out ShipKinematicState crossed))
            {
                continue;
            }

            if (crossed.Position == waypoint
                && crossed.Velocity != ShipVelocity.Zero
                && SameCourse(crossed.Velocity, outgoingVelocity))
            {
                transition = [first!, second!];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Materializes one candidate pair only when both acceleration and speed
    /// caps hold at its observable boundaries. Position and course tolerance
    /// remain the caller's final admission checks.
    /// </summary>
    private static bool TryBuildCornerSegments(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition waypoint,
        ShipVelocity outgoingVelocity,
        ManeuverAcceleration precisionLimit,
        ManeuverSpeed maximumSpeed,
        ulong halfDuration,
        out AnalyticManeuverSegment? first,
        out AnalyticManeuverSegment? second,
        out ShipKinematicState crossed)
    {
        first = null;
        second = null;
        crossed = default;
        if (!TryResolveCornerAccelerations(
                start,
                waypoint,
                outgoingVelocity,
                halfDuration,
                precisionLimit,
                out ShipAcceleration firstAcceleration,
                out ShipAcceleration secondAcceleration))
        {
            return false;
        }

        SimulationTime midpoint = new(checked(
            startsAt.Milliseconds + halfDuration));
        SimulationTime endsAt = new(checked(
            midpoint.Milliseconds + halfDuration));
        first = new AnalyticManeuverSegment(
            startsAt,
            midpoint,
            start,
            firstAcceleration,
            ShipAngularRate.Zero);
        ShipKinematicState middle = first.StateAt(midpoint);
        secondAcceleration = new ShipAcceleration(
            ResolveVelocityCorrection(
                middle.Velocity.MillimetersPerSecondX,
                outgoingVelocity.MillimetersPerSecondX,
                halfDuration),
            ResolveVelocityCorrection(
                middle.Velocity.MillimetersPerSecondY,
                outgoingVelocity.MillimetersPerSecondY,
                halfDuration));
        if (!WithinAccelerationLimit(secondAcceleration, precisionLimit))
        {
            return false;
        }

        second = new AnalyticManeuverSegment(
            midpoint,
            endsAt,
            middle,
            secondAcceleration,
            ShipAngularRate.Zero);
        crossed = second.StateAt(endsAt);
        return WithinSpeedLimit(middle.Velocity, maximumSpeed)
            && WithinSpeedLimit(crossed.Velocity, maximumSpeed);
    }

    private static long ResolveVelocityCorrection(
        long startingVelocity,
        long targetVelocity,
        ulong durationMilliseconds)
    {
        Int128 numerator = checked(
            ((Int128)targetVelocity - startingVelocity)
            * MillisecondsPerSecond);
        return checked((long)ManeuverKinematics.RoundSignedRatio(
            numerator,
            durationMilliseconds));
    }

    /// <summary>
    /// Solves the continuous two-phase boundary equations, publishes each
    /// acceleration component with normal rounding, and applies the precision
    /// vector magnitude cap to both phases.
    /// </summary>
    private static bool TryResolveCornerAccelerations(
        ShipKinematicState start,
        SystemPosition waypoint,
        ShipVelocity outgoingVelocity,
        ulong halfDurationMilliseconds,
        ManeuverAcceleration precisionLimit,
        out ShipAcceleration first,
        out ShipAcceleration second)
    {
        first = ShipAcceleration.Zero;
        second = ShipAcceleration.Zero;
        if (halfDurationMilliseconds == 0)
        {
            return false;
        }

        try
        {
            first = new ShipAcceleration(
                ResolveCornerAccelerationComponent(
                    DeltaMillimeters(
                        start.Position.Position.X,
                        waypoint.Position.X),
                    start.Velocity.MillimetersPerSecondX,
                    outgoingVelocity.MillimetersPerSecondX,
                    halfDurationMilliseconds,
                    firstPhase: true),
                ResolveCornerAccelerationComponent(
                    DeltaMillimeters(
                        start.Position.Position.Y,
                        waypoint.Position.Y),
                    start.Velocity.MillimetersPerSecondY,
                    outgoingVelocity.MillimetersPerSecondY,
                    halfDurationMilliseconds,
                    firstPhase: true));
            second = new ShipAcceleration(
                ResolveCornerAccelerationComponent(
                    DeltaMillimeters(
                        start.Position.Position.X,
                        waypoint.Position.X),
                    start.Velocity.MillimetersPerSecondX,
                    outgoingVelocity.MillimetersPerSecondX,
                    halfDurationMilliseconds,
                    firstPhase: false),
                ResolveCornerAccelerationComponent(
                    DeltaMillimeters(
                        start.Position.Position.Y,
                        waypoint.Position.Y),
                    start.Velocity.MillimetersPerSecondY,
                    outgoingVelocity.MillimetersPerSecondY,
                    halfDurationMilliseconds,
                    firstPhase: false));
        }
        catch (OverflowException)
        {
            return false;
        }

        return WithinAccelerationLimit(first, precisionLimit)
            && WithinAccelerationLimit(second, precisionLimit);
    }

    private static long ResolveCornerAccelerationComponent(
        long displacementMillimeters,
        long startingVelocity,
        long outgoingVelocity,
        ulong halfDurationMilliseconds,
        bool firstPhase)
    {
        Int128 duration = halfDurationMilliseconds;
        Int128 displacementTerm = checked(
            (Int128)2 * displacementMillimeters
            * MillisecondsPerSecond * MillisecondsPerSecond);
        Int128 velocityCoefficient = firstPhase
            ? checked((Int128)3 * startingVelocity + outgoingVelocity)
            : checked((Int128)startingVelocity + 3 * (Int128)outgoingVelocity);
        Int128 velocityTerm = checked(
            velocityCoefficient * MillisecondsPerSecond * duration);
        Int128 numerator = firstPhase
            ? checked(displacementTerm - velocityTerm)
            : checked(-displacementTerm + velocityTerm);
        UInt128 denominator = checked(
            (UInt128)2
            * halfDurationMilliseconds
            * halfDurationMilliseconds);
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

    private static bool SameCourse(
        ShipVelocity left,
        ShipVelocity right) =>
        ManeuverHeadingProjection.ResolveCourseHeading(
            left.MillimetersPerSecondX,
            left.MillimetersPerSecondY)
        == ManeuverHeadingProjection.ResolveCourseHeading(
            right.MillimetersPerSecondX,
            right.MillimetersPerSecondY);

    private static bool IsVelocityAligned(
        ShipVelocity velocity,
        SystemPosition origin,
        SystemPosition destination)
    {
        if (velocity == ShipVelocity.Zero || origin == destination)
        {
            return false;
        }

        ShipHeading velocityCourse = ManeuverHeadingProjection.ResolveCourseHeading(
            velocity.MillimetersPerSecondX,
            velocity.MillimetersPerSecondY);
        ShipHeading destinationCourse = ManeuverHeadingProjection.ResolveCourseHeading(
            DeltaMillimeters(origin.Position.X, destination.Position.X),
            DeltaMillimeters(origin.Position.Y, destination.Position.Y));
        return velocityCourse == destinationCourse;
    }

    private static long DeltaMillimeters(
        SpatialCoordinate origin,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - origin.Units)
            * MillisecondsPerSecond));

    private static bool TryLocateBoundary(
        ExecutableBoundedTerminalManeuverPlan plan,
        SystemPosition origin,
        Int128 pathX,
        Int128 pathY,
        Int128 targetProgress,
        SystemPosition waypoint,
        out SimulationTime boundary)
    {
        ulong lower = plan.StartsAt.Milliseconds;
        ulong upper = plan.EndsAt.Milliseconds;
        while (lower < upper)
        {
            ulong midpoint = lower + ((upper - lower) / 2);
            Int128 progress = ProjectProgress(
                plan.StateAt(new SimulationTime(midpoint)).Position,
                origin,
                pathX,
                pathY);
            if (progress < targetProgress)
            {
                lower = checked(midpoint + 1);
            }
            else
            {
                upper = midpoint;
            }
        }

        SimulationTime later = new(lower);
        SimulationTime selected = later;
        Int128 selectedDistance = DistanceSquared(
            plan.StateAt(later).Position,
            waypoint);
        if (lower > plan.StartsAt.Milliseconds)
        {
            var earlier = new SimulationTime(lower - 1);
            Int128 earlierDistance = DistanceSquared(
                plan.StateAt(earlier).Position,
                waypoint);
            if (earlierDistance <= selectedDistance)
            {
                selected = earlier;
            }
        }

        ShipKinematicState state = plan.StateAt(selected);
        boundary = selected;
        return selected > plan.StartsAt
            && selected < plan.EndsAt
            && state.Velocity != ShipVelocity.Zero
            && ManeuverArrival.IsFlyThroughWaypointReached(state, waypoint);
    }

    private static Int128 ProjectProgress(
        SystemPosition position,
        SystemPosition origin,
        Int128 pathX,
        Int128 pathY)
    {
        Int128 x = (Int128)position.Position.X.Units
            - origin.Position.X.Units;
        Int128 y = (Int128)position.Position.Y.Units
            - origin.Position.Y.Units;
        return checked(pathX * x + pathY * y);
    }

    private static Int128 DistanceSquared(
        SystemPosition left,
        SystemPosition right)
    {
        Int128 x = (Int128)left.Position.X.Units - right.Position.X.Units;
        Int128 y = (Int128)left.Position.Y.Units - right.Position.Y.Units;
        return checked(x * x + y * y);
    }

    private static int FindBoundaryPhase(
        ExecutableBoundedTerminalManeuverPlan plan,
        SimulationTime boundary)
    {
        for (int index = 0; index < plan.Phases.Count - 1; index++)
        {
            if (plan.Phases[index].EndsAt == boundary)
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            "A waypoint boundary was not retained in the split maneuver schedule.");
    }
}
