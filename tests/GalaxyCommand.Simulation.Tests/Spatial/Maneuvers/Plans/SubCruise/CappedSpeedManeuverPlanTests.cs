using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CappedSpeedManeuverPlanTests
{
    [Fact]
    public void RestPlanSchedulesAccelerateTravelAndBrakePhases()
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            new SimulationTime(100),
            State(0, 0, 0, 0, 123_000),
            Position(200, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? candidate);

        Assert.True(created);
        CappedSpeedManeuverPlan plan = Assert.IsType<CappedSpeedManeuverPlan>(candidate);
        Assert.NotNull(plan.AccelerationPhase);
        Assert.Equal(new SimulationTime(3_100), plan.TravelStartsAt);
        Assert.Equal(new SimulationTime(6_767), plan.BrakingStartsAt);
        Assert.Equal(new SimulationTime(9_767), plan.EndsAt);

        ShipKinematicState atCap = plan.StateAt(plan.TravelStartsAt);
        Assert.Equal(Position(45, 0), atCap.Position);
        Assert.Equal(new ShipVelocity(30_000, 0), atCap.Velocity);
        Assert.Equal(new ShipHeading(123_000), atCap.Heading);

        ShipKinematicState braking = plan.StateAt(plan.BrakingStartsAt);
        Assert.Equal(Position(155, 0), braking.Position);
        Assert.Equal(new ShipVelocity(30_000, 0), braking.Velocity);

        ShipKinematicState stopped = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(200, 0), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(new ShipHeading(123_000), stopped.Heading);
    }

    [Fact]
    public void ExistingAlignedSpeedShortensAccelerationPhase()
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 10_000, 0, 45_000),
            Position(200, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? candidate);

        Assert.True(created);
        CappedSpeedManeuverPlan plan = Assert.IsType<CappedSpeedManeuverPlan>(candidate);
        Assert.Equal(new SimulationTime(2_000), plan.TravelStartsAt);
        Assert.Equal(new SimulationTime(5_833), plan.BrakingStartsAt);
        Assert.Equal(new SimulationTime(8_833), plan.EndsAt);
        Assert.Equal(Position(200, 0), plan.StateAt(plan.EndsAt).Position);
    }

    [Fact]
    public void StartAtCapOmitsAccelerationPhase()
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 30_000, 0, 0),
            Position(100, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? candidate);

        Assert.True(created);
        CappedSpeedManeuverPlan plan = Assert.IsType<CappedSpeedManeuverPlan>(candidate);
        Assert.Null(plan.AccelerationPhase);
        Assert.Equal(SimulationTime.Zero, plan.TravelStartsAt);
        Assert.Equal(new SimulationTime(1_833), plan.BrakingStartsAt);
        Assert.Equal(new SimulationTime(4_833), plan.EndsAt);
        Assert.Equal(Position(100, 0), plan.StateAt(plan.EndsAt).Position);
    }

    [Fact]
    public void DiagonalPlanUsesNormalizedAccelerationAndCappedVelocity()
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 270_000),
            Position(120, 160),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? candidate);

        Assert.True(created);
        CappedSpeedManeuverPlan plan = Assert.IsType<CappedSpeedManeuverPlan>(candidate);
        Assert.Equal(
            new ShipAcceleration(6_000, 8_000),
            plan.AccelerationPhase?.Acceleration);
        Assert.Equal(
            new ShipVelocity(18_000, 24_000),
            plan.StateAt(plan.TravelStartsAt).Velocity);
        Assert.Equal(Position(120, 160), plan.StateAt(plan.EndsAt).Position);
        Assert.Equal(new ShipHeading(270_000), plan.StateAt(plan.EndsAt).Heading);
    }

    [Fact]
    public void ExactCapTouchRemainsTriangular()
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 0),
            Position(90, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void UnalignedVelocityUsesAnotherPlannerPath()
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 0, 1_000, 0),
            Position(200, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void CrossSystemDestinationIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            CappedSpeedManeuverPlan.TryCreateAligned(
                SimulationTime.Zero,
                State(0, 0, 0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(200),
                        new SpatialCoordinate(0))),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000),
                out _));
    }

    private static ShipKinematicState State(
        long x,
        long y,
        long velocityX,
        long velocityY,
        uint heading) =>
        new(
            Position(x, y),
            new ShipVelocity(velocityX, velocityY),
            new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
