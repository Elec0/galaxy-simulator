using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class WaypointManeuverPlanTests
{
    [Fact]
    public void CollinearWaypointSplitsCompletePlanAtNonzeroVelocity()
    {
        ShipKinematicState start = State(0, 0);
        ExecutableBoundedTerminalManeuverPlan terminal = TerminalPlan(
            start,
            Position(100, 0));

        bool created = WaypointManeuverPlan.TryCreate(
            terminal,
            start,
            Position(100, 0),
            [Position(50, 0)],
            out WaypointManeuverPlan? candidate);

        WaypointManeuverPlan plan = Assert.IsType<WaypointManeuverPlan>(
            candidate);
        Assert.True(created);
        int waypointPhaseIndex = Assert.Single(plan.WaypointPhaseIndices);
        ManeuverScheduledPhase waypointPhase = plan.Plan.Phases[waypointPhaseIndex];
        ShipKinematicState waypointState = plan.Plan.StateAt(
            waypointPhase.EndsAt);
        Assert.True(IsWithinOneMeter(waypointState.Position, Position(50, 0)));
        Assert.NotEqual(ShipVelocity.Zero, waypointState.Velocity);
        Assert.True(waypointPhaseIndex < plan.Plan.Phases.Count - 1);
        Assert.Equal(terminal.EndsAt, plan.Plan.EndsAt);
        Assert.Equal(
            terminal.StateAt(terminal.EndsAt),
            plan.Plan.StateAt(plan.Plan.EndsAt));
    }

    [Fact]
    public void NoncollinearWaypointRequiresDirectionalContinuation()
    {
        ShipKinematicState start = State(0, 0);
        ExecutableBoundedTerminalManeuverPlan terminal = TerminalPlan(
            start,
            Position(100, 0));

        bool created = WaypointManeuverPlan.TryCreate(
            terminal,
            start,
            Position(100, 0),
            [Position(50, 10)],
            out WaypointManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void MultipleOrderedCollinearWaypointsRemainOneCompletePlan()
    {
        ShipKinematicState start = State(0, 0);
        ExecutableBoundedTerminalManeuverPlan terminal = TerminalPlan(
            start,
            Position(100, 0));
        SystemPosition[] waypoints =
        [
            Position(25, 0),
            Position(50, 0),
            Position(75, 0),
        ];

        Assert.True(WaypointManeuverPlan.TryCreate(
            terminal,
            start,
            Position(100, 0),
            waypoints,
            out WaypointManeuverPlan? candidate));
        WaypointManeuverPlan plan = Assert.IsType<WaypointManeuverPlan>(
            candidate);

        Assert.Equal(waypoints.Length, plan.WaypointPhaseIndices.Count);
        for (int index = 0; index < waypoints.Length; index++)
        {
            ManeuverScheduledPhase phase =
                plan.Plan.Phases[plan.WaypointPhaseIndices[index]];
            ShipKinematicState state = plan.Plan.StateAt(phase.EndsAt);
            Assert.True(IsWithinOneMeter(state.Position, waypoints[index]));
            Assert.NotEqual(ShipVelocity.Zero, state.Velocity);
        }

        Assert.Equal(terminal.EndsAt, plan.Plan.EndsAt);
        Assert.Equal(
            terminal.StateAt(terminal.EndsAt),
            plan.Plan.StateAt(plan.Plan.EndsAt));
    }

    [Fact]
    public void NoncollinearRouteKeepsEveryBoundaryWithinSubCruiseSpeedCap()
    {
        ShipKinematicState start = State(0, 0);
        EffectiveShipManeuverCapability capability = new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            ManeuverAcceleration.ParseMetersPerSecondSquared("10"),
            customPassiveDeceleration: null,
            ManeuverSpeed.ParseMetersPerSecond("5"),
            ManeuverSpeed.ParseMetersPerSecond("1000"),
            ManeuverTurnRate.ParseDegreesPerSecond("45"),
            new SimulationDuration(10_000))
            .ResolveForMass(10_000);

        Assert.True(WaypointManeuverPlan.TryCreateRoute(
            SimulationTime.Zero,
            start,
            [Position(100, 0), Position(100, 100)],
            capability,
            ManeuverObjective.FastestArrival,
            out WaypointManeuverPlan? candidate));
        WaypointManeuverPlan plan = Assert.IsType<WaypointManeuverPlan>(candidate);

        Int128 speedCap = capability.MaximumSubCruiseSpeed.MillimetersPerSecond;
        Int128 speedCapSquared = speedCap * speedCap;
        Assert.All(plan.Plan.Phases, phase =>
        {
            ShipVelocity velocity = plan.Plan.StateAt(phase.EndsAt).Velocity;
            Int128 x = velocity.MillimetersPerSecondX;
            Int128 y = velocity.MillimetersPerSecondY;
            Assert.True(x * x + y * y <= speedCapSquared);
        });
    }

    [Fact]
    public void MultipleCornersAlignEveryWaypointVelocityToItsOutgoingLeg()
    {
        SystemPosition first = Position(100, 0);
        SystemPosition second = Position(100, 100);
        SystemPosition terminal = Position(200, 100);

        Assert.True(WaypointManeuverPlan.TryCreateRoute(
            SimulationTime.Zero,
            State(0, 0),
            [first, second, terminal],
            Capability(),
            ManeuverObjective.FastestArrival,
            out WaypointManeuverPlan? candidate));
        WaypointManeuverPlan plan = Assert.IsType<WaypointManeuverPlan>(candidate);

        Assert.Collection(
            plan.WaypointPhaseIndices,
            phaseIndex => AssertWaypoint(
                plan,
                phaseIndex,
                first,
                new ShipHeading(270_000)),
            phaseIndex => AssertWaypoint(
                plan,
                phaseIndex,
                second,
                ShipHeading.Zero));
        Assert.True(ManeuverArrival.EvaluateTerminal(
            plan.Plan.StateAt(plan.Plan.EndsAt),
            terminal,
            requestedHeading: null).IsSatisfied);
    }

    [Fact]
    public void ReducedThrustRouteKeepsShortNoncollinearMoveAnalytic()
    {
        EffectiveShipManeuverCapability capability = new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(1_000_000_000_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
            .ResolveForMass(10_000);

        Assert.True(WaypointManeuverPlan.TryCreateRoute(
            SimulationTime.Zero,
            State(0, 0),
            [Position(2, 0), Position(2, 2)],
            capability,
            ManeuverObjective.FastestArrival,
            out WaypointManeuverPlan? candidate));
        WaypointManeuverPlan plan = Assert.IsType<WaypointManeuverPlan>(candidate);
        int waypointPhase = Assert.Single(plan.WaypointPhaseIndices);
        ShipKinematicState crossing = plan.Plan.StateAt(
            plan.Plan.Phases[waypointPhase].EndsAt);

        Assert.True(ManeuverArrival.IsFlyThroughWaypointReached(
            crossing,
            Position(2, 0)));
        Assert.Equal(
            new ShipHeading(270_000),
            ManeuverHeadingProjection.ResolveCourseHeading(
                crossing.Velocity.MillimetersPerSecondX,
                crossing.Velocity.MillimetersPerSecondY));
        Assert.True(ManeuverArrival.EvaluateTerminal(
            plan.Plan.StateAt(plan.Plan.EndsAt),
            Position(2, 2),
            requestedHeading: null).IsSatisfied);
    }

    private static ExecutableBoundedTerminalManeuverPlan TerminalPlan(
        ShipKinematicState start,
        SystemPosition destination)
    {
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                start,
                destination,
                requestedHeading: null,
                Capability(),
                ManeuverObjective.FastestArrival);
        return Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
            selection.ExecutablePlan);
    }

    private static EffectiveShipManeuverCapability Capability() =>
        GameSessionTestFixture.ManeuverCapability.ResolveForMass(10_000);

    private static ShipKinematicState State(long x, long y) =>
        new(Position(x, y), ShipVelocity.Zero, ShipHeading.Zero);

    private static SystemPosition Position(long x, long y) =>
        GameSessionTestFixture.Position(x, y);

    private static bool IsWithinOneMeter(
        SystemPosition actual,
        SystemPosition expected)
    {
        long deltaX = actual.Position.X.Units - expected.Position.X.Units;
        long deltaY = actual.Position.Y.Units - expected.Position.Y.Units;
        return checked(deltaX * deltaX + deltaY * deltaY) <= 1;
    }

    private static void AssertWaypoint(
        WaypointManeuverPlan plan,
        int phaseIndex,
        SystemPosition waypoint,
        ShipHeading outgoingCourse)
    {
        ManeuverScheduledPhase phase = plan.Plan.Phases[phaseIndex];
        ShipKinematicState state = plan.Plan.StateAt(phase.EndsAt);
        Assert.True(ManeuverArrival.IsFlyThroughWaypointReached(state, waypoint));
        Assert.Equal(
            outgoingCourse,
            ManeuverHeadingProjection.ResolveCourseHeading(
                state.Velocity.MillimetersPerSecondX,
                state.Velocity.MillimetersPerSecondY));
    }
}
