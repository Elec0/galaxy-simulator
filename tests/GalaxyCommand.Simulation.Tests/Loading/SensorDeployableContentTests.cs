using System.Text.Json.Nodes;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SensorDeployableContentTests
{
    [Fact]
    public void BuiltInContentProvidesBothSatelliteDefinitionsAndExplicitInitialCargo()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalaxyCommand.slnx")))
            repository = repository.Parent;
        StaticNewGameLoadResult loaded = BuiltInNewGame.Load(
            Path.Combine(repository!.FullName, "content", "built-in"), GameSessionTestFixture.RootSeed, 64, 2);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(2, loaded.Setup!.SensorDeployableDefinitions.Count);
        Assert.Collection(loaded.Setup.SensorDeployableDefinitions,
            definition => Assert.Equal(75_000UL, definition.SensorRadiusUnits),
            definition => Assert.Equal(30_000UL, definition.SensorRadiusUnits));
        Assert.All(loaded.Setup.SensorDeployableDefinitions, definition => Assert.Equal(new Quantity(1), definition.Physical.CapacityCost));
        Assert.Equal(3, Assert.Single(loaded.Setup.Ships).Cargo.Count);
    }

    [Fact]
    public void ExplicitSensorDeployableDefinitionLoadsThroughProductionAdmission()
    {
        using var package = new DeployablePackage();

        StaticNewGameLoadResult loaded = package.Load();

        Assert.True(loaded.IsSuccess, string.Join("\n", loaded.Diagnostics));
        SensorDeployableDefinition definition = Assert.Single(loaded.Setup!.SensorDeployableDefinitions);
        Assert.Equal(new Quantity(3), definition.Physical.CapacityCost);
        Assert.Equal(2500UL, definition.DeploymentRangeUnits);
        Assert.Equal(7000UL, definition.SensorRadiusUnits);
        Assert.Equal(PhysicalHoldingKind.Discrete, definition.Physical.HoldingKind);
    }

    [Theory]
    [InlineData("capacityCost", "0")]
    [InlineData("capacityCost", "-1")]
    [InlineData("deploymentRangeMeters", "-1")]
    [InlineData("sensorRadiusMeters", "1.5")]
    [InlineData("sensorRadiusMeters", "\"7000\"")]
    [InlineData("sensorRadiusMeters", null)]
    public void InvalidDeployableCapabilityRejectsTheWholeSetup(string field, string? authored)
    {
        using var package = new DeployablePackage();
        package.SetValue(field, authored);

        StaticNewGameLoadResult loaded = package.Load();

        Assert.False(loaded.IsSuccess);
        Assert.Null(loaded.Setup);
        Assert.Null(loaded.Content);
        Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Path.StartsWith("$definition.values", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("deploymentRangeMeters")]
    [InlineData("sensorRadiusMeters")]
    public void ExplicitZeroRangeRemainsAValidCapability(string field)
    {
        using var package = new DeployablePackage();
        package.SetValue(field, "0");

        Assert.True(package.Load().IsSuccess);
    }

    [Fact]
    public void InitialCargoCreatesDistinctInstancesInTheSessionInventory()
    {
        using var package = new DeployablePackage();
        package.SetCargo("galaxy-command.core/sensor-deployable/fixture-probe",
            "galaxy-command.core/sensor-deployable/fixture-probe");
        StaticNewGameLoadResult loaded = package.Load();
        Assert.True(loaded.IsSuccess, string.Join("\n", loaded.Diagnostics));
        using var session = new GameSession(loaded.Setup!,
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        GameSessionCheckpoint checkpoint = capture.Value!;
        InventoryCheckpoint cargo = Assert.Single(checkpoint.Lifecycle.Inventories.Inventories)!;
        Assert.Equal(2, cargo.DiscreteItems.Count);
        Assert.Equal(new[] { new ItemInstanceId(1), new ItemInstanceId(2) }, cargo.DiscreteItems.Select(item => item.Id));
        Assert.Equal<ulong?>(3, checkpoint.InventoryCommit!.ItemInstanceIds.NextValue);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;
        InventoryCheckpoint roundTripped = Assert.Single(resumed.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories);
        Assert.Equal(cargo.DiscreteItems, roundTripped.DiscreteItems);
    }

    [Fact]
    public void OverCapacityInitialCargoRejectsSetupWithoutThrowing()
    {
        using var package = new DeployablePackage();
        package.SetValue("capacityCost", "101");
        package.SetCargo("galaxy-command.core/sensor-deployable/fixture-probe");

        StaticNewGameLoadResult loaded = package.Load();

        Assert.False(loaded.IsSuccess);
        Assert.Null(loaded.Setup);
        Assert.NotEmpty(loaded.Diagnostics);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("5000", true)]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData(null, false)]
    public void ShipPickupRangeMustBeExplicitAndIndependent(string? value, bool accepted)
    {
        using var package = new DeployablePackage();
        package.SetShipValue("pickupRangeMeters", value);

        StaticNewGameLoadResult loaded = package.Load();

        Assert.Equal(accepted, loaded.IsSuccess);
    }

    private sealed class DeployablePackage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"galaxy-command-deployable-{Guid.NewGuid():N}");

        internal DeployablePackage()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalaxyCommand.slnx")))
                repository = repository.Parent;
            string source = Path.Combine(repository!.FullName, "content", "built-in", "galaxy-command.core");
            string directory = Path.Combine(_root, "galaxy-command.core");
            Directory.CreateDirectory(directory);
            foreach (string name in new[] { "package.json", "definitions.json", "minimal.json" })
                File.Copy(Path.Combine(source, name), Path.Combine(directory, name));

            JsonNode definitions = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "definitions.json")))!;
            JsonArray entries = definitions["definitions"]!.AsArray();
            foreach (JsonNode? definition in entries.Where(definition => (string?)definition?["kind"] == "sensor-deployable").ToArray())
                entries.Remove(definition);
            definitions["definitions"]!.AsArray().Add(JsonNode.Parse("""
                {
                  "kind": "sensor-deployable",
                  "id": "fixture-probe",
                  "fallback": "Fixture Probe",
                  "references": [],
                  "values": {
                    "capacityCost": 3,
                    "deploymentRangeMeters": 2500,
                    "sensorRadiusMeters": 7000
                  }
                }
                """));
            File.WriteAllText(Path.Combine(directory, "definitions.json"), definitions.ToJsonString());
            JsonNode scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "minimal.json")))!;
            JsonArray references = scenario["references"]!.AsArray();
            foreach (JsonNode? reference in references.Where(reference => reference!.GetValue<string>().Contains("/sensor-deployable/", StringComparison.Ordinal)).ToArray())
                references.Remove(reference);
            foreach (JsonNode? ship in scenario["values"]!["ships"]!.AsArray()) ship!.AsObject().Remove("cargo");
            scenario["references"]!.AsArray().Add("galaxy-command.core/sensor-deployable/fixture-probe");
            File.WriteAllText(Path.Combine(directory, "minimal.json"), scenario.ToJsonString());
        }

        internal StaticNewGameLoadResult Load() => BuiltInNewGame.Load(_root,
            GameSessionTestFixture.RootSeed, 64, maximumDegreeOfParallelism: 2);

        internal void SetValue(string field, string? authored)
        {
            string path = Path.Combine(_root, "galaxy-command.core", "definitions.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
            JsonObject values = root["definitions"]!.AsArray().Last()!["values"]!.AsObject();
            if (authored is null) values.Remove(field);
            else values[field] = JsonNode.Parse(authored);
            File.WriteAllText(path, root.ToJsonString());
        }

        internal void SetCargo(params string[] keys)
        {
            string path = Path.Combine(_root, "galaxy-command.core", "minimal.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
            root["values"]!["ships"]![0]!["cargo"] = new JsonArray(keys.Select(key => JsonValue.Create(key)).ToArray());
            File.WriteAllText(path, root.ToJsonString());
        }

        internal void SetShipValue(string field, string? authored)
        {
            string path = Path.Combine(_root, "galaxy-command.core", "definitions.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
            JsonObject values = root["definitions"]!.AsArray()
                .Single(definition => (string?)definition?["kind"] == "ship-design")!["values"]!.AsObject();
            if (authored is null) values.Remove(field);
            else values[field] = JsonNode.Parse(authored);
            File.WriteAllText(path, root.ToJsonString());
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
