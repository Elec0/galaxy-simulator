using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class RelationshipCheckpointTests
{
    private static readonly PrincipalId Player = new(1);
    private static readonly PrincipalId Neighbor = new(2);
    private static readonly PrincipalId ThirdParty = new(3);

    [Fact]
    public void RestorePreservesRelationshipTruthAndReceiptIdempotency()
    {
        RelationshipOwner owner = CreateOwner();
        StandingChangeBatch standingBatch = StandingBatch(4, Player, Neighbor, 15);
        RelationshipPolicyChangeBatch policyBatch = PolicyBatch(
            7,
            new SetDiplomaticConditionProposal(
                Neighbor,
                Player,
                DiplomaticCondition.War,
                RelationshipPolicyChangeReason.Explicit),
            new IssueRelationshipGrantProposal(
                new RelationshipGrantId(2),
                Player,
                ThirdParty,
                new RelationshipGrantKind("dock"),
                StandingBand.Neutral,
                RelationshipPolicyChangeReason.Explicit));
        StandingChangeBatchResult.Applied standingResult = Commit(owner, standingBatch);
        RelationshipPolicyChangeBatchResult.Applied policyResult = Commit(owner, policyBatch);

        RelationshipCheckpoint checkpoint = owner.CaptureCheckpoint();
        CheckpointResult<RelationshipOwner> restoredResult =
            RelationshipOwner.RestoreCheckpoint(checkpoint);

        Assert.True(restoredResult.IsSuccess);
        RelationshipOwner restored = Assert.IsType<RelationshipOwner>(restoredResult.Value);
        AssertSnapshotsEqual(owner.CaptureSnapshot(), restored.CaptureSnapshot());
        var repeatedStanding = Assert.IsType<StandingChangePreparation.Resolved>(
            restored.PrepareStandingChanges(standingBatch));
        var repeatedPolicy = Assert.IsType<RelationshipPolicyChangePreparation.Resolved>(
            restored.PreparePolicyChanges(policyBatch));
        StandingChangeBatchResult.Applied restoredStanding =
            Assert.IsType<StandingChangeBatchResult.Applied>(repeatedStanding.Result);
        RelationshipPolicyChangeBatchResult.Applied restoredPolicy =
            Assert.IsType<RelationshipPolicyChangeBatchResult.Applied>(repeatedPolicy.Result);
        Assert.Equal(standingResult.BatchId, restoredStanding.BatchId);
        Assert.Equal(standingResult.Outcomes, restoredStanding.Outcomes);
        Assert.Equal(policyResult.BatchId, restoredPolicy.BatchId);
        Assert.Equal(policyResult.DiplomaticOutcomes, restoredPolicy.DiplomaticOutcomes);
        Assert.Equal(policyResult.GrantOutcomes, restoredPolicy.GrantOutcomes);
    }

    [Fact]
    public void RestorePreservesRevokedGrantState()
    {
        RelationshipOwner owner = CreateOwner();
        RelationshipPolicyChangeBatch revoke = PolicyBatch(
            8,
            new RevokeRelationshipGrantProposal(
                new RelationshipGrantId(1),
                RelationshipPolicyChangeReason.Explicit));
        Commit(owner, revoke);

        CheckpointResult<RelationshipOwner> restored =
            RelationshipOwner.RestoreCheckpoint(owner.CaptureCheckpoint());

        RelationshipGrantSnapshot grant = Assert.Single(
            Assert.IsType<RelationshipOwner>(restored.Value)
                .CaptureSnapshot()
                .Grants);
        Assert.False(grant.IsIssued);
        Assert.False(grant.IsEffective);
    }

    [Fact]
    public void RestoreAcceptsUnorderedCollectionsAndCanonicalizesCapture()
    {
        RelationshipCheckpoint original = CreateOwner().CaptureCheckpoint();
        var unordered = new RelationshipCheckpoint(
            original.PlayerPrincipalId,
            original.StandingPolicy,
            original.Principals.Reverse(),
            original.Standings.Reverse(),
            original.DiplomaticConditions.Reverse(),
            original.Grants.Reverse(),
            original.StandingReceipts.Reverse(),
            original.PolicyReceipts.Reverse());

        RelationshipCheckpoint recaptured = Assert.IsType<RelationshipOwner>(
                RelationshipOwner.RestoreCheckpoint(unordered).Value)
            .CaptureCheckpoint();

        Assert.Equal([1UL, 2UL, 3UL], recaptured.Principals.Select(value => value!.Id.Value));
        Assert.Equal(
            [(1UL, 2UL), (1UL, 3UL), (2UL, 1UL), (2UL, 3UL), (3UL, 1UL), (3UL, 2UL)],
            recaptured.Standings.Select(value => (
                value!.AssessingPrincipalId.Value,
                value.SubjectPrincipalId.Value)));
        Assert.Equal(
            [(1UL, 2UL), (1UL, 3UL), (2UL, 3UL)],
            recaptured.DiplomaticConditions.Select(value => (
                value!.LowerPrincipalId.Value,
                value.UpperPrincipalId.Value)));
    }

    [Fact]
    public void RestoreRejectsDuplicatePrincipalContentIdentity()
    {
        RelationshipCheckpoint checkpoint = CreateOwner().CaptureCheckpoint();
        RelationshipPrincipalCheckpoint first = Assert.IsType<RelationshipPrincipalCheckpoint>(
            checkpoint.Principals[0]);
        RelationshipPrincipalCheckpoint second = Assert.IsType<RelationshipPrincipalCheckpoint>(
            checkpoint.Principals[1]);
        RelationshipPrincipalCheckpoint[] principals = checkpoint.Principals
            .Select(value => Assert.IsType<RelationshipPrincipalCheckpoint>(value))
            .ToArray();
        principals[1] = second with { ContentId = first.ContentId };
        var corrupt = Copy(checkpoint, principals: principals);

        CheckpointResult<RelationshipOwner> result =
            RelationshipOwner.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.relationships.principals[1].contentId", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsIncompleteStandingMatrix()
    {
        RelationshipCheckpoint checkpoint = CreateOwner().CaptureCheckpoint();
        var corrupt = Copy(checkpoint, standings: checkpoint.Standings.Skip(1));

        CheckpointResult<RelationshipOwner> result =
            RelationshipOwner.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.relationships.standings", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsNoncanonicalDiplomaticPair()
    {
        RelationshipCheckpoint checkpoint = CreateOwner().CaptureCheckpoint();
        RelationshipDiplomacyCheckpoint first = Assert.IsType<RelationshipDiplomacyCheckpoint>(
            checkpoint.DiplomaticConditions[0]);
        RelationshipDiplomacyCheckpoint[] diplomacy = checkpoint.DiplomaticConditions
            .Select(value => Assert.IsType<RelationshipDiplomacyCheckpoint>(value))
            .ToArray();
        diplomacy[0] = first with
        {
            LowerPrincipalId = first.UpperPrincipalId,
            UpperPrincipalId = first.LowerPrincipalId,
        };
        var corrupt = Copy(checkpoint, diplomacy: diplomacy);

        CheckpointResult<RelationshipOwner> result =
            RelationshipOwner.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.relationships.diplomaticConditions[0]",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsStandingReceiptWithIncorrectDerivedBand()
    {
        RelationshipOwner owner = CreateOwner();
        Commit(owner, StandingBatch(9, Player, Neighbor, 15));
        RelationshipCheckpoint checkpoint = owner.CaptureCheckpoint();
        StandingBatchReceiptCheckpoint receipt = Assert.IsType<StandingBatchReceiptCheckpoint>(
            Assert.Single(checkpoint.StandingReceipts));
        StandingChangeOutcome outcome = Assert.Single(receipt.Result!.Outcomes);
        var corruptOutcome = new StandingChangeOutcome(
            outcome.AssessingPrincipalId,
            outcome.SubjectPrincipalId,
            outcome.PriorValue,
            outcome.PriorBand,
            outcome.CombinedDelta,
            outcome.ResultingValue,
            StandingBand.Hostile,
            outcome.Contributions);
        var corruptResult = new StandingChangeBatchResult.Applied(
            receipt.Result.BatchId,
            [corruptOutcome]);
        var corruptReceipt = receipt with { Result = corruptResult };
        var corrupt = Copy(checkpoint, standingReceipts: [corruptReceipt]);

        CheckpointResult<RelationshipOwner> result =
            RelationshipOwner.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.relationships.standingReceipts[0].result.outcomes[0]",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsPolicyReceiptWithNoncanonicalProposals()
    {
        RelationshipOwner owner = CreateOwner();
        Commit(
            owner,
            PolicyBatch(
                10,
                new IssueRelationshipGrantProposal(
                    new RelationshipGrantId(2),
                    Player,
                    ThirdParty,
                    new RelationshipGrantKind("dock"),
                    StandingBand.Neutral,
                    RelationshipPolicyChangeReason.Explicit),
                new SetDiplomaticConditionProposal(
                    Player,
                    Neighbor,
                    DiplomaticCondition.War,
                    RelationshipPolicyChangeReason.Explicit)));
        RelationshipCheckpoint checkpoint = owner.CaptureCheckpoint();
        PolicyBatchReceiptCheckpoint receipt = Assert.IsType<PolicyBatchReceiptCheckpoint>(
            Assert.Single(checkpoint.PolicyReceipts));
        var corrupt = Copy(
            checkpoint,
            policyReceipts:
            [
                new PolicyBatchReceiptCheckpoint(
                    receipt.BatchId,
                    receipt.Proposals.Reverse(),
                    receipt.Result),
            ]);

        CheckpointResult<RelationshipOwner> result =
            RelationshipOwner.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.relationships.policyReceipts[0].proposals",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreCopiesReceiptOutcomeCollections()
    {
        RelationshipOwner owner = CreateOwner();
        StandingChangeBatch batch = StandingBatch(11, Player, Neighbor, 15);
        Commit(owner, batch);
        RelationshipCheckpoint checkpoint = owner.CaptureCheckpoint();
        StandingBatchReceiptCheckpoint receipt = Assert.IsType<StandingBatchReceiptCheckpoint>(
            Assert.Single(checkpoint.StandingReceipts));
        var mutableOutcomes = receipt.Result!.Outcomes.ToList();
        var decodedResult = new StandingChangeBatchResult.Applied(
            receipt.BatchId,
            mutableOutcomes);
        var decodedReceipt = receipt with { Result = decodedResult };
        RelationshipCheckpoint decoded = Copy(
            checkpoint,
            standingReceipts: [decodedReceipt]);
        RelationshipOwner restored = Assert.IsType<RelationshipOwner>(
            RelationshipOwner.RestoreCheckpoint(decoded).Value);

        mutableOutcomes.Clear();

        var repeated = Assert.IsType<StandingChangePreparation.Resolved>(
            restored.PrepareStandingChanges(batch));
        Assert.Single(Assert.IsType<StandingChangeBatchResult.Applied>(repeated.Result).Outcomes);
    }

    [Fact]
    public void CaptureDoesNotExposeLiveReceiptProposalCollections()
    {
        RelationshipOwner owner = CreateOwner();
        StandingChangeBatch batch = StandingBatch(14, Player, Neighbor, 15);
        Commit(owner, batch);
        StandingBatchReceiptCheckpoint receipt = Assert.IsType<StandingBatchReceiptCheckpoint>(
            Assert.Single(owner.CaptureCheckpoint().StandingReceipts));
        var exposed = Assert.IsAssignableFrom<IList<StandingChangeProposal?>>(
            receipt.Proposals);

        Assert.Throws<NotSupportedException>(() =>
            exposed[0] = StandingBatch(14, Player, Neighbor, -10).Proposals[0]);

        var repeated = Assert.IsType<StandingChangePreparation.Resolved>(
            owner.PrepareStandingChanges(batch));
        Assert.IsType<StandingChangeBatchResult.Applied>(repeated.Result);
    }

    [Fact]
    public void RestoreRejectsDuplicateGrantIssuanceAcrossReceipts()
    {
        RelationshipOwner owner = CreateOwner();
        Commit(
            owner,
            PolicyBatch(
                12,
                new IssueRelationshipGrantProposal(
                    new RelationshipGrantId(2),
                    Player,
                    ThirdParty,
                    new RelationshipGrantKind("dock"),
                    StandingBand.Neutral,
                    RelationshipPolicyChangeReason.Explicit)));
        RelationshipCheckpoint checkpoint = owner.CaptureCheckpoint();
        PolicyBatchReceiptCheckpoint first = Assert.IsType<PolicyBatchReceiptCheckpoint>(
            Assert.Single(checkpoint.PolicyReceipts));
        var duplicateId = new RelationshipPolicyChangeBatchId(
            RelationshipPolicyChangeSourceKind.Explicit,
            13);
        var duplicate = new PolicyBatchReceiptCheckpoint(
            duplicateId,
            first.Proposals,
            new RelationshipPolicyChangeBatchResult.Applied(
                duplicateId,
                first.Result!.DiplomaticOutcomes,
                first.Result.GrantOutcomes));
        var corrupt = Copy(checkpoint, policyReceipts: [first, duplicate]);

        CheckpointResult<RelationshipOwner> result =
            RelationshipOwner.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.relationships.policyReceipts[1].result",
            result.Failure!.Path);
    }

    private static RelationshipOwner CreateOwner() => new(new RelationshipSetup(
        [
            new PrincipalDefinition(Player, new PrincipalContentId("player"), "Player"),
            new PrincipalDefinition(Neighbor, new PrincipalContentId("neighbor"), "Neighbor"),
            new PrincipalDefinition(ThirdParty, new PrincipalContentId("third"), "Third"),
        ],
        Player,
        Policy(),
        [new InitialStandingSetup(Player, Neighbor, new StandingValue(20))],
        [new InitialDiplomaticConditionSetup(Player, ThirdParty, DiplomaticCondition.War)],
        [
            new InitialRelationshipGrantSetup(
                new RelationshipGrantId(1),
                Neighbor,
                Player,
                new RelationshipGrantKind("transit"),
                StandingBand.Neutral),
        ]));

    private static StandingPolicy Policy() => new(
        new StandingPolicyId("standard"),
        new StandingValue(-100),
        new StandingValue(100),
        new StandingValue(0),
        new StandingValue(-50),
        new StandingValue(0),
        new StandingValue(25),
        new StandingValue(75));

    private static StandingChangeBatch StandingBatch(
        ulong id,
        PrincipalId assessing,
        PrincipalId subject,
        long delta) =>
        new(
            new StandingChangeBatchId(StandingChangeSourceKind.Explicit, id),
            [
                new StandingChangeProposal(
                    assessing,
                    subject,
                    new StandingChangeContribution(
                        new StandingChangeContributionId(1),
                        delta,
                        StandingChangeReason.Explicit)),
            ]);

    private static RelationshipPolicyChangeBatch PolicyBatch(
        ulong id,
        params RelationshipPolicyChangeProposal[] proposals) =>
        new(
            new RelationshipPolicyChangeBatchId(
                RelationshipPolicyChangeSourceKind.Explicit,
                id),
            proposals);

    private static StandingChangeBatchResult.Applied Commit(
        RelationshipOwner owner,
        StandingChangeBatch batch)
    {
        var prepared = Assert.IsType<StandingChangePreparation.Prepared>(
            owner.PrepareStandingChanges(batch));
        return Assert.IsType<StandingChangeBatchResult.Applied>(
            owner.ApplyStandingChanges(prepared.Value));
    }

    private static RelationshipPolicyChangeBatchResult.Applied Commit(
        RelationshipOwner owner,
        RelationshipPolicyChangeBatch batch)
    {
        var prepared = Assert.IsType<RelationshipPolicyChangePreparation.Prepared>(
            owner.PreparePolicyChanges(batch));
        return Assert.IsType<RelationshipPolicyChangeBatchResult.Applied>(
            owner.ApplyPolicyChanges(prepared.Value));
    }

    private static RelationshipCheckpoint Copy(
        RelationshipCheckpoint source,
        IEnumerable<RelationshipPrincipalCheckpoint?>? principals = null,
        IEnumerable<RelationshipStandingCheckpoint?>? standings = null,
        IEnumerable<RelationshipDiplomacyCheckpoint?>? diplomacy = null,
        IEnumerable<StandingBatchReceiptCheckpoint?>? standingReceipts = null,
        IEnumerable<PolicyBatchReceiptCheckpoint?>? policyReceipts = null) =>
        new(
            source.PlayerPrincipalId,
            source.StandingPolicy,
            principals ?? source.Principals,
            standings ?? source.Standings,
            diplomacy ?? source.DiplomaticConditions,
            source.Grants,
            standingReceipts ?? source.StandingReceipts,
            policyReceipts ?? source.PolicyReceipts);

    private static void AssertSnapshotsEqual(
        RelationshipSnapshot expected,
        RelationshipSnapshot actual)
    {
        Assert.Equal(expected.PlayerPrincipalId, actual.PlayerPrincipalId);
        Assert.Equal(expected.StandingPolicyId, actual.StandingPolicyId);
        Assert.Equal(expected.Principals, actual.Principals);
        Assert.Equal(expected.Standings, actual.Standings);
        Assert.Equal(expected.DiplomaticConditions, actual.DiplomaticConditions);
        Assert.Equal(expected.Grants, actual.Grants);
    }
}
