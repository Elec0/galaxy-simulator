using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ConstructionTests
{
    [Fact]
    public void CatalogStoresDifferentConstructionDesignTypesByStableId()
    {
        var ids = new IdSequence<ConstructionDesignId>();
        var recipe = new ConstructionRecipe([], new Work(1));
        var first = new TestConstructionDesign(ids.Allocate(), "Structure", recipe);
        var second = new ShipDesign(
            ids.Allocate(),
            "Freighter",
            recipe,
            new Quantity(5));
        var catalog = new ConstructionDesignCatalog();

        catalog.Add(first);
        catalog.Add(second);

        Assert.Same(first, catalog.Get(first.Id));
        Assert.Same(second, catalog.Get(second.Id));
        Assert.Equal([first.Id, second.Id], catalog.Designs.Select(design => design.Id));
    }

    [Fact]
    public void ProductNeutralProcessCompletesDerivedDesign()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        InventoryId inventoryId = new IdSequence<InventoryId>().Allocate();
        MaterialId materialId = new IdSequence<MaterialId>().Allocate();
        var inventory = new Inventory(inventoryId, new Quantity(20));
        inventory.Add(materialId, new Quantity(6));
        var design = new TestConstructionDesign(
            new IdSequence<ConstructionDesignId>().Allocate(),
            "Test Structure",
            new ConstructionRecipe(
                [new KeyValuePair<MaterialId, Quantity>(materialId, new Quantity(6))],
                new Work(8)));
        var process = new ConstructionProcess(
            facilityId,
            inventoryId,
            new Throughput(2));
        ConstructionOrderId orderId = process.Enqueue(
            new ConstructionIdSequences(),
            design);

        SimulationTime completesAt = Assert.IsType<SimulationTime>(
            process.PrepareActive(
                new IdSequence<ReservationId>(),
                inventory,
                SimulationTime.Zero));
        ConstructionMaterializationEffect materialization =
            Assert.IsType<ConstructionMaterializationEffect>(
                process.CompleteActive(completesAt));
        ConstructionOrder pending = Assert.IsType<ConstructionOrder>(
            process.GetOrder(orderId));

        Assert.Equal(facilityId, materialization.FacilityId);
        Assert.Equal(orderId, materialization.OrderId);
        Assert.Equal(design.Id, materialization.DesignId);
        Assert.Equal(completesAt, materialization.CompletedAt);
        Assert.Equal(default, materialization.Generation);
        Assert.Null(materialization.CompletionEventKey);
        Assert.Same(design, pending.Design);
        Assert.Equal(ConstructionOrderStatus.AwaitingMaterialization, pending.Status);
        Assert.Null(process.GetCompletedOrder(orderId));
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.Applied,
            process.AcknowledgeMaterialization(materialization));
        Assert.Equal(ConstructionOrderStatus.Completed, pending.Status);
        Assert.Same(pending, process.GetCompletedOrder(orderId));
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.AlreadyAcknowledged,
            process.AcknowledgeMaterialization(materialization));
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.MismatchedMaterialization,
            process.AcknowledgeMaterialization(materialization with
            {
                CompletionEventKey = new EventKey(
                    completesAt,
                    EventPhase.PhysicalCompletion,
                    1),
            }));
        Assert.Equal(Quantity.Zero, inventory.Stored(materialId));
    }

    [Fact]
    public void ProcessPromotesQueuedDesignAfterCompletion()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        InventoryId inventoryId = new IdSequence<InventoryId>().Allocate();
        var designs = new IdSequence<ConstructionDesignId>();
        var process = new ConstructionProcess(
            facilityId,
            inventoryId,
            new Throughput(1));
        var ids = new ConstructionIdSequences();
        var first = new TestConstructionDesign(
            designs.Allocate(),
            "First",
            new ConstructionRecipe([], new Work(1)));
        var second = new TestConstructionDesign(
            designs.Allocate(),
            "Second",
            new ConstructionRecipe([], new Work(1)));
        ConstructionOrderId firstOrder = process.Enqueue(ids, first);
        ConstructionOrderId secondOrder = process.Enqueue(ids, second);
        var inventory = new Inventory(inventoryId, new Quantity(1));

        SimulationTime completesAt = Assert.IsType<SimulationTime>(
            process.PrepareActive(
                new IdSequence<ReservationId>(),
                inventory,
                SimulationTime.Zero));
        ConstructionMaterializationEffect materialization =
            Assert.IsType<ConstructionMaterializationEffect>(
                process.CompleteActive(completesAt));

        Assert.Equal(firstOrder, process.GetPendingMaterialization(firstOrder)?.OrderId);
        Assert.Equal(secondOrder, process.ActiveOrder?.Id);
        Assert.Same(second, process.ActiveOrder?.Design);
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.Applied,
            process.AcknowledgeMaterialization(materialization));
    }

    [Fact]
    public void RecipeInputsAreReadOnly()
    {
        MaterialId materialId = new IdSequence<MaterialId>().Allocate();
        var recipe = new ConstructionRecipe(
            [new KeyValuePair<MaterialId, Quantity>(materialId, new Quantity(1))],
            new Work(1));
        var inputs = Assert.IsAssignableFrom<IDictionary<MaterialId, Quantity>>(recipe.Inputs);

        Assert.Throws<NotSupportedException>(() =>
            inputs.Add(new MaterialId(2), new Quantity(1)));
    }

    [Fact]
    public void CompletionRetainsProductEffectUntilAcknowledged()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        InventoryId inventoryId = new IdSequence<InventoryId>().Allocate();
        var process = new ConstructionProcess(
            facilityId,
            inventoryId,
            new Throughput(1));
        ConstructionOrderId orderId = process.Enqueue(
            new ConstructionIdSequences(),
            new TestConstructionDesign(
                new ConstructionDesignId(1),
                "Failure Test",
                new ConstructionRecipe([], new Work(1))));
        var inventory = new Inventory(inventoryId, new Quantity(1));
        SimulationTime completesAt = Assert.IsType<SimulationTime>(
            process.PrepareActive(
                new IdSequence<ReservationId>(),
                inventory,
                SimulationTime.Zero));

        ConstructionMaterializationEffect materialization =
            Assert.IsType<ConstructionMaterializationEffect>(
                process.CompleteActive(completesAt));

        Assert.Equal(orderId, materialization.OrderId);
        Assert.Null(process.ActiveOrder);
        Assert.Equal(
            ConstructionOrderStatus.AwaitingMaterialization,
            process.GetOrder(orderId)?.Status);
        Assert.Same(
            materialization,
            process.GetPendingMaterialization(orderId));
        Assert.Null(process.GetCompletedOrder(orderId));
    }

    [Fact]
    public void CancelledCompletionCannotMaterializeReplacementOrder()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        InventoryId inventoryId = new IdSequence<InventoryId>().Allocate();
        var process = new ConstructionProcess(
            facilityId,
            inventoryId,
            new Throughput(1));
        var ids = new ConstructionIdSequences();
        var designIds = new IdSequence<ConstructionDesignId>();
        var firstDesign = new TestConstructionDesign(
            designIds.Allocate(),
            "First",
            new ConstructionRecipe([], new Work(1)));
        var secondDesign = new TestConstructionDesign(
            designIds.Allocate(),
            "Second",
            new ConstructionRecipe([], new Work(1)));
        ConstructionOrderId firstId = process.Enqueue(ids, firstDesign);
        ConstructionOrderId secondId = process.Enqueue(ids, secondDesign);
        var inventory = new Inventory(inventoryId, new Quantity(1));
        ConstructionOrder first = Assert.IsType<ConstructionOrder>(process.GetOrder(firstId));
        EventGeneration scheduledGeneration = first.Generation;
        SimulationTime completesAt = Assert.IsType<SimulationTime>(
            process.PrepareActive(
                new IdSequence<ReservationId>(),
                inventory,
                SimulationTime.Zero));
        Assert.True(process.CancelActive(inventory));
        Assert.Equal(secondId, process.ActiveOrder?.Id);
        Assert.Equal(
            completesAt,
            process.PrepareActive(
                new IdSequence<ReservationId>(),
                inventory,
                SimulationTime.Zero));
        ScheduledEventDisposition disposition = process.CompleteScheduled(
            firstId,
            scheduledGeneration,
            completesAt,
            null,
            out ConstructionMaterializationEffect? materialization);

        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration, disposition);
        Assert.Null(materialization);
        Assert.Equal(ConstructionOrderStatus.Cancelled, first.Status);
        Assert.Equal(secondId, process.ActiveOrder?.Id);
        Assert.Equal(ConstructionOrderStatus.Running, process.ActiveOrder?.Status);
    }

    [Fact]
    public void CancellingReservedConstructionReleasesInputs()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        InventoryId inventoryId = new IdSequence<InventoryId>().Allocate();
        MaterialId materialId = new IdSequence<MaterialId>().Allocate();
        var inventory = new Inventory(inventoryId, new Quantity(10));
        inventory.Add(materialId, new Quantity(2));
        var process = new ConstructionProcess(
            facilityId,
            inventoryId,
            new Throughput(1));
        ConstructionOrderId orderId = process.Enqueue(
            new ConstructionIdSequences(),
            new TestConstructionDesign(
                new ConstructionDesignId(1),
                "Cancelled",
                new ConstructionRecipe(
                    [
                        new KeyValuePair<MaterialId, Quantity>(
                            materialId,
                            new Quantity(4)),
                    ],
                    new Work(1))));
        Assert.Null(process.PrepareActive(
            new IdSequence<ReservationId>(),
            inventory,
            SimulationTime.Zero));
        ConstructionOrder order =
            Assert.IsType<ConstructionOrder>(process.GetOrder(orderId));
        EventGeneration originalGeneration = order.Generation;

        Assert.True(process.CancelActive(inventory));

        Assert.Equal(ConstructionOrderStatus.Cancelled, order.Status);
        Assert.Equal(originalGeneration.Next(), order.Generation);
        Assert.Equal(Quantity.Zero, inventory.Reserved(materialId));
        Assert.Equal(new Quantity(2), inventory.Stored(materialId));
    }

    private sealed class TestConstructionDesign : ConstructionDesign
    {
        public TestConstructionDesign(
            ConstructionDesignId id,
            string name,
            ConstructionRecipe recipe)
            : base(id, name, recipe)
        {
        }
    }
}
