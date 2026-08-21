using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryRemovalTests
{
    [Fact]
    public void DestroyDispositionRemovesInventoryAndAllContents()
    {
        RemovalFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition fungible = FungibleDefinition("ore", 2);
        PhysicalDefinition discrete = DiscreteDefinition("sensor", 3);
        Assert.True(fixture.Source.StoreFungible(
            fungible,
            new Quantity(2)).IsAccepted);
        Assert.True(fixture.Source.StoreDiscrete(
            discrete,
            new ItemInstanceId(7)).IsAccepted);

        InventoryRemovalResult result = fixture.Registry.RemovePhysicalInventory(
            new InventoryRemovalRequest(
                fixture.Source.Id,
                new InventoryRemovalDisposition.DestroyContents()));

        Assert.True(result.IsAccepted);
        Assert.Null(fixture.Registry.Get(fixture.Source.Id));
        Assert.Equal(Quantity.Zero, fixture.Source.UsedCapacity);
        Assert.Equal(Quantity.Zero, fixture.Source.FungibleStored(fungible.Key));
        Assert.Null(fixture.Source.GetDiscrete(new ItemInstanceId(7)));
    }

    [Fact]
    public void RemovalRejectsLiveCommitmentsWithoutMutation()
    {
        RemovalFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(2)).IsAccepted);
        Assert.True(fixture.Source.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Fungible(
                definition.Key,
                new Quantity(1)),
            Owner(3)).IsAccepted);

        InventoryRemovalResult result = fixture.Registry.RemovePhysicalInventory(
            new InventoryRemovalRequest(
                fixture.Source.Id,
                new InventoryRemovalDisposition.DestroyContents()));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryRemovalRejectionReason.InventoryHasCommitments,
            result.RejectionReason);
        Assert.Same(fixture.Source, fixture.Registry.Get(fixture.Source.Id));
        Assert.Equal<ulong>(2, fixture.Source.FungibleStored(definition.Key).Units);
    }

    [Fact]
    public void TransferDispositionMovesAllLegacyAndPhysicalContents()
    {
        RemovalFixture fixture = CreateFixture(30, 30);
        MaterialId materialId = new(1);
        PhysicalDefinition fungible = FungibleDefinition("ore", 2);
        PhysicalDefinition discrete = DiscreteDefinition("sensor", 3);
        var instanceId = new ItemInstanceId(7);
        fixture.Source.Add(materialId, new Quantity(4));
        Assert.True(fixture.Source.StoreFungible(
            fungible,
            new Quantity(2)).IsAccepted);
        Assert.True(fixture.Source.StoreDiscrete(discrete, instanceId).IsAccepted);

        InventoryRemovalResult result = fixture.Registry.RemovePhysicalInventory(
            new InventoryRemovalRequest(
                fixture.Source.Id,
                new InventoryRemovalDisposition.TransferContents(
                    fixture.Destination.Id)));

        Assert.True(result.IsAccepted);
        Assert.Null(fixture.Registry.Get(fixture.Source.Id));
        Assert.Equal<ulong>(4, fixture.Destination.Stored(materialId).Units);
        Assert.Equal<ulong>(2, fixture.Destination.FungibleStored(fungible.Key).Units);
        Assert.NotNull(fixture.Destination.GetDiscrete(instanceId));
        Assert.Equal<ulong>(11, fixture.Destination.UsedCapacity.Units);
        Assert.Equal(Quantity.Zero, fixture.Source.UsedCapacity);
    }

    [Fact]
    public void InsufficientDestinationCapacityRejectsWithoutMutation()
    {
        RemovalFixture fixture = CreateFixture(20, 5);
        PhysicalDefinition definition = FungibleDefinition("ore", 2);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(3)).IsAccepted);

        InventoryRemovalResult result = fixture.Registry.RemovePhysicalInventory(
            new InventoryRemovalRequest(
                fixture.Source.Id,
                new InventoryRemovalDisposition.TransferContents(
                    fixture.Destination.Id)));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryRemovalRejectionReason.InsufficientCapacity,
            result.RejectionReason);
        Assert.Same(fixture.Source, fixture.Registry.Get(fixture.Source.Id));
        Assert.Equal<ulong>(3, fixture.Source.FungibleStored(definition.Key).Units);
        Assert.Equal(Quantity.Zero, fixture.Destination.UsedCapacity);
    }

    [Fact]
    public void DestinationInstanceConflictRejectsWithoutMutation()
    {
        RemovalFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = DiscreteDefinition("sensor", 2);
        var instanceId = new ItemInstanceId(7);
        Assert.True(fixture.Source.StoreDiscrete(definition, instanceId).IsAccepted);
        Assert.True(fixture.Destination.StoreDiscrete(definition, instanceId).IsAccepted);

        InventoryRemovalResult result = fixture.Registry.RemovePhysicalInventory(
            new InventoryRemovalRequest(
                fixture.Source.Id,
                new InventoryRemovalDisposition.TransferContents(
                    fixture.Destination.Id)));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryRemovalRejectionReason.DestinationItemConflict,
            result.RejectionReason);
        Assert.Same(fixture.Source, fixture.Registry.Get(fixture.Source.Id));
        Assert.NotNull(fixture.Source.GetDiscrete(instanceId));
        Assert.NotNull(fixture.Destination.GetDiscrete(instanceId));
    }

    [Fact]
    public void MissingTransferDestinationRejectsWithoutMutation()
    {
        RemovalFixture fixture = CreateFixture(20, 20);

        InventoryRemovalResult result = fixture.Registry.RemovePhysicalInventory(
            new InventoryRemovalRequest(
                fixture.Source.Id,
                new InventoryRemovalDisposition.TransferContents(
                    new InventoryId(99))));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryRemovalRejectionReason.MissingDestinationInventory,
            result.RejectionReason);
        Assert.Same(fixture.Source, fixture.Registry.Get(fixture.Source.Id));
    }

    private static RemovalFixture CreateFixture(
        ulong sourceCapacity,
        ulong destinationCapacity)
    {
        var registry = new InventoryRegistry();
        Inventory source = CreateInventory(1, 17, sourceCapacity);
        Inventory destination = CreateInventory(2, 18, destinationCapacity);
        registry.Add(source);
        registry.Add(destination);
        return new RemovalFixture(registry, source, destination);
    }

    private static Inventory CreateInventory(
        ulong inventoryId,
        ulong entityId,
        ulong capacity) =>
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

    private sealed record RemovalFixture(
        InventoryRegistry Registry,
        Inventory Source,
        Inventory Destination);
}
