using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InitialStationSetupTests
{
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.initial", "station", "base");
    private static readonly QualifiedContentKey ModuleKey = QualifiedContentKey.Create("test.initial", "station-module", "storage");

    [Fact]
    public void ExplicitInitialStationUsesExistingInventoryAndNoFabricatedProjects()
    {
        var state = Prepare();
        var owner = new StationRuntimeOwner(state.Setup, state.Lifecycle, new(), (_, _) => null);

        StationSnapshot station = Assert.Single(owner.Capture());
        Assert.Equal(new StationId(5), station.Id);
        Assert.Equal(new EntityId(7), station.EntityId);
        Assert.Equal(new InventoryId(1), station.InventoryId);
        Assert.Equal(StationPhase.Operational, station.Phase);
        Assert.Empty(station.Projects);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Assert.Equal(new StationModuleId(9), module.Id);
        Assert.Equal(StationModulePhase.Operational, module.Phase);
        Assert.Equal(new StationId(5), state.Lifecycle.Entities.GetStationId(new EntityId(7)));
        Assert.Same(state.Inventory, state.Lifecycle.Inventories.Get(station.InventoryId));
    }

    [Fact]
    public void InitialAdmissionValidatesWholeBatchBeforePublishingAnyIdentity()
    {
        var state = Prepare();
        InitialStationSetup first = Assert.Single(state.Setup.InitialStations);
        var invalid = new InitialStationSetup(new(8), new(6), new(2), GameSessionTestFixture.Principal,
            BaseKey, GameSessionTestFixture.Position(0, 0), [], SimulationTime.Zero);
        var setup = new StationRuntimeSetup(state.Setup.Definitions, state.Setup.Materials, StationTerritoryPolicy.Absent,
            initialStations: [first, invalid]);
        IdSequenceCheckpoint prior = state.Lifecycle.CaptureCheckpoint().EntityIds;

        Assert.Throws<ArgumentException>(() => new StationRuntimeOwner(setup, state.Lifecycle, new(), (_, _) => null));

        Assert.Null(state.Lifecycle.Entities.GetStationId(first.EntityId));
        Assert.Equal(prior, state.Lifecycle.CaptureCheckpoint().EntityIds);
    }

    [Fact]
    public void InitialIdentityAdvancesSharedAllocatorsWithoutAllocatingProjectHistory()
    {
        var state = Prepare();
        var setup = state.Setup.BindPlayer(GameSessionTestFixture.Player, GameSessionTestFixture.Principal);
        var owner = new StationRuntimeOwner(setup, state.Lifecycle, new(), (_, _) => new(
            GameSessionTestFixture.Principal, new(100, new(1)), GameSessionTestFixture.Position(0, 0), true));
        GameplayCommandHandlingResult result = owner.Handle(new(new(1), SimulationTime.Zero,
            GameSessionTestFixture.Player, new CreateStationSiteCommand(GameSessionTestFixture.Principal,
                new(1), BaseKey, GameSessionTestFixture.Position(0, 0), new(0))));

        Assert.Equal(CommandResultStatus.Accepted, result.Result.Status);
        StationSnapshot created = owner.Capture().Single(station => station.Id == new StationId(6));
        Assert.Equal(new EntityId(8), created.EntityId);
        Assert.Equal(new StationProjectId(1), Assert.Single(created.Projects).Id);
        Assert.Equal(new InventoryId(2), created.InventoryId);
    }

    private static (StationRuntimeSetup Setup, EntityLifecycleOwner Lifecycle, Inventory Inventory) Prepare()
    {
        var recipe = new ConstructionRecipe([], new(1));
        var module = new StationModuleDefinition(ModuleKey, "Storage", 1, new(5), 20, null,
            recipe, new(1), [], []);
        var definition = new StationDefinition(BaseKey, "Initial Station", 1, new(0), new(10), recipe,
            10, [ModuleKey], new(1), []);
        var materials = new MaterialInventoryCompatibilityMap([]);
        var initial = new InitialStationSetup(new(7), new(5), new(1), GameSessionTestFixture.Principal,
            BaseKey, GameSessionTestFixture.Position(0, 0), [new(new(9), ModuleKey, true, new FacilityId(1))], SimulationTime.Zero);
        var setup = new StationRuntimeSetup(new([definition], [module]), materials, StationTerritoryPolicy.Absent,
            initialStations: [initial]);
        var lifecycle = new EntityLifecycleOwner(new(), new(), new(), []);
        var inventory = new Inventory(new(1), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(initial.EntityId), initial.PrincipalId), new(15), materials);
        lifecycle.RegisterEconomyInventory(inventory);
        return (setup, lifecycle, inventory);
    }
}
