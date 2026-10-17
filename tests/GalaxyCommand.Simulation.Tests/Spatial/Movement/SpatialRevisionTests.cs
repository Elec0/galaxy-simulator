using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialRevisionTests
{
    private static readonly ShipId Ship = new(1);

    [Fact]
    public void RevisionExhaustionRejectsDepartureBeforeChangingStateOrAllocators()
    {
        var original = new SpatialMovement();
        original.Add(Ship, Position(0));
        SpatialMovementCheckpoint checkpoint = original.CaptureCheckpoint(SimulationTime.Zero).Value!;
        SpatialMovement restored = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(
            checkpoint.MotionIds, checkpoint.TransitIds,
            [checkpoint.Actors[0] with { SpatialRevision = new SpatialRevision(ulong.MaxValue) }]),
            SimulationTime.Zero).Value!;

        Assert.Throws<OverflowException>(() => Start(restored, Position(0), Position(10), 0));

        SpatialMovementCheckpoint after = restored.CaptureCheckpoint(SimulationTime.Zero).Value!;
        Assert.Equal(checkpoint.MotionIds, after.MotionIds);
        Assert.Equal(checkpoint.TransitIds, after.TransitIds);
        Assert.Equal(Position(0), restored.PositionAt(Ship, SimulationTime.Zero));
        Assert.Equal(new SpatialRevision(ulong.MaxValue), restored.GetSpatialRevision(Ship));
    }

    [Fact]
    public void RevisionNeverWraps()
    {
        Assert.Equal(new SpatialRevision(1), new SpatialRevision(0).Next());
        Assert.Throws<OverflowException>(() => new SpatialRevision(ulong.MaxValue).Next());
    }

    [Fact]
    public void InteractionReadViewRemainsStableAcrossLaterMotionChanges()
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Position(0));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        _ = Start(movement, Position(0), Position(10), 0);

        InteractionSpatialParticipant participant = Assert.IsType<InteractionSpatialParticipant>(view.Get(Ship));
        Assert.Equal(new SpatialRevision(0), participant.Revision);
        Assert.Equal(Position(0), participant.Spatial.Position);
        Assert.Null(participant.Spatial.Motion);
        Assert.Equal(new SpatialRevision(1), movement.CaptureInteractionReadView(SimulationTime.Zero).Get(Ship)!.Revision);
        Assert.Null(view.Get(new ShipId(2)));
    }

    [Fact]
    public void DepartureArrivalAndReturnAdvanceRevisionWithoutChangingCompletionGeneration()
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Position(0));
        Assert.Equal(new SpatialRevision(0), movement.GetSpatialRevision(Ship));
        LocalMotionSegment outward = Start(movement, Position(0), Position(10), 0);
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(Ship));
        Assert.Equal(new EventGeneration(0), outward.Generation);
        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(
            new SpatialMovementEvent.Arrive(Ship, outward.Id, outward.Generation),
            outward.Generation, new SimulationTime(10)));
        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(Ship));

        LocalMotionSegment returning = Start(movement, Position(10), Position(0), 10);
        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(
            new SpatialMovementEvent.Arrive(Ship, returning.Id, returning.Generation),
            returning.Generation, new SimulationTime(20)));

        Assert.Equal(Position(0), movement.PositionAt(Ship, new SimulationTime(20)));
        Assert.Equal(new SpatialRevision(4), movement.GetSpatialRevision(Ship));
        Assert.Equal(new EventGeneration(0), returning.Generation);
    }

    [Fact]
    public void ReplanningCancellationAndIgnoredEventsHaveDefinedRevisionBehavior()
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Position(0));
        LocalMotionSegment old = Start(movement, Position(0), Position(10), 0);
        _ = Start(movement, Position(1), Position(20), 1);
        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(Ship));
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration, movement.HandleEvent(
            new SpatialMovementEvent.Arrive(Ship, old.Id, old.Generation), old.Generation,
            new SimulationTime(10)));
        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(Ship));
        Assert.True(movement.CommitCancel(Ship, new SimulationTime(2)));
        Assert.Equal(new SpatialRevision(3), movement.GetSpatialRevision(Ship));
        Assert.False(movement.CommitCancel(Ship, new SimulationTime(2)));
        Assert.Equal(new SpatialRevision(3), movement.GetSpatialRevision(Ship));
        Assert.True(movement.CommitRemove(Ship, new SimulationTime(2)));
        Assert.Null(movement.GetSpatialRevision(Ship));
    }

    [Fact]
    public void ConnectorEntryAndEmergenceAdvanceRevision()
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Position(0));
        ConnectorTransitCommit<SpatialMovementEvent> commit = movement.CommitStartConnector(
            Ship, new TravelLeg.Connector(new TransitConnectionId(1), Position(0),
                Position(0, 2), new SimulationDuration(10)),
            SimulationTime.Zero, static value => value);
        Assert.Equal(new SpatialRevision(1), movement.GetSpatialRevision(Ship));
        Assert.Null(movement.PositionAt(Ship, SimulationTime.Zero));

        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(
            commit.EventProposal.Payload, commit.Transit.Generation, new SimulationTime(10)));

        Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(Ship));
        Assert.Equal(Position(0, 2), movement.PositionAt(Ship, new SimulationTime(10)));
    }

    [Fact]
    public void CheckpointRestoresExactStationaryAndMovingRevisions()
    {
        var movement = new SpatialMovement();
        movement.Add(Ship, Position(0));
        LocalMotionCommit<SpatialMovementEvent> commit = movement.CommitStartOrReplace(
            Ship, new TravelLeg.Local(Position(0), Position(10), new SimulationDuration(10)),
            SimulationTime.Zero, static value => value);
        movement.BindCompletionEvent(Ship, new EventKey(
            new SimulationTime(10), EventPhase.PhysicalCompletion, 0));
        SpatialMovementCheckpoint checkpoint = movement.CaptureCheckpoint(new SimulationTime(2)).Value!;

        SpatialMovement restored = SpatialMovement.RestoreCheckpoint(checkpoint, new SimulationTime(2)).Value!;

        Assert.Equal(new SpatialRevision(1), restored.GetSpatialRevision(Ship));
        Assert.Equal(ScheduledEventDisposition.Applied, restored.HandleEvent(
            commit.EventProposal!.Payload, commit.Motion!.Generation, new SimulationTime(10)));
        SpatialMovement stationary = SpatialMovement.RestoreCheckpoint(
            restored.CaptureCheckpoint(new SimulationTime(10)).Value!, new SimulationTime(10)).Value!;
        Assert.Equal(new SpatialRevision(2), stationary.GetSpatialRevision(Ship));
    }

    private static LocalMotionSegment Start(SpatialMovement movement, SystemPosition origin,
        SystemPosition destination, ulong now) => movement.CommitStartOrReplace(
            Ship, new TravelLeg.Local(origin, destination, new SimulationDuration(10)),
            new SimulationTime(now), static value => value).Motion!;

    private static SystemPosition Position(long x, ulong system = 1) => new(
        new SystemId(system), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
