using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryTransferTests
{
    [Fact]
    public void AvailableFungibleTransferMovesOneAtomicQuantity()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = FungibleDefinition("ore", 2);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(5)).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3))));

        Assert.True(result.IsAccepted);
        Assert.Equal<ulong>(2, fixture.Source.FungibleStored(definition.Key).Units);
        Assert.Equal<ulong>(3, fixture.Destination.FungibleStored(definition.Key).Units);
        Assert.Equal<ulong>(4, fixture.Source.UsedCapacity.Units);
        Assert.Equal<ulong>(6, fixture.Destination.UsedCapacity.Units);
    }

    [Fact]
    public void FullDestinationRejectsFungibleTransferWithoutMutation()
    {
        TransferFixture fixture = CreateFixture(20, 5);
        PhysicalDefinition definition = FungibleDefinition("ore", 2);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(5)).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3))));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.InsufficientCapacity,
            result.RejectionReason);
        Assert.Equal<ulong>(5, fixture.Source.FungibleStored(definition.Key).Units);
        Assert.Equal(Quantity.Zero, fixture.Destination.FungibleStored(definition.Key));
    }

    [Fact]
    public void ReservedFungibleTransferConsumesExactAuthorizedReservation()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(5)).IsAccepted);
        Assert.True(fixture.Source.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.Fungible(
                definition.Key,
                new Quantity(3)),
            owner).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3)),
                reservationId,
                null,
                owner));

        Assert.True(result.IsAccepted);
        Assert.Null(fixture.Source.GetPhysicalReservation(reservationId));
        Assert.Equal<ulong>(2, fixture.Source.FungibleStored(definition.Key).Units);
        Assert.Equal<ulong>(3, fixture.Destination.FungibleStored(definition.Key).Units);
    }

    [Fact]
    public void ReservedFungibleCannotMoveWithoutReservationAuthority()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(5)).IsAccepted);
        Assert.True(fixture.Source.ReservePhysical(
            new ReservationId(4),
            new PhysicalReservationSubject.Fungible(
                definition.Key,
                new Quantity(3)),
            Owner(3)).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3))));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.InsufficientAvailableQuantity,
            result.RejectionReason);
        Assert.Equal<ulong>(5, fixture.Source.FungibleStored(definition.Key).Units);
    }

    [Fact]
    public void IncomingCapacityReservationIsConsumedByExactTransferCost()
    {
        TransferFixture fixture = CreateFixture(20, 6);
        PhysicalDefinition definition = FungibleDefinition("ore", 2);
        ReservationOwner owner = Owner(3);
        var capacityReservationId = new ReservationId(5);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(3)).IsAccepted);
        Assert.True(fixture.Destination.ReservePhysical(
            capacityReservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(6)),
            owner).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3)),
                null,
                capacityReservationId,
                owner));

        Assert.True(result.IsAccepted);
        Assert.Null(fixture.Destination.GetPhysicalReservation(capacityReservationId));
        Assert.Equal(Quantity.Zero, fixture.Destination.ReservedCapacity);
        Assert.Equal<ulong>(6, fixture.Destination.UsedCapacity.Units);
    }

    [Fact]
    public void DiscreteTransferPreservesExactInstanceIdentity()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = DiscreteDefinition("sensor", 3);
        var instanceId = new ItemInstanceId(7);
        Assert.True(fixture.Source.StoreDiscrete(definition, instanceId).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Discrete(instanceId)));

        Assert.True(result.IsAccepted);
        Assert.Null(fixture.Source.GetDiscrete(instanceId));
        Assert.Equal(
            new DiscreteItemInstance(instanceId, definition.Key),
            fixture.Destination.GetDiscrete(instanceId));
    }

    [Fact]
    public void ReservedDiscreteTransferConsumesExactAuthorizedReservation()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = DiscreteDefinition("sensor", 3);
        var instanceId = new ItemInstanceId(7);
        var reservationId = new ReservationId(4);
        ReservationOwner owner = Owner(3);
        Assert.True(fixture.Source.StoreDiscrete(definition, instanceId).IsAccepted);
        Assert.True(fixture.Source.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.Discrete(instanceId),
            owner).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Discrete(instanceId),
                reservationId,
                null,
                owner));

        Assert.True(result.IsAccepted);
        Assert.Null(fixture.Source.GetPhysicalReservation(reservationId));
        Assert.Null(fixture.Source.GetDiscrete(instanceId));
        Assert.NotNull(fixture.Destination.GetDiscrete(instanceId));
    }

    [Fact]
    public void DestinationCapacityReservationMustMatchExactTransferCost()
    {
        TransferFixture fixture = CreateFixture(20, 6);
        PhysicalDefinition definition = FungibleDefinition("ore", 2);
        ReservationOwner owner = Owner(3);
        var capacityReservationId = new ReservationId(5);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(3)).IsAccepted);
        Assert.True(fixture.Destination.ReservePhysical(
            capacityReservationId,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(5)),
            owner).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3)),
                null,
                capacityReservationId,
                owner));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.DestinationReservationMismatch,
            result.RejectionReason);
        Assert.NotNull(fixture.Destination.GetPhysicalReservation(capacityReservationId));
        Assert.Equal<ulong>(3, fixture.Source.FungibleStored(definition.Key).Units);
        Assert.Equal(Quantity.Zero, fixture.Destination.FungibleStored(definition.Key));
    }

    [Fact]
    public void DestinationInstanceConflictRejectsWithoutMutation()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = DiscreteDefinition("sensor", 3);
        var instanceId = new ItemInstanceId(7);
        Assert.True(fixture.Source.StoreDiscrete(definition, instanceId).IsAccepted);
        Assert.True(fixture.Destination.StoreDiscrete(definition, instanceId).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Discrete(instanceId)));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.DestinationItemConflict,
            result.RejectionReason);
        Assert.NotNull(fixture.Source.GetDiscrete(instanceId));
        Assert.NotNull(fixture.Destination.GetDiscrete(instanceId));
    }

    [Fact]
    public void CustodyFreeEndpointRejectsWithoutMutation()
    {
        var registry = new InventoryRegistry();
        var source = new Inventory(new InventoryId(1), new Quantity(20));
        Inventory destination = CreateInventory(2, 18, 20);
        registry.Add(source);
        registry.Add(destination);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);

        InventoryTransferResult result = registry.TransferPhysical(
            new PhysicalTransferRequest(
                source.Id,
                destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(1))));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.MissingCustody,
            result.RejectionReason);
    }

    [Fact]
    public void SourceReservationMismatchRejectsWithoutMutation()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        ReservationOwner owner = Owner(3);
        var reservationId = new ReservationId(4);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(5)).IsAccepted);
        Assert.True(fixture.Source.ReservePhysical(
            reservationId,
            new PhysicalReservationSubject.Fungible(
                definition.Key,
                new Quantity(2)),
            owner).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Destination.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(3)),
                reservationId,
                null,
                owner));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.SourceReservationMismatch,
            result.RejectionReason);
        Assert.NotNull(fixture.Source.GetPhysicalReservation(reservationId));
        Assert.Equal<ulong>(5, fixture.Source.FungibleStored(definition.Key).Units);
        Assert.Equal(Quantity.Zero, fixture.Destination.FungibleStored(definition.Key));
    }

    [Fact]
    public void SameInventoryTransferRejectsWithoutMutation()
    {
        TransferFixture fixture = CreateFixture(20, 20);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        Assert.True(fixture.Source.StoreFungible(
            definition,
            new Quantity(2)).IsAccepted);

        InventoryTransferResult result = fixture.Registry.TransferPhysical(
            new PhysicalTransferRequest(
                fixture.Source.Id,
                fixture.Source.Id,
                definition,
                new PhysicalTransferSubject.Fungible(
                    definition.Key,
                    new Quantity(1))));

        Assert.False(result.IsAccepted);
        Assert.Equal(
            InventoryTransferRejectionReason.SameInventory,
            result.RejectionReason);
        Assert.Equal<ulong>(2, fixture.Source.FungibleStored(definition.Key).Units);
    }

    private static TransferFixture CreateFixture(
        ulong sourceCapacity,
        ulong destinationCapacity)
    {
        var registry = new InventoryRegistry();
        Inventory source = CreateInventory(1, 17, sourceCapacity);
        Inventory destination = CreateInventory(2, 18, destinationCapacity);
        registry.Add(source);
        registry.Add(destination);
        return new TransferFixture(registry, source, destination);
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

    private sealed record TransferFixture(
        InventoryRegistry Registry,
        Inventory Source,
        Inventory Destination);
}
