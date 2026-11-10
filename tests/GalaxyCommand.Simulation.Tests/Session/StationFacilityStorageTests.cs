using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationFacilityStorageTests
{
    private static readonly MaterialId Ore = new(1);
    private static readonly InventoryId SharedInventory = new(2);
    private static readonly StationId Station = new(1);
    private static readonly EntityId StationEntity = new(10);
    private static readonly SystemPosition Position = GameSessionTestFixture.Position(0, 0);

    [Fact]
    public void StationFacilitiesShareOneInventoryAndStationCustody()
    {
        GameSessionEconomySetup setup = Seed([Facility(1, 1), Facility(2, 2)]);
        var lifecycle = Lifecycle();

        _ = new SessionEconomyOwner(setup, lifecycle);

        Assert.Single(lifecycle.Inventories.CaptureCheckpoint().Inventories);
        Inventory inventory = lifecycle.Inventories.Get(SharedInventory)!;
        Assert.Equal(SharedInventory, inventory.Id);
        Assert.Equal(new Quantity(5), inventory.Stored(Ore));
        Assert.Equal(new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(StationEntity), GameSessionTestFixture.Principal), inventory.Custody);
    }

    [Fact]
    public void SharedStockIsReservedOnceAcrossIndependentModuleWorkloads()
    {
        EconomyFacilitySetup[] facilities = [Facility(2, 2), Facility(1, 1)];
        GameSessionEconomySetup seed = Seed(facilities);
        var recipe = new Recipe([new(Ore, new Quantity(4))], Ore, new Quantity(1), new Work(1));
        var setup = new GameSessionEconomySetup(seed.Inventories, facilities,
            facilities.Select(facility => new ProductionFacilitySetup(facility.FacilityId, recipe, new Throughput(1), false)),
            [], [], [], [], seed.Navigation, seed.TransportTiming, seed.MaterialCompatibility);
        var lifecycle = Lifecycle();
        var economy = new SessionEconomyOwner(setup, lifecycle);

        EconomicReconciliationResult result = economy.Runtime.Reconcile(new SimulationTime(0), setup.TransportTiming);

        ProductionCompletionProposal completion = Assert.Single(result.Production.Commit.CompletionProposals);
        Assert.Equal(new FacilityId(1), completion.FacilityId);
        Inventory shared = lifecycle.Inventories.Get(SharedInventory)!;
        Assert.Equal(Quantity.Zero, shared.Available(Ore));
        Assert.Equal(new Quantity(1), shared.Stored(Ore));
        Assert.Equal(new Quantity(4), Assert.Single(result.Production.Commit.ConsumedInputs).Quantity);
        Assert.Equal(new Quantity(1), Assert.Single(shared.CaptureCheckpoint().Reservations).Quantity);
    }

    [Fact]
    public void SharedMappedInventoryRequiresExplicitStationLinkOnEveryFacility()
    {
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { StationBinding = null },
        ]));
    }

    [Fact]
    public void FacilitiesCannotShareAcrossStationsOrPrincipals()
    {
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { StationBinding = new(new StationId(2), new EntityId(11), new StationModuleId(2)) },
        ]));
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { ControllingPrincipalId = new PrincipalId(2) },
        ]));
    }

    [Fact]
    public void ModulesCannotHaveDuplicateFacilityBindings()
    {
        Assert.Throws<ArgumentException>(() => Seed([Facility(1, 1), Facility(2, 1)]));
    }

    [Fact]
    public void SharedStationFacilitiesMustRetainOnePosition()
    {
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { Position = GameSessionTestFixture.Position(1, 0) },
        ]));
    }

    [Fact]
    public void ExplicitStationFacilitiesShareOneLogisticsAnchorAcrossRestore()
    {
        EconomyFacilitySetup[] facilities = [Facility(1, 1), Facility(2, 2) with { LogisticsLocationId = new(1) }];
        var planner = new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator());
        var navigation = new HierarchicalLogisticsNavigation(
            new Dictionary<LocationId, SystemPosition> { [new(1)] = Position }, planner);
        GameSessionEconomySetup setup = Seed(facilities, navigation: navigation);
        var lifecycle = Lifecycle();
        var owner = new SessionEconomyOwner(setup, lifecycle);
        CheckpointResult<SessionEconomyCheckpoint> captured = owner.CaptureCheckpoint(planner);
        Assert.True(captured.IsSuccess, captured.Failure?.Message);
        CheckpointResult<SessionEconomyOwner> restored = SessionEconomyOwner.RestoreCheckpoint(captured.Value!, lifecycle,
            new WorldTopology([new StarSystem(GameSessionTestFixture.System, "Test")], new([], [])), planner, []);

        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(2, restored.Value!.CaptureStationFacilities().Count);
        Assert.True(restored.Value.CaptureCheckpoint(planner).IsSuccess);
    }

    [Fact]
    public void SharedLogisticsAnchorCannotCombineDifferentStations()
    {
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { InventoryId = new(3), LogisticsLocationId = new(1),
                StationBinding = new(new(2), new(11), new(2)) },
        ], [new InitialInventorySetup(new(3), new(20), [])]));
    }

    [Fact]
    public void OneStationCannotSplitItsInventoryOrEntityLink()
    {
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { InventoryId = new InventoryId(3) },
        ], [new InitialInventorySetup(new InventoryId(3), new Quantity(20), [])]));
        Assert.Throws<ArgumentException>(() => Seed([
            Facility(1, 1), Facility(2, 2) with { StationBinding = new(Station, new EntityId(11), new StationModuleId(2)) },
        ]));
    }

    [Fact]
    public void SharedStationBindingSurvivesEconomyCheckpointRestore()
    {
        var state = Capture();
        CheckpointResult<SessionEconomyOwner> restored = SessionEconomyOwner.RestoreCheckpoint(
            state.Checkpoint, state.Lifecycle, state.Topology, state.Planner, []);

        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal([new StationModuleId(1), new StationModuleId(2)],
            restored.Value!.CaptureStationFacilities().Select(facility => facility.StationBinding!.ModuleId));
        Assert.Equal(2, restored.Value.CaptureStationFacilities().Count);
        Assert.All(restored.Value.CaptureStationFacilities(), facility => Assert.Equal(SharedInventory, facility.InventoryId));
        Assert.Single(state.Lifecycle.Inventories.CaptureCheckpoint().Inventories);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-module")]
    [InlineData("wrong-entity")]
    public void RestoreRejectsMalformedSharedStationBinding(string fault)
    {
        var state = Capture();
        EconomyFacilityCheckpoint[] facilities = state.Checkpoint.Facilities.Select(facility => facility!).ToArray();
        facilities[1] = fault switch
        {
            "missing" => facilities[1] with { StationBinding = null },
            "duplicate-module" => facilities[1] with { StationBinding = facilities[0].StationBinding },
            _ => facilities[1] with { StationBinding = new(Station, new EntityId(11), new StationModuleId(2)) },
        };

        CheckpointResult<SessionEconomyOwner> restored = SessionEconomyOwner.RestoreCheckpoint(
            state.Checkpoint with { Facilities = facilities }, state.Lifecycle, state.Topology, state.Planner, []);

        Assert.False(restored.IsSuccess);
        Assert.Contains("$.checkpoint.economy.facilities", restored.Failure!.Path);
        Assert.Equal(new Quantity(5), state.Lifecycle.Inventories.Get(SharedInventory)!.Stored(Ore));
    }

    private static (SessionEconomyCheckpoint Checkpoint, EntityLifecycleOwner Lifecycle,
        WorldTopology Topology, ISpatialNavigationPlanner Planner) Capture()
    {
        EconomyFacilitySetup[] facilities = [Facility(1, 1), Facility(2, 2)];
        var planner = new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator());
        var navigation = new HierarchicalLogisticsNavigation(
            facilities.ToDictionary(facility => facility.LogisticsLocationId, facility => facility.Position), planner);
        var lifecycle = Lifecycle();
        var economy = new SessionEconomyOwner(Seed(facilities, navigation: navigation), lifecycle);
        SessionEconomyCheckpoint checkpoint = economy.CaptureCheckpoint(planner).Value!;
        return (checkpoint, lifecycle, new WorldTopology([new StarSystem(GameSessionTestFixture.System, "Test")],
            new ConnectorTopology([], [])), planner);
    }

    private static EconomyFacilitySetup Facility(ulong facility, ulong module) => new(
        new FacilityId(facility), SharedInventory, new LocationId(facility), Position,
        GameSessionTestFixture.Principal, new StationFacilityBinding(Station, StationEntity, new StationModuleId(module)));

    private static GameSessionEconomySetup Seed(IEnumerable<EconomyFacilitySetup> facilities,
        IEnumerable<InitialInventorySetup>? extraInventories = null,
        ILogisticsNavigation? navigation = null) => new(
        new[] { new InitialInventorySetup(SharedInventory, new Quantity(20), [new(Ore, new Quantity(5))]) }.Concat(extraInventories ?? []),
        facilities, [], [], [], [], [], navigation ?? new NoNavigation(),
        new TransportTiming(SimulationDuration.Zero, new TransferRate(1), new TransferRate(1)),
        new MaterialInventoryCompatibilityMap([
            new(Ore, new PhysicalDefinition(QualifiedContentKey.Create("test.stations", "material", "ore"),
                PhysicalHoldingKind.Fungible, new Quantity(1))),
        ]));

    private static EntityLifecycleOwner Lifecycle() => new(
        new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);

    private sealed class NoNavigation : ILogisticsNavigation
    {
        public LogisticsTravelEstimate? Estimate(ShipId actorId, LocationId origin, LocationId destination,
            SimulationTime plannedAt) => null;
    }
}
