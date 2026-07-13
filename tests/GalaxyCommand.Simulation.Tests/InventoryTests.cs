using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void SharedCapacityAppliesAcrossMaterials()
    {
        InventoryFixture fixture = CreateFixture(10);
        fixture.Inventory.Add(fixture.Material, new Quantity(7));

        Assert.Throws<InvalidOperationException>(
            () => fixture.Inventory.Add(fixture.OtherMaterial, new Quantity(4)));
    }

    [Fact]
    public void ReservationReducesAvailabilityWithoutRemovingMaterial()
    {
        InventoryFixture fixture = CreateFixture(10);
        fixture.Inventory.Add(fixture.Material, new Quantity(8));
        ProductionJobId jobId = fixture.JobIds.Allocate();

        fixture.Inventory.Reserve(
            fixture.ReservationIds.Allocate(),
            fixture.Material,
            new Quantity(3),
            new ReservationOwner.ProductionJob(jobId));

        Assert.Equal<ulong>(8, fixture.Inventory.Stored(fixture.Material).Units);
        Assert.Equal<ulong>(3, fixture.Inventory.Reserved(fixture.Material).Units);
        Assert.Equal<ulong>(5, fixture.Inventory.Available(fixture.Material).Units);
    }

    [Fact]
    public void ConsumingReservationsRemovesMaterialAtomically()
    {
        InventoryFixture fixture = CreateFixture(10);
        fixture.Inventory.Add(fixture.Material, new Quantity(8));
        var owner = new ReservationOwner.ProductionJob(fixture.JobIds.Allocate());
        ReservationId first = fixture.ReservationIds.Allocate();
        ReservationId second = fixture.ReservationIds.Allocate();
        fixture.Inventory.Reserve(first, fixture.Material, new Quantity(2), owner);
        fixture.Inventory.Reserve(second, fixture.Material, new Quantity(3), owner);

        fixture.Inventory.ConsumeReservations([first, second], owner);

        Assert.Equal<ulong>(3, fixture.Inventory.Stored(fixture.Material).Units);
        Assert.Equal(Quantity.Zero, fixture.Inventory.Reserved(fixture.Material));
        Assert.Equal<ulong>(3, fixture.Inventory.TotalStored.Units);
    }

    [Fact]
    public void ReservedTransferDoesNotMutateWhenDestinationIsFull()
    {
        var inventoryIds = new IdSequence<InventoryId>();
        var materialIds = new IdSequence<MaterialId>();
        var reservationIds = new IdSequence<ReservationId>();
        var jobIds = new IdSequence<TransportJobId>();
        MaterialId material = materialIds.Allocate();
        var source = new Inventory(inventoryIds.Allocate(), new Quantity(10));
        var destination = new Inventory(inventoryIds.Allocate(), new Quantity(2));
        var owner = new ReservationOwner.TransportJob(jobIds.Allocate());
        source.Add(material, new Quantity(4));
        destination.Add(material, new Quantity(2));
        ReservationId reservationId = reservationIds.Allocate();
        source.Reserve(reservationId, material, new Quantity(4), owner);
        var registry = new InventoryRegistry();
        registry.Add(source);
        registry.Add(destination);

        Assert.Throws<InvalidOperationException>(
            () => registry.TransferReserved(source.Id, destination.Id, reservationId, owner));
        Assert.Equal<ulong>(4, source.Stored(material).Units);
        Assert.NotNull(source.GetReservation(reservationId));
    }

    [Fact]
    public void TransferCanConsumePreviouslyReservedDestinationCapacity()
    {
        var inventoryIds = new IdSequence<InventoryId>();
        var materialIds = new IdSequence<MaterialId>();
        var capacityReservationIds = new IdSequence<CapacityReservationId>();
        var jobIds = new IdSequence<TransportJobId>();
        MaterialId material = materialIds.Allocate();
        var source = new Inventory(inventoryIds.Allocate(), new Quantity(10));
        var destination = new Inventory(inventoryIds.Allocate(), new Quantity(4));
        var owner = new ReservationOwner.TransportJob(jobIds.Allocate());
        source.Add(material, new Quantity(4));
        CapacityReservationId reservationId = capacityReservationIds.Allocate();
        destination.ReserveCapacity(reservationId, new Quantity(4), owner);
        var registry = new InventoryRegistry();
        registry.Add(source);
        registry.Add(destination);

        registry.TransferIntoReservedCapacity(
            source.Id,
            destination.Id,
            material,
            new Quantity(4),
            reservationId,
            owner);

        Assert.Equal(Quantity.Zero, source.Stored(material));
        Assert.Equal<ulong>(4, destination.Stored(material).Units);
        Assert.Equal(Quantity.Zero, destination.ReservedCapacity);
    }

    private static InventoryFixture CreateFixture(ulong capacity)
    {
        var inventoryIds = new IdSequence<InventoryId>();
        var materialIds = new IdSequence<MaterialId>();
        return new InventoryFixture(
            new Inventory(inventoryIds.Allocate(), new Quantity(capacity)),
            materialIds.Allocate(),
            materialIds.Allocate(),
            new IdSequence<ProductionJobId>(),
            new IdSequence<ReservationId>());
    }

    private sealed record InventoryFixture(
        Inventory Inventory,
        MaterialId Material,
        MaterialId OtherMaterial,
        IdSequence<ProductionJobId> JobIds,
        IdSequence<ReservationId> ReservationIds);
}
