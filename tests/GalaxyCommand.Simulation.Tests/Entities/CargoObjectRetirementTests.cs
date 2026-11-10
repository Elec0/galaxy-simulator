using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoObjectRetirementTests
{
    private static readonly PhysicalDefinition Ore = new(
        QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinition Sensor = new(
        QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(3));

    [Fact]
    public void ExpiryPreparationLeavesContentsAndRegistriesUntouchedUntilCommit()
    {
        var owner = Create(Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7)), out CargoObjectSnapshot cargo);
        Inventory original = owner.Inventories.Get(cargo.InventoryId)!;
        EntityLifecycleCheckpoint before = owner.CaptureCheckpoint();
        Assert.True(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Expired, out var prepared));
        Assert.True(owner.CanCommitCargoObjectRetirement(prepared!));
        Assert.Equal(new Quantity(7), original.FungibleStored(Ore.Key));
        Assert.Equal(EntityKind.CargoObject, owner.Entities.GetKind(cargo.EntityId));
        Assert.Single(owner.CaptureCargoObjects());

        owner.ApplyPreparedCargoObjectRetirement(prepared!);

        Assert.Null(owner.Entities.GetKind(cargo.EntityId));
        Assert.Null(owner.Inventories.Get(cargo.InventoryId));
        Assert.Empty(owner.CaptureCargoObjects());
        Assert.Equal(Quantity.Zero, original.UsedCapacity);
        Assert.Equal(before.EntityIds, owner.CaptureCheckpoint().EntityIds);
        Assert.Equal(before.InventoryIds, owner.CaptureCheckpoint().InventoryIds);
        Assert.False(owner.CanCommitCargoObjectRetirement(prepared!));
        Assert.Throws<InvalidOperationException>(() => owner.ApplyPreparedCargoObjectRetirement(prepared!));
    }

    [Fact]
    public void CollectedRetirementRequiresEmptyContentsAndPreservesTransferredOriginalItem()
    {
        var owner = Create(Sensor, new PhysicalTransferSubject.Discrete(new(91)), out CargoObjectSnapshot cargo);
        Inventory destination = Destination(owner);
        Assert.False(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Collected, out _));
        Assert.True(owner.Inventories.TransferPhysical(new(cargo.InventoryId, destination.Id, Sensor,
            new PhysicalTransferSubject.Discrete(new(91)))).IsAccepted);
        Assert.True(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Collected, out var prepared));
        owner.ApplyPreparedCargoObjectRetirement(prepared!);

        Assert.Equal(new ItemInstanceId(91), destination.GetDiscrete(new(91))!.Id);
        Assert.Equal(Sensor.Key, destination.GetDiscrete(new(91))!.DefinitionKey);
        Assert.Null(owner.Inventories.Get(cargo.InventoryId));
        Assert.Empty(owner.CaptureCargoObjects());
        Assert.Null(owner.Entities.GetKind(cargo.EntityId));
    }

    [Fact]
    public void ChangedContentsRejectStaleExpiryBeforeAnyRetirementMutation()
    {
        var owner = Create(Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7)), out CargoObjectSnapshot cargo);
        Inventory destination = Destination(owner);
        Assert.True(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Expired, out var prepared));
        Assert.True(owner.Inventories.TransferPhysical(new(cargo.InventoryId, destination.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(2)))).IsAccepted);

        Assert.False(owner.CanCommitCargoObjectRetirement(prepared!));
        Assert.Throws<InvalidOperationException>(() => owner.ApplyPreparedCargoObjectRetirement(prepared!));
        Assert.Equal(new Quantity(5), owner.Inventories.Get(cargo.InventoryId)!.FungibleStored(Ore.Key));
        Assert.Single(owner.CaptureCargoObjects());
        Assert.Equal(EntityKind.CargoObject, owner.Entities.GetKind(cargo.EntityId));

        Assert.True(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Expired, out var fresh));
        owner.ApplyPreparedCargoObjectRetirement(fresh!);
        Assert.Equal(new Quantity(2), destination.FungibleStored(Ore.Key));
        Assert.Empty(owner.CaptureCargoObjects());
    }

    [Fact]
    public void PreparedRetirementCannotApplyToAnotherOwnerOrAlteredObjectMetadata()
    {
        var owner = Create(Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7)), out CargoObjectSnapshot cargo);
        var other = Create(Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(7)), out _);
        Assert.True(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Expired, out var prepared));
        Assert.False(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, (CargoObjectRetirementKind)999, out _));
        Assert.False(other.CanCommitCargoObjectRetirement(prepared!));
        Assert.Throws<InvalidOperationException>(() => other.ApplyPreparedCargoObjectRetirement(prepared!));
        Assert.False(owner.CanCommitCargoObjectRetirement(prepared! with
        {
            Object = cargo with { Position = new(new(1), new(new(999), new(0))) },
        }));
        Assert.Single(owner.CaptureCargoObjects());
        Assert.Single(other.CaptureCargoObjects());
    }

    [Fact]
    public void MissingUnknownAndNonCargoEntitiesCannotBeRetiredAsCargo()
    {
        var owner = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        owner.RegisterSetup([new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal, GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        Assert.False(owner.TryPrepareCargoObjectRetirement(GameSessionTestFixture.Entity, CargoObjectRetirementKind.Expired, out _));
        Assert.False(owner.TryPrepareCargoObjectRetirement(new(999), CargoObjectRetirementKind.Expired, out _));
        Assert.False(owner.TryPrepareCargoObjectRetirement(default, CargoObjectRetirementKind.Expired, out _));
        Assert.False(owner.TryPrepareCargoObjectRetirement(GameSessionTestFixture.Entity, (CargoObjectRetirementKind)999, out _));
        owner.Entities.ApplyAddSensorDeployable(new(50));
        Assert.False(owner.TryPrepareCargoObjectRetirement(new(50), CargoObjectRetirementKind.Expired, out _));
        Assert.Equal(EntityKind.SensorDeployable, owner.Entities.GetKind(new(50)));
        Assert.Equal(EntityKind.Ship, owner.Entities.GetKind(GameSessionTestFixture.Entity));
        Assert.NotNull(owner.Inventories.Get(GameSessionTestFixture.CargoInventory));
    }

    [Fact]
    public void RetiredObjectsRestoreAbsentAndNextPublicationDoesNotReuseTheirIdentities()
    {
        var owner = Create(Sensor, new PhysicalTransferSubject.Discrete(new(91)), out CargoObjectSnapshot cargo);
        Assert.True(owner.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Expired, out var prepared));
        owner.ApplyPreparedCargoObjectRetirement(prepared!);
        var result = EntityLifecycleOwner.RestoreCheckpoint(owner.CaptureCheckpoint(), new SpatialMovement(),
            new ActorControlRegistry(), new ShipOrderCoordinator(), [], new PhysicalDefinitionCatalog([Ore, Sensor]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        var restored = result.Value!;
        Assert.Null(restored.Entities.GetKind(cargo.EntityId));
        Assert.Empty(restored.CaptureCargoObjects());
        Assert.True(restored.TryPrepareCargoObjects([new(cargo.Position, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(1)))], out var next));
        Assert.Equal(new EntityId(2), next!.Objects[0].Object.EntityId);
        Assert.Equal(new InventoryId(2), next.Objects[0].Object.InventoryId);
    }

    private static EntityLifecycleOwner Create(PhysicalDefinition definition, PhysicalTransferSubject subject, out CargoObjectSnapshot cargo)
    {
        var owner = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        Assert.True(owner.TryPrepareCargoObjects([new(new(new(1), new(new(30), new(40))), definition, subject)], out var batch));
        owner.ApplyPreparedCargoObjects(batch!);
        cargo = Assert.Single(owner.CaptureCargoObjects());
        return owner;
    }

    private static Inventory Destination(EntityLifecycleOwner owner)
    {
        var inventory = new Inventory(new(10), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.Facility(new(1)), new(1)), new(100));
        owner.RegisterEconomyInventory(inventory);
        return inventory;
    }
}
