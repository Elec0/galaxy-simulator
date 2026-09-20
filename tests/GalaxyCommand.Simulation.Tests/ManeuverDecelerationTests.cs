using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverDecelerationTests
{
    [Theory]
    [InlineData(3_000, 4_000, 5_000UL)]
    [InlineData(1, 1, 1UL)]
    [InlineData(1, 2, 2UL)]
    public void SpeedMagnitudeUsesCompleteVectorWithNormalRounding(
        long x,
        long y,
        ulong expected)
    {
        Assert.Equal(
            expected,
            ManeuverVector.SpeedMagnitude(new ShipVelocity(x, y)));
    }

    [Fact]
    public void PassiveDragSchedulesExactStopFromVectorSpeedAndRate()
    {
        var segment = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.PassiveDrag,
            new SimulationTime(100),
            State(0, 0, 3_000, 4_000, 123_000),
            new ManeuverAcceleration(1_000));

        Assert.Equal(new SimulationTime(5_100), segment.EndsAt);
        Assert.Equal(5_000UL, segment.InitialSpeedMillimetersPerSecond);
    }

    [Fact]
    public void PassiveDragPreservesDirectionAndHeadingBeforeStop()
    {
        var segment = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.PassiveDrag,
            SimulationTime.Zero,
            State(0, 0, 3_000, 4_000, 123_000),
            new ManeuverAcceleration(1_000));

        ShipKinematicState halfway = segment.StateAt(new SimulationTime(2_500));

        Assert.Equal(Position(6, 8), halfway.Position);
        Assert.Equal(new ShipVelocity(1_500, 2_000), halfway.Velocity);
        Assert.Equal(new ShipHeading(123_000), halfway.Heading);
    }

    [Fact]
    public void DecelerationPublishesExactZeroAtRoundedUpStopBoundary()
    {
        var segment = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.PassiveDrag,
            SimulationTime.Zero,
            State(0, 0, 3_000, 4_000, 270_000),
            new ManeuverAcceleration(3_000));

        ShipKinematicState before = segment.StateAt(new SimulationTime(1_666));
        ShipKinematicState stopped = segment.StateAt(segment.EndsAt);

        Assert.Equal(new SimulationTime(1_667), segment.EndsAt);
        Assert.Equal(new ShipVelocity(1, 2), before.Velocity);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(Position(3, 3), stopped.Position);
        Assert.Equal(new ShipHeading(270_000), stopped.Heading);
    }

    [Fact]
    public void ActiveBrakeAndPassiveDragRemainDistinctSingleRateInstructions()
    {
        ShipKinematicState start = State(0, 0, -3_000, 4_000, 90_000);
        var passive = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.PassiveDrag,
            SimulationTime.Zero,
            start,
            new ManeuverAcceleration(1_000));
        var active = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            SimulationTime.Zero,
            start,
            new ManeuverAcceleration(1_000));

        Assert.Equal(ManeuverDecelerationKind.PassiveDrag, passive.Kind);
        Assert.Equal(ManeuverDecelerationKind.ActiveBrake, active.Kind);
        Assert.Equal(
            passive.StateAt(new SimulationTime(2_500)),
            active.StateAt(new SimulationTime(2_500)));
    }

    [Fact]
    public void ForcedDropoutBrakesToSubCruiseCapAtTwicePrimaryRate()
    {
        var segment = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            SimulationTime.Zero,
            State(0, 0, 1_000_000, 0, 0),
            new ManeuverAcceleration(20_000),
            new ManeuverSpeed(300_000));

        Assert.Equal(new SimulationTime(35_000), segment.EndsAt);
        Assert.Equal(300_000UL, segment.TargetSpeedMillimetersPerSecond);
        Assert.Equal(
            new ShipVelocity(650_000, 0),
            segment.StateAt(new SimulationTime(17_500)).Velocity);
        Assert.Equal(
            Position(14_438, 0),
            segment.StateAt(new SimulationTime(17_500)).Position);
        ShipKinematicState atSubCruise = segment.StateAt(segment.EndsAt);
        Assert.Equal(new ShipVelocity(300_000, 0), atSubCruise.Velocity);
        Assert.Equal(Position(22_750, 0), atSubCruise.Position);
    }

    [Fact]
    public void DecelerationRejectsStationaryStartOrEvaluationOutsideSegment()
    {
        Assert.Throws<ArgumentException>(() =>
            new DecelerationManeuverSegment(
                ManeuverDecelerationKind.PassiveDrag,
                SimulationTime.Zero,
                State(0, 0, 0, 0, 0),
                new ManeuverAcceleration(1_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DecelerationManeuverSegment(
                (ManeuverDecelerationKind)99,
                SimulationTime.Zero,
                State(0, 0, 1_000, 0, 0),
                new ManeuverAcceleration(1_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DecelerationManeuverSegment(
                ManeuverDecelerationKind.PassiveDrag,
                SimulationTime.Zero,
                State(0, 0, 1_000, 0, 0),
                default));

        var segment = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            new SimulationTime(10),
            State(0, 0, 1_000, 0, 0),
            new ManeuverAcceleration(1_000));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            segment.StateAt(new SimulationTime(9)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            segment.StateAt(new SimulationTime(1_011)));
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
