using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class RelationshipTests
{
    private static readonly PrincipalId PlayerPrincipal = new(1);
    private static readonly PrincipalId RegionalPrincipal = new(2);

    [Theory]
    [InlineData(-100, StandingBand.Hostile)]
    [InlineData(-50, StandingBand.Adversarial)]
    [InlineData(0, StandingBand.Neutral)]
    [InlineData(50, StandingBand.Favorable)]
    [InlineData(90, StandingBand.Allied)]
    [InlineData(100, StandingBand.Allied)]
    public void StandingPolicyMapsExactValuesToAcceptedBands(
        long value,
        StandingBand expected)
    {
        Assert.Equal(expected, Policy().GetBand(new StandingValue(value)));
    }

    [Fact]
    public void StandingPolicyRejectsInvalidBoundsAndOutOfRangeValues()
    {
        Assert.Throws<ArgumentException>(() => new StandingPolicy(
            new StandingPolicyId("invalid"),
            new StandingValue(-100),
            new StandingValue(100),
            new StandingValue(0),
            new StandingValue(-50),
            new StandingValue(0),
            new StandingValue(90),
            new StandingValue(50)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Policy().GetBand(new StandingValue(101)));
    }

    [Fact]
    public void RelationshipSetupRejectsPrincipalIdentityCollisionsAndBlankNames()
    {
        Assert.Throws<ArgumentException>(() =>
            new PrincipalDefinition(
                PlayerPrincipal,
                new PrincipalContentId("player"),
                " "));
        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            [
                Definition(PlayerPrincipal, "player", "Player"),
                Definition(PlayerPrincipal, "regional", "Regional"),
            ],
            []));
        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            [
                Definition(PlayerPrincipal, "shared", "Player"),
                Definition(RegionalPrincipal, "shared", "Regional"),
            ],
            []));
    }

    [Fact]
    public void RelationshipSetupRejectsUnknownAndDuplicateStandingReferences()
    {
        PrincipalDefinition[] principals =
        [
            Definition(PlayerPrincipal, "player", "Player"),
            Definition(RegionalPrincipal, "regional", "Regional"),
        ];
        var standing = new InitialStandingSetup(
            RegionalPrincipal,
            PlayerPrincipal,
            new StandingValue(25));

        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            principals,
            [
                new InitialStandingSetup(
                    new PrincipalId(99),
                    PlayerPrincipal,
                    new StandingValue(0)),
            ]));
        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            principals,
            [standing, standing]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRelationshipSetup(
            principals,
            [
                new InitialStandingSetup(
                    RegionalPrincipal,
                    PlayerPrincipal,
                    new StandingValue(101)),
            ]));
    }

    [Fact]
    public void GameSessionSetupRejectsAssetOwnedByUnknownPrincipal()
    {
        var ship = new InitialShipSetup(
            GameSessionTestFixture.Entity,
            GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory,
            new PrincipalId(99),
            GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.PlayerController);

        Assert.Throws<ArgumentException>(() => new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [ship],
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 256));
    }

    [Fact]
    public void SnapshotResolvesDirectionalDefaultsAndPreservesCanonicalOrdering()
    {
        RelationshipSetup relationships = CreateRelationshipSetup(
            [
                Definition(RegionalPrincipal, "regional", "Regional"),
                Definition(PlayerPrincipal, "player", "Player"),
            ],
            [
                new InitialStandingSetup(
                    RegionalPrincipal,
                    PlayerPrincipal,
                    new StandingValue(75)),
            ]);
        var session = new GameSession(
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [],
                relationships,
                factRetentionCapacity: 256),
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));

        RelationshipSnapshot snapshot = session.CaptureSnapshot().Relationships;

        Assert.Equal(PlayerPrincipal, snapshot.PlayerPrincipalId);
        Assert.Equal(new StandingPolicyId("test-standing"), snapshot.StandingPolicyId);
        Assert.Equal(
            [PlayerPrincipal, RegionalPrincipal],
            snapshot.Principals.Select(principal => principal.Id));
        Assert.Collection(
            snapshot.Standings,
            standing =>
            {
                Assert.Equal(PlayerPrincipal, standing.AssessingPrincipalId);
                Assert.Equal(RegionalPrincipal, standing.SubjectPrincipalId);
                Assert.Equal(new StandingValue(0), standing.Value);
                Assert.Equal(StandingBand.Neutral, standing.Band);
            },
            standing =>
            {
                Assert.Equal(RegionalPrincipal, standing.AssessingPrincipalId);
                Assert.Equal(PlayerPrincipal, standing.SubjectPrincipalId);
                Assert.Equal(new StandingValue(75), standing.Value);
                Assert.Equal(StandingBand.Favorable, standing.Band);
            });
    }

    [Fact]
    public void RelationshipSnapshotCollectionsCannotBeModified()
    {
        RelationshipSnapshot snapshot = GameSessionTestFixture.Create()
            .CaptureSnapshot()
            .Relationships;
        var principals = Assert.IsAssignableFrom<IList<PrincipalSnapshot>>(
            snapshot.Principals);
        var standings = Assert.IsAssignableFrom<IList<StandingSnapshot>>(
            snapshot.Standings);

        Assert.Throws<NotSupportedException>(() => principals.Add(
            new PrincipalSnapshot(
                new PrincipalId(99),
                new PrincipalContentId("injected"),
                "Injected")));
        Assert.Throws<NotSupportedException>(() => standings.Add(
            new StandingSnapshot(
                PlayerPrincipal,
                RegionalPrincipal,
                new StandingValue(0),
                StandingBand.Neutral)));
    }

    [Fact]
    public void StandingBatchReducesContributionsInStableOrderAndEmitsOneFact()
    {
        GameSession first = CreateRelationshipSession();
        GameSession permuted = CreateRelationshipSession();
        StandingChangeProposal lower = Proposal(
            PlayerPrincipal,
            RegionalPrincipal,
            contributionId: 1,
            delta: 40);
        StandingChangeProposal higher = Proposal(
            PlayerPrincipal,
            RegionalPrincipal,
            contributionId: 2,
            delta: 20);

        StandingChangeBatchResult.Applied result = Assert.IsType<
            StandingChangeBatchResult.Applied>(first.CommitStandingChanges(
                new StandingChangeBatch(
                    BatchId(1),
                    [higher, lower])));
        StandingChangeBatchResult.Applied permutedResult = Assert.IsType<
            StandingChangeBatchResult.Applied>(permuted.CommitStandingChanges(
                new StandingChangeBatch(
                    BatchId(1),
                    [lower, higher])));

        StandingChangeOutcome outcome = Assert.Single(result.Outcomes);
        Assert.Equal(PlayerPrincipal, outcome.AssessingPrincipalId);
        Assert.Equal(RegionalPrincipal, outcome.SubjectPrincipalId);
        Assert.Equal(new StandingValue(0), outcome.PriorValue);
        Assert.Equal(StandingBand.Neutral, outcome.PriorBand);
        Assert.Equal(60, outcome.CombinedDelta);
        Assert.Equal(new StandingValue(60), outcome.ResultingValue);
        Assert.Equal(StandingBand.Favorable, outcome.ResultingBand);
        Assert.True(outcome.Changed);
        Assert.Equal(
            [1UL, 2UL],
            outcome.Contributions.Select(contribution => contribution.Id.Value));
        Assert.Equal(
            result.Outcomes.Select(OutcomeValues),
            permutedResult.Outcomes.Select(OutcomeValues));
        Assert.Equal(
            first.CaptureSnapshot().Relationships.Standings,
            permuted.CaptureSnapshot().Relationships.Standings);

        GameFactEnvelope envelope = Assert.Single(ReadAllFacts(first));
        var cause = Assert.IsType<StandingChangeFactCause>(envelope.Cause);
        Assert.Equal(BatchId(1), cause.BatchId);
        var fact = Assert.IsType<StandingChangedFact>(envelope.Fact);
        Assert.Equal(outcome.AssessingPrincipalId, fact.AssessingPrincipalId);
        Assert.Equal(outcome.SubjectPrincipalId, fact.SubjectPrincipalId);
        Assert.Equal(outcome.PriorValue, fact.PriorValue);
        Assert.Equal(outcome.ResultingValue, fact.ResultingValue);
        Assert.Equal(outcome.CombinedDelta, fact.CombinedDelta);
        Assert.Equal(
            [1UL, 2UL],
            fact.Contributions.Select(contribution => contribution.Id.Value));
    }

    [Fact]
    public void StandingBatchOrdersDirectionalOutcomesAndFactsByPrincipalPair()
    {
        GameSession session = CreateRelationshipSession();

        StandingChangeBatchResult.Applied result = Assert.IsType<
            StandingChangeBatchResult.Applied>(session.CommitStandingChanges(
                new StandingChangeBatch(
                    BatchId(2),
                    [
                        Proposal(RegionalPrincipal, PlayerPrincipal, 1, -20),
                        Proposal(PlayerPrincipal, RegionalPrincipal, 1, 25),
                    ])));

        Assert.Equal(
            [
                (PlayerPrincipal, RegionalPrincipal),
                (RegionalPrincipal, PlayerPrincipal),
            ],
            result.Outcomes.Select(outcome => (
                outcome.AssessingPrincipalId,
                outcome.SubjectPrincipalId)));
        Assert.Equal(
            [
                (PlayerPrincipal, RegionalPrincipal),
                (RegionalPrincipal, PlayerPrincipal),
            ],
            ReadAllFacts(session)
                .Select(envelope => Assert.IsType<StandingChangedFact>(envelope.Fact))
                .Select(fact => (
                    fact.AssessingPrincipalId,
                    fact.SubjectPrincipalId)));
    }

    [Fact]
    public void StandingBatchClampsCombinedResultOnceAndSuppressesNoOpFacts()
    {
        GameSession session = CreateRelationshipSession(
            [
                new InitialStandingSetup(
                    PlayerPrincipal,
                    RegionalPrincipal,
                    new StandingValue(90)),
            ]);

        StandingChangeBatchResult.Applied changed = Assert.IsType<
            StandingChangeBatchResult.Applied>(session.CommitStandingChanges(
                new StandingChangeBatch(
                    BatchId(3),
                    [
                        Proposal(PlayerPrincipal, RegionalPrincipal, 1, 20),
                        Proposal(PlayerPrincipal, RegionalPrincipal, 2, -5),
                    ])));
        StandingChangeOutcome changedOutcome = Assert.Single(changed.Outcomes);
        Assert.Equal(15, changedOutcome.CombinedDelta);
        Assert.Equal(new StandingValue(100), changedOutcome.ResultingValue);
        Assert.Single(ReadAllFacts(session));

        StandingChangeBatchResult.Applied noOp = Assert.IsType<
            StandingChangeBatchResult.Applied>(session.CommitStandingChanges(
                new StandingChangeBatch(
                    BatchId(4),
                    [Proposal(PlayerPrincipal, RegionalPrincipal, 1, 1)])));

        Assert.False(Assert.Single(noOp.Outcomes).Changed);
        Assert.Single(ReadAllFacts(session));
        StandingSnapshot standing = session.CaptureSnapshot().Relationships.Standings
            .Single(value => value.AssessingPrincipalId == PlayerPrincipal);
        Assert.Equal(new StandingValue(100), standing.Value);
    }

    [Fact]
    public void RejectedStandingBatchLeavesAllDirectionsAndFactsUnchanged()
    {
        GameSession session = CreateRelationshipSession();

        var rejected = Assert.IsType<StandingChangeBatchResult.Rejected>(
            session.CommitStandingChanges(new StandingChangeBatch(
                BatchId(5),
                [
                    Proposal(PlayerPrincipal, RegionalPrincipal, 1, 25),
                    Proposal(new PrincipalId(99), PlayerPrincipal, 1, 25),
                ])));

        Assert.Equal(StandingChangeRejectionReason.UnknownPrincipal, rejected.Reason);
        Assert.All(
            session.CaptureSnapshot().Relationships.Standings,
            standing => Assert.Equal(new StandingValue(0), standing.Value));
        Assert.Empty(ReadAllFacts(session));
    }

    [Fact]
    public void StandingBatchRejectsDuplicateContributionsAndOverflowAtomically()
    {
        GameSession session = CreateRelationshipSession();
        StandingChangeProposal duplicate = Proposal(
            PlayerPrincipal,
            RegionalPrincipal,
            contributionId: 1,
            delta: 1);

        var duplicateResult = Assert.IsType<StandingChangeBatchResult.Rejected>(
            session.CommitStandingChanges(new StandingChangeBatch(
                BatchId(6),
                [duplicate, duplicate])));
        var overflowResult = Assert.IsType<StandingChangeBatchResult.Rejected>(
            session.CommitStandingChanges(new StandingChangeBatch(
                BatchId(7),
                [
                    Proposal(
                        PlayerPrincipal,
                        RegionalPrincipal,
                        contributionId: 1,
                        delta: long.MaxValue),
                    Proposal(
                        PlayerPrincipal,
                        RegionalPrincipal,
                        contributionId: 2,
                        delta: 1),
                ])));

        Assert.Equal(
            StandingChangeRejectionReason.DuplicateContribution,
            duplicateResult.Reason);
        Assert.Equal(
            StandingChangeRejectionReason.DeltaOverflow,
            overflowResult.Reason);
        Assert.All(
            session.CaptureSnapshot().Relationships.Standings,
            standing => Assert.Equal(new StandingValue(0), standing.Value));
        Assert.Empty(ReadAllFacts(session));
    }

    [Fact]
    public void RepeatedStandingBatchReturnsReceiptWithoutMutationOrDuplicateFact()
    {
        GameSession session = CreateRelationshipSession();
        var batch = new StandingChangeBatch(
            BatchId(8),
            [Proposal(PlayerPrincipal, RegionalPrincipal, 1, 30)]);

        StandingChangeBatchResult.Applied first = Assert.IsType<
            StandingChangeBatchResult.Applied>(session.CommitStandingChanges(batch));
        StandingChangeBatchResult.Applied repeated = Assert.IsType<
            StandingChangeBatchResult.Applied>(session.CommitStandingChanges(batch));
        var conflict = Assert.IsType<StandingChangeBatchResult.Rejected>(
            session.CommitStandingChanges(new StandingChangeBatch(
                batch.Id,
                [Proposal(PlayerPrincipal, RegionalPrincipal, 1, 31)])));

        Assert.Same(first, repeated);
        Assert.Equal(
            StandingChangeRejectionReason.BatchIdentityConflict,
            conflict.Reason);
        Assert.Single(ReadAllFacts(session));
        StandingSnapshot standing = session.CaptureSnapshot().Relationships.Standings
            .Single(value => value.AssessingPrincipalId == PlayerPrincipal);
        Assert.Equal(new StandingValue(30), standing.Value);
    }

    [Fact]
    public void StandingFactContributionsCannotBeModified()
    {
        GameSession session = CreateRelationshipSession();
        session.CommitStandingChanges(new StandingChangeBatch(
            BatchId(9),
            [Proposal(PlayerPrincipal, RegionalPrincipal, 1, 10)]));
        var fact = Assert.IsType<StandingChangedFact>(
            Assert.Single(ReadAllFacts(session)).Fact);
        var contributions = Assert.IsAssignableFrom<IList<StandingChangeContribution>>(
            fact.Contributions);

        Assert.Throws<NotSupportedException>(() => contributions.Add(
            new StandingChangeContribution(
                new StandingChangeContributionId(2),
                10,
                StandingChangeReason.Explicit)));
    }

    private static PrincipalDefinition Definition(
        PrincipalId id,
        string contentId,
        string name) =>
        new(id, new PrincipalContentId(contentId), name);

    private static StandingChangeProposal Proposal(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId,
        ulong contributionId,
        long delta) =>
        new(
            assessingPrincipalId,
            subjectPrincipalId,
            new StandingChangeContribution(
                new StandingChangeContributionId(contributionId),
                delta,
                StandingChangeReason.Explicit));

    private static StandingChangeBatchId BatchId(ulong value) =>
        new(StandingChangeSourceKind.Explicit, value);

    private static GameSession CreateRelationshipSession(
        IEnumerable<InitialStandingSetup>? standings = null)
    {
        RelationshipSetup relationships = CreateRelationshipSetup(
            [
                Definition(PlayerPrincipal, "player", "Player"),
                Definition(RegionalPrincipal, "regional", "Regional"),
            ],
            standings ?? []);
        return new GameSession(
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [],
                relationships,
                factRetentionCapacity: 256),
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static IReadOnlyList<GameFactEnvelope> ReadAllFacts(GameSession session) =>
        session.ReadFactsAfter(null, 256).Facts;

    private static (
        PrincipalId Assessing,
        PrincipalId Subject,
        StandingValue Prior,
        long Delta,
        StandingValue Result) OutcomeValues(StandingChangeOutcome outcome) =>
        (
            outcome.AssessingPrincipalId,
            outcome.SubjectPrincipalId,
            outcome.PriorValue,
            outcome.CombinedDelta,
            outcome.ResultingValue);

    private static RelationshipSetup CreateRelationshipSetup(
        IEnumerable<PrincipalDefinition> principals,
        IEnumerable<InitialStandingSetup> standings) =>
        new(principals, PlayerPrincipal, Policy(), standings);

    private static StandingPolicy Policy() =>
        new(
            new StandingPolicyId("test-standing"),
            new StandingValue(-100),
            new StandingValue(100),
            new StandingValue(0),
            new StandingValue(-50),
            new StandingValue(0),
            new StandingValue(50),
            new StandingValue(90));
}
