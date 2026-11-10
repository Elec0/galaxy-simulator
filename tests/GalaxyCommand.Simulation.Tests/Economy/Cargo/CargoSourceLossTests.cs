using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoSourceLossTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.loss", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinition Ice = new(QualifiedContentKey.Create("test.loss", "commodity", "ice"), PhysicalHoldingKind.Fungible, new(3));
    private static readonly PhysicalDefinition Module = new(QualifiedContentKey.Create("test.loss", "equipment", "module"), PhysicalHoldingKind.Discrete, new(7));
    private static readonly PhysicalDefinitionCatalog Definitions = new([Ore, Ice, Module]);
    private static readonly ReservationOwner Owner = new ReservationOwner.TransportJob(new(1));

    [Fact]
    public void PreparationGroupsCanonicallyAndPreservesOriginalInstancesWithoutMutation()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.StoreFungible(Ice, new(4));
        source.StoreDiscrete(Module, new(9));
        source.StoreDiscrete(Module, new(3));
        DiscreteItemInstance original = source.GetDiscrete(new(9))!;
        CargoSourceLossPreparation preparation = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9)), new PhysicalTransferSubject.Fungible(Ore.Key, new(2)),
                new PhysicalTransferSubject.Fungible(Ice.Key, new(1)), new PhysicalTransferSubject.Fungible(Ore.Key, new(3)),
                new PhysicalTransferSubject.Discrete(new(3))], Definitions, []);

        PreparedCargoSourceLoss plan = Assert.IsType<PreparedCargoSourceLoss>(preparation.Prepared);
        Assert.Same(source, plan.Source);
        Assert.Null(preparation.Failure);
        Assert.Equal(new Quantity(46), source.UsedCapacity);
        Assert.Equal(new Quantity(27), plan.RemovedCapacity);
        Assert.Equal(new[] { Ice.Key, Ore.Key, Module.Key }, plan.Groups.Select(group => group.Definition.Key));
        Assert.Equal(new Quantity(5), plan.Groups.Single(group => group.Definition.Key == Ore.Key).Quantity);
        CargoSourceLossGroup discrete = plan.Groups.Single(group => group.Definition.Key == Module.Key);
        Assert.Equal(new[] { new ItemInstanceId(3), new ItemInstanceId(9) }, discrete.DiscreteItems.Select(item => item.Id));
        Assert.Same(original, discrete.DiscreteItems[1]);
        Assert.True(plan.CanApply());
        Assert.True(plan.Apply());
        Assert.Equal(new Quantity(19), source.UsedCapacity);
        Assert.Equal(new Quantity(5), source.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(3), source.FungibleStored(Ice.Key));
        Assert.Null(source.GetDiscrete(new(9)));
        Assert.False(plan.Apply());
    }

    [Fact]
    public void ExplicitPhysicalCleanupReleasesOnlyApprovedCommitments()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.StoreFungible(Ice, new(4));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(8)), Owner);
        source.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ice.Key, new(2)), Owner);
        source.ReservePhysical(new(3), new PhysicalReservationSubject.IncomingCapacity(new(5)), Owner);
        var selected = new PhysicalTransferSubject[] { new PhysicalTransferSubject.Fungible(Ore.Key, new(5)) };
        Assert.Equal(CargoSourceLossFailure.CommitmentConflict, CargoSourceLoss.Prepare(source, selected, Definitions, []).Failure);

        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(source, selected, Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), Owner)]).Prepared!;
        Assert.NotNull(source.GetPhysicalReservation(new(1)));
        Assert.True(plan.Apply());
        Assert.Null(source.GetPhysicalReservation(new(1)));
        Assert.NotNull(source.GetPhysicalReservation(new(2)));
        Assert.NotNull(source.GetPhysicalReservation(new(3)));
        Assert.Equal(new Quantity(2), source.FungibleReserved(Ice.Key));
        Assert.Equal(new Quantity(5), source.ReservedCapacity);
    }

    [Fact]
    public void UnaffectedReservationsRemainWhenLossFitsAvailableStock()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(6)), Owner);
        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(4))], Definitions, []).Prepared!;
        Assert.True(plan.Apply());
        Assert.Equal(new Quantity(6), source.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(6), source.FungibleReserved(Ore.Key));
        Assert.NotNull(source.GetPhysicalReservation(new(1)));
    }

    [Fact]
    public void LegacyMappedReservationsRequireExactAuthorizedCleanup()
    {
        var material = new MaterialId(1);
        var mapped = new PhysicalDefinition(Ore.Key, PhysicalHoldingKind.Fungible, new(1));
        var compatibility = new MaterialInventoryCompatibilityMap([new(material, mapped)]);
        var source = new Inventory(new(1), Custody(), new(100), compatibility);
        source.Add(material, new(10));
        source.Reserve(new(1), material, new(8), Owner);
        var selected = new PhysicalTransferSubject[] { new PhysicalTransferSubject.Fungible(Ore.Key, new(5)) };
        var definitions = new PhysicalDefinitionCatalog([mapped]);
        Assert.Equal(CargoSourceLossFailure.CommitmentConflict, CargoSourceLoss.Prepare(source, selected, definitions, []).Failure);
        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(source, selected, definitions,
            [new CargoSourceReservationCleanup.Legacy(new(1), Owner)]).Prepared!;
        Assert.True(plan.Apply());
        Assert.Null(source.GetReservation(new(1)));
        Assert.Equal(new Quantity(5), source.Stored(material));
        Assert.Equal(Quantity.Zero, source.Reserved(material));
        Assert.Equal(new Quantity(5), source.UsedCapacity);
    }

    [Fact]
    public void AnyChangedHoldingsOrCommitmentsRejectApplyWithoutPartialCleanup()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(8)), Owner);
        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(5))], Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), Owner)]).Prepared!;
        source.StoreFungible(Ice, new(1));
        Assert.False(plan.CanApply());
        Assert.False(plan.Apply());
        Assert.NotNull(source.GetPhysicalReservation(new(1)));
        Assert.Equal(new Quantity(10), source.FungibleStored(Ore.Key));
    }

    [Fact]
    public void ChangedSelectedInstanceReferenceRejectsEvenWhenCheckpointValuesMatch()
    {
        Inventory source = Source();
        source.StoreDiscrete(Module, new(9));
        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9))], Definitions, []).Prepared!;
        source.ApplyRemoveDiscrete(new(9), Module.CapacityCost);
        source.StoreDiscrete(Module, new(9));
        Assert.False(plan.CanApply());
        Assert.False(plan.Apply());
        Assert.NotNull(source.GetDiscrete(new(9)));
    }

    [Fact]
    public void InvalidSelectionsAndCleanupRejectWithoutMutation()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(10));
        source.StoreDiscrete(Module, new(9));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Discrete(new(9)), Owner);
        var wrongOwner = new ReservationOwner.TransportJob(new(2));
        Assert.Equal(CargoSourceLossFailure.DuplicateInstance, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9)), new PhysicalTransferSubject.Discrete(new(9))], Definitions, []).Failure);
        Assert.Equal(CargoSourceLossFailure.MissingHolding, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(99))], Definitions, []).Failure);
        Assert.Equal(CargoSourceLossFailure.InsufficientQuantity, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(11))], Definitions, []).Failure);
        Assert.Equal(CargoSourceLossFailure.MissingDefinition, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(1))], new PhysicalDefinitionCatalog([]), []).Failure);
        Assert.Equal(CargoSourceLossFailure.InvalidCleanup, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9))], Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), wrongOwner)]).Failure);
        Assert.Equal(CargoSourceLossFailure.InvalidCleanup, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9))], Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), Owner), new CargoSourceReservationCleanup.Physical(new(1), Owner)]).Failure);
        Assert.Equal(CargoSourceLossFailure.CommitmentConflict, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9))], Definitions, []).Failure);
        Assert.NotNull(source.GetDiscrete(new(9)));
        Assert.NotNull(source.GetPhysicalReservation(new(1)));
        Assert.Equal(new Quantity(10), source.FungibleStored(Ore.Key));
    }

    [Fact]
    public void IncomingCapacityCannotBeSilentlyCancelledAsSourceCleanup()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(2));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(5)), Owner);
        Assert.Equal(CargoSourceLossFailure.InvalidCleanup, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(1))], Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), Owner)]).Failure);
        Assert.Equal(new Quantity(5), source.ReservedCapacity);
    }

    [Fact]
    public void CatalogCapacityMismatchRejectsBeforePreparingRemoval()
    {
        Inventory source = Source();
        source.StoreFungible(Ore, new(2));
        var incompatible = new PhysicalDefinitionCatalog([new PhysicalDefinition(Ore.Key, PhysicalHoldingKind.Fungible, new(3))]);
        Assert.Equal(CargoSourceLossFailure.CapacityMismatch, CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(1))], incompatible, []).Failure);
        Assert.Equal(new Quantity(4), source.UsedCapacity);
    }

    [Fact]
    public void GroupedQuantityOverflowRejectsWithoutRemovingAnySourceStock()
    {
        var definition = new PhysicalDefinition(Ore.Key, PhysicalHoldingKind.Fungible, new(1));
        var source = new Inventory(new(1), Custody(), new(ulong.MaxValue));
        source.StoreFungible(definition, new(ulong.MaxValue));
        CargoSourceLossPreparation result = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(ulong.MaxValue)), new PhysicalTransferSubject.Fungible(Ore.Key, new(1))],
            new PhysicalDefinitionCatalog([definition]), []);
        Assert.Equal(CargoSourceLossFailure.ArithmeticOverflow, result.Failure);
        Assert.Null(result.Prepared);
        Assert.Equal(new Quantity(ulong.MaxValue), source.FungibleStored(Ore.Key));
    }

    [Fact]
    public void SelectedReservedInstanceCanLeaveOnlyWithItsExactCleanup()
    {
        Inventory source = Source();
        source.StoreDiscrete(Module, new(9));
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Discrete(new(9)), Owner);
        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9))], Definitions,
            [new CargoSourceReservationCleanup.Physical(new(1), Owner)]).Prepared!;
        Assert.Same(source.GetDiscrete(new(9)), Assert.Single(Assert.Single(plan.Groups).DiscreteItems));
        Assert.True(plan.Apply());
        Assert.Null(source.GetPhysicalReservation(new(1)));
        Assert.Null(source.GetDiscrete(new(9)));
        Assert.False(source.HasCommitments);
        Assert.Equal(Quantity.Zero, source.UsedCapacity);
    }

    [Fact]
    public void EmptySelectionProducesAnExplicitNoLossPlan()
    {
        PreparedCargoSourceLoss plan = CargoSourceLoss.Prepare(Source(), [], Definitions, []).Prepared!;
        Assert.Empty(plan.Groups);
        Assert.Equal(Quantity.Zero, plan.RemovedCapacity);
        Assert.True(plan.Apply());
        Assert.False(plan.Apply());
    }

    private static Inventory Source() => new(new(1), Custody(), new(100));
    private static InventoryCustody.PrincipalControlled Custody() => new(new InventoryOwnerReference.SessionEntity(new(1)), new(4));
}
