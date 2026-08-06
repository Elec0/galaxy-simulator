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
                GameSessionTestFixture.Organization,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController),
            new InitialShipSetup(
                GameSessionTestFixture.Entity,
                secondShip,
                new InventoryId(2),
                GameSessionTestFixture.Organization,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(10, 0),
                GameSessionTestFixture.PlayerController),
        };

        Assert.Throws<ArgumentException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                setupShips,
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
    public void ConstructionMaterializationPublishesCompleteShipAndIsIdempotent()
    {
        var facilityId = new FacilityId(1);
        var design = new ShipDesign(
            new ConstructionDesignId(2),
            "Materialized Ship",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(25));
        GameSession session = CreateSession(facilityId, design);
        (ConstructionProcess process, ConstructionOrderId orderId) =
            PrepareCompletedConstruction(facilityId, design);
        session.AdvanceTo(new SimulationTime(1000));
        ConstructionMaterializationEffect effect = Assert.IsType<ConstructionMaterializationEffect>(
            process.CompleteActive(session.CurrentTime));

        ConstructionEntityMaterializationResult.Materialized result =
            Assert.IsType<ConstructionEntityMaterializationResult.Materialized>(
                session.MaterializeConstruction(process, effect));
        GameShipSnapshot ship = Assert.Single(
            session.CaptureSnapshot().Ships,
            candidate => candidate.Id == result.ShipId);

        Assert.Equal(new EntityId(2), result.EntityId);
        Assert.Equal(new ShipId(2), result.ShipId);
        Assert.Equal(new InventoryId(2), result.CargoInventoryId);
        Assert.Equal(result.EntityId, ship.EntityId);
        Assert.Equal(GameSessionTestFixture.Organization, ship.OrganizationId);
        Assert.Equal(design.Id, ship.DesignId);
        Assert.Equal(result.CargoInventoryId, ship.CargoInventoryId);
        Assert.Equal(design.CargoCapacity, ship.CargoCapacity);
        Assert.Equal(GameSessionTestFixture.Position(20, 30), ship.Position);
        Assert.Equal(GameSessionTestFixture.PlayerController, ship.Control.BaseController);
        Assert.Null(ship.Control.TemporaryOverride);
        Assert.Null(ship.CurrentOrder);
        Assert.Empty(ship.QueuedOrders);
        Assert.Empty(ship.SuspendedOrders);
        Assert.Equal(result.ShipId, session.ResolveShip(result.EntityId));
        Assert.Equal(result.EntityId, session.ResolveEntity(result.ShipId));
        Assert.Equal(
            ConstructionOrderStatus.Completed,
            process.GetOrder(orderId)?.Status);
        Assert.Equal(
            new ConstructionMaterializationIdentity.Ship(
                result.EntityId,
                result.ShipId,
                result.CargoInventoryId),
            process.GetMaterializationReceipt(orderId)?.Identity);

        ConstructionEntityMaterializationResult repeated =
            session.MaterializeConstruction(process, effect);

        Assert.Equal(result, repeated);
        Assert.Equal(2, session.CaptureSnapshot().Ships.Count);
    }

    [Fact]
    public void RejectedConstructionMaterializationIsAtomicAndDoesNotBurnIds()
    {
        var facilityId = new FacilityId(1);
        var allowedDesign = new ShipDesign(
            new ConstructionDesignId(2),
            "Allowed Ship",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(25));
        var rejectedDesign = new ShipDesign(
            new ConstructionDesignId(3),
            "Rejected Ship",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(40));
        GameSession session = CreateSession(facilityId, allowedDesign);
        (ConstructionProcess rejectedProcess, ConstructionOrderId rejectedOrderId) =
            PrepareCompletedConstruction(facilityId, rejectedDesign);
        session.AdvanceTo(new SimulationTime(1000));
        ConstructionMaterializationEffect rejectedEffect =
            Assert.IsType<ConstructionMaterializationEffect>(
                rejectedProcess.CompleteActive(session.CurrentTime));

        var rejection = Assert.IsType<ConstructionEntityMaterializationResult.Deferred>(
            session.MaterializeConstruction(rejectedProcess, rejectedEffect));

        Assert.Equal(
            ConstructionMaterializationDeferredReason.DesignNotAllowed,
            rejection.Reason);
        Assert.Equal(
            ConstructionOrderStatus.AwaitingMaterialization,
            rejectedProcess.GetOrder(rejectedOrderId)?.Status);
        Assert.Same(
            rejectedEffect,
            rejectedProcess.GetPendingMaterialization(rejectedOrderId));
        Assert.Null(rejectedProcess.GetMaterializationReceipt(rejectedOrderId));
        Assert.Single(session.CaptureSnapshot().Ships);

        (ConstructionProcess allowedProcess, _) =
            PrepareCompletedConstruction(facilityId, allowedDesign);
        ConstructionMaterializationEffect allowedEffect =
            Assert.IsType<ConstructionMaterializationEffect>(
                allowedProcess.CompleteActive(session.CurrentTime));
        var materialized = Assert.IsType<ConstructionEntityMaterializationResult.Materialized>(
            session.MaterializeConstruction(allowedProcess, allowedEffect));

        Assert.Equal(new EntityId(2), materialized.EntityId);
        Assert.Equal(new ShipId(2), materialized.ShipId);
        Assert.Equal(new InventoryId(2), materialized.CargoInventoryId);
    }

    [Fact]
    public void PendingConstructionBatchUsesStableFacilityOrder()
    {
        var lowerFacility = new FacilityId(1);
        var higherFacility = new FacilityId(2);
        var design = new ShipDesign(
            new ConstructionDesignId(2),
            "Batch Ship",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(25));
        ShipMaterializationPolicy[] policies =
        [
            CreatePolicy(lowerFacility, design, 10),
            CreatePolicy(higherFacility, design, 20),
        ];
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [
                new InitialShipSetup(
                    GameSessionTestFixture.Entity,
                    GameSessionTestFixture.Ship,
                    GameSessionTestFixture.CargoInventory,
                    GameSessionTestFixture.Organization,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(0, 0),
                    GameSessionTestFixture.PlayerController),
            ],
            new ConnectorTopology([], []),
            policies,
            factRetentionCapacity: 256);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
        (ConstructionProcess lower, _) =
            PrepareCompletedConstruction(lowerFacility, design);
        (ConstructionProcess higher, _) =
            PrepareCompletedConstruction(higherFacility, design);
        session.AdvanceTo(new SimulationTime(1000));
        Assert.NotNull(lower.CompleteActive(session.CurrentTime));
        Assert.NotNull(higher.CompleteActive(session.CurrentTime));

        IReadOnlyList<ConstructionEntityMaterializationResult> results =
            session.MaterializePendingConstruction([higher, lower]);
        ConstructionEntityMaterializationResult.Materialized[] materialized =
            results
                .Select(Assert.IsType<ConstructionEntityMaterializationResult.Materialized>)
                .ToArray();

        Assert.Equal(
            [lowerFacility, higherFacility],
            materialized.Select(result => result.Effect.FacilityId));
        Assert.Equal(
            [new EntityId(2), new EntityId(3)],
            materialized.Select(result => result.EntityId));
        Assert.Equal(
            [new ShipId(2), new ShipId(3)],
            materialized.Select(result => result.ShipId));
        Assert.Equal(3, session.CaptureSnapshot().Ships.Count);
    }

    [Fact]
    public void RemovalUnpublishesCompleteEntityAndMakesScheduledArrivalStale()
    {
        GameSession session = GameSessionTestFixture.Create();
        GameplayCommandRecord move = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, move.Result.Status);
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

        Assert.Equal(
            ScheduledEventDisposition.IgnoredMissingReference,
            Assert.Single(session.EventRecords).Disposition);
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

    private static GameSession CreateSession(
        FacilityId facilityId,
        ShipDesign allowedDesign)
    {
        ShipMaterializationPolicy policy = CreatePolicy(
            facilityId,
            allowedDesign,
            20,
            30);
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [
                new InitialShipSetup(
                    GameSessionTestFixture.Entity,
                    GameSessionTestFixture.Ship,
                    GameSessionTestFixture.CargoInventory,
                    GameSessionTestFixture.Organization,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(0, 0),
                    GameSessionTestFixture.PlayerController),
            ],
            new ConnectorTopology([], []),
            [policy],
            factRetentionCapacity: 256);
        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static ShipMaterializationPolicy CreatePolicy(
        FacilityId facilityId,
        ShipDesign allowedDesign,
        long x,
        long y = 0) =>
        new(
            facilityId,
            GameSessionTestFixture.Organization,
            GameSessionTestFixture.Position(x, y),
            GameSessionTestFixture.PlayerController,
            InitialShipOrderPolicy.NoInitialOrder,
            [allowedDesign]);

    private static GameSession CreateTwoShipSession()
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [
                new InitialShipSetup(
                    GameSessionTestFixture.Entity,
                    GameSessionTestFixture.Ship,
                    GameSessionTestFixture.CargoInventory,
                    GameSessionTestFixture.Organization,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(0, 0),
                    GameSessionTestFixture.PlayerController),
                new InitialShipSetup(
                    new EntityId(2),
                    new ShipId(2),
                    new InventoryId(2),
                    GameSessionTestFixture.Organization,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(100, 0),
                    GameSessionTestFixture.PlayerController),
            ],
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

    private static (ConstructionProcess Process, ConstructionOrderId OrderId)
        PrepareCompletedConstruction(
            FacilityId facilityId,
            ShipDesign design)
    {
        var inventory = new Inventory(new InventoryId(99), new Quantity(1));
        var process = new ConstructionProcess(
            facilityId,
            inventory.Id,
            new Throughput(1));
        ConstructionOrderId orderId = process.Enqueue(
            new ConstructionIdSequences(),
            design);
        SimulationTime completion = Assert.IsType<SimulationTime>(
            process.PrepareActive(
                new IdSequence<ReservationId>(),
                inventory,
                SimulationTime.Zero));
        Assert.Equal(new SimulationTime(1000), completion);
        return (process, orderId);
    }
}
