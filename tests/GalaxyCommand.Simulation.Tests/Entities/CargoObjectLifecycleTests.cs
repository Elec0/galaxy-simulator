using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoObjectLifecycleTests
{
    private static readonly PhysicalDefinition Ore = new(
        QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinition Sensor = new(
        QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(3));
    private static readonly SystemPosition Position = new(new(1), new(new(30), new(40)));

    [Fact]
    public void PreparedPublicationDoesNotAllocateUntilCommitAndRegistersStationaryObjects()
    {
        var owner = Create();
        Assert.True(owner.TryPrepareCargoObjects([
            new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7))),
            new(Position, Sensor, new PhysicalTransferSubject.Discrete(new(99)))], out var batch));
        Assert.Empty(owner.CaptureCargoObjects());
        Assert.Equal<ulong?>(1, owner.CaptureCheckpoint().EntityIds.NextValue);
        Assert.Equal<ulong?>(1, owner.CaptureCheckpoint().InventoryIds.NextValue);
        Assert.True(owner.CanCommitCargoObjects(batch!));
        owner.ApplyPreparedCargoObjects(batch!);
        Assert.Equal(2, owner.Entities.Count);
        Assert.All(owner.CaptureCargoObjects(), cargo => Assert.Equal(EntityKind.CargoObject, owner.Entities.GetKind(cargo.EntityId)));
        Assert.Equal(new Quantity(7), owner.Inventories.Get(new(1))!.FungibleStored(Ore.Key));
        Assert.Equal(new ItemInstanceId(99), owner.Inventories.Get(new(2))!.GetDiscrete(new(99))!.Id);
        Assert.Equal<ulong?>(3, owner.CaptureCheckpoint().EntityIds.NextValue);
        Assert.Equal<ulong?>(3, owner.CaptureCheckpoint().InventoryIds.NextValue);
        Assert.All(owner.CaptureCargoObjects(), cargo => Assert.Equal(Position, cargo.Position));
    }

    [Fact]
    public void StalePreparationRejectsBeforePublishingAnyAdditionalObject()
    {
        var owner = Create();
        CargoObjectSeed[] seeds = [new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7)))];
        Assert.True(owner.TryPrepareCargoObjects(seeds, out var first));
        Assert.True(owner.TryPrepareCargoObjects(seeds, out var stale));
        owner.ApplyPreparedCargoObjects(first!);
        Assert.False(owner.CanCommitCargoObjects(stale!));
        Assert.Throws<InvalidOperationException>(() => owner.ApplyPreparedCargoObjects(stale!));
        Assert.Single(owner.CaptureCargoObjects());
    }

    [Fact]
    public void InvalidPreparedContentsCannotAllocateOrPublish()
    {
        var owner = Create();
        Assert.False(owner.TryPrepareCargoObjects([
            new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(ulong.MaxValue)))], out _));
        Assert.False(owner.TryPrepareCargoObjects([
            new(Position, Sensor, new PhysicalTransferSubject.Discrete(new(99))),
            new(Position, Sensor, new PhysicalTransferSubject.Discrete(new(99)))], out _));
        Assert.False(owner.TryPrepareCargoObjects([
            new(new(new(1), new(new(long.MaxValue), new(0))), Ore,
                new PhysicalTransferSubject.Fungible(Ore.Key, new(1)))], out _));
        Assert.Empty(owner.CaptureCargoObjects());
        Assert.Equal<ulong?>(1, owner.CaptureCheckpoint().EntityIds.NextValue);
    }

    [Fact]
    public void LifecycleRestoreRetainsObjectsCustodyPositionsAndAllocatorContinuity()
    {
        var owner = Create();
        Assert.True(owner.TryPrepareCargoObjects([
            new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7)))], out var batch));
        owner.ApplyPreparedCargoObjects(batch!);
        EntityLifecycleCheckpoint checkpoint = owner.CaptureCheckpoint();
        var result = Restore(checkpoint);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(owner.CaptureCargoObjects(), result.Value!.CaptureCargoObjects());
        Assert.IsType<InventoryCustody.Unclaimed>(result.Value.Inventories.Get(new(1))!.Custody);
        Assert.Equal(checkpoint.EntityIds, result.Value.CaptureCheckpoint().EntityIds);
        Assert.True(result.Value.TryPrepareCargoObjects([
            new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(1)))], out var next));
        Assert.Equal(new EntityId(2), next!.Objects[0].Object.EntityId);
    }

    [Fact]
    public void LifecycleRestoreRejectsUnclaimedInventoryWithoutCargoEntity()
    {
        Inventory inventory = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        var checkpoint = new EntityLifecycleCheckpoint(new(10), new(1), new(2),
            new([inventory.CaptureCheckpoint()]), [], [], []);
        Assert.False(Restore(checkpoint).IsSuccess);
    }

    [Fact]
    public void EconomyRegistrationCannotAdmitUnregisteredUnclaimedCustody()
    {
        var owner = Create();
        Inventory inventory = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(7)));
        Assert.Throws<ArgumentException>(() => owner.RegisterEconomyInventory(inventory));
        Assert.Null(owner.Inventories.Get(inventory.Id));
        Assert.Equal<ulong?>(1, owner.CaptureCheckpoint().InventoryIds.NextValue);
    }

    private static EntityLifecycleOwner Create() => new(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);

    private static CheckpointResult<EntityLifecycleOwner> Restore(EntityLifecycleCheckpoint checkpoint) =>
        EntityLifecycleOwner.RestoreCheckpoint(checkpoint, new SpatialMovement(), new ActorControlRegistry(),
            new ShipOrderCoordinator(), [], new PhysicalDefinitionCatalog([Ore, Sensor]));
}
