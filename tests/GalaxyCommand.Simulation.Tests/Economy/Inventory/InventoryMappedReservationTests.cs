using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryMappedReservationTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.mapped", "cargo", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly MaterialInventoryCompatibilityMap Materials = new(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = Ore });
    private static readonly PhysicalDefinitionCatalog Definitions = new([Ore]);
    private static readonly ReservationOwner Owner = new ReservationOwner.TransportJob(new(1));

    [Fact]
    public void LegacyReservationReducesGeneralizedAvailabilityAndBlocksCombinedOverbooking()
    {
        Inventory inventory = Source(1, 20);
        inventory.Reserve(new(1), Metal, new(12), Owner);

        Assert.Equal(new Quantity(8), inventory.FungibleAvailable(Ore.Key));
        Assert.False(inventory.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(9)), Owner).IsAccepted);
        Assert.True(inventory.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(8)), Owner).IsAccepted);
        Assert.Equal(Quantity.Zero, inventory.Available(Metal));
        Assert.Equal(Quantity.Zero, inventory.FungibleAvailable(Ore.Key));
        Assert.Equal(new Quantity(12), inventory.Reserved(Metal));
        Assert.Equal(new Quantity(8), inventory.FungibleReserved(Ore.Key));
    }

    [Fact]
    public void PhysicalReservationReducesLegacyAvailabilityAndProtectsUnreservedRemoval()
    {
        Inventory inventory = Source(1, 20);
        PhysicalReservation protectedClaim = inventory.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(12)), Owner).Reservation!;

        Assert.Equal(new Quantity(8), inventory.Available(Metal));
        Assert.Throws<InvalidOperationException>(() => inventory.Reserve(new(2), Metal, new(9), Owner));
        Assert.Throws<InvalidOperationException>(() => inventory.RemoveAvailable(Metal, new(9)));
        inventory.RemoveAvailable(Metal, new(8));

        Assert.Equal(new Quantity(12), inventory.Stored(Metal));
        Assert.Equal(Quantity.Zero, inventory.Available(Metal));
        Assert.Same(protectedClaim, inventory.GetPhysicalReservation(protectedClaim.Id));
        Assert.Equal(2UL, inventory.CaptureCheckpoint().NextOutgoingAdmissionSequence);
    }

    [Fact]
    public void ConsumingLegacyClaimRetainsOppositeKindClaimAndSharedStockAccounting()
    {
        Inventory inventory = Source(1, 20);
        PhysicalReservation physical = inventory.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(8)), Owner).Reservation!;
        Reservation legacy = inventory.Reserve(new(2), Metal, new(6), Owner);

        inventory.ConsumeReservations([legacy.Id], legacy.Owner);

        Assert.Same(physical, inventory.GetPhysicalReservation(physical.Id));
        Assert.Equal(new Quantity(14), inventory.Stored(Metal));
        Assert.Equal(new Quantity(6), inventory.Available(Metal));
        Assert.Equal(new Quantity(6), inventory.FungibleAvailable(Ore.Key));
        Assert.Equal(Quantity.Zero, inventory.Reserved(Metal));
    }

    [Fact]
    public void TransferringReservedPhysicalStockRetainsTheLegacyClaim()
    {
        Inventory source = Source(1, 20);
        Inventory destination = Source(2, 0);
        Reservation legacy = source.Reserve(new(1), Metal, new(8), Owner);
        PhysicalReservation physical = source.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(6)), Owner).Reservation!;
        var inventories = new InventoryRegistry();
        inventories.Add(source);
        inventories.Add(destination);

        InventoryTransferResult transferred = inventories.TransferPhysical(new(source.Id, destination.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(6)), sourceReservationId: physical.Id, reservationOwner: Owner));

        Assert.True(transferred.IsAccepted);
        Assert.Same(legacy, source.GetReservation(legacy.Id));
        Assert.Equal(new Quantity(14), source.Stored(Metal));
        Assert.Equal(new Quantity(6), source.Available(Metal));
        Assert.Equal(new Quantity(6), source.FungibleAvailable(Ore.Key));
    }

    [Fact]
    public void RestoreRejectsCombinedOverbookingEvenWhenEachFacadeFitsIndividually()
    {
        Inventory source = Source(1, 20);
        source.Reserve(new(1), Metal, new(8), Owner);
        source.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(8)), Owner);
        InventoryCheckpoint saved = source.CaptureCheckpoint();
        var malformed = new InventoryCheckpoint(saved.Id, saved.Custody, saved.Capacity, saved.StoredMaterials,
            saved.Reservations, saved.CapacityReservations, [new(Ore.Key, new(10))], saved.DiscreteItems,
            saved.PhysicalReservations, saved.EnabledIntakeCapacity, saved.ProtectedIntakeCapacity,
            saved.NextIncomingAdmissionSequence, saved.IncomingAdmissions, saved.NextOutgoingAdmissionSequence, saved.OutgoingAdmissions);

        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(malformed, Definitions, Materials);

        Assert.False(restored.IsSuccess);
        Assert.Null(restored.Value);
    }

    private static Inventory Source(ulong id, ulong amount)
    {
        var inventory = new Inventory(new(id), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(id)), new(1)), new(100), Materials);
        inventory.Add(Metal, new(amount));
        return inventory;
    }
}
