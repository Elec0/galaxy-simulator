using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ExecutableManeuverPhaseScheduleTests
{
    [Fact]
    public void TerminalSettlePublishesOneZeroDurationPhase()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectWithCapability(
            new SimulationTime(125),
            State(0, 0, 500, 0, 0),
            Position(0, 0),
            requestedHeading: null);

        AssertSchedule(plan, ManeuverPhaseKind.TerminalSettle);
        Assert.Equal(plan.StartsAt, plan.Phases[0].EndsAt);
    }

    [Fact]
    public void TurnThenPrimaryPublishesEveryOrderedPhase()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 0),
            Position(0, -90),
            requestedHeading: null);

        AssertSchedule(
            plan,
            ManeuverPhaseKind.Turn,
            ManeuverPhaseKind.Accelerate,
            ManeuverPhaseKind.ActiveBrake);
    }

    [Fact]
    public void CappedPlanPublishesAccelerateTravelAndBrakePhases()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 0),
            Position(500, 0),
            requestedHeading: null);

        AssertSchedule(
            plan,
            ManeuverPhaseKind.Accelerate,
            ManeuverPhaseKind.CappedSpeedTravel,
            ManeuverPhaseKind.ActiveBrake);
    }

    [Fact]
    public void CruisePlanPublishesSpoolCruiseAndDropoutApproach()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 0),
            Position(100_000, 0),
            requestedHeading: null);

        AssertSchedule(
            plan,
            ManeuverPhaseKind.Accelerate,
            ManeuverPhaseKind.MovingSpool,
            ManeuverPhaseKind.CruiseTravel,
            ManeuverPhaseKind.ActiveBrake);
    }

    [Fact]
    public void CappedPlanAtSpeedOmitsAccelerationPhase()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectLegacy(
            State(0, 0, 30_000, 0, 0),
            Position(100, 0),
            maximumSpeed: 30_000);

        AssertSchedule(
            plan,
            ManeuverPhaseKind.CappedSpeedTravel,
            ManeuverPhaseKind.ActiveBrake);
    }

    [Fact]
    public void ImmediateBrakingPublishesOnlyActiveBrake()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectLegacy(
            State(0, 0, 30_000, 0, 0),
            Position(45, 0),
            maximumSpeed: 300_000);

        AssertSchedule(plan, ManeuverPhaseKind.ActiveBrake);
    }

    [Fact]
    public void BrakeThenStationaryCompositionRetainsBothSchedules()
    {
        BoundedTerminalPlanSelection selection = BoundedTerminalManeuverPlanner.SelectWithoutMovingTurn(
            SimulationTime.Zero,
            State(0, 0, 0, 2_000, 0),
            Position(90, 2),
            requestedHeading: null, Capability(), ManeuverObjective.FastestArrival);
        ExecutableBoundedTerminalManeuverPlan plan = Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(selection.ExecutablePlan);

        AssertSchedule(
            plan,
            ManeuverPhaseKind.ActiveBrake,
            ManeuverPhaseKind.Accelerate,
            ManeuverPhaseKind.ActiveBrake);
    }

    [Fact]
    public void FinalHeadingTurnFollowsMovingTranslation()
    {
        ExecutableBoundedTerminalManeuverPlan plan = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 1_000, 0, 0),
            Position(90, 0),
            new ShipHeading(90_000));

        AssertSchedule(
            plan,
            ManeuverPhaseKind.Accelerate,
            ManeuverPhaseKind.ActiveBrake,
            ManeuverPhaseKind.Turn);
    }

    [Fact]
    public void MovingCourseTurnScheduleRetainsBothConcurrentCompletionsBeforeContinuation()
    {
        ShipKinematicState start = State(0, 0, 1_000, 0, 90_000);
        BoundedTerminalPlanSelection selection = BoundedTerminalManeuverPlanner.Select(
            new(125), start, Position(90, 0), requestedHeading: null, Capability(), ManeuverObjective.FastestArrival);
        MovingCourseTurnManeuverPlan composite = Assert.IsType<MovingCourseTurnManeuverPlan>(selection.MovingCourseTurnPlan);
        ExecutableBoundedTerminalManeuverPlan executable = Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(selection.ExecutablePlan);
        SimulationTime[] prefixBoundaries = new[] { composite.StartsAt, composite.CourseTurn.EndsAt,
                composite.BrakingPhase.EndsAt }.Distinct().Order().ToArray();
        Assert.Equal(executable.StartsAt, executable.Phases[0].StartsAt);
        Assert.Equal(executable.EndsAt, executable.Phases[^1].EndsAt);
        for (int index = 1; index < executable.Phases.Count; index++)
            Assert.Equal(executable.Phases[index - 1].EndsAt, executable.Phases[index].StartsAt);
        Assert.Equal(prefixBoundaries, executable.Phases.Where(phase => phase.StartsAt < composite.PrefixEndsAt)
            .Select(phase => phase.StartsAt).Append(composite.PrefixEndsAt));
        Assert.Equal(composite.PrefixEndsAt, composite.ContinuationPlan.StartsAt);
        Assert.Equal(composite.StateAt(composite.PrefixEndsAt), composite.ContinuationPlan.StateAt(composite.ContinuationPlan.StartsAt));
        Assert.Equal(start, executable.StateAt(executable.StartsAt));
        foreach (SimulationTime boundary in executable.Phases.Select(phase => phase.EndsAt))
            Assert.Equal(composite.StateAt(boundary), executable.StateAt(boundary));
        Assert.True(ManeuverArrival.EvaluateTerminal(executable.StateAt(executable.EndsAt), Position(90, 0),
            requestedHeading: null).IsSatisfied);
    }

    private static void AssertSchedule(
        ExecutableBoundedTerminalManeuverPlan plan,
        params ManeuverPhaseKind[] expectedKinds)
    {
        Assert.Equal(expectedKinds, plan.Phases.Select(phase => phase.Kind));
        Assert.Equal(plan.StartsAt, plan.Phases[0].StartsAt);
        Assert.Equal(plan.EndsAt, plan.Phases[^1].EndsAt);
        for (int index = 1; index < plan.Phases.Count; index++)
        {
            Assert.Equal(
                plan.Phases[index - 1].EndsAt,
                plan.Phases[index].StartsAt);
        }
    }

    private static ExecutableBoundedTerminalManeuverPlan SelectWithCapability(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading)
    {
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                startsAt,
                start,
                destination,
                requestedHeading,
                Capability(),
                ManeuverObjective.FastestArrival);
        return Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
            selection.ExecutablePlan);
    }

    private static ExecutableBoundedTerminalManeuverPlan SelectLegacy(
        ShipKinematicState start,
        SystemPosition destination,
        ulong maximumSpeed)
    {
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                start,
                destination,
                requestedHeading: null,
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(maximumSpeed),
                new ManeuverTurnRate(45_000));
        return Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
            selection.ExecutablePlan);
    }

    private static EffectiveShipManeuverCapability Capability() =>
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(10_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
        .ResolveForMass(effectiveMassKilograms: 10_000);

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
