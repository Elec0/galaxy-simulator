using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionInventoryTests
{
    private static readonly PhysicalDefinition Ore = new(
        QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly ReservationOwner.CargoCollection Owner = new(new(1), new(1), new(1));

    [Fact]
    public void ContestedRemainderConsumesTransferCostAndReleasesUnusedCapacityAtomically()
    {
        var (registry, cargo, ship) = Create();
        Assert.True(ship.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(20)), Owner).IsAccepted);
        cargo.ApplyRemoveFungible(Ore.Key, new(7), new(14));
        var request = new PhysicalTransferRequest(cargo.Id, ship.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(3)), destinationCapacityReservationId: new(1), reservationOwner: Owner);
        Assert.False(registry.TransferPhysical(request).IsAccepted);
        Assert.True(Collect(registry, request).IsAccepted);
        Assert.Equal(new Quantity(3), ship.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(94), ship.RemainingCapacity);
        Assert.Equal(Quantity.Zero, ship.ReservedCapacity);
        Assert.Null(ship.GetPhysicalReservation(new(1)));
        Assert.Equal(Quantity.Zero, cargo.UsedCapacity);
    }

    [Fact]
    public void InvalidCollectionAuthorityLeavesReservationAndBothHoldingsUnchanged()
    {
        var (registry, cargo, ship) = Create();
        Assert.True(ship.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(20)), Owner).IsAccepted);
        var other = new ReservationOwner.CargoCollection(new(1), new(1), new(2));
        var request = new PhysicalTransferRequest(cargo.Id, ship.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(3)), destinationCapacityReservationId: new(1), reservationOwner: other);
        Assert.False(Collect(registry, request).IsAccepted);
        Assert.Equal(new Quantity(10), cargo.FungibleStored(Ore.Key));
        Assert.Equal(Quantity.Zero, ship.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(20), ship.ReservedCapacity);
    }

    [Fact]
    public void CollectionCannotClaimMoreThanReservedOrUseOrdinaryInventoryAsSource()
    {
        var (registry, cargo, ship) = Create();
        Assert.True(ship.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(2)), Owner).IsAccepted);
        Assert.False(Collect(registry, new(cargo.Id, ship.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(2)), destinationCapacityReservationId: new(1), reservationOwner: Owner)).IsAccepted);
        Assert.True(ship.StoreFungible(Ore, new(2)).IsAccepted);
        var otherShip = new Inventory(new(3), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(3)), new(7)), new(100));
        registry.Add(otherShip);
        Assert.True(otherShip.ReservePhysical(new(2), new PhysicalReservationSubject.IncomingCapacity(new(10)), Owner).IsAccepted);
        Assert.False(Collect(registry, new(ship.Id, otherShip.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(2)), destinationCapacityReservationId: new(2), reservationOwner: Owner)).IsAccepted);
        Assert.Equal(new Quantity(10), otherShip.ReservedCapacity);
    }

    [Fact]
    public void CargoCollectionOwnerCanReserveOnlyGeneralizedIncomingCapacity()
    {
        var (_, _, ship) = Create();
        Assert.True(ship.StoreFungible(Ore, new(2)).IsAccepted);
        Assert.False(ship.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner).IsAccepted);
        Assert.False(ship.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(2)),
            new ReservationOwner.CargoCollection(default, new(1), new(1))).IsAccepted);
        Assert.True(ship.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(2)), Owner).IsAccepted);
        var restored = Inventory.RestoreCheckpoint(ship.CaptureCheckpoint(), new PhysicalDefinitionCatalog([Ore]));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(Owner, restored.Value!.GetPhysicalReservation(new(1))!.Owner);
    }

    [Fact]
    public void CollectionSettlementRequiresExactShipCustodyAndCapacityReservation()
    {
        var (registry, cargo, ship) = Create();
        var wrongShip = new ReservationOwner.CargoCollection(new(7), new(1), new(1));
        Assert.True(ship.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(20)), wrongShip).IsAccepted);
        Assert.False(Collect(registry, new(cargo.Id, ship.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(3)), destinationCapacityReservationId: new(1), reservationOwner: wrongShip)).IsAccepted);
        Assert.NotNull(ship.GetPhysicalReservation(new(1)));
    }

    [Fact]
    public void RestoreRejectsCollectionOwnerInLegacyReservationShapes()
    {
        var custody = new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(new(1)), new(4));
        var capacity = new InventoryCheckpoint(new(2), custody, new(100), [], [],
            [new CapacityReservation(new(1), new(2), new(2), Owner)]);
        var material = new InventoryCheckpoint(new(2), custody, new(100), [new(new(1), new(2))],
            [new Reservation(new(1), new(2), new(1), new(1), Owner)], []);
        Assert.False(Inventory.RestoreCheckpoint(capacity).IsSuccess);
        Assert.False(Inventory.RestoreCheckpoint(material).IsSuccess);
    }

    private static InventoryTransferResult Collect(InventoryRegistry registry, PhysicalTransferRequest request) =>
        registry.TransferCollectedCargo(request, new GameSessionShip(new(1), new(4), new(1), new(2)), new(1));

    private static (InventoryRegistry Registry, Inventory Cargo, Inventory Ship) Create()
    {
        Inventory cargo = Inventory.CreateCargoObjectInventory(new(1), new(9), Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(10)));
        var ship = new Inventory(new(2), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(1)), new(4)), new(100));
        var registry = new InventoryRegistry();
        registry.Add(cargo);
        registry.Add(ship);
        return (registry, cargo, ship);
    }
}
