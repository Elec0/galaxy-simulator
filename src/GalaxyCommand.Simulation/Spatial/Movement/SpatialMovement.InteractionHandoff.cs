namespace GalaxyCommand.Simulation;

public sealed partial class SpatialMovement
{
    /// <summary>
    /// Atomically interrupts analytic schedules and enters caller-owned intervals at the agenda's
    /// current time. Stationary handoffs and same-owner updates may join the same complete claim.
    /// Missing actors, stale revisions, competing ownership, or missing/mismatched agenda work
    /// return false without changes. Invalid geometry, unbound schedules, boundary-time interruption,
    /// wrapper failures, and exhausted counters throw before mutation. Empty input is a no-op.
    /// The wrapper must be a pure envelope adapter and runs once per pending envelope in preflight.
    /// Requires exclusive movement/agenda commit authority. Gameplay membership stays with the caller.
    /// </summary>
    public bool TryHandoffInteractionMotions<TEvent>(IEnumerable<InteractionMotionRequest> requests,
        InteractionMotionOwner owner, SimulationTime now, EventAgenda<TEvent> agenda,
        Func<SpatialMovementEvent, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        ValidateInteractionOwner(owner);
        if (agenda.CurrentTime != now)
            throw new InvalidOperationException("Handoff time must match the agenda's current time.");

        InteractionMotionRequest[] captured = requests.ToArray();
        foreach (InteractionMotionRequest request in captured)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Interval);
            ArgumentOutOfRangeException.ThrowIfZero(request.ShipId.Value);
        }
        Array.Sort(captured, (left, right) => left.ShipId.Value.CompareTo(right.ShipId.Value));
        for (int index = 1; index < captured.Length; index++)
            if (captured[index].ShipId == captured[index - 1].ShipId)
                throw new ArgumentException("Interaction participants must be distinct.", nameof(requests));

        var prepared = new List<PreparedInteractionHandoff<TEvent>>(captured.Length);
        var cancellationKeys = new HashSet<EventKey>();
        foreach (InteractionMotionRequest request in captured)
        {
            if (!_actors.TryGetValue(request.ShipId, out ActorState? actor) || actor.SpatialRevision != request.ExpectedRevision)
                return false;
            if (actor.State is not ShipSpatialState.AnalyticManeuver analytic)
            {
                PreparedInteractionMotion? direct = PrepareInteractionMotion(request.ShipId, owner,
                    request.Interval, request.ExpectedRevision, now);
                if (direct is null) return false;
                prepared.Add(new PreparedInteractionHandoff<TEvent>(direct, null, null));
                continue;
            }

            ManeuverInterruption interruption = analytic.Maneuver.PrepareInterruption(now, agenda.CurrentTime);
            ValidateInteractionInterval(request.Interval, interruption.MaterializedState, now);
            // Retain the counters of separate interruption then entry, but expose
            // only the final state. Capacity for both transitions is checked now.
            var motion = new PreparedInteractionMotion(request.ShipId, actor, request.ExpectedRevision,
                actor.SpatialRevision.Next().Next(), interruption.NextGeneration.Next(),
                new ShipSpatialState.InteractionControlled(owner, request.Interval), null);
            IReadOnlyList<PendingManeuverScheduleEvent> pending = analytic.Maneuver.PendingEvents
                ?? throw new InvalidOperationException($"Ship {request.ShipId} has no bound maneuver schedule.");
            var payloads = new Dictionary<ManeuverScheduleEvent, TEvent>();
            foreach (PendingManeuverScheduleEvent item in pending)
            {
                // A captured/restored claim must not cancel the same envelope twice,
                // even when the caller's payload adapter maps both claims equally.
                if (!cancellationKeys.Add(item.EventKey)) return false;
                TEvent payload = wrapEvent(new SpatialMovementEvent.Maneuver(request.ShipId, item.Payload));
                if (agenda.CheckCancellation(item.EventKey, item.Generation, payload) != AgendaCancellationCheck.Matches)
                    return false;
                payloads.Add(item.Payload, payload);
            }
            prepared.Add(new PreparedInteractionHandoff<TEvent>(motion, analytic.Maneuver, payloads));
        }

        // All caller callbacks, interval evaluations, and counter increments have
        // finished. Cached envelopes are safe under exclusive agenda ownership.
        foreach (PreparedInteractionHandoff<TEvent> item in prepared)
        {
            if (item.Maneuver is not null && item.Maneuver.InvalidateAndCancelPendingEvents(agenda,
                    maneuverEvent => item.Payloads![maneuverEvent]) != AgendaCancellationCheck.Matches)
                throw new InvalidOperationException("A prepared handoff no longer matches authoritative agenda state.");
            CommitInteractionMotion(item.Motion, now);
        }
        return true;
    }

    private sealed record PreparedInteractionHandoff<TEvent>(PreparedInteractionMotion Motion,
        ScheduledTerminalManeuver? Maneuver, Dictionary<ManeuverScheduleEvent, TEvent>? Payloads);
}
