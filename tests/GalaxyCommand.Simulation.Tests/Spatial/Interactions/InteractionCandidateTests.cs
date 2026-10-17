using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionCandidateTests
{
    [Fact]
    public void CanonicalKeyRetainsDirectedInterestRoles()
    {
        ShipPairInteractionInterest interest = Interest(7, 8, 3);
        var candidate = new InteractionCandidate(new SystemId(2), interest, Contact(10));

        Assert.Equal(new ShipId(3), candidate.Key.Pair.Lower);
        Assert.Equal(new ShipId(8), candidate.Key.Pair.Higher);
        Assert.Equal(interest.Id, candidate.Key.InterestId);
        Assert.Equal(new SimulationTime(10), candidate.Key.Timestamp);
        Assert.Equal(new ShipId(8), candidate.Interest.Source);
        Assert.Equal(new ShipId(3), candidate.Interest.Target);
        Assert.Equal("test-domain", candidate.Interest.DomainKind);
        Assert.Equal("range-entry", candidate.Interest.PurposeId);
        Assert.Equal<ulong>(5, candidate.Interest.RangeUnits);
        Assert.Equal(new EventGeneration(4), candidate.Interest.Generation);
    }

    [Fact]
    public void SweptEvidenceFromOppositeDiscoveryOrdersNormalizesOnce()
    {
        ShipPairInteractionInterest interest = Interest(1, 8, 3);
        var system = new SystemId(1);
        var left = new SystemPosition(system,
            new SpatialPosition(new SpatialCoordinate(-10), new SpatialCoordinate(0)));
        var right = new SystemPosition(system,
            new SpatialPosition(new SpatialCoordinate(10), new SpatialCoordinate(0)));
        var first = new LocalMotionSegment(new MotionId(1), new EventGeneration(1),
            left, right, SimulationTime.Zero, new SimulationTime(20));
        var second = new LocalMotionSegment(new MotionId(2), new EventGeneration(1),
            right, left, SimulationTime.Zero, new SimulationTime(20));
        SweptRangeContact forward = Assert.IsType<SweptRangeContact>(SweptRangeQuery.FindFirstContact(
            first, second, SimulationTime.Zero, new SimulationTime(20), interest.RangeUnits));
        SweptRangeContact reverse = Assert.IsType<SweptRangeContact>(SweptRangeQuery.FindFirstContact(
            second, first, SimulationTime.Zero, new SimulationTime(20), interest.RangeUnits));

        InteractionCandidate candidate = Assert.Single(InteractionCandidateNormalizer.Normalize(
        [
            new InteractionCandidate(system, interest, forward),
            new InteractionCandidate(system, interest, reverse),
        ]));

        Assert.Equal(new SimulationTime(8), candidate.Key.Timestamp);
        Assert.Equal(new ShipId(8), candidate.Interest.Source);
        Assert.Equal(new ShipId(3), candidate.Interest.Target);
        Assert.True(candidate.Contact.InRangeAtTimestamp);
    }

    [Fact]
    public void CandidateOrderUsesEveryAcceptedKeyField()
    {
        InteractionCandidate[] expected =
        [
            Candidate(system: 1, time: 10, first: 1, second: 3, id: 1),
            Candidate(system: 1, time: 10, first: 1, second: 3, id: ulong.MaxValue),
            Candidate(system: 1, time: 10, first: 1, second: 4, id: 1),
            Candidate(system: 1, time: 10, first: 2, second: 3, id: 1),
            Candidate(system: 1, time: 11, first: 1, second: 3, id: 1),
            Candidate(system: 2, time: 0, first: 1, second: 3, id: 1),
        ];

        Assert.Equal(expected, InteractionCandidateNormalizer.Normalize(expected.Reverse()));
    }

    [Fact]
    public void DuplicateDiscoveryReducesToOneCandidate()
    {
        InteractionCandidate candidate = Candidate(1, 10, 8, 3, 1);
        var duplicate = new InteractionCandidate(
            candidate.SystemId, Interest(1, 8, 3), Contact(10));

        InteractionCandidate normalized = Assert.Single(
            InteractionCandidateNormalizer.Normalize([candidate, duplicate, candidate]));

        Assert.Equal(candidate, normalized);
        Assert.Equal(new ShipId(8), normalized.Interest.Source);
    }

    [Fact]
    public void OppositeDirectedInterestsForTheSamePairRemainDistinct()
    {
        InteractionCandidate forward = Candidate(1, 10, 3, 8, 1);
        InteractionCandidate reverse = Candidate(1, 10, 8, 3, 2);

        Assert.Equal(new[] { forward, reverse },
            InteractionCandidateNormalizer.Normalize([reverse, forward, reverse]));
    }

    [Fact]
    public void InputPermutationAndBatchConcatenationDoNotChangeOutput()
    {
        InteractionCandidate[] candidates =
        [
            Candidate(2, 10, 3, 8, 1), Candidate(1, 10, 8, 3, 2),
            Candidate(1, 11, 1, 4, 3), Candidate(1, 10, 3, 8, 1),
        ];
        IReadOnlyList<InteractionCandidate> expected = InteractionCandidateNormalizer.Normalize(candidates);
        foreach (int batchSize in new[] { 1, 2, 3, 4 })
        {
            IEnumerable<InteractionCandidate> discovered = candidates
                .Concat(candidates.Reverse()).Chunk(batchSize).Reverse().SelectMany(batch => batch.Reverse());

            Assert.Equal(expected, InteractionCandidateNormalizer.Normalize(discovered));
        }
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("range")]
    [InlineData("generation")]
    [InlineData("domain")]
    [InlineData("purpose")]
    [InlineData("contact")]
    public void InconsistentEvidenceForOneKeyIsRejectedInEitherOrder(string mismatch)
    {
        InteractionCandidate original = Candidate(1, 10, 3, 8, 1);
        var changed = new InteractionCandidate(new SystemId(1),
            new ShipPairInteractionInterest(new InteractionInterestId(1),
                mismatch == "domain" ? "other-domain" : "test-domain",
                mismatch == "purpose" ? "other-purpose" : "range-entry",
                new ShipId(mismatch == "roles" ? 8UL : 3UL),
                new ShipId(mismatch == "roles" ? 3UL : 8UL),
                mismatch == "range" ? 6UL : 5UL,
                new EventGeneration(mismatch == "generation" ? 5UL : 4UL)),
            new SweptRangeContact(new SimulationTime(10), mismatch != "contact", false));

        Assert.Throws<InvalidOperationException>(() =>
            InteractionCandidateNormalizer.Normalize([original, changed]));
        Assert.Throws<InvalidOperationException>(() =>
            InteractionCandidateNormalizer.Normalize([changed, original]));
    }

    [Fact]
    public void NormalizationReturnsAnIsolatedReadOnlyCollection()
    {
        InteractionCandidate candidate = Candidate(1, 10, 3, 8, 1);
        var input = new List<InteractionCandidate> { candidate };

        IReadOnlyList<InteractionCandidate> output = InteractionCandidateNormalizer.Normalize(input);
        input.Clear();

        Assert.Equal(candidate, Assert.Single(output));
        IList<InteractionCandidate> collection = Assert.IsAssignableFrom<IList<InteractionCandidate>>(output);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
    }

    [Fact]
    public void EmptyInputProducesNoCandidates()
    {
        Assert.Empty(InteractionCandidateNormalizer.Normalize([]));
    }

    [Fact]
    public void InterestIdsUseTheExistingNonzeroSequenceContract()
    {
        var sequence = new IdSequence<InteractionInterestId>();

        Assert.Equal(new InteractionInterestId(1), sequence.Allocate());
        Assert.Equal(new InteractionInterestId(2), sequence.Allocate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionInterestId(0));
    }

    [Fact]
    public void CandidateKeysRejectDefaultIdentitiesAndPairs()
    {
        var pair = new InteractionShipPair(new ShipId(1), new ShipId(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionCandidateKey(
            default, SimulationTime.Zero, pair, new InteractionInterestId(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionCandidateKey(
            new SystemId(1), SimulationTime.Zero, pair, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionCandidateKey(
            new SystemId(1), SimulationTime.Zero, default, new InteractionInterestId(1)));
    }

    [Fact]
    public void CandidatesRejectMissingSystemInterestOrContact()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionCandidate(
            default, Interest(1, 3, 8), Contact(10)));
        Assert.Throws<ArgumentNullException>(() => new InteractionCandidate(
            new SystemId(1), null!, Contact(10)));
        Assert.Throws<ArgumentNullException>(() => new InteractionCandidate(
            new SystemId(1), Interest(1, 3, 8), null!));
    }

    [Fact]
    public void NormalizationRejectsMissingInputAndElements()
    {
        Assert.Throws<ArgumentNullException>(() => InteractionCandidateNormalizer.Normalize(null!));
        Assert.Throws<ArgumentException>(() => InteractionCandidateNormalizer.Normalize([null!]));
    }

    [Fact]
    public void InterestRejectsMissingIdentityAndInvalidParticipantRoles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipPairInteractionInterest(
            default, "test-domain", "purpose", new ShipId(1), new ShipId(2), 0, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipPairInteractionInterest(
            new InteractionInterestId(1), "test-domain", "purpose", default, new ShipId(2), 0, default));
        Assert.Throws<ArgumentException>(() => new ShipPairInteractionInterest(
            new InteractionInterestId(1), "test-domain", "purpose", new ShipId(1), new ShipId(1), 0, default));
    }

    [Theory]
    [InlineData(null, "purpose")]
    [InlineData("", "purpose")]
    [InlineData(" ", "purpose")]
    [InlineData("domain", null)]
    [InlineData("domain", "")]
    [InlineData("domain", " ")]
    public void InterestRejectsMissingDomainAndPurpose(string? domain, string? purpose)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ShipPairInteractionInterest(
            new InteractionInterestId(1), domain!, purpose!,
            new ShipId(1), new ShipId(2), 0, default));
    }

    private static ShipPairInteractionInterest Interest(ulong id, ulong source, ulong target) =>
        new(new InteractionInterestId(id), "test-domain", "range-entry",
            new ShipId(source), new ShipId(target), 5, new EventGeneration(4));

    private static SweptRangeContact Contact(ulong time) => new(new SimulationTime(time), true, false);

    private static InteractionCandidate Candidate(ulong system, ulong time, ulong first, ulong second, ulong id) =>
        new(new SystemId(system), Interest(id, first, second), Contact(time));
}
