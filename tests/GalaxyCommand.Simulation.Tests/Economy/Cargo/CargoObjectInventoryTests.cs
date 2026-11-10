using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoObjectInventoryTests
{
    private static readonly PhysicalDefinition Ore = new(
        QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinition Sensor = new(
        QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(3));

    [Fact]
    public void CargoInventoryInitializesExactContentsAndCapacityWithoutAllocatingItems()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        Assert.IsType<InventoryCustody.Unclaimed>(cargo.Custody);
        Assert.Equal(new Quantity(14), cargo.Capacity);
        Assert.Equal(cargo.Capacity, cargo.UsedCapacity);
        Assert.Equal(new Quantity(7), cargo.FungibleStored(Ore.Key));

        Inventory discrete = Inventory.CreateCargoObjectInventory(new(2), new(10), Sensor,
            new PhysicalTransferSubject.Discrete(new(99)));
        Assert.Equal(new Quantity(3), discrete.Capacity);
        Assert.Equal(new ItemInstanceId(99), discrete.GetDiscrete(new(99))!.Id);
    }

    [Fact]
    public void CargoCannotReceiveHoldingsOrAnyGameplayReservations()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        cargo.ApplyRemoveFungible(Ore.Key, new(1), new(2));
        Assert.False(cargo.StoreFungible(Ore, new(1)).IsAccepted);
        Assert.False(cargo.StoreDiscrete(Sensor, new(99)).IsAccepted);
        Assert.False(cargo.TryAdd(new MaterialId(1), new(1)));
        var owner = new ReservationOwner.TransportJob(new(1));
        Assert.False(cargo.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), owner).IsAccepted);
        Assert.False(cargo.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(1)), owner).IsAccepted);
        Assert.Throws<InvalidOperationException>(() => cargo.ReserveCapacity(new(1), new(1), owner));
        Assert.Throws<InvalidOperationException>(() => cargo.Reserve(new(1), new MaterialId(1), new(1), owner));
        Assert.Equal(new Quantity(6), cargo.FungibleStored(Ore.Key));
        Assert.False(cargo.HasCommitments);
    }

    [Fact]
    public void PhysicalTransfersCanCollectOriginalItemButCannotRefillCargo()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Sensor,
            new PhysicalTransferSubject.Discrete(new(99)));
        var ship = new Inventory(new(2), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(10)), new(4)), new(100));
        var registry = new InventoryRegistry();
        registry.Add(cargo);
        registry.Add(ship);
        DiscreteItemInstance original = cargo.GetDiscrete(new(99))!;
        Assert.True(registry.TransferPhysical(new(cargo.Id, ship.Id, Sensor,
            new PhysicalTransferSubject.Discrete(new(99)))).IsAccepted);
        Assert.Same(original, ship.GetDiscrete(new(99)));
        Assert.Equal(new ItemInstanceId(99), ship.GetDiscrete(new(99))!.Id);
        Assert.False(registry.TransferPhysical(new(ship.Id, cargo.Id, Sensor,
            new PhysicalTransferSubject.Discrete(new(99)))).IsAccepted);
        Assert.NotNull(ship.GetDiscrete(new(99)));
    }

    [Fact]
    public void RestorePreservesPartialCargoWithoutAllowingRefill()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        cargo.ApplyRemoveFungible(Ore.Key, new(4), new(8));
        CheckpointResult<Inventory> result = Inventory.RestoreCheckpoint(cargo.CaptureCheckpoint(), new PhysicalDefinitionCatalog([Ore]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(new Quantity(14), result.Value!.Capacity);
        Assert.Equal(new Quantity(3), result.Value.FungibleStored(Ore.Key));
        Assert.False(result.Value.StoreFungible(Ore, new(1)).IsAccepted);
    }

    [Fact]
    public void RestoreRejectsInvalidCargoShapeAndCommitments()
    {
        var custody = new InventoryCustody.Unclaimed(new(9));
        InventoryCheckpoint Checkpoint(IEnumerable<InventoryFungibleCheckpoint> fungible,
            IEnumerable<InventoryDiscreteItemCheckpoint> discrete, IEnumerable<PhysicalReservation>? reservations = null) =>
            new(new(1), custody, new(100), [], [], [], fungible, discrete, reservations ?? []);
        InventoryCheckpoint[] invalid =
        [
            Checkpoint([], []),
            Checkpoint([new(Ore.Key, new(1))], [new(new(99), Sensor.Key)]),
            Checkpoint([], [new(new(99), Sensor.Key), new(new(100), Sensor.Key)]),
            Checkpoint([new(Ore.Key, new(1))], [], [new(new(1), new(1),
                new PhysicalReservationSubject.IncomingCapacity(new(2)), new ReservationOwner.TransportJob(new(1)))]),
        ];
        foreach (InventoryCheckpoint checkpoint in invalid)
            Assert.False(Inventory.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([Ore, Sensor])).IsSuccess);
    }

    [Fact]
    public void GlobalRestoreRejectsDuplicateDiscreteInstanceAcrossInventories()
    {
        Inventory first = Inventory.CreateCargoObjectInventory(new(1), new(9), Sensor,
            new PhysicalTransferSubject.Discrete(new(99)));
        Inventory second = Inventory.CreateCargoObjectInventory(new(2), new(10), Sensor,
            new PhysicalTransferSubject.Discrete(new(99)));
        var checkpoint = new InventoryRegistryCheckpoint([first.CaptureCheckpoint(), second.CaptureCheckpoint()]);
        Assert.False(InventoryRegistry.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([Sensor])).IsSuccess);
    }

    [Fact]
    public void WholeInventoryRemovalCannotDeliverIntoFloatingCargo()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        cargo.ApplyRemoveFungible(Ore.Key, new(5), new(10));
        var source = new Inventory(new(2), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(10)), new(4)), new(100));
        Assert.True(source.StoreDiscrete(Sensor, new(99)).IsAccepted);
        var registry = new InventoryRegistry();
        registry.Add(cargo);
        registry.Add(source);
        Assert.False(registry.RemovePhysicalInventory(new(source.Id,
            new InventoryRemovalDisposition.TransferContents(cargo.Id))).IsAccepted);
        Assert.NotNull(registry.Get(source.Id)!.GetDiscrete(new(99)));
        Assert.Null(cargo.GetDiscrete(new(99)));
    }

    [Fact]
    public void LegacyTransferCannotRemoveSourceStockBeforeRejectingFloatingDestination()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        cargo.ApplyRemoveFungible(Ore.Key, new(5), new(10));
        var source = new Inventory(new(2), new Quantity(100));
        source.Add(new(1), new(3));
        var registry = new InventoryRegistry();
        registry.Add(cargo);
        registry.Add(source);
        Assert.Throws<InvalidOperationException>(() => registry.TransferAvailable(source.Id, cargo.Id, new(1), new(3)));
        Assert.Equal(new Quantity(3), source.Stored(new(1)));
        Assert.Equal(Quantity.Zero, cargo.TotalStored);
    }
}
