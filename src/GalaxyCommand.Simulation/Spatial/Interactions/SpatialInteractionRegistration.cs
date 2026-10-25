namespace GalaxyCommand.Simulation;

internal sealed record SpatialInteractionRegistrationCheckpoint(
    IdSequenceCheckpoint Ids, IReadOnlyList<InteractionInterestOwnerCheckpoint> Domains,
    IReadOnlyList<string>? EvaluationDomains = null);

/// <summary>Session registration boundary retaining domain stores and one shared identity allocator.</summary>
internal sealed class SpatialInteractionRegistration
{
    private readonly IdSequence<InteractionInterestId> _ids;
    private Action? _sessionMutationGuard;
    private readonly SortedSet<string> _requiredEvaluators = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISpatialInteractionDomainEvaluator> _evaluators = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, InteractionInterestOwner> _domains = new(StringComparer.Ordinal);

    internal SpatialInteractionRegistration() : this(new IdSequence<InteractionInterestId>()) { }

    private SpatialInteractionRegistration(IdSequence<InteractionInterestId> ids) => _ids = ids;

    /// <summary>Registers a distinct domain store without allocating an interest or selecting gameplay policy.</summary>
    internal InteractionInterestOwner Register(string domainKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        if (_domains.ContainsKey(domainKind))
            throw new InvalidOperationException($"Spatial interaction domain '{domainKind}' is already registered.");
        var owner = new InteractionInterestOwner(domainKind, _ids);
        if (_sessionMutationGuard is not null) owner.SetSessionMutationGuard(_sessionMutationGuard);
        _domains.Add(domainKind, owner);
        return owner;
    }

    /// <summary>Finds the live domain store, including the restored instance a consumer must rebind to.</summary>
    internal InteractionInterestOwner? Get(string domainKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        return _domains.GetValueOrDefault(domainKind);
    }

    /// <summary>Enforces session health and evaluation barriers even through previously returned domain stores.</summary>
    internal void AttachMutationGuard(Action guard)
    {
        _sessionMutationGuard = guard;
        foreach (InteractionInterestOwner owner in _domains.Values) owner.SetSessionMutationGuard(guard);
    }

    /// <summary>Binds runtime behavior to an existing domain; evaluator objects are not checkpoint state.</summary>
    internal void Bind(string domainKind, ISpatialInteractionDomainEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        if (Get(domainKind) is null) throw new InvalidOperationException("Register the spatial interaction domain before binding evaluation.");
        _evaluators[domainKind] = evaluator;
        _requiredEvaluators.Add(domainKind);
    }

    /// <summary>Requires restored behavior to be rebound before any events can be consumed.</summary>
    internal void EnsureEvaluationBindings()
    {
        foreach (string domain in _requiredEvaluators)
            if (!_evaluators.ContainsKey(domain))
                throw new InvalidOperationException($"Rebind spatial interaction evaluator '{domain}' before advancing the restored session.");
    }

    internal ISpatialInteractionDomainEvaluator? Evaluator(string domainKind) => _evaluators.GetValueOrDefault(domainKind);
    internal IReadOnlyList<InteractionInterestOwner> CaptureOwners() => Array.AsReadOnly(_domains.Values.ToArray());

    internal SpatialInteractionRegistrationCheckpoint CaptureCheckpoint() => new(
        _ids.CaptureCheckpoint(), Array.AsReadOnly(_domains.Values.Select(owner => owner.CaptureCheckpoint()).ToArray()),
        Array.AsReadOnly(_requiredEvaluators.ToArray()));

    /// <summary>Rejects delivery history beyond any restored live participant's authoritative revision.</summary>
    internal CheckpointValidationFailure? ValidateDeliveryHistory(InteractionSpatialReadView view)
    {
        foreach (InteractionInterestOwner owner in _domains.Values)
        {
            foreach (InteractionDeliveryCorrelation delivery in owner.CaptureCheckpoint().Deliveries ?? [])
            {
                SpatialInteractionInterest interest = owner.GetInterest(delivery.InterestId)!;
                if ((view.Get(interest.Source) is { } source && delivery.SourceRevision.Value > source.Revision.Value)
                    || (interest.TargetRevision(view) is { } target && delivery.TargetRevision.Value > target.Value))
                    return new CheckpointValidationFailure("$.checkpoint.spatialInteractions.deliveries",
                        "Delivery history cannot reference a future spatial revision.");
            }
        }
        return null;
    }

    /// <summary>Restores the allocator once and validates every domain before publishing any registration.</summary>
    internal static CheckpointResult<SpatialInteractionRegistration> RestoreCheckpoint(
        SpatialInteractionRegistrationCheckpoint? checkpoint)
    {
        if (checkpoint is null || checkpoint.Ids is null || checkpoint.Domains is null)
            return Reject("Spatial interaction registration, allocator, and domain collection are required.");
        CheckpointResult<IdSequence<InteractionInterestId>> ids = IdSequence<InteractionInterestId>.RestoreCheckpoint(checkpoint.Ids);
        if (!ids.IsSuccess) return Reject(ids.Failure!.Message);
        var registration = new SpatialInteractionRegistration(ids.Value!);
        var seen = new HashSet<InteractionInterestId>();
        foreach (InteractionInterestOwnerCheckpoint domain in checkpoint.Domains)
        {
            if (domain is null || string.IsNullOrWhiteSpace(domain.DomainKind)
                || registration._domains.ContainsKey(domain.DomainKind))
                return Reject("Registered domains must be nonnull and have distinct stable names.");
            CheckpointResult<InteractionInterestOwner> owner = InteractionInterestOwner.RestoreCheckpoint(domain, ids.Value!);
            if (!owner.IsSuccess) return Reject(owner.Failure!.Message);
            if (owner.Value!.CaptureAllSnapshot().Any(interest => !seen.Add(interest.Id)))
                return Reject("Interest identities must be unique across all registered domains.");
            registration._domains.Add(domain.DomainKind, owner.Value!);
        }
        foreach (string domain in checkpoint.EvaluationDomains ?? [])
        {
            if (domain is null || !registration._domains.ContainsKey(domain) || !registration._requiredEvaluators.Add(domain))
                return Reject("Evaluation bindings must reference distinct registered domains.");
        }
        return CheckpointResult<SpatialInteractionRegistration>.Success(registration);
    }

    private static CheckpointResult<SpatialInteractionRegistration> Reject(string message) =>
        CheckpointResult<SpatialInteractionRegistration>.Rejected(
            new CheckpointValidationFailure("$.checkpoint.spatialInteractions", message));
}
