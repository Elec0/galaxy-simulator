using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StaticNewGameLoaderTests
{
    [Fact]
    public void BuiltInMinimalScenarioLoadsThroughProductionPipeline()
    {
        string packageDirectory = Path.Combine(
            FindRepositoryRoot(),
            "content",
            "built-in",
            "galaxy-command.core");

        StaticNewGameLoadResult result = StaticNewGameLoader.Load(
            [packageDirectory],
            PackageId.Create("galaxy-command.core"),
            LocalContentId.Create("minimal"),
            RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]),
            factRetentionCapacity: 1024,
            maximumDegreeOfParallelism: 2);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        GameSessionSetup setup = Assert.IsType<GameSessionSetup>(result.Setup);
        StarSystem system = Assert.Single(setup.Systems);
        Assert.Equal(new SystemId(1), system.Id);
        Assert.Equal("Initial System", system.Name);

        PrincipalDefinition principal = Assert.Single(setup.Relationships.Principals);
        Assert.Equal(new PrincipalId(1), principal.Id);
        Assert.Equal(new PrincipalContentId("player"), principal.ContentId);
        Assert.Equal("Player", principal.Name);
        Assert.Equal(principal.Id, setup.Relationships.PlayerPrincipalId);

        InitialShipSetup ship = Assert.Single(setup.Ships);
        Assert.Equal(new EntityId(1), ship.EntityId);
        Assert.Equal(new ShipId(1), ship.Id);
        Assert.Equal(new InventoryId(1), ship.CargoInventoryId);
        Assert.Equal(principal.Id, ship.PrincipalId);
        Assert.Equal(
            new SystemPosition(
                system.Id,
                new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0))),
            ship.Position);
        Assert.Equal("Starter Ship", ship.Design.Name);
        Assert.Equal(new ConstructionDesignId(1), ship.Design.Id);
        Assert.Equal(new Quantity(100), ship.Design.CargoCapacity);
        Assert.Equal(new CommandSourceId("local-player"), ship.BaseController.Id);

        StaticGalaxyLayoutEntry layout = Assert.Single(result.GalaxyLayout!);
        Assert.Equal(system.Id, layout.SystemId);
        Assert.Equal(0m, layout.X);
        Assert.Equal(0m, layout.Y);
    }

    [Fact]
    public void BuiltInEntryPointSelectsTheApprovedCoreScenario()
    {
        string builtInContentDirectory = Path.Combine(
            FindRepositoryRoot(),
            "content",
            "built-in");

        StaticNewGameLoadResult result = BuiltInNewGame.Load(
            builtInContentDirectory,
            RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]),
            factRetentionCapacity: 1024,
            maximumDegreeOfParallelism: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["galaxy-command.core"],
            result.Content!.PackageOrder.Select(packageId => packageId.Value));
        Assert.Equal("minimal", Assert.Single(result.Content.Scenarios).Id.Value);
    }

    [Fact]
    public void InvalidBuiltInScenarioPublishesNeitherContentNorSetup()
    {
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"galaxy-command-static-new-game-{Guid.NewGuid():N}");
        string packageDirectory = Path.Combine(temporaryRoot, "galaxy-command.core");
        Directory.CreateDirectory(packageDirectory);
        try
        {
            string sourceDirectory = Path.Combine(
                FindRepositoryRoot(),
                "content",
                "built-in",
                "galaxy-command.core");
            foreach (string fileName in new[] { "package.json", "definitions.json", "minimal.json" })
            {
                File.Copy(
                    Path.Combine(sourceDirectory, fileName),
                    Path.Combine(packageDirectory, fileName));
            }

            string scenarioPath = Path.Combine(packageDirectory, "minimal.json");
            File.WriteAllText(
                scenarioPath,
                File.ReadAllText(scenarioPath).Replace(
                    "\"system\": \"initial-system\"",
                    "\"system\": \"missing-system\"",
                    StringComparison.Ordinal));

            StaticNewGameLoadResult result = BuiltInNewGame.Load(
                temporaryRoot,
                RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]),
                factRetentionCapacity: 1024,
                maximumDegreeOfParallelism: 2);

            Assert.False(result.IsSuccess);
            Assert.Null(result.Content);
            Assert.Null(result.Setup);
            Assert.Null(result.GalaxyLayout);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Path.EndsWith(".system", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GalaxyCommand.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
