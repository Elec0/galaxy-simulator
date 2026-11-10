using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StaticStationContentTests
{
    [Fact]
    public void LoadsStructuralDefinitionsWithOneCanonicalMaterialMapAndNoInitialStationsOrEconomy()
    {
        using var fixture = new StationContentFixture();
        StaticNewGameLoadResult result = fixture.Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(value => value.Message)));
        StationRuntimeSetup setup = result.Setup!.Stations!;
        Assert.Null(result.Setup.Economy);
        Assert.Empty(setup.InitialStations);
        Assert.Equal(new[] { StationContentFixture.MaterialA, StationContentFixture.MaterialZ },
            setup.Materials.Mappings.Values.Select(value => value.Key.ToString()));
        Assert.Equal(new MaterialId(1), setup.Materials.GetMaterialId(QualifiedContentKey.Parse(StationContentFixture.MaterialA)));
        StationDefinition station = Assert.Single(setup.Definitions.Stations);
        Assert.Equal(new Quantity(3), station.BaseRecipe.Inputs[new(1)]);
        Assert.True(station.PlacementRules.Allows(GameSessionTestFixture.Position(0, 0)));
        Assert.False(station.PlacementRules.Allows(GameSessionTestFixture.Position(5, 0)));
        var module = setup.Definitions.Modules.Single(value => value.Key.LocalId.Value == "module");
        Assert.Equal(new Work(9), Assert.Single(module.UpgradePaths).Recipe.RequiredWork);
        Assert.Equal(new Quantity(2), module.DismantleRecovery[new(2)]);
        Assert.Equal(100UL, setup.StorageLossPolicy!.CutoffDenominator);
    }

    [Theory]
    [InlineData("zero-work")]
    [InlineData("duplicate-material")]
    [InlineData("unlisted-material")]
    [InlineData("unknown-system")]
    [InlineData("invalid-region")]
    [InlineData("staging-deadlock")]
    [InlineData("non-unit-material")]
    [InlineData("invalid-cutoff")]
    [InlineData("unknown-module-field")]
    public void RejectsInvalidStructuralContentBeforePublishingSetup(string invalid)
    {
        using var fixture = new StationContentFixture();
        JsonObject station = fixture.Definition("station", "base");
        JsonObject module = fixture.Definition("station-module", "module");
        switch (invalid)
        {
            case "zero-work": station["values"]!["baseRecipe"]!["requiredWork"] = 0; break;
            case "duplicate-material": ((JsonArray)station["values"]!["baseRecipe"]!["inputs"]!).Add(
                JsonNode.Parse("{\"material\":\"" + StationContentFixture.MaterialA + "\",\"quantity\":1}")); break;
            case "unlisted-material": station["references"] = new JsonArray(StationContentFixture.Module); break;
            case "unknown-system": station["values"]!["placementRules"]!["allowedSystems"] = new JsonArray("missing"); break;
            case "invalid-region": station["values"]!["placementRules"]!["allowedRegions"]![0]!["radiusMeters"] = -1; break;
            case "staging-deadlock": station["values"]!["stagingCapacity"] = 0; break;
            case "non-unit-material": fixture.Definition("material", "a")["values"]!["capacityCost"] = 2; break;
            case "invalid-cutoff": fixture.Definition("station-storage-loss-policy", "loss")["values"]!["cutoffDenominator"] = 0; break;
            case "unknown-module-field": module["values"]!["guessedCapability"] = 1; break;
        }
        StaticNewGameLoadResult result = fixture.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Setup);
        Assert.NotEmpty(result.Diagnostics);
    }
}

