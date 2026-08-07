using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Stable content-facing identity for an authored principal definition.
/// </summary>
public readonly record struct PrincipalContentId
{
    /// <summary>
    /// Creates an opaque case-sensitive content identity.
    /// </summary>
    public PrincipalContentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Authored identity and presentation metadata for one relationship principal.
/// </summary>
public sealed record PrincipalDefinition
{
    /// <summary>
    /// Creates one principal definition with stable runtime and content identities.
    /// </summary>
    public PrincipalDefinition(
        PrincipalId id,
        PrincipalContentId contentId,
        string name)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        ContentId = contentId;
        Name = name;
    }

    public PrincipalId Id { get; }

    public PrincipalContentId ContentId { get; }

    public string Name { get; }
}

/// <summary>
/// Qualitative treatment derived from authoritative directional standing.
/// </summary>
public enum StandingBand
{
    Hostile,
    Adversarial,
    Neutral,
    Favorable,
    Allied,
}

/// <summary>
/// Exact deterministic value underlying a qualitative standing band.
/// </summary>
public readonly record struct StandingValue(long Value);

/// <summary>
/// Stable content-facing identity for a standing policy.
/// </summary>
public readonly record struct StandingPolicyId
{
    /// <summary>
    /// Creates an opaque case-sensitive standing policy identity.
    /// </summary>
    public StandingPolicyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Session policy that bounds standing and maps exact values to accepted bands.
/// </summary>
public sealed class StandingPolicy
{
    /// <summary>
    /// Creates and validates a complete five-band standing policy.
    /// </summary>
    public StandingPolicy(
        StandingPolicyId id,
        StandingValue minimum,
        StandingValue maximum,
        StandingValue initial,
        StandingValue adversarialThreshold,
        StandingValue neutralThreshold,
        StandingValue favorableThreshold,
        StandingValue alliedThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        if (minimum.Value >= adversarialThreshold.Value
            || adversarialThreshold.Value >= neutralThreshold.Value
            || neutralThreshold.Value >= favorableThreshold.Value
            || favorableThreshold.Value >= alliedThreshold.Value
            || alliedThreshold.Value > maximum.Value)
        {
            throw new ArgumentException(
                "Standing bounds and band thresholds must be strictly ordered.");
        }

        if (initial.Value < minimum.Value || initial.Value > maximum.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initial),
                initial,
                "Initial standing must be within the configured bounds.");
        }

        Id = id;
        Minimum = minimum;
        Maximum = maximum;
        Initial = initial;
        AdversarialThreshold = adversarialThreshold;
        NeutralThreshold = neutralThreshold;
        FavorableThreshold = favorableThreshold;
        AlliedThreshold = alliedThreshold;
    }

    public StandingPolicyId Id { get; }

    public StandingValue Minimum { get; }

    public StandingValue Maximum { get; }

    public StandingValue Initial { get; }

    public StandingValue AdversarialThreshold { get; }

    public StandingValue NeutralThreshold { get; }

    public StandingValue FavorableThreshold { get; }

    public StandingValue AlliedThreshold { get; }

    /// <summary>
    /// Resolves one in-range exact value to its qualitative treatment band.
    /// </summary>
    public StandingBand GetBand(StandingValue value)
    {
        if (value.Value < Minimum.Value || value.Value > Maximum.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Standing must be within the configured bounds.");
        }

        if (value.Value >= AlliedThreshold.Value)
        {
            return StandingBand.Allied;
        }

        if (value.Value >= FavorableThreshold.Value)
        {
            return StandingBand.Favorable;
        }

        if (value.Value >= NeutralThreshold.Value)
        {
            return StandingBand.Neutral;
        }

        return value.Value >= AdversarialThreshold.Value
            ? StandingBand.Adversarial
            : StandingBand.Hostile;
    }
}

