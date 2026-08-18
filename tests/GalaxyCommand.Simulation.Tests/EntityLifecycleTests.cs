using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class EntityLifecycleTests
{
    [Fact]
    public void SetupPublishesBidirectionalEntityIdentity()
    {
        GameSession session = GameSessionTestFixture.Create();

        Assert.Equal(
            GameSessionTestFixture.Ship,
            session.ResolveShip(GameSessionTestFixture.Entity));
        Assert.Equal(
            GameSessionTestFixture.Entity,
            session.ResolveEntity(GameSessionTestFixture.Ship));
        Assert.Null(session.ResolveShip(new EntityId(99)));
        Assert.Null(session.ResolveEntity(new ShipId(99)));
    }

    [Fact]
    public void SetupRejectsDuplicateEntityIdentityBeforeSessionConstruction()
    {
        var secondShip = new ShipId(2);
        var setupShips = new[]
        {
            new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController),
            new InitialShipSetup(
                GameSessionTestFixture.Entity,
                secondShip,
                new InventoryId(2),
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(10, 0),
                GameSessionTestFixture.PlayerController),
        };

        Assert.Throws<ArgumentException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                setupShips,
                GameSessionTestFixture.Relationships,
                GameSessionTestFixture.RootSeed,
                factRetentionCapacity: 256));
    }

    [Fact]
    public void SequenceAdvancesBeyondExplicitHighWaterMark()
    {
        var sequence = new IdSequence<EntityId>();

        sequence.AdvancePast(new EntityId(41));

        Assert.True(sequence.CanAllocate(2));
        Assert.Equal(new EntityId(42), sequence.Allocate());
        Assert.Equal(new EntityId(43), sequence.Allocate());
    }

    [Fact]
    public void SequenceRejectsAllocationBeyondExplicitMaximum()
    {
        var sequence = new IdSequence<EntityId>();

        sequence.AdvancePast(new EntityId(ulong.MaxValue));

        Assert.False(sequence.CanAllocate(1));
        Assert.Throws<InvalidOperationException>(() => sequence.Allocate());
    }

    [Fact]
    public void SequenceCheckpointRestoresExactNextValueAndExhaustion()
    {
        var sequence = new IdSequence<EntityId>();
        sequence.AdvancePast(new EntityId(41));

        CheckpointResult<IdSequence<EntityId>> restored =
            IdSequence<EntityId>.RestoreCheckpoint(
                sequence.CaptureCheckpoint());
        CheckpointResult<IdSequence<EntityId>> exhausted =
            IdSequence<EntityId>.RestoreCheckpoint(
                new IdSequenceCheckpoint(NextValue: null));

        Assert.True(restored.IsSuccess);
        Assert.Equal(new EntityId(42), restored.Value!.Allocate());
        Assert.True(exhausted.IsSuccess);
        Assert.False(exhausted.Value!.CanAllocate(1));
        Assert.Throws<InvalidOperationException>(
            () => exhausted.Value.Allocate());
    }

    [Fact]
    public void SequenceRestoreRejectsZeroNextValue()
    {
        CheckpointResult<IdSequence<EntityId>> restored =
            IdSequence<EntityId>.RestoreCheckpoint(
                new IdSequenceCheckpoint(NextValue: 0));

        Assert.False(restored.IsSuccess);
        Assert.Equal(
            "$.checkpoint.allocators.nextValue",
            restored.Failure!.Path);
    }

    [Fact]
    public void RemovalUnpublishesCompleteEntityAndCancelsScheduledArrival()
    {
        GameSession session = GameSessionTestFixture.Create();
        GameplayCommandRecord move = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, move.Result.Status);
        GameShipSnapshot moving = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.NotNull(moving.Motion?.CompletionEventKey);
        var request = new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Despawned,
            EntityCargoDisposition.DiscardCargo);

        var removed = Assert.IsType<EntityRemovalResult.Removed>(
            session.RemoveEntity(request));
        int factCount = session.ReadFactsAfter(null, 256).Facts.Count;

        Assert.Equal(GameSessionTestFixture.Ship, removed.ShipId);
        Assert.Equal(GameSessionTestFixture.CargoInventory, removed.CargoInventoryId);
        Assert.Null(session.ResolveShip(GameSessionTestFixture.Entity));
        Assert.Null(session.ResolveEntity(GameSessionTestFixture.Ship));
        Assert.Empty(session.CaptureSnapshot().Ships);
        Assert.Equal(removed, session.RemoveEntity(request));
        Assert.Equal(factCount, session.ReadFactsAfter(null, 256).Facts.Count);
        GamePresentationSnapshot presentation = session.CapturePresentation(
            new GamePresentationRequest(
                GameSessionTestFixture.Principal,
                [GameSessionTestFixture.Ship],
                GameSessionTestFixture.Ship,
                factCursor: null,
                maximumFactCount: 256));
        Assert.Empty(presentation.Selection.ResolvedShips);
        Assert.Equal(
            [GameSessionTestFixture.Ship],
            presentation.Selection.UnresolvedShipIds);
        Assert.Null(presentation.Selection.FocusedShip);
        Assert.Contains(
            presentation.SelectedShipFacts,
            fact => fact.Fact is EntityRemovedFact);

        session.AdvanceTo(new SimulationTime(100));

        Assert.Empty(session.EventRecords);
        Assert.IsType<EntityRemovedFact>(
            session.ReadFactsAfter(null, 256).Facts[^1].Fact);
        GameplayCommandRecord rejected = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(200, 0),
                OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
    }

    [Fact]
    public void MissingEntityRemovalRejectsWithoutMutation()
    {
        GameSession session = GameSessionTestFixture.Create();
        GameSnapshot before = session.CaptureSnapshot();
        int factsBefore = session.ReadFactsAfter(null, 256).Facts.Count;

        var rejection = Assert.IsType<EntityRemovalResult.Rejected>(
            session.RemoveEntity(new EntityRemovalRequest(
                new EntityId(99),
                EntityRemovalReason.Despawned,
                EntityCargoDisposition.DiscardCargo)));

        Assert.Equal(EntityRemovalRejectionReason.MissingEntity, rejection.Reason);
        GameSnapshot after = session.CaptureSnapshot();
        Assert.Equal(before.Time, after.Time);
        Assert.Equal(before.Systems, after.Systems);
        Assert.Equal(before.ConnectorEndpoints, after.ConnectorEndpoints);
        Assert.Equal(before.TransitConnections, after.TransitConnections);
        GameShipSnapshot beforeShip = Assert.Single(before.Ships);
        GameShipSnapshot afterShip = Assert.Single(after.Ships);
        Assert.Equal(beforeShip.EntityId, afterShip.EntityId);
        Assert.Equal(beforeShip.Id, afterShip.Id);
        Assert.Equal(beforeShip.CargoInventoryId, afterShip.CargoInventoryId);
        Assert.Equal(beforeShip.SpatialState, afterShip.SpatialState);
        Assert.Equal(beforeShip.Control, afterShip.Control);
        Assert.Null(afterShip.CurrentOrder);
        Assert.Empty(afterShip.QueuedOrders);
        Assert.Empty(afterShip.SuspendedOrders);
        Assert.Equal(factsBefore, session.ReadFactsAfter(null, 256).Facts.Count);
    }

    [Fact]
    public void RemovalFailsActiveInboundOrderAndPromotesOrdinaryQueue()
    {
        GameSession session = CreateTwoShipSession();
        SubmitMove(
            session,
            new NavigationDestination.Entity(GameSessionTestFixture.Entity),
            OrderPlacement.ReplaceAll);
        SubmitMove(
            session,
            GameSessionTestFixture.Destination(200, 0),
            OrderPlacement.Append);
        session.AdvanceTo(new SimulationTime(25));

        EntityRemovalResult result = session.RemoveEntity(new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo));

        Assert.IsType<EntityRemovalResult.Removed>(result);
        GameShipSnapshot survivor = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipId(2), survivor.Id);
        Assert.Equal(new ShipOrderId(2), survivor.CurrentOrder?.Id);
        Assert.Equal(ShipOrderStatus.Active, survivor.CurrentOrder?.Status);
        Assert.Empty(survivor.QueuedOrders);
        GameFactEnvelope[] removalFacts = RemovalFacts(session);
        var failed = Assert.Single(removalFacts.Select(fact => fact.Fact)
            .OfType<ShipOrderTransitionFact>(), fact =>
                fact.OrderId == new ShipOrderId(1)
                && fact.NextStatus == ShipOrderStatus.Failed);
        Assert.Equal(ShipOrderReason.TargetRemoved, failed.Reason);
        Assert.IsType<ShipLocalMotionEndedFact>(removalFacts[0].Fact);
        Assert.IsType<EntityRemovedFact>(removalFacts[^1].Fact);
    }

    [Fact]
    public void RemovalFailsQueuedInboundOrderWithoutDisturbingActiveMotion()
    {
        GameSession session = CreateTwoShipSession();
        SubmitMove(
            session,
            GameSessionTestFixture.Destination(200, 0),
            OrderPlacement.ReplaceAll);
        SubmitMove(
            session,
            new NavigationDestination.Entity(GameSessionTestFixture.Entity),
            OrderPlacement.Append);
        LocalMotionSnapshot motion = Assert.Single(session.CaptureSnapshot().Ships,
            ship => ship.Id == new ShipId(2)).Motion!;

        session.RemoveEntity(new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Despawned,
            EntityCargoDisposition.DiscardCargo));

        GameShipSnapshot survivor = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(motion, survivor.Motion);
        Assert.Equal(new ShipOrderId(1), survivor.CurrentOrder?.Id);
        Assert.Empty(survivor.QueuedOrders);
        ShipOrderTransitionFact failed = Assert.Single(
            RemovalFacts(session).Select(fact => fact.Fact)
                .OfType<ShipOrderTransitionFact>(),
            fact => fact.OrderId == new ShipOrderId(2));
        Assert.Equal(ShipOrderStatus.Failed, failed.NextStatus);
        Assert.Equal(ShipOrderReason.TargetRemoved, failed.Reason);
    }

    [Fact]
    public void RemovalFailsSuspendedInboundOrderDuringScriptedOverride()
    {
        GameSession session = CreateTwoShipSession();
        SubmitMove(
            session,
            new NavigationDestination.Entity(GameSessionTestFixture.Entity),
            OrderPlacement.ReplaceAll);
        var script = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("removal-test-script"));
        GameplayCommandRecord beginOverride = session.SubmitCommand(
            script,
            new BeginScriptedOverrideCommand(
                new ShipId(2),
                new ActorOverrideReasonId("removal-test"),
                default));
        Assert.Equal(CommandResultStatus.Accepted, beginOverride.Result.Status);

        session.RemoveEntity(new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo));

        GameShipSnapshot survivor = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Empty(survivor.SuspendedOrders);
        ShipOrderTransitionFact failed = Assert.Single(
            RemovalFacts(session).Select(fact => fact.Fact)
                .OfType<ShipOrderTransitionFact>(),
            fact => fact.OrderId == new ShipOrderId(1)
                && fact.NextStatus == ShipOrderStatus.Failed);
        Assert.Equal(ShipOrderReason.TargetRemoved, failed.Reason);
    }

    private static GameSession CreateTwoShipSession()
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [
                new InitialShipSetup(
                    GameSessionTestFixture.Entity,
                    GameSessionTestFixture.Ship,
                    GameSessionTestFixture.CargoInventory,
                    GameSessionTestFixture.Principal,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(0, 0),
                    GameSessionTestFixture.PlayerController),
                new InitialShipSetup(
                    new EntityId(2),
                    new ShipId(2),
                    new InventoryId(2),
                    GameSessionTestFixture.Principal,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(100, 0),
                    GameSessionTestFixture.PlayerController),
            ],
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 256);
        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static void SubmitMove(
        GameSession session,
        NavigationDestination destination,
        OrderPlacement placement)
    {
        GameplayCommandRecord result = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(new ShipId(2), destination, placement));
        Assert.Equal(CommandResultStatus.Accepted, result.Result.Status);
    }

    private static GameFactEnvelope[] RemovalFacts(GameSession session) =>
        session.ReadFactsAfter(null, 256).Facts
            .Where(fact => fact.Cause is EntityRemovalFactCause)
            .ToArray();

}
