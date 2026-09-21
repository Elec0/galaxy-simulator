using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record RelationshipPrincipalCheckpoint(
    PrincipalId Id,
    string? ContentId,
    string? Name);

internal sealed record RelationshipStandingPolicyCheckpoint(
    string? Id,
    StandingValue Minimum,
    StandingValue Maximum,
    StandingValue Initial,
    StandingValue AdversarialThreshold,
    StandingValue NeutralThreshold,
    StandingValue FavorableThreshold,
    StandingValue AlliedThreshold);

internal sealed record RelationshipStandingCheckpoint(
    PrincipalId AssessingPrincipalId,
    PrincipalId SubjectPrincipalId,
    StandingValue Value);

internal sealed record RelationshipDiplomacyCheckpoint(
    PrincipalId LowerPrincipalId,
    PrincipalId UpperPrincipalId,
    DiplomaticCondition Condition);

internal sealed record RelationshipGrantCheckpoint(
    RelationshipGrantId Id,
    PrincipalId IssuerPrincipalId,
    PrincipalId HolderPrincipalId,
    string? Kind,
    StandingBand MinimumStandingBand,
    bool IsIssued);

internal sealed record StandingBatchReceiptCheckpoint
{
    internal StandingBatchReceiptCheckpoint(
        StandingChangeBatchId batchId,
        IEnumerable<StandingChangeProposal?> proposals,
        StandingChangeBatchResult.Applied? result)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        BatchId = batchId;
        Proposals = new ReadOnlyCollection<StandingChangeProposal?>(proposals.ToArray());
        Result = result;
    }

    internal StandingChangeBatchId BatchId { get; }

    internal IReadOnlyList<StandingChangeProposal?> Proposals { get; }

    internal StandingChangeBatchResult.Applied? Result { get; init; }
}

internal sealed record PolicyBatchReceiptCheckpoint
{
    internal PolicyBatchReceiptCheckpoint(
        RelationshipPolicyChangeBatchId batchId,
        IEnumerable<RelationshipPolicyChangeProposal?> proposals,
        RelationshipPolicyChangeBatchResult.Applied? result)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        BatchId = batchId;
        Proposals = new ReadOnlyCollection<RelationshipPolicyChangeProposal?>(
            proposals.ToArray());
        Result = result;
    }

    internal RelationshipPolicyChangeBatchId BatchId { get; }

    internal IReadOnlyList<RelationshipPolicyChangeProposal?> Proposals { get; }

    internal RelationshipPolicyChangeBatchResult.Applied? Result { get; init; }
}

internal sealed class RelationshipCheckpoint
{
    internal RelationshipCheckpoint(
        PrincipalId playerPrincipalId,
        RelationshipStandingPolicyCheckpoint? standingPolicy,
        IEnumerable<RelationshipPrincipalCheckpoint?> principals,
        IEnumerable<RelationshipStandingCheckpoint?> standings,
        IEnumerable<RelationshipDiplomacyCheckpoint?> diplomaticConditions,
        IEnumerable<RelationshipGrantCheckpoint?> grants,
        IEnumerable<StandingBatchReceiptCheckpoint?> standingReceipts,
        IEnumerable<PolicyBatchReceiptCheckpoint?> policyReceipts)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(diplomaticConditions);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(standingReceipts);
        ArgumentNullException.ThrowIfNull(policyReceipts);
        PlayerPrincipalId = playerPrincipalId;
        StandingPolicy = standingPolicy;
        Principals = new ReadOnlyCollection<RelationshipPrincipalCheckpoint?>(
            principals.ToArray());
        Standings = new ReadOnlyCollection<RelationshipStandingCheckpoint?>(
            standings.ToArray());
        DiplomaticConditions = new ReadOnlyCollection<RelationshipDiplomacyCheckpoint?>(
            diplomaticConditions.ToArray());
        Grants = new ReadOnlyCollection<RelationshipGrantCheckpoint?>(grants.ToArray());
        StandingReceipts = new ReadOnlyCollection<StandingBatchReceiptCheckpoint?>(
            standingReceipts.ToArray());
        PolicyReceipts = new ReadOnlyCollection<PolicyBatchReceiptCheckpoint?>(
            policyReceipts.ToArray());
    }

    internal PrincipalId PlayerPrincipalId { get; }

    internal RelationshipStandingPolicyCheckpoint? StandingPolicy { get; }

    internal ReadOnlyCollection<RelationshipPrincipalCheckpoint?> Principals { get; }

    internal ReadOnlyCollection<RelationshipStandingCheckpoint?> Standings { get; }

    internal ReadOnlyCollection<RelationshipDiplomacyCheckpoint?>
        DiplomaticConditions
    { get; }

    internal ReadOnlyCollection<RelationshipGrantCheckpoint?> Grants { get; }

    internal ReadOnlyCollection<StandingBatchReceiptCheckpoint?> StandingReceipts { get; }

    internal ReadOnlyCollection<PolicyBatchReceiptCheckpoint?> PolicyReceipts { get; }
}

