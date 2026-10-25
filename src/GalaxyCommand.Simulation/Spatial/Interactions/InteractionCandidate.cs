namespace GalaxyCommand.Simulation;

/// <summary>Accepted deterministic ordering and deduplication key for discovery.</summary>
public readonly record struct InteractionCandidateKey : IComparable<InteractionCandidateKey>
{
    private readonly InteractionShipPair? _pair;
    /// <summary>Creates a complete discovery key, rejecting default identities and pairs.</summary>
    public InteractionCandidateKey(
        SystemId systemId, SimulationTime timestamp,
        InteractionShipPair pair, InteractionInterestId interestId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(interestId.Value);
        _ = new InteractionShipPair(pair.Lower, pair.Higher);

        SystemId = systemId;
        Timestamp = timestamp;
        _pair = pair;
        InterestId = interestId;
    }

    public SystemId SystemId { get; }
    public SimulationTime Timestamp { get; }
    /// <summary>Gets ship-pair identity; stationary-target keys have no ship pair.</summary>
    public InteractionShipPair Pair => _pair
        ?? throw new InvalidOperationException("A stationary target has no ship-pair identity.");
    public ShipId? StationarySource { get; }
    public EntityId? StationaryEntity { get; }
    public InteractionInterestId InterestId { get; }

    /// <summary>Formats both target categories without invoking the ship-only compatibility accessor.</summary>
    public override string ToString() =>
        $"InteractionCandidateKey {{ SystemId = {SystemId}, Timestamp = {Timestamp}, Pair = {_pair}, StationarySource = {StationarySource}, StationaryEntity = {StationaryEntity}, InterestId = {InterestId} }}";

    /// <summary>Creates a fixed-target key without borrowing a ship identity for the target.</summary>
    public InteractionCandidateKey(SystemId systemId, SimulationTime timestamp,
        ShipId source, EntityId? entityId, InteractionInterestId interestId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        ArgumentOutOfRangeException.ThrowIfZero(interestId.Value);
        if (entityId is { Value: 0 }) throw new ArgumentOutOfRangeException(nameof(entityId));
        SystemId = systemId;
        Timestamp = timestamp;
        StationarySource = source;
        StationaryEntity = entityId;
        InterestId = interestId;
    }

    /// <summary>
    /// Orders by system, timestamp and target category, then canonical ship pair
    /// or stationary source/entity, followed by interest identity. Existing ship-pair
    /// ordering is preserved. No worker or discovery-order data participates.
    /// </summary>
    public int CompareTo(InteractionCandidateKey other)
    {
        int result = SystemId.Value.CompareTo(other.SystemId.Value);
        if (result != 0) return result;
        result = Timestamp.CompareTo(other.Timestamp);
        if (result != 0) return result;
        result = StationarySource.HasValue.CompareTo(other.StationarySource.HasValue);
        if (result != 0) return result;
        if (_pair is { } pair && other._pair is { } otherPair)
            result = pair.CompareTo(otherPair);
        else
        {
            result = Nullable.Compare(StationarySource?.Value, other.StationarySource?.Value);
            if (result == 0) result = Nullable.Compare(StationaryEntity?.Value, other.StationaryEntity?.Value);
        }
        return result != 0 ? result : InterestId.Value.CompareTo(other.InterestId.Value);
    }

    public static bool operator <(InteractionCandidateKey left, InteractionCandidateKey right) =>
        left.CompareTo(right) < 0;
    public static bool operator <=(InteractionCandidateKey left, InteractionCandidateKey right) =>
        left.CompareTo(right) <= 0;
    public static bool operator >(InteractionCandidateKey left, InteractionCandidateKey right) =>
        left.CompareTo(right) > 0;
    public static bool operator >=(InteractionCandidateKey left, InteractionCandidateKey right) =>
        left.CompareTo(right) >= 0;
}

/// <summary>Immutable derived evidence carrying the interest's original directed roles.</summary>
public sealed record InteractionCandidate
{
    /// <summary>
    /// Captures contact evidence evaluated from a stable spatial view without
    /// changing the domain interest, participant roles, or authoritative motion.
    /// </summary>
    public InteractionCandidate(
        SystemId systemId, SpatialInteractionInterest interest, SweptRangeContact contact)
    {
        ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
        ArgumentNullException.ThrowIfNull(interest);
        ArgumentNullException.ThrowIfNull(contact);

        SystemId = systemId;
        Interest = interest;
        Contact = contact;
        Key = interest switch
        {
            ShipPairInteractionInterest pair => new InteractionCandidateKey(systemId, contact.Timestamp,
                new InteractionShipPair(pair.Source, pair.Target), pair.Id),
            StationaryInteractionInterest stationary => new InteractionCandidateKey(systemId, contact.Timestamp,
                stationary.Source, stationary.EntityId, stationary.Id),
            _ => throw new ArgumentException("Unsupported interaction target category.", nameof(interest)),
        };
    }

    public SystemId SystemId { get; }
    public SpatialInteractionInterest Interest { get; }
    public SweptRangeContact Contact { get; }
    public InteractionCandidateKey Key { get; }
}

/// <summary>Deterministic merge boundary for independently discovered candidates.</summary>
public static class InteractionCandidateNormalizer
{
    /// <summary>
    /// Copies, sorts, and deduplicates complete candidate evidence. Conflicting
    /// evidence under one key is rejected rather than resolved by input order.
    /// The returned collection is isolated and read-only; nothing is committed.
    /// </summary>
    public static IReadOnlyList<InteractionCandidate> Normalize(
        IEnumerable<InteractionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        InteractionCandidate[] ordered = candidates.ToArray();
        foreach (InteractionCandidate candidate in ordered)
        {
            if (candidate is null)
            {
                throw new ArgumentException("Candidate input cannot contain null elements.", nameof(candidates));
            }
        }

        Array.Sort(ordered, static (left, right) => left.Key.CompareTo(right.Key));
        var normalized = new List<InteractionCandidate>(ordered.Length);
        foreach (InteractionCandidate candidate in ordered)
        {
            if (normalized.Count > 0 && normalized[^1].Key == candidate.Key)
            {
                // A stable evaluation wave must agree on duplicate evidence.
                // Choosing either unequal payload would make batch order policy.
                if (normalized[^1] != candidate)
                {
                    throw new InvalidOperationException(
                        $"Conflicting interaction evidence for candidate key {candidate.Key}.");
                }

                continue;
            }

            normalized.Add(candidate);
        }

        return normalized.AsReadOnly();
    }
}
