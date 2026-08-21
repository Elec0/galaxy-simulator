using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryPresentationTests
{
    [Fact]
    public void SnapshotOrdersInventoriesAndHoldingsByStableIdentity()
    {
        var registry = new InventoryRegistry();
        Inventory second = CreateInventory(2, 18, 30);
        Inventory first = CreateInventory(1, 17, 30);
        PhysicalDefinition water = FungibleDefinition("water", 1);
        PhysicalDefinition ore = FungibleDefinition("ore", 2);
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 3);
        Assert.True(first.StoreFungible(water, new Quantity(4)).IsAccepted);
        Assert.True(first.StoreFungible(ore, new Quantity(5)).IsAccepted);
        Assert.True(first.StoreDiscrete(sensor, new ItemInstanceId(9)).IsAccepted);
        Assert.True(first.StoreDiscrete(sensor, new ItemInstanceId(3)).IsAccepted);
        registry.Add(second);
        registry.Add(first);

        InventoryRegistryPresentationSnapshot snapshot =
            registry.CapturePhysicalPresentationSnapshot();

        Assert.Equal(
            [new InventoryId(1), new InventoryId(2)],
            snapshot.Inventories.Select(inventory => inventory.Id));
        InventoryPresentationSnapshot presented = snapshot.Inventories[0];
        Assert.Equal(
            [ore.Key, water.Key],
            presented.FungibleHoldings.Select(holding => holding.DefinitionKey));
        Assert.Equal(
            [new ItemInstanceId(3), new ItemInstanceId(9)],
            presented.DiscreteItems.Select(item => item.Id));
    }

    [Fact]
    public void SnapshotReportsCustodyCapacityAndAvailabilityWithoutLocalizedText()
    {
        Inventory inventory = CreateInventory(1, 17, 20);
        PhysicalDefinition ore = FungibleDefinition("ore", 2);
        Assert.True(inventory.StoreFungible(ore, new Quantity(5)).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Fungible(
                ore.Key,
                new Quantity(2)),
            Owner(3)).IsAccepted);

        InventoryPresentationSnapshot snapshot = inventory.CapturePresentationSnapshot();

        Assert.Equal(inventory.Id, snapshot.Id);
        Assert.Equal(inventory.Custody, snapshot.Custody);
        Assert.Equal<ulong>(20, snapshot.Capacity.Units);
        Assert.Equal<ulong>(10, snapshot.UsedCapacity.Units);
        Assert.Equal<ulong>(10, snapshot.RemainingCapacity.Units);
        FungibleHoldingPresentationSnapshot holding = Assert.Single(
            snapshot.FungibleHoldings);
        Assert.Equal<ulong>(5, holding.Stored.Units);
        Assert.Equal<ulong>(2, holding.Reserved.Units);
        Assert.Equal<ulong>(3, holding.Available.Units);
        Assert.Null(typeof(InventoryPresentationSnapshot).GetProperty("DisplayName"));
    }

    [Fact]
    public void SnapshotOrdersReservationSummariesAndMarksReservedInstances()
    {
        Inventory inventory = CreateInventory(1, 17, 30);
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 3);
        var reservedItemId = new ItemInstanceId(9);
        var availableItemId = new ItemInstanceId(3);
        Assert.True(inventory.StoreDiscrete(sensor, reservedItemId).IsAccepted);
        Assert.True(inventory.StoreDiscrete(sensor, availableItemId).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(8),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(4)),
            Owner(3)).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(2),
            new PhysicalReservationSubject.Discrete(reservedItemId),
            Owner(4)).IsAccepted);

        InventoryPresentationSnapshot snapshot = inventory.CapturePresentationSnapshot();

        Assert.Equal(
            [new ReservationId(2), new ReservationId(8)],
            snapshot.Reservations.Select(reservation => reservation.Id));
        Assert.Equal<ulong>(4, snapshot.ReservedIncomingCapacity.Units);
        Assert.False(snapshot.DiscreteItems[0].IsReserved);
        Assert.True(snapshot.DiscreteItems[1].IsReserved);
    }

    [Fact]
    public void SnapshotCollectionsAreImmutableCopies()
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(1, 17, 20);
        PhysicalDefinition ore = FungibleDefinition("ore", 1);
        Assert.True(inventory.StoreFungible(ore, new Quantity(2)).IsAccepted);
        registry.Add(inventory);

        InventoryRegistryPresentationSnapshot snapshot =
            registry.CapturePhysicalPresentationSnapshot();
        var inventories = Assert.IsAssignableFrom<IList<InventoryPresentationSnapshot>>(
            snapshot.Inventories);
        var holdings = Assert.IsAssignableFrom<IList<FungibleHoldingPresentationSnapshot>>(
            snapshot.Inventories[0].FungibleHoldings);

        Assert.Throws<NotSupportedException>(() => inventories.Clear());
        Assert.Throws<NotSupportedException>(() => holdings.Clear());

        Assert.True(inventory.StoreFungible(ore, new Quantity(1)).IsAccepted);
        Assert.Equal<ulong>(2, snapshot.Inventories[0].FungibleHoldings[0].Stored.Units);
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
}
