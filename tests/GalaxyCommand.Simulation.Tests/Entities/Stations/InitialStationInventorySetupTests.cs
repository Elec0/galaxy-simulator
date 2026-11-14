using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InitialStationInventorySetupTests
{
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.stations", "station", "beacon");

    [Fact]
    public void AuthoredStationUsesOneEmptyOwnedInventoryWithoutAnEconomicFacility()
    {
        using GameSession session = CreateSession(Setup([Seed()]));
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(StationPhase.Operational, station.Phase);
        Assert.Empty(station.Projects);
        Assert.Empty(station.Modules);
        Assert.Empty(session.CommandRecords);
        Assert.Empty(session.ReadFactsAfter(null, 100).Facts);
        GameSessionCheckpoint checkpoint = Capture(session);
        Assert.Null(checkpoint.Economy);
        InventoryCheckpoint inventory = Assert.Single(checkpoint.Lifecycle.Inventories.Inventories,
            item => item.Id == station.InventoryId);
        var custody = Assert.IsType<InventoryCustody.PrincipalControlled>(inventory.Custody);
        Assert.Equal(new InventoryOwnerReference.SessionEntity(station.EntityId), custody.PhysicalOwner);
        Assert.Equal(station.PrincipalId, custody.ControllingPrincipalId);
        Assert.Equal(new Quantity(0), inventory.Capacity);
        Assert.Empty(inventory.FungibleHoldings);
        Assert.Empty(inventory.DiscreteItems);
        Assert.Single(checkpoint.Stations!.Setup.InitialStations);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("ship-inventory")]
    [InlineData("wrong-entity")]
    [InlineData("wrong-principal")]
    [InlineData("wrong-inventory")]
    [InlineData("wrong-capacity")]
    [InlineData("no-stations")]
    public void RejectsInvalidStationInventoryOriginsBeforeSessionPublication(string invalid)
    {
        InitialStationInventorySetup seed = invalid switch
        {
            "ship-inventory" => new(new InventoryId(1), new EntityId(2), new PrincipalId(1), new Quantity(0)),
            "wrong-entity" => new(new InventoryId(2), new EntityId(99), new PrincipalId(1), new Quantity(0)),
            "wrong-principal" => new(new InventoryId(2), new EntityId(2), new PrincipalId(99), new Quantity(0)),
            "wrong-inventory" => new(new InventoryId(99), new EntityId(2), new PrincipalId(1), new Quantity(0)),
            "wrong-capacity" => new(new InventoryId(2), new EntityId(2), new PrincipalId(1), new Quantity(1)),
            _ => Seed(),
        };
        Assert.Throws<ArgumentException>(() => Setup(invalid == "duplicate" ? [seed, seed] : [seed],
            omitStations: invalid == "no-stations"));
    }

    [Fact]
    public void SeedCollectionIsCopiedAndDefaultIdsAreRejected()
    {
        InitialStationInventorySetup[] seeds = [Seed()];
        GameSessionSetup setup = Setup(seeds);
        seeds[0] = new(new InventoryId(9), new EntityId(9), new PrincipalId(1), new Quantity(0));
        Assert.Equal(new InventoryId(2), Assert.Single(setup.StationInventories).Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InitialStationInventorySetup(default, new EntityId(2), new PrincipalId(1), new Quantity(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InitialStationInventorySetup(new InventoryId(2), default, new PrincipalId(1), new Quantity(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InitialStationInventorySetup(new InventoryId(2), new EntityId(2), default, new Quantity(0)));
    }

    [Fact]
    public void RestoreDoesNotReseedInventoriesAndAllocatorsSkipAuthoredIdentities()
    {
        using GameSession original = CreateSession(Setup([Seed()]));
        CheckpointResult<GameSession> admission = GameSession.RestoreCheckpoint(Capture(original));
        Assert.True(admission.IsSuccess, admission.Failure?.Message);
        using GameSession restored = admission.Value!;
        Assert.Equal(2, Capture(restored).Lifecycle.Inventories.Inventories.Count);
        Assert.Single(restored.CaptureSnapshot().Stations);
        GameplayCommandRecord created = restored.SubmitCommand(GameSessionTestFixture.Player,
            new CreateStationSiteCommand(new PrincipalId(1), new ShipId(1), BaseKey,
                GameSessionTestFixture.Position(0, 0), new ActorControlRevision(0)));
        Assert.Equal(CommandResultStatus.Accepted, created.Result.Status);
        StationSnapshot next = restored.CaptureSnapshot().Stations.Single(station => station.Id != new StationId(1));
        Assert.Equal(new StationId(2), next.Id);
        Assert.Equal(new EntityId(3), next.EntityId);
        Assert.Equal(new InventoryId(3), next.InventoryId);
        Assert.Equal(new StationProjectId(1), Assert.Single(next.Projects).Id);
    }

    private static InitialStationInventorySetup Seed() => new(new InventoryId(2), new EntityId(2), new PrincipalId(1), new Quantity(0));

    private static GameSession CreateSession(GameSessionSetup setup) =>
        new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));

    private static GameSessionCheckpoint Capture(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> captured = session.CaptureCheckpoint();
        Assert.True(captured.IsSuccess, captured.Failure?.Message);
        return captured.Value!;
    }

    private static GameSessionSetup Setup(IEnumerable<InitialStationInventorySetup> inventories, bool omitStations = false)
    {
        var definition = new StationDefinition(BaseKey, "Beacon", 0, new Quantity(0), new Quantity(0),
            new ConstructionRecipe([], new Work(1)), 0, [], new Work(1), []);
        var station = new InitialStationSetup(new EntityId(2), new StationId(1), new InventoryId(2), new PrincipalId(1),
            BaseKey, GameSessionTestFixture.Position(100, 0), [], SimulationTime.Zero);
        var stations = new StationRuntimeSetup(new StationDefinitionCatalog([definition], []),
            new MaterialInventoryCompatibilityMap([]), StationTerritoryPolicy.Absent,
            initialStations: [station]);
        var design = new ShipDesign(new ConstructionDesignId(1), "Builder", new ConstructionRecipe([], new Work(1)), new Quantity(10),
            GameSessionTestFixture.ManeuverCapability, 10_000, 5_000, new StationConstructionCapability(0, new Throughput(1)));
        return new GameSessionSetup([new StarSystem(new SystemId(1), "System")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), new PrincipalId(1), design,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 128,
            GameSessionTestFixture.Player, stations: omitStations ? null : stations, stationInventories: inventories);
    }
}
