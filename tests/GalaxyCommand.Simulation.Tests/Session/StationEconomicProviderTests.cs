using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationEconomicProviderTests
{
    [Fact]
    public void ActivationIsPreparedWithoutAllocatingAndUsesTheActualSharedInventory()
    {
        var state = Create();
        var prepared = Assert.IsType<PreparedStationProviderChange>(Prepare(state.Owner, Module(new(3)), null, true, 0));
        Assert.Empty(state.Owner.CaptureStationFacilities());
        Assert.True(prepared.CanCommit());
        prepared.Apply();
        Assert.Single(state.Owner.CaptureStationFacilities());
        Assert.Equal(new InventoryId(7), state.Owner.CaptureStationFacilities()[0].InventoryId);
        var result = state.Owner.Runtime.Reconcile(SimulationTime.Zero, state.Timing);
        Assert.Equal(new SimulationTime(2_334), Assert.Single(result.Production.Commit.CompletionProposals).Timestamp);
        Assert.False(prepared.CanCommit());
    }

    [Fact]
    public void CompatibleUpgradePreservesExactWorkAndRejectsIncompatibleRecipeBeforeMutation()
    {
        var state = Create();
        var initial = Assert.IsType<PreparedStationProviderChange>(Prepare(state.Owner, Module(new(3)), null, true, 0));
        initial.Apply();
        state.Owner.Runtime.Reconcile(SimulationTime.Zero, state.Timing);
        Assert.Null(Prepare(state.Owner, Module(new(5), new(2)), initial.FacilityId, true, 101));
        var upgrade = Assert.IsType<PreparedStationProviderChange>(Prepare(state.Owner, Module(new(5)), initial.FacilityId, true, 101));
        Assert.Equal(new SimulationTime(2_334), Assert.Single(upgrade.Cancellations).Timestamp);
        Assert.Equal(new SimulationTime(1_441), Assert.Single(upgrade.Completions).Timestamp);
        upgrade.Apply();
        Assert.Equal(initial.FacilityId, upgrade.FacilityId);
    }

    [Fact]
    public void WorkflowLossInvalidatesOnlyExactInputClaimAfterCanonicalSourceLoss()
    {
        var state = Create();
        state.Inventory.Add(new(1), new(2));
        var module = new StationModuleDefinition(QualifiedContentKey.Create("test.dynamic-provider", "station-module", "waiting"),
            "Waiting", 1, new(0), 0, null, new([], new(1)), new(1), [], [], productionProvider:
            new StationProductionProvider(new([new(new MaterialId(1), new Quantity(3))], new(1), new(1), new(7)), new(3), false));
        var activation = Assert.IsType<PreparedStationProviderChange>(Prepare(state.Owner, module, null, true, 0));
        activation.Apply();
        state.Owner.Runtime.Reconcile(SimulationTime.Zero, state.Timing);
        Reservation copy = Assert.Single(state.Inventory.CaptureCheckpoint().Reservations);
        var release = new InventoryCommitmentRelease.Material(state.Inventory, state.Inventory.GetReservation(copy.Id)!);
        var loss = CargoSourceLoss.Prepare(state.Inventory,
            [new PhysicalTransferSubject.Fungible(state.Definition.Key, new(1))], new([state.Definition]),
            [new CargoSourceReservationCleanup.Legacy(copy.Id, copy.Owner)]).Prepared!;
        var effect = Assert.IsType<PreparedStationEconomyLoss>(state.Owner.PrepareStationDestruction(new(1), [], false, [release]));
        Assert.True(effect.CanCommit(loss));
        Assert.True(loss.Apply());
        effect.ApplyAfterSourceLoss(loss);
        state.Owner.Runtime.Reconcile(new(100), state.Timing);
        Assert.Equal(new Quantity(1), state.Inventory.Reserved(new(1)));
        Assert.Equal(new Quantity(1), state.Inventory.Stored(new(1)));
    }

    [Fact]
    public void DisableWithdrawsUnassignedDemandAndResumePublishesOnlyTheRetainedShortfall()
    {
        var state = Create();
        var module = new StationModuleDefinition(QualifiedContentKey.Create("test.dynamic-provider", "station-module", "waiting"),
            "Waiting", 1, new(0), 0, null, new([], new(1)), new(1), [], [], productionProvider:
            new StationProductionProvider(new([new(new MaterialId(1), new Quantity(3))], new(1), new(1), new(7)), new(3), false));
        var activation = Assert.IsType<PreparedStationProviderChange>(Prepare(state.Owner, module, null, true, 0));
        activation.Apply();
        state.Owner.Runtime.Reconcile(SimulationTime.Zero, state.Timing);
        Assert.Equal(new Quantity(3), Assert.Single(state.Owner.CaptureCheckpoint(state.Planner).Value!.Transport.Board.Demands)!.Remaining);
        Prepare(state.Owner, module, activation.FacilityId, false, 100)!.Apply();
        state.Owner.Runtime.Reconcile(new(100), state.Timing);
        Assert.All(state.Owner.CaptureCheckpoint(state.Planner).Value!.Transport.Board.Demands,
            demand => Assert.Equal(Quantity.Zero, demand!.Remaining));
        Prepare(state.Owner, module, activation.FacilityId, true, 200)!.Apply();
        state.Owner.Runtime.Reconcile(new(200), state.Timing);
        Assert.Equal(3m, state.Owner.CaptureCheckpoint(state.Planner).Value!.Transport.Board.Demands.Sum(demand => (decimal)demand!.Remaining.Units));
    }

    private static PreparedStationProviderChange? Prepare(SessionEconomyOwner owner, StationModuleDefinition module,
        FacilityId? facility, bool available, ulong at) => owner.PrepareStationProvider(new(1), new(10), new(7),
            new(1), GameSessionTestFixture.Position(0, 0), new(1), facility, module, available, new(at));

    private static StationModuleDefinition Module(Throughput throughput, MaterialId? output = null) => new(
        QualifiedContentKey.Create("test.dynamic-provider", "station-module", "factory"), "Factory", 1, new(0), 0,
        null, new([], new(1)), new(1), [], [], productionProvider:
            new StationProductionProvider(new([], output ?? new(1), new(1), new(7)), throughput, false));

    private static (SessionEconomyOwner Owner, TransportTiming Timing, Inventory Inventory, PhysicalDefinition Definition, ISpatialNavigationPlanner Planner) Create()
    {
        var definition = new PhysicalDefinition(QualifiedContentKey.Create("test.dynamic-provider", "material", "output"),
            PhysicalHoldingKind.Fungible, new(1));
        var materials = new MaterialInventoryCompatibilityMap([new(new MaterialId(1), definition)]);
        var lifecycle = new EntityLifecycleOwner(new(), new(), new(), [], materials);
        var inventory = new Inventory(new(7), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(10)), new(1)), new(20), materials);
        lifecycle.RegisterEconomyInventory(inventory);
        var planner = new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator());
        var navigation = new HierarchicalLogisticsNavigation(new Dictionary<LocationId, SystemPosition>(), planner);
        var timing = new TransportTiming(new(1), new(1), new(1));
        var setup = new GameSessionEconomySetup([], [], [], [], [], [], [], navigation, timing, materials);
        return (new SessionEconomyOwner(setup, lifecycle), timing, inventory, definition, planner);
    }
}
