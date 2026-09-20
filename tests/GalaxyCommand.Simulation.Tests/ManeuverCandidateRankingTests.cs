using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverCandidateRankingTests
{
    [Fact]
    public void DefaultObjectiveIsFastestArrival()
    {
        Assert.Equal(
            ManeuverObjective.FastestArrival,
            default(ManeuverObjective));
    }

    [Fact]
    public void FastestArrivalPrefersEarlierArrivalBeforeShorterPath()
    {
        ManeuverCandidateRank earlier = Rank(100, 1_000, 3, "earlier");
        ManeuverCandidateRank shorter = Rank(101, 100, 1, "shorter");

        AssertPreferred(
            ManeuverObjective.FastestArrival,
            earlier,
            shorter);
    }

    [Fact]
    public void FastestArrivalTiePrefersShorterPath()
    {
        ManeuverCandidateRank shorter = Rank(100, 999, 3, "shorter");
        ManeuverCandidateRank longer = Rank(100, 1_000, 1, "longer");

        AssertPreferred(
            ManeuverObjective.FastestArrival,
            shorter,
            longer);
    }

    [Fact]
    public void ShortestPathPrefersDistanceBeforeEarlierArrival()
    {
        ManeuverCandidateRank shorter = Rank(101, 100, 3, "shorter");
        ManeuverCandidateRank earlier = Rank(100, 1_000, 1, "earlier");

        AssertPreferred(
            ManeuverObjective.ShortestPath,
            shorter,
            earlier);
    }

    [Fact]
    public void ShortestPathTiePrefersEarlierArrival()
    {
        ManeuverCandidateRank earlier = Rank(100, 1_000, 3, "earlier");
        ManeuverCandidateRank later = Rank(101, 1_000, 1, "later");

        AssertPreferred(
            ManeuverObjective.ShortestPath,
            earlier,
            later);
    }

    [Theory]
    [InlineData(ManeuverObjective.FastestArrival)]
    [InlineData(ManeuverObjective.ShortestPath)]
    public void RemainingTiePrefersFewerPhases(ManeuverObjective objective)
    {
        ManeuverCandidateRank fewer = Rank(100, 1_000, 2, "fewer");
        ManeuverCandidateRank more = Rank(100, 1_000, 3, "more");

        AssertPreferred(objective, fewer, more);
    }

    [Theory]
    [InlineData(ManeuverObjective.FastestArrival)]
    [InlineData(ManeuverObjective.ShortestPath)]
    public void FinalTieUsesOrdinalProfileKey(ManeuverObjective objective)
    {
        ManeuverCandidateRank alpha = Rank(100, 1_000, 2, "Alpha");
        ManeuverCandidateRank lowerAlpha = Rank(100, 1_000, 2, "alpha");

        AssertPreferred(objective, alpha, lowerAlpha);
    }

    [Fact]
    public void EqualCandidateRanksCompareEqual()
    {
        ManeuverCandidateRank left = Rank(100, 1_000, 2, "same");
        ManeuverCandidateRank right = Rank(100, 1_000, 2, "same");

        Assert.Equal(
            0,
            ManeuverCandidateRanking.Compare(
                ManeuverObjective.FastestArrival,
                left,
                right));
    }

    [Fact]
    public void CandidateRequiresPositivePhaseCountAndStableProfileKey()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rank(100, 1_000, 0, "profile"));
        Assert.Throws<ArgumentException>(() =>
            new ManeuverProfileKey(" "));
    }

    [Fact]
    public void UnknownObjectiveIsRejected()
    {
        ManeuverCandidateRank left = Rank(100, 1_000, 2, "left");
        ManeuverCandidateRank right = Rank(100, 1_000, 2, "right");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManeuverCandidateRanking.Compare(
                (ManeuverObjective)99,
                left,
                right));
    }

    private static ManeuverCandidateRank Rank(
        ulong arrivesAt,
        UInt128 pathDistance,
        int phaseCount,
        string profileKey) =>
        new(
            new SimulationTime(arrivesAt),
            new ManeuverPathDistance(pathDistance),
            phaseCount,
            new ManeuverProfileKey(profileKey));

    private static void AssertPreferred(
        ManeuverObjective objective,
        ManeuverCandidateRank preferred,
        ManeuverCandidateRank other)
    {
        Assert.True(
            ManeuverCandidateRanking.Compare(
                objective,
                preferred,
                other) < 0);
        Assert.True(
            ManeuverCandidateRanking.Compare(
                objective,
                other,
                preferred) > 0);
    }
}
