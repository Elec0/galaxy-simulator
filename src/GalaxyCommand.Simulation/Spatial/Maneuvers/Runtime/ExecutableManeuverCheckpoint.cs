namespace GalaxyCommand.Simulation;

/// <summary>Inert selected authority, including inserted phase boundaries; no evaluator delegate is persisted.</summary>
internal sealed record ExecutableManeuverCheckpoint(
    BoundedTerminalPlanKind Kind,
    IReadOnlyList<ManeuverScheduledPhase> Phases,
    ExecutableManeuverPayload Payload);

/// <summary>Closed payload union retaining selected analytic instructions instead of rerunning selection.</summary>
internal abstract record ExecutableManeuverPayload
{
    internal sealed record TerminalSettle(SimulationTime At, ShipKinematicState State) : ExecutableManeuverPayload;
    internal sealed record PassiveDrag(DecelerationManeuverSegment Plan) : ExecutableManeuverPayload;
    internal sealed record Turn(StationaryTurnManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record Directional(StationaryDirectionalManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record MovingAligned(MovingAlignedTerminalManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record Cruise(CruiseTerminalManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record ForcedDropout(SystemPosition Destination, ShipHeading? RequestedHeading,
        DecelerationManeuverSegment DropoutPhase, ExecutableManeuverCheckpoint Continuation) : ExecutableManeuverPayload;
    internal sealed record BrakeThenDirectional(BrakeThenStationaryDirectionalManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record ReducedThrust(ReducedThrustTerminalManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record ImmediateBraking(ShortMoveImmediateBrakingPlan Plan) : ExecutableManeuverPayload;
    internal sealed record Triangular(ShortMoveTriangularPlan Plan) : ExecutableManeuverPayload;
    internal sealed record CappedSpeed(CappedSpeedManeuverPlan Plan) : ExecutableManeuverPayload;
    internal sealed record WaypointRoute(IReadOnlyList<AnalyticManeuverSegment> Transitions,
        ExecutableManeuverCheckpoint Terminal) : ExecutableManeuverPayload;
}

public sealed partial class ExecutableBoundedTerminalManeuverPlan
{
    /// <summary>Captures immutable selected instructions and the exact observable schedule, without geometry or delegates.</summary>
    internal ExecutableManeuverCheckpoint CaptureCheckpoint() => new(Kind, Array.AsReadOnly(Phases.ToArray()), payload);

    /// <summary>
    /// Rebinds selected analytic instructions without planner replay. Saved phases may only split original phases;
    /// changing an instruction or removing an original boundary rejects before the plan is returned.
    /// </summary>
    internal static ExecutableBoundedTerminalManeuverPlan RestoreCheckpoint(ExecutableManeuverCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.Phases);
        ArgumentNullException.ThrowIfNull(checkpoint.Payload);
        ExecutableBoundedTerminalManeuverPlan original = RestorePayload(checkpoint.Payload);
        SelectedManeuverCheckpointValidation.Validate(checkpoint.Payload, original);
        if (checkpoint.Kind != original.Kind || checkpoint.Phases.Count == 0)
            throw new ArgumentException("A maneuver checkpoint must retain its selected kind and complete schedule.");

        // Additional observation boundaries are authority, but cannot erase or reinterpret a physical phase.
        SimulationTime[] boundaries = checkpoint.Phases.Select(phase => phase.EndsAt)
            .Where(time => time > original.StartsAt && time < original.EndsAt).Distinct().ToArray();
        ExecutableBoundedTerminalManeuverPlan restored = boundaries.Length == 0
            ? original : original.WithAdditionalBoundaries(boundaries);
        if (!restored.Phases.SequenceEqual(checkpoint.Phases))
            throw new ArgumentException("A maneuver checkpoint schedule must preserve every original instruction and boundary.");
        return restored;
    }

    /// <summary>Dispatches the closed selected-payload union and regenerates its geometry from exact analytic inputs.</summary>
    private static ExecutableBoundedTerminalManeuverPlan RestorePayload(ExecutableManeuverPayload payload) => payload switch
    {
        ExecutableManeuverPayload.TerminalSettle item => TerminalSettle(item.At, item.State),
        ExecutableManeuverPayload.PassiveDrag item => FromPassiveDrag(item.Plan),
        ExecutableManeuverPayload.Turn item => From(item.Plan),
        ExecutableManeuverPayload.Directional item => new(BoundedTerminalPlanKind.StationaryDirectional,
            item.Plan.StartsAt, item.Plan.EndsAt, item.Plan.StateAt, payload,
            ManeuverPhaseSchedules.From(item.Plan), ManeuverInteractionCurves.From(item.Plan)),
        ExecutableManeuverPayload.MovingAligned item => From(item.Plan),
        ExecutableManeuverPayload.Cruise item => From(item.Plan),
        ExecutableManeuverPayload.ForcedDropout item => From(ForcedCruiseDropoutTerminalManeuverPlan.RestoreSelected(
            item.Destination, item.RequestedHeading, item.DropoutPhase, RestoreCheckpoint(item.Continuation))),
        ExecutableManeuverPayload.BrakeThenDirectional item => From(item.Plan),
        ExecutableManeuverPayload.ReducedThrust item => From(item.Plan),
        ExecutableManeuverPayload.ImmediateBraking item => From(AlignedSubCruisePlanSelection.ImmediateBraking(item.Plan)),
        ExecutableManeuverPayload.Triangular item => From(AlignedSubCruisePlanSelection.Triangular(item.Plan)),
        ExecutableManeuverPayload.CappedSpeed item => From(AlignedSubCruisePlanSelection.CappedSpeed(item.Plan)),
        ExecutableManeuverPayload.WaypointRoute item => WaypointRoute(item.Transitions, RestoreCheckpoint(item.Terminal)),
        _ => throw new ArgumentException("Unsupported selected maneuver payload."),
    };
}
