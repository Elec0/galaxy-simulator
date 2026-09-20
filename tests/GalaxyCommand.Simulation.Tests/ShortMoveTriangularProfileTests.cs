using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShortMoveTriangularProfileTests
{
    [Fact]
    public void SymmetricRatesSwitchHalfwayWithoutCappedTravel()
    {
        bool created = ShortMoveTriangularProfile.TryCreateFromRest(
            distanceMillimeters: 100_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(0UL, profile.InitialSpeedMillimetersPerSecond);
        Assert.False(profile.BeginsWithBraking);
        Assert.Equal(100_000UL, profile.DistanceMillimeters);
        Assert.Equal(50_000UL, profile.SwitchDistanceMillimeters);
        Assert.Equal(31_623UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void AsymmetricRatesMoveSwitchPointTowardSlowerBraking()
    {
        bool created = ShortMoveTriangularProfile.TryCreateFromRest(
            distanceMillimeters: 150_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(5_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(50_000UL, profile.SwitchDistanceMillimeters);
        Assert.Equal(31_623UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void PublishedSwitchValuesUseNormalRounding()
    {
        bool created = ShortMoveTriangularProfile.TryCreateFromRest(
            distanceMillimeters: 1,
            new ManeuverAcceleration(1),
            new ManeuverAcceleration(1),
            new ManeuverSpeed(10),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(1UL, profile.SwitchDistanceMillimeters);
        Assert.Equal(1UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void CandidateAboveSpeedCapRequiresAnotherProfile()
    {
        bool created = ShortMoveTriangularProfile.TryCreateFromRest(
            distanceMillimeters: 100_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveTriangularProfile? profile);

        Assert.False(created);
        Assert.Null(profile);
    }

    [Fact]
    public void CandidateThatTouchesSpeedCapStillHasNoCappedTravel()
    {
        bool created = ShortMoveTriangularProfile.TryCreateFromRest(
            distanceMillimeters: 90_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(30_000UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void MovingStartAdvancesTheSwitchPoint()
    {
        bool created = ShortMoveTriangularProfile.TryCreate(
            distanceMillimeters: 100_000,
            initialSpeedMillimetersPerSecond: 10_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.Equal(10_000UL, profile.InitialSpeedMillimetersPerSecond);
        Assert.False(profile.BeginsWithBraking);
        Assert.Equal(47_500UL, profile.SwitchDistanceMillimeters);
        Assert.Equal(32_404UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void SpeedAtSwitchBoundaryBeginsWithBraking()
    {
        bool created = ShortMoveTriangularProfile.TryCreate(
            distanceMillimeters: 45_000,
            initialSpeedMillimetersPerSecond: 30_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.True(profile.BeginsWithBraking);
        Assert.Equal(0UL, profile.SwitchDistanceMillimeters);
        Assert.Equal(30_000UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void SpeedBeyondSwitchBrakesImmediatelyEvenAboveCap()
    {
        bool created = ShortMoveTriangularProfile.TryCreate(
            distanceMillimeters: 45_000,
            initialSpeedMillimetersPerSecond: 40_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveTriangularProfile? profile);

        Assert.True(created);
        Assert.NotNull(profile);
        Assert.True(profile.BeginsWithBraking);
        Assert.Equal(0UL, profile.SwitchDistanceMillimeters);
        Assert.Equal(40_000UL, profile.SwitchSpeedMillimetersPerSecond);
    }

    [Fact]
    public void MovingCandidateThatWouldAccelerateAboveCapIsNotTriangular()
    {
        bool created = ShortMoveTriangularProfile.TryCreate(
            distanceMillimeters: 100_000,
            initialSpeedMillimetersPerSecond: 10_000,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveTriangularProfile? profile);

        Assert.False(created);
        Assert.Null(profile);
    }

    [Fact]
    public void ZeroDistanceIsNotATriangularMove()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShortMoveTriangularProfile.TryCreateFromRest(
                distanceMillimeters: 0,
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(300_000),
                out _));
    }

    [Fact]
    public void UnrepresentableSwitchCalculationIsRejected()
    {
        Assert.Throws<OverflowException>(() =>
            ShortMoveTriangularProfile.TryCreateFromRest(
                distanceMillimeters: ulong.MaxValue,
                new ManeuverAcceleration(ulong.MaxValue),
                new ManeuverAcceleration(ulong.MaxValue),
                new ManeuverSpeed(ulong.MaxValue),
                out _));
    }
}
