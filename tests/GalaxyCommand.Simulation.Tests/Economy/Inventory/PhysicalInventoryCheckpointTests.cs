using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryCheckpointTests
{
    [Fact]
    public void CheckpointRoundTripsGeneralizedStateInCanonicalOrder()
    {
        PhysicalDefinition water = FungibleDefinition("water", 1);
        PhysicalDefinition ore = FungibleDefinition("ore", 2);
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 3);
        PhysicalDefinitionCatalog catalog = new([water, sensor, ore]);
        Inventory inventory = CreateInventory(100);
        Assert.True(inventory.StoreFungible(water, new Quantity(4)).IsAccepted);
        Assert.True(inventory.StoreFungible(ore, new Quantity(5)).IsAccepted);
        Assert.True(inventory.StoreDiscrete(sensor, new ItemInstanceId(9)).IsAccepted);
        Assert.True(inventory.StoreDiscrete(sensor, new ItemInstanceId(3)).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(9),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(7)),
            Owner(3)).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(2),
            new PhysicalReservationSubject.Discrete(new ItemInstanceId(9)),
            Owner(4)).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(7),
            new PhysicalReservationSubject.Fungible(ore.Key, new Quantity(2)),
            Owner(5)).IsAccepted);

        InventoryCheckpoint checkpoint = inventory.CaptureCheckpoint();
        CheckpointResult<Inventory> restoration = Inventory.RestoreCheckpoint(
            checkpoint,
            catalog);

        Assert.Equal(
            [ore.Key, water.Key],
            checkpoint.FungibleHoldings.Select(holding => holding.DefinitionKey));
        Assert.Equal(
            [new ItemInstanceId(3), new ItemInstanceId(9)],
            checkpoint.DiscreteItems.Select(item => item.Id));
        Assert.Equal(
            [new ReservationId(2), new ReservationId(7), new ReservationId(9)],
            checkpoint.PhysicalReservations.Select(reservation => reservation.Id));
        Assert.Null(typeof(InventoryFungibleCheckpoint).GetProperty("Definition"));
        Assert.True(restoration.IsSuccess);
        Inventory restored = restoration.Value!;
        Assert.Equal(inventory.Custody, restored.Custody);
        Assert.Equal(inventory.UsedCapacity, restored.UsedCapacity);
        Assert.Equal(inventory.ReservedCapacity, restored.ReservedCapacity);
        Assert.Equal(inventory.RemainingCapacity, restored.RemainingCapacity);
        Assert.Equal<ulong>(5, restored.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(2, restored.FungibleReserved(ore.Key).Units);
        Assert.NotNull(restored.GetDiscrete(new ItemInstanceId(3)));
        Assert.True(restored.IsDiscreteReserved(new ItemInstanceId(9)));
        Assert.NotNull(restored.GetPhysicalReservation(new ReservationId(9)));
    }

    [Fact]
    public void GeneralizedRestoreRequiresCompatibleDefinitionCatalog()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 2);
        Inventory inventory = CreateInventory(20);
        Assert.True(inventory.StoreFungible(ore, new Quantity(2)).IsAccepted);
        InventoryCheckpoint checkpoint = inventory.CaptureCheckpoint();

        CheckpointResult<Inventory> missingCatalog =
            Inventory.RestoreCheckpoint(checkpoint);
        CheckpointResult<Inventory> missingDefinition = Inventory.RestoreCheckpoint(
            checkpoint,
            new PhysicalDefinitionCatalog([]));
        CheckpointResult<Inventory> incompatibleDefinition = Inventory.RestoreCheckpoint(
            checkpoint,
            new PhysicalDefinitionCatalog([
                DiscreteDefinition("ore", 2),
            ]));

        Assert.False(missingCatalog.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.fungibleHoldings",
            missingCatalog.Failure!.Path);
        Assert.False(missingDefinition.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.fungibleHoldings[0].definitionKey",
            missingDefinition.Failure!.Path);
        Assert.False(incompatibleDefinition.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.fungibleHoldings[0].definitionKey",
            incompatibleDefinition.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsGeneralizedCapacityOverflowWithoutPublishingState()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 3);
        var checkpoint = new InventoryCheckpoint(
            new InventoryId(1),
            new InventoryCustody(
                new InventoryOwnerReference.SessionEntity(new EntityId(17)),
                new PrincipalId(4)),
            new Quantity(5),
            Array.Empty<InventoryMaterialCheckpoint>(),
            Array.Empty<Reservation>(),
            Array.Empty<CapacityReservation>(),
            [new InventoryFungibleCheckpoint(ore.Key, new Quantity(2))],
            Array.Empty<InventoryDiscreteItemCheckpoint>(),
            Array.Empty<PhysicalReservation>());

        CheckpointResult<Inventory> restoration = Inventory.RestoreCheckpoint(
            checkpoint,
            new PhysicalDefinitionCatalog([ore]));

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.fungibleHoldings[0]",
            restoration.Failure!.Path);
    }

    [Fact]
    public void RegistryRestoreUsesOneCompatibleCatalogForEveryInventory()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 1);
        var registry = new InventoryRegistry();
        Inventory second = CreateInventory(20, 2, 18);
        Inventory first = CreateInventory(20, 1, 17);
        Assert.True(first.StoreFungible(ore, new Quantity(2)).IsAccepted);
        registry.Add(second);
        registry.Add(first);

        CheckpointResult<InventoryRegistry> restoration =
            InventoryRegistry.RestoreCheckpoint(
                registry.CaptureCheckpoint(),
                new PhysicalDefinitionCatalog([ore]));

        Assert.True(restoration.IsSuccess);
        Assert.Equal<ulong>(2, restoration.Value!
            .Get(new InventoryId(1))!
            .FungibleStored(ore.Key).Units);
    }

    [Fact]
    public void RestoreRejectsMalformedPhysicalReservationWithoutThrowing()
    {
        var inventoryId = new InventoryId(1);
        var checkpoint = new InventoryCheckpoint(
            inventoryId,
            new InventoryCustody(
                new InventoryOwnerReference.SessionEntity(new EntityId(17)),
                new PrincipalId(4)),
            new Quantity(10),
            Array.Empty<InventoryMaterialCheckpoint>(),
            Array.Empty<Reservation>(),
            Array.Empty<CapacityReservation>(),
            Array.Empty<InventoryFungibleCheckpoint>(),
            Array.Empty<InventoryDiscreteItemCheckpoint>(),
            [
                new PhysicalReservation(
                    new ReservationId(1),
                    inventoryId,
                    null!,
                    Owner(3)),
            ]);

        CheckpointResult<Inventory> restoration = Inventory.RestoreCheckpoint(
            checkpoint,
            new PhysicalDefinitionCatalog([]));

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventories.physicalReservations[0]",
            restoration.Failure!.Path);
    }

    private static Inventory CreateInventory(
        ulong capacity,
        ulong inventoryId = 1,
        ulong entityId = 17) =>
        new(
            new InventoryId(inventoryId),
            new InventoryCustody(
                new InventoryOwnerReference.SessionEntity(new EntityId(entityId)),
                new PrincipalId(4)),
            new Quantity(capacity));

    private static ReservationOwner.TransportJob Owner(ulong jobId) =>
        new(new TransportJobId(jobId));

    private static PhysicalDefinition FungibleDefinition(
        string localId,
        ulong capacityCost) =>
        Definition(localId, PhysicalHoldingKind.Fungible, capacityCost);

    private static PhysicalDefinition DiscreteDefinition(
        string localId,
        ulong capacityCost) =>
        Definition(localId, PhysicalHoldingKind.Discrete, capacityCost);

    private static PhysicalDefinition Definition(
        string localId,
        PhysicalHoldingKind holdingKind,
        ulong capacityCost) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            holdingKind,
            new Quantity(capacityCost));
}
