using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverBrakingTests
{
    [Theory]
    [InlineData(10_000, 0, 10_000, 5_000UL)]
    [InlineData(5_000, 3_000, 2_000, 4_000UL)]
    [InlineData(1_000, 0, 1_000, 500UL)]
    [InlineData(1_000, 0, 3_000, 167UL)]
    public void RequiredDistanceUsesSquaredSpeedDifferenceWithNormalRounding(
        long currentSpeed,
        long targetSpeed,
        ulong deceleration,
        ulong expectedMillimeters)
    {
        Assert.Equal(
            expectedMillimeters,
            ManeuverBraking.RequiredDistanceMillimeters(
                new ShipVelocity(currentSpeed, 0),
                new ShipVelocity(targetSpeed, 0),
                new ManeuverAcceleration(deceleration)));
    }

    [Fact]
    public void RequiredDistanceUsesCompleteVelocityVector()
    {
        Assert.Equal(
            5_000UL,
            ManeuverBraking.RequiredDistanceMillimeters(
                new ShipVelocity(3_000, 4_000),
                ShipVelocity.Zero,
                new ManeuverAcceleration(2_500)));
    }

    [Fact]
    public void RequiredDistanceIsZeroWhenTargetSpeedIsNotLower()
    {
        Assert.Equal(
            0UL,
            ManeuverBraking.RequiredDistanceMillimeters(
                new ShipVelocity(3_000, 4_000),
                new ShipVelocity(6_000, 0),
                new ManeuverAcceleration(1_000)));
    }

    [Fact]
    public void LatestSafeBoundaryIncludesArrivalTolerance()
    {
        ShipVelocity current = new(10_000, 0);
        var deceleration = new ManeuverAcceleration(10_000);

        Assert.False(ManeuverBraking.ShouldBegin(
            current,
            ShipVelocity.Zero,
            deceleration,
            remainingDistanceMillimeters: 6_001,
            arrivalToleranceMillimeters: 1_000));
        Assert.True(ManeuverBraking.ShouldBegin(
            current,
            ShipVelocity.Zero,
            deceleration,
            remainingDistanceMillimeters: 6_000,
            arrivalToleranceMillimeters: 1_000));
        Assert.True(ManeuverBraking.ShouldBegin(
            current,
            ShipVelocity.Zero,
            deceleration,
            remainingDistanceMillimeters: 999,
            arrivalToleranceMillimeters: 1_000));
    }

    [Fact]
    public void LatestSafeBoundaryUsesExactRatioInsteadOfRoundedDistance()
    {
        ShipVelocity current = new(1_000, 0);
        var deceleration = new ManeuverAcceleration(3_000);

        Assert.Equal(
            167UL,
            ManeuverBraking.RequiredDistanceMillimeters(
                current,
                ShipVelocity.Zero,
                deceleration));
        Assert.False(ManeuverBraking.ShouldBegin(
            current,
            ShipVelocity.Zero,
            deceleration,
            remainingDistanceMillimeters: 167,
            arrivalToleranceMillimeters: 0));
        Assert.True(ManeuverBraking.ShouldBegin(
            current,
            ShipVelocity.Zero,
            deceleration,
            remainingDistanceMillimeters: 166,
            arrivalToleranceMillimeters: 0));
    }

    [Fact]
    public void NoBrakingIsRequiredWithoutExcessSpeed()
    {
        Assert.False(ManeuverBraking.ShouldBegin(
            ShipVelocity.Zero,
            ShipVelocity.Zero,
            new ManeuverAcceleration(1_000),
            remainingDistanceMillimeters: 0,
            arrivalToleranceMillimeters: 1_000));
    }

    [Fact]
    public void RequiredDistanceRejectsValuesOutsidePublishedRange()
    {
        Assert.Throws<OverflowException>(() =>
            ManeuverBraking.RequiredDistanceMillimeters(
                new ShipVelocity(long.MinValue, long.MinValue),
                ShipVelocity.Zero,
                new ManeuverAcceleration(1)));
    }
}
