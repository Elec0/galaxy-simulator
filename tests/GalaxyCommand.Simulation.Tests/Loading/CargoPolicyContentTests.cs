using System.Text.Json.Nodes;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoPolicyContentTests
{
    [Fact]
    public void BuiltInScenarioSelectsExplicitApprovedCargoPolicy()
    {
        using var package = new CargoPackage();
        StaticNewGameLoadResult loaded = package.Load();
        Assert.True(loaded.IsSuccess, string.Join("\n", loaded.Diagnostics));
        CargoDispositionPolicy policy = Assert.IsType<CargoDispositionPolicy>(loaded.Setup!.CargoPolicy);
        Assert.Equal("galaxy-command.core/cargo-policy/default-cargo", policy.Key.ToString());
        Assert.Equal(new CargoRecoveryFraction(1, 2), policy.RecoveryFor(CargoLossCause.EntityDestruction));
        Assert.Equal(new CargoRecoveryFraction(1, 2), policy.RecoveryFor(CargoLossCause.StorageModuleLoss));
        Assert.Equal(new CargoRecoveryFraction(1, 1), policy.RecoveryFor(CargoLossCause.FunctionalEjection));
        Assert.Equal(4, policy.FungibleObjectCount);
        Assert.Equal(10000UL, policy.ScatterRadiusMeters);
        Assert.Equal(new SimulationDuration(63_072_000_000), policy.Lifetime);
        Assert.Equal(new SimulationDuration(1000), policy.MinimumCollectionDuration);
        Assert.Equal(new SimulationDuration(15000), policy.MaximumCollectionDuration);
        Assert.Equal(new Quantity(100), policy.CollectionStorageCap);
    }

    [Theory]
    [InlineData("generationBehaviorVersion", "2")]
    [InlineData("mergeBehaviorVersion", "0")]
    [InlineData("orderingBehaviorVersion", "2")]
    [InlineData("durationBehaviorVersion", "0")]
    [InlineData("fungibleObjectCount", "0")]
    [InlineData("fungibleObjectCount", "2147483648")]
    [InlineData("scatterRadiusMeters", "1125899906842625")]
    [InlineData("scatterRadiusMeters", "-1")]
    [InlineData("lifetimeMilliseconds", "0")]
    [InlineData("minimumCollectionDurationMilliseconds", "0")]
    [InlineData("maximumCollectionDurationMilliseconds", "999")]
    [InlineData("collectionStorageCap", "1")]
    [InlineData("collectionStorageCap", "1.5")]
    [InlineData("collectionStorageCap", "\"100\"")]
    [InlineData("lifetimeMilliseconds", null)]
    public void InvalidPolicyRejectsWholeSetup(string field, string? authored)
    {
        using var package = new CargoPackage();
        package.SetValue(field, authored);
        StaticNewGameLoadResult loaded = package.Load();
        Assert.False(loaded.IsSuccess);
        Assert.Null(loaded.Setup);
        Assert.Null(loaded.Content);
        Assert.NotEmpty(loaded.Diagnostics);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"cause\":\"unknown\",\"numerator\":1,\"denominator\":2}]")]
    [InlineData("[{\"cause\":\"entity-destruction\",\"numerator\":1,\"denominator\":0}]")]
    public void MissingUnknownOrInvalidCauseRejectsAdmission(string authored)
    {
        using var package = new CargoPackage();
        package.SetValue("recoveryByCause", authored);
        Assert.False(package.Load().IsSuccess);
    }

    [Fact]
    public void ScenarioDoesNotInferPolicyFromLoadedDefinitions()
    {
        using var package = new CargoPackage();
        package.SetScenarioValue("cargoPolicy", null);
        StaticNewGameLoadResult loaded = package.Load();
        Assert.True(loaded.IsSuccess, string.Join("\n", loaded.Diagnostics));
        Assert.Null(loaded.Setup!.CargoPolicy);
    }

    [Fact]
    public void PolicySelectorMustBeASelectedReferenceOfTheCorrectKind()
    {
        using var package = new CargoPackage();
        package.SetScenarioValue("cargoPolicy", "\"galaxy-command.core/standing-policy/default-standing\"");
        Assert.False(package.Load().IsSuccess);
    }

    private sealed class CargoPackage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"galaxy-command-cargo-policy-{Guid.NewGuid():N}");
        private readonly string _directory;

        internal CargoPackage()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalaxyCommand.slnx")))
                repository = repository.Parent;
            string source = Path.Combine(repository!.FullName, "content", "built-in", "galaxy-command.core");
            _directory = Path.Combine(_root, "galaxy-command.core");
            Directory.CreateDirectory(_directory);
            foreach (string name in new[] { "package.json", "definitions.json", "minimal.json" })
                File.Copy(Path.Combine(source, name), Path.Combine(_directory, name));
        }

        internal StaticNewGameLoadResult Load() => BuiltInNewGame.Load(_root, GameSessionTestFixture.RootSeed, 64, 2);

        internal void SetValue(string field, string? authored)
        {
            string path = Path.Combine(_directory, "definitions.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
            JsonObject values = root["definitions"]!.AsArray()
                .Single(definition => (string?)definition?["kind"] == "cargo-policy")!["values"]!.AsObject();
            if (authored is null) values.Remove(field);
            else values[field] = JsonNode.Parse(authored);
            File.WriteAllText(path, root.ToJsonString());
        }

        internal void SetScenarioValue(string field, string? authored)
        {
            string path = Path.Combine(_directory, "minimal.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
            JsonObject values = root["values"]!.AsObject();
            if (authored is null) values.Remove(field);
            else values[field] = JsonNode.Parse(authored);
            File.WriteAllText(path, root.ToJsonString());
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
