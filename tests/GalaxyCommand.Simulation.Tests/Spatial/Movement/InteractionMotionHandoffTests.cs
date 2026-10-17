using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionMotionHandoffTests
{
    private static readonly InteractionMotionOwner Owner = new("test", 7);
    private static readonly SimulationTime Now = new(500);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandoffMatchesSeparateOperationsAndPreservesUnrelatedAgendaWork(bool reverse)
    {
        var (movement, agenda) = Create();
        var (reference, referenceAgenda) = Create();
        var requests = Requests(movement);
        var oldSchedules = requests.Select(request => Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(request.ShipId)).Maneuver).ToArray();
        var before = agenda.CaptureCheckpoint().Value!;
        int expectedCalls = before.PendingEvents.Count(item => item.Payload.ShipId.Value <= 2);
        int calls = 0;
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());

        Assert.True(movement.TryHandoffInteractionMotions(reverse ? requests.Reverse() : requests, Owner, Now, agenda,
            item => { calls++; return item; }));

        Assert.Equal(expectedCalls, calls);
        Assert.All(oldSchedules, schedule => Assert.True(schedule.IsInvalidated));
        Assert.Equal(AgendaCancellationCheck.Matches, reference.TryInterruptTerminalManeuvers(
            requests.Select(item => item.ShipId), Now, referenceAgenda, static item => item, out _));
        Assert.True(reference.TryCommitInteractionMotions(requests.Select(item => item with { ExpectedRevision = new SpatialRevision(2) }), Owner, Now));
        Assert.Equal(reference.CaptureSnapshot(Now), movement.CaptureSnapshot(Now));
        Assert.Equal(reference.CaptureCheckpoint(Now).Value!.Actors.Select(actor => (actor.ShipId, actor.Generation, actor.SpatialRevision)),
            movement.CaptureCheckpoint(Now).Value!.Actors.Select(actor => (actor.ShipId, actor.Generation, actor.SpatialRevision)));
        Assert.Equal(reference.CaptureSnapshot(new SimulationTime(750)), movement.CaptureSnapshot(new SimulationTime(750)));
        Assert.Equal(before.PendingEvents.Where(item => item.Payload.ShipId.Value == 3), agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(before.NextCreationSequence, agenda.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.All(notices.Capture(), notice => Assert.Equal(new SpatialRevision(3), notice.Revision));
        Assert.Equal(new ulong[] { 1, 2 }, notices.Capture().Select(item => item.ShipId.Value));
        Assert.All(requests, request => Assert.Null(movement.CaptureInteractionReadView(Now).Get(request.ShipId)!.InteractionReceipts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LaterRejectionLeavesAllSchedulesActorsAndNoticesUnchanged(int failure)
    {
        var (movement, agenda) = Create();
        var requests = Requests(movement);
        var pending = agenda.CaptureCheckpoint().Value!.PendingEvents.Last(item => item.Payload.ShipId.Value == 2);
        if (failure == 0) Assert.True(agenda.TryCancelExact(pending.Key, pending.Generation, pending.Payload));
        if (failure == 2) requests[1] = requests[1] with { ExpectedRevision = new SpatialRevision(0) };
        if (failure == 3) requests[1] = requests[1] with { ShipId = new ShipId(99) };
        AssertUnchanged(movement, agenda, () => Assert.False(movement.TryHandoffInteractionMotions(requests, Owner, Now, agenda,
            item => failure == 1 && item.ShipId.Value == 2 ? new SpatialMovementEvent.Maneuver(new ShipId(3), ((SpatialMovementEvent.Maneuver)item).Event) : item)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LaterPreparationExceptionCannotPartiallyHandoff(int failure)
    {
        var (movement, agenda) = Create();
        var requests = Requests(movement);
        if (failure == 1)
            requests[1] = requests[1] with { Interval = Segment(requests[1].Interval.Start with { Position = Point(99) }) };
        if (failure == 2)
            requests[1] = requests[1] with { Interval = new AnalyticManeuverSegment(Now, new SimulationTime(2500), requests[1].Interval.Start,
                new ShipAcceleration(long.MaxValue, 0), ShipAngularRate.Zero) };
        AssertUnchanged(movement, agenda, () =>
        {
            void Run() => movement.TryHandoffInteractionMotions(requests, Owner, Now, agenda,
                item => failure == 0 && item.ShipId.Value == 2 ? throw new InvalidOperationException("wrapper failed") : item);
            if (failure == 0) Assert.Throws<InvalidOperationException>(Run);
            else if (failure == 1) Assert.Throws<ArgumentException>(Run);
            else Assert.Throws<OverflowException>(Run);
        });
    }

    [Fact]
    public void BothTransitionRevisionsArePreflightedBeforeAnyCancellation()
    {
        var (movement, agenda) = Create();
        var saved = movement.CaptureCheckpoint(Now).Value!;
        var restore = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(saved.MotionIds, saved.TransitIds,
            saved.Actors.Select(actor => actor.ShipId.Value == 2 ? actor with { SpatialRevision = new SpatialRevision(ulong.MaxValue - 1) } : actor)), Now);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        movement = restore.Value!;
        var requests = Requests(movement);
        AssertUnchanged(movement, agenda, () => Assert.Throws<OverflowException>(() =>
            movement.TryHandoffInteractionMotions(requests, Owner, Now, agenda, static item => item)));
    }

    [Fact]
    public void BothGenerationIncrementsArePreflightedBeforeAnyCancellation()
    {
        var (movement, agenda) = Create();
        var saved = movement.CaptureCheckpoint(Now).Value!;
        var actors = saved.Actors.Select(actor =>
        {
            if (actor.ShipId.Value != 2) return actor;
            var state = Assert.IsType<ShipSpatialStateCheckpoint.AnalyticManeuver>(actor.State);
            var generation = new EventGeneration(ulong.MaxValue - 1);
            return actor with
            {
                Generation = generation,
                State = new ShipSpatialStateCheckpoint.AnalyticManeuver(state.Id, generation, state.Plan,
                    state.Objective, state.CurrentPhaseIndex, state.WaypointPhaseIndices, state.PendingEventKeys),
            };
        });
        var restore = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(saved.MotionIds, saved.TransitIds, actors), Now);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        movement = restore.Value!;
        AssertUnchanged(movement, agenda, () => Assert.Throws<OverflowException>(() =>
            movement.TryHandoffInteractionMotions(Requests(movement), Owner, Now, agenda, static item => item)));
    }

    [Fact]
    public void PendingPhysicalBoundaryCannotBeSkippedByHandoff()
    {
        var (movement, agenda) = Create();
        var boundary = Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(new ShipId(1))).Maneuver.CurrentPhase!.Value.EndsAt;
        agenda.AdvanceTo(boundary);
        var requests = new[] { new ShipId(1), new ShipId(2) }.Select(ship => new InteractionMotionRequest(ship,
            new AnalyticManeuverSegment(boundary, new SimulationTime(boundary.Milliseconds + 1000),
                movement.KinematicStateAt(ship, boundary)!.Value, ShipAcceleration.Zero, ShipAngularRate.Zero),
            movement.GetSpatialRevision(ship)!.Value)).ToArray();
        var pending = agenda.CaptureCheckpoint().Value!;
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.TryHandoffInteractionMotions(requests, Owner, boundary, agenda, static item => item));
        Assert.Equal(pending.PendingEvents, agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.All(requests, request => Assert.False(Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(request.ShipId)).Maneuver.IsInvalidated));
    }

    [Fact]
    public void MixedBatchPreflightsStationaryAndControlledParticipantsWithAnalyticSchedules()
    {
        var (movement, agenda) = Create();
        movement.Add(new ShipId(4), Point(40));
        movement.Add(new ShipId(5), Point(50));
        Assert.True(movement.TryCommitInteractionMotion(new ShipId(5), Owner,
            Segment(movement.KinematicStateAt(new ShipId(5), Now)!.Value), new SpatialRevision(0), Now));
        var requests = Requests(movement).Concat(new[] { new ShipId(4), new ShipId(5) }.Select(ship =>
            new InteractionMotionRequest(ship, Segment(movement.KinematicStateAt(ship, Now)!.Value), movement.GetSpatialRevision(ship)!.Value))).ToArray();
        Assert.True(movement.TryHandoffInteractionMotions(requests, Owner, Now, agenda, static item => item));
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(new ShipId(4)));
        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(new ShipId(5)));
        Assert.All(requests, request => Assert.IsType<ShipSpatialState.InteractionControlled>(movement.GetState(request.ShipId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedCancellationKeysRejectBeforeEitherScheduleIsInvalidated(bool combined)
    {
        var (movement, agenda) = Create();
        var saved = movement.CaptureCheckpoint(Now).Value!;
        var first = Assert.IsType<ShipSpatialStateCheckpoint.AnalyticManeuver>(saved.Actors[0].State);
        var second = Assert.IsType<ShipSpatialStateCheckpoint.AnalyticManeuver>(saved.Actors[1].State);
        var aliased = saved.Actors[1] with
        {
            State = new ShipSpatialStateCheckpoint.AnalyticManeuver(second.Id, second.Generation, second.Plan,
                second.Objective, second.CurrentPhaseIndex, second.WaypointPhaseIndices, first.PendingEventKeys),
        };
        var restore = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(saved.MotionIds, saved.TransitIds,
            [saved.Actors[0], aliased, saved.Actors[2]]), Now);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        movement = restore.Value!;
        var firstPending = Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(new ShipId(1))).Maneuver.PendingEvents!;
        var secondPending = Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(new ShipId(2))).Maneuver.PendingEvents!;
        var payloads = secondPending.Zip(firstPending).ToDictionary(pair => pair.First.Payload, pair => pair.Second.Payload);
        SpatialMovementEvent Wrap(SpatialMovementEvent item) => item.ShipId.Value == 2
            ? new SpatialMovementEvent.Maneuver(new ShipId(1), payloads[((SpatialMovementEvent.Maneuver)item).Event]) : item;
        AssertUnchanged(movement, agenda, () =>
        {
            if (combined)
                Assert.False(movement.TryHandoffInteractionMotions(Requests(movement), Owner, Now, agenda, Wrap));
            else
            {
                Assert.Equal(AgendaCancellationCheck.Mismatch, movement.TryInterruptTerminalManeuvers(
                    [new ShipId(1), new ShipId(2)], Now, agenda, Wrap, out var result));
                Assert.Null(result);
            }
        });
    }

    [Fact]
    public void InvalidBatchAndTimeRejectBeforeWrappingAndEmptyBatchIsNoop()
    {
        var (movement, agenda) = Create();
        var request = Requests(movement)[0];
        int calls = 0;
        SpatialMovementEvent Wrap(SpatialMovementEvent item) { calls++; return item; }
        AssertUnchanged(movement, agenda, () =>
        {
            Assert.Throws<ArgumentException>(() => movement.TryHandoffInteractionMotions([request, request], Owner, Now, agenda, Wrap));
            Assert.Throws<ArgumentOutOfRangeException>(() => movement.TryHandoffInteractionMotions([request with { ShipId = new ShipId(0) }], Owner, Now, agenda, Wrap));
            Assert.Throws<InvalidOperationException>(() => movement.TryHandoffInteractionMotions([request], Owner, SimulationTime.Zero, agenda, Wrap));
            Assert.True(movement.TryHandoffInteractionMotions([], Owner, Now, agenda, Wrap));
        });
        Assert.Equal(0, calls);
    }

    private static void AssertUnchanged(SpatialMovement movement, EventAgenda<SpatialMovementEvent> agenda, Action action)
    {
        var before = movement.CaptureCheckpoint(Now).Value!;
        var snapshot = movement.CaptureSnapshot(Now);
        var pending = agenda.CaptureCheckpoint().Value!;
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());
        action();
        Assert.Equal(snapshot, movement.CaptureSnapshot(Now));
        Assert.Equal(before.Actors.Select(actor => (actor.ShipId, actor.Generation, actor.SpatialRevision)),
            movement.CaptureCheckpoint(Now).Value!.Actors.Select(actor => (actor.ShipId, actor.Generation, actor.SpatialRevision)));
        Assert.Equal(pending.PendingEvents, agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(pending.NextCreationSequence, agenda.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.Empty(notices.Capture());
        foreach (var actor in before.Actors)
            if (movement.GetState(actor.ShipId) is ShipSpatialState.AnalyticManeuver analytic)
                Assert.False(analytic.Maneuver.IsInvalidated);
    }

    private static InteractionMotionRequest[] Requests(SpatialMovement movement) => new[] { new ShipId(1), new ShipId(2) }
        .Select(ship => new InteractionMotionRequest(ship, Segment(movement.KinematicStateAt(ship, Now)!.Value), movement.GetSpatialRevision(ship)!.Value)).ToArray();

    private static AnalyticManeuverSegment Segment(ShipKinematicState state) =>
        new(Now, new SimulationTime(1000), state, new ShipAcceleration(20000, 0), new ShipAngularRate(45000));

    private static (SpatialMovement Movement, EventAgenda<SpatialMovementEvent> Agenda) Create()
    {
        var movement = new SpatialMovement();
        var agenda = new EventAgenda<SpatialMovementEvent>();
        for (ulong id = 1; id <= 3; id++)
        {
            var ship = new ShipId(id);
            movement.Add(ship, Point((long)id * 10));
            var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
                movement.KinematicStateAt(ship, SimulationTime.Zero)!.Value, Point((long)id * 10 + 90), null,
                new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000), new ManeuverTurnRate(45000)).ExecutablePlan!;
            var start = movement.CommitStartTerminalManeuver(ship, plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static item => item);
            movement.BindTerminalManeuverEvents(ship, AgendaCommitOwner.Commit(agenda, start.EventProposals).EventKeys);
        }
        agenda.AdvanceTo(Now);
        return (movement, agenda);
    }

    private static SystemPosition Point(long x) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
