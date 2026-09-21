namespace GalaxyCommand.Simulation;

/// <summary>
/// Canonical instruction active during one analytically scheduled maneuver
/// phase. Passive coast is reserved for plans that explicitly own drag.
/// </summary>
public enum ManeuverPhaseKind
{
    Turn,
    Accelerate,
    MovingSpool,
    CruiseTravel,
    CruiseDropoutBrake,
    CappedSpeedTravel,
    CoastUnderPassiveDrag,
    ActiveBrake,
    TerminalSettle,
}

/// <summary>
/// One immutable maneuver phase and its inclusive scheduled boundaries.
/// </summary>
public readonly record struct ManeuverScheduledPhase
{
    /// <summary>
    /// Admits positive analytic phases and the sole zero-duration terminal
    /// settle boundary produced by an already-satisfied goal.
    /// </summary>
    internal ManeuverScheduledPhase(
        ManeuverPhaseKind kind,
        SimulationTime startsAt,
        SimulationTime endsAt)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown maneuver phase kind.");
        }

        if (kind == ManeuverPhaseKind.TerminalSettle
            ? endsAt != startsAt
            : endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                endsAt,
                "Analytic phases must have positive duration and terminal settle must have zero duration.");
        }

        Kind = kind;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public ManeuverPhaseKind Kind { get; }

    public SimulationTime StartsAt { get; }

    public SimulationTime EndsAt { get; }
}

/// <summary>
/// Projects existing concrete maneuver plans into one canonical ordered phase
/// schedule without recalculating any boundary.
/// </summary>
internal static class ManeuverPhaseSchedules
{
    internal static ManeuverScheduledPhase[] TerminalSettle(
        SimulationTime at) =>
        [new(ManeuverPhaseKind.TerminalSettle, at, at)];

    internal static ManeuverScheduledPhase[] From(
        StationaryTurnManeuverPlan plan) =>
        [Turn(plan)];

    /// <summary>
    /// Projects the selected complete directional payload and preserves any
    /// terminal heading turn appended after translation.
    /// </summary>
    internal static ManeuverScheduledPhase[] From(
        StationaryDirectionalPlanSelection selection) =>
        From(selection.CompletePlan
            ?? throw InvalidPayload(selection.Kind));

    internal static ManeuverScheduledPhase[] From(
        MovingAlignedTerminalManeuverPlan plan) =>
        AppendFinalTurn(
            From(plan.TranslationPlan),
            plan.FinalTurnPlan);

    /// <summary>
    /// Preserves the cruise plan's discontinuous velocity transitions as
    /// shared phase boundaries without inventing zero-duration instructions.
    /// </summary>
    internal static ManeuverScheduledPhase[] From(
        CruiseTerminalManeuverPlan plan)
    {
        ManeuverScheduledPhase[] afterAcceleration =
        [
            new(
                ManeuverPhaseKind.MovingSpool,
                plan.MovingSpoolPhase.StartsAt,
                plan.MovingSpoolPhase.EndsAt),
            new(
                ManeuverPhaseKind.CruiseTravel,
                plan.CruisePhase.StartsAt,
                plan.CruisePhase.EndsAt),
            Deceleration(plan.BrakingPhase),
        ];
        ManeuverScheduledPhase[] translation =
            plan.AccelerationPhase is { } acceleration
                ? Concat(
                    [new ManeuverScheduledPhase(
                        ManeuverPhaseKind.Accelerate,
                        acceleration.StartsAt,
                        acceleration.EndsAt)],
                    afterAcceleration)
                : afterAcceleration;
        ManeuverScheduledPhase[] withCourseTurn = plan.CourseTurnPlan is null
            ? translation
            : Concat([Turn(plan.CourseTurnPlan)], translation);
        return AppendFinalTurn(withCourseTurn, plan.FinalTurnPlan);
    }

    /// <summary>
    /// Prefixes the complete ordinary continuation with the dedicated forced
    /// dropout brake while preserving the continuation's exact boundaries.
    /// </summary>
    internal static ManeuverScheduledPhase[] From(
        ForcedCruiseDropoutTerminalManeuverPlan plan) =>
        Concat(
            [new ManeuverScheduledPhase(
                ManeuverPhaseKind.CruiseDropoutBrake,
                plan.DropoutPhase.StartsAt,
                plan.DropoutPhase.EndsAt)],
            plan.ContinuationPlan.Phases);

    internal static ManeuverScheduledPhase[] From(
        BrakeThenStationaryDirectionalManeuverPlan plan) =>
        plan.StationaryPlan is { } stationary
            ? Concat([Deceleration(plan.BrakingPhase)], From(stationary))
            : plan.ReducedThrustPlan is { } reduced
                ? Concat([Deceleration(plan.BrakingPhase)], From(reduced))
                : [Deceleration(plan.BrakingPhase)];

