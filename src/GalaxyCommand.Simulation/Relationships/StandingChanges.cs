using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

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
