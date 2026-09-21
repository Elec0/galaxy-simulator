using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CappedSpeedManeuverProfileTests
{
    [Fact]
    public void RestProfileContainsAccelerateTravelAndBrakeDistances()
    {
        bool created = CappedSpeedManeuverProfile.TryCreate(
            distanceMillimeters: 200_000,
            initialSpeedMillimetersPerSecond: 0,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(45_000UL, profile.AccelerationDistanceMillimeters);
        Assert.Equal(110_000UL, profile.CappedTravelDistanceMillimeters);
        Assert.Equal(45_000UL, profile.BrakingDistanceMillimeters);
    }

    [Fact]
    public void ExistingAlignedSpeedReducesAccelerationDistance()
    {
        bool created = CappedSpeedManeuverProfile.TryCreate(
            distanceMillimeters: 200_000,
            initialSpeedMillimetersPerSecond: 10_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(10_000UL, profile.InitialSpeedMillimetersPerSecond);
        Assert.Equal(40_000UL, profile.AccelerationDistanceMillimeters);
        Assert.Equal(115_000UL, profile.CappedTravelDistanceMillimeters);
        Assert.Equal(45_000UL, profile.BrakingDistanceMillimeters);
    }

    [Fact]
    public void AsymmetricBrakingMovesCappedTravelBoundaryEarlier()
    {
        bool created = CappedSpeedManeuverProfile.TryCreate(
            distanceMillimeters: 200_000,
            initialSpeedMillimetersPerSecond: 0,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(5_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(45_000UL, profile.AccelerationDistanceMillimeters);
        Assert.Equal(65_000UL, profile.CappedTravelDistanceMillimeters);
        Assert.Equal(90_000UL, profile.BrakingDistanceMillimeters);
    }

    [Fact]
    public void PublishedDistancesUseNormalRounding()
    {
        bool created = CappedSpeedManeuverProfile.TryCreate(
            distanceMillimeters: 1_000,
            initialSpeedMillimetersPerSecond: 0,
            new ManeuverAcceleration(3_000),
            new ManeuverAcceleration(4_000),
            new ManeuverSpeed(1_000),
            out CappedSpeedManeuverProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(167UL, profile.AccelerationDistanceMillimeters);
        Assert.Equal(708UL, profile.CappedTravelDistanceMillimeters);
        Assert.Equal(125UL, profile.BrakingDistanceMillimeters);
    }

    [Theory]
    [InlineData(90_000)]
    [InlineData(89_000)]
    public void ZeroOrNegativeCappedTravelRemainsTriangular(
        ulong distanceMillimeters)
    {
        bool created = CappedSpeedManeuverProfile.TryCreate(
            distanceMillimeters,
            initialSpeedMillimetersPerSecond: 0,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverProfile? profile);

        Assert.False(created);
        Assert.Null(profile);
    }

    [Fact]
    public void InitialSpeedAboveCapRequiresBrakingBeforeThisProfile()
    {
        bool created = CappedSpeedManeuverProfile.TryCreate(
            distanceMillimeters: 200_000,
            initialSpeedMillimetersPerSecond: 31_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverProfile? profile);

        Assert.False(created);
        Assert.Null(profile);
    }

    [Fact]
    public void ZeroDistanceIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CappedSpeedManeuverProfile.TryCreate(
                distanceMillimeters: 0,
                initialSpeedMillimetersPerSecond: 0,
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000),
                out _));
    }

    [Fact]
    public void UnrepresentableProfileCalculationIsRejected()
    {
        Assert.Throws<OverflowException>(() =>
            CappedSpeedManeuverProfile.TryCreate(
                distanceMillimeters: ulong.MaxValue,
                initialSpeedMillimetersPerSecond: 0,
                new ManeuverAcceleration(ulong.MaxValue),
                new ManeuverAcceleration(ulong.MaxValue),
                new ManeuverSpeed(ulong.MaxValue),
                out _));
    }
}
