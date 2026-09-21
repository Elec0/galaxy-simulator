using GalaxyCommand.Simulation;

using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void MappedLegacyReservationRoundTripsAgainstGeneralizedHolding()
    {
        MaterialId oreId = new(1);
        PhysicalDefinition ore = new(
            QualifiedContentKey.Create("core", "cargo", "ore"),
            PhysicalHoldingKind.Fungible,
            new Quantity(1));
        var compatibility = new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(oreId, ore),
        ]);
        var inventory = new Inventory(
            new InventoryId(1),
            Custody(17),
            new Quantity(10),
            compatibility);
        inventory.Add(oreId, new Quantity(6));
        inventory.Reserve(
            new ReservationId(1),
            oreId,
            new Quantity(2),
            new ReservationOwner.ProductionJob(new ProductionJobId(1)));

        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(
            inventory.CaptureCheckpoint(),
            new PhysicalDefinitionCatalog([ore]),
            compatibility);

        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(new Quantity(6), restored.Value!.Stored(oreId));
        Assert.Equal(new Quantity(2), restored.Value.Reserved(oreId));
    }

    [Fact]
    public void MappedLegacyFacadePreservesReservationConsumptionAndTransferBehavior()
    {
        MaterialId oreId = new(1);
        PhysicalDefinition ore = new(
            QualifiedContentKey.Create("core", "cargo", "ore"),
            PhysicalHoldingKind.Fungible,
            new Quantity(1));
        var compatibility = new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(oreId, ore),
        ]);
        var source = new Inventory(
            new InventoryId(1),
            Custody(17),
            new Quantity(10),
            compatibility);
        var destination = new Inventory(
            new InventoryId(2),
            Custody(18),
            new Quantity(10),
            compatibility);
        var inventories = new InventoryRegistry();
        inventories.Add(source);
        inventories.Add(destination);
        source.Add(oreId, new Quantity(6));
        Reservation reservation = source.Reserve(
            new ReservationId(1),
            oreId,
            new Quantity(2),
            new ReservationOwner.ProductionJob(new ProductionJobId(1)));

        source.ConsumeReservations([reservation.Id], reservation.Owner);
        inventories.TransferAvailable(
            source.Id,
            destination.Id,
            oreId,
            new Quantity(3));

        Assert.Equal<ulong>(1, source.Stored(oreId).Units);
        Assert.Equal<ulong>(1, source.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(3, destination.Stored(oreId).Units);
        Assert.Equal<ulong>(3, destination.FungibleStored(ore.Key).Units);
        Assert.Equal(Quantity.Zero, source.Reserved(oreId));
        Assert.Equal(Quantity.Zero, source.Stored(new MaterialId(99)));
        Assert.Empty(source.CaptureCheckpoint().StoredMaterials);
    }
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

    [Fact]
    public void CheckpointRestoresStoredMaterialAndCommitmentsDirectly()
    {
        var inventory = new Inventory(new InventoryId(7), new Quantity(20));
        var firstMaterial = new MaterialId(1);
        var secondMaterial = new MaterialId(2);
        var materialOwner = new ReservationOwner.ProductionJob(
            new ProductionJobId(3));
        var capacityOwner = new ReservationOwner.TransportJob(
            new TransportJobId(4));
        inventory.Add(secondMaterial, new Quantity(3));
        inventory.Add(firstMaterial, new Quantity(8));
        inventory.Reserve(
            new ReservationId(5),
            firstMaterial,
            new Quantity(2),
            materialOwner);
        inventory.ReserveCapacity(
            new CapacityReservationId(6),
            new Quantity(4),
            capacityOwner);

        InventoryCheckpoint checkpoint = inventory.CaptureCheckpoint();
        CheckpointResult<Inventory> restoration =
            Inventory.RestoreCheckpoint(checkpoint);

        Assert.True(restoration.IsSuccess);
        Inventory restored = restoration.Value!;
        Assert.Equal(inventory.Id, restored.Id);
        Assert.Equal(inventory.Capacity, restored.Capacity);
        Assert.Equal(inventory.TotalStored, restored.TotalStored);
        Assert.Equal(inventory.ReservedCapacity, restored.ReservedCapacity);
        Assert.Equal(inventory.RemainingCapacity, restored.RemainingCapacity);
        Assert.Equal(inventory.Stored(firstMaterial), restored.Stored(firstMaterial));
        Assert.Equal(inventory.Stored(secondMaterial), restored.Stored(secondMaterial));
        Assert.Equal(inventory.Reserved(firstMaterial), restored.Reserved(firstMaterial));
        Assert.Equal(
            inventory.GetReservation(new ReservationId(5)),
            restored.GetReservation(new ReservationId(5)));
        Assert.Equal(
            inventory.GetCapacityReservation(new CapacityReservationId(6)),
            restored.GetCapacityReservation(new CapacityReservationId(6)));

        Assert.Equal(
            inventory.Release(new ReservationId(5)),
            restored.Release(new ReservationId(5)));
        Assert.Equal(
            inventory.ReleaseCapacity(new CapacityReservationId(6)),
            restored.ReleaseCapacity(new CapacityReservationId(6)));
        Assert.Equal(inventory.RemainingCapacity, restored.RemainingCapacity);
    }

    [Fact]
    public void RestoreAcceptsEditedUnorderedMaterialsAndCanonicalizesCapture()
    {
        var checkpoint = new InventoryCheckpoint(
            new InventoryId(1),
            new Quantity(10),
            [
                new InventoryMaterialCheckpoint(
                    new MaterialId(2),
                    new Quantity(3)),
                new InventoryMaterialCheckpoint(
                    new MaterialId(1),
                    new Quantity(4)),
            ],
            Array.Empty<Reservation>(),
            Array.Empty<CapacityReservation>());

        CheckpointResult<Inventory> restoration =
            Inventory.RestoreCheckpoint(checkpoint);

        Assert.True(restoration.IsSuccess);
        Assert.Equal(
            [new MaterialId(1), new MaterialId(2)],
            restoration.Value!.CaptureCheckpoint().StoredMaterials
                .Select(material => material.MaterialId));
    }

    [Fact]
    public void RestoreRejectsStoredMaterialBeyondCapacity()
    {
        var checkpoint = new InventoryCheckpoint(
            new InventoryId(1),
            new Quantity(4),
            [
                new InventoryMaterialCheckpoint(
                    new MaterialId(1),
                    new Quantity(5)),
            ],
            Array.Empty<Reservation>(),
            Array.Empty<CapacityReservation>());

        CheckpointResult<Inventory> restoration =
            Inventory.RestoreCheckpoint(checkpoint);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.storedMaterials",
            restoration.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsReservationsBeyondStoredMaterial()
    {
        var inventoryId = new InventoryId(1);
        var materialId = new MaterialId(1);
        var checkpoint = new InventoryCheckpoint(
            inventoryId,
            new Quantity(10),
            [new InventoryMaterialCheckpoint(materialId, new Quantity(3))],
            [
                new Reservation(
                    new ReservationId(1),
                    inventoryId,
                    materialId,
                    new Quantity(4),
                    new ReservationOwner.ProductionJob(
                        new ProductionJobId(1))),
            ],
            Array.Empty<CapacityReservation>());

        CheckpointResult<Inventory> restoration =
            Inventory.RestoreCheckpoint(checkpoint);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.reservations[0]",
            restoration.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsStoredMaterialAndReservedCapacityBeyondCapacity()
    {
        var inventoryId = new InventoryId(1);
        var checkpoint = new InventoryCheckpoint(
            inventoryId,
            new Quantity(10),
            [
                new InventoryMaterialCheckpoint(
                    new MaterialId(1),
                    new Quantity(7)),
            ],
            Array.Empty<Reservation>(),
            [
                new CapacityReservation(
                    new CapacityReservationId(1),
                    inventoryId,
                    new Quantity(4),
                    new ReservationOwner.TransportJob(new TransportJobId(1))),
            ]);

        CheckpointResult<Inventory> restoration =
            Inventory.RestoreCheckpoint(checkpoint);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.capacityReservations",
            restoration.Failure!.Path);
    }

    [Fact]
    public void RegistryCheckpointRestoresInventoriesInStableIdentityOrder()
    {
        var registry = new InventoryRegistry();
        registry.Add(new Inventory(new InventoryId(2), new Quantity(20)));
        registry.Add(new Inventory(new InventoryId(1), new Quantity(10)));

        InventoryRegistryCheckpoint checkpoint = registry.CaptureCheckpoint();
        CheckpointResult<InventoryRegistry> restoration =
            InventoryRegistry.RestoreCheckpoint(checkpoint);

        Assert.Equal(
            [new InventoryId(1), new InventoryId(2)],
            checkpoint.Inventories.Select(inventory => inventory.Id));
        Assert.True(restoration.IsSuccess);
        Assert.NotNull(restoration.Value!.Get(new InventoryId(1)));
        Assert.NotNull(restoration.Value.Get(new InventoryId(2)));
    }

    [Fact]
    public void RegistryRestoreRejectsDuplicateInventoryIdentity()
    {
        var inventory = new InventoryCheckpoint(
            new InventoryId(1),
            new Quantity(10),
            Array.Empty<InventoryMaterialCheckpoint>(),
            Array.Empty<Reservation>(),
            Array.Empty<CapacityReservation>());
        var checkpoint = new InventoryRegistryCheckpoint([inventory, inventory]);

        CheckpointResult<InventoryRegistry> restoration =
            InventoryRegistry.RestoreCheckpoint(checkpoint);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories[1].id",
            restoration.Failure!.Path);
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

    private static InventoryCustody Custody(ulong entityId) =>
        new(
            new InventoryOwnerReference.SessionEntity(new EntityId(entityId)),
            new PrincipalId(1));

    private sealed record InventoryFixture(
        Inventory Inventory,
        MaterialId Material,
        MaterialId OtherMaterial,
        IdSequence<ProductionJobId> JobIds,
        IdSequence<ReservationId> ReservationIds);
}
