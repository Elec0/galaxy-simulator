namespace GalaxyCommand.Simulation;

/// <summary>
/// One complete aligned moving terminal candidate: a concrete translation and
/// an optional final stationary turn at the authoritative stopped position.
/// </summary>
public sealed record MovingAlignedTerminalManeuverPlan
{
    private MovingAlignedTerminalManeuverPlan(
        SystemPosition destination,
        ShipHeading? requestedHeading,
        AlignedSubCruisePlanSelection translationPlan,
        StationaryTurnManeuverPlan? finalTurnPlan)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        TranslationPlan = translationPlan;
        FinalTurnPlan = finalTurnPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public AlignedSubCruisePlanSelection TranslationPlan { get; }

    public StationaryTurnManeuverPlan? FinalTurnPlan { get; }

    public SimulationTime StartsAt => TranslationPlan switch
    {
        {
            Kind: AlignedSubCruisePlanKind.ImmediateBraking,
            ImmediateBrakingPlan: { } immediate,
        } => immediate.StartsAt,
        {
            Kind: AlignedSubCruisePlanKind.Triangular,
            TriangularPlan: { } triangular,
        } => triangular.StartsAt,
        {
            Kind: AlignedSubCruisePlanKind.CappedSpeed,
            CappedSpeedPlan: { } capped,
        } => capped.StartsAt,
        _ => throw InvalidTranslation(),
    };

    public SimulationTime TranslationEndsAt => TranslationPlan switch
    {
        {
            Kind: AlignedSubCruisePlanKind.ImmediateBraking,
            ImmediateBrakingPlan: { } immediate,
        } => immediate.EndsAt,
        {
            Kind: AlignedSubCruisePlanKind.Triangular,
            TriangularPlan: { } triangular,
        } => triangular.EndsAt,
        {
            Kind: AlignedSubCruisePlanKind.CappedSpeed,
            CappedSpeedPlan: { } capped,
        } => capped.EndsAt,
        _ => throw InvalidTranslation(),
    };

    public SimulationTime EndsAt =>
        FinalTurnPlan?.EndsAt ?? TranslationEndsAt;

    /// <summary>
    /// Builds a complete terminal candidate from one concrete aligned plan.
    /// Translation must stop within destination tolerance before a requested
    /// final turn can be scheduled. Failure publishes no partial candidate.
    /// </summary>
    public static bool TryCreate(
        AlignedSubCruisePlanSelection translationPlan,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverTurnRate turnRate,
        out MovingAlignedTerminalManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(translationPlan);
        if (translationPlan.Kind is not AlignedSubCruisePlanKind.ImmediateBraking
            and not AlignedSubCruisePlanKind.Triangular
            and not AlignedSubCruisePlanKind.CappedSpeed)
        {
            plan = null;
            return false;
        }

        SimulationTime translationEndsAt = TranslationEndsAtOf(
            translationPlan);
        ShipKinematicState stopped = TranslationStateAt(
            translationPlan,
            translationEndsAt);
        if (stopped.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "An aligned terminal plan requires a destination in the translation system.",
                nameof(destination));
        }

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
                    translationEndsAt,
                    stopped,
                    stopped.Position,
                    heading,
                    turnRate,
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

        plan = new MovingAlignedTerminalManeuverPlan(
            destination,
            requestedHeading,
            translationPlan,
            finalTurn);
        return true;
    }

    /// <summary>
    /// Evaluates translation through its inclusive endpoint, then any final
    /// turn. Times outside the complete schedule reject.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Moving aligned terminal time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return time <= TranslationEndsAt
            ? TranslationStateAt(TranslationPlan, time)
            : FinalTurnPlan!.StateAt(time);
    }

    /// <summary>
    /// Returns the endpoint owned by the sole concrete aligned translation
    /// payload and rejects a broken union invariant.
    /// </summary>
    private static SimulationTime TranslationEndsAtOf(
        AlignedSubCruisePlanSelection translationPlan) =>
        translationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.ImmediateBraking,
                ImmediateBrakingPlan: { } immediate,
            } => immediate.EndsAt,
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } triangular,
            } => triangular.EndsAt,
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } capped,
            } => capped.EndsAt,
            _ => throw InvalidTranslation(),
        };

    /// <summary>
    /// Dispatches evaluation to the sole concrete aligned translation payload
    /// admitted during construction.
    /// </summary>
    private static ShipKinematicState TranslationStateAt(
        AlignedSubCruisePlanSelection translationPlan,
        SimulationTime time) =>
        translationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.ImmediateBraking,
                ImmediateBrakingPlan: { } immediate,
            } => immediate.StateAt(time),
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
        new("A moving aligned terminal plan has an invalid translation payload.");
}
