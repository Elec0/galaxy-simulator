namespace GalaxyCommand.Simulation;

/// <summary>Derived participant index and shared forecast waves for domain-owned ship and fixed-target interests.</summary>
public sealed class SpatialInteractionCoordinator : IDisposable
{
    private readonly SpatialMovement _movement;
    private readonly List<InteractionInterestOwner> _owners;
    private readonly SpatialInteractionChanges _movementChanges;
    private readonly Dictionary<InteractionInterestId, InteractionInterestOwner> _identityOwners = [];
    private readonly Dictionary<InteractionInterestId, SpatialInteractionInterest> _interests = [];
    private readonly Dictionary<ShipId, HashSet<InteractionInterestId>> _participants = [];
    private readonly HashSet<InteractionInterestId> _dirty = [];
    private bool _disposed;

    /// <summary>Indexes existing domain intent and subscribes to movement commits. Duplicate session identities are rejected.</summary>
    public SpatialInteractionCoordinator(SpatialMovement movement, IEnumerable<InteractionInterestOwner> owners)
    {
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(owners);
        _movement = movement;
        _owners = owners.ToList();
        if (_owners.Any(static owner => owner is null) || _owners.Distinct().Count() != _owners.Count)
            throw new ArgumentException("Interest owners must be distinct and nonnull.", nameof(owners));
        foreach (InteractionInterestOwner owner in _owners)
        {
            foreach (SpatialInteractionInterest interest in owner.CaptureAllSnapshot())
            {
                ApplyChange(owner, new InteractionInterestChange(interest.Id, interest));
                _dirty.Add(interest.Id);
            }
        }
        _movementChanges = movement.AttachInteractionChanges();
    }

    /// <summary>Adds a newly registered domain without discarding existing pending correlations.</summary>
    internal void RegisterOwner(InteractionInterestOwner owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(owner);
        if (_owners.Contains(owner)) return;
        IReadOnlyList<SpatialInteractionInterest> interests = owner.CaptureAllSnapshot();
        if (interests.Any(interest => _identityOwners.ContainsKey(interest.Id)))
            throw new InvalidOperationException("New domain interests reuse a registered session identity.");
        _owners.Add(owner);
        foreach (SpatialInteractionInterest interest in interests)
        {
            ApplyChange(owner, new InteractionInterestChange(interest.Id, interest));
            _dirty.Add(interest.Id);
        }
    }

    public InteractionForecastScheduler Scheduler { get; } = new();

    /// <summary>Buffers due results in canonical order against one immutable post-completion view.</summary>
    public InteractionDueWave EvaluateDueWave(IEnumerable<ScheduledEvent<InteractionForecast>> events, SimulationTime now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(events);
        ScheduledEvent<InteractionForecast>[] inputs = events.ToArray();
        if (inputs.Any(scheduled => scheduled is null || scheduled.Payload is null
            || scheduled.Key.Timestamp != now || scheduled.Key.Phase != EventPhase.StateUpdate)
            || inputs.Select(scheduled => scheduled.Key).Distinct().Count() != inputs.Length)
            throw new ArgumentException("Due events must be distinct StateUpdate envelopes at the wave timestamp.", nameof(events));
        InteractionSpatialReadView view = _movement.CaptureInteractionReadView(now);
        var interests = inputs.Select(scheduled => scheduled.Payload.Candidate.Interest.Id).Distinct()
            .ToDictionary(id => id, id => _identityOwners.GetValueOrDefault(id)?.GetInterest(id));
        InteractionDueResult[] results = inputs.OrderBy(scheduled => scheduled.Payload.Candidate.Key)
            .ThenBy(scheduled => scheduled.Key)
            .Select(scheduled => new InteractionDueResult(scheduled,
                ValidateDueDelivery(scheduled, interests[scheduled.Payload.Candidate.Interest.Id], view))).ToArray();
        return new InteractionDueWave(this, view, Array.AsReadOnly(results));
    }

    /// <summary>Suppresses a contact already delivered for this stable state without changing pending correlations.</summary>
    private ScheduledEventDisposition ValidateDueDelivery(ScheduledEvent<InteractionForecast> scheduled,
        SpatialInteractionInterest? interest, InteractionSpatialReadView view)
    {
        ScheduledEventDisposition disposition = Scheduler.ValidateDue(scheduled, interest, view);
        return disposition == ScheduledEventDisposition.Applied && interest is not null
            && _identityOwners[interest.Id].HasDelivered(interest, view)
                ? ScheduledEventDisposition.IgnoredStateMismatch : disposition;
    }

