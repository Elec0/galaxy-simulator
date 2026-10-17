using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionMotionOwnershipTests
{
    private static readonly ShipId Ship = new(1);
    private static readonly InteractionMotionOwner Owner = new("test", 7);

    [Fact]
    public void CallerIntervalOwnsExactMotionAndReleasesFromAnIntermediateTime()
    {
        var movement = Create();
        var interval = Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(0), SimulationTime.Zero));
        var time = new SimulationTime(400);
        Assert.Equal(interval.StateAt(time), movement.KinematicStateAt(Ship, time));
        Assert.Equal(interval.StateAt(time).Position, Assert.Single(movement.CaptureSnapshot(time)).Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.PositionAt(Ship, new SimulationTime(1001)));
        Assert.True(movement.TryReleaseInteractionMotion(Ship, Owner, new SpatialRevision(1), time, out var released));
        Assert.Equal(interval.StateAt(time), released);
        Assert.Equal(released, movement.KinematicStateAt(Ship, time));
        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(Ship));
        Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(Ship));
    }

    [Fact]
    public void WrongOwnerAndStaleRevisionCannotReplaceOrReleaseMotion()
    {
        var movement = Create();
        var first = Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, first, new SpatialRevision(0), SimulationTime.Zero));
        var now = new SimulationTime(400);
        var next = Segment(now, new SimulationTime(1000), first.StateAt(now));
        Assert.False(movement.TryCommitInteractionMotion(Ship, new InteractionMotionOwner("other", 7), next, new SpatialRevision(1), now));
        Assert.False(movement.TryReleaseInteractionMotion(Ship, Owner, new SpatialRevision(0), now, out var result));
        Assert.Null(result);
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(Ship));
        Assert.Equal(first.StateAt(now), movement.KinematicStateAt(Ship, now));
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, next, new SpatialRevision(1), now));
        Assert.False(movement.TryReleaseInteractionMotion(Ship, Owner, new SpatialRevision(1), now, out _));
    }

    [Fact]
    public void MovementCheckpointRetainsBindingAndOriginalCurve()
    {
        var movement = Create();
        var interval = Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(0), SimulationTime.Zero));
        var at = new SimulationTime(400);
        var checkpoint = movement.CaptureCheckpoint(at);
        Assert.True(checkpoint.IsSuccess, checkpoint.Failure?.Message);
        var restore = SpatialMovement.RestoreCheckpoint(checkpoint.Value!, at);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        Assert.Equal(movement.CaptureSnapshot(new SimulationTime(800)), restore.Value!.CaptureSnapshot(new SimulationTime(800)));
        Assert.True(restore.Value.TryReleaseInteractionMotion(Ship, Owner, new SpatialRevision(1), new SimulationTime(800), out var actual));
        Assert.Equal(interval.StateAt(new SimulationTime(800)), actual);
    }

    [Fact]
    public void SweptDiscoveryUsesControlledCurveInsteadOfFrozenPublishedPosition()
    {
        var movement = Create();
        movement.Add(new ShipId(2), Point(5));
        var interval = Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(0), SimulationTime.Zero));
        var view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        var interest = new ShipPairInteractionInterest(new InteractionInterestId(1), "test", "cross", Ship, new ShipId(2), 0, new EventGeneration(0));
        var forecast = InteractionForecast.Evaluate(interest, view, new SimulationTime(2000));
        Assert.NotNull(forecast);
        Assert.Equal(new SimulationTime(708), forecast.Candidate.Contact.Timestamp);
        foreach (var strategy in new[] { InteractionPreparationStrategy.Scan, InteractionPreparationStrategy.SparseGrid(16, 4096), InteractionPreparationStrategy.BoundingBoxHierarchy(4) })
            Assert.Equal(forecast.Candidate.Contact, Assert.Single(InteractionSweptQuery.Prepare(view, new SimulationTime(2000))
                .PrepareScope([new ShipId(2)], strategy).Find(Ship, 0)).Contact);
    }

    [Theory]
    [InlineData(1, ScheduledEventDisposition.Applied)]
    [InlineData(2, ScheduledEventDisposition.IgnoredStaleGeneration)]
    public void ExactCompletionRetainsBriefContactButInterruptionDoesNot(ulong oldEnd, ScheduledEventDisposition expected)
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Point(-1));
        movement.Add(new ShipId(2), Point(0));
        var interval = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(oldEnd),
            movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value,
            new ShipAcceleration(6000000000, 0), ShipAngularRate.Zero);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(0), SimulationTime.Zero));
        var interest = new ShipPairInteractionInterest(new InteractionInterestId(1), "test", "brief", Ship,
            new ShipId(2), 0, new EventGeneration(0));
        var forecast = InteractionForecast.Evaluate(interest, movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(oldEnd));
        Assert.NotNull(forecast);
        Assert.Equal(new SimulationTime(1), forecast.Candidate.Contact.Timestamp);
        Assert.False(forecast.Candidate.Contact.InRangeAtTimestamp);
        var now = new SimulationTime(1);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner,
            Segment(now, new SimulationTime(3), interval.StateAt(now)), new SpatialRevision(1), now));
        Assert.Equal(expected, forecast.Validate(interest, movement.CaptureInteractionReadView(now), new EventGeneration(0)));
    }

    [Fact]
    public void InvalidStartAndFutureOverflowRejectBeforePublishingChanges()
    {
        var movement = Create();
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());
        var invalid = Segment(SimulationTime.Zero, new SimulationTime(1000),
            new ShipKinematicState(Point(9), ShipVelocity.Zero, ShipHeading.Zero));
        Assert.Throws<ArgumentException>(() => movement.TryCommitInteractionMotion(Ship, Owner, invalid, new SpatialRevision(0), SimulationTime.Zero));
        var overflow = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(2000),
            movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value,
            new ShipAcceleration(long.MaxValue, 0), ShipAngularRate.Zero);
        Assert.Throws<OverflowException>(() => movement.TryCommitInteractionMotion(Ship, Owner, overflow, new SpatialRevision(0), SimulationTime.Zero));
        Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(Ship));
        Assert.Equal(new SpatialRevision(0), movement.GetSpatialRevision(Ship));
        Assert.Empty(notices.Capture());
    }

    [Fact]
    public void CorruptControlledCheckpointRejectsOwnerAndKinematicMismatch()
    {
        var movement = Create();
        var interval = Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(0), SimulationTime.Zero));
        var now = new SimulationTime(400);
        var saved = movement.CaptureCheckpoint(now).Value!;
        SpatialMovementCheckpoint Change(SpatialActorCheckpoint actor) => new(saved.MotionIds, saved.TransitIds, [actor]);
        Assert.False(SpatialMovement.RestoreCheckpoint(Change(saved.Actors[0] with { Velocity = ShipVelocity.Zero }), now).IsSuccess);
        Assert.False(SpatialMovement.RestoreCheckpoint(Change(saved.Actors[0] with
        {
            State = new ShipSpatialStateCheckpoint.InteractionControlled(default, interval),
        }), now).IsSuccess);
        Assert.False(SpatialMovement.RestoreCheckpoint(saved, new SimulationTime(1001)).IsSuccess);
    }

    [Fact]
    public void OrdinaryNavigationCannotOverwriteControlledMotion()
    {
        var movement = Create();
        var interval = Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(0), SimulationTime.Zero));
        Assert.Throws<InvalidOperationException>(() => movement.CommitStartOrReplace(Ship,
            new TravelLeg.Local(Point(0), Point(10), new SimulationDuration(1000)), SimulationTime.Zero, static item => item));
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(Ship));
        Assert.IsType<ShipSpatialState.InteractionControlled>(movement.GetState(Ship));
    }

    [Theory]
    [InlineData(1, 619)]
    [InlineData(2, 817)]
    [InlineData(3, 708)]
    // Source x=10*t^2; targets are 10-10*t, 10-5*t^2, and 10-10*t^2 (seconds).
    public void ControlledDiscoveryMatchesMixedTrajectoriesAndAllIndexes(int kind, ulong expectedTime)
    {
        var movement = Create();
        var target = new ShipId(2);
        movement.Add(target, Point(10));
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner,
            Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value),
            new SpatialRevision(0), SimulationTime.Zero));
        if (kind == 1)
            movement.CommitStartOrReplace(target, new TravelLeg.Local(Point(10), Point(0), new SimulationDuration(1000)),
                SimulationTime.Zero, static item => item);
        else if (kind == 2)
        {
            var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
                movement.KinematicStateAt(target, SimulationTime.Zero)!.Value, Point(-80), null,
                new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
                new ManeuverTurnRate(45000)).ExecutablePlan!;
            movement.CommitStartTerminalManeuver(target, plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static item => item);
        }
        else
            Assert.True(movement.TryCommitInteractionMotion(target, Owner,
                new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(1000),
                    movement.KinematicStateAt(target, SimulationTime.Zero)!.Value, new ShipAcceleration(-20000, 0), ShipAngularRate.Zero),
                new SpatialRevision(0), SimulationTime.Zero));
        var view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        var interest = new ShipPairInteractionInterest(new InteractionInterestId(1), "test", "mixed", Ship, target, 0, new EventGeneration(0));
        var expected = InteractionForecast.Evaluate(interest, view, new SimulationTime(2000));
        Assert.NotNull(expected);
        Assert.Equal(new SimulationTime(expectedTime), expected.Candidate.Contact.Timestamp);
        foreach (var strategy in new[] { InteractionPreparationStrategy.Scan, InteractionPreparationStrategy.SparseGrid(16, 4096), InteractionPreparationStrategy.BoundingBoxHierarchy(4) })
            Assert.Equal(expected.Candidate.Contact, Assert.Single(InteractionSweptQuery.Prepare(view, new SimulationTime(2000))
                .PrepareScope([target], strategy).Find(Ship, 0)).Contact);
    }

    [Fact]
    public void AnalyticScheduleMustHandoffBeforeControlledOwnershipCanBegin()
    {
        var movement = Create();
        var agenda = new EventAgenda<SpatialMovementEvent>();
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value, Point(90), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!;
        var start = movement.CommitStartTerminalManeuver(Ship, plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static item => item);
        movement.BindTerminalManeuverEvents(Ship, AgendaCommitOwner.Commit(agenda, start.EventProposals).EventKeys);
        var now = new SimulationTime(500);
        agenda.AdvanceTo(now);
        var interval = Segment(now, new SimulationTime(1000), plan.StateAt(now));
        Assert.False(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(1), now));
        Assert.Equal(AgendaCancellationCheck.Matches, movement.TryInterruptTerminalManeuvers([Ship], now, agenda, static item => item, out _));
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, interval, new SpatialRevision(2), now));
        Assert.Empty(agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(interval.StateAt(new SimulationTime(800)), movement.KinematicStateAt(Ship, new SimulationTime(800)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchCommitsMixedEntryAndUpdateRegardlessOfInputOrder(bool reverse)
    {
        var movement = Create();
        var other = new ShipId(2);
        movement.Add(other, Point(10));
        var now = new SimulationTime(500);
        var first = Segment(SimulationTime.Zero, now, movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, first, new SpatialRevision(0), SimulationTime.Zero));
        InteractionMotionRequest[] requests =
        [
            new(Ship, Segment(now, new SimulationTime(1000), first.StateAt(now)), new SpatialRevision(1)),
            new(other, Segment(now, new SimulationTime(1000), movement.KinematicStateAt(other, now)!.Value), new SpatialRevision(0)),
        ];
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());
        Assert.True(movement.TryCommitInteractionMotions(reverse ? requests.Reverse() : requests, Owner, now));
        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(Ship));
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(other));
        foreach (var request in requests)
            Assert.Equal(request.Interval.StateAt(new SimulationTime(750)), movement.KinematicStateAt(request.ShipId, new SimulationTime(750)));
        Assert.Equal(new[] { Ship, other }, notices.Capture().Select(change => change.ShipId));
        Assert.Single(movement.CaptureInteractionReadView(now).Get(Ship)!.InteractionReceipts!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BatchFailureDoesNotCommitEarlierUpdateOrCompletionReceipt(int failure)
    {
        var movement = Create();
        var other = new ShipId(2);
        movement.Add(other, Point(10));
        var now = new SimulationTime(500);
        var first = Segment(SimulationTime.Zero, now, movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value);
        Assert.True(movement.TryCommitInteractionMotion(Ship, Owner, first, new SpatialRevision(0), SimulationTime.Zero));
        if (failure == 1)
            Assert.True(movement.TryCommitInteractionMotion(other, new InteractionMotionOwner("other", 1),
                Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(other, SimulationTime.Zero)!.Value),
                new SpatialRevision(0), SimulationTime.Zero));
        var state = movement.KinematicStateAt(other, now)!.Value;
        var second = failure == 2
            ? Segment(now, new SimulationTime(1000), state with { Position = Point(99) })
            : failure == 3
                ? new AnalyticManeuverSegment(now, new SimulationTime(2500), state, new ShipAcceleration(long.MaxValue, 0), ShipAngularRate.Zero)
                : Segment(now, new SimulationTime(1000), state);
        InteractionMotionRequest[] requests =
        [
            new(Ship, Segment(now, new SimulationTime(1000), first.StateAt(now)), new SpatialRevision(1)),
            new(other, second, failure == 0 ? new SpatialRevision(99) : movement.GetSpatialRevision(other)!.Value),
        ];
        var before = movement.CaptureSnapshot(now);
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());
        if (failure == 2)
            Assert.Throws<ArgumentException>(() => movement.TryCommitInteractionMotions(requests, Owner, now));
        else if (failure == 3)
            Assert.Throws<OverflowException>(() => movement.TryCommitInteractionMotions(requests, Owner, now));
        else
            Assert.False(movement.TryCommitInteractionMotions(requests, Owner, now));
        Assert.Equal(before, movement.CaptureSnapshot(now));
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(Ship));
        Assert.Empty(notices.Capture());
        Assert.Null(movement.CaptureInteractionReadView(now).Get(Ship)!.InteractionReceipts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchPreflightsRevisionAndGenerationCapacity(bool generation)
    {
        var movement = Create();
        var other = new ShipId(2);
        movement.Add(other, Point(10));
        var saved = movement.CaptureCheckpoint(SimulationTime.Zero).Value!;
        var exhausted = saved.Actors[1] with
        {
            Generation = new EventGeneration(generation ? ulong.MaxValue : 0),
            SpatialRevision = new SpatialRevision(generation ? 0 : ulong.MaxValue),
        };
        movement = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(saved.MotionIds, saved.TransitIds,
            [saved.Actors[0], exhausted]), SimulationTime.Zero).Value!;
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());
        InteractionMotionRequest Request(ShipId id) => new(id,
            Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(id, SimulationTime.Zero)!.Value),
            movement.GetSpatialRevision(id)!.Value);
        Assert.Throws<OverflowException>(() => movement.TryCommitInteractionMotions([Request(Ship), Request(other)], Owner, SimulationTime.Zero));
        Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(Ship));
        Assert.Equal(new SpatialRevision(0), movement.GetSpatialRevision(Ship));
        Assert.Empty(notices.Capture());
    }

    [Fact]
    public void BatchRejectsDuplicateOrZeroIdentitiesAndAllowsEmptyInput()
    {
        var movement = Create();
        var request = new InteractionMotionRequest(Ship,
            Segment(SimulationTime.Zero, new SimulationTime(1000), movement.KinematicStateAt(Ship, SimulationTime.Zero)!.Value), new SpatialRevision(0));
        Assert.True(movement.TryCommitInteractionMotions([], Owner, SimulationTime.Zero));
        Assert.Throws<ArgumentException>(() => movement.TryCommitInteractionMotions([request, request], Owner, SimulationTime.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.TryCommitInteractionMotions([request with { ShipId = new ShipId(0) }], Owner, SimulationTime.Zero));
        Assert.False(movement.TryCommitInteractionMotions([request with { ShipId = new ShipId(99) }], Owner, SimulationTime.Zero));
        Assert.Equal(new SpatialRevision(0), movement.GetSpatialRevision(Ship));
    }

    private static SpatialMovement Create()
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Point(0));
        return movement;
    }

    private static AnalyticManeuverSegment Segment(SimulationTime start, SimulationTime end, ShipKinematicState state) =>
        new(start, end, state, new ShipAcceleration(20000, 0), new ShipAngularRate(45000));
    private static SystemPosition Point(long x) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
