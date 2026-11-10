using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionRangeForecastTests
{
    [Theory]
    [InlineData(100, 50, 61)]
    [InlineData(101, 41, 61)]
    public void InclusiveBoundaryDoesNotInterruptUntilTheFirstStrictOutsideMillisecond(ulong duration, ulong start, ulong expected)
    {
        var motion = new LocalMotionSegment(new(1), default, Position(0), Position(100), new(0), new(duration));
        Assert.Equal(new SimulationTime(expected), CargoCollectionRangeForecast.FirstStrictExit(
            InteractionPhaseCurve.From(motion), Position(50), new(start), new(duration), 10));
    }

    [Fact]
    public void CompletionAtInclusiveEqualityHasNoEarlierExit()
    {
        var motion = new LocalMotionSegment(new(1), default, Position(0), Position(100), new(0), new(100));
        Assert.Null(CargoCollectionRangeForecast.FirstStrictExit(InteractionPhaseCurve.From(motion), Position(50), new(50), new(60), 10));
    }

    [Fact]
    public void LeavingAndReenteringBeforeCompletionStillFindsTheFirstExit()
    {
        var motion = new AnalyticManeuverSegment(new(0), new(10000),
            new ShipKinematicState(Position(0), new ShipVelocity(10000, 0), ShipHeading.Zero),
            new ShipAcceleration(-2000, 0), ShipAngularRate.Zero);
        Assert.Equal(Position(0), motion.StateAt(new(10000)).Position);
        Assert.Equal(new SimulationTime(1128), CargoCollectionRangeForecast.FirstStrictExit(
            InteractionPhaseCurve.From(motion), Position(0), new(0), new(10000), 10));
    }

    [Fact]
    public void TangencyAndConstantEqualityDoNotInterrupt()
    {
        var motion = new AnalyticManeuverSegment(new(0), new(10000),
            new ShipKinematicState(Position(-15), new ShipVelocity(10000, 0), ShipHeading.Zero),
            new ShipAcceleration(-2000, 0), ShipAngularRate.Zero);
        Assert.Null(CargoCollectionRangeForecast.FirstStrictExit(InteractionPhaseCurve.From(motion), Position(0), new(3000), new(8000), 10));
        Assert.Null(CargoCollectionRangeForecast.FirstStrictExit(InteractionPhaseCurve.Fixed(Position(10), new(0), new(100)),
            Position(0), new(0), new(100), 10));
    }

    [Fact]
    public void CurrentOutsideAndDifferentSystemAreImmediateAndUnknownFutureIsNotExtrapolated()
    {
        InteractionPhaseCurve curve = InteractionPhaseCurve.Fixed(Position(20), new(0), new(5));
        Assert.Equal(new SimulationTime(0), CargoCollectionRangeForecast.FirstStrictExit(curve, Position(0), new(0), new(100), 10));
        Assert.Equal(new SimulationTime(0), CargoCollectionRangeForecast.FirstStrictExit(curve, new(new(2), Position(20).Position), new(0), new(100), 10));
        Assert.Null(CargoCollectionRangeForecast.FirstStrictExit(curve, Position(20), new(0), new(100), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => CargoCollectionRangeForecast.FirstStrictExit(curve, Position(20), new(5), new(0), 10));
    }

    private static SystemPosition Position(long x) => new(new(1), new(new(x), new(0)));
}