/// <summary>
/// Explicit initial directional standing from an assessing principal toward a
/// distinct subject principal.
/// </summary>
public sealed record InitialStandingSetup
{
    /// <summary>
    /// Creates one directional initial standing override.
    /// </summary>
    public InitialStandingSetup(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId,
        StandingValue value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(assessingPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(subjectPrincipalId.Value);
        if (assessingPrincipalId == subjectPrincipalId)
        {
            throw new ArgumentException(
                "A principal cannot hold directional standing toward itself.");
        }

        AssessingPrincipalId = assessingPrincipalId;
        SubjectPrincipalId = subjectPrincipalId;
        Value = value;
    }

    public PrincipalId AssessingPrincipalId { get; }

    public PrincipalId SubjectPrincipalId { get; }

    public StandingValue Value { get; }
}

/// <summary>
/// Explicit mutual diplomatic condition for one unordered principal pair.
/// </summary>
public enum DiplomaticCondition
{
    Peace,
    War,
}

/// <summary>
/// Authored non-default diplomatic condition for one principal pair.
/// </summary>
public sealed record InitialDiplomaticConditionSetup
{
    /// <summary>
    /// Creates one validated non-peace diplomatic setup entry.
    /// </summary>
    public InitialDiplomaticConditionSetup(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId,
        DiplomaticCondition condition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(firstPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(secondPrincipalId.Value);
        if (firstPrincipalId == secondPrincipalId)
        {
            throw new ArgumentException("Self-diplomacy is invalid.");
        }

        if (!Enum.IsDefined(condition) || condition == DiplomaticCondition.Peace)
        {
            throw new ArgumentOutOfRangeException(
                nameof(condition),
                condition,
                "Initial diplomacy stores only a defined non-peace condition.");
        }

        (LowerPrincipalId, UpperPrincipalId) = CanonicalPair(
            firstPrincipalId,
            secondPrincipalId);
        Condition = condition;
    }

    public PrincipalId LowerPrincipalId { get; }

    public PrincipalId UpperPrincipalId { get; }

    public DiplomaticCondition Condition { get; }

    /// <summary>
    /// Orders a distinct pair by stable principal identity.
    /// </summary>
    private static (PrincipalId Lower, PrincipalId Upper) CanonicalPair(
        PrincipalId first,
        PrincipalId second) =>
        first.Value < second.Value ? (first, second) : (second, first);
}

/// <summary>
/// Stable identity for one explicit relationship grant.
/// </summary>
public readonly record struct RelationshipGrantId
{
    /// <summary>
    /// Creates a non-zero grant identity.
    /// </summary>
    public RelationshipGrantId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
}

/// <summary>
/// Stable content-defined kind of relationship permission.
/// </summary>
public readonly record struct RelationshipGrantKind
{
    /// <summary>
    /// Creates an opaque case-sensitive grant kind.
    /// </summary>
    public RelationshipGrantKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Authored issued grant with a standing-dependent use requirement.
/// </summary>
public sealed record InitialRelationshipGrantSetup
{
    /// <summary>
    /// Creates one validated initial issued grant.
    /// </summary>
    public InitialRelationshipGrantSetup(
        RelationshipGrantId id,
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind,
        StandingBand minimumStandingBand)
    {
        ValidateGrantValues(
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

    /// <summary>
    /// Validates the shared structural fields used by setup and issue proposals.
    /// </summary>
    internal static void ValidateGrantValues(
        RelationshipGrantId id,
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind,
        StandingBand minimumStandingBand)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(issuerPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(holderPrincipalId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind.Value);
        if (issuerPrincipalId == holderPrincipalId)
        {
            throw new ArgumentException("A relationship grant requires distinct principals.");
        }

        if (!Enum.IsDefined(minimumStandingBand))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumStandingBand),
                minimumStandingBand,
                "Unknown minimum standing band.");
        }
    }
}

/// <summary>
/// Authoritative source domain for a standing-change delivery identity.
/// </summary>
public enum StandingChangeSourceKind
{
    Explicit,
}

/// <summary>
/// Stable source-scoped identity used to make one standing-change delivery
/// idempotent without requiring unrelated domain owners to share an allocator.
/// </summary>
public readonly record struct StandingChangeBatchId
{
    /// <summary>
    /// Creates a non-zero standing-change identity within one source domain.
    /// </summary>
    public StandingChangeBatchId(StandingChangeSourceKind sourceKind, ulong value)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown standing change source kind.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(value);
        SourceKind = sourceKind;
        Value = value;
    }

    public StandingChangeSourceKind SourceKind { get; }

    public ulong Value { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{SourceKind}:{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Stable ordering identity for one contribution within a directional pair.
/// </summary>
public readonly record struct StandingChangeContributionId
{
    /// <summary>
    /// Creates a non-zero contribution identity.
    /// </summary>
    public StandingChangeContributionId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Initial reason vocabulary for explicit relationship-domain changes.
/// Other domains add reasons only with their approved gameplay policy.
/// </summary>
public enum StandingChangeReason
{
    Explicit,
}

/// <summary>
/// One immutable delta and reason with stable within-pair ordering identity.
/// </summary>
public sealed record StandingChangeContribution
{
    /// <summary>
    /// Creates one validated standing contribution.
    /// </summary>
    public StandingChangeContribution(
        StandingChangeContributionId id,
        long delta,
        StandingChangeReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown standing change reason.");
        }

        Id = id;
        Delta = delta;
        Reason = reason;
    }

    public StandingChangeContributionId Id { get; }

    public long Delta { get; }

    public StandingChangeReason Reason { get; }
}

/// <summary>
/// Proposed directional standing effect from one assessing principal toward a
/// distinct subject principal.
/// </summary>
public sealed record StandingChangeProposal
{
    /// <summary>
    /// Creates one validated directional standing proposal.
    /// </summary>
    public StandingChangeProposal(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId,
        StandingChangeContribution contribution)
    {
        ArgumentOutOfRangeException.ThrowIfZero(assessingPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(subjectPrincipalId.Value);
        ArgumentNullException.ThrowIfNull(contribution);
        if (assessingPrincipalId == subjectPrincipalId)
        {
            throw new ArgumentException(
                "A principal cannot change standing toward itself.");
        }

        AssessingPrincipalId = assessingPrincipalId;
        SubjectPrincipalId = subjectPrincipalId;
        Contribution = contribution;
    }

    public PrincipalId AssessingPrincipalId { get; }

    public PrincipalId SubjectPrincipalId { get; }

    public StandingChangeContribution Contribution { get; }
}

/// <summary>
/// One idempotent standing-change delivery containing independently produced
/// proposals for deterministic reduction.
/// </summary>
public sealed record StandingChangeBatch
{
    /// <summary>
    /// Copies a non-empty proposal batch without retaining caller mutation.
    /// </summary>
    public StandingChangeBatch(
        StandingChangeBatchId id,
        IEnumerable<StandingChangeProposal> proposals)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentNullException.ThrowIfNull(proposals);
        StandingChangeProposal[] values = proposals.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException(
                "A standing-change batch requires at least one proposal.",
                nameof(proposals));
        }

        foreach (StandingChangeProposal proposal in values)
        {
            ArgumentNullException.ThrowIfNull(proposal);
        }

        Id = id;
        Proposals = new ReadOnlyCollection<StandingChangeProposal>(values);
    }

    public StandingChangeBatchId Id { get; }

    public IReadOnlyList<StandingChangeProposal> Proposals { get; }
}

/// <summary>
/// Typed reason that prevents a standing batch from mutating relationship state.
/// </summary>
public enum StandingChangeRejectionReason
{
    UnknownPrincipal,
    DuplicateContribution,
    DeltaOverflow,
    BatchIdentityConflict,
    FactSequenceExhausted,
}

/// <summary>
/// Prepared and committed result for one directional standing pair.
/// </summary>
public sealed record StandingChangeOutcome
{
    /// <summary>
    /// Creates one validated immutable result of directional contribution reduction.
    /// </summary>
    public StandingChangeOutcome(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId,
        StandingValue priorValue,
        StandingBand priorBand,
        long combinedDelta,
        StandingValue resultingValue,
        StandingBand resultingBand,
        IEnumerable<StandingChangeContribution> contributions)
    {
        ArgumentOutOfRangeException.ThrowIfZero(assessingPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(subjectPrincipalId.Value);
        if (assessingPrincipalId == subjectPrincipalId)
        {
            throw new ArgumentException(
                "A standing outcome requires distinct principals.");
        }

        if (!Enum.IsDefined(priorBand))
        {
            throw new ArgumentOutOfRangeException(
                nameof(priorBand),
                priorBand,
                "Unknown prior standing band.");
        }

        if (!Enum.IsDefined(resultingBand))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingBand),
                resultingBand,
                "Unknown resulting standing band.");
        }

        ArgumentNullException.ThrowIfNull(contributions);
        StandingChangeContribution[] contributionValues = contributions.ToArray();
        if (contributionValues.Length == 0)
        {
            throw new ArgumentException(
                "A standing outcome requires at least one contribution.",
                nameof(contributions));
        }

        long verifiedDelta = 0;
        foreach (StandingChangeContribution contribution in contributionValues)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            verifiedDelta = checked(verifiedDelta + contribution.Delta);
        }

