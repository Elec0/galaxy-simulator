namespace GalaxyCommand.Simulation.Tests;

public sealed class LogisticsNavigationTests
{
    [Fact]
    public void HierarchicalAdapterReturnsOnlyTheMappedJourneyDuration()
    {
        LocationId mine = new(1);
        LocationId refinery = new(2);
        LocationId shipyard = new(3);
        SystemPosition minePosition = Position(1);
        SystemPosition refineryPosition = Position(2);
        SystemPosition shipyardPosition = Position(3);
        var navigation = new HierarchicalLogisticsNavigation(
            new Dictionary<LocationId, SystemPosition>
            {
                [mine] = minePosition,
                [refinery] = refineryPosition,
                [shipyard] = shipyardPosition,
            },
            new HierarchicalNavigationPlanner(
                new ConnectorTopology(
                    [
                        new ConnectorEndpoint(new ConnectorEndpointId(1), minePosition),
                        new ConnectorEndpoint(new ConnectorEndpointId(2), refineryPosition),
                        new ConnectorEndpoint(new ConnectorEndpointId(3), shipyardPosition),
                    ],
                    [
                        new TransitConnection(
                            new TransitConnectionId(1),
                            new ConnectorEndpointId(1),
                            new ConnectorEndpointId(2),
                            new SimulationDuration(60_000)),
                        new TransitConnection(
                            new TransitConnectionId(2),
                            new ConnectorEndpointId(2),
                            new ConnectorEndpointId(3),
                            new SimulationDuration(60_000)),
                    ]),
                new ZeroLocalTravelTimeEstimator()));

        LogisticsTravelEstimate estimate = Assert.IsType<LogisticsTravelEstimate>(
            navigation.Estimate(new ShipId(1), mine, shipyard, SimulationTime.Zero));

        Assert.Equal(new SimulationDuration(120_000), estimate.Duration);
    }

    [Fact]
    public void MissingLegacyLocationIsUnreachable()
    {
        LocationId mapped = new(1);
        var navigation = new HierarchicalLogisticsNavigation(
            new Dictionary<LocationId, SystemPosition> { [mapped] = Position(1) },
            new DirectLocalNavigationPlanner(new ZeroLocalTravelTimeEstimator()));

        LogisticsTravelEstimate? estimate = navigation.Estimate(
            new ShipId(1),
            mapped,
            new LocationId(2),
            SimulationTime.Zero);

        Assert.Null(estimate);
    }

    private static SystemPosition Position(ulong systemId) =>
        new(new SystemId(systemId), new SpatialPosition());

    private sealed class ZeroLocalTravelTimeEstimator : ILocalTravelTimeEstimator
    {
        /// <inheritdoc />
        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination) => SimulationDuration.Zero;
    }
}
