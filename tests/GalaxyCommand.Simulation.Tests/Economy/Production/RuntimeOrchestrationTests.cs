using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class RuntimeOrchestrationTests
{
    [Fact]
    public void ProductionEvaluationDoesNotMutateAuthoritativeState()
    {
        ProductionSystemFixture fixture = CreateSingleLineFixture();
        fixture.Inventory.Add(fixture.Input, new Quantity(4));
        fixture.Line.Enqueue(
            fixture.ProductionIds,
            CreateRecipe(fixture.Input, fixture.Output),
            repeat: false);
        var system = new ProductionSystem();

        ProductionEvaluationBatch batch =
            system.CreateBatch([fixture.Line], fixture.Inventories);
        ProductionEvaluationResult evaluation = ProductionSystem.Evaluate(batch);

        Assert.Single(evaluation.ReservationProposals);
        Assert.Equal(new Quantity(4), fixture.Inventory.Stored(fixture.Input));
        Assert.Equal(Quantity.Zero, fixture.Inventory.Reserved(fixture.Input));
        Assert.Equal(ProductionJobStatus.WaitingForInputs, fixture.Line.ActiveJob?.Status);
        Assert.Null(fixture.Line.ActiveJob?.CompletesAt);
    }

    [Fact]
    public void ProductionCommitStartsWorkAndReturnsCompletionProposal()
    {
        ProductionSystemFixture fixture = CreateSingleLineFixture();
        fixture.Inventory.Add(fixture.Input, new Quantity(4));
        ProductionJobId jobId = fixture.Line.Enqueue(
            fixture.ProductionIds,
            CreateRecipe(fixture.Input, fixture.Output),
            repeat: false);
        var lines = new Dictionary<FacilityId, ProductionLine>
        {
            [fixture.Line.FacilityId] = fixture.Line,
        };
        var system = new ProductionSystem();

        ProductionReconciliationResult result = system.Reconcile(
            lines,
            fixture.Inventories,
            fixture.ReservationIds,
            SimulationTime.Zero);

        ProductionCompletionProposal completion =
            Assert.Single(result.Commit.CompletionProposals);
        Assert.Equal(jobId, completion.JobId);
        Assert.Equal<ulong>(1_250, completion.Timestamp.Milliseconds);
        Assert.Equal(Quantity.Zero, fixture.Inventory.Stored(fixture.Input));
        Assert.Equal(ProductionJobStatus.Running, fixture.Line.ActiveJob?.Status);
        Assert.Collection(
            result.Measurements,
            measurement => Assert.Equal(
                RuntimeMeasurementStage.BatchPreparation,
                measurement.Stage),
            measurement => Assert.Equal(
                RuntimeMeasurementStage.Evaluation,
                measurement.Stage),
            measurement => Assert.Equal(
                RuntimeMeasurementStage.Commit,
                measurement.Stage));

        ProductionCompletionCommitResult completed =
            ProductionSystem.CommitCompletion(
                lines,
                fixture.ProductionIds,
                fixture.Inventories,
                fixture.Line.FacilityId,
                completion.JobId,
                completion.Generation,
                completion.Timestamp);
        Assert.Equal(ScheduledEventDisposition.Applied, completed.Disposition);
        Assert.Equal(
            new ProductionOutputStoredEffect(
                fixture.Line.FacilityId,
                jobId,
                fixture.Output,
                new Quantity(2)),
            completed.OutputStored);
    }

    [Fact]
    public void LowerFacilityIdWinsSharedInventoryRegardlessOfEnumerationOrder()
    {
        static FacilityId Run(bool reverse)
        {
            var facilityIds = new IdSequence<FacilityId>();
            var inventoryIds = new IdSequence<InventoryId>();
            var materialIds = new IdSequence<MaterialId>();
            MaterialId input = materialIds.Allocate();
            MaterialId output = materialIds.Allocate();
            var inventory = new Inventory(inventoryIds.Allocate(), new Quantity(20));
            inventory.Add(input, new Quantity(4));
            var inventories = new InventoryRegistry();
            inventories.Add(inventory);
            var productionIds = new ProductionIdSequences();
            var lower = new ProductionLine(
                facilityIds.Allocate(),
                inventory.Id,
                new Throughput(4));
            var higher = new ProductionLine(
                facilityIds.Allocate(),
                inventory.Id,
                new Throughput(4));
            lower.Enqueue(productionIds, CreateRecipe(input, output), repeat: false);
            higher.Enqueue(productionIds, CreateRecipe(input, output), repeat: false);
            ProductionLine[] enumeration = reverse ? [higher, lower] : [lower, higher];
            var lines = new Dictionary<FacilityId, ProductionLine>
            {
                [lower.FacilityId] = lower,
                [higher.FacilityId] = higher,
            };
            var system = new ProductionSystem();
            ProductionEvaluationResult evaluation =
                ProductionSystem.Evaluate(system.CreateBatch(enumeration, inventories));

            system.Commit(
                evaluation,
                lines,
                inventories,
                new IdSequence<ReservationId>(),
                SimulationTime.Zero);

            Assert.Equal(ProductionJobStatus.WaitingForInputs, higher.ActiveJob?.Status);
            Assert.Equal(ProductionJobStatus.Running, lower.ActiveJob?.Status);
            return lower.FacilityId;
        }

        Assert.Equal(Run(reverse: false), Run(reverse: true));
    }

    [Fact]
    public void ConstructionEvaluationDoesNotMutateAuthoritativeState()
    {
        ConstructionSystemFixture fixture = CreateConstructionFixture();
        fixture.Inventory.Add(fixture.Material, new Quantity(4));
        fixture.Process.Enqueue(
            fixture.ConstructionIds,
            CreateConstructionDesign(fixture.DesignId, fixture.Material));
        var system = new ConstructionSystem();

        ConstructionEvaluationBatch batch =
            system.CreateBatch([fixture.Process], fixture.Inventories);
        ConstructionEvaluationResult evaluation = ConstructionSystem.Evaluate(batch);

        Assert.Single(evaluation.ReservationProposals);
        Assert.Equal(new Quantity(4), fixture.Inventory.Stored(fixture.Material));
        Assert.Equal(Quantity.Zero, fixture.Inventory.Reserved(fixture.Material));
        Assert.Equal(
            ConstructionOrderStatus.WaitingForInputs,
            fixture.Process.ActiveOrder?.Status);
        Assert.Null(fixture.Process.ActiveOrder?.CompletesAt);
    }

    [Fact]
    public void ConstructionCommitReturnsCompletionThenMaterializationEffects()
    {
        ConstructionSystemFixture fixture = CreateConstructionFixture();
        fixture.Inventory.Add(fixture.Material, new Quantity(4));
        ConstructionOrderId orderId = fixture.Process.Enqueue(
            fixture.ConstructionIds,
            CreateConstructionDesign(fixture.DesignId, fixture.Material));
        var processes = new Dictionary<FacilityId, ConstructionProcess>
        {
            [fixture.Process.FacilityId] = fixture.Process,
        };
        var system = new ConstructionSystem();

        ConstructionReconciliationResult reconciliation = system.Reconcile(
            processes,
            fixture.Inventories,
            fixture.ReservationIds,
            SimulationTime.Zero);
        ConstructionCompletionProposal completion =
            Assert.Single(reconciliation.Commit.CompletionProposals);
        var completionEventKey = new EventKey(
            completion.Timestamp,
            EventPhase.PhysicalCompletion,
            17);
        ConstructionCompletionCommitResult completed = ConstructionSystem.CommitCompletion(
            processes,
            completion.FacilityId,
            completion.OrderId,
            completion.Generation,
            completion.Timestamp,
            completionEventKey);

        Assert.Equal(orderId, completion.OrderId);
        Assert.Equal<ulong>(1_250, completion.Timestamp.Milliseconds);
        Assert.Equal(ScheduledEventDisposition.Applied, completed.Disposition);
        Assert.Equal(
            new ConstructionMaterializationEffect(
                fixture.Process.FacilityId,
                orderId,
                fixture.DesignId,
                completion.Timestamp,
                completion.Generation,
                completionEventKey),
            completed.Materialization);
        Assert.Equal(Quantity.Zero, fixture.Inventory.Stored(fixture.Material));
        Assert.Equal(
            ConstructionOrderStatus.AwaitingMaterialization,
            fixture.Process.GetOrder(orderId)?.Status);
        Assert.Null(fixture.Process.GetCompletedOrder(orderId));
        Assert.Collection(
            reconciliation.Measurements,
            measurement => Assert.Equal(
                RuntimeMeasurementStage.BatchPreparation,
                measurement.Stage),
            measurement => Assert.Equal(
                RuntimeMeasurementStage.Evaluation,
                measurement.Stage),
            measurement => Assert.Equal(
                RuntimeMeasurementStage.Commit,
                measurement.Stage));
    }

    [Fact]
    public void LowerConstructionFacilityWinsSharedInventoryRegardlessOfEnumerationOrder()
    {
        static FacilityId Run(bool reverse)
        {
            var facilityIds = new IdSequence<FacilityId>();
            var inventoryIds = new IdSequence<InventoryId>();
            var materialIds = new IdSequence<MaterialId>();
            var designIds = new IdSequence<ConstructionDesignId>();
            InventoryId inventoryId = inventoryIds.Allocate();
            MaterialId material = materialIds.Allocate();
            var inventory = new Inventory(inventoryId, new Quantity(20));
            inventory.Add(material, new Quantity(4));
            var inventories = new InventoryRegistry();
            inventories.Add(inventory);
            var lower = new ConstructionProcess(
                facilityIds.Allocate(),
                inventoryId,
                new Throughput(4));
            var higher = new ConstructionProcess(
                facilityIds.Allocate(),
                inventoryId,
                new Throughput(4));
            var constructionIds = new ConstructionIdSequences();
            lower.Enqueue(
                constructionIds,
                CreateConstructionDesign(designIds.Allocate(), material));
            higher.Enqueue(
                constructionIds,
                CreateConstructionDesign(designIds.Allocate(), material));
            ConstructionProcess[] enumeration =
                reverse ? [higher, lower] : [lower, higher];
            var processes = new Dictionary<FacilityId, ConstructionProcess>
            {
                [lower.FacilityId] = lower,
                [higher.FacilityId] = higher,
            };
            var system = new ConstructionSystem();
            ConstructionEvaluationResult evaluation = ConstructionSystem.Evaluate(
                system.CreateBatch(enumeration, inventories));

            system.Commit(
                evaluation,
                processes,
                inventories,
                new IdSequence<ReservationId>(),
                SimulationTime.Zero);

            Assert.Equal(ConstructionOrderStatus.Running, lower.ActiveOrder?.Status);
            Assert.Equal(
                ConstructionOrderStatus.WaitingForInputs,
                higher.ActiveOrder?.Status);
            return lower.FacilityId;
        }

        Assert.Equal(Run(reverse: false), Run(reverse: true));
    }

    [Fact]
    public void EconomicRuntimeSystemCommitsWavesAndDispatchesCompletion()
    {
        var facilityIds = new IdSequence<FacilityId>();
        FacilityId productionFacility = facilityIds.Allocate();
        FacilityId constructionFacility = facilityIds.Allocate();
        InventoryId inventoryId = new IdSequence<InventoryId>().Allocate();
        var materialIds = new IdSequence<MaterialId>();
        MaterialId input = materialIds.Allocate();
        MaterialId output = materialIds.Allocate();
        var inventory = new Inventory(inventoryId, new Quantity(20));
        inventory.Add(input, new Quantity(4));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        var production = new ProductionLine(
            productionFacility,
            inventoryId,
            new Throughput(4));
        production.Enqueue(
            new ProductionIdSequences(),
            CreateRecipe(input, output),
            repeat: false);
        var construction = new ConstructionProcess(
            constructionFacility,
            inventoryId,
            new Throughput(4));
        construction.Enqueue(
            new ConstructionIdSequences(),
            CreateConstructionDesign(
                new ConstructionDesignId(1),
                input));
        LocationId location = new IdSequence<LocationId>().Allocate();
        var navigation = new RouteGraph();
        navigation.AddLocation(location);
        var coordinator = new EconomicRuntimeCoordinator(
            new Dictionary<FacilityId, ProductionLine>
            {
                [productionFacility] = production,
            },
            new Dictionary<FacilityId, MaterialId>(),
            new Dictionary<FacilityId, LocationId>
            {
                [productionFacility] = location,
            },
            new Dictionary<FacilityId, ConstructionProcess>
            {
                [constructionFacility] = construction,
            },
            new Dictionary<FacilityId, LocationId>
            {
                [constructionFacility] = location,
            },
            inventories,
            new TransportBoard(),
            new ShipRegistry(),
            navigation,
            new ProductionIdSequences(),
            new TransportIdSequences(),
            new IdSequence<ReservationId>(),
            new IdSequence<CapacityReservationId>());

        var system = new EconomicRuntimeSystem(coordinator);
        EconomicReconciliationResult result =
            system.Reconcile(
                SimulationTime.Zero,
                new TransportTiming(
                    SimulationDuration.Zero,
                    new TransferRate(1),
                    new TransferRate(1)));

        Assert.Equal(ProductionJobStatus.Running, production.ActiveJob?.Status);
        Assert.Equal(
            ConstructionOrderStatus.WaitingForInputs,
            construction.ActiveOrder?.Status);
        Assert.Equal(Quantity.Zero, inventory.Stored(input));
        Assert.Equal(Quantity.Zero, inventory.Reserved(input));
        Assert.Equal(
            [
                "production",
                "production",
                "production",
                "construction",
                "construction",
                "construction",
                "logistics-publication",
                "logistics-publication",
                "logistics-publication",
                "logistics-assignment",
                "logistics-assignment",
                "logistics-assignment",
                "transport-advance",
                "transport-advance",
                "transport-advance",
            ],
            result.Measurements.Select(measurement => measurement.Domain));

        ProductionCompletionProposal completion = Assert.Single(
            result.Production.Commit.CompletionProposals);
        EconomicEventCommitResult commit = system.CommitEvent(
            new EconomicEvent.ProductionComplete(
                completion.FacilityId,
                completion.JobId),
            new EventKey(
                completion.Timestamp,
                EventPhase.PhysicalCompletion,
                0),
            completion.Generation,
            new TransportTiming(
                SimulationDuration.Zero,
                new TransferRate(1),
                new TransferRate(1)),
            completion.Timestamp);
        var productionCommit =
            Assert.IsType<EconomicEventCommitResult.Production>(commit);
        Assert.Equal(
            ScheduledEventDisposition.Applied,
            productionCommit.Disposition);
        Assert.Equal(
            output,
            productionCommit.Result.OutputStored?.MaterialId);
    }

    private static ProductionSystemFixture CreateSingleLineFixture()
    {
        var facilityIds = new IdSequence<FacilityId>();
        var inventoryIds = new IdSequence<InventoryId>();
        var materialIds = new IdSequence<MaterialId>();
        MaterialId input = materialIds.Allocate();
        MaterialId output = materialIds.Allocate();
        var inventory = new Inventory(inventoryIds.Allocate(), new Quantity(20));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        return new ProductionSystemFixture(
            new ProductionLine(facilityIds.Allocate(), inventory.Id, new Throughput(4)),
            inventory,
            inventories,
            new ProductionIdSequences(),
            new IdSequence<ReservationId>(),
            input,
            output);
    }

    private static Recipe CreateRecipe(MaterialId input, MaterialId output) =>
        new(
            [new KeyValuePair<MaterialId, Quantity>(input, new Quantity(4))],
            output,
            new Quantity(2),
            new Work(5));

    private static ConstructionSystemFixture CreateConstructionFixture()
    {
        var facilityIds = new IdSequence<FacilityId>();
        var inventoryIds = new IdSequence<InventoryId>();
        var materialIds = new IdSequence<MaterialId>();
        var designIds = new IdSequence<ConstructionDesignId>();
        var inventory = new Inventory(inventoryIds.Allocate(), new Quantity(20));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        return new ConstructionSystemFixture(
            new ConstructionProcess(
                facilityIds.Allocate(),
                inventory.Id,
                new Throughput(4)),
            inventory,
            inventories,
            new ConstructionIdSequences(),
            new IdSequence<ReservationId>(),
            materialIds.Allocate(),
            designIds.Allocate());
    }

    private static TestConstructionDesign CreateConstructionDesign(
        ConstructionDesignId designId,
        MaterialId material) =>
        new TestConstructionDesign(
            designId,
            "Test Construction",
            new ConstructionRecipe(
                [new KeyValuePair<MaterialId, Quantity>(material, new Quantity(4))],
                new Work(5)));

    private sealed record ProductionSystemFixture(
        ProductionLine Line,
        Inventory Inventory,
        InventoryRegistry Inventories,
        ProductionIdSequences ProductionIds,
        IdSequence<ReservationId> ReservationIds,
        MaterialId Input,
        MaterialId Output);

    private sealed record ConstructionSystemFixture(
        ConstructionProcess Process,
        Inventory Inventory,
        InventoryRegistry Inventories,
        ConstructionIdSequences ConstructionIds,
        IdSequence<ReservationId> ReservationIds,
        MaterialId Material,
        ConstructionDesignId DesignId);

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
