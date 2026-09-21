using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverPlanRankingTests
{
    [Fact]
    public void StationaryTurnPublishesZeroDistanceSinglePhaseRank()
    {
        StationaryTurnManeuverPlan plan = StationaryTurn();

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);

        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)0, rank.PathDistance.Millimeters);
        Assert.Equal(1, rank.PhaseCount);
        Assert.Equal("stationary-turn", rank.ProfileKey.Value);
    }

    [Fact]
    public void ImmediateBrakingPublishesAnalyticStoppingDistance()
    {
        bool created = ShortMoveImmediateBrakingPlan.TryCreateAligned(
            new SimulationTime(100),
            State(0, 0, 30_000, 0, 123_000),
            Position(45, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(300_000),
            out ShortMoveImmediateBrakingPlan? candidate);
        ShortMoveImmediateBrakingPlan plan =
            Assert.IsType<ShortMoveImmediateBrakingPlan>(candidate);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);

        Assert.True(created);
        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)45_000, rank.PathDistance.Millimeters);
        Assert.Equal(1, rank.PhaseCount);
        Assert.Equal("immediate-braking", rank.ProfileKey.Value);
    }

    [Fact]
    public void TriangularPlanPublishesProfileDistanceAndTwoPhases()
    {
        bool created = ShortMoveTriangularPlan.TryCreateFromRest(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 0),
            Position(90, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out ShortMoveTriangularPlan? candidate);
        ShortMoveTriangularPlan plan =
            Assert.IsType<ShortMoveTriangularPlan>(candidate);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);

        Assert.True(created);
        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)90_000, rank.PathDistance.Millimeters);
        Assert.Equal(2, rank.PhaseCount);
        Assert.Equal("short-move-triangular", rank.ProfileKey.Value);
    }

    [Fact]
    public void CappedPlanFromRestPublishesThreePhaseRank()
    {
        CappedSpeedManeuverPlan plan = CappedPlan(
            initialSpeedMillimetersPerSecond: 0,
            distanceMeters: 200);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);

        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)200_000, rank.PathDistance.Millimeters);
        Assert.Equal(3, rank.PhaseCount);
        Assert.Equal("capped-speed", rank.ProfileKey.Value);
    }

    [Fact]
    public void CappedPlanAtCapPublishesActualTwoPhaseRank()
    {
        CappedSpeedManeuverPlan plan = CappedPlan(
            initialSpeedMillimetersPerSecond: 30_000,
            distanceMeters: 100);

        ManeuverCandidateRank rank = ManeuverPlanRanking.Rank(plan);

        Assert.Equal(plan.EndsAt, rank.ArrivesAt);
        Assert.Equal((UInt128)100_000, rank.PathDistance.Millimeters);
        Assert.Equal(2, rank.PhaseCount);
        Assert.Equal("capped-speed", rank.ProfileKey.Value);
    }

    private static StationaryTurnManeuverPlan StationaryTurn()
    {
        bool created = StationaryTurnManeuverPlan.TryCreate(
            SimulationTime.Zero,
            State(0, 0, 0, 0, 350_000),
            Position(0, 0),
            new ShipHeading(35_000),
            new ManeuverTurnRate(45_000),
            out StationaryTurnManeuverPlan? candidate);

        Assert.True(created);
        return Assert.IsType<StationaryTurnManeuverPlan>(candidate);
    }

    private static CappedSpeedManeuverPlan CappedPlan(
        long initialSpeedMillimetersPerSecond,
        long distanceMeters)
    {
        bool created = CappedSpeedManeuverPlan.TryCreateAligned(
            SimulationTime.Zero,
            State(0, 0, initialSpeedMillimetersPerSecond, 0, 0),
            Position(distanceMeters, 0),
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            out CappedSpeedManeuverPlan? candidate);

        Assert.True(created);
        return Assert.IsType<CappedSpeedManeuverPlan>(candidate);
    }

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