    internal static ManeuverScheduledPhase[] From(
        ReducedThrustTerminalManeuverPlan plan) =>
        AppendFinalTurn(
            [
                new(
                    ManeuverPhaseKind.Accelerate,
                    plan.AccelerationPhase.StartsAt,
                    plan.AccelerationPhase.EndsAt),
                new(
                    ManeuverPhaseKind.ActiveBrake,
                    plan.BrakingPhase.StartsAt,
                    plan.BrakingPhase.EndsAt),
            ],
            plan.FinalTurnPlan);

    /// <summary>
    /// Projects the sole aligned payload, including optional capped-speed
    /// acceleration, and rejects non-executable selector outcomes.
    /// </summary>
    internal static ManeuverScheduledPhase[] From(
        AlignedSubCruisePlanSelection selection) =>
        selection switch
        {
            {
                Kind: AlignedSubCruisePlanKind.ImmediateBraking,
                ImmediateBrakingPlan: { } plan,
            } => [Deceleration(plan.BrakingPhase)],
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } plan,
            } =>
            [
                new(
                    ManeuverPhaseKind.Accelerate,
                    plan.AccelerationPhase.StartsAt,
                    plan.AccelerationPhase.EndsAt),
                Deceleration(plan.BrakingPhase),
            ],
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } plan,
            } => From(plan),
            _ => throw InvalidPayload(selection.Kind),
        };

    /// <summary>
    /// Projects the selected translation union, then appends the independently
    /// scheduled terminal heading turn when present.
    /// </summary>
    private static ManeuverScheduledPhase[] From(
        StationaryDirectionalManeuverPlan plan)
    {
        ManeuverScheduledPhase[] translation = plan.Kind switch
        {
            StationaryDirectionalPlanKind.PrimarySubCruise
                when plan.PrimarySubCruisePlan is { } primary =>
                From(primary.TranslationPlan),
            StationaryDirectionalPlanKind.TurnThenPrimary
                when plan.TurnThenPrimaryPlan is { } turnThenPrimary =>
                Concat(
                    From(turnThenPrimary.CourseTurnPlan),
                    From(turnThenPrimary.TranslationPlan)),
            StationaryDirectionalPlanKind.PrecisionSubCruise
                when plan.PrecisionSubCruisePlan is { } precision =>
                From(precision.TranslationPlan),
            _ => throw InvalidPayload(plan.Kind),
        };
        return AppendFinalTurn(translation, plan.FinalTurnPlan);
    }

    /// <summary>
    /// Preserves the capped plan's optional acceleration while always
    /// retaining its positive travel and terminal braking boundaries.
    /// </summary>
    private static ManeuverScheduledPhase[] From(
        CappedSpeedManeuverPlan plan)
    {
        var travel = new ManeuverScheduledPhase(
            ManeuverPhaseKind.CappedSpeedTravel,
            plan.CappedTravelPhase.StartsAt,
            plan.CappedTravelPhase.EndsAt);
        ManeuverScheduledPhase braking = Deceleration(plan.BrakingPhase);
        if (plan.AccelerationPhase is not { } acceleration)
        {
            return [travel, braking];
        }

        return
        [
            new(
                ManeuverPhaseKind.Accelerate,
                acceleration.StartsAt,
                acceleration.EndsAt),
            travel,
            braking,
        ];
    }

    private static ManeuverScheduledPhase Deceleration(
        DecelerationManeuverSegment phase) =>
        new(
            phase.Kind switch
            {
                ManeuverDecelerationKind.PassiveDrag =>
                    ManeuverPhaseKind.CoastUnderPassiveDrag,
                ManeuverDecelerationKind.ActiveBrake =>
                    ManeuverPhaseKind.ActiveBrake,
                _ => throw InvalidPayload(phase.Kind),
            },
            phase.StartsAt,
            phase.EndsAt);

    private static ManeuverScheduledPhase Turn(
        StationaryTurnManeuverPlan plan) =>
        new(ManeuverPhaseKind.Turn, plan.StartsAt, plan.EndsAt);

    private static ManeuverScheduledPhase[] AppendFinalTurn(
        ManeuverScheduledPhase[] phases,
        StationaryTurnManeuverPlan? finalTurn) =>
        finalTurn is null
            ? phases
            : Concat(phases, [Turn(finalTurn)]);

    private static ManeuverScheduledPhase[] Concat(
        params IReadOnlyList<ManeuverScheduledPhase>[] schedules) =>
        schedules.SelectMany(static schedule => schedule).ToArray();

    private static InvalidOperationException InvalidPayload<TKind>(
        TKind kind) =>
        new($"The {kind} maneuver outcome has no phase schedule payload.");
}
