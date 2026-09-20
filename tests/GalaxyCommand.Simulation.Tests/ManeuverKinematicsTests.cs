using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverKinematicsTests
{
    [Fact]
    public void ZeroElapsedTimeReturnsThePhaseStartState()
    {
        ShipKinematicState start = State(
            x: 12,
            y: -34,
            velocityX: 5_000,
            velocityY: -7_000,
            heading: 123_000);

        ShipKinematicState result = ManeuverKinematics.Evaluate(
            start,
            new ShipAcceleration(10_000, -20_000),
            new ShipAngularRate(45_000),
            SimulationDuration.Zero);

        Assert.Equal(start, result);
    }

    [Fact]
    public void ConstantVelocityRoundsPositionHalfUnitsAwayFromZero()
    {
        ShipKinematicState result = ManeuverKinematics.Evaluate(
            State(0, 0, 1_000, -1_000, 0),
            ShipAcceleration.Zero,
            ShipAngularRate.Zero,
            new SimulationDuration(500));

        Assert.Equal(Position(1, -1), result.Position);
        Assert.Equal(new ShipVelocity(1_000, -1_000), result.Velocity);
    }

    [Fact]
    public void ConstantAccelerationEvaluatesPositionAndVelocityAnalytically()
    {
        ShipKinematicState result = ManeuverKinematics.Evaluate(
            State(0, 0, 0, 0, 0),
            new ShipAcceleration(10_000, -4_000),
            ShipAngularRate.Zero,
            new SimulationDuration(1_000));

        Assert.Equal(Position(5, -2), result.Position);
        Assert.Equal(new ShipVelocity(10_000, -4_000), result.Velocity);
    }

    [Fact]
    public void AccelerationRoundsVelocityHalfUnitsAwayFromZero()
    {
        ShipKinematicState result = ManeuverKinematics.Evaluate(
            State(0, 0, 0, 0, 0),
            new ShipAcceleration(1, -1),
            ShipAngularRate.Zero,
            new SimulationDuration(500));

        Assert.Equal(new ShipVelocity(1, -1), result.Velocity);
    }

    [Fact]
    public void TurningWrapsHeadingWithoutRotatingVelocity()
    {
        ShipKinematicState result = ManeuverKinematics.Evaluate(
            State(0, 0, 2_000, -1_000, 350_000),
            ShipAcceleration.Zero,
            new ShipAngularRate(45_000),
            new SimulationDuration(500));

        Assert.Equal(new ShipHeading(12_500), result.Heading);
        Assert.Equal(new ShipVelocity(2_000, -1_000), result.Velocity);
        Assert.Equal(Position(1, -1), result.Position);
    }

    [Fact]
    public void CounterclockwiseTurningCanonicalizesNegativeResult()
    {
        ShipKinematicState result = ManeuverKinematics.Evaluate(
            State(0, 0, 0, 0, 10_000),
            ShipAcceleration.Zero,
            new ShipAngularRate(-45_000),
            new SimulationDuration(500));

        Assert.Equal(new ShipHeading(347_500), result.Heading);
    }

    [Fact]
    public void PositionOverflowRejectsInsteadOfWrapping()
    {
        ShipKinematicState start = State(
            long.MaxValue,
            0,
            velocityX: 1_000,
            velocityY: 0,
            heading: 0);

        Assert.Throws<OverflowException>(() => ManeuverKinematics.Evaluate(
            start,
            ShipAcceleration.Zero,
            ShipAngularRate.Zero,
            new SimulationDuration(1_000)));
    }

    [Fact]
    public void AnalyticSegmentEvaluatesFromItsImmutableStartBoundary()
    {
        ShipKinematicState start = State(10, -10, 0, 0, 350_000);
        var segment = new AnalyticManeuverSegment(
            new SimulationTime(2_000),
            new SimulationTime(3_000),
            start,
            new ShipAcceleration(10_000, -4_000),
            new ShipAngularRate(45_000));

        ShipKinematicState halfway = segment.StateAt(new SimulationTime(2_500));
        ShipKinematicState end = segment.StateAt(segment.EndsAt);

        Assert.Equal(start, segment.StateAt(segment.StartsAt));
        Assert.Equal(Position(11, -11), halfway.Position);
        Assert.Equal(new ShipVelocity(5_000, -2_000), halfway.Velocity);
        Assert.Equal(new ShipHeading(12_500), halfway.Heading);
        Assert.Equal(Position(15, -12), end.Position);
        Assert.Equal(new ShipVelocity(10_000, -4_000), end.Velocity);
        Assert.Equal(new ShipHeading(35_000), end.Heading);
    }

    [Fact]
    public void AnalyticSegmentRejectsInvalidBoundaryOrOutOfRangeEvaluation()
    {
        ShipKinematicState start = State(0, 0, 0, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnalyticManeuverSegment(
                new SimulationTime(10),
                new SimulationTime(10),
                start,
                ShipAcceleration.Zero,
                ShipAngularRate.Zero));

        var segment = new AnalyticManeuverSegment(
            new SimulationTime(10),
            new SimulationTime(20),
            start,
            ShipAcceleration.Zero,
            ShipAngularRate.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            segment.StateAt(new SimulationTime(9)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            segment.StateAt(new SimulationTime(21)));
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
