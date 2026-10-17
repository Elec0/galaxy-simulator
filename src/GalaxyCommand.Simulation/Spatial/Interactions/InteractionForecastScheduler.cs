namespace GalaxyCommand.Simulation;

/// <summary>Owner of derived pending forecast correlations and deterministic agenda submission.</summary>
public sealed class InteractionForecastScheduler
{
    private readonly Dictionary<InteractionCandidateKey, InteractionForecast> _pending = [];
    private readonly Dictionary<InteractionCandidateKey, EventKey> _pendingKeys = [];

    public int PendingCount => _pending.Count;

    /// <summary>Rebuilds pending correlations from exact restored agenda envelopes without allocating sequences.</summary>
    internal void RestorePending(IEnumerable<ScheduledEvent<InteractionForecast>> events)
    {
        foreach (ScheduledEvent<InteractionForecast> scheduled in events.OrderBy(item => item.Key.CreationSequence))
        {
            InteractionForecast forecast = scheduled.Payload;
            if (scheduled.Key.Phase != EventPhase.StateUpdate || scheduled.Key.Timestamp != forecast.Candidate.Key.Timestamp
                || scheduled.Generation != forecast.Candidate.Interest.Generation)
                throw new InvalidOperationException("Restored interaction envelope disagrees with its contact evidence.");
            _pending[forecast.Candidate.Key] = forecast;
            _pendingKeys[forecast.Candidate.Key] = scheduled.Key;
        }
    }

    /// <summary>Validates due work against this owner's latest pending correlation.</summary>
    public ScheduledEventDisposition ValidateDue(InteractionForecast forecast,
        ShipPairInteractionInterest? currentInterest, InteractionSpatialReadView view,
        EventGeneration scheduledGeneration)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentNullException.ThrowIfNull(view);
        if (!_pending.TryGetValue(forecast.Candidate.Key, out InteractionForecast? current)
            || current != forecast)
            return ScheduledEventDisposition.IgnoredStaleGeneration;
        return forecast.Validate(currentInterest, view, scheduledGeneration);
    }

    /// <summary>Validates the exact committed agenda envelope before accepting contact evidence.</summary>
    public ScheduledEventDisposition ValidateDue(ScheduledEvent<InteractionForecast> scheduled,
        ShipPairInteractionInterest? currentInterest, InteractionSpatialReadView view)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(view);
        InteractionForecast forecast = scheduled.Payload;
        if (!_pending.TryGetValue(forecast.Candidate.Key, out InteractionForecast? current)
            || current != forecast)
            return ScheduledEventDisposition.IgnoredStaleGeneration;
        if (!_pendingKeys.TryGetValue(forecast.Candidate.Key, out EventKey key) || key != scheduled.Key)
            return ScheduledEventDisposition.IgnoredStateMismatch;
        return forecast.Validate(currentInterest, view, scheduled.Generation);
    }

    /// <summary>
    /// Sorts and deduplicates one stable forecast wave and suppresses identical
    /// already-pending triggers. Conflicting duplicates are rejected. The agenda
    /// owner commits StateUpdate events before this owner records pending state.
    /// </summary>
    public IReadOnlyList<EventKey> Commit<TEvent>(EventAgenda<TEvent> agenda,
        IEnumerable<InteractionForecast> forecasts, Func<InteractionForecast, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(forecasts);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        InteractionForecast[] inputs = forecasts.ToArray();
        if (inputs.Any(static forecast => forecast is null))
            throw new ArgumentException("Forecast input cannot contain null elements.", nameof(forecasts));
        var unique = new SortedDictionary<InteractionCandidateKey, InteractionForecast>();
        foreach (InteractionForecast forecast in inputs)
        {
            InteractionCandidateKey key = forecast.Candidate.Key;
            if (unique.TryGetValue(key, out InteractionForecast? existing) && existing != forecast)
                throw new InvalidOperationException($"Conflicting forecasts for candidate key {key}.");
            unique[key] = forecast;
        }

        InteractionForecast[] accepted = unique.Values.Where(forecast =>
            !_pending.TryGetValue(forecast.Candidate.Key, out InteractionForecast? pending)
            || !SameCorrelation(pending, forecast)).ToArray();
        var proposals = new List<AgendaEventProposal<TEvent>>(accepted.Length);
        for (int index = 0; index < accepted.Length; index++)
        {
            InteractionForecast forecast = accepted[index];
            // Rank is assigned only after the complete wave is canonically
            // reduced. It is a transient adapter to the shared agenda order,
            // never a worker-assigned identity or a forecast's durable key.
            proposals.Add(new AgendaEventProposal<TEvent>(new AgendaProposalOrder(
                RuntimeEvaluationWave.SpatialInteractions, forecast.Candidate.SystemId.Value,
                (ulong)index, EffectKind: 0, LocalOrdinal: 0), forecast.Candidate.Key.Timestamp,
                EventPhase.StateUpdate, forecast.Candidate.Interest.Generation, wrapEvent(forecast)));
        }

        AgendaCommitResult committed = AgendaCommitOwner.Commit(agenda, proposals);
        for (int index = 0; index < accepted.Length; index++)
        {
            InteractionForecast forecast = accepted[index];
            _pending[forecast.Candidate.Key] = forecast;
            _pendingKeys[forecast.Candidate.Key] = committed.EventKeys[index];
        }
        return committed.EventKeys;
    }

    /// <summary>Releases a popped forecast's derived pending correlation.</summary>
    public void Complete(InteractionForecast forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        if (_pending.TryGetValue(forecast.Candidate.Key, out InteractionForecast? current) && current == forecast)
        {
            _pending.Remove(forecast.Candidate.Key);
            _pendingKeys.Remove(forecast.Candidate.Key);
        }
    }

    /// <summary>Releases only the matching committed envelope, preserving replacement forecasts.</summary>
    public void Complete(ScheduledEvent<InteractionForecast> scheduled)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        if (_pendingKeys.TryGetValue(scheduled.Payload.Candidate.Key, out EventKey key)
            && key == scheduled.Key && scheduled.Generation == scheduled.Payload.Candidate.Interest.Generation)
            Complete(scheduled.Payload);
    }

    /// <summary>
    /// Ignores shifted query horizons and sampled positions when the same
    /// unchanged trajectories and interest still forecast the same crossing.
    /// </summary>
    private static bool SameCorrelation(InteractionForecast first, InteractionForecast second) =>
        first.Candidate.Key == second.Candidate.Key && first.Candidate.Interest == second.Candidate.Interest
        && first.Source.Revision == second.Source.Revision && first.Target.Revision == second.Target.Revision;
}
