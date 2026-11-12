namespace GalaxyCommand.Simulation;

/// <summary>
/// Starts course rotation from a moving state while translation retains
/// the existing braking trajectory, then joins an exact selected continuation.
/// </summary>
public sealed record MovingCourseTurnManeuverPlan
{
    private MovingCourseTurnManeuverPlan(SystemPosition destination, ShipHeading? requestedHeading,
        StationaryTurnManeuverPlan courseTurn, DecelerationManeuverSegment? cruiseDropoutPhase,
        DecelerationManeuverSegment brakingPhase, ExecutableBoundedTerminalManeuverPlan continuationPlan)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        CourseTurn = courseTurn;
        CruiseDropoutPhase = cruiseDropoutPhase;
        BrakingPhase = brakingPhase;
        ContinuationPlan = continuationPlan;
    }

    public SystemPosition Destination { get; }
    public ShipHeading? RequestedHeading { get; }
    public StationaryTurnManeuverPlan CourseTurn { get; }
    public DecelerationManeuverSegment? CruiseDropoutPhase { get; }
    public DecelerationManeuverSegment BrakingPhase { get; }
    public ExecutableBoundedTerminalManeuverPlan ContinuationPlan { get; }
    public SimulationTime StartsAt => CourseTurn.StartsAt;
    public SimulationTime EndsAt => ContinuationPlan.EndsAt;
    public SimulationTime PrefixEndsAt => BrakingPhase.EndsAt > CourseTurn.EndsAt
        ? BrakingPhase.EndsAt : CourseTurn.EndsAt;

    /// <summary>Identifies moving requests that require course rotation beyond the shared heading tolerance.</summary>
    internal static bool RequiresCourseTurn(ShipKinematicState start, SystemPosition destination) =>
        start.Velocity != ShipVelocity.Zero && start.Position != destination
        && Math.Abs(start.Heading.ShortestTurnTo(ResolveCourse(start.Position, destination)))
            > ManeuverArrival.ArrivalHeadingToleranceDegrees * 1_000;

    /// <summary>
    /// Builds an immediate shortest-arc course turn without rotating velocity.
    /// Ordinary motion retains precision braking; exact cruise speed first retains
    /// its dedicated dropout brake. Continuation starts after both braking and
    /// rotation finish, and never inserts another initial turn.
    /// </summary>
    public static bool TryCreate(SimulationTime startsAt, ShipKinematicState start, SystemPosition destination,
        ShipHeading? requestedHeading, EffectiveShipManeuverCapability capability, ManeuverObjective objective,
        out MovingCourseTurnManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
            throw new ArgumentException("A moving course turn requires a same-system destination.", nameof(destination));
        plan = null;
        if (!RequiresCourseTurn(start, destination)) return false;
        ShipHeading course = ResolveCourse(start.Position, destination);
        // The stationary plan is solely the angular instruction. Its artificial
        // zero velocity never supplies translational state to the moving ship.
        ShipKinematicState turnStart = start with { Velocity = ShipVelocity.Zero };
        if (!StationaryTurnManeuverPlan.TryCreate(startsAt, turnStart, start.Position, course, capability.TurnRate,
            out StationaryTurnManeuverPlan? turn) || turn is null) return false;

        DecelerationManeuverSegment? dropout = null;
        ShipKinematicState brakingStart = start;
        SimulationTime brakingStartsAt = startsAt;
        if (capability.CruiseSpeed.MillimetersPerSecond > capability.MaximumSubCruiseSpeed.MillimetersPerSecond
            && ManeuverVector.SpeedMagnitude(start.Velocity) == capability.CruiseSpeed.MillimetersPerSecond)
        {
            dropout = new(ManeuverDecelerationKind.ActiveBrake, startsAt, start,
                new ManeuverAcceleration(checked(capability.PrimaryAcceleration.MillimetersPerSecondSquared * 2)),
                capability.MaximumSubCruiseSpeed);
            brakingStartsAt = dropout.EndsAt;
            brakingStart = WithTurnHeading(dropout.StateAt(dropout.EndsAt), turn, dropout.EndsAt);
        }
        var braking = new DecelerationManeuverSegment(ManeuverDecelerationKind.ActiveBrake, brakingStartsAt,
            brakingStart, capability.PrecisionAcceleration);
        SimulationTime prefixEndsAt = braking.EndsAt > turn.EndsAt ? braking.EndsAt : turn.EndsAt;
        ShipKinematicState afterTurn = EvaluatePrefix(turn, dropout, braking, prefixEndsAt);
        BoundedTerminalPlanSelection continuation = BoundedTerminalManeuverPlanner.SelectWithoutMovingTurn(
            prefixEndsAt, afterTurn, destination, requestedHeading, capability, objective);
        if (continuation.ExecutablePlan is not { } executable) return false;
        plan = new(destination, requestedHeading, turn, dropout, braking, executable);
        return true;
    }

    /// <summary>
    /// Rebinds saved selected instructions without rerunning maneuver selection.
    /// Every translational stage and the child must join the composed moving
    /// state exactly; missing braking or changed headings reject.
    /// </summary>
    internal static MovingCourseTurnManeuverPlan RestoreSelected(SystemPosition destination, ShipHeading? requestedHeading,
        StationaryTurnManeuverPlan courseTurn, DecelerationManeuverSegment? cruiseDropoutPhase,
        DecelerationManeuverSegment brakingPhase, ExecutableBoundedTerminalManeuverPlan continuationPlan)
    {
        ArgumentNullException.ThrowIfNull(courseTurn);
        ArgumentNullException.ThrowIfNull(continuationPlan);
        ArgumentNullException.ThrowIfNull(brakingPhase);
        DecelerationManeuverSegment initial = cruiseDropoutPhase ?? brakingPhase;
        ShipKinematicState start = initial.Start;
        if (start.Velocity == ShipVelocity.Zero || start.Position == destination
            || start.Position.SystemId != destination.SystemId || initial.StartsAt != courseTurn.StartsAt
            || courseTurn.TurnPhase.Start.Position != start.Position || courseTurn.TurnPhase.Start.Heading != start.Heading
            || courseTurn.TurnPhase.Start.Velocity != ShipVelocity.Zero || courseTurn.TurnPhase.Acceleration != ShipAcceleration.Zero
            || courseTurn.Destination != start.Position || courseTurn.RequestedHeading != ResolveCourse(start.Position, destination))
            throw new ArgumentException("A saved moving turn must retain its initial course and exact angular instruction.");

        if (cruiseDropoutPhase is { } dropout)
        {
            if (dropout.Kind != ManeuverDecelerationKind.ActiveBrake || dropout.TargetSpeedMillimetersPerSecond == 0)
                throw new ArgumentException("A saved course turn must retain its complete bounded cruise dropout.");
            if (brakingPhase.StartsAt != dropout.EndsAt
                || brakingPhase.Start != WithTurnHeading(dropout.StateAt(dropout.EndsAt), courseTurn, dropout.EndsAt))
                throw new ArgumentException("Precision braking must join cruise dropout at the exact rotating state.");
        }
        if (brakingPhase.Kind != ManeuverDecelerationKind.ActiveBrake || brakingPhase.TargetSpeedMillimetersPerSecond != 0)
            throw new ArgumentException("A moving turn must retain precision active braking to rest.");

        var restored = new MovingCourseTurnManeuverPlan(destination, requestedHeading, courseTurn,
            cruiseDropoutPhase, brakingPhase, continuationPlan);
        if (continuationPlan.StartsAt != restored.PrefixEndsAt
            || continuationPlan.StateAt(continuationPlan.StartsAt) != restored.PrefixStateAt(restored.PrefixEndsAt)
            || !ManeuverArrival.EvaluateTerminal(continuationPlan.StateAt(continuationPlan.EndsAt), destination, requestedHeading).IsSatisfied)
            throw new ArgumentException("A saved moving turn must join its selected terminal continuation exactly.");
        return restored;
    }

    /// <summary>Evaluates the moving prefix and its selected continuation only inside their complete committed bounds.</summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt) throw new ArgumentOutOfRangeException(nameof(time));
        return time <= PrefixEndsAt ? PrefixStateAt(time) : ContinuationPlan.StateAt(time);
    }

    /// <summary>Combines exact braking/dropout translation with rotation, holding the stopped position until the turn completes.</summary>
    internal ShipKinematicState PrefixStateAt(SimulationTime time)
    {
        if (time < StartsAt || time > PrefixEndsAt) throw new ArgumentOutOfRangeException(nameof(time));
        return EvaluatePrefix(CourseTurn, CruiseDropoutPhase, BrakingPhase, time);
    }

    /// <summary>Clamps only completed component instructions, never extrapolating a deceleration or rotation phase.</summary>
    private static ShipKinematicState EvaluatePrefix(StationaryTurnManeuverPlan turn,
        DecelerationManeuverSegment? dropout, DecelerationManeuverSegment braking, SimulationTime time)
    {
        ShipKinematicState translation = dropout is not null && time <= dropout.EndsAt
            ? dropout.StateAt(time)
            : braking.StateAt(time <= braking.EndsAt ? time : braking.EndsAt);
        return WithTurnHeading(translation, turn, time);
    }

    /// <summary>Applies angular state independently so turning never rotates or replaces the velocity vector.</summary>
    private static ShipKinematicState WithTurnHeading(ShipKinematicState translation, StationaryTurnManeuverPlan turn,
        SimulationTime time) => translation with { Heading = turn.StateAt(time <= turn.EndsAt ? time : turn.EndsAt).Heading };

    /// <summary>Resolves the initial requested course using the same exact inverse projection as ordinary planning.</summary>
    private static ShipHeading ResolveCourse(SystemPosition start, SystemPosition destination) => ManeuverHeadingProjection.ResolveCourseHeading(
        checked((long)(((Int128)destination.Position.X.Units - start.Position.X.Units) * 1_000)),
        checked((long)(((Int128)destination.Position.Y.Units - start.Position.Y.Units) * 1_000)));
}
