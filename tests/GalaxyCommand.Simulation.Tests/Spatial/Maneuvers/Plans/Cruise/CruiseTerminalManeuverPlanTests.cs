using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CruiseTerminalManeuverPlanTests
{
    [Fact]
    public void LongCardinalMovePublishesCompleteCruiseSchedule()
    {
        bool created = CruiseTerminalManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 180_000),
            Position(100_000, 0),
            new ShipHeading(90_000),
            Capability(),
            out CruiseTerminalManeuverPlan? candidate);

        Assert.True(created);
        CruiseTerminalManeuverPlan plan =
            Assert.IsType<CruiseTerminalManeuverPlan>(candidate);
        Assert.Equal(new ShipHeading(0), plan.CourseHeading);
        Assert.Equal(new SimulationTime(4_000), plan.AccelerationStartsAt);
        Assert.Equal(new SimulationTime(34_000), plan.SpoolStartsAt);
        Assert.Equal(new SimulationTime(44_000), plan.CruiseStartsAt);
        Assert.Equal(new SimulationTime(91_500), plan.DropoutAt);
        Assert.Equal(new SimulationTime(391_500), plan.TranslationEndsAt);
        Assert.Equal(new SimulationTime(393_500), plan.EndsAt);

        ShipKinematicState cruise = plan.StateAt(plan.CruiseStartsAt);
        Assert.Equal(Position(7_500, 0), cruise.Position);
        Assert.Equal(new ShipVelocity(1_000_000, 0), cruise.Velocity);
        Assert.Equal(new ShipHeading(0), cruise.Heading);

        ShipKinematicState dropout = plan.StateAt(plan.DropoutAt);
        Assert.Equal(Position(55_000, 0), dropout.Position);
        Assert.Equal(new ShipVelocity(300_000, 0), dropout.Velocity);
        Assert.Equal(new ShipHeading(0), dropout.Heading);

        ShipKinematicState arrived = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(100_000, 0), arrived.Position);
        Assert.Equal(ShipVelocity.Zero, arrived.Velocity);
        Assert.Equal(new ShipHeading(90_000), arrived.Heading);
    }

    [Fact]
    public void DiagonalCourseUsesCordicForwardProjection()
    {
        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            60_000_000,
            -80_000_000);
        bool created = CruiseTerminalManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            new ShipKinematicState(
                Position(0, 0),
                ShipVelocity.Zero,
                course),
            Position(60_000, -80_000),
            requestedHeading: null,
            Capability(),
            out CruiseTerminalManeuverPlan? candidate);

        Assert.True(created);
        CruiseTerminalManeuverPlan plan =
            Assert.IsType<CruiseTerminalManeuverPlan>(candidate);
        Assert.Equal(
            ManeuverHeadingProjection.ProjectForward(
                plan.CourseHeading,
                Capability().PrimaryAcceleration),
            Assert.IsType<AnalyticManeuverSegment>(
                plan.AccelerationPhase).Acceleration);
        Assert.Equal(
            ManeuverHeadingProjection.ProjectForwardVelocity(
                plan.CourseHeading,
                Capability().CruiseSpeed),
            plan.StateAt(plan.CruiseStartsAt).Velocity);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            plan.StateAt(plan.EndsAt),
            Position(60_000, -80_000),
            requestedHeading: null).IsSatisfied);
    }

    [Fact]
    public void RouteWithoutPositiveCruiseIntervalIsRejected()
    {
        bool created = CruiseTerminalManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(50_000, 0),
            requestedHeading: null,
            Capability(),
            out CruiseTerminalManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void ManeuverSpeedStartSchedulesFullSpoolWithoutAcceleration()
    {
        var startsAt = new SimulationTime(35_000);
        bool created = CruiseTerminalManeuverPlan.TryCreateFromManeuverSpeed(
            startsAt,
            new ShipKinematicState(
                Position(6_000, 0),
                new ShipVelocity(300_000, 0),
                ShipHeading.Zero),
            Position(200_000, 0),
            requestedHeading: null,
            Capability(),
            out CruiseTerminalManeuverPlan? candidate);

        Assert.True(created);
        CruiseTerminalManeuverPlan plan =
            Assert.IsType<CruiseTerminalManeuverPlan>(candidate);
        Assert.Null(plan.AccelerationPhase);
        Assert.Equal(startsAt, plan.StartsAt);
        Assert.Equal(startsAt, plan.SpoolStartsAt);
        Assert.Equal(new SimulationTime(45_000), plan.CruiseStartsAt);
        Assert.Equal(
            new ShipVelocity(1_000_000, 0),
            plan.StateAt(plan.CruiseStartsAt).Velocity);
    }

    [Fact]
    public void MovingStartAndCrossSystemDestinationAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            CruiseTerminalManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0) with
                {
                    Velocity = new ShipVelocity(1, 0),
                },
                Position(100_000, 0),
                requestedHeading: null,
                Capability(),
                out _));
        Assert.Throws<ArgumentException>(() =>
            CruiseTerminalManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(100_000),
                        new SpatialCoordinate(0))),
                requestedHeading: null,
                Capability(),
                out _));
    }

    private static EffectiveShipManeuverCapability Capability() =>
        GameSessionTestFixture.ManeuverCapability.ResolveForMass(10_000);

    private static ShipKinematicState State(long x, long y, uint heading) =>
        new(Position(x, y), ShipVelocity.Zero, new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        GameSessionTestFixture.Position(x, y);
}