    /// <summary>Consumes correlations only after the caller has handled the complete buffered wave.</summary>
    public void CompleteDueWave(InteractionDueWave wave)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(wave);
        if (!ReferenceEquals(wave.Owner, this))
            throw new ArgumentException("Due wave belongs to a different coordinator.", nameof(wave));
        foreach (InteractionDueResult result in wave.Results) Scheduler.Complete(result.Event);
    }

    /// <summary>
    /// Evaluates affected interests from one stable view through the next known
    /// movement boundary, or an instant for stationary pairs. The caller owns the
    /// stable commit boundary. Failed evaluation or agenda commit retains triggers.
    /// </summary>
    public IReadOnlyList<EventKey> Reevaluate<TEvent>(EventAgenda<TEvent> agenda, SimulationTime now,
        Func<InteractionForecast, TEvent> wrapEvent) => ReevaluateCore(agenda, now, wrapEvent, null);

    /// <summary>
    /// Opts one reevaluation wave into explicitly tuned pure forecast batches. Captures owner
    /// eligibility and per-interest horizons serially, then evaluates only immutable inputs on
    /// workers. Wrapping, agenda commit, and acknowledgement remain on the caller thread.
    /// Null tuning is rejected. Failed evaluation or submission retains triggers for retry.
    /// The caller must maintain the stable owner boundary throughout the synchronous call.
    /// </summary>
    public IReadOnlyList<EventKey> Reevaluate<TEvent>(EventAgenda<TEvent> agenda, SimulationTime now,
        Func<InteractionForecast, TEvent> wrapEvent, InteractionBatchExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return ReevaluateCore(agenda, now, wrapEvent, execution);
    }

    /// <summary>Shares serial capture/commit/acknowledgement, changing only pure evaluation when a caller opts in.</summary>
    private IReadOnlyList<EventKey> ReevaluateCore<TEvent>(EventAgenda<TEvent> agenda, SimulationTime now,
        Func<InteractionForecast, TEvent> wrapEvent, InteractionBatchExecution? execution)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        var captures = _owners.Select(owner => (Owner: owner, Changes: owner.CaptureChanges())).ToArray();
        foreach (var capture in captures)
        {
            foreach (InteractionInterestChange change in capture.Changes)
            {
                ApplyChange(capture.Owner, change);
                _dirty.Add(change.Id);
            }
        }

        IReadOnlyList<SpatialInteractionChange> movements = _movementChanges.Capture();
        foreach (SpatialInteractionChange change in movements)
        {
            if (_participants.TryGetValue(change.ShipId, out HashSet<InteractionInterestId>? affected))
                _dirty.UnionWith(affected);
        }

        var forecasts = new List<InteractionForecast>();
        if (_dirty.Count != 0)
        {
            InteractionSpatialReadView view = _movement.CaptureInteractionReadView(now);
            if (execution is null)
            {
                foreach (SpatialInteractionInterest interest in EligibleInterests(view))
                    if (InteractionForecast.Evaluate(interest, view, Boundary(interest, view)) is { } forecast)
                        forecasts.Add(forecast);
            }
            else
            {
                // Owner dictionaries and delivery history are read before dispatch. Each pair keeps
                // its own horizon, including stationary instant checks, rather than a wave-wide maximum.
                var inputs = EligibleInterests(view).Select(interest => (Interest: interest, Through: Boundary(interest, view))).ToArray();
                forecasts.AddRange(execution.Evaluate(inputs.Length,
                    index => InteractionForecast.Evaluate(inputs[index].Interest, view, inputs[index].Through))
                    .OfType<InteractionForecast>());
            }
        }

        IReadOnlyList<EventKey> keys = Scheduler.Commit(agenda, forecasts, wrapEvent);
        foreach (var capture in captures)
            capture.Owner.AcknowledgeChanges(capture.Changes);
        _movementChanges.Acknowledge(movements);
        _dirty.Clear();
        return keys;
    }

    /// <summary>Reads current intent and delivery history only on the coordinator thread in stable interest order.</summary>
    private IEnumerable<SpatialInteractionInterest> EligibleInterests(InteractionSpatialReadView view)
    {
        foreach (InteractionInterestId id in _dirty.OrderBy(id => id.Value))
            if (_interests.TryGetValue(id, out SpatialInteractionInterest? interest)
                && !_identityOwners[id].HasDelivered(interest, view)) yield return interest;
    }

    /// <summary>Detaches movement notifications and prevents later evaluation.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _movementChanges.Dispose();
        _disposed = true;
    }

    /// <summary>Updates only derived participant membership while enforcing session identity ownership.</summary>
    private void ApplyChange(InteractionInterestOwner owner, InteractionInterestChange change)
    {
        if (_identityOwners.TryGetValue(change.Id, out InteractionInterestOwner? existing) && existing != owner)
            throw new InvalidOperationException("Interaction identity is shared by distinct domain owners.");
        _identityOwners[change.Id] = owner;
        if (_interests.Remove(change.Id, out SpatialInteractionInterest? previous))
        {
            foreach (ShipId ship in previous.Ships) RemoveParticipant(ship, change.Id);
        }
        if (change.Interest is not { } interest)
        {
            _identityOwners.Remove(change.Id);
            return;
        }
        _interests.Add(change.Id, interest);
        foreach (ShipId ship in interest.Ships) AddParticipant(ship, change.Id);
    }

    /// <summary>Creates membership only for ships with live explicit interests.</summary>
    private void AddParticipant(ShipId shipId, InteractionInterestId id)
    {
        if (!_participants.TryGetValue(shipId, out HashSet<InteractionInterestId>? ids))
            _participants.Add(shipId, ids = []);
        ids.Add(id);
    }

    /// <summary>Removes empty membership buckets after withdrawal or role changes.</summary>
    private void RemoveParticipant(ShipId shipId, InteractionInterestId id)
    {
        HashSet<InteractionInterestId> ids = _participants[shipId];
        ids.Remove(id);
        if (ids.Count == 0) _participants.Remove(shipId);
    }

    /// <summary>Uses the owner-selected next motion boundary, avoiding an unrenewed short query horizon.</summary>
    private static SimulationTime Boundary(SpatialInteractionInterest interest, InteractionSpatialReadView view)
    {
        SimulationTime? first = NextBoundary(view.Get(interest.Source));
        SimulationTime? second = interest is ShipPairInteractionInterest pair ? NextBoundary(view.Get(pair.Target)) : null;
        return first is { } a && second is { } b ? (a < b ? a : b) : first ?? second ?? view.Timestamp;
    }
    private static SimulationTime? NextBoundary(InteractionSpatialParticipant? participant) =>
        participant?.Spatial.Motion?.ArrivesAt ?? participant?.Spatial.Maneuver?.NextBoundary?.Timestamp
        ?? (participant?.Spatial.State as ShipSpatialSnapshotState.InteractionControlled)?.Interval.EndsAt;

}
