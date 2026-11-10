using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryStationLossCapacityTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.loss-capacity", "cargo", "ore"),
        PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinition Equipment = new(QualifiedContentKey.Create("test.loss-capacity", "cargo", "equipment"),
        PhysicalHoldingKind.Discrete, new(5));
    private static readonly PhysicalDefinitionCatalog Definitions = new([Ore, Equipment]);
    private static readonly ReservationOwner Owner = new ReservationOwner.TransportJob(new(1));

    [Fact]
    public void PreparesBeforeLossAndAppliesOnlyAfterExactLossAndAuthorizedIncomingRelease()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(50)), Owner);
        CapacityReservation newer = source.ReserveCapacity(new(1), new(20), Owner);
        PreparedCargoSourceLoss loss = Loss(source, 5);
        PreparedStationLossCapacityChange? plan = source.PrepareStationLossCapacity(loss, Definitions,
            [new InventoryCommitmentRelease.Capacity(source, newer)], new(60), new(60));

        Assert.NotNull(plan);
        Assert.True(source.CanCommitStationLossCapacity(plan));
        Assert.False(source.ApplyStationLossCapacity(plan));
        Assert.Equal(new Quantity(100), source.Capacity);
        Assert.Equal(new Quantity(20), source.UsedCapacity);
        Assert.True(loss.Apply());
        source.ReleaseCapacity(newer.Id);

        Assert.True(source.ApplyStationLossCapacity(plan));
        Assert.False(source.ApplyStationLossCapacity(plan));
        Assert.Equal(new Quantity(60), source.Capacity);
        Assert.Equal(new Quantity(10), source.UsedCapacity);
        Assert.Equal(new Quantity(50), source.ReservedCapacity);
    }

    [Fact]
    public void InsufficientProjectedRetentionRejectsWithoutAnyMutation()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.ReserveCapacity(new(1), new(70), Owner);

        Assert.Null(source.PrepareStationLossCapacity(Loss(source, 5), Definitions, [], new(60), new(60)));
        Assert.Equal(new Quantity(100), source.Capacity);
        Assert.Equal(new Quantity(20), source.UsedCapacity);
        Assert.Equal(new Quantity(70), source.ReservedCapacity);
    }

    [Fact]
    public void WrongSourceDuplicateAndMismatchedReleaseProofsReject()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        CapacityReservation claim = source.ReserveCapacity(new(1), new(10), Owner);
        PreparedCargoSourceLoss loss = Loss(source, 5);
        var exact = new InventoryCommitmentRelease.Capacity(source, claim);
        Inventory other = Source(2);

        Assert.Null(other.PrepareStationLossCapacity(loss, Definitions, [], new(100), new(100)));
        Assert.Null(source.PrepareStationLossCapacity(loss, Definitions, [exact, exact], new(100), new(100)));
        Assert.Null(source.PrepareStationLossCapacity(loss, Definitions,
            [new InventoryCommitmentRelease.Capacity(other, claim)], new(100), new(100)));
        Assert.Null(source.PrepareStationLossCapacity(loss, Definitions,
            [new InventoryCommitmentRelease.Capacity(source, claim with { Owner = new ReservationOwner.TransportJob(new(2)) })], new(100), new(100)));
    }

    [Fact]
    public void OutgoingCommitmentsCannotBeMisrepresentedAsIncomingReleaseRoom()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        PhysicalReservation outgoing = source.ReservePhysical(new(1),
            new PhysicalReservationSubject.Fungible(Ore.Key, new(2)), Owner).Reservation!;

        Assert.Null(source.PrepareStationLossCapacity(Loss(source, 5), Definitions,
            [new InventoryCommitmentRelease.Physical(source, outgoing)], new(100), new(100)));
    }

    [Fact]
    public void BeforeCommitRejectsDriftAndSameValuedCapacityReplacement()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        PreparedCargoSourceLoss loss = Loss(source, 5);
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(loss, Definitions, [], new(20), new(20))!;
        PreparedInventoryCapacityChange sameLimits = source.PrepareCapacityChange(new(100), new(100))!;
        source.ApplyCapacityChange(sameLimits);

        Assert.True(loss.CanApply());
        Assert.False(source.CanCommitStationLossCapacity(plan));
        Assert.True(loss.Apply());
        Assert.False(source.ApplyStationLossCapacity(plan));
        Assert.Equal(new Quantity(100), source.Capacity);
    }

    [Fact]
    public void ASubstituteRemovalCannotSatisfyTheExactLossReceipt()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        PreparedCargoSourceLoss expectedLoss = Loss(source, 5);
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(expectedLoss, Definitions, [], new(20), new(20))!;
        Assert.True(Loss(source, 5).Apply());

        Assert.False(source.ApplyStationLossCapacity(plan));
        Assert.Equal(new Quantity(100), source.Capacity);
    }

    [Fact]
    public void ExtraUnapprovedIncomingReleaseRejectsThePostLossState()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        CapacityReservation first = source.ReserveCapacity(new(1), new(10), Owner);
        CapacityReservation second = source.ReserveCapacity(new(2), new(10), Owner);
        PreparedCargoSourceLoss loss = Loss(source, 5);
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(loss, Definitions,
            [new InventoryCommitmentRelease.Capacity(source, second)], new(30), new(30))!;
        loss.Apply();
        source.ReleaseCapacity(first.Id);
        source.ReleaseCapacity(second.Id);

        Assert.False(source.ApplyStationLossCapacity(plan));
        Assert.Equal(new Quantity(100), source.Capacity);
    }

    [Fact]
    public void ForgedRemovedTotalsOrSelectedQuantitiesCannotAuthorizeCapacityLoss()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        var forged = new PreparedCargoSourceLoss(source, source.CaptureCheckpoint(),
            [new(Ore, new(2), [])], new(18), [], []);

        Assert.Null(source.PrepareStationLossCapacity(forged, Definitions, [], new(2), new(2)));
    }

    [Fact]
    public void PhysicalIncomingReleasesAndOutgoingLossCleanupProjectExactly()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.StoreDiscrete(Equipment, new(1));
        DiscreteItemInstance retained = source.GetDiscrete(new(1))!;
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(8)), Owner);
        PhysicalReservation incoming = source.ReservePhysical(new(2),
            new PhysicalReservationSubject.IncomingCapacity(new(40)), Owner).Reservation!;
        PreparedCargoSourceLoss loss = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(5))], Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), Owner)]).Prepared!;
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(loss, Definitions,
            [new InventoryCommitmentRelease.Physical(source, incoming)], new(15), new(0))!;

        Assert.True(source.CanCommitStationLossCapacity(plan));
        loss.Apply();
        source.ReleasePhysicalReservation(incoming.Id, incoming.Owner);

        Assert.True(source.ApplyStationLossCapacity(plan));
        Assert.Equal(new Quantity(15), source.Capacity);
        Assert.Equal(Quantity.Zero, source.EnabledIntakeCapacity);
        Assert.Equal(Quantity.Zero, source.ReservedCapacity);
        Assert.Same(retained, source.GetDiscrete(new(1)));
    }

    [Fact]
    public void SurvivingReservationReplacementIsNotExactPostLossState()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        CapacityReservation retained = source.ReserveCapacity(new(1), new(10), Owner);
        PreparedCargoSourceLoss loss = Loss(source, 5);
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(loss, Definitions, [], new(30), new(30))!;
        loss.Apply();
        source.ReleaseCapacity(retained.Id);
        source.ReserveCapacity(retained.Id, retained.Quantity, retained.Owner);

        Assert.False(source.ApplyStationLossCapacity(plan));
    }

    [Fact]
    public void EqualReplacementSurvivingItemCannotSatisfyProjectedIdentityContinuity()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.StoreDiscrete(Equipment, new(1));
        PreparedCargoSourceLoss loss = Loss(source, 5);
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(loss, Definitions, [], new(15), new(15))!;
        loss.Apply();
        CargoSourceLoss.Prepare(source, [new PhysicalTransferSubject.Discrete(new(1))], Definitions, []).Prepared!.Apply();
        source.StoreDiscrete(Equipment, new(1));

        Assert.False(source.ApplyStationLossCapacity(plan));
    }

    [Fact]
    public void BeforeLossRejectsEqualReplacementOfAnUntouchedIncomingCommitment()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        CapacityReservation retained = source.ReserveCapacity(new(1), new(10), Owner);
        PreparedCargoSourceLoss loss = Loss(source, 5);
        PreparedStationLossCapacityChange plan = source.PrepareStationLossCapacity(loss, Definitions, [], new(30), new(30))!;
        source.ReleaseCapacity(retained.Id);
        source.ReserveCapacity(retained.Id, retained.Quantity, retained.Owner);

        Assert.False(loss.CanApply());
        Assert.False(source.CanCommitStationLossCapacity(plan));
    }

    private static Inventory Source(ulong id = 1) => new(new(id),
        new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(new(id)), new(1)), new(100));

    private static PreparedCargoSourceLoss Loss(Inventory source, ulong units) => CargoSourceLoss.Prepare(source,
        [new PhysicalTransferSubject.Fungible(Ore.Key, new(units))], Definitions, []).Prepared!;
}
