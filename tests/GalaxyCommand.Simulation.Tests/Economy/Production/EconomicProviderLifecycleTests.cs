namespace GalaxyCommand.Simulation.Tests;

public sealed class EconomicProviderLifecycleTests
{
    private static readonly MaterialId Input = new(1);
    private static readonly MaterialId Output = new(2);

    [Fact]
    public void ProductionPauseRetuneAndResumeRetainExactWorkAndQueue()
    {
        var inventory = new Inventory(new(1), new(20));
        inventory.Add(Input, new(1));
        var ids = new ProductionIdSequences();
        var line = new ProductionLine(new(1), inventory.Id, new(3));
        ProductionJobId first = line.Enqueue(ids, new([new(Input, new(1))], Output, new(2), new(7)), false);
        ProductionJobId second = line.Enqueue(ids, new([], Output, new(1), new(1)), false);
        line.PrepareActive(new(), inventory, SimulationTime.Zero);
        EventGeneration generation = line.ActiveJob!.Generation;

        var pause = Assert.IsType<PreparedProductionProviderTransition>(line.PrepareProviderTransition(false, new(3), new(101)));
        Assert.True(line.CanApplyProviderTransition(pause));
        Assert.Null(line.ApplyProviderTransition(pause));
        Assert.False(line.IsProviderAvailable);
        Assert.Null(line.ActiveJob.CompletesAt);
        Assert.Equal(1, line.QueuedJobCount);
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration,
            line.CompleteScheduled(first, generation, ids, inventory, new(2_334), out _));

