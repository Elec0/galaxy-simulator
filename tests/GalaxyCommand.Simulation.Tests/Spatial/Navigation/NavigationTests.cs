using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class NavigationTests
{
    [Fact]
    public void BidirectionalHelperCreatesDistinctDirectedRoutes()
    {
        LocationId[] locations = CreateLocations(2);
        var graph = CreateGraph(locations);

        (RouteId forward, RouteId reverse) = graph.AddBidirectionalRoutes(
            locations[0],
            locations[1],
            new SimulationDuration(10));

        Assert.NotEqual(forward, reverse);
        Assert.Equal(locations[0], graph.GetRoute(forward)?.Origin);
        Assert.Equal(locations[1], graph.GetRoute(reverse)?.Origin);
    }

    [Fact]
    public void ShortestEnabledPathWins()
    {
        LocationId[] locations = CreateLocations(3);
        var graph = CreateGraph(locations);
        RouteId direct = graph.AddRoute(locations[0], locations[2], new SimulationDuration(30));
        RouteId firstLeg = graph.AddRoute(locations[0], locations[1], new SimulationDuration(10));
        RouteId secondLeg = graph.AddRoute(locations[1], locations[2], new SimulationDuration(10));

        RoutePlan plan = Assert.IsType<RoutePlan>(graph.FindRoute(locations[0], locations[2]));

        Assert.Equal([firstLeg, secondLeg], plan.RouteIds);
        Assert.Equal(new SimulationDuration(20), plan.TotalDuration);
        Assert.DoesNotContain(direct, plan.RouteIds);
    }

    [Fact]
    public void DisabledRouteIsExcludedFromNewPlans()
    {
        LocationId[] locations = CreateLocations(3);
        var graph = CreateGraph(locations);
        RouteId direct = graph.AddRoute(locations[0], locations[2], new SimulationDuration(10));
        RouteId firstLeg = graph.AddRoute(locations[0], locations[1], new SimulationDuration(10));
        RouteId secondLeg = graph.AddRoute(locations[1], locations[2], new SimulationDuration(10));
        graph.SetRouteEnabled(direct, false);

        RoutePlan plan = Assert.IsType<RoutePlan>(graph.FindRoute(locations[0], locations[2]));

        Assert.Equal([firstLeg, secondLeg], plan.RouteIds);
    }

    [Fact]
    public void EqualDurationPathsUseRouteIdOrder()
    {
        LocationId[] locations = CreateLocations(4);
        var graph = CreateGraph(locations);
        RouteId preferredFirst = graph.AddRoute(
            locations[0], locations[1], new SimulationDuration(10));
        RouteId preferredSecond = graph.AddRoute(
            locations[1], locations[3], new SimulationDuration(10));
        graph.AddRoute(locations[0], locations[2], new SimulationDuration(10));
        graph.AddRoute(locations[2], locations[3], new SimulationDuration(10));

        RoutePlan plan = Assert.IsType<RoutePlan>(graph.FindRoute(locations[0], locations[3]));

        Assert.Equal([preferredFirst, preferredSecond], plan.RouteIds);
    }

    private static LocationId[] CreateLocations(int count)
    {
        var ids = new IdSequence<LocationId>();
        return Enumerable.Range(0, count).Select(_ => ids.Allocate()).ToArray();
    }

    private static RouteGraph CreateGraph(IEnumerable<LocationId> locations)
    {
        var graph = new RouteGraph();
        foreach (LocationId location in locations)
        {
            graph.AddLocation(location);
        }

        return graph;
    }
}
