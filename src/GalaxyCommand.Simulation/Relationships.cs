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
    private readonly IReadOnlyDictionary<(PrincipalId Assessing, PrincipalId Subject), StandingValue>
        _standingOverrides;
    private readonly StandingPolicy _standingPolicy;

    /// <summary>
    /// Copies canonical setup state into the authoritative runtime owner.
    /// </summary>
    internal RelationshipOwner(RelationshipSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _principals = setup.Principals;
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

                StandingValue value = _standingOverrides.GetValueOrDefault(
                    (assessing.Id, subject.Id),
                    _standingPolicy.Initial);
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
}
