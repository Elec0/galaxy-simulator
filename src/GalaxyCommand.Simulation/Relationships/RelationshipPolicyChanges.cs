using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public enum RelationshipPolicyChangeSourceKind
{
    Explicit,
}

/// <summary>
/// Stable source-scoped identity for one diplomacy and grant change batch.
/// </summary>
public readonly record struct RelationshipPolicyChangeBatchId
{
    /// <summary>
    /// Creates a non-zero change identity within one source domain.
    /// </summary>
    public RelationshipPolicyChangeBatchId(
        RelationshipPolicyChangeSourceKind sourceKind,
        ulong value)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown relationship policy change source kind.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(value);
        SourceKind = sourceKind;
        Value = value;
    }

    public RelationshipPolicyChangeSourceKind SourceKind { get; }

    public ulong Value { get; }
}

/// <summary>
/// Initial reason vocabulary for explicit diplomacy and grant changes.
/// </summary>
public enum RelationshipPolicyChangeReason
{
    Explicit,
}

/// <summary>
/// Closed proposal vocabulary for diplomacy and explicit grant changes.
/// </summary>
public abstract record RelationshipPolicyChangeProposal
{
    private protected RelationshipPolicyChangeProposal(
        RelationshipPolicyChangeReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reason.");
        }

        Reason = reason;
    }

    public RelationshipPolicyChangeReason Reason { get; }
}

/// <summary>
/// Explicit assignment of one mutual diplomatic condition.
/// </summary>
public sealed record SetDiplomaticConditionProposal : RelationshipPolicyChangeProposal
{
    /// <summary>
    /// Creates one validated mutual diplomatic assignment.
    /// </summary>
    public SetDiplomaticConditionProposal(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId,
        DiplomaticCondition condition,
        RelationshipPolicyChangeReason reason)
        : base(reason)
    {
        ArgumentOutOfRangeException.ThrowIfZero(firstPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(secondPrincipalId.Value);
        if (firstPrincipalId == secondPrincipalId)
        {
            throw new ArgumentException("Self-diplomacy is invalid.");
        }

        if (!Enum.IsDefined(condition))
        {
            throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown condition.");
        }

        (LowerPrincipalId, UpperPrincipalId) = firstPrincipalId.Value < secondPrincipalId.Value
            ? (firstPrincipalId, secondPrincipalId)
            : (secondPrincipalId, firstPrincipalId);
        Condition = condition;
    }

    public PrincipalId LowerPrincipalId { get; }

    public PrincipalId UpperPrincipalId { get; }

    public DiplomaticCondition Condition { get; }
}

/// <summary>
/// Explicit issuance of one stable relationship grant.
/// </summary>
public sealed record IssueRelationshipGrantProposal : RelationshipPolicyChangeProposal
{
    /// <summary>
    /// Creates one validated grant issuance proposal.
    /// </summary>
    public IssueRelationshipGrantProposal(
        RelationshipGrantId id,
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind,
        StandingBand minimumStandingBand,
        RelationshipPolicyChangeReason reason)
        : base(reason)
    {
        InitialRelationshipGrantSetup.ValidateGrantValues(
            id,
            issuerPrincipalId,
            holderPrincipalId,
            kind,
            minimumStandingBand);
        Id = id;
        IssuerPrincipalId = issuerPrincipalId;
        HolderPrincipalId = holderPrincipalId;
        Kind = kind;
        MinimumStandingBand = minimumStandingBand;
    }

    public RelationshipGrantId Id { get; }

    public PrincipalId IssuerPrincipalId { get; }

    public PrincipalId HolderPrincipalId { get; }

    public RelationshipGrantKind Kind { get; }

    public StandingBand MinimumStandingBand { get; }
}

/// <summary>
/// Explicit revocation of one previously issued relationship grant.
/// </summary>
public sealed record RevokeRelationshipGrantProposal : RelationshipPolicyChangeProposal
{
    /// <summary>
    /// Creates one validated grant revocation proposal.
    /// </summary>
    public RevokeRelationshipGrantProposal(
        RelationshipGrantId id,
        RelationshipPolicyChangeReason reason)
        : base(reason)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        Id = id;
    }

    public RelationshipGrantId Id { get; }
}

/// <summary>
/// One immutable idempotent delivery of diplomacy and grant effects.
/// </summary>
public sealed record RelationshipPolicyChangeBatch
{
    /// <summary>
    /// Copies a non-empty proposal batch without retaining caller mutation.
    /// </summary>
    public RelationshipPolicyChangeBatch(
        RelationshipPolicyChangeBatchId id,
        IEnumerable<RelationshipPolicyChangeProposal> proposals)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentNullException.ThrowIfNull(proposals);
        RelationshipPolicyChangeProposal[] values = proposals.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("A relationship policy batch requires proposals.", nameof(proposals));
        }

        foreach (RelationshipPolicyChangeProposal proposal in values)
        {
            ArgumentNullException.ThrowIfNull(proposal);
        }

        Id = id;
        Proposals = new ReadOnlyCollection<RelationshipPolicyChangeProposal>(values);
    }

    public RelationshipPolicyChangeBatchId Id { get; }

    public IReadOnlyList<RelationshipPolicyChangeProposal> Proposals { get; }
}

