namespace GalaxyCommand.Simulation;

/// <summary>
/// Composite primary-thrust candidate that turns from exact rest to an exact
/// course heading before beginning triangular or capped-speed translation.
/// </summary>
public sealed record TurnThenSubCruiseManeuverPlan
{
    private const long MillimetersPerMeter = 1_000;

    private TurnThenSubCruiseManeuverPlan(
        SystemPosition destination,
        ShipHeading courseHeading,
        StationaryTurnManeuverPlan courseTurnPlan,
        AlignedSubCruisePlanSelection translationPlan)
    {
        Destination = destination;
        CourseHeading = courseHeading;
        CourseTurnPlan = courseTurnPlan;
        TranslationPlan = translationPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading CourseHeading { get; }

    public StationaryTurnManeuverPlan CourseTurnPlan { get; }

    public AlignedSubCruisePlanSelection TranslationPlan { get; }

    public SimulationTime StartsAt => CourseTurnPlan.StartsAt;

    public SimulationTime TranslationStartsAt => CourseTurnPlan.EndsAt;

    public SimulationTime EndsAt => TranslationPlan.Kind switch
    {
        AlignedSubCruisePlanKind.Triangular =>
            TranslationPlan.TriangularPlan!.EndsAt,
        AlignedSubCruisePlanKind.CappedSpeed =>
            TranslationPlan.CappedSpeedPlan!.EndsAt,
        _ => throw new InvalidOperationException(
            "A turn-then-sub-cruise plan has an invalid translation payload."),
    };

    /// <summary>
    /// Builds a primary-thrust candidate from exact rest by resolving the
    /// destination course, turning in place, and then scheduling an existing
    /// triangular or capped-speed plan. Returns false when no turn is needed,
    /// the discrete turn does not publish the exact course, arrival is already
    /// satisfied, or aligned translation cannot produce a concrete plan.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        ManeuverTurnRate turnRate,
        out TurnThenSubCruiseManeuverPlan? plan)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A turn-then-sub-cruise plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "A turn-then-sub-cruise plan requires zero starting velocity.",
                nameof(start));
        }

        if (ManeuverArrival.EvaluateTerminal(
                start,
                destination,
                requestedHeading: null).IsSatisfied)
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
        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            deltaX,
            deltaY);
        if (!StationaryTurnManeuverPlan.TryCreate(
                startsAt,
                start,
                start.Position,
                course,
                turnRate,
                out StationaryTurnManeuverPlan? courseTurn)
            || courseTurn is null)
        {
            plan = null;
            return false;
        }

        ShipKinematicState turned = courseTurn.StateAt(courseTurn.EndsAt);
        // Forward-only primary thrust must follow the published heading. A
        // tolerance-satisfied turn cannot silently borrow another direction.
        if (turned.Heading != course)
        {
            plan = null;
            return false;
        }

        AlignedSubCruisePlanSelection translation =
            AlignedSubCruisePlanner.Select(
                courseTurn.EndsAt,
                turned,
                destination,
                acceleration,
                braking,
                maximumSpeed);
        if (translation.Kind is not AlignedSubCruisePlanKind.Triangular
            and not AlignedSubCruisePlanKind.CappedSpeed)
        {
            plan = null;
            return false;
        }

        plan = new TurnThenSubCruiseManeuverPlan(
            destination,
            course,
            courseTurn,
            translation);
        return true;
    }

    /// <summary>
    /// Evaluates the course turn through its inclusive end boundary, then the
    /// concrete translation plan. Times outside the combined schedule reject.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Turn-then-sub-cruise time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return time <= TranslationStartsAt
            ? CourseTurnPlan.StateAt(time)
            : TranslationStateAt(time);
    }

    private static long DeltaMillimeters(
        SpatialCoordinate start,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - start.Units)
            * MillimetersPerMeter));

    /// <summary>
    /// Dispatches evaluation to the one concrete translation payload admitted
    /// during construction and rejects any broken union invariant.
    /// </summary>
    private ShipKinematicState TranslationStateAt(SimulationTime time) =>
        TranslationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } triangular,
            } => triangular.StateAt(time),
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } capped,
            } => capped.StateAt(time),
            _ => throw new InvalidOperationException(
                "A turn-then-sub-cruise plan has an invalid translation payload."),
        };
}
