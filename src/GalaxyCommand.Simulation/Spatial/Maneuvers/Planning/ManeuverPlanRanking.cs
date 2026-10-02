namespace GalaxyCommand.Simulation;

/// <summary>
/// Extracts comparable deterministic metrics from implemented maneuver plans.
/// </summary>
public static class ManeuverPlanRanking
{
    private static readonly ManeuverProfileKey StationaryTurnProfile =
        new("stationary-turn");
    private static readonly ManeuverProfileKey ImmediateBrakingProfile =
        new("immediate-braking");
    private static readonly ManeuverProfileKey ShortMoveTriangularProfile =
        new("short-move-triangular");
    private static readonly ManeuverProfileKey CappedSpeedProfile =
        new("capped-speed");
    private static readonly ManeuverProfileKey TurnThenTriangularProfile =
        new("turn-then-short-move-triangular");
    private static readonly ManeuverProfileKey TurnThenCappedSpeedProfile =
        new("turn-then-capped-speed");
    private static readonly ManeuverProfileKey TurnThenPrecisionTriangularProfile =
        new("turn-then-precision-short-move-triangular");
    private static readonly ManeuverProfileKey TurnThenPrecisionCappedSpeedProfile =
        new("turn-then-precision-capped-speed");
    private static readonly ManeuverProfileKey PrecisionTriangularProfile =
        new("precision-short-move-triangular");
    private static readonly ManeuverProfileKey PrecisionCappedSpeedProfile =
        new("precision-capped-speed");
    private static readonly ManeuverProfileKey PrimaryTriangularProfile =
        new("primary-short-move-triangular");
    private static readonly ManeuverProfileKey PrimaryCappedSpeedProfile =
        new("primary-capped-speed");
    private static readonly ManeuverProfileKey PrecisionBrakeTerminalProfile =
        new("precision-brake-terminal");
    private static readonly ManeuverProfileKey CruiseTerminalProfile =
        new("cruise-terminal");
    private static readonly ManeuverProfileKey ReducedThrustTerminalProfile =
        new("reduced-thrust-terminal");