/// <summary>
/// Typed reason that prevents a diplomacy and grant batch from committing.
/// </summary>
public enum RelationshipPolicyChangeRejectionReason
{
    UnknownPrincipal,
    DuplicateDiplomaticAssignment,
    DuplicateGrantAssignment,
    GrantIdentityAlreadyExists,
    UnknownGrant,
    GrantAlreadyRevoked,
    StandingRequirementNotMet,
    BatchIdentityConflict,
    FactSequenceExhausted,
}

/// <summary>
/// Prepared result for one mutual diplomatic assignment.
/// </summary>
public sealed record DiplomaticConditionChangeOutcome
{
    /// <summary>
    /// Creates one validated canonical diplomatic outcome.
    /// </summary>
    public DiplomaticConditionChangeOutcome(
        PrincipalId lowerPrincipalId,
        PrincipalId upperPrincipalId,
        DiplomaticCondition priorCondition,
        DiplomaticCondition resultingCondition,
        RelationshipPolicyChangeReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfZero(lowerPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(upperPrincipalId.Value);
        if (lowerPrincipalId.Value >= upperPrincipalId.Value)
        {
            throw new ArgumentException("A diplomatic outcome requires a canonical pair.");
        }

        if (!Enum.IsDefined(priorCondition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(priorCondition),
                priorCondition,
                "Unknown prior diplomatic condition.");
        }

        if (!Enum.IsDefined(resultingCondition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingCondition),
                resultingCondition,
                "Unknown resulting diplomatic condition.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reason.");
        }

        LowerPrincipalId = lowerPrincipalId;
        UpperPrincipalId = upperPrincipalId;
        PriorCondition = priorCondition;
        ResultingCondition = resultingCondition;
        Reason = reason;
    }

    public PrincipalId LowerPrincipalId { get; }

    public PrincipalId UpperPrincipalId { get; }

    public DiplomaticCondition PriorCondition { get; }

    public DiplomaticCondition ResultingCondition { get; }

    public RelationshipPolicyChangeReason Reason { get; }

    public bool Changed => PriorCondition != ResultingCondition;
}

/// <summary>
/// Prepared result for one relationship grant state transition.
/// </summary>
public sealed record RelationshipGrantChangeOutcome
{
    /// <summary>
    /// Creates one validated grant issuance or revocation outcome.
    /// </summary>
    public RelationshipGrantChangeOutcome(
        RelationshipGrantId id,
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind,
        StandingBand minimumStandingBand,
        bool priorIssued,
        bool resultingIssued,
        RelationshipPolicyChangeReason reason)
    {
        InitialRelationshipGrantSetup.ValidateGrantValues(
            id,
            issuerPrincipalId,
            holderPrincipalId,
            kind,
            minimumStandingBand);
        if (priorIssued == resultingIssued)
        {
            throw new ArgumentException("A grant outcome requires a state transition.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reason.");
        }

        Id = id;
        IssuerPrincipalId = issuerPrincipalId;
        HolderPrincipalId = holderPrincipalId;
        Kind = kind;
        MinimumStandingBand = minimumStandingBand;
        PriorIssued = priorIssued;
        ResultingIssued = resultingIssued;
        Reason = reason;
    }

    public RelationshipGrantId Id { get; }

    public PrincipalId IssuerPrincipalId { get; }

    public PrincipalId HolderPrincipalId { get; }

    public RelationshipGrantKind Kind { get; }

    public StandingBand MinimumStandingBand { get; }

    public bool PriorIssued { get; }

    public bool ResultingIssued { get; }

    public RelationshipPolicyChangeReason Reason { get; }

    public bool Changed => PriorIssued != ResultingIssued;
}

/// <summary>
/// Idempotent outcome of one diplomacy and grant change batch.
/// </summary>
public abstract record RelationshipPolicyChangeBatchResult
{
    private RelationshipPolicyChangeBatchResult()
    {
    }

    public sealed record Applied(
        RelationshipPolicyChangeBatchId BatchId,
        IReadOnlyList<DiplomaticConditionChangeOutcome> DiplomaticOutcomes,
        IReadOnlyList<RelationshipGrantChangeOutcome> GrantOutcomes)
        : RelationshipPolicyChangeBatchResult;

    public sealed record Rejected(
        RelationshipPolicyChangeBatchId BatchId,
        RelationshipPolicyChangeRejectionReason Reason)
        : RelationshipPolicyChangeBatchResult;
}

/// <summary>
/// Validated immutable relationship input for one clean game session.
/// </summary>
