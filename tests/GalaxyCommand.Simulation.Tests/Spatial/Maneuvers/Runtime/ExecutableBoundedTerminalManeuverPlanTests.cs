using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ExecutableBoundedTerminalManeuverPlanTests
{
    [Fact]
    public void WaypointRouteFreezesTheCallerTransitionCollection()
    {
        ShipKinematicState state = State(0, 0, 0, 0, 0);
        var transitions = new List<AnalyticManeuverSegment>
        {
            new(SimulationTime.Zero, new SimulationTime(10), state, ShipAcceleration.Zero, ShipAngularRate.Zero),
        };
        ExecutableBoundedTerminalManeuverPlan route = ExecutableBoundedTerminalManeuverPlan.WaypointRoute(transitions,
            ExecutableBoundedTerminalManeuverPlan.TerminalSettle(new SimulationTime(10), state));
        transitions.Clear();

        Assert.Equal(state, route.StateAt(new SimulationTime(5)));
    }

    [Theory]
    [InlineData(25, 2237)]
    [InlineData(65, 3764)]
    public void ProjectedCurvesRetainAccelerationAndBrakingContactTimes(long target, ulong expected)
    {
        BoundedTerminalPlanSelection selection = BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero, State(0, 0, 0, 0, 0), Position(90, 0), null,
            new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000), new ManeuverTurnRate(45_000));
        ExecutableBoundedTerminalManeuverPlan plan = selection.ExecutablePlan!;
        InteractionPhaseCurve stationary = InteractionPhaseCurve.Fixed(Position(target, 0), plan.StartsAt, plan.EndsAt);

        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(plan.InteractionCurves
            .Select(curve => InteractionPhaseRangeQuery.Find(curve, stationary, plan.StartsAt, plan.EndsAt, 0))
            .Where(contact => contact is not null).OrderBy(contact => contact!.Timestamp).FirstOrDefault());

        Assert.Equal(new SimulationTime(expected), contact.Timestamp);
    }

    [Fact]
    public void TerminalSettleIsAZeroDurationExecutablePlan()
    {
        var startsAt = new SimulationTime(125);
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            startsAt,
            State(0, 0, 500, 0, 0),
            Position(0, 0),
            requestedHeading: null);

        ExecutableBoundedTerminalManeuverPlan plan =
            Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
                selection.ExecutablePlan);

        Assert.Equal(BoundedTerminalPlanKind.TerminalSettle, plan.Kind);
        Assert.Equal(startsAt, plan.StartsAt);
        Assert.Equal(startsAt, plan.EndsAt);
        Assert.Equal(selection.SettledState, plan.StateAt(startsAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(startsAt.Milliseconds - 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(startsAt.Milliseconds + 1)));
    }

    [Fact]
    public void StationaryTurnExposesItsExistingExecutablePlan()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 350_000),
            Position(0, 0),
            new ShipHeading(35_000));
        StationaryTurnManeuverPlan expected =
            Assert.IsType<StationaryTurnManeuverPlan>(
                selection.StationaryTurnPlan);

        AssertAdapts(selection, expected.StartsAt, expected.EndsAt, expected.StateAt);
    }

    [Fact]
    public void StationaryDirectionalExposesItsExistingExecutablePlan()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 90_000),
            Position(25, 0),
            new ShipHeading(90_000));
        StationaryDirectionalManeuverPlan expected =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                selection.StationaryDirectionalPlan?.CompletePlan);

        AssertAdapts(selection, expected.StartsAt, expected.EndsAt, expected.StateAt);
    }

    [Fact]
    public void MovingAlignedExposesItsExistingExecutablePlan()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 1_000, 0, 0),
            Position(90, 0),
            requestedHeading: null);
        MovingAlignedTerminalManeuverPlan expected =
            Assert.IsType<MovingAlignedTerminalManeuverPlan>(
                selection.MovingAlignedTerminalPlan);

        AssertAdapts(selection, expected.StartsAt, expected.EndsAt, expected.StateAt);
    }

    [Fact]
    public void BrakeThenStationaryExposesItsExistingExecutablePlan()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            SimulationTime.Zero,
            State(0, 0, 0, 2_000, 0),
            Position(90, 0),
            new ShipHeading(90_000));
        BrakeThenStationaryDirectionalManeuverPlan expected =
            Assert.IsType<BrakeThenStationaryDirectionalManeuverPlan>(
                selection.BrakeThenStationaryDirectionalPlan);

        AssertAdapts(selection, expected.StartsAt, expected.EndsAt, expected.StateAt);
    }

    [Fact]
    public void LegacyAlignedSelectionExposesItsExistingExecutablePlan()
    {
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0, 0, 123_000),
                Position(90, 0),
                requestedHeading: null,
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000),
                new ManeuverTurnRate(45_000));
        ShortMoveTriangularPlan expected =
            Assert.IsType<ShortMoveTriangularPlan>(
                selection.AlignedSubCruisePlan?.TriangularPlan);

        AssertAdapts(selection, expected.StartsAt, expected.EndsAt, expected.StateAt);
    }

    [Fact]
    public void DirectionalHandoffHasNoExecutablePlan()
    {
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0, 1_000, 0),
                Position(90, 0),
                requestedHeading: null,
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000),
                new ManeuverTurnRate(45_000));

        Assert.Equal(BoundedTerminalPlanKind.DirectionalPlanner, selection.Kind);
        Assert.Null(selection.ExecutablePlan);
    }

    private static void AssertAdapts(
        BoundedTerminalPlanSelection selection,
        SimulationTime expectedStartsAt,
        SimulationTime expectedEndsAt,
        Func<SimulationTime, ShipKinematicState> expectedStateAt)
    {
        ExecutableBoundedTerminalManeuverPlan plan =
            Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
                selection.ExecutablePlan);

        Assert.Equal(selection.Kind, plan.Kind);
        Assert.Equal(expectedStartsAt, plan.StartsAt);
        Assert.Equal(expectedEndsAt, plan.EndsAt);
        Assert.Equal(expectedStateAt(expectedStartsAt), plan.StateAt(plan.StartsAt));
        Assert.Equal(expectedStateAt(expectedEndsAt), plan.StateAt(plan.EndsAt));
    }

    private static BoundedTerminalPlanSelection SelectWithCapability(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading) =>
        BoundedTerminalManeuverPlanner.Select(
            startsAt,
            start,
            destination,
            requestedHeading,
            Capability(),
            ManeuverObjective.FastestArrival);

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
