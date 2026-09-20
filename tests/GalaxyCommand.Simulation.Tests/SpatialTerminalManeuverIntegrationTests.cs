using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialTerminalManeuverIntegrationTests
{
    [Fact]
    public void StartOwnsTheScheduleAndPublishesLiveAnalyticSnapshot()
    {
        var movement = new SpatialMovement();
        var agenda = new EventAgenda<SpatialMovementEvent>();
        var shipId = new ShipId(1);
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        movement.Add(shipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan plan = Plan(
            SimulationTime.Zero,
            start,
            Position(25, 0),
            ManeuverObjective.FastestArrival);

        TerminalManeuverCommit<SpatialMovementEvent> commit =
            movement.CommitStartTerminalManeuver(
                shipId,
                plan,
                ManeuverObjective.FastestArrival,
                SimulationTime.Zero,
                static movementEvent => movementEvent);

        var active = Assert.IsType<ShipSpatialState.AnalyticManeuver>(
            movement.GetState(shipId));
        Assert.Same(commit.Maneuver, active.Maneuver);
        Assert.Equal(ManeuverObjective.FastestArrival, active.Objective);
        Assert.Equal(new MotionId(1), active.Maneuver.MotionId);
        Assert.Equal(new EventGeneration(0), active.Maneuver.Generation);
        Assert.Equal(plan.Phases.Count, commit.EventProposals.Count);
        Assert.Equal(0, agenda.Count);
        Assert.All(commit.EventProposals, proposal =>
            Assert.IsType<SpatialMovementEvent.Maneuver>(proposal.Payload));

        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
            agenda,
            commit.EventProposals);
        movement.BindTerminalManeuverEvents(shipId, agendaCommit.EventKeys);
        SimulationTime now = Inside(plan.Phases[0]);
        ShipKinematicState expected = plan.StateAt(now);

        ShipSpatialSnapshot snapshot = Assert.Single(
            movement.CaptureSnapshot(now));

        Assert.Equal(expected.Position, snapshot.Position);
        Assert.Equal(expected.Velocity, snapshot.Velocity);
        Assert.Equal(expected.Heading, snapshot.Heading);
        var state = Assert.IsType<ShipSpatialSnapshotState.AnalyticManeuver>(
            snapshot.State);
        Assert.Equal(expected, state.CurrentState);
        Assert.Equal(active.Maneuver.MotionId, state.Maneuver.MotionId);
        Assert.Equal(active.Maneuver.NextBoundary, state.Maneuver.NextBoundary);
        Assert.Equal(ManeuverObjective.FastestArrival, state.Maneuver.Objective);
    }

    [Fact]
    public void ScheduledAnalyticPhasesCompleteThroughSpatialEventDispatch()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan plan = Plan(
            SimulationTime.Zero,
            start,
            Position(25, 0),
            ManeuverObjective.FastestArrival);
        fixture.Start(plan, ManeuverObjective.FastestArrival);

        fixture.Engine.RunUntil(plan.EndsAt);

        Assert.Equal(
            Enumerable.Repeat(
                ScheduledEventDisposition.Applied,
                plan.Phases.Count),
            fixture.Runtime.Dispositions);
        ShipKinematicState expected = plan.StateAt(plan.EndsAt);
        var final = Assert.IsType<ShipSpatialState.AtPosition>(
            fixture.Movement.GetState(fixture.ShipId));
        Assert.Equal(expected.Position, final.Position);
        ShipSpatialSnapshot snapshot = Assert.Single(
            fixture.Movement.CaptureSnapshot(plan.EndsAt));
        Assert.Equal(expected.Velocity, snapshot.Velocity);
        Assert.Equal(expected.Heading, snapshot.Heading);
        Assert.Null(snapshot.Maneuver);
    }

    [Fact]
    public void ReplacementMaterializesCancelsAndSchedulesTheNextGeneration()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan originalPlan = Plan(
            SimulationTime.Zero,
            start,
            Position(25, 0),
            ManeuverObjective.FastestArrival);
        ScheduledTerminalManeuver original = fixture.Start(
            originalPlan,
            ManeuverObjective.FastestArrival);
        SimulationTime now = Inside(originalPlan.Phases[0]);
        fixture.Engine.RunUntil(now);
        ExecutableBoundedTerminalManeuverPlan replacementPlan = Plan(
            now,
            originalPlan.StateAt(now),
            Position(50, 0),
            ManeuverObjective.ShortestPath);

        AgendaCancellationCheck result =
            fixture.Movement.TryReplaceTerminalManeuver(
                fixture.ShipId,
                replacementPlan,
                ManeuverObjective.ShortestPath,
                LocalMotionEndReason.ReplacedByCommand,
                now,
                fixture.Agenda,
                static movementEvent => movementEvent,
                out TerminalManeuverReplacement<
                    SpatialMovementEvent,
                    LocalMotionEndReason>? replacement);

        Assert.Equal(AgendaCancellationCheck.Matches, result);
        Assert.NotNull(replacement);
        Assert.Equal(original.MotionId, replacement.InterruptedMotionId);
        Assert.Equal(
            ManeuverObjective.FastestArrival,
            replacement.InterruptedObjective);
        Assert.Equal(0, replacement.InterruptedPhaseIndex);
        Assert.Equal(
            originalPlan.Phases[0].Kind,
            replacement.InterruptedPhaseKind);
        Assert.Equal(
            LocalMotionEndReason.ReplacedByCommand,
            replacement.Reason);
        Assert.Equal(now, replacement.Interruption.Timestamp);
        Assert.Equal(
            originalPlan.StateAt(now),
            replacement.Interruption.MaterializedState);
        Assert.True(original.IsInvalidated);
        Assert.Equal(0, fixture.Agenda.Count);
        Assert.Equal(
            original.Generation.Next(),
            replacement.Commit.Maneuver.Generation);
        Assert.NotEqual(
            original.MotionId,
            replacement.Commit.Maneuver.MotionId);
        var active = Assert.IsType<ShipSpatialState.AnalyticManeuver>(
            fixture.Movement.GetState(fixture.ShipId));
        Assert.Same(replacement.Commit.Maneuver, active.Maneuver);
        Assert.Equal(ManeuverObjective.ShortestPath, active.Objective);

        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
            fixture.Agenda,
            replacement.Commit.EventProposals);
        fixture.Movement.BindTerminalManeuverEvents(
            fixture.ShipId,
            agendaCommit.EventKeys);
        fixture.Engine.RunUntil(replacementPlan.EndsAt);

        Assert.Equal(
            replacementPlan.StateAt(replacementPlan.EndsAt).Position,
            fixture.Movement.PositionAt(
                fixture.ShipId,
                replacementPlan.EndsAt));
        Assert.Equal(replacementPlan.Phases.Count, fixture.Runtime.Dispositions.Count);
    }

    [Fact]
    public void CruiseReplacementReceiptCarriesForcedDropoutBoundary()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        SystemPosition destination = Position(100_000_000, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan originalPlan = Plan(
            SimulationTime.Zero,
            start,
            destination,
            ManeuverObjective.FastestArrival);
        Assert.Equal(BoundedTerminalPlanKind.CruiseTerminal, originalPlan.Kind);
        ScheduledTerminalManeuver original = fixture.Start(
            originalPlan,
            ManeuverObjective.FastestArrival);
        ManeuverScheduledPhase cruise = Assert.Single(
            originalPlan.Phases,
            static phase => phase.Kind == ManeuverPhaseKind.CruiseTravel);
        SimulationTime now = Inside(cruise);
        fixture.Engine.RunUntil(now);
        ShipKinematicState interruptionState = originalPlan.StateAt(now);
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                now,
                interruptionState,
                destination,
                requestedHeading: null,
                Capability(),
                ManeuverObjective.FastestArrival);
        Assert.Equal(
            BoundedTerminalPlanKind.ForcedCruiseDropoutTerminal,
            selection.Kind);
        ExecutableBoundedTerminalManeuverPlan forcedDropout =
            Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
                selection.ExecutablePlan);

        AgendaCancellationCheck result =
            fixture.Movement.TryReplaceTerminalManeuver(
                fixture.ShipId,
                forcedDropout,
                ManeuverObjective.FastestArrival,
                LocalMotionEndReason.ReplacedByCommand,
                now,
                fixture.Agenda,
                static movementEvent => movementEvent,
                out TerminalManeuverReplacement<
                    SpatialMovementEvent,
                    LocalMotionEndReason>? replacement);

        Assert.Equal(AgendaCancellationCheck.Matches, result);
        Assert.NotNull(replacement);
        Assert.Equal(original.MotionId, replacement.InterruptedMotionId);
        Assert.Equal(
            ManeuverPhaseKind.CruiseTravel,
            replacement.InterruptedPhaseKind);
        Assert.Equal(
            interruptionState,
            replacement.Interruption.MaterializedState);
        Assert.Equal(
            ManeuverPhaseKind.CruiseDropoutBrake,
            replacement.Commit.Maneuver.CurrentPhase?.Kind);
        Assert.Equal(
            Capability().CruiseSpeed.MillimetersPerSecond,
            ManeuverVector.SpeedMagnitude(
                replacement.Interruption.MaterializedState.Velocity));
    }

    [Fact]
    public void FailedReplacementLeavesTheActiveManeuverUnchanged()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan originalPlan = Plan(
            SimulationTime.Zero,
            start,
            Position(25, 0),
            ManeuverObjective.FastestArrival);
        ScheduledTerminalManeuver original = fixture.Start(
            originalPlan,
            ManeuverObjective.FastestArrival);
        PendingManeuverScheduleEvent missing = original.PendingEvents![0];
        var wrappedMissing = new SpatialMovementEvent.Maneuver(
            fixture.ShipId,
            missing.Payload);
        Assert.True(fixture.Agenda.TryCancelExact(
            missing.EventKey,
            missing.Generation,
            wrappedMissing));
        SimulationTime now = Inside(originalPlan.Phases[0]);
        fixture.Engine.RunUntil(now);
        ExecutableBoundedTerminalManeuverPlan replacementPlan = Plan(
            now,
            originalPlan.StateAt(now),
            Position(50, 0),
            ManeuverObjective.ShortestPath);

        AgendaCancellationCheck result =
            fixture.Movement.TryReplaceTerminalManeuver(
                fixture.ShipId,
                replacementPlan,
                ManeuverObjective.ShortestPath,
                LocalMotionEndReason.ReplacedByCommand,
                now,
                fixture.Agenda,
                static movementEvent => movementEvent,
                out TerminalManeuverReplacement<
                    SpatialMovementEvent,
                    LocalMotionEndReason>? replacement);

        Assert.Equal(AgendaCancellationCheck.Missing, result);
        Assert.Null(replacement);
        Assert.False(original.IsInvalidated);
        var active = Assert.IsType<ShipSpatialState.AnalyticManeuver>(
            fixture.Movement.GetState(fixture.ShipId));
        Assert.Same(original, active.Maneuver);
        Assert.Equal(new MotionId(1), original.MotionId);
    }

    [Fact]
    public void CheckpointRestoreContinuesTheBoundAnalyticSchedule()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan plan = Plan(
            SimulationTime.Zero,
            start,
            Position(25, 0),
            ManeuverObjective.FastestArrival);
        fixture.Start(plan, ManeuverObjective.FastestArrival);
        SimulationTime checkpointTime = Inside(plan.Phases[0]);
        fixture.Engine.RunUntil(checkpointTime);
        SpatialMovementCheckpoint movementCheckpoint = fixture.Movement
            .CaptureCheckpoint(checkpointTime).Value!;
        SimulationEngineCheckpoint<SpatialMovementEvent> engineCheckpoint =
            fixture.Engine.CaptureCheckpoint().Value!;

        CheckpointResult<SpatialMovement> movementRestoration =
            SpatialMovement.RestoreCheckpoint(
                movementCheckpoint,
                checkpointTime);
        var restoredRuntime = new ManeuverRuntime(movementRestoration.Value!);
        CheckpointResult<SimulationEngine<SpatialMovementEvent>> engineRestoration =
            SimulationEngine<SpatialMovementEvent>.RestoreCheckpoint(
                restoredRuntime,
                engineCheckpoint);

        Assert.True(movementRestoration.IsSuccess);
        Assert.True(engineRestoration.IsSuccess);
        Assert.Equal(
            fixture.Movement.CaptureSnapshot(checkpointTime),
            movementRestoration.Value!.CaptureSnapshot(checkpointTime));

        fixture.Engine.RunUntil(plan.EndsAt);
        engineRestoration.Value!.RunUntil(plan.EndsAt);

        Assert.Equal(
            fixture.Movement.CaptureSnapshot(plan.EndsAt),
            movementRestoration.Value.CaptureSnapshot(plan.EndsAt));
        Assert.Equal(fixture.Runtime.Dispositions, restoredRuntime.Dispositions);
    }

    [Fact]
    public void CheckpointRestorePreservesPendingWaypointBoundaries()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        ExecutableBoundedTerminalManeuverPlan terminal = Plan(
            SimulationTime.Zero,
            start,
            Position(100, 0),
            ManeuverObjective.FastestArrival);
        Assert.True(WaypointManeuverPlan.TryCreate(
            terminal,
            start,
            Position(100, 0),
            [Position(50, 0)],
            out WaypointManeuverPlan? waypointPlan));
        WaypointManeuverPlan route = Assert.IsType<WaypointManeuverPlan>(
            waypointPlan);
        fixture.Start(
            route.Plan,
            ManeuverObjective.FastestArrival,
            route.WaypointPhaseIndices);
        var checkpointTime = new SimulationTime(1_000);
        fixture.Engine.RunUntil(checkpointTime);
        SpatialMovementCheckpoint movementCheckpoint = fixture.Movement
            .CaptureCheckpoint(checkpointTime).Value!;
        SimulationEngineCheckpoint<SpatialMovementEvent> engineCheckpoint =
            fixture.Engine.CaptureCheckpoint().Value!;

        SpatialMovement restoredMovement = SpatialMovement.RestoreCheckpoint(
            movementCheckpoint,
            checkpointTime).Value!;
        var restoredRuntime = new ManeuverRuntime(restoredMovement);
        SimulationEngine<SpatialMovementEvent> restoredEngine =
            SimulationEngine<SpatialMovementEvent>.RestoreCheckpoint(
                restoredRuntime,
                engineCheckpoint).Value!;
        var restored = Assert.IsType<ShipSpatialState.AnalyticManeuver>(
            restoredMovement.GetState(fixture.ShipId));

        Assert.Equal(
            route.WaypointPhaseIndices,
            restored.Maneuver.WaypointPhaseIndices);
        Assert.True(restored.Maneuver.IsWaypointBoundary(
            Assert.Single(route.WaypointPhaseIndices)));

        fixture.Engine.RunUntil(route.Plan.EndsAt);
        restoredEngine.RunUntil(route.Plan.EndsAt);

        Assert.Equal(
            fixture.Movement.CaptureSnapshot(route.Plan.EndsAt),
            restoredMovement.CaptureSnapshot(route.Plan.EndsAt));
        Assert.Equal(fixture.Runtime.Dispositions, restoredRuntime.Dispositions);
    }

    [Fact]
    public void CheckpointRestoreContinuesNoncollinearWaypointRouteExactly()
    {
        var fixture = new ManeuverFixture();
        ShipKinematicState start = State(0, 0, ShipVelocity.Zero, 0);
        fixture.Movement.Add(fixture.ShipId, start.Position, start.Heading);
        Assert.True(WaypointManeuverPlan.TryCreateRoute(
            SimulationTime.Zero,
            start,
            [Position(100, 0), Position(100, 100)],
            Capability(),
            ManeuverObjective.FastestArrival,
            out WaypointManeuverPlan? candidate));
        WaypointManeuverPlan route = Assert.IsType<WaypointManeuverPlan>(candidate);
        fixture.Start(
            route.Plan,
            ManeuverObjective.FastestArrival,
            route.WaypointPhaseIndices);
        SimulationTime checkpointTime = Inside(route.Plan.Phases[0]);
        fixture.Engine.RunUntil(checkpointTime);
        SpatialMovementCheckpoint movementCheckpoint = fixture.Movement
            .CaptureCheckpoint(checkpointTime).Value!;
        SimulationEngineCheckpoint<SpatialMovementEvent> engineCheckpoint =
            fixture.Engine.CaptureCheckpoint().Value!;

        SpatialMovement restoredMovement = SpatialMovement.RestoreCheckpoint(
            movementCheckpoint,
            checkpointTime).Value!;
        var restoredRuntime = new ManeuverRuntime(restoredMovement);
        SimulationEngine<SpatialMovementEvent> restoredEngine =
            SimulationEngine<SpatialMovementEvent>.RestoreCheckpoint(
                restoredRuntime,
                engineCheckpoint).Value!;

        fixture.Engine.RunUntil(route.Plan.EndsAt);
        restoredEngine.RunUntil(route.Plan.EndsAt);

        Assert.Equal(
            fixture.Movement.CaptureSnapshot(route.Plan.EndsAt),
            restoredMovement.CaptureSnapshot(route.Plan.EndsAt));
        Assert.Equal(fixture.Runtime.Dispositions, restoredRuntime.Dispositions);
    }

    [Fact]
    public void InvalidStartPlanDoesNotMutateOrAllocateMotionIdentity()
    {
        var movement = new SpatialMovement();
        var shipId = new ShipId(1);
        ShipKinematicState actual = State(0, 0, ShipVelocity.Zero, 0);
        movement.Add(shipId, actual.Position, actual.Heading);
        ExecutableBoundedTerminalManeuverPlan invalid = Plan(
            SimulationTime.Zero,
            State(1, 0, ShipVelocity.Zero, 0),
            Position(25, 0),
            ManeuverObjective.FastestArrival);

        Assert.Throws<InvalidOperationException>(() =>
            movement.CommitStartTerminalManeuver(
                shipId,
                invalid,
                ManeuverObjective.FastestArrival,
                SimulationTime.Zero,
                static movementEvent => movementEvent));

        Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(shipId));
        ExecutableBoundedTerminalManeuverPlan valid = Plan(
            SimulationTime.Zero,
            actual,
            Position(25, 0),
            ManeuverObjective.FastestArrival);
        TerminalManeuverCommit<SpatialMovementEvent> commit =
            movement.CommitStartTerminalManeuver(
                shipId,
                valid,
                ManeuverObjective.FastestArrival,
                SimulationTime.Zero,
                static movementEvent => movementEvent);
        Assert.Equal(new MotionId(1), commit.Maneuver.MotionId);
    }

    private static ExecutableBoundedTerminalManeuverPlan Plan(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverObjective objective)
    {
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                startsAt,
                start,
                destination,
                requestedHeading: null,
                Capability(),
                objective);
        return Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
            selection.ExecutablePlan);
    }

    private static EffectiveShipManeuverCapability Capability() =>
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(10_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
        .ResolveForMass(effectiveMassKilograms: 10_000);

    private static SimulationTime Inside(ManeuverScheduledPhase phase) =>
        new(phase.StartsAt.Milliseconds
            + ((phase.EndsAt.Milliseconds - phase.StartsAt.Milliseconds) / 2));

    private static ShipKinematicState State(
        long x,
        long y,
        ShipVelocity velocity,
        uint heading) =>
        new(Position(x, y), velocity, new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));

    private sealed class ManeuverFixture
    {
        public ManeuverFixture()
        {
            Runtime = new ManeuverRuntime(Movement);
            Engine = new SimulationEngine<SpatialMovementEvent>(Runtime, Agenda);
        }

        public ShipId ShipId { get; } = new(1);

        public SpatialMovement Movement { get; } = new();

        public EventAgenda<SpatialMovementEvent> Agenda { get; } = new();

        public ManeuverRuntime Runtime { get; }

        public SimulationEngine<SpatialMovementEvent> Engine { get; }

        public ScheduledTerminalManeuver Start(
            ExecutableBoundedTerminalManeuverPlan plan,
            ManeuverObjective objective,
            IEnumerable<int>? waypointPhaseIndices = null)
        {
            TerminalManeuverCommit<SpatialMovementEvent> commit =
                Movement.CommitStartTerminalManeuver(
                    ShipId,
                    plan,
                    objective,
                    Engine.CurrentTime,
                    static movementEvent => movementEvent,
                    waypointPhaseIndices);
            AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
                Agenda,
                commit.EventProposals);
            Movement.BindTerminalManeuverEvents(
                ShipId,
                agendaCommit.EventKeys);
            return commit.Maneuver;
        }
    }

    private sealed class ManeuverRuntime : ISimulationRuntime<SpatialMovementEvent>
    {
        private readonly SpatialMovement _movement;

        public ManeuverRuntime(SpatialMovement movement)
        {
            _movement = movement;
        }

        public List<ScheduledEventDisposition> Dispositions { get; } = [];

        public bool ShouldStop => false;

        public void Reconcile(
            SimulationTime now,
            EventAgenda<SpatialMovementEvent> agenda)
        {
        }

        public void AccrueTo(SimulationTime now)
        {
        }

        public ScheduledEventDisposition HandleEvent(
            ScheduledEvent<SpatialMovementEvent> simulationEvent,
            SimulationTime now,
            EventAgenda<SpatialMovementEvent> agenda) =>
            _movement.HandleEvent(
                simulationEvent.Payload,
                simulationEvent.Generation,
                now);

        public void RecordEvent(
            ScheduledEvent<SpatialMovementEvent> simulationEvent,
            ScheduledEventDisposition disposition) =>
            Dispositions.Add(disposition);
    }
}
