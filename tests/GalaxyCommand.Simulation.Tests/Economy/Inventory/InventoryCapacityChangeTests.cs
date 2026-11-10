using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryCapacityChangeTests
{
    private static readonly MaterialId Ore = new(1);
    private static readonly PhysicalDefinition Metal = new(
        QualifiedContentKey.Create("test", "cargo", "metal"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly ReservationOwner Owner = new ReservationOwner.TransportJob(new(1));

    [Fact]
    public void DisablingStorageRetainsCargoAndCommitmentsButStopsNewIntake()
    {
        Inventory inventory = Create(1, 20);
        Assert.True(inventory.StoreFungible(Metal, new(8)).IsAccepted);
        inventory.ReserveCapacity(new(1), new(4), Owner);

        Apply(inventory, 20, 5);

        Assert.Equal(new Quantity(20), inventory.Capacity);
        Assert.Equal(new Quantity(5), inventory.EnabledIntakeCapacity);
        Assert.Equal(new Quantity(8), inventory.FungibleStored(Metal.Key));
        Assert.Equal(new Quantity(4), inventory.ReservedCapacity);
        Assert.Equal(Quantity.Zero, inventory.RemainingCapacity);
        Assert.False(inventory.StoreFungible(Metal, new(1)).IsAccepted);
        Assert.False(inventory.ReservePhysical(new(2), new PhysicalReservationSubject.IncomingCapacity(new(1)), Owner).IsAccepted);
    }

    [Fact]
    public void ProtectedCapacityIncludesCargoAndAllIncomingCommitments()
    {
        Inventory inventory = Create(1, 20);
        inventory.StoreFungible(Metal, new(6));
        inventory.ReserveCapacity(new(1), new(2), Owner);
        inventory.ReservePhysical(new(2), new PhysicalReservationSubject.IncomingCapacity(new(3)), Owner);

        Assert.Null(inventory.PrepareCapacityChange(new(20), new(20), new(10)));
        Assert.Null(inventory.PrepareCapacityChange(new(10), new(10)));
        Apply(inventory, 20, 20, 11);

        Assert.Equal(new Quantity(11), inventory.ProtectedIntakeCapacity);
        Assert.Equal(Quantity.Zero, inventory.RemainingCapacity);
        Assert.False(inventory.StoreFungible(Metal, new(1)).IsAccepted);
    }

    [Fact]
    public void CapacityExpansionActivatesOnlyAtCommit()
    {
        Inventory inventory = Create(1, 5);
        PreparedInventoryCapacityChange prepared = inventory.PrepareCapacityChange(new(12), new(12))!;

        Assert.Equal(new Quantity(5), inventory.Capacity);
        Assert.True(inventory.ApplyCapacityChange(prepared));
        Assert.Equal(new Quantity(12), inventory.Capacity);
        Assert.False(inventory.ApplyCapacityChange(prepared));
    }

    [Fact]
    public void PreparedReductionRejectsNewHoldingsWithoutMutation()
    {
        Inventory inventory = Create(1, 20);
        PreparedInventoryCapacityChange prepared = inventory.PrepareCapacityChange(new(10), new(10))!;
        inventory.StoreFungible(Metal, new(12));

        Assert.False(inventory.CanCommitCapacityChange(prepared));
        Assert.False(inventory.ApplyCapacityChange(prepared));
        Assert.Equal(new Quantity(20), inventory.Capacity);
        Assert.Equal(new Quantity(12), inventory.UsedCapacity);
    }

    [Fact]
    public void PlansCannotCrossInventoriesOrSurviveAnotherCapacityCommit()
    {
        Inventory inventory = Create(1, 20);
        Inventory other = Create(2, 20);
        PreparedInventoryCapacityChange prepared = inventory.PrepareCapacityChange(new(10), new(10))!;

        Assert.False(other.ApplyCapacityChange(prepared));
        Apply(inventory, 20, 20);
        Assert.False(inventory.ApplyCapacityChange(prepared));
    }

    [Fact]
    public void InvalidLimitsRejectWithoutChangingCurrentLimits()
    {
        Inventory inventory = Create(1, 20);
        Assert.Null(inventory.PrepareCapacityChange(new(10), new(11)));
        Assert.Null(inventory.PrepareCapacityChange(new(10), new(10), new(11)));
        Assert.Equal(new Quantity(20), inventory.RemainingCapacity);
    }

    [Fact]
    public void DisabledStorageStillAllowsUnloading()
    {
        Inventory source = Create(1, 20);
        Inventory destination = Create(2, 20);
        source.StoreFungible(Metal, new(8));
        Apply(source, 20, 0);
        var registry = new InventoryRegistry();
        registry.Add(source);
        registry.Add(destination);

        Assert.True(registry.TransferPhysical(new(source.Id, destination.Id, Metal,
            new PhysicalTransferSubject.Fungible(Metal.Key, new(8)))).IsAccepted);
        Assert.Equal(Quantity.Zero, source.UsedCapacity);
    }

    [Fact]
    public void ReservedPhysicalDeliveryCannotBypassDisabledIntake()
    {
        Inventory source = Create(1, 20);
        Inventory destination = Create(2, 20);
        source.StoreFungible(Metal, new(8));
        destination.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(8)), Owner);
        Apply(destination, 20, 0);
        var registry = new InventoryRegistry();
        registry.Add(source);
        registry.Add(destination);

        InventoryTransferResult result = registry.TransferPhysical(new(source.Id, destination.Id, Metal,
            new PhysicalTransferSubject.Fungible(Metal.Key, new(8)), destinationCapacityReservationId: new(1), reservationOwner: Owner));

        Assert.False(result.IsAccepted);
        Assert.Equal(InventoryTransferRejectionReason.InsufficientCapacity, result.RejectionReason);
        Assert.Equal(new Quantity(8), source.UsedCapacity);
        Assert.Equal(Quantity.Zero, destination.UsedCapacity);
        Assert.NotNull(destination.GetPhysicalReservation(new(1)));
    }

    [Fact]
    public void ReservedLegacyDeliveryRejectsBeforeSourceOrReservationMutation()
    {
        var source = new Inventory(new(1), new(20));
        var destination = new Inventory(new(2), new(20));
        source.Add(Ore, new(8));
        destination.ReserveCapacity(new(1), new(8), Owner);
        Apply(destination, 20, 0);
        var registry = new InventoryRegistry();
        registry.Add(source);
        registry.Add(destination);

        Assert.Throws<InvalidOperationException>(() => registry.TransferIntoReservedCapacity(
            source.Id, destination.Id, Ore, new(8), new(1), Owner));
        Assert.Equal(new Quantity(8), source.Stored(Ore));
        Assert.NotNull(destination.GetCapacityReservation(new(1)));
    }

    [Fact]
    public void DisabledAndProtectedCapacityRoundTripWithHoldingsAndIncomingClaims()
    {
        Inventory inventory = Create(1, 20);
        inventory.StoreFungible(Metal, new(8));
        inventory.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(2)), Owner);
        Apply(inventory, 20, 4, 12);

        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(inventory.CaptureCheckpoint(), new PhysicalDefinitionCatalog([Metal]));

        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(new Quantity(20), restored.Value!.Capacity);
        Assert.Equal(new Quantity(4), restored.Value.EnabledIntakeCapacity);
        Assert.Equal(new Quantity(12), restored.Value.ProtectedIntakeCapacity);
        Assert.Equal(new Quantity(8), restored.Value.UsedCapacity);
        Assert.Equal(new Quantity(2), restored.Value.ReservedCapacity);
        Assert.Equal(Quantity.Zero, restored.Value.RemainingCapacity);
    }

    [Fact]
    public void MalformedSavedCapacityLimitsRejectBeforePublication()
    {
        Inventory inventory = Create(1, 20);
        inventory.StoreFungible(Metal, new(8));
        InventoryCheckpoint original = inventory.CaptureCheckpoint();
        foreach ((ulong enabled, ulong? protection) in new (ulong, ulong?)[] { (21, null), (20, 21), (20, 7) })
        {
            var invalid = new InventoryCheckpoint(original.Id, original.Custody, original.Capacity,
                original.StoredMaterials, original.Reservations, original.CapacityReservations,
                original.FungibleHoldings, original.DiscreteItems, original.PhysicalReservations,
                new(enabled), protection is { } value ? new Quantity(value) : null,
                original.NextIncomingAdmissionSequence, original.IncomingAdmissions,
                original.NextOutgoingAdmissionSequence, original.OutgoingAdmissions);

            Assert.False(Inventory.RestoreCheckpoint(invalid, new PhysicalDefinitionCatalog([Metal])).IsSuccess);
        }
    }

    [Fact]
    public void PreparedCargoLossRejectsChangedIntakeAuthority()
    {
        Inventory source = Create(1, 20);
        source.StoreFungible(Metal, new(8));
        PreparedCargoSourceLoss prepared = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Metal.Key, new(2))], new PhysicalDefinitionCatalog([Metal]), []).Prepared!;

        Apply(source, 20, 10);

        Assert.False(prepared.CanApply());
        Assert.False(prepared.Apply());
        Assert.Equal(new Quantity(8), source.FungibleStored(Metal.Key));
    }

    [Fact]
    public void PreparedCollectionRejectsChangedDestinationIntakeAuthority()
    {
        Inventory source = Inventory.CreateCargoObjectInventory(new(1), new(1), Metal,
            new PhysicalTransferSubject.Fungible(Metal.Key, new(8)));
        Inventory destination = Create(2, 20);
        var owner = new ReservationOwner.CargoCollection(new(1), new(1), new(1));
        destination.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(8)), owner);
        var registry = new InventoryRegistry();
        registry.Add(source);
        registry.Add(destination);
        var collector = new GameSessionShip(new(1), new(1), new(1), destination.Id);
        var request = new PhysicalTransferRequest(source.Id, destination.Id, Metal,
            new PhysicalTransferSubject.Fungible(Metal.Key, new(8)), destinationCapacityReservationId: new(1), reservationOwner: owner);
        Assert.True(registry.TryPrepareCollectedCargoTransfer(request, collector, new(2), out var prepared, out _));

        Apply(destination, 20, 0);

        Assert.False(prepared!.CanApply());
        Assert.False(prepared.Apply());
        Assert.Equal(new Quantity(8), source.UsedCapacity);
        Assert.NotNull(destination.GetPhysicalReservation(new(1)));
    }

    private static Inventory Create(ulong id, ulong capacity) => new(new(id),
        new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(new(id)), new(1)), new(capacity));

    private static void Apply(Inventory inventory, ulong physical, ulong enabled, ulong? protection = null)
    {
        PreparedInventoryCapacityChange? prepared = inventory.PrepareCapacityChange(new(physical), new(enabled),
            protection is { } value ? new Quantity(value) : null);
        Assert.NotNull(prepared);
        Assert.True(inventory.ApplyCapacityChange(prepared));
    }
}
