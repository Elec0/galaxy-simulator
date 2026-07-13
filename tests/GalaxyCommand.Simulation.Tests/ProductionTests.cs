using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ProductionTests
{
    [Fact]
    public void ThroughputRoundsPartialMillisecondsUp()
    {
        var throughput = new Throughput(3);

        SimulationDuration duration = throughput.DurationFor(new Work(1));

        Assert.Equal<ulong>(334, duration.Milliseconds);
    }

    [Fact]
    public void InputsAreReservedIncrementallyThenConsumedAtStart()
    {
        ProductionFixture fixture = CreateFixture(10);
        fixture.Line.Enqueue(fixture.ProductionIds, CreateRecipe(fixture), repeat: false);
        fixture.Inventory.Add(fixture.Input, new Quantity(2));

        SimulationTime? firstAttempt = fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero);

        Assert.Null(firstAttempt);
        Assert.Equal<ulong>(2, fixture.Inventory.Stored(fixture.Input).Units);
        Assert.Equal<ulong>(2, fixture.Inventory.Reserved(fixture.Input).Units);

        fixture.Inventory.Add(fixture.Input, new Quantity(2));
        SimulationTime completion = Assert.IsType<SimulationTime>(fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));

        Assert.Equal<ulong>(1_250, completion.Milliseconds);
        Assert.Equal(Quantity.Zero, fixture.Inventory.Stored(fixture.Input));
        Assert.Equal(ProductionJobStatus.Running, fixture.Line.ActiveJob?.Status);
    }

    [Fact]
    public void CompletedOutputWaitsForStorageCapacity()
    {
        ProductionFixture fixture = CreateFixture(4);
        fixture.Inventory.Add(fixture.Input, new Quantity(4));
        fixture.Line.Enqueue(fixture.ProductionIds, CreateRecipe(fixture), repeat: false);
        SimulationTime completesAt = Assert.IsType<SimulationTime>(fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));
        fixture.Inventory.Add(fixture.Input, new Quantity(4));

        bool stored = fixture.Line.CompleteActive(
            fixture.ProductionIds,
            fixture.Inventory,
            completesAt);

        Assert.False(stored);
        Assert.Equal(
            ProductionJobStatus.CompletedAwaitingStorage,
            fixture.Line.ActiveJob?.Status);
    }

    [Fact]
    public void RepeatingJobRejoinsFifoAfterCompletion()
    {
        ProductionFixture fixture = CreateFixture(10);
        fixture.Inventory.Add(fixture.Input, new Quantity(8));
        fixture.Line.Enqueue(fixture.ProductionIds, CreateRecipe(fixture), repeat: true);
        SimulationTime completesAt = Assert.IsType<SimulationTime>(fixture.Line.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));

        bool stored = fixture.Line.CompleteActive(
            fixture.ProductionIds,
            fixture.Inventory,
            completesAt);

        Assert.True(stored);
        Assert.Equal(ProductionJobStatus.WaitingForInputs, fixture.Line.ActiveJob?.Status);
    }

    private static ProductionFixture CreateFixture(ulong capacity)
    {
        var facilityIds = new IdSequence<FacilityId>();
        var inventoryIds = new IdSequence<InventoryId>();
        var materialIds = new IdSequence<MaterialId>();
        MaterialId input = materialIds.Allocate();
        MaterialId output = materialIds.Allocate();
        var inventory = new Inventory(inventoryIds.Allocate(), new Quantity(capacity));
        return new ProductionFixture(
            new ProductionLine(facilityIds.Allocate(), inventory.Id, new Throughput(4)),
            inventory,
            new ProductionIdSequences(),
            new IdSequence<ReservationId>(),
            input,
            output);
    }

    private static Recipe CreateRecipe(ProductionFixture fixture) =>
        new(
            [new KeyValuePair<MaterialId, Quantity>(fixture.Input, new Quantity(4))],
            fixture.Output,
            new Quantity(2),
            new Work(5));

    private sealed record ProductionFixture(
        ProductionLine Line,
        Inventory Inventory,
        ProductionIdSequences ProductionIds,
        IdSequence<ReservationId> ReservationIds,
        MaterialId Input,
        MaterialId Output);
}
