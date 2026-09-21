using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class TurnThenSubCruiseManeuverPlanTests
{
    [Fact]
    public void CardinalCourseTurnThenTriangularMovePublishesCombinedSchedule()
    {
        bool created = TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(0, -90),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000),
            out TurnThenSubCruiseManeuverPlan? candidate);

        Assert.True(created);
        TurnThenSubCruiseManeuverPlan plan =
            Assert.IsType<TurnThenSubCruiseManeuverPlan>(candidate);
        Assert.Equal(new ShipHeading(90_000), plan.CourseHeading);
        Assert.Equal(new SimulationTime(2_000), plan.TranslationStartsAt);
        Assert.Equal(new SimulationTime(8_000), plan.EndsAt);
        Assert.Equal(
            AlignedSubCruisePlanKind.Triangular,
            plan.TranslationPlan.Kind);

        ShipKinematicState turned = plan.StateAt(plan.TranslationStartsAt);
        Assert.Equal(Position(0, 0), turned.Position);
        Assert.Equal(ShipVelocity.Zero, turned.Velocity);
        Assert.Equal(new ShipHeading(90_000), turned.Heading);

        ShipKinematicState stopped = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(0, -90), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(new ShipHeading(90_000), stopped.Heading);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);
        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)90_000, rank.PathDistance.Millimeters);
        Assert.Equal(3, rank.PhaseCount);
        Assert.Equal(
            "turn-then-short-move-triangular",
            rank.ProfileKey.Value);
    }

    [Fact]
    public void CardinalCourseTurnThenCappedMovePublishesCombinedRank()
    {
        bool created = TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
            new SimulationTime(100),
            State(0, 0, 0),
            Position(0, -200),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000),
            out TurnThenSubCruiseManeuverPlan? candidate);

        Assert.True(created);
        TurnThenSubCruiseManeuverPlan plan =
            Assert.IsType<TurnThenSubCruiseManeuverPlan>(candidate);
        Assert.Equal(new SimulationTime(2_100), plan.TranslationStartsAt);
        Assert.Equal(new SimulationTime(11_767), plan.EndsAt);
        Assert.Equal(
            AlignedSubCruisePlanKind.CappedSpeed,
            plan.TranslationPlan.Kind);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);
        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)200_000, rank.PathDistance.Millimeters);
        Assert.Equal(4, rank.PhaseCount);
        Assert.Equal("turn-then-capped-speed", rank.ProfileKey.Value);
    }

    [Fact]
    public void ExistingExactCourseNeedsNoTurnThenCandidate()
    {
        bool created = TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 90_000),
            Position(0, -90),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000),
            out TurnThenSubCruiseManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void RoundedTurnThatMissesExactCourseIsNotPublished()
    {
        bool created = TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 0),
            Position(3, -4),
            new ManeuverAcceleration(1_000),
            new ManeuverAcceleration(1_000),
            new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000),
            out TurnThenSubCruiseManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void MovingStartAndCrossSystemDestinationAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0) with { Velocity = new ShipVelocity(1, 0) },
                Position(0, -90),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000),
                new ManeuverTurnRate(45_000),
                out _));
        Assert.Throws<ArgumentException>(() =>
            TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(0),
                        new SpatialCoordinate(-90))),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000),
                new ManeuverTurnRate(45_000),
                out _));
    }

    [Fact]
    public void EvaluationOutsideCombinedScheduleIsRejected()
    {
        TurnThenSubCruiseManeuverPlan.TryCreateFromRest(
            new SimulationTime(100),
            State(0, 0, 0),
            Position(0, -90),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000),
            out TurnThenSubCruiseManeuverPlan? candidate);
        TurnThenSubCruiseManeuverPlan plan =
            Assert.IsType<TurnThenSubCruiseManeuverPlan>(candidate);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(99)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(plan.EndsAt.Milliseconds + 1)));
    }

    private static ShipKinematicState State(long x, long y, uint heading) =>
        new(Position(x, y), ShipVelocity.Zero, new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
