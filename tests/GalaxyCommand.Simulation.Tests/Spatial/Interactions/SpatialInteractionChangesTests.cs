using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialInteractionChangesTests
{
    [Fact]
    public void SubscriptionSeedsActorsAndRetainsChangesCommittedAfterCapture()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(2), Position(0));
        movement.Add(new ShipId(1), Position(0));
        using SpatialInteractionChanges changes = movement.AttachInteractionChanges();
        IReadOnlyList<SpatialInteractionChange> initial = changes.Capture();
        Assert.Equal(new ulong[] { 1, 2 }, initial.Select(change => change.ShipId.Value));
        Start(movement);
        changes.Acknowledge(initial);
        SpatialInteractionChange change = Assert.Single(changes.Capture());
        Assert.Equal(new ShipId(1), change.ShipId);
        Assert.Equal(new SpatialRevision(1), change.Revision);
    }

    [Fact]
    public void ArrivalAndRemovalCoalesceWhileIgnoredEventsDoNotPublishChanges()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        using SpatialInteractionChanges changes = movement.AttachInteractionChanges();
        LocalMotionSegment motion = Start(movement);
        changes.Acknowledge(changes.Capture());
        var arrive = new SpatialMovementEvent.Arrive(new ShipId(1), motion.Id, motion.Generation);
        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch,
            movement.HandleEvent(arrive, new EventGeneration(99), new SimulationTime(10)));
        Assert.Empty(changes.Capture());
        movement.HandleEvent(arrive, motion.Generation, new SimulationTime(10));
        Assert.Equal(new SpatialRevision(2), Assert.Single(changes.Capture()).Revision);
        movement.CommitRemove(new ShipId(1), new SimulationTime(10));
        Assert.Null(Assert.Single(changes.Capture()).Revision);
    }

    [Fact]
    public void SubscriptionsAreIndependentAndDisposalDetaches()
    {
        var movement = new SpatialMovement();
        using SpatialInteractionChanges first = movement.AttachInteractionChanges();
        using SpatialInteractionChanges second = movement.AttachInteractionChanges();
        first.Dispose();
        movement.Add(new ShipId(1), Position(0));
        Assert.Empty(first.Capture());
        Assert.Single(second.Capture());
        second.Acknowledge(second.Capture());
        movement.CommitRemove(new ShipId(1), SimulationTime.Zero);
        Assert.Null(Assert.Single(second.Capture()).Revision);
    }

    [Fact]
    public void CancellationAndConnectorBoundariesPublishTheirCommittedRevisions()
    {
        var movement = new SpatialMovement();
        var ship = new ShipId(1);
        movement.Add(ship, Position(0));
        using SpatialInteractionChanges changes = movement.AttachInteractionChanges();
        Start(movement);
        changes.Acknowledge(changes.Capture());
        movement.CommitCancel(ship, new SimulationTime(2));
        Assert.Equal(new SpatialRevision(2), Assert.Single(changes.Capture()).Revision);
        changes.Acknowledge(changes.Capture());
        var destination = new SystemPosition(new SystemId(2), Position(0).Position);
        ConnectorTransitCommit<SpatialMovementEvent> transit = movement.CommitStartConnector(ship,
            new TravelLeg.Connector(new TransitConnectionId(1), movement.PositionAt(ship, new SimulationTime(2))!.Value,
                destination, new SimulationDuration(10)), new SimulationTime(2), static value => value);
        Assert.Equal(new SpatialRevision(3), Assert.Single(changes.Capture()).Revision);
        changes.Acknowledge(changes.Capture());
        movement.HandleEvent(transit.EventProposal.Payload, transit.Transit.Generation, new SimulationTime(12));
        Assert.Equal(new SpatialRevision(4), Assert.Single(changes.Capture()).Revision);
    }

    private static LocalMotionSegment Start(SpatialMovement movement) => movement.CommitStartOrReplace(
        new ShipId(1), new TravelLeg.Local(Position(0), Position(10), new SimulationDuration(10)),
        SimulationTime.Zero, static value => value).Motion!;

    private static SystemPosition Position(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
