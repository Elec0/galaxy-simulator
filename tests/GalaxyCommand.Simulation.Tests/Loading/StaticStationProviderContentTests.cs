using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StaticStationProviderContentTests
{
    private const string Ship = "galaxy-command.core/ship-design/starter-ship";

    [Fact]
    public void LoadsExplicitBuilderMaterialsAndBothEconomicProvidersWithSharedCanonicalDefinitions()
    {
        using var fixture = new StationContentFixture();
        Configure(fixture);
        fixture.Definition("ship-design", "starter-ship")["values"]!["inputs"] = JsonNode.Parse(
            "[{\"material\":\"" + StationContentFixture.MaterialA + "\",\"quantity\":2}]");
        ((JsonArray)fixture.Definition("ship-design", "starter-ship")["references"]!).Add(StationContentFixture.MaterialA);
        var planner = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        StaticNewGameLoadResult result = fixture.Load(planner);
        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(value => value.Message)));
        var design = Assert.Single(result.Setup!.Ships).Design;
        Assert.Equal(new StationConstructionCapability(100, new(1_000)), design.StationConstructionCapability);
        Assert.Equal(new Quantity(2), design.Recipe.Inputs[new(1)]);
        var module = result.Setup.Stations!.Definitions.Modules.Single(value => value.Key.LocalId.Value == "module");
        Assert.Equal(new Throughput(1), module.ProductionProvider!.Throughput);
        Assert.False(module.ProductionProvider.Repeat);
        Assert.Equal(new MaterialId(2), module.ProductionProvider.Recipe.OutputMaterial);
        Assert.Same(design, Assert.Single(module.ShipConstructionProvider!.AllowedDesigns));
        Assert.Empty(result.Setup.Economy!.Inventories);
    }

    [Fact]
    public void PackageBuilderCreatesRealModuleProviderAndQueuesARealOwnedShip()
    {
        using var fixture = new StationContentFixture();
        Configure(fixture);
        fixture.Definition("station", "base")["values"]!["baseRecipe"] = JsonNode.Parse("{\"inputs\":[],\"requiredWork\":1}");
        var planner = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        StaticNewGameLoadResult loaded = fixture.Load(planner);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Diagnostics.Select(value => value.Message)));
        using var session = new GameSession(loaded.Setup!, planner);
        CommandSource source = loaded.Setup!.PlayerCommandSource;
        PrincipalId principal = loaded.Setup.Relationships.PlayerPrincipalId;
        void Accepted(GameplayCommand command) => Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(source, command).Result.Status);
        StationSnapshot Current() => Assert.Single(session.CaptureSnapshot().Stations);
        void Assign()
        {
            StationSnapshot station = Current();
            Accepted(new AssignStationBuilderCommand(principal, station.Id,
                station.Projects.Single(project => project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled)).Id,
                station.Revision, new(1), new(0)));
        }
        Accepted(new CreateStationSiteCommand(principal, new(1), QualifiedContentKey.Parse(StationContentFixture.Base),
            GameSessionTestFixture.Position(0, 0), new(0)));
        Assign();
        session.AdvanceTo(new(1));
        Accepted(new ConstructStationModuleCommand(principal, Current().Id, Current().Revision, QualifiedContentKey.Parse(StationContentFixture.Module)));
        Assign();
        session.AdvanceTo(new(2));
        StationSnapshot built = Current();
        StationModuleSnapshot module = Assert.Single(built.Modules);
        Assert.NotNull(module.FacilityId);
        Accepted(new EnqueueStationShipCommand(principal, built.Id, built.Revision, module.Id, module.FacilityId!.Value,
            Assert.Single(loaded.Setup.Ships).Design.Id));
        session.AdvanceTo(new(502));
        Assert.Equal(2, session.CaptureSnapshot().Ships.Count);
        session.AdvanceTo(new(3_002));
        InventoryCheckpoint storage = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(value => value.Id == built.InventoryId);
        Assert.Equal(new Quantity(2), Assert.Single(storage.FungibleHoldings).Quantity);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        restored.Value?.Dispose();
    }

    [Theory]
    [InlineData("zero-production-rate")]
    [InlineData("nonboolean-repeat")]
    [InlineData("missing-output-reference")]
    [InlineData("unknown-provider-field")]
    [InlineData("duplicate-design")]
    [InlineData("undeclared-design")]
    [InlineData("script-controller")]
    [InlineData("initial-order")]
    [InlineData("zero-builder-rate")]
    [InlineData("unknown-builder-field")]
    [InlineData("duplicate-input")]
    [InlineData("zero-input")]
    [InlineData("overflow-input-total")]
    public void RejectsMalformedProviderBuilderAndShipRecipeValues(string invalid)
    {
        using var fixture = new StationContentFixture();
        Configure(fixture);
        JsonObject module = fixture.Definition("station-module", "module");
        JsonObject ship = fixture.Definition("ship-design", "starter-ship");
        switch (invalid)
        {
            case "zero-production-rate": module["values"]!["productionProvider"]!["throughputUnitsPerSecond"] = 0; break;
            case "nonboolean-repeat": module["values"]!["productionProvider"]!["repeat"] = "false"; break;
            case "missing-output-reference": module["references"] = new JsonArray(StationContentFixture.MaterialA, StationContentFixture.Upgrade, Ship); break;
            case "unknown-provider-field": module["values"]!["productionProvider"]!["guessed"] = 1; break;
            case "duplicate-design": ((JsonArray)module["values"]!["shipConstructionProvider"]!["allowedDesigns"]!).Add(Ship); break;
            case "undeclared-design": module["references"] = new JsonArray(StationContentFixture.MaterialA, StationContentFixture.MaterialZ, StationContentFixture.Upgrade); break;
            case "script-controller": module["values"]!["shipConstructionProvider"]!["materializedShipController"]!["kind"] = "script"; break;
            case "initial-order": module["values"]!["shipConstructionProvider"]!["initialOrderPolicy"] = "guessed-order"; break;
            case "zero-builder-rate": ship["values"]!["stationConstructionCapability"]!["throughputUnitsPerSecond"] = 0; break;
            case "unknown-builder-field": ship["values"]!["stationConstructionCapability"]!["guessed"] = 1; break;
            default:
                ((JsonArray)ship["references"]!).Add(StationContentFixture.MaterialA);
                ((JsonArray)ship["references"]!).Add(StationContentFixture.MaterialZ);
                string entries = invalid switch
                {
                    "zero-input" => "{\"material\":\"" + StationContentFixture.MaterialA + "\",\"quantity\":0}",
                    "overflow-input-total" => "{\"material\":\"" + StationContentFixture.MaterialA + "\",\"quantity\":18446744073709551615},{\"material\":\"" + StationContentFixture.MaterialZ + "\",\"quantity\":1}",
                    _ => "{\"material\":\"" + StationContentFixture.MaterialA + "\",\"quantity\":1},{\"material\":\"" + StationContentFixture.MaterialA + "\",\"quantity\":1}",
                };
                ship["values"]!["inputs"] = JsonNode.Parse("[" + entries + "]");
                break;
        }
        var planner = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        StaticNewGameLoadResult result = fixture.Load(planner);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Setup);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData("cargoCapacity")]
    [InlineData("requiredWork")]
    [InlineData("stationConstructionCapability")]
    public void SelectedUnusedShipDefinitionCannotSilentlyDropInvalidRequiredOrOptionalValues(string field)
    {
        using var fixture = new StationContentFixture();
        JsonObject values = fixture.Definition("ship-design", "starter-ship")["values"]!.DeepClone().AsObject();
        if (field == "stationConstructionCapability") values[field] = new JsonObject { ["rangeMeters"] = 100, ["throughputUnitsPerSecond"] = 0 };
        else values[field] = 0;
        fixture.AddDefinition("ship-design", "unused", [], values.ToJsonString());
        ((JsonArray)fixture.Scenario["references"]!).Add("galaxy-command.core/ship-design/unused");
        StaticNewGameLoadResult result = fixture.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Setup);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Source == "galaxy-command.core/ship-design/unused");
    }

    private static void Configure(StationContentFixture fixture)
    {
        fixture.Definition("ship-design", "starter-ship")["values"]!["stationConstructionCapability"] =
            JsonNode.Parse("{\"rangeMeters\":100,\"throughputUnitsPerSecond\":1000}");
        JsonObject module = fixture.Definition("station-module", "module");
        ((JsonArray)module["references"]!).Add(Ship);
        module["values"]!["productionProvider"] = JsonNode.Parse("{\"inputs\":[],\"outputMaterial\":\"" + StationContentFixture.MaterialZ
            + "\",\"outputQuantity\":2,\"requiredWork\":3,\"throughputUnitsPerSecond\":1,\"repeat\":false}");
        module["values"]!["shipConstructionProvider"] = JsonNode.Parse("{\"allowedDesigns\":[\"" + Ship
            + "\"],\"throughputUnitsPerSecond\":2,\"materializedShipController\":{\"kind\":\"player\",\"source\":\"local-player\"},\"initialOrderPolicy\":\"no-initial-order\"}");
        fixture.Scenario["values"]!["stationRuntime"]!["economy"] = JsonNode.Parse(
            "{\"transportTiming\":{\"dockingOverheadMilliseconds\":1,\"loadingUnitsPerSecond\":1,\"unloadingUnitsPerSecond\":1}}");
    }
}
