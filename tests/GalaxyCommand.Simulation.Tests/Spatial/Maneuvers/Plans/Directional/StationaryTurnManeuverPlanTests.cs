using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationaryTurnManeuverPlanTests
{
    [Fact]
    public void ClockwiseTurnWrapsToRequestedHeadingAtEffectiveRate()
    {
        bool created = StationaryTurnManeuverPlan.TryCreate(
            new SimulationTime(100),
            State(0, 0, 350_000),
            Position(0, 0),
            new ShipHeading(35_000),
            new ManeuverTurnRate(45_000),
            out StationaryTurnManeuverPlan? candidate);

        Assert.True(created);
        StationaryTurnManeuverPlan plan =
            Assert.IsType<StationaryTurnManeuverPlan>(candidate);
        Assert.Equal(new SimulationTime(100), plan.StartsAt);
        Assert.Equal(new SimulationTime(1_100), plan.EndsAt);
        Assert.Equal(new ShipAngularRate(45_000), plan.TurnPhase.AngularRate);

        ShipKinematicState completed = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(0, 0), completed.Position);
        Assert.Equal(ShipVelocity.Zero, completed.Velocity);
        Assert.Equal(new ShipHeading(35_000), completed.Heading);
    }

    [Fact]
    public void CounterclockwiseTurnUsesNormallyRoundedDuration()
    {
        bool created = StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 10_000),
            Position(0, 0),
            new ShipHeading(350_000),
            new ManeuverTurnRate(45_000),
            out StationaryTurnManeuverPlan? candidate);

        Assert.True(created);
        StationaryTurnManeuverPlan plan =
            Assert.IsType<StationaryTurnManeuverPlan>(candidate);
        Assert.Equal(new SimulationTime(444), plan.EndsAt);
        Assert.Equal(new ShipAngularRate(-45_000), plan.TurnPhase.AngularRate);
        Assert.Equal(
            new ShipHeading(350_020),
            plan.StateAt(plan.EndsAt).Heading);
    }

    [Fact]
    public void ExactHalfTurnTieTurnsClockwise()
    {
        bool created = StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 270_000),
            Position(0, 0),
            new ShipHeading(90_000),
            new ManeuverTurnRate(45_000),
            out StationaryTurnManeuverPlan? candidate);

        Assert.True(created);
        StationaryTurnManeuverPlan plan =
            Assert.IsType<StationaryTurnManeuverPlan>(candidate);
        Assert.Equal(new ShipAngularRate(45_000), plan.TurnPhase.AngularRate);
        Assert.Equal(new SimulationTime(4_000), plan.EndsAt);
        Assert.Equal(new ShipHeading(90_000), plan.StateAt(plan.EndsAt).Heading);
    }

    [Fact]
    public void HeadingAlreadyWithinArrivalToleranceNeedsNoTurnPlan()
    {
        bool created = StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 10_000),
            Position(0, 0),
            new ShipHeading(11_000),
            new ManeuverTurnRate(45_000),
            out StationaryTurnManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void MinimumDurationThatOvershootsToleranceIsNotPublished()
    {
        bool created = StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(0, 0),
            new ShipHeading(1_001),
            new ManeuverTurnRate(3_000_000),
            out StationaryTurnManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void MovingOrTranslatingRequestRequiresAnotherPlanner()
    {
        Assert.False(StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 0, velocityX: 1),
            Position(0, 0),
            new ShipHeading(90_000),
            new ManeuverTurnRate(45_000),
            out _));
        Assert.False(StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(1, 0),
            new ShipHeading(90_000),
            new ManeuverTurnRate(45_000),
            out _));
    }

    [Fact]
    public void CrossSystemDestinationIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            StationaryTurnManeuverPlan.TryCreate(
                SimulationTime.Zero,
                State(0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(0),
                        new SpatialCoordinate(0))),
                new ShipHeading(90_000),
                new ManeuverTurnRate(45_000),
                out _));
    }

    private static ShipKinematicState State(
        long x,
        long y,
        uint heading,
        long velocityX = 0,
        long velocityY = 0) =>
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
