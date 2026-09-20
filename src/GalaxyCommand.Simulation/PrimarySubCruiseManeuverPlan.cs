namespace GalaxyCommand.Simulation;

/// <summary>
/// Exact-course sub-cruise candidate that accelerates with forward primary
/// thrust and stops with heading-preserving precision thrust.
/// </summary>
public sealed record PrimarySubCruiseManeuverPlan
{
    private const long MillimetersPerMeter = 1_000;

    private PrimarySubCruiseManeuverPlan(
        SystemPosition destination,
        ShipHeading courseHeading,
        ManeuverAcceleration primaryAcceleration,
        ManeuverAcceleration brakingAcceleration,
        AlignedSubCruisePlanSelection translationPlan)
    {
        Destination = destination;
        CourseHeading = courseHeading;
        PrimaryAcceleration = primaryAcceleration;
        BrakingAcceleration = brakingAcceleration;
        TranslationPlan = translationPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading CourseHeading { get; }

    public ManeuverAcceleration PrimaryAcceleration { get; }

    public ManeuverAcceleration BrakingAcceleration { get; }

    public AlignedSubCruisePlanSelection TranslationPlan { get; }

    public SimulationTime StartsAt => TranslationPlan.Kind switch
    {
        AlignedSubCruisePlanKind.Triangular =>
            TranslationPlan.TriangularPlan!.StartsAt,
        AlignedSubCruisePlanKind.CappedSpeed =>
            TranslationPlan.CappedSpeedPlan!.StartsAt,
        _ => throw InvalidTranslation(),
    };

    public SimulationTime EndsAt => TranslationPlan.Kind switch
    {
        AlignedSubCruisePlanKind.Triangular =>
            TranslationPlan.TriangularPlan!.EndsAt,
        AlignedSubCruisePlanKind.CappedSpeed =>
            TranslationPlan.CappedSpeedPlan!.EndsAt,
        _ => throw InvalidTranslation(),
    };

    /// <summary>
    /// Builds a candidate from exact rest only when the live heading exactly
    /// matches the CORDIC course. Forward acceleration uses the resolved
    /// primary rate, while braking uses the resolved all-direction precision
    /// rate so heading remains unchanged. Unsupported schedules return false.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        EffectiveShipManeuverCapability capability,
        out PrimarySubCruiseManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A primary sub-cruise plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "A primary sub-cruise plan requires zero starting velocity.",
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

        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            DeltaMillimeters(
                start.Position.Position.X,
                destination.Position.X),
            DeltaMillimeters(
                start.Position.Position.Y,
                destination.Position.Y));
        if (start.Heading != course)
        {
            plan = null;
            return false;
        }

        AlignedSubCruisePlanSelection translation =
            AlignedSubCruisePlanner.Select(
                startsAt,
                start,
                destination,
                capability.PrimaryAcceleration,
                capability.PrecisionAcceleration,
                capability.MaximumSubCruiseSpeed);
        if (translation.Kind is not AlignedSubCruisePlanKind.Triangular
            and not AlignedSubCruisePlanKind.CappedSpeed)
        {
            plan = null;
            return false;
        }

        plan = new PrimarySubCruiseManeuverPlan(
            destination,
            course,
            capability.PrimaryAcceleration,
            capability.PrecisionAcceleration,
            translation);
        return true;
    }

    /// <summary>
    /// Evaluates the concrete primary-acceleration translation and preserves
    /// its bounded schedule rejection outside the plan interval.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time) =>
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
            _ => throw InvalidTranslation(),
        };

    private static long DeltaMillimeters(
        SpatialCoordinate start,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - start.Units)
            * MillimetersPerMeter));

    private static InvalidOperationException InvalidTranslation() =>
        new("A primary sub-cruise plan has an invalid translation payload.");
}
