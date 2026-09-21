using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ReducedThrustTerminalManeuverPlanTests
{
    [Fact]
    public void TimestampWithoutRoomForTwoPositivePhasesIsRejected()
    {
        bool created = ReducedThrustTerminalManeuverPlan.TryCreateFromRest(
            new SimulationTime(ulong.MaxValue - 1),
            State(0, 0),
            Position(2, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ReducedThrustTerminalManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void UnrepresentableCoordinateDeltaIsRejectedWithoutThrowing()
    {
        bool created = ReducedThrustTerminalManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(long.MinValue, 0),
            Position(long.MaxValue, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ReducedThrustTerminalManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    private static ShipKinematicState State(long x, long y) =>
        new(Position(x, y), ShipVelocity.Zero, ShipHeading.Zero);

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
