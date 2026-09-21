using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShortMoveTriangularPlanTests
{
    [Fact]
    public void EastwardMoveSchedulesAccelerationThenActiveBraking()
    {
        bool created = ShortMoveTriangularPlan.TryCreateFromRest(
            new SimulationTime(100),
            State(0, 0, 123_000),
            Position(100, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? candidate);

        Assert.True(created);
        ShortMoveTriangularPlan plan = Assert.IsType<ShortMoveTriangularPlan>(candidate);
        Assert.Equal(new SimulationTime(3_262), plan.SwitchesAt);
        Assert.Equal(new SimulationTime(6_424), plan.EndsAt);
        Assert.Equal(new ShipAcceleration(10_000, 0), plan.AccelerationPhase.Acceleration);
        Assert.Equal(ManeuverDecelerationKind.ActiveBrake, plan.BrakingPhase.Kind);

        ShipKinematicState switchState = plan.StateAt(plan.SwitchesAt);
        Assert.Equal(Position(50, 0), switchState.Position);
        Assert.Equal(new ShipVelocity(31_620, 0), switchState.Velocity);
        Assert.Equal(new ShipHeading(123_000), switchState.Heading);

        ShipKinematicState stopped = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(100, 0), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(new ShipHeading(123_000), stopped.Heading);
    }

    [Fact]
    public void DiagonalMoveNormalizesAccelerationTowardDestination()
    {
        bool created = ShortMoveTriangularPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 270_000),
            Position(3, 4),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? candidate);

        Assert.True(created);
        ShortMoveTriangularPlan plan = Assert.IsType<ShortMoveTriangularPlan>(candidate);
        Assert.Equal(new ShipAcceleration(600, 800), plan.AccelerationPhase.Acceleration);
        Assert.Equal(Position(3, 4), plan.StateAt(plan.EndsAt).Position);
        Assert.Equal(ShipVelocity.Zero, plan.StateAt(plan.EndsAt).Velocity);
        Assert.Equal(new ShipHeading(270_000), plan.StateAt(plan.EndsAt).Heading);
    }

    [Fact]
    public void NegativeDirectionUsesSignedAccelerationComponents()
    {
        bool created = ShortMoveTriangularPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(10, 10, 0),
            Position(7, 6),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? candidate);

        Assert.True(created);
        ShortMoveTriangularPlan plan = Assert.IsType<ShortMoveTriangularPlan>(candidate);
        Assert.Equal(new ShipAcceleration(-600, -800), plan.AccelerationPhase.Acceleration);
        Assert.Equal(Position(7, 6), plan.StateAt(plan.EndsAt).Position);
    }

    [Fact]
    public void AlignedMovingStartAcceleratesOnlyToRemainingSwitchSpeed()
    {
        bool created = ShortMoveTriangularPlan.TryCreateAligned(
            new SimulationTime(100),
            MovingState(0, 0, 10_000, 0, 45_000),
            Position(100, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? candidate);

        Assert.True(created);
        ShortMoveTriangularPlan plan = Assert.IsType<ShortMoveTriangularPlan>(candidate);
        Assert.Equal(10_000UL, plan.Profile.InitialSpeedMillimetersPerSecond);
        Assert.False(plan.Profile.BeginsWithBraking);
        Assert.Equal(new SimulationTime(2_340), plan.SwitchesAt);
        Assert.Equal(new SimulationTime(5_580), plan.EndsAt);

        ShipKinematicState switchState = plan.StateAt(plan.SwitchesAt);
        Assert.Equal(Position(47, 0), switchState.Position);
        Assert.Equal(new ShipVelocity(32_400, 0), switchState.Velocity);

        ShipKinematicState stopped = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(99, 0), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(new ShipHeading(45_000), stopped.Heading);
    }

    [Theory]
    [InlineData(30_000, 0, 45)]
    [InlineData(0, 1_000, 100)]
    [InlineData(-1_000, 0, 100)]
    public void ImmediateBrakeSidewaysOrAwayMotionUsesAnotherPlan(
        long velocityX,
        long velocityY,
        long destinationX)
    {
        bool created = ShortMoveTriangularPlan.TryCreateAligned(
            SimulationTime.Zero,
            MovingState(0, 0, velocityX, velocityY, 0),
            Position(destinationX, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void MoveWithinArrivalToleranceNeedsNoTriangularPlan()
    {
        bool created = ShortMoveTriangularPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(1, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void CandidateThatNeedsCappedTravelIsNotTriangular()
    {
        bool created = ShortMoveTriangularPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(100, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveTriangularPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void PlanRejectsNonzeroVelocityOrCrossSystemDestination()
    {
        Assert.Throws<ArgumentException>(() =>
            ShortMoveTriangularPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0) with { Velocity = new ShipVelocity(1, 0) },
                Position(100, 0),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(300_000),
                out _));
        Assert.Throws<ArgumentException>(() =>
            ShortMoveTriangularPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(100),
                        new SpatialCoordinate(0))),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(300_000),
                out _));
    }

    [Fact]
    public void PlanRejectsEvaluationOutsideItsScheduledBoundaries()
    {
        ShortMoveTriangularPlan.TryCreateFromRest(
            new SimulationTime(100),
            State(0, 0, 0),
            Position(100, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveTriangularPlan? candidate);
        ShortMoveTriangularPlan plan = Assert.IsType<ShortMoveTriangularPlan>(candidate);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(99)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(plan.EndsAt.Milliseconds + 1)));
    }

    private static ShipKinematicState State(long x, long y, uint heading) =>
        new(Position(x, y), ShipVelocity.Zero, new ShipHeading(heading));

    private static ShipKinematicState MovingState(
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
