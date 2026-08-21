using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryHoldingTests
{
    [Fact]
    public void FungibleStorageAggregatesByQualifiedKeyAndConsumesDefinitionCapacity()
    {
        Inventory inventory = CreateInventory(20);
        PhysicalDefinition definition = Definition(
            "ore",
            PhysicalHoldingKind.Fungible,
            3);

        InventoryStorageResult first = inventory.StoreFungible(
            definition,
            new Quantity(2));
        InventoryStorageResult second = inventory.StoreFungible(
            definition,
            new Quantity(3));

        Assert.True(first.IsAccepted);
        Assert.True(second.IsAccepted);
        Assert.Equal<ulong>(5, inventory.FungibleStored(definition.Key).Units);
        Assert.Equal<ulong>(15, inventory.UsedCapacity.Units);
        Assert.Equal<ulong>(5, inventory.RemainingCapacity.Units);
    }

    [Fact]
    public void DiscreteStorageRetainsInstanceIdentityAndConsumesOneDefinitionCost()
    {
        Inventory inventory = CreateInventory(10);
        PhysicalDefinition definition = Definition(
            "sensor",
            PhysicalHoldingKind.Discrete,
            4);
        var instanceId = new ItemInstanceId(7);

        InventoryStorageResult result = inventory.StoreDiscrete(
            definition,
            instanceId);

        Assert.True(result.IsAccepted);
        Assert.Equal(
            new DiscreteItemInstance(instanceId, definition.Key),
            inventory.GetDiscrete(instanceId));
        Assert.Equal<ulong>(4, inventory.UsedCapacity.Units);
    }

    [Fact]
    public void StorageRejectsHoldingKindMismatchWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);
        PhysicalDefinition definition = Definition(
            "sensor",
            PhysicalHoldingKind.Discrete,
            1);

        InventoryStorageResult result = inventory.StoreFungible(
            definition,
            new Quantity(1));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.HoldingKindMismatch,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.UsedCapacity);
        Assert.Equal(Quantity.Zero, inventory.FungibleStored(definition.Key));
    }

    [Fact]
    public void FungibleStorageRejectsZeroQuantityWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);
        PhysicalDefinition definition = Definition(
            "ore",
            PhysicalHoldingKind.Fungible,
            1);

        InventoryStorageResult result = inventory.StoreFungible(
            definition,
            Quantity.Zero);

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.InvalidQuantity,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.UsedCapacity);
    }

    [Fact]
    public void PhysicalStorageRequiresCustody()
    {
        var inventory = new Inventory(new InventoryId(2), new Quantity(10));
        PhysicalDefinition definition = Definition(
            "ore",
            PhysicalHoldingKind.Fungible,
            1);

        InventoryStorageResult result = inventory.StoreFungible(
            definition,
            new Quantity(1));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.MissingCustody,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.UsedCapacity);
    }

    [Fact]
    public void LegacyAndPhysicalHoldingsShareOneCapacityLimit()
    {
        Inventory inventory = CreateInventory(10);
        inventory.Add(new MaterialId(1), new Quantity(3));
        PhysicalDefinition definition = Definition(
            "ore",
            PhysicalHoldingKind.Fungible,
            4);

        InventoryStorageResult result = inventory.StoreFungible(
            definition,
            new Quantity(2));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.InsufficientCapacity,
            result.RejectionReason);
        Assert.Equal<ulong>(3, inventory.UsedCapacity.Units);
        Assert.Equal(Quantity.Zero, inventory.FungibleStored(definition.Key));
    }

    [Fact]
    public void CapacityOverflowRejectsWithoutMutation()
    {
        Inventory inventory = CreateInventory(ulong.MaxValue);
        PhysicalDefinition definition = Definition(
            "ore",
            PhysicalHoldingKind.Fungible,
            ulong.MaxValue);

        InventoryStorageResult result = inventory.StoreFungible(
            definition,
            new Quantity(2));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.CapacityOverflow,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.UsedCapacity);
        Assert.Equal(Quantity.Zero, inventory.FungibleStored(definition.Key));
    }

    [Fact]
    public void DuplicateDiscreteIdentityRejectsWithoutAdditionalMutation()
    {
        Inventory inventory = CreateInventory(10);
        PhysicalDefinition definition = Definition(
            "sensor",
            PhysicalHoldingKind.Discrete,
            2);
        var instanceId = new ItemInstanceId(7);
        Assert.True(inventory.StoreDiscrete(definition, instanceId).IsAccepted);

        InventoryStorageResult result = inventory.StoreDiscrete(
            definition,
            instanceId);

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.DuplicateItemInstance,
            result.RejectionReason);
        Assert.Equal<ulong>(2, inventory.UsedCapacity.Units);
    }

    [Fact]
    public void DiscreteStorageRejectsDefaultInstanceIdentity()
    {
        Inventory inventory = CreateInventory(10);
        PhysicalDefinition definition = Definition(
            "sensor",
            PhysicalHoldingKind.Discrete,
            2);

        InventoryStorageResult result = inventory.StoreDiscrete(
            definition,
            default);

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryStorageRejectionReason.InvalidItemInstance,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.UsedCapacity);
    }

    private static Inventory CreateInventory(ulong capacity) =>
        new(
            new InventoryId(2),
            new InventoryCustody(
                new InventoryOwnerReference.SessionEntity(new EntityId(17)),
                new PrincipalId(4)),
            new Quantity(capacity));

    private static PhysicalDefinition Definition(
        string localId,
        PhysicalHoldingKind holdingKind,
        ulong capacityCost) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            holdingKind,
            new Quantity(capacityCost));
}
