using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShortMoveImmediateBrakingPlanTests
{
    [Fact]
    public void SpeedAtSwitchBoundarySchedulesOnlyActiveBraking()
    {
        bool created = ShortMoveImmediateBrakingPlan.TryCreateAligned(
            new SimulationTime(100),
            State(0, 0, 30_000, 0, 123_000),
            Position(45, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveImmediateBrakingPlan? candidate);

        Assert.True(created);
        ShortMoveImmediateBrakingPlan plan =
            Assert.IsType<ShortMoveImmediateBrakingPlan>(candidate);
        Assert.Equal(new SimulationTime(100), plan.StartsAt);
        Assert.Equal(new SimulationTime(3_100), plan.EndsAt);
        Assert.Equal(ManeuverDecelerationKind.ActiveBrake, plan.BrakingPhase.Kind);
        Assert.True(plan.CompletesTerminalArrival);

        ShipKinematicState stopped = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(45, 0), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(new ShipHeading(123_000), stopped.Heading);
    }

    [Fact]
    public void AlignedDiagonalVelocityBrakesAlongItsCompleteVector()
    {
        bool created = ShortMoveImmediateBrakingPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 1_800, 2_400, 270_000),
            Position(3, 4),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(900),
            new ManeuverSpeed(300_000),
            out ShortMoveImmediateBrakingPlan? candidate);

        Assert.True(created);
        ShortMoveImmediateBrakingPlan plan =
            Assert.IsType<ShortMoveImmediateBrakingPlan>(candidate);
        Assert.True(plan.CompletesTerminalArrival);
        Assert.Equal(Position(3, 4), plan.StateAt(plan.EndsAt).Position);
        Assert.Equal(ShipVelocity.Zero, plan.StateAt(plan.EndsAt).Velocity);
        Assert.Equal(new ShipHeading(270_000), plan.StateAt(plan.EndsAt).Heading);
    }

    [Fact]
    public void BeyondSwitchBrakesAboveCapAndReportsFollowUpReplan()
    {
        bool created = ShortMoveImmediateBrakingPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 40_000, 0, 0),
            Position(45, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveImmediateBrakingPlan? candidate);

        Assert.True(created);
        ShortMoveImmediateBrakingPlan plan =
            Assert.IsType<ShortMoveImmediateBrakingPlan>(candidate);
        Assert.False(plan.CompletesTerminalArrival);
        Assert.Equal(Position(80, 0), plan.StateAt(plan.EndsAt).Position);
        Assert.Equal(ShipVelocity.Zero, plan.StateAt(plan.EndsAt).Velocity);
    }

    [Fact]
    public void ZeroDistanceWithExcessSpeedBrakesBeforeReplanning()
    {
        bool created = ShortMoveImmediateBrakingPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 2_000, 0, 90_000),
            Position(0, 0),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(300_000),
            out ShortMoveImmediateBrakingPlan? candidate);

        Assert.True(created);
        ShortMoveImmediateBrakingPlan plan =
            Assert.IsType<ShortMoveImmediateBrakingPlan>(candidate);
        Assert.False(plan.CompletesTerminalArrival);
        Assert.Equal(Position(2, 0), plan.StateAt(plan.EndsAt).Position);
        Assert.Equal(new ShipHeading(90_000), plan.StateAt(plan.EndsAt).Heading);
    }

    [Theory]
    [InlineData(0, 1_000)]
    [InlineData(-1_000, 0)]
    public void SidewaysOrAwayVelocityIsNotAnAlignedCandidate(
        long velocityX,
        long velocityY)
    {
        bool created = ShortMoveImmediateBrakingPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, velocityX, velocityY, 0),
            Position(10, 0),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(300_000),
            out ShortMoveImmediateBrakingPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void AlreadySatisfiedOrStationaryMoveNeedsNoImmediateBraking()
    {
        Assert.False(ShortMoveImmediateBrakingPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 1_000, 0, 0),
            Position(0, 0),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(300_000),
            out _));
        Assert.False(ShortMoveImmediateBrakingPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 0),
            Position(10, 0),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(300_000),
            out _));
    }

    [Fact]
    public void CrossSystemDestinationIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ShortMoveImmediateBrakingPlan.TryCreateAligned(
                SimulationTime.Zero,
                State(0, 0, 1_000, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(10),
                        new SpatialCoordinate(0))),
                new ManeuverAcceleration(1_000),
                new ManeuverAcceleration(1_000),
                new ManeuverSpeed(300_000),
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
