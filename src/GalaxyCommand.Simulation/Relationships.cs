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

    /// <summary>
    /// Directly constructs a fully validated restored owner without replaying
    /// setup or committed relationship deliveries.
    /// </summary>
    private RelationshipOwner(
        IReadOnlyList<PrincipalDefinition> principals,
        StandingPolicy standingPolicy,
        Dictionary<(PrincipalId Assessing, PrincipalId Subject), StandingValue> standings,
        Dictionary<(PrincipalId Lower, PrincipalId Upper), DiplomaticCondition> diplomacy,
        Dictionary<RelationshipGrantId, RelationshipGrantState> grants,
        Dictionary<StandingChangeBatchId, CommittedStandingBatch> standingReceipts,
        Dictionary<RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>
            policyReceipts,
        PrincipalId playerPrincipalId)
    {
        _principals = principals;
        _principalIds = principals.Select(principal => principal.Id).ToHashSet();
        _standingPolicy = standingPolicy;
        _standingOverrides = standings;
        _diplomaticConditions = diplomacy;
        _grants = grants;
        _committedStandingBatches = standingReceipts;
        _committedPolicyBatches = policyReceipts;
        PlayerPrincipalId = playerPrincipalId;
    }

    internal PrincipalId PlayerPrincipalId { get; }

    /// <summary>
    /// Captures complete relationship truth and durable delivery receipts in
    /// stable identity order, excluding derived bands and projections.
    /// </summary>
    internal RelationshipCheckpoint CaptureCheckpoint()
    {
        RelationshipSnapshot snapshot = CaptureSnapshot();
        return new RelationshipCheckpoint(
            PlayerPrincipalId,
            new RelationshipStandingPolicyCheckpoint(
                _standingPolicy.Id.Value,
                _standingPolicy.Minimum,
                _standingPolicy.Maximum,
                _standingPolicy.Initial,
                _standingPolicy.AdversarialThreshold,
                _standingPolicy.NeutralThreshold,
                _standingPolicy.FavorableThreshold,
                _standingPolicy.AlliedThreshold),
            _principals.Select(principal => new RelationshipPrincipalCheckpoint(
                principal.Id,
                principal.ContentId.Value,
                principal.Name)),
            snapshot.Standings.Select(standing => new RelationshipStandingCheckpoint(
                standing.AssessingPrincipalId,
                standing.SubjectPrincipalId,
                standing.Value)),
            snapshot.DiplomaticConditions.Select(value =>
                new RelationshipDiplomacyCheckpoint(
                    value.LowerPrincipalId,
                    value.UpperPrincipalId,
                    value.Condition)),
            _grants.Values
                .OrderBy(grant => grant.Id.Value)
                .Select(grant => new RelationshipGrantCheckpoint(
                    grant.Id,
                    grant.IssuerPrincipalId,
                    grant.HolderPrincipalId,
                    grant.Kind.Value,
                    grant.MinimumStandingBand,
                    grant.IsIssued)),
            _committedStandingBatches
                .OrderBy(value => value.Key.SourceKind)
                .ThenBy(value => value.Key.Value)
                .Select(value => new StandingBatchReceiptCheckpoint(
                    value.Key,
                    value.Value.Proposals,
                    value.Value.Result)),
            _committedPolicyBatches
                .OrderBy(value => value.Key.SourceKind)
                .ThenBy(value => value.Key.Value)
                .Select(value => new PolicyBatchReceiptCheckpoint(
                    value.Key,
                    value.Value.Proposals,
                    value.Value.Result)));
    }

    /// <summary>
    /// Validates and directly restores complete relationship truth and
    /// idempotency receipts without emitting facts or replaying any batch.
    /// </summary>
    internal static CheckpointResult<RelationshipOwner> RestoreCheckpoint(
        RelationshipCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CheckpointResult<StandingPolicy> policyResult = RestoreStandingPolicy(
            checkpoint.StandingPolicy);
        if (!policyResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(policyResult.Failure!);
        }

        CheckpointResult<IReadOnlyList<PrincipalDefinition>> principalResult =
            RestorePrincipals(checkpoint);
        if (!principalResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(principalResult.Failure!);
        }

        IReadOnlyList<PrincipalDefinition> principals = principalResult.Value!;
        var principalIds = principals.Select(principal => principal.Id).ToHashSet();
        if (checkpoint.PlayerPrincipalId.Value == 0
            || !principalIds.Contains(checkpoint.PlayerPrincipalId))
        {
            return Rejected(
                "$.checkpoint.relationships.playerPrincipalId",
                "The player principal must name one registered principal.");
        }

        CheckpointResult<Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>> standingResult =
            RestoreStandings(checkpoint, principalIds, policyResult.Value!);
        if (!standingResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(standingResult.Failure!);
        }

        CheckpointResult<Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>> diplomacyResult =
            RestoreDiplomacy(checkpoint, principalIds);
        if (!diplomacyResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(diplomacyResult.Failure!);
        }

        CheckpointResult<Dictionary<RelationshipGrantId, RelationshipGrantState>> grantResult =
            RestoreGrants(checkpoint, principalIds);
        if (!grantResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(grantResult.Failure!);
        }

        CheckpointResult<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>
            standingReceiptResult = RestoreStandingReceipts(
                checkpoint,
                principalIds,
                policyResult.Value!);
        if (!standingReceiptResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(
                standingReceiptResult.Failure!);
        }

        CheckpointResult<Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
            policyReceiptResult = RestorePolicyReceipts(
                checkpoint,
                principalIds,
                grantResult.Value!);
        if (!policyReceiptResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(
                policyReceiptResult.Failure!);
        }

        return CheckpointResult<RelationshipOwner>.Success(new RelationshipOwner(
            principals,
            policyResult.Value!,
            standingResult.Value!,
            diplomacyResult.Value!,
            grantResult.Value!,
            standingReceiptResult.Value!,
            policyReceiptResult.Value!,
            checkpoint.PlayerPrincipalId));
    }

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

    /// <summary>
    /// Reconstructs the exact standing policy after validating its raw saved
    /// identity, bounds, initial value, and ordered thresholds.
    /// </summary>
    private static CheckpointResult<StandingPolicy> RestoreStandingPolicy(
        RelationshipStandingPolicyCheckpoint? checkpoint)
    {
        const string path = "$.checkpoint.relationships.standingPolicy";
        if (checkpoint is null || string.IsNullOrWhiteSpace(checkpoint.Id))
        {
            return RejectedPolicy(path, "The standing policy identity is required.");
        }

        if (checkpoint.Minimum.Value >= checkpoint.AdversarialThreshold.Value
            || checkpoint.AdversarialThreshold.Value >= checkpoint.NeutralThreshold.Value
            || checkpoint.NeutralThreshold.Value >= checkpoint.FavorableThreshold.Value
            || checkpoint.FavorableThreshold.Value >= checkpoint.AlliedThreshold.Value
            || checkpoint.AlliedThreshold.Value > checkpoint.Maximum.Value)
        {
            return RejectedPolicy(path, "Standing bounds and thresholds are not ordered.");
        }

        if (!IsWithinPolicy(
                checkpoint.Initial,
                checkpoint.Minimum,
                checkpoint.Maximum))
        {
            return RejectedPolicy(
                $"{path}.initial",
                "The initial standing is outside the policy bounds.");
        }

        return CheckpointResult<StandingPolicy>.Success(new StandingPolicy(
            new StandingPolicyId(checkpoint.Id),
            checkpoint.Minimum,
            checkpoint.Maximum,
            checkpoint.Initial,
            checkpoint.AdversarialThreshold,
            checkpoint.NeutralThreshold,
            checkpoint.FavorableThreshold,
            checkpoint.AlliedThreshold));
    }

    /// <summary>
    /// Restores unique runtime and content principal identities in canonical
    /// order without resolving display metadata from an implicit catalog.
    /// </summary>
    private static CheckpointResult<IReadOnlyList<PrincipalDefinition>> RestorePrincipals(
        RelationshipCheckpoint checkpoint)
    {
        const string path = "$.checkpoint.relationships.principals";
        var ids = new HashSet<PrincipalId>();
        var contentIds = new HashSet<string>(StringComparer.Ordinal);
        var principals = new List<PrincipalDefinition>(checkpoint.Principals.Count);
        for (int index = 0; index < checkpoint.Principals.Count; index++)
        {
            RelationshipPrincipalCheckpoint? principal = checkpoint.Principals[index];
            if (principal is null || principal.Id.Value == 0)
            {
                return RejectedPrincipals(
                    $"{path}[{index}]",
                    "A principal checkpoint with a nonzero identity is required.");
            }

            if (!ids.Add(principal.Id))
            {
                return RejectedPrincipals(
                    $"{path}[{index}].id",
                    "The principal identity is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(principal.ContentId)
                || !contentIds.Add(principal.ContentId))
            {
                return RejectedPrincipals(
                    $"{path}[{index}].contentId",
                    "The principal content identity is missing or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(principal.Name))
            {
                return RejectedPrincipals(
                    $"{path}[{index}].name",
                    "The principal display name is required.");
            }

            principals.Add(new PrincipalDefinition(
                principal.Id,
                new PrincipalContentId(principal.ContentId),
                principal.Name));
        }

        principals.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
        return CheckpointResult<IReadOnlyList<PrincipalDefinition>>.Success(
            new ReadOnlyCollection<PrincipalDefinition>(principals));
    }

    /// <summary>
    /// Requires one exact in-range standing value for every directional pair,
    /// preventing omitted external data from silently taking the policy default.
    /// </summary>
    private static CheckpointResult<Dictionary<
        (PrincipalId Assessing, PrincipalId Subject), StandingValue>> RestoreStandings(
        RelationshipCheckpoint checkpoint,
        HashSet<PrincipalId> principalIds,
        StandingPolicy policy)
    {
        const string path = "$.checkpoint.relationships.standings";
        int expectedCount = checked(principalIds.Count * Math.Max(0, principalIds.Count - 1));
        if (checkpoint.Standings.Count != expectedCount)
        {
            return RejectedStandings(
                path,
                "The complete directional standing matrix is required.");
        }

        var standings = new Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>();
        for (int index = 0; index < checkpoint.Standings.Count; index++)
        {
            RelationshipStandingCheckpoint? standing = checkpoint.Standings[index];
            if (standing is null
                || standing.AssessingPrincipalId == standing.SubjectPrincipalId
                || !principalIds.Contains(standing.AssessingPrincipalId)
                || !principalIds.Contains(standing.SubjectPrincipalId))
            {
                return RejectedStandings(
                    $"{path}[{index}]",
                    "A standing entry must reference two distinct registered principals.");
            }

            if (!IsWithinPolicy(standing.Value, policy.Minimum, policy.Maximum))
            {
                return RejectedStandings(
                    $"{path}[{index}].value",
                    "The standing value is outside the restored policy bounds.");
            }

            if (!standings.TryAdd(
                    (standing.AssessingPrincipalId, standing.SubjectPrincipalId),
                    standing.Value))
            {
                return RejectedStandings(
                    $"{path}[{index}]",
                    "The directional standing pair is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>>.Success(standings);
    }

    /// <summary>
    /// Requires one defined condition for every canonical unordered pair so a
    /// hand edit cannot accidentally replace missing diplomacy with peace.
    /// </summary>
    private static CheckpointResult<Dictionary<
        (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>> RestoreDiplomacy(
        RelationshipCheckpoint checkpoint,
        HashSet<PrincipalId> principalIds)
    {
        const string path = "$.checkpoint.relationships.diplomaticConditions";
        int expectedCount = checked(
            principalIds.Count * Math.Max(0, principalIds.Count - 1) / 2);
        if (checkpoint.DiplomaticConditions.Count != expectedCount)
        {
            return RejectedDiplomacy(path, "The complete diplomatic pair matrix is required.");
        }

        var diplomacy = new Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>();
        for (int index = 0; index < checkpoint.DiplomaticConditions.Count; index++)
        {
            RelationshipDiplomacyCheckpoint? value =
                checkpoint.DiplomaticConditions[index];
            if (value is null
                || value.LowerPrincipalId.Value >= value.UpperPrincipalId.Value
                || !principalIds.Contains(value.LowerPrincipalId)
                || !principalIds.Contains(value.UpperPrincipalId)
                || !Enum.IsDefined(value.Condition))
            {
                return RejectedDiplomacy(
                    $"{path}[{index}]",
                    "Diplomacy must name a defined condition for a canonical principal pair.");
            }

            if (!diplomacy.TryAdd(
                    (value.LowerPrincipalId, value.UpperPrincipalId),
                    value.Condition))
            {
                return RejectedDiplomacy(
                    $"{path}[{index}]",
                    "The diplomatic principal pair is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>>.Success(diplomacy);
    }

    /// <summary>
    /// Restores issued and revoked grants as persistent state while leaving
    /// effectiveness derived from the restored standing matrix.
    /// </summary>
    private static CheckpointResult<Dictionary<RelationshipGrantId, RelationshipGrantState>>
        RestoreGrants(
            RelationshipCheckpoint checkpoint,
            HashSet<PrincipalId> principalIds)
    {
        const string path = "$.checkpoint.relationships.grants";
        var grants = new Dictionary<RelationshipGrantId, RelationshipGrantState>();
        for (int index = 0; index < checkpoint.Grants.Count; index++)
        {
            RelationshipGrantCheckpoint? grant = checkpoint.Grants[index];
            if (grant is null
                || grant.Id.Value == 0
                || grant.IssuerPrincipalId == grant.HolderPrincipalId
                || !principalIds.Contains(grant.IssuerPrincipalId)
                || !principalIds.Contains(grant.HolderPrincipalId)
                || string.IsNullOrWhiteSpace(grant.Kind)
                || !Enum.IsDefined(grant.MinimumStandingBand))
            {
                return RejectedGrants(
                    $"{path}[{index}]",
                    "A grant has invalid identity, endpoints, kind, or standing band.");
            }

            var state = new RelationshipGrantState(
                grant.Id,
                grant.IssuerPrincipalId,
                grant.HolderPrincipalId,
                new RelationshipGrantKind(grant.Kind),
                grant.MinimumStandingBand,
                grant.IsIssued);
            if (!grants.TryAdd(grant.Id, state))
            {
                return RejectedGrants(
                    $"{path}[{index}].id",
                    "The relationship grant identity is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<RelationshipGrantId, RelationshipGrantState>>
            .Success(grants);
    }

    /// <summary>
    /// Restores standing delivery receipts only when their canonical proposals
    /// and saved outcomes independently prove the same checked reduction.
    /// </summary>
    private static CheckpointResult<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>
        RestoreStandingReceipts(
            RelationshipCheckpoint checkpoint,
            HashSet<PrincipalId> principalIds,
            StandingPolicy policy)
    {
        const string path = "$.checkpoint.relationships.standingReceipts";
        var receipts = new Dictionary<StandingChangeBatchId, CommittedStandingBatch>();
        for (int index = 0; index < checkpoint.StandingReceipts.Count; index++)
        {
            StandingBatchReceiptCheckpoint? receipt = checkpoint.StandingReceipts[index];
            string receiptPath = $"{path}[{index}]";
            if (receipt is null
                || receipt.BatchId.Value == 0
                || !Enum.IsDefined(receipt.BatchId.SourceKind)
                || receipt.Proposals is null
                || receipt.Proposals.Count == 0
                || receipt.Result is null
                || receipt.Result.BatchId != receipt.BatchId
                || receipt.Result.Outcomes is null)
            {
                return RejectedStandingReceipts(
                    receiptPath,
                    "A standing receipt is missing required identity, proposals, or result.");
            }

            StandingChangeProposal[] proposals = receipt.Proposals
                .OfType<StandingChangeProposal>()
                .ToArray();
            if (proposals.Length != receipt.Proposals.Count
                || proposals.Any(proposal =>
                    !principalIds.Contains(proposal.AssessingPrincipalId)
                    || !principalIds.Contains(proposal.SubjectPrincipalId)
                    || proposal.AssessingPrincipalId == proposal.SubjectPrincipalId
                    || proposal.Contribution.Id.Value == 0
                    || !Enum.IsDefined(proposal.Contribution.Reason)))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.proposals",
                    "Standing receipt proposals are missing or structurally invalid.");
            }

            StandingChangeProposal[] canonical = proposals
                .OrderBy(proposal => proposal.AssessingPrincipalId.Value)
                .ThenBy(proposal => proposal.SubjectPrincipalId.Value)
                .ThenBy(proposal => proposal.Contribution.Id.Value)
                .ToArray();
            if (!proposals.SequenceEqual(canonical))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.proposals",
                    "Standing receipt proposals are not in canonical order.");
            }

            var contributionIds = new HashSet<(
                PrincipalId Assessing,
                PrincipalId Subject,
                StandingChangeContributionId Contribution)>();
            if (proposals.Any(proposal => !contributionIds.Add((
                    proposal.AssessingPrincipalId,
                    proposal.SubjectPrincipalId,
                    proposal.Contribution.Id))))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.proposals",
                    "A standing receipt contribution identity is duplicated.");
            }

            IGrouping<(PrincipalId Assessing, PrincipalId Subject), StandingChangeProposal>[]
                groups = proposals.GroupBy(proposal => (
                    proposal.AssessingPrincipalId,
                    proposal.SubjectPrincipalId)).ToArray();
            if (receipt.Result.Outcomes.Count != groups.Length)
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.result.outcomes",
                    "Standing receipt outcomes do not match proposal groups.");
            }

            for (int outcomeIndex = 0; outcomeIndex < groups.Length; outcomeIndex++)
            {
                IGrouping<(PrincipalId Assessing, PrincipalId Subject),
                    StandingChangeProposal> group = groups[outcomeIndex];
                StandingChangeOutcome? outcome = receipt.Result.Outcomes[outcomeIndex];
                if (!IsValidStandingOutcome(outcome, group, policy))
                {
                    return RejectedStandingReceipts(
                        $"{receiptPath}.result.outcomes[{outcomeIndex}]",
                        "The standing outcome disagrees with its proposals or policy.");
                }
            }

            var restoredResult = new StandingChangeBatchResult.Applied(
                receipt.BatchId,
                GameSnapshotCollection.Copy(receipt.Result.Outcomes));
            if (!receipts.TryAdd(
                    receipt.BatchId,
                    new CommittedStandingBatch(proposals, restoredResult)))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.batchId",
                    "The standing batch identity is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>
            .Success(receipts);
    }

    /// <summary>
    /// Verifies one saved standing outcome against its exact ordered
    /// contributions, checked sum, clamping, and policy-derived bands.
    /// </summary>
    private static bool IsValidStandingOutcome(
        StandingChangeOutcome? outcome,
        IGrouping<(PrincipalId Assessing, PrincipalId Subject), StandingChangeProposal> group,
        StandingPolicy policy)
    {
        if (outcome is null
            || outcome.AssessingPrincipalId != group.Key.Assessing
            || outcome.SubjectPrincipalId != group.Key.Subject
            || outcome.Contributions is null)
        {
            return false;
        }

        StandingChangeContribution[] contributions = group
            .Select(proposal => proposal.Contribution)
            .ToArray();
        if (!outcome.Contributions.SequenceEqual(contributions)
            || !IsWithinPolicy(outcome.PriorValue, policy.Minimum, policy.Maximum)
            || !IsWithinPolicy(outcome.ResultingValue, policy.Minimum, policy.Maximum)
            || outcome.PriorBand != policy.GetBand(outcome.PriorValue)
            || outcome.ResultingBand != policy.GetBand(outcome.ResultingValue))
        {
            return false;
        }

        try
        {
            long delta = 0;
            foreach (StandingChangeContribution contribution in contributions)
            {
                delta = checked(delta + contribution.Delta);
            }

            long unbounded = checked(outcome.PriorValue.Value + delta);
            return outcome.CombinedDelta == delta
                && outcome.ResultingValue.Value == Math.Clamp(
                    unbounded,
                    policy.Minimum.Value,
                    policy.Maximum.Value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Restores diplomacy and grant delivery receipts after proving canonical
    /// proposal order and exact correspondence with their saved outcomes.
    /// </summary>
    private static CheckpointResult<Dictionary<
        RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
        RestorePolicyReceipts(
            RelationshipCheckpoint checkpoint,
            HashSet<PrincipalId> principalIds,
            Dictionary<RelationshipGrantId, RelationshipGrantState> grants)
    {
        const string path = "$.checkpoint.relationships.policyReceipts";
        var receipts = new Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>();
        var issuedGrantIds = new HashSet<RelationshipGrantId>();
        var revokedGrantIds = new HashSet<RelationshipGrantId>();
        for (int index = 0; index < checkpoint.PolicyReceipts.Count; index++)
        {
            PolicyBatchReceiptCheckpoint? receipt = checkpoint.PolicyReceipts[index];
            string receiptPath = $"{path}[{index}]";
            if (receipt is null
                || receipt.BatchId.Value == 0
                || !Enum.IsDefined(receipt.BatchId.SourceKind)
                || receipt.Proposals is null
                || receipt.Proposals.Count == 0
                || receipt.Result is null
                || receipt.Result.BatchId != receipt.BatchId
                || receipt.Result.DiplomaticOutcomes is null
                || receipt.Result.GrantOutcomes is null)
            {
                return RejectedPolicyReceipts(
                    receiptPath,
                    "A policy receipt is missing required identity, proposals, or result.");
            }

            RelationshipPolicyChangeProposal[] proposals = receipt.Proposals
                .OfType<RelationshipPolicyChangeProposal>()
                .ToArray();
            if (proposals.Length != receipt.Proposals.Count
                || proposals.Any(proposal => !IsValidPolicyProposal(proposal, principalIds)))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.proposals",
                    "Policy receipt proposals are missing or structurally invalid.");
            }

            RelationshipPolicyChangeProposal[] canonical = proposals
                .OrderBy(ProposalPrimaryIdentity)
                .ThenBy(ProposalSecondaryIdentity)
                .ThenBy(ProposalKindOrder)
                .ThenBy(ProposalGrantIdentity)
                .ToArray();
            if (!proposals.SequenceEqual(canonical))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.proposals",
                    "Policy receipt proposals are not in canonical order.");
            }

            if (!HasUniquePolicyAssignments(proposals)
                || !PolicyOutcomesMatch(
                    proposals,
                    receipt.Result,
                    principalIds,
                    grants))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.result",
                    "Policy receipt outcomes disagree with their proposals or restored grants.");
            }

            foreach (RelationshipGrantChangeOutcome outcome in receipt.Result.GrantOutcomes)
            {
                HashSet<RelationshipGrantId> identities = outcome.ResultingIssued
                    ? issuedGrantIds
                    : revokedGrantIds;
                if (!identities.Add(outcome.Id))
                {
                    return RejectedPolicyReceipts(
                        $"{receiptPath}.result",
                        "A grant transition is committed by more than one receipt.");
                }
            }

            var restoredResult = new RelationshipPolicyChangeBatchResult.Applied(
                receipt.BatchId,
                GameSnapshotCollection.Copy(receipt.Result.DiplomaticOutcomes),
                GameSnapshotCollection.Copy(receipt.Result.GrantOutcomes));
            if (!receipts.TryAdd(
                    receipt.BatchId,
                    new CommittedRelationshipPolicyBatch(proposals, restoredResult)))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.batchId",
                    "The policy batch identity is duplicated.");
            }
        }
        foreach (RelationshipGrantState grant in grants.Values)
        {
            bool wasRevokedByReceipt = revokedGrantIds.Contains(grant.Id);
            if (grant.IsIssued == wasRevokedByReceipt)
            {
                return RejectedPolicyReceipts(
                    path,
                    "Grant state disagrees with its committed issuance and revocation receipts.");
            }
        }

        return CheckpointResult<Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
            .Success(receipts);
    }

    /// <summary>
    /// Validates closed policy proposal variants without depending on their
    /// constructors having run during external decoding.
    /// </summary>
    private static bool IsValidPolicyProposal(
        RelationshipPolicyChangeProposal proposal,
        HashSet<PrincipalId> principalIds) =>
        proposal switch
        {
            SetDiplomaticConditionProposal value =>
                value.LowerPrincipalId.Value < value.UpperPrincipalId.Value
                && principalIds.Contains(value.LowerPrincipalId)
                && principalIds.Contains(value.UpperPrincipalId)
                && Enum.IsDefined(value.Condition)
                && Enum.IsDefined(value.Reason),
            IssueRelationshipGrantProposal value =>
                value.Id.Value != 0
                && value.IssuerPrincipalId != value.HolderPrincipalId
                && principalIds.Contains(value.IssuerPrincipalId)
                && principalIds.Contains(value.HolderPrincipalId)
                && !string.IsNullOrWhiteSpace(value.Kind.Value)
                && Enum.IsDefined(value.MinimumStandingBand)
                && Enum.IsDefined(value.Reason),
            RevokeRelationshipGrantProposal value =>
                value.Id.Value != 0 && Enum.IsDefined(value.Reason),
            _ => false,
        };

    /// <summary>
    /// Enforces the same one-assignment-per-pair and per-grant invariant used
    /// by live policy batch preparation.
    /// </summary>
    private static bool HasUniquePolicyAssignments(
        IEnumerable<RelationshipPolicyChangeProposal> proposals)
    {
        var diplomaticPairs = new HashSet<(PrincipalId Lower, PrincipalId Upper)>();
        var grantIds = new HashSet<RelationshipGrantId>();
        foreach (RelationshipPolicyChangeProposal proposal in proposals)
        {
            switch (proposal)
            {
                case SetDiplomaticConditionProposal value
                    when !diplomaticPairs.Add((
                        value.LowerPrincipalId,
                        value.UpperPrincipalId)):
                case IssueRelationshipGrantProposal issue
                    when !grantIds.Add(issue.Id):
                case RevokeRelationshipGrantProposal revoke
                    when !grantIds.Add(revoke.Id):
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Matches each saved result list to its proposal subset and verifies that
    /// grant metadata remains anchored by the restored authoritative grant.
    /// </summary>
    private static bool PolicyOutcomesMatch(
        IReadOnlyList<RelationshipPolicyChangeProposal> proposals,
        RelationshipPolicyChangeBatchResult.Applied result,
        HashSet<PrincipalId> principalIds,
        Dictionary<RelationshipGrantId, RelationshipGrantState> grants)
    {
        SetDiplomaticConditionProposal[] diplomatic = proposals
            .OfType<SetDiplomaticConditionProposal>()
            .ToArray();
        RelationshipPolicyChangeProposal[] grantProposals = proposals
            .Where(proposal => proposal is IssueRelationshipGrantProposal
                or RevokeRelationshipGrantProposal)
            .ToArray();
        if (result.DiplomaticOutcomes.Count != diplomatic.Length
            || result.GrantOutcomes.Count != grantProposals.Length)
        {
            return false;
        }

        for (int index = 0; index < diplomatic.Length; index++)
        {
            SetDiplomaticConditionProposal proposal = diplomatic[index];
            DiplomaticConditionChangeOutcome? outcome = result.DiplomaticOutcomes[index];
            if (outcome is null
                || outcome.LowerPrincipalId != proposal.LowerPrincipalId
                || outcome.UpperPrincipalId != proposal.UpperPrincipalId
                || outcome.ResultingCondition != proposal.Condition
                || outcome.Reason != proposal.Reason
                || !principalIds.Contains(outcome.LowerPrincipalId)
                || !principalIds.Contains(outcome.UpperPrincipalId)
                || !Enum.IsDefined(outcome.PriorCondition)
                || !Enum.IsDefined(outcome.ResultingCondition)
                || !Enum.IsDefined(outcome.Reason))
            {
                return false;
            }
        }

        for (int index = 0; index < grantProposals.Length; index++)
        {
            RelationshipPolicyChangeProposal proposal = grantProposals[index];
            RelationshipGrantChangeOutcome? outcome = result.GrantOutcomes[index];
            if (outcome is null
                || !grants.TryGetValue(outcome.Id, out RelationshipGrantState? grant)
                || grant.IssuerPrincipalId != outcome.IssuerPrincipalId
                || grant.HolderPrincipalId != outcome.HolderPrincipalId
                || grant.Kind != outcome.Kind
                || grant.MinimumStandingBand != outcome.MinimumStandingBand
                || outcome.PriorIssued == outcome.ResultingIssued
                || !Enum.IsDefined(outcome.MinimumStandingBand)
                || !Enum.IsDefined(outcome.Reason))
            {
                return false;
            }

            bool matches = proposal switch
            {
                IssueRelationshipGrantProposal issue =>
                    outcome.Id == issue.Id
                    && outcome.IssuerPrincipalId == issue.IssuerPrincipalId
                    && outcome.HolderPrincipalId == issue.HolderPrincipalId
                    && outcome.Kind == issue.Kind
                    && outcome.MinimumStandingBand == issue.MinimumStandingBand
                    && !outcome.PriorIssued
                    && outcome.ResultingIssued
                    && outcome.Reason == issue.Reason,
                RevokeRelationshipGrantProposal revoke =>
                    outcome.Id == revoke.Id
                    && outcome.PriorIssued
                    && !outcome.ResultingIssued
                    && outcome.Reason == revoke.Reason,
                _ => false,
            };
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWithinPolicy(
        StandingValue value,
        StandingValue minimum,
        StandingValue maximum) =>
        value.Value >= minimum.Value && value.Value <= maximum.Value;

    private static CheckpointResult<T> RejectedCheckpoint<T>(
        string path,
        string message)
        where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message));

    private static CheckpointResult<RelationshipOwner> Rejected(
        string path,
        string message) =>
        RejectedCheckpoint<RelationshipOwner>(path, message);

    private static CheckpointResult<StandingPolicy> RejectedPolicy(
        string path,
        string message) =>
        RejectedCheckpoint<StandingPolicy>(path, message);

    private static CheckpointResult<IReadOnlyList<PrincipalDefinition>> RejectedPrincipals(
        string path,
        string message) =>
        RejectedCheckpoint<IReadOnlyList<PrincipalDefinition>>(path, message);

    private static CheckpointResult<Dictionary<
        (PrincipalId Assessing, PrincipalId Subject), StandingValue>> RejectedStandings(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>>(path, message);

    private static CheckpointResult<Dictionary<
        (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>> RejectedDiplomacy(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>>(path, message);

    private static CheckpointResult<Dictionary<
        RelationshipGrantId, RelationshipGrantState>> RejectedGrants(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<RelationshipGrantId, RelationshipGrantState>>(
            path,
            message);

    private static CheckpointResult<Dictionary<
        StandingChangeBatchId, CommittedStandingBatch>> RejectedStandingReceipts(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>(
            path,
            message);

    private static CheckpointResult<Dictionary<
        RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
        RejectedPolicyReceipts(
            string path,
            string message) =>
        RejectedCheckpoint<Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>(
                path,
                message);

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
