using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ConnectorTraversalTests
{
    private static readonly SystemId OriginSystem = new(1);
    private static readonly SystemId DestinationSystem = new(2);
    private static readonly ShipId Ship = new(1);
    private static readonly CommandSource Player = new(
        CommandSourceKind.Player,
        new CommandSourceId("connector-player"));

    [Fact]
    public void MultiSystemOrderExecutesLocalTransitAndFinalLocalLegs()
    {
        GameSession session = CreateSession();

        GameplayCommandRecord command = session.SubmitCommand(
            Player,
            MoveTo(Destination(0), OrderPlacement.ReplaceAll));

        Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);
        GameSnapshot initial = session.CaptureSnapshot();
        Assert.Equal(2, initial.ConnectorEndpoints.Count);
        Assert.Equal(
            new TransitConnectionId(1),
            Assert.Single(initial.TransitConnections).Id);

        session.AdvanceTo(new SimulationTime(10));

        GameShipSnapshot traversing = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Null(traversing.Position);
        Assert.Null(traversing.Motion);
        Assert.Equal(new TransitConnectionId(1), traversing.Transit?.ConnectionId);
        Assert.Equal(new SimulationTime(60), traversing.Transit?.ArrivesAt);
        Assert.Equal(ShipOrderStatus.Active, traversing.CurrentOrder?.Status);

        session.AdvanceTo(new SimulationTime(60));

        GameShipSnapshot emerged = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Null(emerged.Transit);
        Assert.Equal(Position(DestinationSystem, -10), emerged.Motion?.Origin);
        Assert.Equal(Position(DestinationSystem, 0), emerged.Motion?.Destination);
        Assert.Equal(ShipOrderStatus.Active, emerged.CurrentOrder?.Status);

        session.AdvanceTo(new SimulationTime(70));

        GameShipSnapshot completed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.IsType<ShipSpatialSnapshotState.AtPosition>(
            completed.SpatialState);
        Assert.Equal(Position(DestinationSystem, 0), completed.Position);
        Assert.Null(completed.Motion);
        Assert.Null(completed.Transit);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.Equal(
            [
                ScheduledEventDisposition.Applied,
                ScheduledEventDisposition.Applied,
                ScheduledEventDisposition.Applied,
            ],
            session.EventRecords.Select(record => record.Disposition));
        Assert.Equal(
            [
                typeof(CommandAcceptedFact),
                typeof(ShipOrderTransitionFact),
                typeof(ShipLocalMotionStartedFact),
                typeof(ShipLocalMotionEndedFact),
                typeof(ShipConnectorTransitStartedFact),
                typeof(ShipConnectorTransitCompletedFact),
                typeof(ShipLocalMotionStartedFact),
                typeof(ShipLocalMotionEndedFact),
                typeof(ShipOrderTransitionFact),
            ],
            session.ReadFactsAfter(null, maximumCount: 20)
                .Facts
                .Select(envelope => envelope.Fact.GetType()));
    }

    [Fact]
    public void SystemDestinationCompletesWhenShipEmergesInRequestedSystem()
    {
        GameSession session = CreateSession();

        GameplayCommandRecord command = session.SubmitCommand(
            Player,
            MoveTo(
                new NavigationDestination.System(DestinationSystem),
                OrderPlacement.ReplaceAll));

        Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);
        session.AdvanceTo(new SimulationTime(60));

        GameShipSnapshot completed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(Position(DestinationSystem, -10), completed.Position);
        Assert.Null(completed.Motion);
        Assert.Null(completed.Transit);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.Equal(2, session.EventRecords.Count);
    }

    [Fact]
    public void CancellingDuringTransitLeavesPhysicalTraversalInProgress()
    {
        GameSession session = CreateSession();
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(20));

        GameplayCommandRecord cancellation = session.SubmitCommand(
            Player,
            new CancelShipOrderCommand(Ship, new ShipOrderId(1)));

        Assert.Equal(CommandResultStatus.Accepted, cancellation.Result.Status);
        GameShipSnapshot cancelled = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.NotNull(cancelled.Transit);
        Assert.Null(cancelled.Position);
        Assert.Equal(ShipOrderStatus.Cancelled, cancelled.CurrentOrder?.Status);

        session.AdvanceTo(new SimulationTime(60));

        GameShipSnapshot emerged = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(Position(DestinationSystem, -10), emerged.Position);
        Assert.Null(emerged.Motion);
        Assert.Null(emerged.Transit);
        Assert.Equal(ShipOrderStatus.Cancelled, emerged.CurrentOrder?.Status);
    }

    [Fact]
    public void RemovingDuringTransitCancelsScheduledEmergence()
    {
        GameSession session = CreateSession();
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(10));

        GameShipSnapshot transit = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.NotNull(transit.Transit?.CompletionEventKey);
        int eventsBeforeRemoval = session.EventRecords.Count;

        EntityRemovalResult result = session.RemoveEntity(new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo));

        Assert.IsType<EntityRemovalResult.Removed>(result);
        Assert.Empty(session.CaptureSnapshot().Ships);
        session.AdvanceTo(new SimulationTime(60));
        Assert.Equal(eventsBeforeRemoval, session.EventRecords.Count);
    }

    [Fact]
    public void ReplacementDuringTransitWaitsAndWakesOnEmergence()
    {
        GameSession session = CreateSession();
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(20));

        GameplayCommandRecord replacement = session.SubmitCommand(
            Player,
            MoveTo(Destination(20), OrderPlacement.ReplaceAll));

        Assert.Equal(CommandResultStatus.Accepted, replacement.Result.Status);
        GameShipSnapshot waiting = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.NotNull(waiting.Transit);
        Assert.Equal(new ShipOrderId(2), waiting.CurrentOrder?.Id);
        Assert.Equal(ShipOrderStatus.Waiting, waiting.CurrentOrder?.Status);
        Assert.Equal(
            ShipOrderReason.WaitingForConnectorTransitCompletion,
            waiting.CurrentOrder?.Reason);

        session.AdvanceTo(new SimulationTime(60));

        GameShipSnapshot resumed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Null(resumed.Transit);
        Assert.Equal(ShipOrderStatus.Active, resumed.CurrentOrder?.Status);
        Assert.Equal(Position(DestinationSystem, -10), resumed.Motion?.Origin);
        Assert.Equal(Position(DestinationSystem, 20), resumed.Motion?.Destination);

        session.AdvanceTo(new SimulationTime(90));

        GameShipSnapshot completed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(Position(DestinationSystem, 20), completed.Position);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
    }

    [Fact]
    public void MultiSystemTraversalIsDeterministicAcrossIncrementalAdvancement()
    {
        GameSession singleRun = CreateSession();
        GameSession incremental = CreateSession();
        Submit(singleRun, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        Submit(incremental, MoveTo(Destination(0), OrderPlacement.ReplaceAll));

        singleRun.AdvanceTo(new SimulationTime(70));
        incremental.AdvanceTo(new SimulationTime(5));
        incremental.AdvanceTo(new SimulationTime(35));
        incremental.AdvanceTo(new SimulationTime(60));
        incremental.AdvanceTo(new SimulationTime(70));

        GameShipSnapshot expected = Assert.Single(
            singleRun.CaptureSnapshot().Ships);
        GameShipSnapshot actual = Assert.Single(
            incremental.CaptureSnapshot().Ships);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.SpatialState, actual.SpatialState);
        Assert.Equal(expected.Control, actual.Control);
        Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
        Assert.Equal(expected.QueuedOrders, actual.QueuedOrders);
        Assert.Equal(expected.SuspendedOrders, actual.SuspendedOrders);
        Assert.Equal(singleRun.EventRecords, incremental.EventRecords);
        Assert.Equal(singleRun.CommandRecords, incremental.CommandRecords);
        Assert.Equal(
            singleRun.ReadFactsAfter(null, maximumCount: 20).Facts,
            incremental.ReadFactsAfter(null, maximumCount: 20).Facts);
    }

    private static GameSession CreateSession()
    {
        ConnectorTopology topology = CreateTopology();
        var setup = new GameSessionSetup(
            [
                new StarSystem(OriginSystem, "Origin"),
                new StarSystem(DestinationSystem, "Destination"),
            ],
            [
                new InitialShipSetup(
                    new EntityId(1),
                    Ship,
                    new InventoryId(1),
                    GameSessionTestFixture.Principal,
                    new ShipDesign(
                        new ConstructionDesignId(1),
                        "Connector Test Ship",
                        new ConstructionRecipe([], new Work(1)),
                        new Quantity(10)),
                    Position(OriginSystem, 0),
                    new ActorController(
                        ActorControllerKind.Player,
                        Player.Id)),
            ],
            topology,
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 256);
        return new GameSession(
            setup,
            new HierarchicalNavigationPlanner(
                topology,
                new LinearTravelTimeEstimator()));
    }

    private static ConnectorTopology CreateTopology() =>
        new(
            [
                new ConnectorEndpoint(
                    new ConnectorEndpointId(1),
                    Position(OriginSystem, 10)),
                new ConnectorEndpoint(
                    new ConnectorEndpointId(2),
                    Position(DestinationSystem, -10)),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    new SimulationDuration(50)),
            ]);

    private static MoveShipCommand MoveTo(
        NavigationDestination destination,
        OrderPlacement placement) =>
        new(Ship, destination, placement);

    private static NavigationDestination.Position Destination(long x) =>
        new NavigationDestination.Position(
            Position(DestinationSystem, x));

    private static SystemPosition Position(
        SystemId systemId,
        long x) =>
        new(
            systemId,
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(0)));

    private static void Submit(
        GameSession session,
        GameplayCommand command)
    {
        GameplayCommandRecord record = session.SubmitCommand(Player, command);
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
    }

    private sealed class LinearTravelTimeEstimator : ILocalTravelTimeEstimator
    {
        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination)
        {
            Assert.Equal(origin.SystemId, destination.SystemId);
            Int128 difference =
                (Int128)origin.Position.X.Units
                - destination.Position.X.Units;
            UInt128 magnitude = difference < 0
                ? (UInt128)(-difference)
                : (UInt128)difference;
            return new SimulationDuration(checked((ulong)magnitude));
        }
    }
}
