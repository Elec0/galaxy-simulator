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
}
