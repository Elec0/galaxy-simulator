using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ProductionCheckpointTests
{
    [Fact]
    public void RestorePreservesActiveQueueReservationsAndAllocatorPosition()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId first = fixture.Line.Enqueue(fixture.Ids, Recipe(), repeat: false);
        ProductionJobId second = fixture.Line.Enqueue(fixture.Ids, Recipe(), repeat: true);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(2));
        Assert.Null(fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));

        ProductionOwnerCheckpoint checkpoint = new(
            fixture.Ids.CaptureCheckpoint(),
            [fixture.Line.CaptureCheckpoint()]);
        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(checkpoint, fixture.Inventories);

        Assert.True(result.IsSuccess);
        RestoredProductionOwner restored = Assert.IsType<RestoredProductionOwner>(result.Value);
        ProductionLine line = Assert.Single(restored.Lines.Values);
        Assert.Equal(first, line.ActiveJob!.Id);
        Assert.Equal(1, line.QueuedJobCount);
        Assert.Equal(new Quantity(2), line.ActiveJob.ReservedInput(
            fixture.Inventory,
            new MaterialId(1)));
        Assert.Equal(second, line.GetJob(second)!.Id);
        Assert.Equal(new ProductionJobId(3), restored.Ids.AllocateJob());
    }

    [Fact]
    public void RestorePreservesRunningCompletionIdentity()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId jobId = fixture.Line.Enqueue(fixture.Ids, Recipe(), repeat: false);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(4));
        SimulationTime completesAt = Assert.IsType<SimulationTime>(fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            new SimulationTime(100)));

        RestoredProductionOwner restored = Restore(fixture);
        ProductionLine line = Assert.Single(restored.Lines.Values);
        bool outputStored;
        ScheduledEventDisposition disposition = line.CompleteScheduled(
            jobId,
            new EventGeneration(0),
            restored.Ids,
            fixture.Inventory,
            completesAt,
            out outputStored);

        Assert.Equal(ScheduledEventDisposition.Applied, disposition);
        Assert.True(outputStored);
        Assert.Equal(new Quantity(2), fixture.Inventory.Stored(new MaterialId(2)));
    }

    [Fact]
    public void RestoreAcceptsUnorderedLinesAndJobsAndCanonicalizesCapture()
    {
        Fixture first = CreateFixture(facilityId: 2, inventoryId: 2);
        Fixture second = CreateFixture(facilityId: 1, inventoryId: 1);
        first.Line.Enqueue(first.Ids, Recipe(), false);
        ProductionJobId secondFirst = second.Line.Enqueue(first.Ids, Recipe(), false);
        ProductionJobId secondQueued = second.Line.Enqueue(first.Ids, Recipe(), false);
        ProductionLineCheckpoint secondCheckpoint = second.Line.CaptureCheckpoint();
        var reorderedSecond = new ProductionLineCheckpoint(
            secondCheckpoint.FacilityId,
            secondCheckpoint.InventoryId,
            secondCheckpoint.Throughput,
            secondCheckpoint.ActiveJobId,
            secondCheckpoint.QueuedJobIds,
            secondCheckpoint.Jobs.Reverse().ToArray());
        var checkpoint = new ProductionOwnerCheckpoint(
            first.Ids.CaptureCheckpoint(),
            [first.Line.CaptureCheckpoint(), reorderedSecond]);
        var inventories = new InventoryRegistry();
        inventories.Add(second.Inventory);
        inventories.Add(first.Inventory);

        RestoredProductionOwner restored = Assert.IsType<RestoredProductionOwner>(
            ProductionCheckpointRestore.Restore(checkpoint, inventories).Value);
        ProductionOwnerCheckpoint recaptured = restored.CaptureCheckpoint();

        Assert.Equal([1UL, 2UL], recaptured.Lines.Select(line => line!.FacilityId.Value));
        ProductionLineCheckpoint line = recaptured.Lines[0]!;
        Assert.Equal([secondFirst.Value, secondQueued.Value],
            line.Jobs.Select(job => job!.Id.Value));
    }

    [Fact]
    public void RestoreRejectsJobAtOrBeyondAllocatorPosition()
    {
        Fixture fixture = CreateFixture();
        fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        ProductionOwnerCheckpoint checkpoint = fixture.Checkpoint() with
        {
            JobIds = new IdSequenceCheckpoint(1),
        };

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(checkpoint, fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].jobs[0].id", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsQueuedJobMissingFromRegistry()
    {
        Fixture fixture = CreateFixture();
        fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        ProductionOwnerCheckpoint checkpoint = fixture.Checkpoint();
        ProductionLineCheckpoint line = checkpoint.Lines[0]!;
        var corruptLine = new ProductionLineCheckpoint(
            line.FacilityId,
            line.InventoryId,
            line.Throughput,
            line.ActiveJobId,
            [new ProductionJobId(99)],
            line.Jobs);

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(
                checkpoint with { Lines = [corruptLine] },
                fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].queuedJobIds[0]", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsRunningJobWithoutCompletionTime()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId id = fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        ProductionLineCheckpoint line = fixture.Line.CaptureCheckpoint();
        ProductionJobCheckpoint job = line.Jobs[0]! with
        {
            Status = ProductionJobStatus.Running,
            CompletesAt = null,
        };
        var corruptLine = Copy(line, jobs: [job], activeJobId: id);

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(
                new ProductionOwnerCheckpoint(fixture.Ids.CaptureCheckpoint(), [corruptLine]),
                fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].jobs[0].completesAt", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsReservationOwnedByDifferentJob()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId id = fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(2));
        Assert.Null(fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));
        ProductionLineCheckpoint line = fixture.Line.CaptureCheckpoint();
        ProductionJobCheckpoint job = line.Jobs[0]!;
        ProductionReservationLinkCheckpoint link = Assert.Single(job.Reservations)!;
        var corruptJob = job with
        {
            Id = new ProductionJobId(2),
            Reservations = [link],
        };

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(
                new ProductionOwnerCheckpoint(
                    new IdSequenceCheckpoint(3),
                    [Copy(line, jobs: [corruptJob], activeJobId: corruptJob.Id)]),
                fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].jobs[0].reservations[0]", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsTerminalJobAsActive()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId id = fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        ProductionLineCheckpoint line = fixture.Line.CaptureCheckpoint();
        ProductionJobCheckpoint job = line.Jobs[0]! with
        {
            Status = ProductionJobStatus.Cancelled,
        };

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(
                new ProductionOwnerCheckpoint(
                    fixture.Ids.CaptureCheckpoint(),
                    [Copy(line, jobs: [job], activeJobId: id)]),
                fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].activeJobId", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsProductionReservationOmittedFromJobLinks()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId id = fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(2));
        fixture.Inventory.Reserve(
            new ReservationId(1),
            new MaterialId(1),
            new Quantity(2),
            new ReservationOwner.ProductionJob(id));

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(fixture.Checkpoint(), fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].jobs", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsQueueWithoutActiveJob()
    {
        Fixture fixture = CreateFixture();
        ProductionJobId first = fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        ProductionJobId second = fixture.Line.Enqueue(fixture.Ids, Recipe(), false);
        ProductionLineCheckpoint line = fixture.Line.CaptureCheckpoint();
        var corrupt = new ProductionLineCheckpoint(
            line.FacilityId,
            line.InventoryId,
            line.Throughput,
            ActiveJobId: null,
            [first, second],
            line.Jobs);

        CheckpointResult<RestoredProductionOwner> result =
            ProductionCheckpointRestore.Restore(
                new ProductionOwnerCheckpoint(fixture.Ids.CaptureCheckpoint(), [corrupt]),
                fixture.Inventories);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.production.lines[0].activeJobId", result.Failure!.Path);
    }

    private static RestoredProductionOwner Restore(Fixture fixture) =>
        Assert.IsType<RestoredProductionOwner>(
            ProductionCheckpointRestore.Restore(fixture.Checkpoint(), fixture.Inventories).Value);

    private static ProductionLineCheckpoint Copy(
        ProductionLineCheckpoint line,
        IEnumerable<ProductionJobCheckpoint?> jobs,
        ProductionJobId? activeJobId) =>
        new(
            line.FacilityId,
            line.InventoryId,
            line.Throughput,
            activeJobId,
            line.QueuedJobIds,
            jobs.ToArray());

    private static Fixture CreateFixture(ulong facilityId = 1, ulong inventoryId = 1)
    {
        var inventory = new Inventory(new InventoryId(inventoryId), new Quantity(20));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        return new Fixture(
            new ProductionLine(
                new FacilityId(facilityId),
                inventory.Id,
                new Throughput(4)),
            inventory,
            inventories,
            new ProductionIdSequences(),
            new IdSequence<ReservationId>());
    }

    private static Recipe Recipe() => new(
        [new KeyValuePair<MaterialId, Quantity>(new MaterialId(1), new Quantity(4))],
        new MaterialId(2),
        new Quantity(2),
        new Work(5));

    private sealed record Fixture(
        ProductionLine Line,
        Inventory Inventory,
        InventoryRegistry Inventories,
        ProductionIdSequences Ids,
        IdSequence<ReservationId> ReservationIds)
    {
        internal ProductionOwnerCheckpoint Checkpoint() =>
            new(Ids.CaptureCheckpoint(), [Line.CaptureCheckpoint()]);
    }
}