        if (verifiedDelta != combinedDelta)
        {
            throw new ArgumentException(
                "Standing contributions do not equal the combined delta.",
                nameof(combinedDelta));
        }

        AssessingPrincipalId = assessingPrincipalId;
        SubjectPrincipalId = subjectPrincipalId;
        PriorValue = priorValue;
        PriorBand = priorBand;
        CombinedDelta = combinedDelta;
        ResultingValue = resultingValue;
        ResultingBand = resultingBand;
        Contributions = new ReadOnlyCollection<StandingChangeContribution>(
            contributionValues);
    }

    public PrincipalId AssessingPrincipalId { get; }

    public PrincipalId SubjectPrincipalId { get; }

    public StandingValue PriorValue { get; }

    public StandingBand PriorBand { get; }

    public long CombinedDelta { get; }

    public StandingValue ResultingValue { get; }

    public StandingBand ResultingBand { get; }

    public IReadOnlyList<StandingChangeContribution> Contributions { get; }

    public bool Changed => PriorValue != ResultingValue;
}

/// <summary>
/// Idempotent outcome of validating and committing one standing-change batch.
/// </summary>
public abstract record StandingChangeBatchResult
{
    private StandingChangeBatchResult()
    {
    }

    public sealed record Applied(
        StandingChangeBatchId BatchId,
        IReadOnlyList<StandingChangeOutcome> Outcomes)
        : StandingChangeBatchResult;

    public sealed record Rejected(
        StandingChangeBatchId BatchId,
        StandingChangeRejectionReason Reason)
        : StandingChangeBatchResult;
}

/// <summary>
/// Authoritative source domain for diplomacy and grant delivery identities.
/// </summary>
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
public sealed class RelationshipSetup
{
    /// <summary>
    /// Validates and canonicalizes principal definitions and standing overrides.
    /// </summary>
    public RelationshipSetup(
        IEnumerable<PrincipalDefinition> principals,
        PrincipalId playerPrincipalId,
        StandingPolicy standingPolicy,
        IEnumerable<InitialStandingSetup> standings)
        : this(principals, playerPrincipalId, standingPolicy, standings, [], [])
    {
    }

    /// <summary>
    /// Validates and canonicalizes complete principal, standing, diplomacy, and
    /// initial issued-grant state.
    /// </summary>
    public RelationshipSetup(
        IEnumerable<PrincipalDefinition> principals,
        PrincipalId playerPrincipalId,
        StandingPolicy standingPolicy,
        IEnumerable<InitialStandingSetup> standings,
        IEnumerable<InitialDiplomaticConditionSetup> diplomaticConditions,
        IEnumerable<InitialRelationshipGrantSetup> grants)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentOutOfRangeException.ThrowIfZero(playerPrincipalId.Value);
        ArgumentNullException.ThrowIfNull(standingPolicy);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(diplomaticConditions);
        ArgumentNullException.ThrowIfNull(grants);

        PrincipalDefinition[] principalValues = principals.ToArray();
        foreach (PrincipalDefinition principal in principalValues)
        {
            ArgumentNullException.ThrowIfNull(principal);
        }

