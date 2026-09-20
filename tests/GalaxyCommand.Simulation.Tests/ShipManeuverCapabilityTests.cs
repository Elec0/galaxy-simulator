using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShipManeuverCapabilityTests
{
    [Fact]
    public void VelocityRetainsSignedFixedPointComponents()
    {
        var velocity = new ShipVelocity(-12_345, 67_890);

        Assert.Equal(-12_345, velocity.MillimetersPerSecondX);
        Assert.Equal(67_890, velocity.MillimetersPerSecondY);
        Assert.Equal(ShipVelocity.Zero, new ShipVelocity(0, 0));
    }

    [Theory]
    [InlineData("300", 300_000UL)]
    [InlineData("300.125", 300_125UL)]
    [InlineData("0.001", 1UL)]
    public void SpeedParsesInvariantDecimalIntoExactFixedPointUnits(
        string authored,
        ulong expectedMillimetersPerSecond)
    {
        ManeuverSpeed speed = ManeuverSpeed.ParseMetersPerSecond(authored);

        Assert.Equal(expectedMillimetersPerSecond, speed.MillimetersPerSecond);
    }

    [Fact]
    public void AccelerationAndTurnRateUseTheirOwnFixedPointUnits()
    {
        ManeuverAcceleration acceleration =
            ManeuverAcceleration.ParseMetersPerSecondSquared("12.345");
        ManeuverTurnRate turnRate =
            ManeuverTurnRate.ParseDegreesPerSecond("45.5");

        Assert.Equal(12_345UL, acceleration.MillimetersPerSecondSquared);
        Assert.Equal(45_500UL, turnRate.MillidegreesPerSecond);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("0.000")]
    [InlineData("1.0000")]
    [InlineData("1e3")]
    [InlineData("1,5")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("340282366920938463463374607431768212")]
    public void FixedPointParsingRejectsNonExactOrNonPositiveValues(string authored)
    {
        Assert.Throws<FormatException>(
            () => ManeuverSpeed.ParseMetersPerSecond(authored));
    }

    [Fact]
    public void AuthoredCapabilityRetainsOnlyValidatedBaseInputs()
    {
        var capability = new ShipManeuverCapability(
            baseMassKilograms: 25_000,
            baseAcceleration: ManeuverAcceleration.ParseMetersPerSecondSquared("20"),
            customPassiveDeceleration:
                ManeuverAcceleration.ParseMetersPerSecondSquared("4.5"),
            maximumSubCruiseSpeed: ManeuverSpeed.ParseMetersPerSecond("300"),
            cruiseSpeed: ManeuverSpeed.ParseMetersPerSecond("1000"),
            turnRate: ManeuverTurnRate.ParseDegreesPerSecond("30"),
            movingSpoolDuration: new SimulationDuration(10_000));

        Assert.Equal(25_000UL, capability.BaseMassKilograms);
        Assert.Equal(20_000UL, capability.BaseAcceleration.MillimetersPerSecondSquared);
        Assert.Equal(
            4_500UL,
            capability.CustomPassiveDeceleration?.MillimetersPerSecondSquared);
        Assert.Equal(300_000UL, capability.MaximumSubCruiseSpeed.MillimetersPerSecond);
        Assert.Equal(1_000_000UL, capability.CruiseSpeed.MillimetersPerSecond);
        Assert.Equal(30_000UL, capability.TurnRate.MillidegreesPerSecond);
        Assert.Equal(10_000UL, capability.MovingSpoolDuration.Milliseconds);
    }

    [Fact]
    public void AuthoredCapabilityRejectsZeroMassOrSpoolDuration()
    {
        ManeuverAcceleration acceleration =
            ManeuverAcceleration.ParseMetersPerSecondSquared("20");
        ManeuverSpeed speed = ManeuverSpeed.ParseMetersPerSecond("300");
        ManeuverTurnRate turnRate = ManeuverTurnRate.ParseDegreesPerSecond("30");

        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipManeuverCapability(
            baseMassKilograms: 0,
            acceleration,
            customPassiveDeceleration: null,
            maximumSubCruiseSpeed: speed,
            cruiseSpeed: speed,
            turnRate,
            movingSpoolDuration: new SimulationDuration(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipManeuverCapability(
            baseMassKilograms: 1,
            acceleration,
            customPassiveDeceleration: null,
            maximumSubCruiseSpeed: speed,
            cruiseSpeed: speed,
            turnRate,
            movingSpoolDuration: SimulationDuration.Zero));
    }

    [Theory]
    [InlineData("0", 0U)]
    [InlineData("45.125", 45_125U)]
    [InlineData("359.999", 359_999U)]
    [InlineData("360", 0U)]
    [InlineData("360.000", 0U)]
    public void HeadingParsesAndCanonicalizesDegrees(
        string authored,
        uint expectedMillidegrees)
    {
        ShipHeading heading = ShipHeading.ParseDegrees(authored);

        Assert.Equal(expectedMillidegrees, heading.Millidegrees);
    }

    [Theory]
    [InlineData("-0.001")]
    [InlineData("360.001")]
    [InlineData("720")]
    public void HeadingRejectsValuesOutsideOneCanonicalRevolution(string authored)
    {
        Assert.Throws<FormatException>(() => ShipHeading.ParseDegrees(authored));
    }

    [Theory]
    [InlineData(10_000U, 350_000U, -20_000)]
    [InlineData(350_000U, 10_000U, 20_000)]
    [InlineData(0U, 180_000U, 180_000)]
    [InlineData(180_000U, 0U, 180_000)]
    [InlineData(25_000U, 25_000U, 0)]
    public void ShortestTurnIsSignedClockwiseWithClockwiseHalfTurnTie(
        uint originMillidegrees,
        uint targetMillidegrees,
        int expectedSignedMillidegrees)
    {
        var origin = new ShipHeading(originMillidegrees);
        var target = new ShipHeading(targetMillidegrees);

        Assert.Equal(expectedSignedMillidegrees, origin.ShortestTurnTo(target));
    }

    [Fact]
    public void ResolveForMassRoundsEachDerivedRateToNearestWithHalfTiesUp()
    {
        ShipManeuverCapability authored = Capability(
            baseAcceleration: "10.005",
            customPassiveDeceleration: null);

        EffectiveShipManeuverCapability effective =
            authored.ResolveForMass(effectiveMassKilograms: 30_000);

        Assert.Equal(3_335UL, effective.PrimaryAcceleration.MillimetersPerSecondSquared);
        Assert.Equal(334UL, effective.PrecisionAcceleration.MillimetersPerSecondSquared);
        Assert.Equal(834UL, effective.PassiveDeceleration.MillimetersPerSecondSquared);
    }

    [Fact]
    public void CustomPassiveDecelerationReplacesDefaultBeforeMassScaling()
    {
        ShipManeuverCapability authored = Capability(
            baseAcceleration: "20",
            customPassiveDeceleration: "4");

        EffectiveShipManeuverCapability effective =
            authored.ResolveForMass(effectiveMassKilograms: 30_000);

        Assert.Equal(6_667UL, effective.PrimaryAcceleration.MillimetersPerSecondSquared);
        Assert.Equal(667UL, effective.PrecisionAcceleration.MillimetersPerSecondSquared);
        Assert.Equal(1_333UL, effective.PassiveDeceleration.MillimetersPerSecondSquared);
        Assert.Equal(authored.MaximumSubCruiseSpeed, effective.MaximumSubCruiseSpeed);
        Assert.Equal(authored.CruiseSpeed, effective.CruiseSpeed);
        Assert.Equal(authored.TurnRate, effective.TurnRate);
        Assert.Equal(authored.MovingSpoolDuration, effective.MovingSpoolDuration);
    }

    [Fact]
    public void ResolveForMassRejectsZeroEffectiveMass()
    {
        ShipManeuverCapability authored = Capability(
            baseAcceleration: "20",
            customPassiveDeceleration: null);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => authored.ResolveForMass(effectiveMassKilograms: 0));
    }

    private static ShipManeuverCapability Capability(
        string baseAcceleration,
        string? customPassiveDeceleration) =>
        new(
            baseMassKilograms: 10_000,
            ManeuverAcceleration.ParseMetersPerSecondSquared(baseAcceleration),
            customPassiveDeceleration is null
                ? null
                : ManeuverAcceleration.ParseMetersPerSecondSquared(
                    customPassiveDeceleration),
            ManeuverSpeed.ParseMetersPerSecond("300"),
            ManeuverSpeed.ParseMetersPerSecond("1000"),
            ManeuverTurnRate.ParseDegreesPerSecond("45"),
            new SimulationDuration(10_000));
}
