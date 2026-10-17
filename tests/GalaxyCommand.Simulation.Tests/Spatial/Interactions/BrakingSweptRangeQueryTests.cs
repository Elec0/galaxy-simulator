using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class BrakingSweptRangeQueryTests
{
    [Fact]
    public void BrakingUsesRationalOpposingAccelerationWithoutRoundingItsDirection()
    {
        var braking = Brake(0, 10_000, 2_000);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            braking, Position(16), SimulationTime.Zero, braking.EndsAt, 0));
        Assert.Equal(new SimulationTime(2_000), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
        Assert.False(contact.AlreadyInRange);
    }

    [Fact]
    public void MovingParticipantCanReachTheHeldTargetAfterTheExactStopTime()
    {
        // Stops at 2/3 ms and 2/3 m. The other curve reaches that held point
        // at 5/6 ms, after deceleration has ended but before the scheduled end.
        var braking = Brake(0, 2_000_000, 3_000_000_000);
        var other = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(1),
            State(1, -400_000), ShipAcceleration.Zero, ShipAngularRate.Zero);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            braking, other, SimulationTime.Zero, new SimulationTime(1), 0));
        Assert.Equal(new SimulationTime(1), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void FinalHoldDoesNotInventAReversalContact()
    {
        var braking = Brake(0, 2_000_000, 3_000_000_000);
        // At 1 ms the other curve is at 0.5 m, exactly where unchecked
        // deceleration would reverse to. The held target stays at 2/3 m.
        var other = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(1),
            State(1, -500_000), ShipAcceleration.Zero, ShipAngularRate.Zero);
        Assert.Null(AnalyticSweptRangeQuery.FindFirstContact(braking, other,
            new SimulationTime(1), new SimulationTime(1), 0));
    }

    [Fact]
    public void TwoBrakingParticipantsAndPositiveTargetSpeedAreSupported()
    {
        var first = Brake(0, 10_000, 2_000);
        var second = new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag,
            SimulationTime.Zero, State(32, -10_000), new ManeuverAcceleration(2_000), new ManeuverSpeed(2_000));
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            first, second, SimulationTime.Zero, second.EndsAt, 0));
        Assert.Equal(new SimulationTime(2_000), contact.Timestamp);
    }

    [Fact]
    public void PositiveTargetSpeedStillHoldsTheExactTargetPositionUntilTheBoundary()
    {
        var braking = new DecelerationManeuverSegment(ManeuverDecelerationKind.ActiveBrake,
            SimulationTime.Zero, State(0, 2_000_000), new ManeuverAcceleration(1_500_000_000), new ManeuverSpeed(1_000_000));
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            braking, Position(1), SimulationTime.Zero, braking.EndsAt, 0));
        Assert.Equal(new SimulationTime(1), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
    }

    private static DecelerationManeuverSegment Brake(long x, long speed, ulong rate) => new(
        ManeuverDecelerationKind.ActiveBrake, SimulationTime.Zero, State(x, speed), new ManeuverAcceleration(rate));
    private static ShipKinematicState State(long x, long speed) => new(Position(x), new ShipVelocity(speed, 0), ShipHeading.Zero);
    private static SystemPosition Position(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
