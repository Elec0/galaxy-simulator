using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialNavigationTests
{
    [Fact]
    public void SameSystemPositionProducesOneLocalLegWithoutRouteIds()
    {
        var estimator = new RecordingEstimator(new SimulationDuration(250));
        var planner = new DirectLocalNavigationPlanner(estimator);
        var origin = Position(new SystemId(1), 10, -20);
        var destination = Position(new SystemId(1), 40, 80);
        var request = new NavigationRequest(
            new ShipId(3),
            origin,
            new NavigationDestination.Position(destination),
            new SimulationTime(500));

        var result = Assert.IsType<NavigationPlanResult.Planned>(
            planner.Plan(request));
        TravelPlan plan = result.Plan;
        var leg = Assert.IsType<TravelLeg.Local>(Assert.Single(plan.Legs));

        Assert.Equal(request.Destination, plan.Destination);
        Assert.Equal(new SimulationDuration(250), plan.TotalDuration);
        Assert.Equal(origin, leg.Origin);
        Assert.Equal(destination, leg.Destination);
        Assert.Equal(new SimulationDuration(250), leg.Duration);
        Assert.Equal(
            (request.ActorId, origin, destination),
            Assert.Single(estimator.Requests));
        Assert.DoesNotContain(
            typeof(RouteId),
            typeof(TravelLeg.Local)
                .GetProperties()
                .Select(property => property.PropertyType));
    }

    [Fact]
    public void CrossSystemPositionRequiresConnectorPlanning()
    {
        var estimator = new RecordingEstimator(new SimulationDuration(250));
        var planner = new DirectLocalNavigationPlanner(estimator);
        var request = new NavigationRequest(
            new ShipId(3),
            Position(new SystemId(1), 10, -20),
            new NavigationDestination.Position(
                Position(new SystemId(2), 40, 80)),
            SimulationTime.Zero);

        var result = Assert.IsType<NavigationPlanResult.Unreachable>(
            planner.Plan(request));

        Assert.Equal(
            NavigationFailureReason.InterSystemConnectorRequired,
            result.Reason);
        Assert.Empty(estimator.Requests);
    }

    [Fact]
    public void CurrentSystemDestinationNeedsNoTravelLeg()
    {
        var estimator = new RecordingEstimator(new SimulationDuration(250));
        var planner = new DirectLocalNavigationPlanner(estimator);
        var request = new NavigationRequest(
            new ShipId(3),
            Position(new SystemId(1), 10, -20),
            new NavigationDestination.System(new SystemId(1)),
            SimulationTime.Zero);

        var result = Assert.IsType<NavigationPlanResult.Planned>(
            planner.Plan(request));

        Assert.Empty(result.Plan.Legs);
        Assert.Equal(SimulationDuration.Zero, result.Plan.TotalDuration);
        Assert.Empty(estimator.Requests);
    }

    [Fact]
    public void CrossSystemDestinationCompletesAtEmergenceEndpoint()
    {
        SystemId firstSystem = new(1);
        SystemId secondSystem = new(2);
        SystemPosition origin = Position(firstSystem, 0, 0);
        SystemPosition source = Position(firstSystem, 10, 0);
        SystemPosition emergence = Position(secondSystem, -10, 0);
        var topology = new ConnectorTopology(
            [
                new ConnectorEndpoint(new ConnectorEndpointId(1), source),
                new ConnectorEndpoint(new ConnectorEndpointId(2), emergence),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    new SimulationDuration(50)),
            ]);
        var destination = new NavigationDestination.System(secondSystem);
        var planner = new HierarchicalNavigationPlanner(
            topology,
            new CoordinateEstimator());

        var result = Assert.IsType<NavigationPlanResult.Planned>(
            planner.Plan(new NavigationRequest(
                new ShipId(3),
                origin,
                destination,
                SimulationTime.Zero)));

        Assert.Equal(destination, result.Plan.Destination);
        Assert.Equal(new SimulationDuration(60), result.Plan.TotalDuration);
        Assert.Collection(
            result.Plan.Legs,
            first => Assert.IsType<TravelLeg.Local>(first),
            second =>
            {
                var connector = Assert.IsType<TravelLeg.Connector>(second);
                Assert.Equal(emergence, connector.Destination);
            });
    }

    [Fact]
    public void HierarchicalPlannerComposesLocalAndConnectorLegs()
    {
        SystemId firstSystem = new(1);
        SystemId secondSystem = new(2);
        SystemPosition origin = Position(firstSystem, 0, 0);
        SystemPosition source = Position(firstSystem, 10, 0);
        SystemPosition emergence = Position(secondSystem, -10, 0);
        SystemPosition destination = Position(secondSystem, 0, 0);
        var topology = new ConnectorTopology(
            [
                new ConnectorEndpoint(new ConnectorEndpointId(1), source),
                new ConnectorEndpoint(new ConnectorEndpointId(2), emergence),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    new SimulationDuration(50)),
            ]);
        var planner = new HierarchicalNavigationPlanner(
            topology,
            new CoordinateEstimator());
        var request = new NavigationRequest(
            new ShipId(3),
            origin,
            new NavigationDestination.Position(destination),
            SimulationTime.Zero);

        var result = Assert.IsType<NavigationPlanResult.Planned>(
            planner.Plan(request));

        Assert.Equal(new SimulationDuration(70), result.Plan.TotalDuration);
        Assert.Collection(
            result.Plan.Legs,
            first =>
            {
                var local = Assert.IsType<TravelLeg.Local>(first);
                Assert.Equal(origin, local.Origin);
                Assert.Equal(source, local.Destination);
            },
            second =>
            {
                var connector = Assert.IsType<TravelLeg.Connector>(second);
                Assert.Equal(new TransitConnectionId(1), connector.ConnectionId);
                Assert.Equal(source, connector.Origin);
                Assert.Equal(emergence, connector.Destination);
            },
            third =>
            {
                var local = Assert.IsType<TravelLeg.Local>(third);
                Assert.Equal(emergence, local.Origin);
                Assert.Equal(destination, local.Destination);
            });
    }

    [Fact]
    public void EqualDurationConnectorPathsUseConnectionIdentityTieBreak()
    {
        SystemId firstSystem = new(1);
        SystemId secondSystem = new(2);
        ConnectorEndpoint[] endpoints =
        [
            new ConnectorEndpoint(
                new ConnectorEndpointId(1),
                Position(firstSystem, 10, 0)),
            new ConnectorEndpoint(
                new ConnectorEndpointId(2),
                Position(secondSystem, -10, 0)),
            new ConnectorEndpoint(
                new ConnectorEndpointId(3),
                Position(firstSystem, 10, 0)),
            new ConnectorEndpoint(
                new ConnectorEndpointId(4),
                Position(secondSystem, -10, 0)),
        ];
        var topology = new ConnectorTopology(
            endpoints,
            [
                new TransitConnection(
                    new TransitConnectionId(2),
                    new ConnectorEndpointId(3),
                    new ConnectorEndpointId(4),
                    new SimulationDuration(50)),
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    new SimulationDuration(50)),
            ]);
        var planner = new HierarchicalNavigationPlanner(
            topology,
            new CoordinateEstimator());

        var result = Assert.IsType<NavigationPlanResult.Planned>(
            planner.Plan(new NavigationRequest(
                new ShipId(3),
                Position(firstSystem, 0, 0),
                new NavigationDestination.Position(
                    Position(secondSystem, 0, 0)),
                SimulationTime.Zero)));

        var selected = Assert.IsType<TravelLeg.Connector>(
            result.Plan.Legs[1]);
        Assert.Equal(new TransitConnectionId(1), selected.ConnectionId);
    }

    [Fact]
    public void HierarchicalPlannerTraversesMultipleSystems()
    {
        SystemId firstSystem = new(1);
        SystemId middleSystem = new(2);
        SystemId finalSystem = new(3);
        var topology = new ConnectorTopology(
            [
                new ConnectorEndpoint(
                    new ConnectorEndpointId(1),
                    Position(firstSystem, 10, 0)),
                new ConnectorEndpoint(
                    new ConnectorEndpointId(2),
                    Position(middleSystem, -10, 0)),
                new ConnectorEndpoint(
                    new ConnectorEndpointId(3),
                    Position(middleSystem, 10, 0)),
                new ConnectorEndpoint(
                    new ConnectorEndpointId(4),
                    Position(finalSystem, -10, 0)),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    new SimulationDuration(50)),
                new TransitConnection(
                    new TransitConnectionId(2),
                    new ConnectorEndpointId(3),
                    new ConnectorEndpointId(4),
                    new SimulationDuration(50)),
            ]);
        var planner = new HierarchicalNavigationPlanner(
            topology,
            new CoordinateEstimator());

        var result = Assert.IsType<NavigationPlanResult.Planned>(
            planner.Plan(new NavigationRequest(
                new ShipId(1),
                Position(firstSystem, 0, 0),
                new NavigationDestination.Position(
                    Position(finalSystem, 0, 0)),
                SimulationTime.Zero)));

        Assert.Equal(new SimulationDuration(140), result.Plan.TotalDuration);
        Assert.Equal(
            [new TransitConnectionId(1), new TransitConnectionId(2)],
            result.Plan.Legs
                .OfType<TravelLeg.Connector>()
                .Select(leg => leg.ConnectionId));
    }

    [Fact]
    public void HierarchicalPlannerReportsMissingDirectedPath()
    {
        SystemPosition source = Position(new SystemId(1), 0, 0);
        SystemPosition destination = Position(new SystemId(2), 0, 0);
        var topology = new ConnectorTopology(
            [
                new ConnectorEndpoint(new ConnectorEndpointId(1), source),
                new ConnectorEndpoint(new ConnectorEndpointId(2), destination),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(2),
                    new ConnectorEndpointId(1),
                    new SimulationDuration(10)),
            ]);
        var planner = new HierarchicalNavigationPlanner(
            topology,
            new CoordinateEstimator());

        var result = Assert.IsType<NavigationPlanResult.Unreachable>(
            planner.Plan(new NavigationRequest(
                new ShipId(1),
                source,
                new NavigationDestination.Position(destination),
                SimulationTime.Zero)));

        Assert.Equal(NavigationFailureReason.NoConnectorPath, result.Reason);
    }

    [Fact]
    public void ConnectorTopologyRejectsInvalidReferencesAndSameSystemTransit()
    {
        var first = new ConnectorEndpoint(
            new ConnectorEndpointId(1),
            Position(new SystemId(1), 0, 0));
        var second = new ConnectorEndpoint(
            new ConnectorEndpointId(2),
            Position(new SystemId(1), 10, 0));

        Assert.Throws<ArgumentException>(() =>
            new ConnectorTopology(
                [first],
                [
                    new TransitConnection(
                        new TransitConnectionId(1),
                        first.Id,
                        new ConnectorEndpointId(99),
                        new SimulationDuration(10)),
                ]));
        Assert.Throws<ArgumentException>(() =>
            new ConnectorTopology(
                [first, second],
                [
                    new TransitConnection(
                        new TransitConnectionId(1),
                        first.Id,
                        second.Id,
                        new SimulationDuration(10)),
                ]));
    }

    [Fact]
    public void TravelPlanCopiesLegCollection()
    {
        SystemPosition position = Position(new SystemId(1), 0, 0);
        var legs = new List<TravelLeg>
        {
            new TravelLeg.Local(position, position, SimulationDuration.Zero),
        };
        var plan = new TravelPlan(
            new NavigationDestination.Position(position),
            legs);

        legs.Clear();

        Assert.Single(plan.Legs);
        var exposed = Assert.IsAssignableFrom<IList<TravelLeg>>(plan.Legs);
        Assert.Throws<NotSupportedException>(() =>
            exposed.Add(new TravelLeg.Local(
                position,
                position,
                SimulationDuration.Zero)));
    }

    [Fact]
    public void SystemAndDestinationRejectInvalidIdentityOrName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StarSystem(default, "Invalid"));
        Assert.Throws<ArgumentException>(() =>
            new StarSystem(new SystemId(1), " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemPosition(default, new SpatialPosition()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NavigationDestination.Position(default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NavigationDestination.System(default));
    }

    private static SystemPosition Position(
        SystemId systemId,
        long x,
        long y) =>
        new(
            systemId,
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));

    private sealed class RecordingEstimator : ILocalTravelTimeEstimator
    {
        private readonly SimulationDuration _duration;

        public RecordingEstimator(SimulationDuration duration)
        {
            _duration = duration;
        }

        public List<(ShipId ActorId, SystemPosition Origin, SystemPosition Destination)> Requests
        {
            get;
        } = [];

        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination)
        {
            Requests.Add((actorId, origin, destination));
            return _duration;
        }
    }

    private sealed class CoordinateEstimator : ILocalTravelTimeEstimator
    {
        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination)
        {
            Assert.Equal(origin.SystemId, destination.SystemId);
            Int128 horizontal =
                (Int128)origin.Position.X.Units
                - destination.Position.X.Units;
            Int128 vertical =
                (Int128)origin.Position.Y.Units
                - destination.Position.Y.Units;
            UInt128 horizontalMagnitude = horizontal < 0
                ? (UInt128)(-horizontal)
                : (UInt128)horizontal;
            UInt128 verticalMagnitude = vertical < 0
                ? (UInt128)(-vertical)
                : (UInt128)vertical;
            return new SimulationDuration(
                checked((ulong)(horizontalMagnitude + verticalMagnitude)));
        }
    }
}
