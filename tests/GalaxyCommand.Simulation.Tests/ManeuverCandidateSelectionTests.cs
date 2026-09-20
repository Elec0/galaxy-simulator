using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverCandidateSelectionTests
{
    [Fact]
    public void FastestArrivalSelectionIsIndependentOfInputOrder()
    {
        ManeuverCandidateRank earliest = Rank(100, 1_000, 3, "earliest");
        ManeuverCandidateRank shorter = Rank(101, 100, 1, "shorter");
        ManeuverCandidateRank later = Rank(102, 50, 1, "later");

        Assert.Equal(
            earliest,
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                [earliest, shorter, later]));
        Assert.Equal(
            earliest,
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                [later, shorter, earliest]));
    }

    [Fact]
    public void ShortestPathSelectionIsIndependentOfInputOrder()
    {
        ManeuverCandidateRank earlier = Rank(100, 1_000, 1, "earlier");
        ManeuverCandidateRank shorter = Rank(101, 100, 3, "shorter");
        ManeuverCandidateRank shortest = Rank(102, 50, 3, "shortest");

        Assert.Equal(
            shortest,
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.ShortestPath,
                [earlier, shorter, shortest]));
        Assert.Equal(
            shortest,
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.ShortestPath,
                [shortest, shorter, earlier]));
    }

    [Fact]
    public void StableProfileKeyBreaksFinalTieRegardlessOfInputOrder()
    {
        ManeuverCandidateRank alpha = Rank(100, 1_000, 2, "alpha");
        ManeuverCandidateRank beta = Rank(100, 1_000, 2, "beta");

        Assert.Equal(
            alpha,
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                [alpha, beta]));
        Assert.Equal(
            alpha,
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                [beta, alpha]));
    }

    [Fact]
    public void EmptyCandidateSetIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                []));
    }

    [Fact]
    public void NullCandidateSetOrEntryIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                null!));
        Assert.Throws<ArgumentException>(() =>
            ManeuverCandidateSelection.SelectPreferred(
                ManeuverObjective.FastestArrival,
                [null!]));
    }

    [Fact]
    public void UnknownObjectiveIsRejectedForSingleCandidate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManeuverCandidateSelection.SelectPreferred(
                (ManeuverObjective)99,
                [Rank(100, 1_000, 2, "only")]));
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
}