        Array.Sort(
            principalValues,
            (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        var principalIds = new HashSet<PrincipalId>();
        var contentIds = new HashSet<PrincipalContentId>();
        foreach (PrincipalDefinition principal in principalValues)
        {
            if (!principalIds.Add(principal.Id))
            {
                throw new ArgumentException(
                    $"Duplicate principal {principal.Id}.",
                    nameof(principals));
            }

            if (!contentIds.Add(principal.ContentId))
            {
                throw new ArgumentException(
                    $"Duplicate principal content identity {principal.ContentId}.",
                    nameof(principals));
            }
        }

        if (!principalIds.Contains(playerPrincipalId))
        {
            throw new ArgumentException(
                $"Player principal {playerPrincipalId} is not registered.",
                nameof(playerPrincipalId));
        }

        InitialStandingSetup[] standingValues = standings.ToArray();
        foreach (InitialStandingSetup standing in standingValues)
        {
            ArgumentNullException.ThrowIfNull(standing);
        }

        Array.Sort(
            standingValues,
            (left, right) =>
            {
                int assessing = left.AssessingPrincipalId.Value.CompareTo(
                    right.AssessingPrincipalId.Value);
                return assessing != 0
                    ? assessing
                    : left.SubjectPrincipalId.Value.CompareTo(
                        right.SubjectPrincipalId.Value);
            });
        var standingKeys = new HashSet<(PrincipalId Assessing, PrincipalId Subject)>();
        foreach (InitialStandingSetup standing in standingValues)
        {
            if (!principalIds.Contains(standing.AssessingPrincipalId)
                || !principalIds.Contains(standing.SubjectPrincipalId))
            {
                throw new ArgumentException(
                    "Initial standing references an unknown principal.",
                    nameof(standings));
            }

            if (standing.Value.Value < standingPolicy.Minimum.Value
                || standing.Value.Value > standingPolicy.Maximum.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(standings),
                    standing.Value,
                    "Initial standing must be within the configured bounds.");
            }

            if (!standingKeys.Add((
                    standing.AssessingPrincipalId,
                    standing.SubjectPrincipalId)))
            {
                throw new ArgumentException(
                    "Duplicate initial directional standing.",
                    nameof(standings));
            }
        }

        InitialDiplomaticConditionSetup[] diplomaticValues = diplomaticConditions.ToArray();
        foreach (InitialDiplomaticConditionSetup diplomatic in diplomaticValues)
        {
            ArgumentNullException.ThrowIfNull(diplomatic);
        }

        Array.Sort(
            diplomaticValues,
            (left, right) =>
            {
                int lower = left.LowerPrincipalId.Value.CompareTo(
                    right.LowerPrincipalId.Value);
                return lower != 0
                    ? lower
                    : left.UpperPrincipalId.Value.CompareTo(right.UpperPrincipalId.Value);
            });
        var diplomaticKeys = new HashSet<(PrincipalId Lower, PrincipalId Upper)>();
        foreach (InitialDiplomaticConditionSetup diplomatic in diplomaticValues)
        {
            if (!principalIds.Contains(diplomatic.LowerPrincipalId)
                || !principalIds.Contains(diplomatic.UpperPrincipalId))
            {
                throw new ArgumentException(
                    "Initial diplomacy references an unknown principal.",
                    nameof(diplomaticConditions));
            }

            if (!diplomaticKeys.Add((
                    diplomatic.LowerPrincipalId,
                    diplomatic.UpperPrincipalId)))
            {
                throw new ArgumentException(
                    "Duplicate initial diplomatic pair.",
                    nameof(diplomaticConditions));
            }
        }

        InitialRelationshipGrantSetup[] grantValues = grants.ToArray();
        foreach (InitialRelationshipGrantSetup grant in grantValues)
        {
            ArgumentNullException.ThrowIfNull(grant);
        }

        Array.Sort(grantValues, (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        var grantIds = new HashSet<RelationshipGrantId>();
        foreach (InitialRelationshipGrantSetup grant in grantValues)
        {
            if (!principalIds.Contains(grant.IssuerPrincipalId)
                || !principalIds.Contains(grant.HolderPrincipalId))
            {
                throw new ArgumentException(
                    "Initial relationship grant references an unknown principal.",
                    nameof(grants));
            }

            if (!grantIds.Add(grant.Id))
            {
                throw new ArgumentException(
                    $"Duplicate initial relationship grant {grant.Id.Value}.",
                    nameof(grants));
            }
        }

        Principals = new ReadOnlyCollection<PrincipalDefinition>(principalValues);
        PlayerPrincipalId = playerPrincipalId;
        StandingPolicy = standingPolicy;
        Standings = new ReadOnlyCollection<InitialStandingSetup>(standingValues);
        DiplomaticConditions = new ReadOnlyCollection<InitialDiplomaticConditionSetup>(
            diplomaticValues);
        Grants = new ReadOnlyCollection<InitialRelationshipGrantSetup>(grantValues);
    }

    public IReadOnlyList<PrincipalDefinition> Principals { get; }

    public PrincipalId PlayerPrincipalId { get; }

    public StandingPolicy StandingPolicy { get; }

    public IReadOnlyList<InitialStandingSetup> Standings { get; }

    public IReadOnlyList<InitialDiplomaticConditionSetup> DiplomaticConditions { get; }

    public IReadOnlyList<InitialRelationshipGrantSetup> Grants { get; }
}

/// <summary>
/// Immutable diagnostic snapshot of one registered principal.
/// </summary>
public sealed record PrincipalSnapshot(
    PrincipalId Id,
    PrincipalContentId ContentId,
    string Name);

/// <summary>
/// Immutable diagnostic snapshot of one resolved directional standing value.
/// </summary>
public sealed record StandingSnapshot(
    PrincipalId AssessingPrincipalId,
    PrincipalId SubjectPrincipalId,
    StandingValue Value,
    StandingBand Band);

/// <summary>
/// Immutable mutual diplomatic condition for one canonical principal pair.
/// </summary>
public sealed record DiplomaticConditionSnapshot(
    PrincipalId LowerPrincipalId,
    PrincipalId UpperPrincipalId,
    DiplomaticCondition Condition);

/// <summary>
/// Immutable explicit grant state and its standing-dependent effectiveness.
/// </summary>
public sealed record RelationshipGrantSnapshot(
    RelationshipGrantId Id,
    PrincipalId IssuerPrincipalId,
    PrincipalId HolderPrincipalId,
    RelationshipGrantKind Kind,
    StandingBand MinimumStandingBand,
    bool IsIssued,
    bool IsEffective);

/// <summary>
/// Complete authoritative relationship diagnostics at one commit boundary.
/// </summary>
public sealed record RelationshipSnapshot(
    PrincipalId PlayerPrincipalId,
    StandingPolicyId StandingPolicyId,
    IReadOnlyList<PrincipalSnapshot> Principals,
    IReadOnlyList<StandingSnapshot> Standings,
    IReadOnlyList<DiplomaticConditionSnapshot> DiplomaticConditions,
    IReadOnlyList<RelationshipGrantSnapshot> Grants);

/// <summary>
/// Deterministic owner of principal identity and directional standing state.
/// </summary>
internal sealed class RelationshipOwner
{
    private readonly IReadOnlyList<PrincipalDefinition> _principals;
    private readonly HashSet<PrincipalId> _principalIds;
    private readonly Dictionary<(PrincipalId Assessing, PrincipalId Subject), StandingValue>
        _standingOverrides;
    private readonly StandingPolicy _standingPolicy;
    private readonly Dictionary<(PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>
        _diplomaticConditions;
    private readonly Dictionary<RelationshipGrantId, RelationshipGrantState> _grants;
    private readonly Dictionary<StandingChangeBatchId, CommittedStandingBatch>
        _committedStandingBatches = [];
    private readonly Dictionary<RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>
        _committedPolicyBatches = [];

    /// <summary>
    /// Copies canonical setup state into the authoritative runtime owner.
    /// </summary>
    internal RelationshipOwner(RelationshipSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _principals = setup.Principals;
        _principalIds = setup.Principals
            .Select(principal => principal.Id)
            .ToHashSet();
        _standingPolicy = setup.StandingPolicy;
        _standingOverrides = setup.Standings.ToDictionary(
            standing => (
                standing.AssessingPrincipalId,
                standing.SubjectPrincipalId),
            standing => standing.Value);
        _diplomaticConditions = setup.DiplomaticConditions.ToDictionary(
            value => (value.LowerPrincipalId, value.UpperPrincipalId),
            value => value.Condition);
        _grants = setup.Grants.ToDictionary(
            grant => grant.Id,
            grant => new RelationshipGrantState(
                grant.Id,
                grant.IssuerPrincipalId,
                grant.HolderPrincipalId,
                grant.Kind,
                grant.MinimumStandingBand,
                IsIssued: true));
        PlayerPrincipalId = setup.PlayerPrincipalId;
    }

    internal PrincipalId PlayerPrincipalId { get; }

    /// <summary>
    /// Validates and reduces a complete batch without mutating relationship state.
    /// </summary>
    internal StandingChangePreparation PrepareStandingChanges(StandingChangeBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        StandingChangeProposal[] ordered = batch.Proposals
            .OrderBy(proposal => proposal.AssessingPrincipalId.Value)
            .ThenBy(proposal => proposal.SubjectPrincipalId.Value)
            .ThenBy(proposal => proposal.Contribution.Id.Value)
            .ToArray();
        if (_committedStandingBatches.TryGetValue(batch.Id, out CommittedStandingBatch? prior))
        {
            return prior.Proposals.SequenceEqual(ordered)
                ? new StandingChangePreparation.Resolved(prior.Result)
                : ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.BatchIdentityConflict);
        }

        var contributionKeys = new HashSet<(
            PrincipalId Assessing,
            PrincipalId Subject,
            StandingChangeContributionId Contribution)>();
        foreach (StandingChangeProposal proposal in ordered)
        {
            if (!_principalIds.Contains(proposal.AssessingPrincipalId)
                || !_principalIds.Contains(proposal.SubjectPrincipalId))
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.UnknownPrincipal);
            }

            if (!contributionKeys.Add((
                    proposal.AssessingPrincipalId,
                    proposal.SubjectPrincipalId,
                    proposal.Contribution.Id)))
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.DuplicateContribution);
            }
        }

        var outcomes = new List<StandingChangeOutcome>();
        foreach (IGrouping<(PrincipalId AssessingPrincipalId, PrincipalId SubjectPrincipalId),
                     StandingChangeProposal> group in ordered.GroupBy(proposal => (
                         proposal.AssessingPrincipalId,
                         proposal.SubjectPrincipalId)))
        {
            StandingChangeContribution[] contributions = group
                .Select(proposal => proposal.Contribution)
                .ToArray();
            long combinedDelta = 0;
            try
            {
                foreach (StandingChangeContribution contribution in contributions)
                {
                    combinedDelta = checked(combinedDelta + contribution.Delta);
                }
            }
            catch (OverflowException)
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.DeltaOverflow);
            }

            StandingValue priorValue = GetStanding(
                group.Key.AssessingPrincipalId,
                group.Key.SubjectPrincipalId);
            long unboundedResult;
            try
            {
                unboundedResult = checked(priorValue.Value + combinedDelta);
            }
            catch (OverflowException)
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.DeltaOverflow);
            }

            var resultingValue = new StandingValue(Math.Clamp(
                unboundedResult,
                _standingPolicy.Minimum.Value,
                _standingPolicy.Maximum.Value));
            outcomes.Add(new StandingChangeOutcome(
                group.Key.AssessingPrincipalId,
                group.Key.SubjectPrincipalId,
                priorValue,
                _standingPolicy.GetBand(priorValue),
                combinedDelta,
                resultingValue,
                _standingPolicy.GetBand(resultingValue),
                contributions));
        }

        var result = new StandingChangeBatchResult.Applied(
            batch.Id,
            new ReadOnlyCollection<StandingChangeOutcome>(outcomes));
        return new StandingChangePreparation.Prepared(new PreparedStandingChange(
            batch.Id,
            ordered,
            result,
            outcomes.Where(outcome => outcome.Changed).ToArray()));
    }

    /// <summary>
    /// Applies an already validated standing preparation through operations that
    /// cannot reject and records its idempotent receipt.
    /// </summary>
    internal StandingChangeBatchResult ApplyStandingChanges(
        PreparedStandingChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        foreach (StandingChangeOutcome outcome in prepared.ChangedOutcomes)
        {
            _standingOverrides[(
                outcome.AssessingPrincipalId,
                outcome.SubjectPrincipalId)] = outcome.ResultingValue;
        }

        _committedStandingBatches.Add(
            prepared.BatchId,
            new CommittedStandingBatch(prepared.Proposals, prepared.Result));
        return prepared.Result;
    }

    /// <summary>
    /// Validates and prepares diplomacy and grant changes without mutation.
    /// </summary>
    internal RelationshipPolicyChangePreparation PreparePolicyChanges(
        RelationshipPolicyChangeBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        RelationshipPolicyChangeProposal[] ordered = batch.Proposals
            .OrderBy(ProposalPrimaryIdentity)
            .ThenBy(ProposalSecondaryIdentity)
            .ThenBy(ProposalKindOrder)
            .ThenBy(ProposalGrantIdentity)
            .ToArray();
        if (_committedPolicyBatches.TryGetValue(
                batch.Id,
                out CommittedRelationshipPolicyBatch? prior))
        {
            return prior.Proposals.SequenceEqual(ordered)
                ? new RelationshipPolicyChangePreparation.Resolved(prior.Result)
                : PolicyRejection(
                    batch.Id,
                    RelationshipPolicyChangeRejectionReason.BatchIdentityConflict);
        }

        var diplomaticKeys = new HashSet<(PrincipalId Lower, PrincipalId Upper)>();
        var grantIds = new HashSet<RelationshipGrantId>();
        var diplomaticOutcomes = new List<DiplomaticConditionChangeOutcome>();
        var grantOutcomes = new List<RelationshipGrantChangeOutcome>();
        foreach (RelationshipPolicyChangeProposal proposal in ordered)
        {
            switch (proposal)
            {
                case SetDiplomaticConditionProposal diplomatic:
                    if (!PrincipalsExist(
                            diplomatic.LowerPrincipalId,
                            diplomatic.UpperPrincipalId))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.UnknownPrincipal);
                    }

                    if (!diplomaticKeys.Add((
                            diplomatic.LowerPrincipalId,
                            diplomatic.UpperPrincipalId)))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason
                                .DuplicateDiplomaticAssignment);
                    }

                    diplomaticOutcomes.Add(new DiplomaticConditionChangeOutcome(
                        diplomatic.LowerPrincipalId,
                        diplomatic.UpperPrincipalId,
                        GetDiplomaticCondition(
                            diplomatic.LowerPrincipalId,
                            diplomatic.UpperPrincipalId),
                        diplomatic.Condition,
                        diplomatic.Reason));
                    break;

                case IssueRelationshipGrantProposal issue:
                    if (!PrincipalsExist(issue.IssuerPrincipalId, issue.HolderPrincipalId))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.UnknownPrincipal);
                    }

                    if (!grantIds.Add(issue.Id))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.DuplicateGrantAssignment);
                    }

                    if (_grants.ContainsKey(issue.Id))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.GrantIdentityAlreadyExists);
                    }

                    if (GetStandingBand(issue.IssuerPrincipalId, issue.HolderPrincipalId)
                        < issue.MinimumStandingBand)
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason
                                .StandingRequirementNotMet);
                    }

                    grantOutcomes.Add(new RelationshipGrantChangeOutcome(
                        issue.Id,
                        issue.IssuerPrincipalId,
                        issue.HolderPrincipalId,
                        issue.Kind,
                        issue.MinimumStandingBand,
                        priorIssued: false,
                        resultingIssued: true,
                        issue.Reason));
                    break;

                case RevokeRelationshipGrantProposal revoke:
                    if (!grantIds.Add(revoke.Id))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.DuplicateGrantAssignment);
                    }

                    if (!_grants.TryGetValue(revoke.Id, out RelationshipGrantState? grant))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.UnknownGrant);
                    }

                    if (!grant.IsIssued)
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.GrantAlreadyRevoked);
                    }

                    grantOutcomes.Add(new RelationshipGrantChangeOutcome(
                        grant.Id,
                        grant.IssuerPrincipalId,
                        grant.HolderPrincipalId,
                        grant.Kind,
                        grant.MinimumStandingBand,
                        priorIssued: true,
                        resultingIssued: false,
                        revoke.Reason));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported relationship policy proposal {proposal.GetType().Name}.");
            }
        }

        var result = new RelationshipPolicyChangeBatchResult.Applied(
            batch.Id,
            GameSnapshotCollection.Copy(diplomaticOutcomes),
            GameSnapshotCollection.Copy(grantOutcomes));
        return new RelationshipPolicyChangePreparation.Prepared(
            new PreparedRelationshipPolicyChange(batch.Id, ordered, result));
    }

    /// <summary>
    /// Applies an already validated diplomacy and grant preparation and records
    /// its idempotent receipt.
    /// </summary>
    internal RelationshipPolicyChangeBatchResult ApplyPolicyChanges(
        PreparedRelationshipPolicyChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        foreach (DiplomaticConditionChangeOutcome outcome in
                 prepared.Result.DiplomaticOutcomes.Where(value => value.Changed))
        {
            var key = (outcome.LowerPrincipalId, outcome.UpperPrincipalId);
            if (outcome.ResultingCondition == DiplomaticCondition.Peace)
            {
                _diplomaticConditions.Remove(key);
            }
            else
            {
                _diplomaticConditions[key] = outcome.ResultingCondition;
            }
        }

        foreach (RelationshipGrantChangeOutcome outcome in prepared.Result.GrantOutcomes)
        {
            _grants[outcome.Id] = new RelationshipGrantState(
                outcome.Id,
                outcome.IssuerPrincipalId,
                outcome.HolderPrincipalId,
                outcome.Kind,
                outcome.MinimumStandingBand,
                outcome.ResultingIssued);
        }

        _committedPolicyBatches.Add(
            prepared.BatchId,
            new CommittedRelationshipPolicyBatch(prepared.Proposals, prepared.Result));
        return prepared.Result;
    }

    /// <summary>
    /// Returns the mutual condition for a registered, distinct principal pair.
    /// </summary>
    internal DiplomaticCondition GetDiplomaticCondition(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId)
    {
        ValidateKnownDistinctPrincipals(firstPrincipalId, secondPrincipalId);
        (PrincipalId lower, PrincipalId upper) = firstPrincipalId.Value < secondPrincipalId.Value
            ? (firstPrincipalId, secondPrincipalId)
            : (secondPrincipalId, firstPrincipalId);
        return _diplomaticConditions.GetValueOrDefault(
            (lower, upper),
            DiplomaticCondition.Peace);
    }

    /// <summary>
    /// Reports whether any matching issued grant currently satisfies its
    /// issuer-to-holder standing requirement.
    /// </summary>
    internal bool HasEffectiveGrant(
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind)
    {
        ValidateKnownDistinctPrincipals(issuerPrincipalId, holderPrincipalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind.Value);
        return _grants.Values.Any(grant =>
            grant.IssuerPrincipalId == issuerPrincipalId
            && grant.HolderPrincipalId == holderPrincipalId
            && grant.Kind == kind
            && IsEffective(grant));
    }

    /// <summary>
    /// Resolves the complete directional matrix in stable principal order.
    /// </summary>
    internal RelationshipSnapshot CaptureSnapshot()
    {
        var standings = new List<StandingSnapshot>(
            checked(_principals.Count * Math.Max(0, _principals.Count - 1)));
        var diplomaticConditions = new List<DiplomaticConditionSnapshot>(
            checked(_principals.Count * Math.Max(0, _principals.Count - 1) / 2));
        foreach (PrincipalDefinition assessing in _principals)
        {
            foreach (PrincipalDefinition subject in _principals)
            {
                if (assessing.Id == subject.Id)
                {
                    continue;
                }

                StandingValue value = GetStanding(assessing.Id, subject.Id);
                standings.Add(new StandingSnapshot(
                    assessing.Id,
                    subject.Id,
                    value,
                    _standingPolicy.GetBand(value)));
                if (assessing.Id.Value < subject.Id.Value)
                {
                    diplomaticConditions.Add(new DiplomaticConditionSnapshot(
                        assessing.Id,
                        subject.Id,
                        GetDiplomaticCondition(assessing.Id, subject.Id)));
                }
            }
        }

        return new RelationshipSnapshot(
            PlayerPrincipalId,
            _standingPolicy.Id,
            GameSnapshotCollection.Copy(_principals.Select(principal =>
                new PrincipalSnapshot(
                    principal.Id,
                    principal.ContentId,
                    principal.Name))),
            GameSnapshotCollection.Copy(standings),
            GameSnapshotCollection.Copy(diplomaticConditions),
            GameSnapshotCollection.Copy(_grants.Values
                .OrderBy(grant => grant.Id.Value)
                .Select(grant => new RelationshipGrantSnapshot(
                    grant.Id,
                    grant.IssuerPrincipalId,
                    grant.HolderPrincipalId,
                    grant.Kind,
                    grant.MinimumStandingBand,
                    grant.IsIssued,
                    IsEffective(grant)))));
    }

    private StandingValue GetStanding(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId) =>
        _standingOverrides.GetValueOrDefault(
            (assessingPrincipalId, subjectPrincipalId),
            _standingPolicy.Initial);

    private StandingBand GetStandingBand(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId) =>
        _standingPolicy.GetBand(GetStanding(assessingPrincipalId, subjectPrincipalId));

    /// <summary>
    /// Combines persistent issuance with the current directional standing band.
    /// </summary>
    private bool IsEffective(RelationshipGrantState grant) =>
        grant.IsIssued
        && GetStandingBand(grant.IssuerPrincipalId, grant.HolderPrincipalId)
            >= grant.MinimumStandingBand;

    /// <summary>
    /// Reports whether both relationship endpoints are registered.
    /// </summary>
    private bool PrincipalsExist(PrincipalId first, PrincipalId second) =>
        _principalIds.Contains(first) && _principalIds.Contains(second);

    /// <summary>
    /// Enforces the shared endpoint contract for public relationship queries.
    /// </summary>
    private void ValidateKnownDistinctPrincipals(PrincipalId first, PrincipalId second)
    {
        ArgumentOutOfRangeException.ThrowIfZero(first.Value);
        ArgumentOutOfRangeException.ThrowIfZero(second.Value);
        if (first == second)
        {
            throw new ArgumentException("A relationship query requires distinct principals.");
        }

        if (!PrincipalsExist(first, second))
        {
            throw new ArgumentException("Relationship query references an unknown principal.");
        }
    }

    /// <summary>
    /// Resolves the first stable proposal ordering identity.
    /// </summary>
    private static ulong ProposalPrimaryIdentity(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            SetDiplomaticConditionProposal value => value.LowerPrincipalId.Value,
            IssueRelationshipGrantProposal value => value.IssuerPrincipalId.Value,
            RevokeRelationshipGrantProposal => ulong.MaxValue,
            _ => throw new InvalidOperationException("Unsupported relationship policy proposal."),
        };

    /// <summary>
    /// Resolves the second stable proposal ordering identity.
    /// </summary>
    private static ulong ProposalSecondaryIdentity(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            SetDiplomaticConditionProposal value => value.UpperPrincipalId.Value,
            IssueRelationshipGrantProposal value => value.HolderPrincipalId.Value,
            RevokeRelationshipGrantProposal => ulong.MaxValue,
            _ => throw new InvalidOperationException("Unsupported relationship policy proposal."),
        };

    /// <summary>
    /// Orders closed proposal variants independently of runtime type metadata.
    /// </summary>
    private static int ProposalKindOrder(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            SetDiplomaticConditionProposal => 0,
            IssueRelationshipGrantProposal => 1,
            RevokeRelationshipGrantProposal => 2,
            _ => throw new InvalidOperationException("Unsupported relationship policy proposal."),
        };

    /// <summary>
    /// Resolves the stable grant tie-breaker for proposal ordering.
    /// </summary>
    private static ulong ProposalGrantIdentity(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            IssueRelationshipGrantProposal value => value.Id.Value,
            RevokeRelationshipGrantProposal value => value.Id.Value,
            _ => 0,
        };

    private static StandingChangePreparation.Resolved ResolvedRejection(
        StandingChangeBatchId batchId,
        StandingChangeRejectionReason reason) =>
        new(new StandingChangeBatchResult.Rejected(batchId, reason));

    private static RelationshipPolicyChangePreparation.Resolved PolicyRejection(
        RelationshipPolicyChangeBatchId batchId,
        RelationshipPolicyChangeRejectionReason reason) =>
        new(new RelationshipPolicyChangeBatchResult.Rejected(batchId, reason));
}

