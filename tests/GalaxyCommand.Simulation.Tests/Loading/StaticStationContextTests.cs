using System.Text.Json.Nodes;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StaticStationContextTests
{
    [Fact]
    public void ExplicitContextRetainsOneMaterialMapAndHostPlannerWithoutCreatingInitialEconomicState()
    {
        using var fixture = new StationContentFixture();
        AddEconomy(fixture);
        var planner = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));

        StaticNewGameLoadResult result = fixture.Load(planner);

        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(value => value.Message)));
        GameSessionEconomySetup economy = result.Setup!.Economy!;
        Assert.Same(result.Setup.Stations!.Materials, economy.MaterialCompatibility);
        Assert.Same(planner, Assert.IsType<HierarchicalLogisticsNavigation>(economy.Navigation).Planner);
        Assert.Equal(new TransportTiming(new(7), new(3), new(5)), economy.TransportTiming);
        Assert.Empty(economy.Inventories);
        Assert.Empty(economy.Facilities);
        Assert.Empty(economy.ProductionFacilities);
        Assert.Empty(economy.ConstructionFacilities);
        Assert.Empty(economy.ConstructionOrders);
        Assert.Empty(economy.Freighters);
        using var session = new GameSession(result.Setup, planner);
        Assert.Empty(session.CaptureSnapshot().Stations);
    }

    [Theory]
    [InlineData("missing-runtime")]
    [InlineData("runtime-scalar")]
    [InlineData("territory")]
    [InlineData("unknown-runtime-field")]
    [InlineData("missing-host")]
    [InlineData("zero-rate")]
    [InlineData("fractional-duration")]
    [InlineData("unknown-timing-field")]
    public void RejectsMissingOrMalformedExplicitContextBeforePublishingSetup(string invalid)
    {
        using var fixture = new StationContentFixture();
        var values = (JsonObject)fixture.Scenario["values"]!;
        JsonObject runtime = (JsonObject)values["stationRuntime"]!;
        AddEconomy(fixture);
        switch (invalid)
        {
            case "missing-runtime": values.Remove("stationRuntime"); break;
            case "runtime-scalar": values["stationRuntime"] = false; break;
            case "territory": runtime["territoryPolicy"] = "implicit-friendly"; break;
            case "unknown-runtime-field": runtime["startingStations"] = new JsonArray(); break;
            case "zero-rate": runtime["economy"]!["transportTiming"]!["loadingUnitsPerSecond"] = 0; break;
            case "fractional-duration": runtime["economy"]!["transportTiming"]!["dockingOverheadMilliseconds"] = 1.5m; break;
            case "unknown-timing-field": runtime["economy"]!["transportTiming"]!["guessedSpeed"] = 10; break;
        }
        StaticNewGameLoadResult result = fixture.Load(invalid == "missing-host" ? null
            : new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        Assert.False(result.IsSuccess);
        Assert.Null(result.Setup);
        Assert.NotEmpty(result.Diagnostics);
    }

    private static void AddEconomy(StationContentFixture fixture) => fixture.Scenario["values"]!["stationRuntime"]!["economy"] =
        JsonNode.Parse("""{"transportTiming":{"dockingOverheadMilliseconds":7,"loadingUnitsPerSecond":3,"unloadingUnitsPerSecond":5}}""");
}
