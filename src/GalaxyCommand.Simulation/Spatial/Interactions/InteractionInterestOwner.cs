namespace GalaxyCommand.Simulation;

/// <summary>Derived change notice from a domain's authoritative interest commit.</summary>
public sealed record InteractionInterestChange(InteractionInterestId Id, SpatialInteractionInterest? Interest);

/// <summary>Durable notification history correlated to directed participants in the stable delivery view.</summary>
internal sealed record InteractionDeliveryCorrelation(InteractionInterestId InterestId,
    EventGeneration Generation, SpatialRevision SourceRevision, SpatialRevision TargetRevision);

internal sealed record InteractionInterestOwnerCheckpoint(
    string DomainKind, IReadOnlyList<SpatialInteractionInterest> Interests,
    IReadOnlyList<InteractionDeliveryCorrelation>? Deliveries = null);

/// <summary>Reusable interest state composed inside a consuming gameplay domain.</summary>
public sealed class InteractionInterestOwner
{
    private readonly IdSequence<InteractionInterestId> _ids;
    private bool _evaluationReadOnly;
    private Action? _sessionMutationGuard;

    private readonly Dictionary<InteractionInterestId, SpatialInteractionInterest> _interests = [];
    private readonly Dictionary<InteractionInterestId, InteractionInterestChange> _changes = [];
    private readonly Dictionary<InteractionInterestId, InteractionDeliveryCorrelation> _deliveries = [];

    internal void SetSessionMutationGuard(Action guard) => _sessionMutationGuard = guard;
    internal void SetEvaluationReadOnly(bool value) => _evaluationReadOnly = value;

    /// <summary>Uses the session's shared identity allocator without transferring domain ownership.</summary>
    public InteractionInterestOwner(string domainKind, IdSequence<InteractionInterestId> sessionIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        ArgumentNullException.ThrowIfNull(sessionIds);
        DomainKind = domainKind;
        _ids = sessionIds;
    }

    public string DomainKind { get; }

    /// <summary>Returns ship-pair intent only; use GetInterest for either target category.</summary>
    public ShipPairInteractionInterest? Get(InteractionInterestId id) => _interests.GetValueOrDefault(id) as ShipPairInteractionInterest;

    /// <summary>Returns current intent of either target category without resolving domain eligibility.</summary>
    public SpatialInteractionInterest? GetInterest(InteractionInterestId id) => _interests.GetValueOrDefault(id);

    /// <summary>Captures both ship-pair and stationary interests in session identity order.</summary>
    public IReadOnlyList<SpatialInteractionInterest> CaptureAllSnapshot() =>
        Array.AsReadOnly(_interests.Values.OrderBy(interest => interest.Id.Value).ToArray());

    /// <summary>Registers domain-approved fixed target input, consuming an ID only after validation.</summary>
    public StationaryInteractionInterest RegisterStationary(string purpose, ShipId source,
        SystemPosition position, ulong rangeUnits, EntityId? entityId = null, SpatialRevision targetRevision = default)
    {
        EnsureMutable();
        // Validate with a nonallocated probe so rejected input cannot advance the shared allocator.
        _ = new StationaryInteractionInterest(new InteractionInterestId(1), DomainKind, purpose,
            source, position, rangeUnits, default, entityId, targetRevision);
        var interest = new StationaryInteractionInterest(_ids.Allocate(), DomainKind, purpose,
            source, position, rangeUnits, default, entityId, targetRevision);
        _interests.Add(interest.Id, interest);
        _changes[interest.Id] = new InteractionInterestChange(interest.Id, interest);
        return interest;
    }

    /// <summary>Revises target evidence under the same intent ID and invalidates prior delivery.</summary>
    public StationaryInteractionInterest UpdateStationary(InteractionInterestId id, ShipId source,
        SystemPosition position, ulong rangeUnits, EntityId? entityId = null, SpatialRevision targetRevision = default)
    {
        EnsureMutable();
        var current = (StationaryInteractionInterest)_interests[id];
        if (current.Source == source && current.Position == position && current.RangeUnits == rangeUnits
            && current.EntityId == entityId && current.Revision == targetRevision) return current;
        var interest = new StationaryInteractionInterest(id, DomainKind, current.PurposeId,
            source, position, rangeUnits, current.Generation.Next(), entityId, targetRevision);
        _interests[id] = interest;
        _deliveries.Remove(id);
        _changes[id] = new InteractionInterestChange(id, interest);
        return interest;
    }

