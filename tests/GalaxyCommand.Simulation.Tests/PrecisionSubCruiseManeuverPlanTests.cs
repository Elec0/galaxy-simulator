using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PrecisionSubCruiseManeuverPlanTests
{
    [Fact]
    public void DiagonalTriangularMoveUsesPrecisionRateAndPreservesHeading()
    {
        EffectiveShipManeuverCapability capability = Capability();

        bool created = PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 123_000),
            Position(15, 20),
            capability,
            out PrecisionSubCruiseManeuverPlan? candidate);

        Assert.True(created);
        PrecisionSubCruiseManeuverPlan plan =
            Assert.IsType<PrecisionSubCruiseManeuverPlan>(candidate);
        Assert.Equal(capability.PrecisionAcceleration, plan.PrecisionAcceleration);
        Assert.Equal(
            AlignedSubCruisePlanKind.Triangular,
            plan.TranslationPlan.Kind);
        ShortMoveTriangularPlan translation =
            Assert.IsType<ShortMoveTriangularPlan>(
                plan.TranslationPlan.TriangularPlan);
        Assert.Equal(
            capability.PrecisionAcceleration,
            translation.Profile.Acceleration);
        Assert.Equal(
            capability.PrecisionAcceleration,
            translation.Profile.Braking);
        Assert.Equal(new ShipAcceleration(600, 800), translation.AccelerationPhase.Acceleration);
        Assert.Equal(new SimulationTime(10_000), plan.EndsAt);

        ShipKinematicState stopped = plan.StateAt(plan.EndsAt);
        Assert.Equal(Position(16, 20), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(new ShipHeading(123_000), stopped.Heading);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            stopped,
            Position(15, 20),
            requestedHeading: null).IsSatisfied);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);
        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)25_000, rank.PathDistance.Millimeters);
        Assert.Equal(2, rank.PhaseCount);
        Assert.Equal(
            "precision-short-move-triangular",
            rank.ProfileKey.Value);
    }

    [Fact]
    public void LongPrecisionMovePublishesCappedSpeedRank()
    {
        bool created = PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
            new SimulationTime(100),
            State(0, 0, 270_000),
            Position(600, 800),
            Capability(),
            out PrecisionSubCruiseManeuverPlan? candidate);

        Assert.True(created);
        PrecisionSubCruiseManeuverPlan plan =
            Assert.IsType<PrecisionSubCruiseManeuverPlan>(candidate);
        Assert.Equal(
            AlignedSubCruisePlanKind.CappedSpeed,
            plan.TranslationPlan.Kind);
        Assert.Equal(new SimulationTime(63_433), plan.EndsAt);
        Assert.Equal(new ShipHeading(270_000), plan.StateAt(plan.EndsAt).Heading);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);
        Assert.Equal((UInt128)1_000_000, rank.PathDistance.Millimeters);
        Assert.Equal(3, rank.PhaseCount);
        Assert.Equal("precision-capped-speed", rank.ProfileKey.Value);
    }

    [Fact]
    public void AlreadySatisfiedDestinationProducesNoPrecisionCandidate()
    {
        bool created = PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 45_000),
            Position(1, 0),
            Capability(),
            out PrecisionSubCruiseManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void MovingStartAndCrossSystemDestinationAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(15, 20),
                null!,
                out _));
        Assert.Throws<ArgumentException>(() =>
            PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0) with { Velocity = new ShipVelocity(1, 0) },
                Position(15, 20),
                Capability(),
                out _));
        Assert.Throws<ArgumentException>(() =>
            PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
                SimulationTime.Zero,
                State(0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(15),
                        new SpatialCoordinate(20))),
                Capability(),
                out _));
    }

    [Fact]
    public void EvaluationOutsidePrecisionScheduleIsRejected()
    {
        PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
            new SimulationTime(100),
            State(0, 0, 0),
            Position(15, 20),
            Capability(),
            out PrecisionSubCruiseManeuverPlan? candidate);
        PrecisionSubCruiseManeuverPlan plan =
            Assert.IsType<PrecisionSubCruiseManeuverPlan>(candidate);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(99)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.StateAt(new SimulationTime(plan.EndsAt.Milliseconds + 1)));
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

    private static ShipKinematicState State(long x, long y, uint heading) =>
        new(Position(x, y), ShipVelocity.Zero, new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
