namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private SpatialInteractionRegistration? _interactionRegistration;
    private SpatialInteractionCoordinator? _interactions;
    private GameSession? _interactionSession;
    private bool _disposed;
    private bool _evaluatingInteractions;

    /// <summary>Detaches session-owned movement notices and closes the runtime mutation boundary.</summary>
    public void Dispose()
    {
        if (_evaluatingInteractions) throw new InvalidOperationException("Interaction evaluation cannot dispose the session.");
        _interactions?.Dispose();
        _disposed = true;
    }

    internal InteractionSpatialReadView CaptureInteractionReadView() => _movement.CaptureInteractionReadView(CurrentTime);

    /// <summary>Rebinds managed action interests and saved forecasts before the session can advance; restore schedules no extra work.</summary>
    internal void AttachSpatialInteractions(SpatialInteractionRegistration registration, GameSession session)
    {
        _interactionRegistration = registration;
        _interactionSession = session;
        AttachSensorActions(registration);
        _interactions = new SpatialInteractionCoordinator(_movement, registration.CaptureOwners());
        IReadOnlyList<ScheduledEvent<GameEvent>> pending = _agenda.CaptureCheckpoint().Value!.PendingEvents;
        _interactions.Scheduler.RestorePending(pending.Where(item => item.Payload is GameEvent.SpatialInteraction)
            .Select(item => new ScheduledEvent<InteractionForecast>(item.Key, item.Generation,
                ((GameEvent.SpatialInteraction)item.Payload).Forecast)));
    }

    /// <summary>
    /// Captures registered owners and submits affected forecasts with the session's fixed tuning.
    /// Only pure evaluation is batched; eligibility, wrapping, commit, and acknowledgements remain serial.
    /// </summary>
    public void PrepareStateUpdate(SimulationTime now, EventAgenda<GameEvent> agenda)
    {
        FlushSensorReevaluations();
        if (_interactions is null || _interactionRegistration is null) return;
        foreach (InteractionInterestOwner owner in _interactionRegistration.CaptureOwners()) _interactions.RegisterOwner(owner);
        if (_interactionSession?.InteractionExecution is { } execution)
            _interactions.Reevaluate(agenda, now, static forecast => new GameEvent.SpatialInteraction(forecast), execution);
        else _interactions.Reevaluate(agenda, now, static forecast => new GameEvent.SpatialInteraction(forecast));
    }

    /// <summary>Evaluates every domain against one stable view, validates all effects, then commits in canonical contact order.</summary>
    public IReadOnlyList<ScheduledEventDisposition> HandleStateUpdateWave(
        IReadOnlyList<ScheduledEvent<GameEvent>> events, SimulationTime now, EventAgenda<GameEvent> agenda)
    {
        if (_interactions is null || _interactionRegistration is null || _interactionSession is null)
            throw new InvalidOperationException("Session interaction ownership is not attached.");
        if (events.Any(item => item.Payload is not GameEvent.SpatialInteraction and not GameEvent.SensorActionReevaluation))
            throw new InvalidOperationException("Session StateUpdate envelopes require an interaction wave owner.");
        ScheduledEvent<InteractionForecast>[] due = events.Where(item => item.Payload is GameEvent.SpatialInteraction)
            .Select(item => new ScheduledEvent<InteractionForecast>(
            item.Key, item.Generation, ((GameEvent.SpatialInteraction)item.Payload).Forecast)).ToArray();
        InteractionDueWave wave = _interactions.EvaluateDueWave(due, now);
        var effects = new List<SpatialInteractionEffectProposal>();
        var delivered = new List<(InteractionInterestOwner Owner, SpatialInteractionInterest Interest)>();
        IReadOnlyList<InteractionInterestOwner> owners = _interactionRegistration.CaptureOwners();
        _evaluatingInteractions = true;
        foreach (InteractionInterestOwner owner in owners) owner.SetEvaluationReadOnly(true);
        try
        {
            foreach (var domain in wave.Results.Where(result => result.Disposition == ScheduledEventDisposition.Applied)
                .GroupBy(result => result.Event.Payload.Candidate.Interest.DomainKind))
            {
                ISpatialInteractionDomainEvaluator? evaluator = _interactionRegistration.Evaluator(domain.Key);
                if (_sensorInterests is not null && ReferenceEquals(_sensorInterests, _interactionRegistration.Get(domain.Key)))
                {
                    delivered.AddRange(domain.Select(result => (_sensorInterests, result.Event.Payload.Candidate.Interest)));
                    continue;
                }
                if (evaluator is null) continue;
                InteractionCandidate[] contacts = domain.Select(result => result.Event.Payload.Candidate).ToArray();
                IReadOnlyList<SpatialInteractionEffectProposal> evaluated = evaluator.Evaluate(wave.View, Array.AsReadOnly(contacts));
                SpatialInteractionEffectProposal[]? proposed = evaluated?.ToArray();
                var allowed = contacts.Select(contact => contact.Key).ToHashSet();
                if (proposed is null || proposed.Any(effect => effect is null || effect.Effect is null || !allowed.Contains(effect.CandidateKey)))
                    throw new InvalidOperationException("Domain effects must identify eligible contacts from their own evaluation wave.");
                effects.AddRange(proposed);
                InteractionInterestOwner owner = _interactionRegistration.Get(domain.Key)!;
                delivered.AddRange(contacts.Select(contact => (owner, contact.Interest)));
            }
        }
        finally
        {
            foreach (InteractionInterestOwner owner in owners) owner.SetEvaluationReadOnly(false);
            _evaluatingInteractions = false;
        }
        if (effects.Select(effect => (effect.CandidateKey, effect.EffectKind, effect.LocalOrdinal)).Distinct().Count() != effects.Count)
            throw new InvalidOperationException("Duplicate spatial interaction effect order.");
        var sensorDue = new List<SensorActionDue>();
        foreach (InteractionDueResult result in wave.Results.Where(result => result.Disposition == ScheduledEventDisposition.Applied))
        {
            SpatialInteractionInterest interest = result.Event.Payload.Candidate.Interest;
            if (interest.DomainKind == SensorDomainKind && _sensorBindings.TryGetValue(interest.Source, out var binding)
                && binding.Interest == interest.Id && _orders.GetActive(interest.Source)?.Action is { } action)
                sensorDue.Add(new SensorActionDue(new SensorActionWake(interest.Source, binding.Order,
                    action.OriginCommandSequence), result.Event.Key));
        }
        foreach (ScheduledEvent<GameEvent> item in events)
            if (item.Payload is GameEvent.SensorActionReevaluation reevaluation)
                sensorDue.AddRange(reevaluation.Work.Select(work => new SensorActionDue(work, item.Key)));
        foreach (SpatialInteractionEffectProposal effect in effects.OrderBy(effect => effect.CandidateKey)
            .ThenBy(effect => effect.EffectKind).ThenBy(effect => effect.LocalOrdinal))
            effect.Effect.Commit(_interactionSession);
        CommitSensorActions(sensorDue);
        foreach (var delivery in delivered) delivery.Owner.RecordDelivery(delivery.Interest, wave.View);
        _interactions.CompleteDueWave(wave);
        PrepareStateUpdate(now, agenda);
        Dictionary<EventKey, ScheduledEventDisposition> dispositions = wave.Results.ToDictionary(result => result.Event.Key, result => result.Disposition);
        foreach (ScheduledEvent<GameEvent> item in events)
            if (item.Payload is GameEvent.SensorActionReevaluation) dispositions.Add(item.Key, ScheduledEventDisposition.Applied);
        return Array.AsReadOnly(events.Select(item => dispositions[item.Key]).ToArray());
    }
}