internal abstract record StandingChangePreparation
{
    private StandingChangePreparation()
    {
    }

    internal sealed record Resolved(StandingChangeBatchResult Result)
        : StandingChangePreparation;

    internal sealed record Prepared(PreparedStandingChange Value)
        : StandingChangePreparation;
}

internal sealed record PreparedStandingChange(
    StandingChangeBatchId BatchId,
    IReadOnlyList<StandingChangeProposal> Proposals,
    StandingChangeBatchResult.Applied Result,
    IReadOnlyList<StandingChangeOutcome> ChangedOutcomes);

internal sealed record CommittedStandingBatch(
    IReadOnlyList<StandingChangeProposal> Proposals,
    StandingChangeBatchResult.Applied Result);

internal abstract record RelationshipPolicyChangePreparation
{
    private RelationshipPolicyChangePreparation()
    {
    }

    internal sealed record Resolved(RelationshipPolicyChangeBatchResult Result)
        : RelationshipPolicyChangePreparation;

    internal sealed record Prepared(PreparedRelationshipPolicyChange Value)
        : RelationshipPolicyChangePreparation;
}

internal sealed record PreparedRelationshipPolicyChange(
    RelationshipPolicyChangeBatchId BatchId,
    IReadOnlyList<RelationshipPolicyChangeProposal> Proposals,
    RelationshipPolicyChangeBatchResult.Applied Result);

internal sealed record CommittedRelationshipPolicyBatch(
    IReadOnlyList<RelationshipPolicyChangeProposal> Proposals,
    RelationshipPolicyChangeBatchResult.Applied Result);

internal sealed record RelationshipGrantState(
    RelationshipGrantId Id,
    PrincipalId IssuerPrincipalId,
    PrincipalId HolderPrincipalId,
    RelationshipGrantKind Kind,
    StandingBand MinimumStandingBand,
    bool IsIssued);
