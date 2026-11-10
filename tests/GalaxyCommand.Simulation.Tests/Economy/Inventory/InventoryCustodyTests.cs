using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryCustodyTests
{
    [Fact]
    public void UnclaimedCargoRetainsPhysicalEntityWithoutInventingAPrincipal()
    {
        var cargoObjectId = new EntityId(23);
        InventoryCustody custody = new InventoryCustody.Unclaimed(cargoObjectId);

        Assert.IsType<InventoryCustody.Unclaimed>(custody);
        Assert.Equal(cargoObjectId, Assert.IsType<InventoryOwnerReference.SessionEntity>(custody.PhysicalOwner).EntityId);
        Assert.Null(custody.ControllingPrincipalId);
    }

    [Fact]
    public void UnclaimedCargoRejectsDefaultPhysicalIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryCustody.Unclaimed(default));
    }

    [Fact]
    public void PrincipalControlledCustodyRejectsDefaultPrincipalAndMissingOwner()
    {
        var owner = new InventoryOwnerReference.SessionEntity(new EntityId(23));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryCustody.PrincipalControlled(owner, default));
        Assert.Throws<ArgumentNullException>(() => new InventoryCustody.PrincipalControlled(null!, new PrincipalId(4)));
    }

    [Fact]
    public void SessionEntityOwnerAcceptsAnySessionEntityIdentity()
    {
        var entityId = new EntityId(17);

        var custody = new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(entityId),
            new PrincipalId(4));

        var owner = Assert.IsType<InventoryOwnerReference.SessionEntity>(
            custody.PhysicalOwner);
        Assert.Equal(entityId, owner.EntityId);
        Assert.Equal(new PrincipalId(4), custody.ControllingPrincipalId);
    }

    [Fact]
    public void FacilityOwnerRetainsFacilityIdentityOutsideEntityRegistry()
    {
        var facilityId = new FacilityId(8);

        var custody = new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.Facility(facilityId),
            new PrincipalId(4));

        var owner = Assert.IsType<InventoryOwnerReference.Facility>(
            custody.PhysicalOwner);
        Assert.Equal(facilityId, owner.FacilityId);
    }

    [Fact]
    public void DiscreteItemIdentitySupportsSessionScopedAllocation()
    {
        var ids = new IdSequence<ItemInstanceId>();

        ItemInstanceId first = ids.Allocate();
        ItemInstanceId second = ids.Allocate();

        Assert.Equal<ulong>(1, first.Value);
        Assert.Equal<ulong>(2, second.Value);
    }

    [Fact]
    public void CustodyAwareInventoryCheckpointPreservesOwnerAndController()
    {
        var custody = new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new EntityId(17)),
            new PrincipalId(4));
        var inventory = new Inventory(
            new InventoryId(2),
            custody,
            new Quantity(20));

        CheckpointResult<Inventory> restoration = Inventory.RestoreCheckpoint(
            inventory.CaptureCheckpoint());

        Assert.True(restoration.IsSuccess);
        Assert.Equal(custody, restoration.Value!.Custody);
    }

    [Fact]
    public void MaterialCompatibilityInventoryRemainsCustodyFree()
    {
        var inventory = new Inventory(
            new InventoryId(2),
            new Quantity(20));

        CheckpointResult<Inventory> restoration = Inventory.RestoreCheckpoint(
            inventory.CaptureCheckpoint());

        Assert.True(restoration.IsSuccess);
        Assert.Null(restoration.Value!.Custody);
    }
}
