using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class MovingCourseTurnManeuverPlanTests
{
    [Fact]
    public void CourseRotationRetainsTheExistingPrecisionBrakingTrajectory()
    {
        EffectiveShipManeuverCapability capability = Capability();
        ShipKinematicState start = State(10_000);
        MovingCourseTurnManeuverPlan plan = Create(start, Position(0, -2_000), capability);
        var expected = new DecelerationManeuverSegment(ManeuverDecelerationKind.ActiveBrake, new(0), start,
            capability.PrecisionAcceleration);

        ShipKinematicState actual = plan.PrefixStateAt(new(1_000));

        Assert.Equal(expected.StateAt(new(1_000)).Velocity, actual.Velocity);
        Assert.Equal(expected.StateAt(new(1_000)).Position, actual.Position);
        Assert.NotEqual(start.Heading, actual.Heading);
    }

    [Fact]
    public void InitialCourseTurnChangesHeadingWithoutRotatingOrDiscardingVelocity()
    {
        ShipKinematicState start = State(10_000);
        Assert.NotNull(BoundedTerminalManeuverPlanner.SelectWithoutMovingTurn(new(0), start, Position(0, -10_000),
            null, Capability(), ManeuverObjective.FastestArrival).ExecutablePlan);
        MovingCourseTurnManeuverPlan plan = Create(start, Position(0, -10_000));

        Assert.Equal(start, plan.StateAt(plan.StartsAt));
        ShipKinematicState during = plan.PrefixStateAt(new(1_000));
        Assert.Equal(new ShipHeading(45_000), during.Heading);
        Assert.True(during.Velocity.MillimetersPerSecondX > 0);
        Assert.Equal(0, during.Velocity.MillimetersPerSecondY);
        ShipKinematicState braking = plan.BrakingPhase.StateAt(new(1_000));
        Assert.Equal(braking.Position, during.Position);
        Assert.Equal(braking.Velocity, during.Velocity);
        Assert.Equal(plan.BrakingPhase.EndsAt, plan.PrefixEndsAt);
        Assert.True(plan.CourseTurn.EndsAt < plan.PrefixEndsAt);
        Assert.Equal(ShipVelocity.Zero, plan.PrefixStateAt(plan.PrefixEndsAt).Velocity);
        Assert.Equal(plan.PrefixStateAt(plan.PrefixEndsAt), plan.ContinuationPlan.StateAt(plan.PrefixEndsAt));
        Assert.True(ManeuverArrival.EvaluateTerminal(plan.StateAt(plan.EndsAt), plan.Destination, null).IsSatisfied);
    }

    [Theory]
    [InlineData(100UL, 1_000UL)]
    [InlineData(1_000UL, 3_000UL)]
    public void BrakingThatStopsBeforeTheTurnCompletesHoldsPositionWithoutStoppingRotation(ulong speed, ulong precision)
    {
        MovingCourseTurnManeuverPlan plan = Create(State(speed), Position(0, -10_000), Capability(precision));
        Assert.True(plan.BrakingPhase.EndsAt < plan.CourseTurn.EndsAt);
        ShipKinematicState stopped = plan.BrakingPhase.StateAt(plan.BrakingPhase.EndsAt);

        ShipKinematicState during = plan.PrefixStateAt(new(1_000));

        Assert.Equal(stopped.Position, during.Position);
        Assert.Equal(ShipVelocity.Zero, during.Velocity);
        Assert.Equal(new ShipHeading(45_000), during.Heading);
        Assert.Equal(plan.PrefixStateAt(plan.PrefixEndsAt), plan.ContinuationPlan.StateAt(plan.PrefixEndsAt));
        Assert.True(ManeuverArrival.EvaluateTerminal(plan.StateAt(plan.EndsAt), plan.Destination, null).IsSatisfied);
    }

    [Fact]
    public void CruiseDropoutRetainsItsDedicatedBrakeWhileHeadingTurnsImmediately()
    {
        EffectiveShipManeuverCapability capability = Capability();
        ShipKinematicState start = State(capability.CruiseSpeed.MillimetersPerSecond);
        SystemPosition destination = Position(0, -2_000);
        Assert.NotNull(BoundedTerminalManeuverPlanner.SelectWithoutMovingTurn(new(0), start, destination,
            null, capability, ManeuverObjective.FastestArrival).ExecutablePlan);
        MovingCourseTurnManeuverPlan plan = Create(start, destination, capability);
        DecelerationManeuverSegment dropout = Assert.IsType<DecelerationManeuverSegment>(plan.CruiseDropoutPhase);

        Assert.Equal(start, plan.StateAt(plan.StartsAt));
        Assert.Equal(capability.PrimaryAcceleration.MillimetersPerSecondSquared * 2,
            dropout.Deceleration.MillimetersPerSecondSquared);
        Assert.Equal(capability.MaximumSubCruiseSpeed.MillimetersPerSecond, dropout.TargetSpeedMillimetersPerSecond);
        Assert.Equal(ManeuverDecelerationKind.ActiveBrake, dropout.Kind);
        Assert.Equal(dropout.EndsAt, plan.BrakingPhase.StartsAt);
        Assert.Equal(capability.PrecisionAcceleration, plan.BrakingPhase.Deceleration);
        Assert.Equal(plan.BrakingPhase.EndsAt, plan.PrefixEndsAt);
        ShipKinematicState during = plan.PrefixStateAt(new(500));
        Assert.Equal(dropout.StateAt(new(500)).Position, during.Position);
        Assert.Equal(dropout.StateAt(new(500)).Velocity, during.Velocity);
        Assert.Equal(new ShipHeading(22_500), during.Heading);
        Assert.Equal(capability.MaximumSubCruiseSpeed.MillimetersPerSecond,
            ManeuverVector.SpeedMagnitude(plan.PrefixStateAt(dropout.EndsAt).Velocity));
        Assert.Equal(ShipVelocity.Zero, plan.PrefixStateAt(plan.PrefixEndsAt).Velocity);
        Assert.Equal(new ShipHeading(90_000), plan.PrefixStateAt(plan.PrefixEndsAt).Heading);
        Assert.True(ManeuverArrival.EvaluateTerminal(plan.StateAt(plan.EndsAt), plan.Destination, null).IsSatisfied);
    }

    [Fact]
    public void LongCruiseTurnJoinsPrecisionBrakingAtExactDropoutState()
    {
        EffectiveShipManeuverCapability capability = Capability();
        MovingCourseTurnManeuverPlan plan = Create(State(capability.CruiseSpeed.MillimetersPerSecond),
            Position(-10_000, 0), capability);
        DecelerationManeuverSegment dropout = Assert.IsType<DecelerationManeuverSegment>(plan.CruiseDropoutPhase);
        DecelerationManeuverSegment braking = Assert.IsType<DecelerationManeuverSegment>(plan.BrakingPhase);

        Assert.Equal(ManeuverDecelerationKind.ActiveBrake, braking.Kind);
        Assert.Equal(capability.PrecisionAcceleration, braking.Deceleration);
        Assert.Equal(dropout.EndsAt, braking.StartsAt);
        Assert.Equal(dropout.StateAt(dropout.EndsAt).Position, braking.Start.Position);
        Assert.Equal(dropout.StateAt(dropout.EndsAt).Velocity, braking.Start.Velocity);
        Assert.Equal(plan.CourseTurn.StateAt(dropout.EndsAt).Heading, braking.Start.Heading);
        Assert.Equal(braking.Start, plan.PrefixStateAt(dropout.EndsAt));
        Assert.Equal(plan.BrakingPhase.EndsAt, plan.PrefixEndsAt);
        ShipKinematicState arrived = plan.StateAt(plan.EndsAt);
        Assert.True(ManeuverArrival.EvaluateTerminal(arrived, plan.Destination, null).IsSatisfied);
    }

    [Fact]
    public void RestoredCompositionReusesTheSelectedContinuationAndRejectsBrokenJoins()
    {
        MovingCourseTurnManeuverPlan original = Create(State(10_000), Position(0, -10_000));
        MovingCourseTurnManeuverPlan restored = MovingCourseTurnManeuverPlan.RestoreSelected(
            original.Destination, original.RequestedHeading, original.CourseTurn, original.CruiseDropoutPhase,
            original.BrakingPhase, original.ContinuationPlan);
        Assert.Same(original.ContinuationPlan, restored.ContinuationPlan);
        Assert.Equal(original.StateAt(new(1_000)), restored.StateAt(new(1_000)));
        Assert.Equal(original.StateAt(original.EndsAt), restored.StateAt(restored.EndsAt));
        DecelerationManeuverSegment braking = original.BrakingPhase;
        var changedBraking = new DecelerationManeuverSegment(braking.Kind, braking.StartsAt,
            braking.Start with { Heading = new ShipHeading(1) }, braking.Deceleration);
        Assert.Throws<ArgumentException>(() => MovingCourseTurnManeuverPlan.RestoreSelected(original.Destination,
            null, original.CourseTurn, null, changedBraking, original.ContinuationPlan));
        Assert.Throws<ArgumentNullException>(() => MovingCourseTurnManeuverPlan.RestoreSelected(original.Destination,
            null, original.CourseTurn, null, null!, original.ContinuationPlan));
    }

    [Fact]
    public void CourseTurnIsNotInsertedForRestZeroDisplacementOrSatisfiedHeadingTolerance()
    {
        EffectiveShipManeuverCapability capability = Capability();
        Assert.False(MovingCourseTurnManeuverPlan.TryCreate(new(0), State(0), Position(0, -10_000), null,
            capability, ManeuverObjective.FastestArrival, out _));
        Assert.False(MovingCourseTurnManeuverPlan.TryCreate(new(0), State(10_000), Position(0, 0), null,
            capability, ManeuverObjective.FastestArrival, out _));
        Assert.False(MovingCourseTurnManeuverPlan.TryCreate(new(0), State(10_000) with { Heading = new(89_500) },
            Position(0, -10_000), null, capability, ManeuverObjective.FastestArrival, out _));
    }

    [Fact]
    public void EstablishedCourseWithinHeadingToleranceDoesNotRequireAnotherTurn()
    {
        EffectiveShipManeuverCapability capability = Capability();
        var start = new ShipKinematicState(Position(50, 0), ShipVelocity.Zero, new(90_000));
        SystemPosition destination = Position(0, -10_000);
        Assert.True(PrecisionSubCruiseManeuverPlan.TryCreateFromRest(new(0), start, destination, capability, out _));

        StationaryDirectionalPlanSelection selected = StationaryDirectionalManeuverPlanner.Select(
            new(0), start, destination, capability, ManeuverObjective.FastestArrival);

        Assert.Equal(StationaryDirectionalPlanKind.PrecisionSubCruise, selected.Kind);
    }

    private static MovingCourseTurnManeuverPlan Create(ShipKinematicState start, SystemPosition destination,
        EffectiveShipManeuverCapability? capability = null)
    {
        Assert.True(MovingCourseTurnManeuverPlan.TryCreate(new(0), start, destination, null,
            capability ?? Capability(), ManeuverObjective.FastestArrival, out MovingCourseTurnManeuverPlan? plan));
        return Assert.IsType<MovingCourseTurnManeuverPlan>(plan);
    }

    private static EffectiveShipManeuverCapability Capability(ulong precision = 1_000) => new ShipManeuverCapability(
        10_000, new(checked(precision * 10)), new ManeuverAcceleration(100), new(30_000), new(100_000), new(45_000), new(10_000)).ResolveForMass(10_000);

    private static ShipKinematicState State(ulong speed) => new(Position(0, 0), new(checked((long)speed), 0), new(0));
    private static SystemPosition Position(long x, long y) => new(new(1), new(new(x), new(y)));
}