        var resume = Assert.IsType<PreparedProductionProviderTransition>(line.PrepareProviderTransition(true, new(5), new(1_000)));
        ProductionCompletionProposal completion = Assert.IsType<ProductionCompletionProposal>(line.ApplyProviderTransition(resume));
        Assert.Equal(new SimulationTime(2_340), completion.Timestamp);
        Assert.Equal(ScheduledEventDisposition.Applied,
            line.CompleteScheduled(first, completion.Generation, ids, inventory, completion.Timestamp, out bool stored));
        Assert.True(stored);
        Assert.Equal(new Quantity(2), inventory.Stored(Output));
        Assert.Equal(second, line.ActiveJob!.Id);
        Assert.Equal(Quantity.Zero, inventory.Stored(Input));
        Assert.False(line.CanApplyProviderTransition(resume));
    }

    [Fact]
    public void ConstructionPauseAndResumeDoNotConsumeRecipeTwice()
    {
        var inventory = new Inventory(new(1), new(20));
        inventory.Add(Input, new(1));
        var process = new ConstructionProcess(new(1), inventory.Id, new(3));
        var design = new ShipDesign(new(1), "Provider Ship", new([new(Input, new(1))], new(10)),
            new(5), GameSessionTestFixture.ManeuverCapability, 0, 0);
        ConstructionOrderId order = process.Enqueue(new(), design);
        process.PrepareActive(new(), inventory, SimulationTime.Zero);
        EventGeneration generation = process.ActiveOrder!.Generation;

        var pause = Assert.IsType<PreparedConstructionProviderTransition>(process.PrepareProviderTransition(false, new(3), new(501)));
        Assert.Null(process.ApplyProviderTransition(pause));
        Assert.False(process.IsProviderAvailable);
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration,
            process.CompleteScheduled(order, generation, new(3_334), null, out _));
        var resume = Assert.IsType<PreparedConstructionProviderTransition>(process.PrepareProviderTransition(true, new(7), new(2_000)));
        ConstructionCompletionProposal completion = Assert.IsType<ConstructionCompletionProposal>(process.ApplyProviderTransition(resume));

        Assert.Equal(new SimulationTime(3_214), completion.Timestamp);
        Assert.Equal(ScheduledEventDisposition.Applied,
            process.CompleteScheduled(order, completion.Generation, completion.Timestamp, null, out ConstructionMaterializationEffect? effect));
        Assert.NotNull(effect);
        Assert.Equal(order, effect.OrderId);
        Assert.Equal(Quantity.Zero, inventory.Stored(Input));
        Assert.Equal(ConstructionOrderStatus.AwaitingMaterialization, process.GetOrder(order)!.Status);
    }

    [Fact]
    public void DisabledProductionRetainsWaitingClaimsAndDoesNotRequestMoreInputs()
    {
        var inventory = new Inventory(new(1), new(20));
        inventory.Add(Input, new(1));
        var ids = new ProductionIdSequences();
        var line = new ProductionLine(new(1), inventory.Id, new(1));
        line.Enqueue(ids, new([new(Input, new(2))], Output, new(1), new(1)), false);
        var reservations = new IdSequence<ReservationId>();
        line.PrepareActive(reservations, inventory, SimulationTime.Zero);
        line.ApplyProviderTransition(Assert.IsType<PreparedProductionProviderTransition>(
            line.PrepareProviderTransition(false, new(1), new(100))));
        inventory.Add(Input, new(1));

        Assert.Null(line.PrepareActive(reservations, inventory, new(1_000)));
        Assert.Empty(line.UnmetInputs(inventory));
        Assert.Equal(new Quantity(1), inventory.Reserved(Input));
        Assert.Equal(new Quantity(1), inventory.Available(Input));
        Assert.Equal(ProductionJobStatus.WaitingForInputs, line.ActiveJob!.Status);
    }

    [Fact]
    public void DisabledCompletedOutputWaitsWithoutPublishingUntilProviderReturns()
    {
        var inventory = new Inventory(new(1), new(2));
        inventory.Add(Input, new(1));
        var ids = new ProductionIdSequences();
        var line = new ProductionLine(new(1), inventory.Id, new(1));
        line.Enqueue(ids, new([new(Input, new(1))], Output, new(2), new(1)), false);
        line.PrepareActive(new(), inventory, SimulationTime.Zero);
        inventory.Add(Input, new(2));
        Assert.False(line.CompleteActive(ids, inventory, new(1_000)));
        line.ApplyProviderTransition(Assert.IsType<PreparedProductionProviderTransition>(
            line.PrepareProviderTransition(false, new(1), new(1_000))));
        inventory.RemoveAvailable(Input, new(2));

        Assert.False(line.CompleteActive(ids, inventory, new(2_000)));
        Assert.Equal(Quantity.Zero, inventory.Stored(Output));
        line.ApplyProviderTransition(Assert.IsType<PreparedProductionProviderTransition>(
            line.PrepareProviderTransition(true, new(1), new(2_000))));
        Assert.True(line.CompleteActive(ids, inventory, new(2_000)));
        Assert.Equal(new Quantity(2), inventory.Stored(Output));
    }

    [Fact]
    public void ProductionPausedWorkAndAvailabilitySurviveDirectCheckpointRestore()
    {
        var inventory = new Inventory(new(1), new(20));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        var ids = new ProductionIdSequences();
        var line = new ProductionLine(new(1), inventory.Id, new(3));
        line.Enqueue(ids, new([], Output, new(1), new(7)), false);
        line.PrepareActive(new(), inventory, SimulationTime.Zero);
        line.ApplyProviderTransition(Assert.IsType<PreparedProductionProviderTransition>(line.PrepareProviderTransition(false, new(3), new(101))));
        CheckpointResult<RestoredProductionOwner> restore = ProductionCheckpointRestore.Restore(
            new(ids.CaptureCheckpoint(), [line.CaptureCheckpoint()]), inventories);

        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        ProductionLine restored = restore.Value!.Lines[new(1)];
        Assert.False(restored.IsProviderAvailable);
        ProductionCompletionProposal completion = Assert.IsType<ProductionCompletionProposal>(restored.ApplyProviderTransition(
            Assert.IsType<PreparedProductionProviderTransition>(restored.PrepareProviderTransition(true, new(5), new(1_000)))));
        Assert.Equal(new SimulationTime(2_340), completion.Timestamp);
    }

    [Fact]
    public void ConstructionPausedWorkAndAvailabilitySurviveDirectCheckpointRestore()
    {
        var inventory = new Inventory(new(1), new(20));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        var ids = new ConstructionIdSequences();
        var process = new ConstructionProcess(new(1), inventory.Id, new(3));
        var design = new ShipDesign(new(1), "Provider Ship", new([], new(10)),
            new(5), GameSessionTestFixture.ManeuverCapability, 0, 0);
        var designs = new ConstructionDesignCatalog();
        designs.Add(design);
        process.Enqueue(ids, design);
        process.PrepareActive(new(), inventory, SimulationTime.Zero);
        process.ApplyProviderTransition(Assert.IsType<PreparedConstructionProviderTransition>(process.PrepareProviderTransition(false, new(3), new(501))));
        CheckpointResult<RestoredConstructionOwner> restore = ConstructionCheckpointRestore.Restore(
            new(ids.CaptureCheckpoint(), [process.CaptureCheckpoint()]), inventories, designs);

        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        ConstructionProcess restored = restore.Value!.Processes[new(1)];
        Assert.False(restored.IsProviderAvailable);
        ConstructionCompletionProposal completion = Assert.IsType<ConstructionCompletionProposal>(restored.ApplyProviderTransition(
            Assert.IsType<PreparedConstructionProviderTransition>(restored.PrepareProviderTransition(true, new(7), new(2_000)))));
        Assert.Equal(new SimulationTime(3_214), completion.Timestamp);
    }
}
