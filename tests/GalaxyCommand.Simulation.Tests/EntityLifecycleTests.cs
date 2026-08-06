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
