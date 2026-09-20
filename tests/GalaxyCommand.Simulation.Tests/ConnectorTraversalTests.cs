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
        GameShipSnapshot approaching = Assert.Single(initial.Ships);
        Assert.Null(approaching.Motion);
        Assert.NotNull(approaching.Maneuver);

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot traversing = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Null(traversing.Position);
        Assert.Null(traversing.Motion);
        Assert.Null(traversing.Maneuver);
        ConnectorTransitSnapshot transit = Assert.IsType<ConnectorTransitSnapshot>(
            traversing.Transit);
        Assert.Equal(new TransitConnectionId(1), transit.ConnectionId);
        Assert.Equal(
            50UL,
            transit.ArrivesAt.Milliseconds - transit.DepartedAt.Milliseconds);
        Assert.Equal(ShipOrderStatus.Active, traversing.CurrentOrder?.Status);

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot emerged = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Null(emerged.Transit);
        Assert.Equal(Position(DestinationSystem, -10), emerged.Position);
        Assert.NotNull(emerged.Maneuver);
        Assert.Equal(ShipOrderStatus.Active, emerged.CurrentOrder?.Status);

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot completed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.IsType<ShipSpatialSnapshotState.AtPosition>(
            completed.SpatialState);
        Assert.Equal(Position(DestinationSystem, 0), completed.Position);
        Assert.Null(completed.Motion);
        Assert.Null(completed.Transit);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(session.EventRecords.Count > 3);
        Assert.All(
            session.EventRecords,
            record => Assert.Equal(
                ScheduledEventDisposition.Applied,
                record.Disposition));
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
    public void CheckpointDuringAnalyticConnectorApproachPreservesContinuation()
    {
        GameSession uninterrupted = CreateSession();
        Submit(uninterrupted, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot approaching = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(uninterrupted.CaptureSnapshot().Ships).Maneuver);
        ManeuverScheduledPhase phase = Assert.IsType<ManeuverScheduledPhase>(
            approaching.CurrentPhase);
        var checkpointAt = new SimulationTime(
            phase.StartsAt.Milliseconds
                + ((phase.EndsAt.Milliseconds - phase.StartsAt.Milliseconds) / 2));
        uninterrupted.AdvanceTo(checkpointAt);
        CheckpointResult<GameSessionCheckpoint> capture =
            uninterrupted.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.ToString());
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            capture.Value);

        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(uninterrupted, Ship);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(restored, Ship);

        GameShipSnapshot expected = Assert.Single(
            uninterrupted.CaptureSnapshot().Ships);
        GameShipSnapshot actual = Assert.Single(
            restored.CaptureSnapshot().Ships);
        Assert.Equal(expected.EntityId, actual.EntityId);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.PrincipalId, actual.PrincipalId);
        Assert.Equal(expected.DesignId, actual.DesignId);
        Assert.Equal(expected.CargoInventoryId, actual.CargoInventoryId);
        Assert.Equal(expected.CargoCapacity, actual.CargoCapacity);
        Assert.Equal(expected.ManeuverCapabilityRevision, actual.ManeuverCapabilityRevision);
        Assert.Equal(expected.SpatialState, actual.SpatialState);
        Assert.Equal(expected.Velocity, actual.Velocity);
        Assert.Equal(expected.Heading, actual.Heading);
        Assert.Equal(expected.Control, actual.Control);
        Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
        Assert.Equal(expected.QueuedOrders, actual.QueuedOrders);
        Assert.Equal(expected.SuspendedOrders, actual.SuspendedOrders);
        Assert.Equal(uninterrupted.EventRecords, restored.EventRecords);
        Assert.Equal(
            uninterrupted.ReadFactsAfter(null, maximumCount: 32).Facts,
            restored.ReadFactsAfter(null, maximumCount: 32).Facts);
    }

    [Fact]
    public void AnalyticArrivalWithinToleranceCanEnterConnectorTransit()
    {
        GameSession session = CreateSession(originConnectorX: 100);
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot traversing = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.NotNull(traversing.Transit);
        Assert.Null(traversing.Position);
    }

    [Fact]
    public void ReplacementDuringAnalyticConnectorApproachCancelsTransitRoute()
    {
        GameSession session = CreateSession();
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot original = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        ManeuverScheduledPhase phase = Assert.IsType<ManeuverScheduledPhase>(
            original.CurrentPhase);
        session.AdvanceTo(new SimulationTime(
            phase.StartsAt.Milliseconds
                + ((phase.EndsAt.Milliseconds - phase.StartsAt.Milliseconds) / 2)));

        Submit(
            session,
            MoveTo(
                new NavigationDestination.Position(Position(OriginSystem, -20)),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot replacing = Assert.Single(session.CaptureSnapshot().Ships);
        TerminalManeuverSnapshot replacement = Assert.IsType<TerminalManeuverSnapshot>(
            replacing.Maneuver);
        Assert.NotEqual(original.MotionId, replacement.MotionId);
        Assert.Equal(new ShipOrderId(2), replacing.CurrentOrder?.Id);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session, Ship);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(Position(OriginSystem, -20), completed.Position);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.DoesNotContain(
            session.ReadFactsAfter(null, maximumCount: 32).Facts,
            envelope => envelope.Fact is ShipConnectorTransitStartedFact);
        Assert.DoesNotContain(
            session.EventRecords,
            record => record.Generation == original.Generation
                && record.Timestamp > phase.StartsAt);
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
        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);
        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot completed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(Position(DestinationSystem, -10), completed.Position);
        Assert.Null(completed.Motion);
        Assert.Null(completed.Maneuver);
        Assert.Null(completed.Transit);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(session.EventRecords.Count > 2);
    }

    [Fact]
    public void CancellingDuringTransitLeavesPhysicalTraversalInProgress()
    {
        GameSession session = CreateSession();
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);
        session.AdvanceTo(new SimulationTime(
            session.CurrentTime.Milliseconds + 10));

        GameplayCommandRecord cancellation = session.SubmitCommand(
            Player,
            new CancelShipOrderCommand(Ship, new ShipOrderId(1)));

        Assert.Equal(CommandResultStatus.Accepted, cancellation.Result.Status);
        GameShipSnapshot cancelled = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.NotNull(cancelled.Transit);
        Assert.Null(cancelled.Position);
        Assert.Equal(ShipOrderStatus.Cancelled, cancelled.CurrentOrder?.Status);

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

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
        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot transit = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.NotNull(transit.Transit?.CompletionEventKey);
        int eventsBeforeRemoval = session.EventRecords.Count;

        EntityRemovalResult result = session.RemoveEntity(new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo));

        Assert.IsType<EntityRemovalResult.Removed>(result);
        Assert.Empty(session.CaptureSnapshot().Ships);
        session.AdvanceTo(new SimulationTime(
            session.CurrentTime.Milliseconds + 100));
        Assert.Equal(eventsBeforeRemoval, session.EventRecords.Count);
    }

    [Fact]
    public void ReplacementDuringTransitWaitsAndWakesOnEmergence()
    {
        GameSession session = CreateSession();
        Submit(session, MoveTo(Destination(0), OrderPlacement.ReplaceAll));
        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);
        session.AdvanceTo(new SimulationTime(
            session.CurrentTime.Milliseconds + 10));

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

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

        GameShipSnapshot resumed = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Null(resumed.Transit);
        Assert.Equal(ShipOrderStatus.Active, resumed.CurrentOrder?.Status);
        Assert.Equal(Position(DestinationSystem, -10), resumed.Position);
        Assert.NotNull(resumed.Maneuver);

        GameSessionTestFixture.AdvanceCurrentMovement(session, Ship);

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

        GameSessionTestFixture.AdvanceUntilOrderTerminal(singleRun, Ship);
        incremental.AdvanceTo(new SimulationTime(5));
        incremental.AdvanceTo(new SimulationTime(35));
        GameSessionTestFixture.AdvanceUntilOrderTerminal(incremental, Ship);

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

    private static GameSession CreateSession(long originConnectorX = 10)
    {
        ConnectorTopology topology = CreateTopology(originConnectorX);
        var controller = new ActorController(
            ActorControllerKind.Player,
            Player.Id);
        var design = new ShipDesign(
            new ConstructionDesignId(1),
            "Connector Test Ship",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(10),
            GameSessionTestFixture.ManeuverCapability);
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
                    design,
                    Position(OriginSystem, 0),
                    controller),
            ],
            topology,
            [new ShipMaterializationPolicy(
                new FacilityId(1),
                GameSessionTestFixture.Principal,
                Position(OriginSystem, 0),
                controller,
                InitialShipOrderPolicy.NoInitialOrder,
                [design])],
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 256);
        return new GameSession(
            setup,
            new HierarchicalNavigationPlanner(
                topology,
                new ChebyshevLocalTravelTimeEstimator(
                    millisecondsPerMapUnit: 1)));
    }

    private static ConnectorTopology CreateTopology(long originConnectorX = 10) =>
        new(
            [
                new ConnectorEndpoint(
                    new ConnectorEndpointId(1),
                    Position(OriginSystem, originConnectorX)),
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

}
