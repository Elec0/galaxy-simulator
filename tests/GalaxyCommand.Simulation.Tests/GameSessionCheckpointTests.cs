using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionCheckpointTests
{
    [Fact]
    public void RestoredSessionContinuesPendingMovementLikeUninterruptedSession()
    {
        GameSession uninterrupted = CreateSession();
        GameplayCommandRecord first = uninterrupted.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(10, 0),
                OrderPlacement.ReplaceAll));
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            uninterrupted.CaptureCheckpoint().Value);

        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);
        uninterrupted.AdvanceTo(new SimulationTime(1_000));
        restored.AdvanceTo(new SimulationTime(1_000));

        GameSnapshot expected = uninterrupted.CaptureSnapshot();
        GameSnapshot actual = restored.CaptureSnapshot();
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Systems, actual.Systems);
        Assert.Equal(expected.ConnectorEndpoints, actual.ConnectorEndpoints);
        Assert.Equal(expected.TransitConnections, actual.TransitConnections);
        GameShipSnapshot expectedShip = Assert.Single(expected.Ships);
        GameShipSnapshot actualShip = Assert.Single(actual.Ships);
        Assert.Equal(expectedShip.EntityId, actualShip.EntityId);
        Assert.Equal(expectedShip.Id, actualShip.Id);
        Assert.Equal(expectedShip.PrincipalId, actualShip.PrincipalId);
        Assert.Equal(expectedShip.DesignId, actualShip.DesignId);
        Assert.Equal(expectedShip.CargoInventoryId, actualShip.CargoInventoryId);
        Assert.Equal(expectedShip.CargoCapacity, actualShip.CargoCapacity);
        Assert.Equal(expectedShip.SpatialState, actualShip.SpatialState);
        Assert.Equal(expectedShip.Control, actualShip.Control);
        Assert.Equal(expectedShip.CurrentOrder, actualShip.CurrentOrder);
        Assert.Equal(expectedShip.QueuedOrders, actualShip.QueuedOrders);
        Assert.Equal(expectedShip.SuspendedOrders, actualShip.SuspendedOrders);
        Assert.Equal(expected.Relationships.Principals, actual.Relationships.Principals);
        Assert.Equal(expected.Relationships.Standings, actual.Relationships.Standings);
        Assert.Equal(
            expected.Relationships.DiplomaticConditions,
            actual.Relationships.DiplomaticConditions);
        Assert.Equal(expected.Relationships.Grants, actual.Relationships.Grants);
        Assert.Equal(
            uninterrupted.ReadFactsAfter(null, 64).Facts,
            restored.ReadFactsAfter(null, 64).Facts);
        GameplayCommandRecord next = restored.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(20, 0),
                OrderPlacement.ReplaceAll));
        Assert.Equal(
            first.Envelope.Sequence.Value + 1,
            next.Envelope.Sequence.Value);
        Assert.Single(restored.EventRecords);
        Assert.Single(restored.CommandRecords);
    }

    [Fact]
    public void CaptureRejectsUnregisteredRuntimePolicyWithoutPartialCheckpoint()
    {
        GameSession session = GameSessionTestFixture.Create();

        CheckpointResult<GameSessionCheckpoint> result = session.CaptureCheckpoint();

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.runtimePolicies.travelTime", result.Failure!.Path);
    }

    [Fact]
    public void CaptureRejectsLiveDesignAbsentFromRuntimeManifest()
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController)],
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 64);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new ChebyshevLocalTravelTimeEstimator(100)));

        CheckpointResult<GameSessionCheckpoint> result = session.CaptureCheckpoint();

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.lifecycle.liveShips[0].designId", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsOwnerShipSetMismatchBeforePublishingSession()
    {
        GameSessionCheckpoint checkpoint = Capture(CreateSession());
        var corruptControl = new ActorControlRegistryCheckpoint([]);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Control = corruptControl });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.control.actors", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsPendingMovementEventWithUnknownShip()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(10, 0),
                OrderPlacement.ReplaceAll));
        GameSessionCheckpoint checkpoint = Capture(session);
        ScheduledEvent<GameEvent> pending = checkpoint.Engine.Agenda.PendingEvents[0];
        var movement = Assert.IsType<GameEvent.SpatialMovement>(pending.Payload);
        var arrive = Assert.IsType<SpatialMovementEvent.Arrive>(movement.Event);
        var corruptEvent = new ScheduledEvent<GameEvent>(
            pending.Key with { CreationSequence = pending.Key.CreationSequence + 1 },
            pending.Generation,
            new GameEvent.SpatialMovement(new SpatialMovementEvent.Arrive(
                new ShipId(99),
                arrive.MotionId,
                arrive.Generation)));
        var agenda = new EventAgendaCheckpoint<GameEvent>(
            checkpoint.Engine.Agenda.CurrentTime,
            checkpoint.Engine.Agenda.NextCreationSequence + 1,
            [pending, corruptEvent]);
        var engine = new SimulationEngineCheckpoint<GameEvent>(
            checkpoint.Engine.IsInitialized,
            checkpoint.Engine.AccruedThrough,
            agenda);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Engine = engine });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.engine.agenda.pendingEvents[1].payload", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsActiveMotionWithoutItsExactAgendaEvent()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(10, 0),
                OrderPlacement.ReplaceAll));
        GameSessionCheckpoint checkpoint = Capture(session);
        var agenda = new EventAgendaCheckpoint<GameEvent>(
            checkpoint.Engine.Agenda.CurrentTime,
            checkpoint.Engine.Agenda.NextCreationSequence,
            []);
        var engine = new SimulationEngineCheckpoint<GameEvent>(
            checkpoint.Engine.IsInitialized,
            checkpoint.Engine.AccruedThrough,
            agenda);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Engine = engine });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.movement.actors[0].state", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsFactCapacityThatDisagreesWithPolicyManifest()
    {
        GameSessionCheckpoint checkpoint = Capture(CreateSession());
        var facts = new GameFactStoreCheckpoint(
            checkpoint.Facts.Capacity + 1,
            checkpoint.Facts.Sequences,
            checkpoint.Facts.RetainedFacts);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Facts = facts });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.facts.capacity", result.Failure!.Path);
    }

    [Fact]
    public void RestoredEconomyContinuesConstructionLikeUninterruptedSession()
    {
        GameSession uninterrupted = CreateEconomySession();
        GameSessionCheckpoint checkpoint = Capture(uninterrupted);

        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);
        uninterrupted.AdvanceTo(new SimulationTime(1_000));
        restored.AdvanceTo(new SimulationTime(1_000));

        Assert.Equal(
            uninterrupted.CaptureSnapshot().Ships.Select(ship => ship.Id),
            restored.CaptureSnapshot().Ships.Select(ship => ship.Id));
        Assert.Equal(
            uninterrupted.ReadFactsAfter(null, 64).Facts,
            restored.ReadFactsAfter(null, 64).Facts);
    }

    [Fact]
    public void RestoreRejectsEconomicEventWithUnknownWorkflowIdentity()
    {
        GameSession session = CreateEconomySession();
        session.AdvanceTo(SimulationTime.Zero);
        GameSessionCheckpoint checkpoint = Capture(session);
        ScheduledEvent<GameEvent> pending = Assert.Single(
            checkpoint.Engine.Agenda.PendingEvents);
        var corrupt = new ScheduledEvent<GameEvent>(
            pending.Key,
            pending.Generation,
            new GameEvent.Economic(new EconomicEvent.ConstructionComplete(
                new FacilityId(1),
                new ConstructionOrderId(99))));
        var agenda = new EventAgendaCheckpoint<GameEvent>(
            checkpoint.Engine.Agenda.CurrentTime,
            checkpoint.Engine.Agenda.NextCreationSequence,
            [corrupt]);
        var engine = new SimulationEngineCheckpoint<GameEvent>(
            checkpoint.Engine.IsInitialized,
            checkpoint.Engine.AccruedThrough,
            agenda);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Engine = engine });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.engine.agenda.pendingEvents[0].payload", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsEconomyAnchorInUnknownSystem()
    {
        GameSessionCheckpoint checkpoint = Capture(CreateEconomySession());
        SessionEconomyCheckpoint economy = checkpoint.Economy!;
        EconomyFacilityCheckpoint facility = economy.Facilities[0]! with
        {
            Position = new SystemPosition(
                new SystemId(99),
                economy.Facilities[0]!.Position.Position),
        };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with
            {
                Economy = economy with { Facilities = [facility] },
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.facilities[0].position.systemId",
            result.Failure!.Path);
    }

    private static GameSessionCheckpoint Capture(GameSession session) =>
        Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);

    private static GameSession CreateSession()
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(
                new FacilityId(1),
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder,
                [GameSessionTestFixture.Design])],
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 64);
        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new ChebyshevLocalTravelTimeEstimator(100)));
    }

    private static GameSession CreateEconomySession()
    {
        var navigation = new DirectLocalNavigationPlanner(
            new ChebyshevLocalTravelTimeEstimator(100));
        LocationId locationId = new(1);
        FacilityId facilityId = new(1);
        InventoryId facilityInventoryId = new(2);
        SystemPosition position = GameSessionTestFixture.Position(0, 0);
        var economy = new GameSessionEconomySetup(
            [new InitialInventorySetup(
                facilityInventoryId,
                new Quantity(20),
                [])],
            [new EconomyFacilitySetup(
                facilityId,
                facilityInventoryId,
                locationId,
                position)],
            [],
            [new ConstructionFacilitySetup(facilityId, new Throughput(1))],
            [GameSessionTestFixture.Design],
            [new InitialConstructionOrderSetup(
                facilityId,
                GameSessionTestFixture.Design.Id)],
            [new InitialFreighterSetup(GameSessionTestFixture.Ship, locationId)],
            new HierarchicalLogisticsNavigation(
                new Dictionary<LocationId, SystemPosition>
                {
                    [locationId] = position,
                },
                navigation),
            new TransportTiming(
                SimulationDuration.Zero,
                new TransferRate(1),
                new TransferRate(1)));
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                position,
                GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(
                facilityId,
                GameSessionTestFixture.Principal,
                position,
                GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder,
                [GameSessionTestFixture.Design])],
            economy,
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 64);
        return new GameSession(setup, navigation);
    }
}