/// <summary>Copies shipped content, adding only isolated authored test definitions and references.</summary>
internal sealed class StationContentFixture : IDisposable
{
    internal const string MaterialA = "galaxy-command.core/material/a";
    internal const string MaterialZ = "galaxy-command.core/material/z";
    internal const string Module = "galaxy-command.core/station-module/module";
    internal const string Upgrade = "galaxy-command.core/station-module/upgraded";
    internal const string Base = "galaxy-command.core/station/base";
    internal const string Loss = "galaxy-command.core/station-storage-loss-policy/loss";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"galaxy-command-station-content-{Guid.NewGuid():N}");
    internal JsonObject Definitions { get; }
    internal JsonObject Scenario { get; }
    internal string PackageDirectory => Path.Combine(_root, "galaxy-command.core");

    /// <summary>Creates isolated package files and selects structural definitions through their transitive references.</summary>
    internal StationContentFixture()
    {
        Directory.CreateDirectory(PackageDirectory);
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "content", "built-in"))) repository = repository.Parent;
        string source = Path.Combine(repository!.FullName, "content", "built-in", "galaxy-command.core");
        File.Copy(Path.Combine(source, "package.json"), Path.Combine(PackageDirectory, "package.json"));
        Definitions = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "definitions.json")))!.AsObject();
        Scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "minimal.json")))!.AsObject();
        foreach (string id in new[] { "z", "a" }) AddDefinition("material", id, [], "{\"holdingKind\":\"fungible\",\"capacityCost\":1}");
        AddDefinition("station-storage-loss-policy", "loss", [], "{\"behaviorVersion\":1,\"cutoffNumerator\":1,\"cutoffDenominator\":100}");
        AddDefinition("station-module", "upgraded", [MaterialA, MaterialZ], ModuleValues("[]"));
        AddDefinition("station-module", "module", [MaterialA, MaterialZ, Upgrade], ModuleValues(
            "[{\"target\":\"" + Upgrade + "\",\"recipe\":{\"inputs\":[],\"requiredWork\":9}}]"));
        AddDefinition("station", "base", [MaterialA, Module], """
            {"moduleCapacity":10,"stagingCapacity":3,"baseCapacity":20,"baseRecipe":{"inputs":[{"material":"galaxy-command.core/material/a","quantity":3}],"requiredWork":3},
            "sensorRadiusMeters":0,"allowedModules":["galaxy-command.core/station-module/module"],"dismantleWork":2,"dismantleRecovery":[],
            "placementRules":{"allowedSystems":["initial-system"],"allowedRegions":[{"system":"initial-system","x":0,"y":0,"radiusMeters":10}],
            "exclusionRegions":[{"system":"initial-system","x":5,"y":0,"radiusMeters":0}]}}
            """);
        ((JsonArray)Scenario["references"]!).Add(Base);
        ((JsonArray)Scenario["references"]!).Add(Loss);
        Scenario["values"]!["stationRuntime"] = JsonNode.Parse("{\"territoryPolicy\":\"absent\",\"storageLossPolicy\":\"" + Loss + "\"}");
    }

    /// <summary>Locates an authored test definition for a focused schema mutation.</summary>
    internal JsonObject Definition(string kind, string id) => ((JsonArray)Definitions["definitions"]!).Select(value => value!.AsObject())
        .Single(value => value["kind"]!.GetValue<string>() == kind && value["id"]!.GetValue<string>() == id);

    internal void AddDefinition(string kind, string id, IEnumerable<string> references, string values) =>
        ((JsonArray)Definitions["definitions"]!).Add(new JsonObject { ["kind"] = kind, ["id"] = id, ["fallback"] = id,
            ["references"] = new JsonArray(references.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()), ["values"] = JsonNode.Parse(values) });

    internal StaticNewGameLoadResult Load(ISpatialNavigationPlanner? navigation = null)
    {
        File.WriteAllText(Path.Combine(PackageDirectory, "definitions.json"), Definitions.ToJsonString());
        File.WriteAllText(Path.Combine(PackageDirectory, "minimal.json"), Scenario.ToJsonString());
        return StaticNewGameLoader.Load([PackageDirectory], PackageId.Create("galaxy-command.core"), LocalContentId.Create("minimal"),
            GameSessionTestFixture.RootSeed, 64, 2, navigation);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string ModuleValues(string upgrades) => "{\"capacityCost\":2,\"storageCapacity\":4,\"sensorRadiusMeters\":0,\"constructionRecipe\":{\"inputs\":[],\"requiredWork\":1},"
        + "\"dismantleWork\":1,\"dismantleRecovery\":[{\"material\":\"" + MaterialZ + "\",\"quantity\":2}],\"upgradePaths\":" + upgrades + "}";
}
