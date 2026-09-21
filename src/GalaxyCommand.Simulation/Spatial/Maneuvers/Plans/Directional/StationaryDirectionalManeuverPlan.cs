namespace GalaxyCommand.Simulation;

/// <summary>
/// One complete stationary directional candidate: a concrete translation and
/// an optional final stationary turn evaluated at the authoritative stop.
/// </summary>
public sealed record StationaryDirectionalManeuverPlan
{
    private StationaryDirectionalManeuverPlan(
        StationaryDirectionalPlanKind kind,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        PrimarySubCruiseManeuverPlan? primarySubCruisePlan,
        TurnThenSubCruiseManeuverPlan? turnThenPrimaryPlan,
        PrecisionSubCruiseManeuverPlan? precisionSubCruisePlan,
        StationaryTurnManeuverPlan? finalTurnPlan)
    {
        Kind = kind;
        Destination = destination;
        RequestedHeading = requestedHeading;
        PrimarySubCruisePlan = primarySubCruisePlan;
        TurnThenPrimaryPlan = turnThenPrimaryPlan;
        PrecisionSubCruisePlan = precisionSubCruisePlan;
        FinalTurnPlan = finalTurnPlan;
    }

    public StationaryDirectionalPlanKind Kind { get; }

    public SystemPosition Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public PrimarySubCruiseManeuverPlan? PrimarySubCruisePlan { get; }

    public TurnThenSubCruiseManeuverPlan? TurnThenPrimaryPlan { get; }

    public PrecisionSubCruiseManeuverPlan? PrecisionSubCruisePlan { get; }

    public StationaryTurnManeuverPlan? FinalTurnPlan { get; }

    public SimulationTime StartsAt => Kind switch
    {
        StationaryDirectionalPlanKind.PrimarySubCruise =>
            PrimarySubCruisePlan!.StartsAt,
        StationaryDirectionalPlanKind.TurnThenPrimary =>
            TurnThenPrimaryPlan!.StartsAt,
        StationaryDirectionalPlanKind.PrecisionSubCruise =>
            PrecisionSubCruisePlan!.StartsAt,
        _ => throw InvalidTranslation(),
    };

    public SimulationTime TranslationEndsAt => Kind switch
    {
        StationaryDirectionalPlanKind.PrimarySubCruise =>
            PrimarySubCruisePlan!.EndsAt,
        StationaryDirectionalPlanKind.TurnThenPrimary =>
            TurnThenPrimaryPlan!.EndsAt,
        StationaryDirectionalPlanKind.PrecisionSubCruise =>
            PrecisionSubCruisePlan!.EndsAt,
        _ => throw InvalidTranslation(),
    };

    public SimulationTime EndsAt =>
        FinalTurnPlan?.EndsAt ?? TranslationEndsAt;

    /// <summary>
    /// Composes exact-course primary translation with an optional final turn.
    /// The final turn begins at the materialized translation endpoint rather
    /// than snapping position to the requested destination.
    /// </summary>
    internal static bool TryCreate(
        PrimarySubCruiseManeuverPlan translation,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverTurnRate turnRate,
        out StationaryDirectionalManeuverPlan? plan) =>
        TryCreate(
            StationaryDirectionalPlanKind.PrimarySubCruise,
            translation,
            turnThenPrimaryPlan: null,
            precisionSubCruisePlan: null,
            destination,
            requestedHeading,
            turnRate,
            translation.EndsAt,
            translation.StateAt,
            out plan);

    /// <summary>
    /// Composes turn-then-primary translation with an optional final turn at
    /// its authoritative stopped position.
    /// </summary>
    internal static bool TryCreate(
        TurnThenSubCruiseManeuverPlan translation,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverTurnRate turnRate,
        out StationaryDirectionalManeuverPlan? plan) =>
        TryCreate(
            StationaryDirectionalPlanKind.TurnThenPrimary,
            primarySubCruisePlan: null,
            translation,
            precisionSubCruisePlan: null,
            destination,
            requestedHeading,
            turnRate,
            translation.EndsAt,
            translation.StateAt,
            out plan);

    /// <summary>
    /// Composes heading-preserving precision translation with an optional
    /// final turn at its authoritative stopped position.
    /// </summary>
    internal static bool TryCreate(
        PrecisionSubCruiseManeuverPlan translation,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverTurnRate turnRate,
        out StationaryDirectionalManeuverPlan? plan) =>
        TryCreate(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            primarySubCruisePlan: null,
            turnThenPrimaryPlan: null,
            translation,
            destination,
            requestedHeading,
            turnRate,
            translation.EndsAt,
            translation.StateAt,
            out plan);

    /// <summary>
    /// Evaluates translation through its inclusive endpoint, then any final
    /// stationary turn. Times outside the complete schedule reject.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Stationary directional time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return time <= TranslationEndsAt
            ? TranslationStateAt(time)
            : FinalTurnPlan!.StateAt(time);
    }

    /// <summary>
    /// Validates the translation endpoint, schedules a needed terminal turn,
    /// and rejects any discrete result that misses the original destination
    /// or requested heading tolerance.
    /// </summary>
    private static bool TryCreate(
        StationaryDirectionalPlanKind kind,
        PrimarySubCruiseManeuverPlan? primarySubCruisePlan,
        TurnThenSubCruiseManeuverPlan? turnThenPrimaryPlan,
        PrecisionSubCruiseManeuverPlan? precisionSubCruisePlan,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverTurnRate turnRate,
        SimulationTime translationEndsAt,
        Func<SimulationTime, ShipKinematicState> translationStateAt,
        out StationaryDirectionalManeuverPlan? plan)
    {
        ShipKinematicState stopped = translationStateAt(translationEndsAt);
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

        plan = new StationaryDirectionalManeuverPlan(
            kind,
            destination,
            requestedHeading,
            primarySubCruisePlan,
            turnThenPrimaryPlan,
            precisionSubCruisePlan,
            finalTurn);
        return true;
    }

    /// <summary>
    /// Dispatches evaluation to the sole translation payload admitted during
    /// construction and rejects any broken union invariant.
    /// </summary>
    private ShipKinematicState TranslationStateAt(SimulationTime time) =>
        Kind switch
        {
            StationaryDirectionalPlanKind.PrimarySubCruise =>
                PrimarySubCruisePlan!.StateAt(time),
            StationaryDirectionalPlanKind.TurnThenPrimary =>
                TurnThenPrimaryPlan!.StateAt(time),
            StationaryDirectionalPlanKind.PrecisionSubCruise =>
                PrecisionSubCruisePlan!.StateAt(time),
            _ => throw InvalidTranslation(),
        };

    private static InvalidOperationException InvalidTranslation() =>
        new("A stationary directional plan has an invalid translation payload.");
}