    /// <summary>
    /// Ranks a stationary turn as one zero-distance phase.
    /// </summary>
    public static ManeuverCandidateRank Rank(StationaryTurnManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(0),
            phaseCount: 1,
            StationaryTurnProfile);
    }

    /// <summary>
    /// Ranks immediate braking by its complete analytic stopping distance.
    /// </summary>
    public static ManeuverCandidateRank Rank(ShortMoveImmediateBrakingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        DecelerationManeuverSegment phase = plan.BrakingPhase;
        ulong pathDistance = ManeuverBraking.RequiredDistanceMillimeters(
            phase.Start.Velocity,
            ShipVelocity.Zero,
            phase.Deceleration);
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(pathDistance),
            phaseCount: 1,
            ImmediateBrakingProfile);
    }

    /// <summary>
    /// Ranks a triangular move by its authored scalar path and two phases.
    /// </summary>
    public static ManeuverCandidateRank Rank(ShortMoveTriangularPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(plan.Profile.DistanceMillimeters),
            phaseCount: 2,
            ShortMoveTriangularProfile);
    }

    /// <summary>
    /// Ranks capped travel using the phases actually present in its schedule.
    /// </summary>
    public static ManeuverCandidateRank Rank(CappedSpeedManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        int phaseCount = plan.AccelerationPhase is null ? 2 : 3;
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(plan.Profile.DistanceMillimeters),
            phaseCount,
            CappedSpeedProfile);
    }

    /// <summary>
    /// Ranks reduced-thrust travel by direct endpoint displacement and its two
    /// admitted analytic phases.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        ReducedThrustTerminalManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ShipKinematicState start = plan.StateAt(plan.StartsAt);
        long x = checked((long)(
            ((Int128)plan.Destination.Position.X.Units
                - start.Position.Position.X.Units)
            * 1_000));
        long y = checked((long)(
            ((Int128)plan.Destination.Position.Y.Units
                - start.Position.Position.Y.Units)
            * 1_000));
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(ManeuverVector.SpeedMagnitude(
                new ShipVelocity(x, y))),
            phaseCount: plan.FinalTurnPlan is null ? 2 : 3,
            ReducedThrustTerminalProfile);
    }

    /// <summary>
    /// Ranks a course-turn composite by retaining the translation path and
    /// adding the stationary turn to its actual scheduled phase count.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        TurnThenSubCruiseManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ManeuverCandidateRank translation = plan.TranslationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } triangular,
            } => Rank(triangular),
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } capped,
            } => Rank(capped),
            _ => throw new InvalidOperationException(
                "A turn-then-sub-cruise plan has an invalid translation payload."),
        };
        ManeuverProfileKey profile = plan.TranslationPlan.Kind
            == AlignedSubCruisePlanKind.Triangular
                ? TurnThenTriangularProfile
                : TurnThenCappedSpeedProfile;
        return new ManeuverCandidateRank(
            translation.ArrivesAt,
            translation.PathDistance,
            checked(translation.PhaseCount + 1),
            profile);
    }

    /// <summary>
    /// Ranks heading-preserving precision travel using the underlying
    /// translation schedule and a distinct stable precision profile identity.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        PrecisionSubCruiseManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ManeuverCandidateRank translation = plan.TranslationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } triangular,
            } => Rank(triangular),
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } capped,
            } => Rank(capped),
            _ => throw new InvalidOperationException(
                "A precision sub-cruise plan has an invalid translation payload."),
        };
        ManeuverProfileKey profile = plan.TranslationPlan.Kind
            == AlignedSubCruisePlanKind.Triangular
                ? PrecisionTriangularProfile
                : PrecisionCappedSpeedProfile;
        return new ManeuverCandidateRank(
            translation.ArrivesAt,
            translation.PathDistance,
            translation.PhaseCount,
            profile);
    }

    /// <summary>
    /// Ranks a course turn followed by precision translation with a distinct
    /// profile identity from heading-preserving precision movement.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        TurnThenPrecisionSubCruiseManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ManeuverCandidateRank translation = Rank(plan.PrecisionPlan);
        ManeuverProfileKey profile = plan.PrecisionPlan.TranslationPlan.Kind
            == AlignedSubCruisePlanKind.Triangular
                ? TurnThenPrecisionTriangularProfile
                : TurnThenPrecisionCappedSpeedProfile;
        return new ManeuverCandidateRank(
            plan.EndsAt,
            translation.PathDistance,
            checked(translation.PhaseCount + 1),
            profile);
    }

    /// <summary>
    /// Ranks exact-course primary travel using its underlying schedule and a
    /// stable identity distinct from precision and unbound aligned profiles.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        PrimarySubCruiseManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ManeuverCandidateRank translation = plan.TranslationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } triangular,
            } => Rank(triangular),
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } capped,
            } => Rank(capped),
            _ => throw new InvalidOperationException(
                "A primary sub-cruise plan has an invalid translation payload."),
        };
        ManeuverProfileKey profile = plan.TranslationPlan.Kind
            == AlignedSubCruisePlanKind.Triangular
                ? PrimaryTriangularProfile
                : PrimaryCappedSpeedProfile;
        return new ManeuverCandidateRank(
            translation.ArrivesAt,
            translation.PathDistance,
            translation.PhaseCount,
            profile);
    }

    /// <summary>
    /// Ranks a complete stationary directional candidate from its concrete
    /// translation metrics plus any final stationary-turn phase. Turning in
    /// place adds no path distance but delays terminal arrival.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        StationaryDirectionalManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ManeuverCandidateRank translation = plan.Kind switch
        {
            StationaryDirectionalPlanKind.PrimarySubCruise
                when plan.PrimarySubCruisePlan is { } primary =>
                Rank(primary),
            StationaryDirectionalPlanKind.TurnThenPrimary
                when plan.TurnThenPrimaryPlan is { } turnThenPrimary =>
                Rank(turnThenPrimary),
            StationaryDirectionalPlanKind.TurnThenPrecisionSubCruise
                when plan.TurnThenPrecisionPlan is { } turnThenPrecision =>
                Rank(turnThenPrecision),
            StationaryDirectionalPlanKind.PrecisionSubCruise
                when plan.PrecisionSubCruisePlan is { } precision =>
                Rank(precision),
            _ => throw new InvalidOperationException(
                "A stationary directional plan has an invalid translation payload."),
        };
        if (plan.FinalTurnPlan is null)
        {
            return translation;
        }

        return new ManeuverCandidateRank(
            plan.EndsAt,
            translation.PathDistance,
            checked(translation.PhaseCount + 1),
            new ManeuverProfileKey(
                $"{translation.ProfileKey.Value}+terminal-heading"));
    }

    /// <summary>
    /// Ranks a complete aligned moving candidate from its concrete translation
    /// plus any final stationary turn. A final turn delays arrival and adds one
    /// phase without changing path distance.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        MovingAlignedTerminalManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ManeuverCandidateRank translation = plan.TranslationPlan switch
        {
            {
                Kind: AlignedSubCruisePlanKind.ImmediateBraking,
                ImmediateBrakingPlan: { } immediate,
            } => Rank(immediate),
            {
                Kind: AlignedSubCruisePlanKind.Triangular,
                TriangularPlan: { } triangular,
            } => Rank(triangular),
            {
                Kind: AlignedSubCruisePlanKind.CappedSpeed,
                CappedSpeedPlan: { } capped,
            } => Rank(capped),
            _ => throw new InvalidOperationException(
                "A moving aligned terminal plan has an invalid translation payload."),
        };
        if (plan.FinalTurnPlan is null)
        {
            return translation;
        }

        return new ManeuverCandidateRank(
            plan.EndsAt,
            translation.PathDistance,
            checked(translation.PhaseCount + 1),
            new ManeuverProfileKey(
                $"{translation.ProfileKey.Value}+terminal-heading"));
    }

    /// <summary>
    /// Ranks precision braking plus any stationary directional continuation.
    /// The complete path and phase count include both immutable stages.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        BrakeThenStationaryDirectionalManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ulong brakingDistance = ManeuverBraking.RequiredDistanceMillimeters(
            plan.BrakingPhase.Start.Velocity,
            ShipVelocity.Zero,
            plan.BrakingPhase.Deceleration);
        StationaryDirectionalManeuverPlan? stationary = plan.StationaryPlan;
        ReducedThrustTerminalManeuverPlan? reduced = plan.ReducedThrustPlan;
        if (stationary is null && reduced is null)
        {
            return new ManeuverCandidateRank(
                plan.EndsAt,
                new ManeuverPathDistance(brakingDistance),
                phaseCount: 1,
                PrecisionBrakeTerminalProfile);
        }

        ManeuverCandidateRank stationaryRank = stationary is not null
            ? Rank(stationary)
            : Rank(reduced!);
        UInt128 completeDistance = checked(
            (UInt128)brakingDistance
            + stationaryRank.PathDistance.Millimeters);
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(completeDistance),
            checked(stationaryRank.PhaseCount + 1),
            new ManeuverProfileKey(
                $"precision-brake+{stationaryRank.ProfileKey.Value}"));
    }

    /// <summary>
    /// Ranks a complete cruise candidate by its direct route distance and every
    /// scheduled turn, optional acceleration, spool, cruise, braking, and final
    /// turn phase that it owns.
    /// </summary>
    public static ManeuverCandidateRank Rank(CruiseTerminalManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        SystemPosition start = plan.StateAt(plan.StartsAt).Position;
        Int128 deltaX = checked(
            ((Int128)plan.Destination.Position.X.Units
                - start.Position.X.Units)
            * 1_000);
        Int128 deltaY = checked(
            ((Int128)plan.Destination.Position.Y.Units
                - start.Position.Y.Units)
            * 1_000);
        long x = checked((long)deltaX);
        long y = checked((long)deltaY);
        int phaseCount = 3
            + (plan.AccelerationPhase is null ? 0 : 1)
            + (plan.CourseTurnPlan is null ? 0 : 1)
            + (plan.FinalTurnPlan is null ? 0 : 1);
        ManeuverProfileKey profile = plan.FinalTurnPlan is null
            ? CruiseTerminalProfile
            : new ManeuverProfileKey("cruise-terminal+terminal-heading");
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(ManeuverVector.SpeedMagnitude(
                new ShipVelocity(x, y))),
            phaseCount,
            profile);
    }

    /// <summary>
    /// Ranks forced dropout as one dedicated brake followed by the already
    /// selected complete continuation. Its direct path diagnostic remains
    /// stable even when the continuation temporarily changes course.
    /// </summary>
    public static ManeuverCandidateRank Rank(
        ForcedCruiseDropoutTerminalManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        SystemPosition start = plan.StateAt(plan.StartsAt).Position;
        long x = checked((long)(
            ((Int128)plan.Destination.Position.X.Units
                - start.Position.X.Units)
            * 1_000));
        long y = checked((long)(
            ((Int128)plan.Destination.Position.Y.Units
                - start.Position.Y.Units)
            * 1_000));
        return new ManeuverCandidateRank(
            plan.EndsAt,
            new ManeuverPathDistance(ManeuverVector.SpeedMagnitude(
                new ShipVelocity(x, y))),
            checked(plan.ContinuationPlan.Phases.Count + 1),
            new ManeuverProfileKey(
                $"forced-cruise-dropout+{plan.ContinuationPlan.Kind}"));
    }
}
