using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationDefinitionsTests
{
    private static readonly MaterialId Metal = new(1);

    [Fact]
    public void StationDefinitionsRetainExplicitValuesAndCopyCollections()
    {
        var modules = new List<QualifiedContentKey> { Key("station-module", "storage") };
        var recovery = new Dictionary<MaterialId, Quantity> { [Metal] = new(2) };
        var station = new StationDefinition(Key("station", "base"), "Base", 4, new(2), new(5),
            Recipe(), 123, modules, new(3), recovery);
        modules.Clear();
        recovery.Clear();
        Assert.Equal("Base", station.InvariantFallback);
        Assert.Equal(123UL, station.SensorRadiusUnits);
        Assert.Single(station.AllowedModules);
        Assert.Equal(new Quantity(2), station.DismantleRecovery[Metal]);
    }

    [Fact]
    public void InvalidKindsWorkAndRecoveryRejectDefinitions()
    {
        Assert.Throws<ArgumentException>(() => Station("station-module"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Station(recipe: new([], new(0))));
        Assert.Throws<ArgumentException>(() => Station(recovery: new Dictionary<MaterialId, Quantity> { [Metal] = new(0) }));
        Assert.Throws<ArgumentException>(() => Station(staging: 6));
    }

    [Fact]
    public void ModuleCapabilitiesAndUpgradeReferencesAreExplicit()
    {
        var upgrade = Key("station-module", "larger-storage");
        var module = Module(upgrades: [upgrade]);
        Assert.Equal(2UL, module.CapacityCost);
        Assert.Equal(new Quantity(10), module.StorageCapacity);
        Assert.Equal(456UL, module.SensorRadiusUnits);
        Assert.Equal(7UL, module.ConstructionCapability!.Throughput.UnitsPerSecond);
        Assert.Equal(upgrade, Assert.Single(module.AllowedUpgrades));
    }

    [Fact]
    public void CatalogRejectsMissingOrSelfUpgradeAndMissingAllowedModule()
    {
        Assert.Throws<ArgumentException>(() => new StationDefinitionCatalog([Station()], []));
        Assert.Throws<ArgumentException>(() => new StationDefinitionCatalog([Station()],
            [Module(upgrades: [Key("station-module", "missing")])]));
        Assert.Throws<ArgumentException>(() => new StationDefinitionCatalog([Station()],
            [Module(upgrades: [Key("station-module", "storage")])]));
        var catalog = new StationDefinitionCatalog([Station()], [Module()]);
        Assert.Equal(Station().Key, Assert.Single(catalog.Stations).Key);
    }

    [Fact]
    public void SessionAdmissionRejectsUnresolvedMaterialsAndStagingDeadlocks()
    {
        var catalog = new StationDefinitionCatalog([Station()], [Module()]);
        var absent = new MaterialInventoryCompatibilityMap([]);
        Assert.Throws<ArgumentException>(() => catalog.ValidateForSession(absent, [new(1)]));
        var material = new PhysicalDefinition(Key("material", "metal"), PhysicalHoldingKind.Fungible, new(1));
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = material });
        catalog.ValidateForSession(materials, [new(1)]);
        var blocked = new StationDefinitionCatalog([Station(staging: 0)], [Module()]);
        Assert.Throws<ArgumentException>(() => blocked.ValidateForSession(materials, [new(1)]));
    }

    [Fact]
    public void ExplicitPlacementCirclesUseExactBoundariesAndSystemReferences()
    {
        var origin = new SystemPosition(new(1), new(new(0), new(0)));
        var allowed = new StationPlacementRegion(origin, 5);
        var excluded = new StationPlacementRegion(origin, 0);
        var rules = new StationPlacementRules([new(1)], [allowed], [excluded]);
        Assert.True(rules.Allows(new(new(1), new(new(3), new(4)))));
        Assert.False(rules.Allows(origin));
        Assert.False(rules.Allows(new(new(1), new(new(4), new(4)))));
        Assert.False(rules.Allows(new(new(2), new(new(3), new(4)))));
        var station = new StationDefinition(Key("station", "base"), "Base", 4, new(2), new(5),
            Recipe(), 123, [], new(3), [], rules);
        var catalog = new StationDefinitionCatalog([station], []);
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition>
            { [Metal] = new(Key("material", "metal"), PhysicalHoldingKind.Fungible, new(1)) });
        Assert.Throws<ArgumentException>(() => catalog.ValidateForSession(materials, [new(2)]));
        catalog.ValidateForSession(materials, [new(1)]);
    }

    [Fact]
    public void IdentityAllocatorsAndCapabilityRejectZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationModuleId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationProjectId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationConstructionCapability(0, default));
        Assert.Equal(new StationId(1), new IdSequence<StationId>().Allocate());
        Assert.Equal(new StationModuleId(1), new IdSequence<StationModuleId>().Allocate());
        Assert.Equal(new StationProjectId(1), new IdSequence<StationProjectId>().Allocate());
    }

    private static StationDefinition Station(string kind = "station", ConstructionRecipe? recipe = null,
        Dictionary<MaterialId, Quantity>? recovery = null, ulong staging = 2) =>
        new(Key(kind, "base"), "Base", 4, new(staging), new(5), recipe ?? Recipe(), 123,
            [Key("station-module", "storage")], new(3), recovery ?? new Dictionary<MaterialId, Quantity> { [Metal] = new(2) });

    private static StationModuleDefinition Module(IEnumerable<QualifiedContentKey>? upgrades = null) =>
        new(Key("station-module", "storage"), "Storage", 2, new(10), 456,
            new(100, new(7)), Recipe(), new(3), new Dictionary<MaterialId, Quantity> { [Metal] = new(2) }, upgrades ?? []);

    private static ConstructionRecipe Recipe() => new(new Dictionary<MaterialId, Quantity> { [Metal] = new(4) }, new(10));
    private static QualifiedContentKey Key(string kind, string id) => QualifiedContentKey.Create("test.stations", kind, id);
}
