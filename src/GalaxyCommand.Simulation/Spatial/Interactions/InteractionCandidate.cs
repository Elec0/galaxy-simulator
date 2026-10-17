namespace GalaxyCommand.Simulation;

/// <summary>Accepted deterministic ordering and deduplication key for discovery.</summary>
public readonly record struct InteractionCandidateKey : IComparable<InteractionCandidateKey>
{
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
        Pair = pair;
        InterestId = interestId;
    }

    public SystemId SystemId { get; }
    public SimulationTime Timestamp { get; }
    public InteractionShipPair Pair { get; }
    public InteractionInterestId InterestId { get; }

    /// <summary>
    /// Orders by system, timestamp, canonical ship pair, then interest identity.
    /// No partition, worker, or discovery-order data participates.
    /// </summary>
    public int CompareTo(InteractionCandidateKey other)
    {
        int result = SystemId.Value.CompareTo(other.SystemId.Value);
        if (result != 0) return result;
        result = Timestamp.CompareTo(other.Timestamp);
        if (result != 0) return result;
        result = Pair.CompareTo(other.Pair);
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
        SystemId systemId, ShipPairInteractionInterest interest, SweptRangeContact contact)
    {
        ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
        ArgumentNullException.ThrowIfNull(interest);
        ArgumentNullException.ThrowIfNull(contact);

        SystemId = systemId;
        Interest = interest;
        Contact = contact;
        Key = new InteractionCandidateKey(systemId, contact.Timestamp,
            new InteractionShipPair(interest.Source, interest.Target), interest.Id);
    }

    public SystemId SystemId { get; }
    public ShipPairInteractionInterest Interest { get; }
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
