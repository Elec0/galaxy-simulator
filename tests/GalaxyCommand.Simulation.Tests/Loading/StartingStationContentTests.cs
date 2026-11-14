using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StartingStationContentTests
{
    [Fact]
    public void PreviewLoadsThreeExplicitOperationalStationOriginsWithoutEconomyOrStartupCommands()
    {
        StaticNewGameLoadResult loaded = Load(PreviewDirectory(), 2);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Diagnostics));
        GameSessionSetup setup = loaded.Setup!;
        Assert.Null(setup.Economy);
        Assert.Equal(3, setup.Stations!.InitialStations.Count);
        Assert.Equal(3, setup.StationInventories.Count);
        var locations = new Dictionary<string, (string System, long X, long Y)>(StringComparer.Ordinal)
        {
            ["aster-relay"] = ("Aster", 80, 60),
            ["borealis-harbor"] = ("Borealis", -80, 80),
            ["cinder-beacon"] = ("Cinder", 0, 60),
        };
        Assert.All(setup.Stations.InitialStations, station =>
        {
            var expected = locations[station.DefinitionKey.LocalId.Value];
            SystemId system = setup.Systems.Single(item => item.Name == expected.System).Id;
            Assert.Equal(new SystemPosition(system, new(new(expected.X), new(expected.Y))), station.Position);
            Assert.Equal(SimulationTime.Zero, station.CreatedAt);
            Assert.Empty(station.Modules);
            Assert.Equal(setup.Relationships.PlayerPrincipalId, station.PrincipalId);
            Assert.True(station.EntityId.Value > setup.Ships.Max(ship => ship.EntityId.Value));
            Assert.True(station.InventoryId.Value > setup.Ships.Max(ship => ship.CargoInventoryId.Value));
            InitialStationInventorySetup inventory = Assert.Single(setup.StationInventories, item => item.Id == station.InventoryId);
            Assert.Equal(station.EntityId, inventory.StationEntityId);
            Assert.Equal(station.PrincipalId, inventory.PrincipalId);
            Assert.Equal(Quantity.Zero, inventory.Capacity);
        });

        using var session = new GameSession(setup, Navigation());
        Assert.Empty(session.CommandRecords);
        Assert.Empty(session.CaptureDialogue());
        GameSnapshot world = session.CaptureSnapshot();
        Assert.Equal(3, world.Stations.Count);
        Assert.All(world.Stations, station =>
        {
            Assert.Equal(StationPhase.Operational, station.Phase);
            Assert.Empty(station.Modules);
            Assert.Empty(station.Projects);
            Assert.Equal(0UL, station.SensorRadiusUnits);
        });
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        foreach (InitialStationInventorySetup expected in setup.StationInventories)
        {
            InventoryCheckpoint actual = Assert.Single(checkpoint.Lifecycle.Inventories.Inventories, item => item.Id == expected.Id);
            Assert.Equal(new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(expected.StationEntityId), expected.PrincipalId), actual.Custody);
            Assert.Equal(Quantity.Zero, actual.Capacity);
            Assert.Empty(actual.FungibleHoldings);
            Assert.Empty(actual.DiscreteItems);
        }
    }

    [Fact]
    public void StartingStationAndInventoryIdsIgnoreArrayOrderAndLoaderWorkerCount()
    {
        using var package = new Package();
        StaticNewGameLoadResult first = Load(package.Root, 1);
        JsonArray stations = package.Scenario["values"]!["startingStations"]!.AsArray();
        package.Scenario["values"]!["startingStations"] = new JsonArray(stations.Reverse().Select(value => value!.DeepClone()).ToArray());
        package.Write();
        StaticNewGameLoadResult second = Load(package.Root, 3);
        Assert.True(first.IsSuccess, string.Join("; ", first.Diagnostics));
        Assert.True(second.IsSuccess, string.Join("; ", second.Diagnostics));
        Assert.Equal(first.Setup!.Stations!.InitialStations.Select(Identity), second.Setup!.Stations!.InitialStations.Select(Identity));
        Assert.Equal(first.Setup.StationInventories.Select(item => (item.Id, item.StationEntityId, item.PrincipalId, item.Capacity)),
            second.Setup.StationInventories.Select(item => (item.Id, item.StationEntityId, item.PrincipalId, item.Capacity)));
    }

    [Theory]
    [InlineData("duplicate-station")]
    [InlineData("duplicate-inventory")]
    [InlineData("unknown-principal")]
    [InlineData("unknown-definition")]
    [InlineData("wrong-definition-kind")]
    [InlineData("unknown-system")]
    [InlineData("capacity-mismatch")]
    [InlineData("negative-capacity")]
    [InlineData("modules")]
    [InlineData("contents")]
    [InlineData("missing-contents")]
    [InlineData("missing-modules")]
    [InlineData("unknown-field")]
    [InlineData("fractional-coordinate")]
    [InlineData("missing-runtime")]
    [InlineData("wrong-array-type")]
    [InlineData("placement")]
    public void MalformedStartingStationsRejectBeforePublishingAnySetup(string invalid)
    {
        using var package = new Package();
        JsonObject values = package.Scenario["values"]!.AsObject();
        JsonArray stations = values["startingStations"]!.AsArray();
        JsonObject station = stations[0]!.AsObject();
        switch (invalid)
        {
            case "duplicate-station": stations[1]!["id"] = station["id"]!.GetValue<string>(); break;
            case "duplicate-inventory": stations[1]!["inventory"]!["id"] = station["inventory"]!["id"]!.GetValue<string>(); break;
            case "unknown-principal": station["principal"] = "galaxy-command.preview/principal/missing"; break;
            case "unknown-definition": station["definition"] = "galaxy-command.preview/station/missing"; break;
            case "wrong-definition-kind": station["definition"] = "galaxy-command.preview/principal/player"; break;
            case "unknown-system": station["system"] = "missing"; break;
            case "capacity-mismatch": station["inventory"]!["capacity"] = 1; break;
            case "negative-capacity": station["inventory"]!["capacity"] = -1; break;
            case "modules": station["modules"] = new JsonArray(new JsonObject()); break;
            case "contents": station["inventory"]!["contents"] = new JsonArray(new JsonObject()); break;
            case "missing-contents": station["inventory"]!.AsObject().Remove("contents"); break;
            case "missing-modules": station.Remove("modules"); break;
            case "unknown-field": station["spawnCommand"] = "create-station-site"; break;
            case "fractional-coordinate": station["x"] = 0.5; break;
            case "missing-runtime": values.Remove("stationRuntime"); break;
            case "wrong-array-type": values["startingStations"] = "stations"; break;
            case "placement":
                string path = Path.Combine(package.Root, "stations.json");
                JsonObject definitions = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                definitions["definitions"]![0]!["values"]!["placementRules"]!["allowedSystems"] = new JsonArray("borealis");
                File.WriteAllText(path, definitions.ToJsonString());
                break;
        }
        package.Write();
        StaticNewGameLoadResult result = Load(package.Root, 2);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Setup);
        Assert.Null(result.Content);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.StartsWith("$scenario.values", diagnostic.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void BuiltInScenarioRetainsNoStartingStationsOrStandaloneStationInventories()
    {
        StaticNewGameLoadResult result = BuiltInNewGame.Load(Path.Combine(Repository(), "content", "built-in"), GameSessionTestFixture.RootSeed, 64, 1);
        Assert.True(result.IsSuccess);
        GameSessionSetup setup = result.Setup!;
        Assert.Empty(setup.StationInventories);
        Assert.Null(setup.Stations);
    }

    private static (StationId Id, EntityId Entity, InventoryId Inventory, PrincipalId Principal, QualifiedContentKey Definition, SystemPosition Position)
        Identity(InitialStationSetup station) => (station.Id, station.EntityId, station.InventoryId, station.PrincipalId, station.DefinitionKey, station.Position);
    private static DirectLocalNavigationPlanner Navigation() => new(new ChebyshevLocalTravelTimeEstimator(1));
    private static StaticNewGameLoadResult Load(string directory, int workers) => StaticNewGameLoader.Load([directory],
        PackageId.Create("galaxy-command.preview"), LocalContentId.Create("visual-preview"), GameSessionTestFixture.RootSeed, 256, workers, Navigation());
    private static string PreviewDirectory() => Path.Combine(Repository(), "content", "development", "galaxy-command.preview");
    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GalaxyCommand.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private sealed class Package : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"galaxy-starting-stations-{Guid.NewGuid():N}");
        internal JsonObject Scenario { get; }
        internal Package()
        {
            Directory.CreateDirectory(Root);
            string source = PreviewDirectory();
            JsonObject manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "package.json")))!.AsObject();
            File.Copy(Path.Combine(source, "package.json"), Path.Combine(Root, "package.json"));
            foreach (JsonNode? document in manifest["documents"]!.AsArray())
            {
                string path = document!["path"]!.GetValue<string>();
                File.Copy(Path.Combine(source, path), Path.Combine(Root, path));
            }
            Scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "visual-preview.json")))!.AsObject();
        }
        internal void Write() => File.WriteAllText(Path.Combine(Root, "visual-preview.json"), Scenario.ToJsonString());
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
