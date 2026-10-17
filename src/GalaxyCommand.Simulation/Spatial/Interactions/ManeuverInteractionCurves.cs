namespace GalaxyCommand.Simulation;

/// <summary>Projects concrete immutable plan payloads into original kinematic curves without fitting sampled positions.</summary>
internal static class ManeuverInteractionCurves
{
    internal static IEnumerable<InteractionPhaseCurve> From(StationaryTurnManeuverPlan plan) =>
        [InteractionPhaseCurve.From(plan.TurnPhase)];

    /// <summary>Preserves optional acceleration, course turns, cruise discontinuities, and terminal braking.</summary>
    internal static IEnumerable<InteractionPhaseCurve> From(CruiseTerminalManeuverPlan plan)
    {
        if (plan.CourseTurnPlan is { } turn)
            foreach (InteractionPhaseCurve curve in From(turn)) yield return curve;
        if (plan.AccelerationPhase is { } acceleration) yield return InteractionPhaseCurve.From(acceleration);
        yield return InteractionPhaseCurve.From(plan.MovingSpoolPhase);
        yield return InteractionPhaseCurve.From(plan.CruisePhase);
        yield return InteractionPhaseCurve.From(plan.BrakingPhase);
        if (plan.FinalTurnPlan is { } final)
            foreach (InteractionPhaseCurve curve in From(final)) yield return curve;
    }

    internal static IEnumerable<InteractionPhaseCurve> From(ForcedCruiseDropoutTerminalManeuverPlan plan) =>
        new[] { InteractionPhaseCurve.From(plan.DropoutPhase) }.Concat(plan.ContinuationPlan.InteractionCurves);

    /// <summary>Retains the selected continuation after the initial velocity-clearing brake.</summary>
    internal static IEnumerable<InteractionPhaseCurve> From(BrakeThenStationaryDirectionalManeuverPlan plan) =>
        new[] { InteractionPhaseCurve.From(plan.BrakingPhase) }.Concat(
            plan.StationaryPlan is { } stationary ? From(stationary) :
            plan.ReducedThrustPlan is { } reduced ? From(reduced) : []);

    internal static IEnumerable<InteractionPhaseCurve> From(ReducedThrustTerminalManeuverPlan plan) =>
        AppendTurn([InteractionPhaseCurve.From(plan.AccelerationPhase), InteractionPhaseCurve.From(plan.BrakingPhase)], plan.FinalTurnPlan);

    internal static IEnumerable<InteractionPhaseCurve> From(MovingAlignedTerminalManeuverPlan plan) =>
        AppendTurn(From(plan.TranslationPlan), plan.FinalTurnPlan);

    /// <summary>Dispatches only the concrete aligned payload already selected by the movement planner.</summary>
    internal static IEnumerable<InteractionPhaseCurve> From(AlignedSubCruisePlanSelection selection) => selection switch
    {
        { Kind: AlignedSubCruisePlanKind.ImmediateBraking, ImmediateBrakingPlan: { } plan } =>
            [InteractionPhaseCurve.From(plan.BrakingPhase)],
        { Kind: AlignedSubCruisePlanKind.Triangular, TriangularPlan: { } plan } =>
            [InteractionPhaseCurve.From(plan.AccelerationPhase), InteractionPhaseCurve.From(plan.BrakingPhase)],
        { Kind: AlignedSubCruisePlanKind.CappedSpeed, CappedSpeedPlan: { } plan } => From(plan),
        _ => throw new InvalidOperationException("Aligned selection has no interaction curve payload."),
    };

    /// <summary>Preserves an optional acceleration phase before capped-speed travel.</summary>
    private static IEnumerable<InteractionPhaseCurve> From(CappedSpeedManeuverPlan plan)
    {
        if (plan.AccelerationPhase is { } acceleration) yield return InteractionPhaseCurve.From(acceleration);
        yield return InteractionPhaseCurve.From(plan.CappedTravelPhase);
        yield return InteractionPhaseCurve.From(plan.BrakingPhase);
    }

    /// <summary>Retains directional translation, including precision and turn-first variants, before any final turn.</summary>
    internal static IEnumerable<InteractionPhaseCurve> From(StationaryDirectionalManeuverPlan plan)
    {
        IEnumerable<InteractionPhaseCurve> translation = plan.Kind switch
        {
            StationaryDirectionalPlanKind.PrimarySubCruise when plan.PrimarySubCruisePlan is { } primary => From(primary.TranslationPlan),
            StationaryDirectionalPlanKind.TurnThenPrimary when plan.TurnThenPrimaryPlan is { } turn =>
                From(turn.CourseTurnPlan).Concat(From(turn.TranslationPlan)),
            StationaryDirectionalPlanKind.TurnThenPrecisionSubCruise when plan.TurnThenPrecisionPlan is { } precision =>
                From(precision.CourseTurnPlan).Concat(From(precision.PrecisionPlan.TranslationPlan)),
            StationaryDirectionalPlanKind.PrecisionSubCruise when plan.PrecisionSubCruisePlan is { } precision => From(precision.TranslationPlan),
            _ => throw new InvalidOperationException("Directional selection has no interaction curve payload."),
        };
        return AppendTurn(translation, plan.FinalTurnPlan);
    }

    /// <summary>Appends the independently scheduled terminal turn without changing translation geometry.</summary>
    private static IEnumerable<InteractionPhaseCurve> AppendTurn(IEnumerable<InteractionPhaseCurve> translation,
        StationaryTurnManeuverPlan? turn) => turn is null ? translation : translation.Concat(From(turn));
}
