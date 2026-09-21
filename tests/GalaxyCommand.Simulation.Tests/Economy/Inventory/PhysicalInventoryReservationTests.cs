using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryReservationTests
{
    [Fact]
    public void FungibleReservationReducesAvailabilityWithoutRemovingHolding()
    {
        Inventory inventory = CreateInventory(20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        Assert.True(inventory.StoreFungible(definition, new Quantity(8)).IsAccepted);
        ReservationOwner owner = Owner(3);

        InventoryReservationResult result = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Fungible(
                definition.Key,
                new Quantity(3)),
            owner);

        Assert.True(result.IsAccepted);
        Assert.Equal<ulong>(8, inventory.FungibleStored(definition.Key).Units);
        Assert.Equal<ulong>(3, inventory.FungibleReserved(definition.Key).Units);
        Assert.Equal<ulong>(5, inventory.FungibleAvailable(definition.Key).Units);
        Assert.True(inventory.HasCommitments);
    }

    [Fact]
    public void FungibleReservationRejectsUnavailableQuantityWithoutMutation()
    {
        Inventory inventory = CreateInventory(20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        Assert.True(inventory.StoreFungible(definition, new Quantity(3)).IsAccepted);

        InventoryReservationResult result = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Fungible(
                definition.Key,
                new Quantity(4)),
            Owner(3));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.InsufficientAvailableQuantity,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.FungibleReserved(definition.Key));
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void PhysicalReservationRequiresCustody()
    {
        var inventory = new Inventory(new InventoryId(2), new Quantity(10));

        InventoryReservationResult result = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            Owner(3));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.MissingCustody,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.ReservedCapacity);
    }

    [Fact]
    public void DiscreteReservationClaimsOneExactStoredInstance()
    {
        Inventory inventory = CreateInventory(20);
        PhysicalDefinition definition = DiscreteDefinition("sensor", 2);
        var instanceId = new ItemInstanceId(7);
        Assert.True(inventory.StoreDiscrete(definition, instanceId).IsAccepted);

        InventoryReservationResult first = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Discrete(instanceId),
            Owner(3));
        InventoryReservationResult second = inventory.ReservePhysical(
            new ReservationId(5),
            new PhysicalReservationSubject.Discrete(instanceId),
            Owner(4));

        Assert.True(first.IsAccepted);
        Assert.True(inventory.IsDiscreteReserved(instanceId));
        Assert.False(second.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.ItemAlreadyReserved,
            second.RejectionReason);
        Assert.Null(inventory.GetPhysicalReservation(new ReservationId(5)));
    }

    [Fact]
    public void DiscreteReservationRejectsUnknownInstanceWithoutMutation()
    {
        Inventory inventory = CreateInventory(20);

        InventoryReservationResult result = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Discrete(new ItemInstanceId(7)),
            Owner(3));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.MissingItemInstance,
            result.RejectionReason);
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void IncomingCapacityReservationReducesRemainingCapacityUntilReleased()
    {
        Inventory inventory = CreateInventory(10);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);

        InventoryReservationResult reserved = inventory.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(6)),
            owner);

        Assert.True(reserved.IsAccepted);
        Assert.Equal<ulong>(6, inventory.ReservedCapacity.Units);
        Assert.Equal<ulong>(4, inventory.RemainingCapacity.Units);

        InventoryReservationResult released = inventory.ReleasePhysicalReservation(
            reservationId,
            owner);

        Assert.True(released.IsAccepted);
        Assert.Equal(Quantity.Zero, inventory.ReservedCapacity);
        Assert.Equal<ulong>(10, inventory.RemainingCapacity.Units);
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void IncomingCapacityReservationRejectsOvercommitmentWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);

        InventoryReservationResult result = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(11)),
            Owner(3));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.InsufficientCapacity,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.ReservedCapacity);
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void ReleaseRejectsOwnerMismatchWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);
        Assert.True(inventory.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(6)),
            owner).IsAccepted);

        InventoryReservationResult result = inventory.ReleasePhysicalReservation(
            reservationId,
            Owner(5));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.OwnerMismatch,
            result.RejectionReason);
        Assert.NotNull(inventory.GetPhysicalReservation(reservationId));
        Assert.Equal<ulong>(6, inventory.ReservedCapacity.Units);
    }

    [Fact]
    public void ReleaseRejectsUnknownReservationWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);

        InventoryReservationResult result = inventory.ReleasePhysicalReservation(
            new ReservationId(4),
            Owner(3));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.UnknownReservation,
            result.RejectionReason);
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void ReleaseRejectsInvalidExpectedOwnerWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);
        Assert.True(inventory.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            owner).IsAccepted);

        InventoryReservationResult result = inventory.ReleasePhysicalReservation(
            reservationId,
            new ReservationOwner.TransportJob(default));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.InvalidOwner,
            result.RejectionReason);
        Assert.NotNull(inventory.GetPhysicalReservation(reservationId));
    }

    [Fact]
    public void PhysicalReservationRejectsLegacyReservationIdentityCollision()
    {
        Inventory inventory = CreateInventory(10);
        MaterialId materialId = new(1);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);
        inventory.Add(materialId, new Quantity(2));
        inventory.Reserve(
            reservationId,
            materialId,
            new Quantity(1),
            owner);

        InventoryReservationResult result = inventory.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            owner);

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.DuplicateReservationId,
            result.RejectionReason);
        Assert.Equal(Quantity.Zero, inventory.ReservedCapacity);
    }

    [Fact]
    public void ReservationRejectsDefaultIdentityWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);

        InventoryReservationResult result = inventory.ReservePhysical(
            default,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            Owner(3));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.InvalidReservationId,
            result.RejectionReason);
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void ReservationRejectsInvalidWorkflowOwnerWithoutMutation()
    {
        Inventory inventory = CreateInventory(10);

        InventoryReservationResult result = inventory.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            new ReservationOwner.TransportJob(default));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryReservationRejectionReason.InvalidOwner,
            result.RejectionReason);
        Assert.False(inventory.HasCommitments);
    }

    [Fact]
    public void LegacyReservationRejectsPhysicalReservationIdentityCollision()
    {
        Inventory inventory = CreateInventory(10);
        MaterialId materialId = new(1);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);
        inventory.Add(materialId, new Quantity(2));
        Assert.True(inventory.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            owner).IsAccepted);

        Assert.Throws<InvalidOperationException>(
            () => inventory.Reserve(
                reservationId,
                materialId,
                new Quantity(1),
                owner));
        Assert.Equal(Quantity.Zero, inventory.Reserved(materialId));
    }

    private static Inventory CreateInventory(ulong capacity) =>
        new(
            new InventoryId(2),
            new InventoryCustody(
                new InventoryOwnerReference.SessionEntity(new EntityId(17)),
                new PrincipalId(4)),
            new Quantity(capacity));

    private static ReservationOwner.TransportJob Owner(ulong jobId) =>
        new ReservationOwner.TransportJob(new TransportJobId(jobId));

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
