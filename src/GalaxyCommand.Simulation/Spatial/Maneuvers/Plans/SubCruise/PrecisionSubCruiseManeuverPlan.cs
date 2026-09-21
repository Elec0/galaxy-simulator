namespace GalaxyCommand.Simulation;

/// <summary>
/// Heading-preserving sub-cruise candidate that uses only the resolved
/// all-direction precision rate for acceleration and active braking.
/// </summary>
public sealed record PrecisionSubCruiseManeuverPlan
{
    private PrecisionSubCruiseManeuverPlan(
        SystemPosition destination,
        ManeuverAcceleration precisionAcceleration,
        AlignedSubCruisePlanSelection translationPlan)
    {
        Destination = destination;
        PrecisionAcceleration = precisionAcceleration;
        TranslationPlan = translationPlan;
    }

    public SystemPosition Destination { get; }

    public ManeuverAcceleration PrecisionAcceleration { get; }

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
    /// Builds a heading-preserving candidate from exact rest using the
    /// effective capability's precision acceleration for both velocity gain
    /// and active braking. Returns false when arrival is already satisfied or
    /// the bounded aligned planner cannot publish triangular or capped travel.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        EffectiveShipManeuverCapability capability,
        out PrecisionSubCruiseManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A precision sub-cruise plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "A precision sub-cruise plan requires zero starting velocity.",
                nameof(start));
        }

        ManeuverAcceleration precision = capability.PrecisionAcceleration;
        AlignedSubCruisePlanSelection translation =
            AlignedSubCruisePlanner.Select(
                startsAt,
                start,
                destination,
                precision,
                precision,
                capability.MaximumSubCruiseSpeed);
        if (translation.Kind is not AlignedSubCruisePlanKind.Triangular
            and not AlignedSubCruisePlanKind.CappedSpeed)
        {
            plan = null;
            return false;
        }

        plan = new PrecisionSubCruiseManeuverPlan(
            destination,
            precision,
            translation);
        return true;
    }

    /// <summary>
    /// Evaluates the concrete precision translation and preserves its bounded
    /// schedule rejection for times before the start or after arrival.
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

    private static InvalidOperationException InvalidTranslation() =>
        new("A precision sub-cruise plan has an invalid translation payload.");
}
