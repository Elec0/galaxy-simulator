using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialMovementTests
{
    [Fact]
    public void MotionCommitReturnsFutureEventWithoutAllocatingAgendaSequence()
    {
        var movement = new SpatialMovement();
        var agenda = new EventAgenda<SpatialMovementEvent>();
        var shipId = new ShipId(4);
        SystemPosition origin = Position(0, 0);
        SystemPosition destination = Position(100, 50);
        movement.Add(shipId, origin);

        LocalMotionCommit<SpatialMovementEvent> commit =
            movement.CommitStartOrReplace(
                shipId,
                new TravelLeg.Local(
                    origin,
                    destination,
                    new SimulationDuration(100)),
                SimulationTime.Zero,
                static movementEvent => movementEvent);

        LocalMotionSegment motion = Assert.IsType<LocalMotionSegment>(
            commit.Motion);
        AgendaEventProposal<SpatialMovementEvent> proposal =
            Assert.IsType<AgendaEventProposal<SpatialMovementEvent>>(
                commit.EventProposal);
        Assert.Equal(0, agenda.Count);
        Assert.Equal(
            new AgendaProposalOrder(
                RuntimeEvaluationWave.ActorOrders,
                shipId.Value,
                motion.Id.Value,
                EffectKind: 1,
                LocalOrdinal: 0),
            proposal.Order);
        Assert.Equal(motion.ArrivesAt, proposal.Timestamp);
        Assert.Equal(EventPhase.PhysicalCompletion, proposal.Phase);
        Assert.Equal(motion.Generation, proposal.Generation);

        AgendaCommitResult result = AgendaCommitOwner.Commit(
            agenda,
            [proposal]);

        Assert.Equal(1, agenda.Count);
        Assert.Equal(
            new EventKey(
                motion.ArrivesAt,
                EventPhase.PhysicalCompletion,
                CreationSequence: 0),
            Assert.Single(result.EventKeys));
    }

    [Fact]
    public void ScheduledLocalMotionInterpolatesAndCompletesAuthoritatively()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        SystemPosition destination = Position(100, 50);
        fixture.Movement.Add(fixture.ShipId, origin);

        LocalMotionSegment motion = Assert.IsType<LocalMotionSegment>(
            fixture.Start(origin, destination, new SimulationDuration(100)));

        fixture.Engine.RunUntil(new SimulationTime(50));
        ShipSpatialSnapshot halfway = Assert.Single(
            fixture.Movement.CaptureSnapshot(fixture.Engine.CurrentTime));
        Assert.Equal(Position(50, 25), halfway.Position);
        Assert.Equal(motion.Id, halfway.Motion?.Id);
        Assert.Equal(new SimulationTime(100), halfway.Motion?.ArrivesAt);

        fixture.Engine.RunUntil(new SimulationTime(100));

        var present = Assert.IsType<ShipSpatialState.AtPosition>(
            fixture.Movement.GetState(fixture.ShipId));
        Assert.Equal(destination, present.Position);
        Assert.Equal(
            [ScheduledEventDisposition.Applied],
            fixture.Runtime.Dispositions);
        Assert.Null(Assert.Single(
            fixture.Movement.CaptureSnapshot(fixture.Engine.CurrentTime)).Motion);
    }

    [Fact]
    public void CancellationMaterializesCurrentPositionAndInvalidatesArrival()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        fixture.Start(origin, Position(100, 40), new SimulationDuration(100));
        fixture.Engine.RunUntil(new SimulationTime(40));

        Assert.True(fixture.Movement.CommitCancel(
            fixture.ShipId,
            fixture.Engine.CurrentTime));
        var cancelled = Assert.IsType<ShipSpatialState.AtPosition>(
            fixture.Movement.GetState(fixture.ShipId));
        Assert.Equal(Position(40, 16), cancelled.Position);

        fixture.Engine.RunUntil(new SimulationTime(100));

        Assert.Equal(
            [ScheduledEventDisposition.IgnoredStaleGeneration],
            fixture.Runtime.Dispositions);
        Assert.Equal(
            Position(40, 16),
            fixture.Movement.PositionAt(fixture.ShipId, fixture.Engine.CurrentTime));
    }

    [Fact]
    public void ReplacementInvalidatesOldArrivalAndCompletesNewMotion()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        LocalMotionSegment original = Assert.IsType<LocalMotionSegment>(
            fixture.Start(origin, Position(100, 0), new SimulationDuration(100)));
        fixture.Engine.RunUntil(new SimulationTime(40));
        SystemPosition replacementOrigin = Assert.IsType<SystemPosition>(
            fixture.Movement.PositionAt(
                fixture.ShipId,
                fixture.Engine.CurrentTime));

        LocalMotionSegment replacement = Assert.IsType<LocalMotionSegment>(
            fixture.Start(
                replacementOrigin,
                Position(40, 60),
                new SimulationDuration(60)));

        Assert.NotEqual(original.Id, replacement.Id);
        Assert.Equal(original.Generation.Next(), replacement.Generation);

        fixture.Engine.RunUntil(new SimulationTime(100));

        Assert.Equal(
            [
                ScheduledEventDisposition.IgnoredStaleGeneration,
                ScheduledEventDisposition.Applied,
            ],
            fixture.Runtime.Dispositions);
        Assert.Equal(
            Position(40, 60),
            fixture.Movement.PositionAt(fixture.ShipId, fixture.Engine.CurrentTime));
    }

    [Fact]
    public void RejectedReplacementDoesNotMutateActiveMotion()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        LocalMotionSegment original = Assert.IsType<LocalMotionSegment>(
            fixture.Start(origin, Position(100, 0), new SimulationDuration(100)));
        fixture.Engine.RunUntil(new SimulationTime(40));

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Start(
                Position(999, 999),
                Position(0, 100),
                new SimulationDuration(60)));

        var moving = Assert.IsType<ShipSpatialState.Moving>(
            fixture.Movement.GetState(fixture.ShipId));
        Assert.Equal(original, moving.Motion);

        fixture.Engine.RunUntil(new SimulationTime(100));

        Assert.Equal(
            [ScheduledEventDisposition.Applied],
            fixture.Runtime.Dispositions);
        Assert.Equal(
            Position(100, 0),
            fixture.Movement.PositionAt(fixture.ShipId, fixture.Engine.CurrentTime));
    }

    [Fact]
    public void ZeroDurationMoveCompletesWithoutScheduling()
    {
        var fixture = new MovementFixture();
        SystemPosition position = Position(25, -30);
        fixture.Movement.Add(fixture.ShipId, position);

        LocalMotionSegment? motion = fixture.Start(
            position,
            position,
            SimulationDuration.Zero);

        Assert.Null(motion);
        Assert.Equal(0, fixture.Agenda.Count);
        Assert.Equal(position, fixture.Movement.PositionAt(
            fixture.ShipId,
            SimulationTime.Zero));
    }

    [Fact]
    public void InterpolationSupportsNegativeCoordinatesDeterministically()
    {
        var motion = new LocalMotionSegment(
            new MotionId(1),
            new EventGeneration(0),
            Position(10, 10),
            Position(-10, -30),
            SimulationTime.Zero,
            new SimulationTime(100));

        Assert.Equal(Position(0, -10), motion.PositionAt(new SimulationTime(50)));
        Assert.Equal(Position(10, 10), motion.PositionAt(SimulationTime.Zero));
        Assert.Equal(Position(-10, -30), motion.PositionAt(new SimulationTime(100)));
    }

    [Fact]
    public void InterpolationHandlesFullCoordinateRangeWithoutIntermediateOverflow()
    {
        var motion = new LocalMotionSegment(
            new MotionId(1),
            new EventGeneration(0),
            Position(long.MinValue, long.MinValue),
            Position(long.MaxValue, long.MaxValue),
            SimulationTime.Zero,
            new SimulationTime(2));

        SystemPosition midpoint = motion.PositionAt(new SimulationTime(1));

        Assert.Equal(Position(-1, -1), midpoint);
    }

    [Fact]
    public void FinalStateDoesNotDependOnIncrementalAdvancement()
    {
        var singleRun = new MovementFixture();
        var incremental = new MovementFixture();
        SystemPosition origin = Position(-50, 25);
        SystemPosition destination = Position(100, -75);
        foreach (MovementFixture fixture in new[] { singleRun, incremental })
        {
            fixture.Movement.Add(fixture.ShipId, origin);
            fixture.Start(origin, destination, new SimulationDuration(100));
        }

        singleRun.Engine.RunUntil(new SimulationTime(100));
        incremental.Engine.RunUntil(new SimulationTime(40));
        incremental.Engine.RunUntil(new SimulationTime(100));

        Assert.Equal(
            singleRun.Movement.CaptureSnapshot(singleRun.Engine.CurrentTime),
            incremental.Movement.CaptureSnapshot(incremental.Engine.CurrentTime));
        Assert.Equal(singleRun.Runtime.Dispositions, incremental.Runtime.Dispositions);
    }

    [Fact]
    public void SnapshotCollectionIsImmutableAndOrderedByShipId()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(2), Position(20, 0));
        movement.Add(new ShipId(1), Position(10, 0));

        IReadOnlyList<ShipSpatialSnapshot> snapshots =
            movement.CaptureSnapshot(SimulationTime.Zero);

        Assert.Equal([1UL, 2UL], snapshots.Select(snapshot => snapshot.ShipId.Value));
        var exposed = Assert.IsAssignableFrom<IList<ShipSpatialSnapshot>>(snapshots);
        Assert.Throws<NotSupportedException>(() =>
            exposed.Add(new ShipSpatialSnapshot(
                new ShipId(3),
                new ShipSpatialSnapshotState.AtPosition(
                    Position(30, 0)))));
    }

    [Fact]
    public void ScheduledGenerationMismatchIsIgnoredWithoutMutation()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        LocalMotionSegment motion = Assert.IsType<LocalMotionSegment>(
            fixture.Start(origin, Position(100, 0), new SimulationDuration(100)));
        var arrival = new SpatialMovementEvent.Arrive(
            fixture.ShipId,
            motion.Id,
            motion.Generation);

        ScheduledEventDisposition disposition = fixture.Movement.HandleEvent(
            arrival,
            motion.Generation.Next(),
            motion.ArrivesAt);

        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch, disposition);
        Assert.Equal(
            new ShipSpatialState.Moving(motion),
            fixture.Movement.GetState(fixture.ShipId));
    }

    [Fact]
    public void RemovingMovingActorMakesPendingArrivalAMissingReference()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        LocalMotionSegment motion = Assert.IsType<LocalMotionSegment>(
            fixture.Start(origin, Position(100, 0), new SimulationDuration(100)));
        var arrival = new SpatialMovementEvent.Arrive(
            fixture.ShipId,
            motion.Id,
            motion.Generation);

        bool removed = fixture.Movement.CommitRemove(
            fixture.ShipId,
            new SimulationTime(25));
        ScheduledEventDisposition disposition = fixture.Movement.HandleEvent(
            arrival,
            motion.Generation,
            motion.ArrivesAt);

        Assert.True(removed);
        Assert.Null(fixture.Movement.GetState(fixture.ShipId));
        Assert.Equal(
            ScheduledEventDisposition.IgnoredMissingReference,
            disposition);
    }

    [Fact]
    public void CheckpointRestoreContinuesActiveMotionWithoutRescheduling()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        fixture.Start(origin, Position(100, 40), new SimulationDuration(100));
        fixture.Engine.RunUntil(new SimulationTime(40));
        SpatialMovementCheckpoint movementCheckpoint = fixture.Movement
            .CaptureCheckpoint(fixture.Engine.CurrentTime).Value!;
        SimulationEngineCheckpoint<SpatialMovementEvent> engineCheckpoint =
            fixture.Engine.CaptureCheckpoint().Value!;

        CheckpointResult<SpatialMovement> movementRestoration =
            SpatialMovement.RestoreCheckpoint(
                movementCheckpoint,
                fixture.Engine.CurrentTime);
        var restoredRuntime = new MovementRuntime(movementRestoration.Value!);
        CheckpointResult<SimulationEngine<SpatialMovementEvent>> engineRestoration =
            SimulationEngine<SpatialMovementEvent>.RestoreCheckpoint(
                restoredRuntime,
                engineCheckpoint);

        Assert.True(movementRestoration.IsSuccess);
        Assert.True(engineRestoration.IsSuccess);
        Assert.Equal(
            fixture.Movement.CaptureSnapshot(fixture.Engine.CurrentTime),
            movementRestoration.Value!.CaptureSnapshot(
                engineRestoration.Value!.CurrentTime));
        Assert.Single(engineCheckpoint.Agenda.PendingEvents);

        fixture.Engine.RunUntil(new SimulationTime(100));
        engineRestoration.Value.RunUntil(new SimulationTime(100));

        Assert.Equal(
            fixture.Movement.CaptureSnapshot(fixture.Engine.CurrentTime),
            movementRestoration.Value.CaptureSnapshot(
                engineRestoration.Value.CurrentTime));
        Assert.Equal(
            fixture.Runtime.Dispositions,
            restoredRuntime.Dispositions);
    }

    [Fact]
    public void CheckpointRestoresConnectorTransitAndCompletionIdentity()
    {
        var shipId = new ShipId(3);
        var generation = new EventGeneration(7);
        var transitId = new ConnectorTransitId(1);
        var arrivesAt = new SimulationTime(100);
        var completionKey = new EventKey(
            arrivesAt,
            EventPhase.PhysicalCompletion,
            CreationSequence: 12);
        var checkpoint = new SpatialMovementCheckpoint(
            new IdSequenceCheckpoint(NextValue: 1),
            new IdSequenceCheckpoint(NextValue: 2),
            [
                new SpatialActorCheckpoint(
                    shipId,
                    generation,
                    new ShipSpatialStateCheckpoint.ConnectorTransit(
                        transitId,
                        generation,
                        new TransitConnectionId(9),
                        Position(new SystemId(1), 10, 20),
                        Position(new SystemId(2), -30, 40),
                        SimulationTime.Zero,
                        arrivesAt,
                        completionKey)),
            ]);

        CheckpointResult<SpatialMovement> restoration =
            SpatialMovement.RestoreCheckpoint(
                checkpoint,
                new SimulationTime(50));

        Assert.True(restoration.IsSuccess);
        var state = Assert.IsType<ShipSpatialState.ConnectorTransit>(
            restoration.Value!.GetState(shipId));
        Assert.Equal(transitId, state.Transit.Id);
        Assert.Equal(completionKey, state.Transit.CompletionEventKey);
        var pending = Assert.IsType<PendingMovementCompletion.Emergence>(
            restoration.Value.GetPendingCompletion(shipId));
        Assert.Equal(transitId, pending.TransitId);
        Assert.Equal(completionKey, pending.EventKey);
    }

    [Fact]
    public void CheckpointRestoresAllocatorBeyondRetiredMotion()
    {
        var fixture = new MovementFixture();
        SystemPosition origin = Position(0, 0);
        fixture.Movement.Add(fixture.ShipId, origin);
        fixture.Start(origin, Position(100, 0), new SimulationDuration(100));
        fixture.Engine.RunUntil(new SimulationTime(40));
        Assert.True(fixture.Movement.CommitCancel(
            fixture.ShipId,
            fixture.Engine.CurrentTime));
        SpatialMovementCheckpoint checkpoint = fixture.Movement
            .CaptureCheckpoint(fixture.Engine.CurrentTime).Value!;
        SpatialMovement restored = SpatialMovement.RestoreCheckpoint(
            checkpoint,
            fixture.Engine.CurrentTime).Value!;
        SystemPosition restoredOrigin = Assert.IsType<SystemPosition>(
            restored.PositionAt(fixture.ShipId, fixture.Engine.CurrentTime));

        LocalMotionCommit<SpatialMovementEvent> next =
            restored.CommitStartOrReplace(
                fixture.ShipId,
                new TravelLeg.Local(
                    restoredOrigin,
                    Position(40, 100),
                    new SimulationDuration(60)),
                fixture.Engine.CurrentTime,
                static movementEvent => movementEvent);

        Assert.Equal(new MotionId(2), next.Motion!.Id);
    }

    [Fact]
    public void CheckpointCaptureRejectsActiveMotionWithoutCommittedEventKey()
    {
        var movement = new SpatialMovement();
        var shipId = new ShipId(1);
        SystemPosition origin = Position(0, 0);
        movement.Add(shipId, origin);
        _ = movement.CommitStartOrReplace(
            shipId,
            new TravelLeg.Local(
                origin,
                Position(100, 0),
                new SimulationDuration(100)),
            SimulationTime.Zero,
            static movementEvent => movementEvent);

        CheckpointResult<SpatialMovementCheckpoint> capture =
            movement.CaptureCheckpoint(SimulationTime.Zero);

        Assert.False(capture.IsSuccess);
        Assert.Equal(
            "$.checkpoint.spatial.actors[1].motion",
            capture.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsActiveMotionNotBelowAllocatorPosition()
    {
        var generation = new EventGeneration(0);
        var arrivesAt = new SimulationTime(100);
        var checkpoint = new SpatialMovementCheckpoint(
            new IdSequenceCheckpoint(NextValue: 1),
            new IdSequenceCheckpoint(NextValue: 1),
            [
                new SpatialActorCheckpoint(
                    new ShipId(1),
                    generation,
                    new ShipSpatialStateCheckpoint.LocalMotion(
                        new MotionId(1),
                        generation,
                        Position(0, 0),
                        Position(100, 0),
                        SimulationTime.Zero,
                        arrivesAt,
                        new EventKey(
                            arrivesAt,
                            EventPhase.PhysicalCompletion,
                            CreationSequence: 0))),
            ]);

        CheckpointResult<SpatialMovement> restoration =
            SpatialMovement.RestoreCheckpoint(
                checkpoint,
                new SimulationTime(50));

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.spatial.actors[0].state",
            restoration.Failure!.Path);
    }

    [Fact]
    public void RestoreAcceptsUnorderedActorsAndCanonicalizesCapture()
    {
        var checkpoint = new SpatialMovementCheckpoint(
            new IdSequenceCheckpoint(NextValue: 1),
            new IdSequenceCheckpoint(NextValue: 1),
            [
                new SpatialActorCheckpoint(
                    new ShipId(2),
                    new EventGeneration(0),
                    new ShipSpatialStateCheckpoint.AtPosition(Position(20, 0))),
                new SpatialActorCheckpoint(
                    new ShipId(1),
                    new EventGeneration(0),
                    new ShipSpatialStateCheckpoint.AtPosition(Position(10, 0))),
            ]);

        SpatialMovement restored = SpatialMovement.RestoreCheckpoint(
            checkpoint,
            SimulationTime.Zero).Value!;

        Assert.Equal(
            [new ShipId(1), new ShipId(2)],
            restored.CaptureCheckpoint(SimulationTime.Zero).Value!.Actors
                .Select(actor => actor.ShipId));
    }

    private static SystemPosition Position(long x, long y) =>
        Position(new SystemId(1), x, y);

    private static SystemPosition Position(SystemId systemId, long x, long y) =>
        new(
            systemId,
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));

    private sealed class MovementFixture
    {
        public MovementFixture()
        {
            Runtime = new MovementRuntime(Movement);
            Engine = new SimulationEngine<SpatialMovementEvent>(Runtime, Agenda);
        }

        public ShipId ShipId { get; } = new(1);

        public SpatialMovement Movement { get; } = new();

        public EventAgenda<SpatialMovementEvent> Agenda { get; } = new();

        public MovementRuntime Runtime { get; }

        public SimulationEngine<SpatialMovementEvent> Engine { get; }

        /// <summary>
        /// Starts one test motion and binds the exact key allocated by the
        /// agenda, matching the production coordinator's completed commit.
        /// </summary>
        public LocalMotionSegment? Start(
            SystemPosition origin,
            SystemPosition destination,
            SimulationDuration duration)
        {
            LocalMotionCommit<SpatialMovementEvent> commit =
                Movement.CommitStartOrReplace(
                    ShipId,
                    new TravelLeg.Local(origin, destination, duration),
                    Engine.CurrentTime,
                    static movementEvent => movementEvent);
            if (commit.EventProposal is { } eventProposal)
            {
                AgendaCommitResult result = AgendaCommitOwner.Commit(
                    Agenda,
                    [eventProposal]);
                Movement.BindCompletionEvent(
                    ShipId,
                    Assert.Single(result.EventKeys));
            }

            return commit.Motion;
        }
    }

    private sealed class MovementRuntime : ISimulationRuntime<SpatialMovementEvent>
    {
        private readonly SpatialMovement _movement;

        public MovementRuntime(SpatialMovement movement)
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
