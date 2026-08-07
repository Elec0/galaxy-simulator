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
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentOutOfRangeException.ThrowIfZero(playerPrincipalId.Value);
        ArgumentNullException.ThrowIfNull(standingPolicy);
        ArgumentNullException.ThrowIfNull(standings);

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

        Principals = new ReadOnlyCollection<PrincipalDefinition>(principalValues);
        PlayerPrincipalId = playerPrincipalId;
        StandingPolicy = standingPolicy;
        Standings = new ReadOnlyCollection<InitialStandingSetup>(standingValues);
    }

    public IReadOnlyList<PrincipalDefinition> Principals { get; }

    public PrincipalId PlayerPrincipalId { get; }

    public StandingPolicy StandingPolicy { get; }

    public IReadOnlyList<InitialStandingSetup> Standings { get; }
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
/// Complete authoritative relationship diagnostics at one commit boundary.
/// </summary>
public sealed record RelationshipSnapshot(
    PrincipalId PlayerPrincipalId,
    StandingPolicyId StandingPolicyId,
    IReadOnlyList<PrincipalSnapshot> Principals,
    IReadOnlyList<StandingSnapshot> Standings);

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
    private readonly Dictionary<StandingChangeBatchId, CommittedStandingBatch>
        _committedStandingBatches = [];

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
    /// Resolves the complete directional matrix in stable principal order.
    /// </summary>
    internal RelationshipSnapshot CaptureSnapshot()
    {
        var standings = new List<StandingSnapshot>(
            checked(_principals.Count * Math.Max(0, _principals.Count - 1)));
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
            GameSnapshotCollection.Copy(standings));
    }

    private StandingValue GetStanding(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId) =>
        _standingOverrides.GetValueOrDefault(
            (assessingPrincipalId, subjectPrincipalId),
            _standingPolicy.Initial);

    private static StandingChangePreparation.Resolved ResolvedRejection(
        StandingChangeBatchId batchId,
        StandingChangeRejectionReason reason) =>
        new(new StandingChangeBatchResult.Rejected(batchId, reason));
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
