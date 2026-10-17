using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionMotionReleaseBatchTests
{
    private static readonly ShipId First = new(1);
    private static readonly ShipId Second = new(2);
    private static readonly InteractionMotionOwner Owner = new("test", 7);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseMaterializesExactStatesAndOnlyCompletedIntervalReceipt(bool reverse)
    {
        var movement = Create();
        var now = new SimulationTime(500);
        var firstInterval = Assert.IsType<ShipSpatialState.InteractionControlled>(movement.GetState(First)).Interval;
        var secondInterval = Assert.IsType<ShipSpatialState.InteractionControlled>(movement.GetState(Second)).Interval;
        InteractionMotionReleaseRequest[] requests = [new(First, new SpatialRevision(1)), new(Second, new SpatialRevision(1))];
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());

        Assert.True(movement.TryReleaseInteractionMotions(reverse ? requests.Reverse() : requests, Owner, now, out var released));

        Assert.NotNull(released);
        Assert.Equal(new[] { First, Second }, released.Keys);
        Assert.Equal(firstInterval.StateAt(now), released[First]);
        Assert.Equal(secondInterval.StateAt(now), released[Second]);
        foreach (var pair in released)
        {
            Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(pair.Key));
            Assert.Equal(pair.Value, movement.KinematicStateAt(pair.Key, now));
            Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(pair.Key));
        }
        Assert.All(movement.CaptureCheckpoint(now).Value!.Actors, actor => Assert.Equal(new EventGeneration(2), actor.Generation));
        var view = movement.CaptureInteractionReadView(now);
        var receipt = Assert.Single(view.Get(First)!.InteractionReceipts!);
        Assert.Same(firstInterval, receipt.Interval);
        Assert.Equal(new SpatialRevision(1), receipt.PreviousRevision);
        Assert.Equal(new SpatialRevision(2), receipt.CompletedRevision);
        Assert.Null(view.Get(Second)!.InteractionReceipts);
        Assert.Equal(new[] { First, Second }, notices.Capture().Select(change => change.ShipId));
        var dictionary = Assert.IsAssignableFrom<IDictionary<ShipId, ShipKinematicState>>(released);
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.False(movement.TryReleaseInteractionMotions(requests, Owner, now, out var repeated));
        Assert.Null(repeated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectedParticipantLeavesWholeBatchAndReceiptsUnchanged(int failure)
    {
        var movement = Create(failure == 2 ? new InteractionMotionOwner("other", 7) : Owner);
        if (failure == 3)
            Assert.True(movement.TryReleaseInteractionMotion(Second, Owner, new SpatialRevision(1), SimulationTime.Zero, out _));
        var now = new SimulationTime(500);
        var before = movement.CaptureCheckpoint(now).Value!;
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());
        InteractionMotionReleaseRequest[] requests =
        [
            new(First, new SpatialRevision(1)),
            new(failure == 0 ? new ShipId(99) : Second,
                failure == 1 ? new SpatialRevision(0) : movement.GetSpatialRevision(Second)!.Value),
        ];

        Assert.False(movement.TryReleaseInteractionMotions(requests, Owner, now, out var released));

        Assert.Null(released);
        Assert.Equal(before.Actors, movement.CaptureCheckpoint(now).Value!.Actors);
        Assert.Empty(notices.Capture());
        Assert.Null(movement.CaptureInteractionReadView(now).Get(First)!.InteractionReceipts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExhaustedParticipantCannotPartiallyReleaseEarlierShip(bool generation)
    {
        var movement = Create();
        var saved = movement.CaptureCheckpoint(SimulationTime.Zero).Value!;
        var exhausted = saved.Actors[1] with
        {
            Generation = new EventGeneration(generation ? ulong.MaxValue : 1),
            SpatialRevision = new SpatialRevision(generation ? 1 : ulong.MaxValue),
        };
        var restore = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(saved.MotionIds, saved.TransitIds,
            [saved.Actors[0], exhausted]), SimulationTime.Zero);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        movement = restore.Value!;
        var now = new SimulationTime(500);
        var before = movement.CaptureCheckpoint(now).Value!;
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());

        Assert.Throws<OverflowException>(() => movement.TryReleaseInteractionMotions(
            [new(First, new SpatialRevision(1)), new(Second, exhausted.SpatialRevision)], Owner, now, out _));

        Assert.Equal(before.Actors, movement.CaptureCheckpoint(now).Value!.Actors);
        Assert.Empty(notices.Capture());
        Assert.Null(movement.CaptureInteractionReadView(now).Get(First)!.InteractionReceipts);
    }

    [Fact]
    public void OutOfBoundsParticipantDoesNotReleaseEarlierShip()
    {
        var movement = Create(secondEnd: 100);
        var before = movement.CaptureCheckpoint(SimulationTime.Zero).Value!;
        using var notices = movement.AttachInteractionChanges();
        notices.Acknowledge(notices.Capture());

        Assert.Throws<ArgumentOutOfRangeException>(() => movement.TryReleaseInteractionMotions(
            [new(First, new SpatialRevision(1)), new(Second, new SpatialRevision(1))], Owner, new SimulationTime(500), out _));

        Assert.Equal(before.Actors, movement.CaptureCheckpoint(SimulationTime.Zero).Value!.Actors);
        Assert.Empty(notices.Capture());
    }

    [Fact]
    public void InvalidOrThrowingInputDoesNotReleaseParticipantsAndEmptyBatchSucceeds()
    {
        var movement = Create();
        var request = new InteractionMotionReleaseRequest(First, new SpatialRevision(1));
        var before = movement.CaptureCheckpoint(SimulationTime.Zero).Value!;
        Assert.True(movement.TryReleaseInteractionMotions([], Owner, SimulationTime.Zero, out var empty));
        Assert.NotNull(empty);
        Assert.Empty(empty);
        Assert.Throws<ArgumentException>(() => movement.TryReleaseInteractionMotions([request, request], Owner, SimulationTime.Zero, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.TryReleaseInteractionMotions([request with { ShipId = new ShipId(0) }], Owner, SimulationTime.Zero, out _));
        Assert.Throws<ArgumentNullException>(() => movement.TryReleaseInteractionMotions(null!, Owner, SimulationTime.Zero, out _));
        Assert.Throws<ArgumentNullException>(() => movement.TryReleaseInteractionMotions([request, null!], Owner, SimulationTime.Zero, out _));
        Assert.ThrowsAny<ArgumentException>(() => movement.TryReleaseInteractionMotions([request], default, SimulationTime.Zero, out _));
        Assert.Throws<InvalidOperationException>(() => movement.TryReleaseInteractionMotions(Throwing(), Owner, SimulationTime.Zero, out _));
        Assert.Equal(before.Actors, movement.CaptureCheckpoint(SimulationTime.Zero).Value!.Actors);

        IEnumerable<InteractionMotionReleaseRequest> Throwing()
        {
            yield return request;
            throw new InvalidOperationException("Input failed.");
        }
    }

    private static SpatialMovement Create(InteractionMotionOwner? secondOwner = null, ulong secondEnd = 1000)
    {
        var movement = new SpatialMovement();
        foreach (ShipId ship in new[] { First, Second })
        {
            movement.Add(ship, new SystemPosition(new SystemId(1),
                new SpatialPosition(new SpatialCoordinate((long)ship.Value * 10), new SpatialCoordinate(0))));
            var interval = new AnalyticManeuverSegment(SimulationTime.Zero,
                new SimulationTime(ship == First ? 500 : secondEnd),
                movement.KinematicStateAt(ship, SimulationTime.Zero)!.Value,
                new ShipAcceleration(20000, 0), new ShipAngularRate(45000));
            Assert.True(movement.TryCommitInteractionMotion(ship, ship == First ? Owner : secondOwner ?? Owner,
                interval, new SpatialRevision(0), SimulationTime.Zero));
        }
        return movement;
    }
}
