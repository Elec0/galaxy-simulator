using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverSweptRangeQueryTests
{
    [Fact]
    public void CommittedEndpointCanCreateContactThatTheContinuousCurveNeverReached()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Drift(0, 2_000_000, 3_000_000_000);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(ManeuverSweptRangeQuery.FindFirstContact(
            plan, Position(1), SimulationTime.Zero, plan.EndsAt, 0));
        Assert.Equal(new SimulationTime(1), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
        Assert.False(contact.AlreadyInRange);
    }

    [Fact]
    public void ContactOnlyAtTheUncommittedEndpointIsExcluded()
    {
        // Both continuous stop positions are 0.5 m. Their committed rounded
        // positions are 1 m and 0 m, so no contact exists at the boundary.
        ExecutableBoundedTerminalManeuverPlan first = Drift(0, 1_000, 1_000);
        ExecutableBoundedTerminalManeuverPlan second = Drift(1, -1_000, 1_000);
        Assert.Null(ManeuverSweptRangeQuery.FindFirstContact(first, second,
            SimulationTime.Zero, first.EndsAt, 0));
    }

    [Theory]
    [InlineData(25, 2237)]
    [InlineData(65, 3764)]
    public void WholePlanRetainsExactAccelerationAndBrakingCrossings(long target, ulong expected)
    {
        ExecutableBoundedTerminalManeuverPlan plan = Move();
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(ManeuverSweptRangeQuery.FindFirstContact(
            plan, Position(target), SimulationTime.Zero, plan.EndsAt, 0));
        Assert.Equal(new SimulationTime(expected), contact.Timestamp);
    }

    [Fact]
    public void InsertedWaypointEventsDoNotResetOrChangeTheForecastCurve()
    {
        ExecutableBoundedTerminalManeuverPlan original = Move();
        ExecutableBoundedTerminalManeuverPlan split = original.WithAdditionalBoundaries([new SimulationTime(2100)]);
        SweptRangeContact? expected = ManeuverSweptRangeQuery.FindFirstContact(original, Position(25),
            new SimulationTime(2100), original.EndsAt, 0);
        SweptRangeContact? actual = ManeuverSweptRangeQuery.FindFirstContact(split, Position(25),
            new SimulationTime(2100), split.EndsAt, 0);
        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
        Assert.Equal(new SimulationTime(2237), actual.Timestamp);
    }

    private static ExecutableBoundedTerminalManeuverPlan Drift(long x, long velocity, ulong rate) =>
        ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(new DecelerationManeuverSegment(
            ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero,
            new ShipKinematicState(Position(x), new ShipVelocity(velocity, 0), ShipHeading.Zero), new ManeuverAcceleration(rate)));

    private static ExecutableBoundedTerminalManeuverPlan Move() => BoundedTerminalManeuverPlanner.Select(
        SimulationTime.Zero, new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(90), null,
        new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000),
        new ManeuverSpeed(30_000), new ManeuverTurnRate(45_000)).ExecutablePlan!;

    private static SystemPosition Position(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