    /// <summary>Commits a new directed interest. Invalid input consumes no session identity.</summary>
    public ShipPairInteractionInterest Register(string purpose, ShipId source, ShipId target, ulong rangeUnits)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        _ = new InteractionShipPair(source, target);
        var interest = new ShipPairInteractionInterest(
            _ids.Allocate(), DomainKind, purpose, source, target, rangeUnits, default);
        _interests.Add(interest.Id, interest);
        _changes[interest.Id] = new InteractionInterestChange(interest.Id, interest);
        return interest;
    }

    /// <summary>Revises a live interest while retaining its purpose and identity. No-op updates keep its generation.</summary>
    public ShipPairInteractionInterest Update(InteractionInterestId id, ShipId source, ShipId target, ulong rangeUnits)
    {
        EnsureMutable();
        ShipPairInteractionInterest current = (ShipPairInteractionInterest)_interests[id];
        if (current.Source == source && current.Target == target && current.RangeUnits == rangeUnits)
            return current;

        var interest = new ShipPairInteractionInterest(id, DomainKind, current.PurposeId,
            source, target, rangeUnits, current.Generation.Next());
        _interests[id] = interest;
        _deliveries.Remove(id);
        _changes[id] = new InteractionInterestChange(id, interest);
        return interest;
    }

    /// <summary>Withdraws a live interest once; its session identity remains permanently consumed.</summary>
    public bool Cancel(InteractionInterestId id)
    {
        EnsureMutable();
        if (!_interests.Remove(id))
            return false;

        _deliveries.Remove(id);
        _changes[id] = new InteractionInterestChange(id, null);
        return true;
    }

    /// <summary>Captures ship-pair interests only; CaptureAllSnapshot includes stationary targets.</summary>
    public IReadOnlyList<ShipPairInteractionInterest> CaptureSnapshot() =>
        Array.AsReadOnly(_interests.Values.OfType<ShipPairInteractionInterest>().OrderBy(interest => interest.Id.Value).ToArray());

    /// <summary>Captures the latest unacknowledged change per identity in deterministic order.</summary>
    public IReadOnlyList<InteractionInterestChange> CaptureChanges() =>
        Array.AsReadOnly(_changes.Values.OrderBy(change => change.Id.Value).ToArray());

    /// <summary>Acknowledges captured notices without discarding later commits to the same identity.</summary>
    public void AcknowledgeChanges(IEnumerable<InteractionInterestChange> changes)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(changes);
        InteractionInterestChange[] captured = changes.ToArray();
        foreach (InteractionInterestChange change in captured)
            ArgumentNullException.ThrowIfNull(change);

        foreach (InteractionInterestChange change in captured)
        {
            // The notice instance is a buffer token, not an authoritative gameplay identity.
            if (_changes.TryGetValue(change.Id, out InteractionInterestChange? current) &&
                ReferenceEquals(current, change))
                _changes.Remove(change.Id);
        }
    }

    /// <summary>Tests delivery history against the actual stable-view revisions, including applied completion boundaries.</summary>
    internal bool HasDelivered(SpatialInteractionInterest interest, InteractionSpatialReadView view) =>
        _deliveries.TryGetValue(interest.Id, out InteractionDeliveryCorrelation? delivery)
        && delivery.Generation == interest.Generation
        && view.Get(interest.Source)?.Revision == delivery.SourceRevision
        && interest.TargetRevision(view) == delivery.TargetRevision;

    /// <summary>
    /// Acknowledges successful domain evaluation after the complete effect commit.
    /// Uses the evaluation view, preserving later effect changes as fresh triggers.
    /// Canceled or revised interests cannot acquire obsolete delivery history.
    /// </summary>
    internal void RecordDelivery(SpatialInteractionInterest interest, InteractionSpatialReadView view)
    {
        EnsureMutable();
        if (GetInterest(interest.Id) != interest) return;
        InteractionSpatialParticipant source = view.Get(interest.Source)
            ?? throw new InvalidOperationException("Delivered contact is missing its stable source participant.");
        SpatialRevision targetRevision = interest.TargetRevision(view)
            ?? throw new InvalidOperationException("Delivered contact is missing its stable target participant.");
        _deliveries[interest.Id] = new(interest.Id, interest.Generation, source.Revision, targetRevision);
    }

    internal InteractionInterestOwnerCheckpoint CaptureCheckpoint() => new(DomainKind, CaptureAllSnapshot(),
        Array.AsReadOnly(_deliveries.Values.OrderBy(delivery => delivery.InterestId.Value).ToArray()));

    /// <summary>Restores domain intent against the already-restored shared allocator and seeds derived rebuild notices.</summary>
    internal static CheckpointResult<InteractionInterestOwner> RestoreCheckpoint(
        InteractionInterestOwnerCheckpoint checkpoint, IdSequence<InteractionInterestId> sessionIds)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(sessionIds);
        if (string.IsNullOrWhiteSpace(checkpoint.DomainKind) || checkpoint.Interests is null)
            return Reject("Interest owner domain and collection are required.");

        var owner = new InteractionInterestOwner(checkpoint.DomainKind, sessionIds);
        ulong? next = sessionIds.CaptureCheckpoint().NextValue;
        foreach (SpatialInteractionInterest interest in checkpoint.Interests)
        {
            if (interest is null || interest.DomainKind != checkpoint.DomainKind ||
                interest.Id.Value == 0 || (next is { } nextValue && interest.Id.Value >= nextValue) ||
                !owner._interests.TryAdd(interest.Id, interest))
                return Reject("Interests must be unique, owned by this domain, and allocated by the session.");

            owner._changes.Add(interest.Id, new InteractionInterestChange(interest.Id, interest));
        }

        foreach (InteractionDeliveryCorrelation delivery in checkpoint.Deliveries ?? [])
        {
            if (delivery is null || !owner._interests.TryGetValue(delivery.InterestId, out SpatialInteractionInterest? interest)
                || delivery.Generation != interest.Generation || !owner._deliveries.TryAdd(delivery.InterestId, delivery))
                return Reject("Delivery history must uniquely reference a live interest at its current generation.");
        }

        return CheckpointResult<InteractionInterestOwner>.Success(owner);
    }

    /// <summary>Rejects cross-owner mutation while the runtime is evaluating a stable interaction wave.</summary>
    private void EnsureMutable()
    {
        _sessionMutationGuard?.Invoke();
        if (_evaluationReadOnly) throw new InvalidOperationException("Interaction evaluation cannot mutate domain interest state.");
    }

    private static CheckpointResult<InteractionInterestOwner> Reject(string message) =>
        CheckpointResult<InteractionInterestOwner>.Rejected(
            new CheckpointValidationFailure("$.checkpoint.interactionInterests", message));
}
