using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Generation-bound scheduled events owned by terminal maneuver execution.
/// </summary>
public abstract record ManeuverScheduleEvent
{
    private ManeuverScheduleEvent(
        MotionId motionId,
        EventGeneration generation)
    {
        MotionId = motionId;
        Generation = generation;
    }

    public MotionId MotionId { get; }

    public EventGeneration Generation { get; }

    public sealed record PhaseBoundary : ManeuverScheduleEvent
    {
        public PhaseBoundary(
            MotionId motionId,
            EventGeneration generation,
            int phaseIndex)
            : base(motionId, generation)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(phaseIndex);
            PhaseIndex = phaseIndex;
        }

        public int PhaseIndex { get; }
    }

    public sealed record Complete : ManeuverScheduleEvent
    {
        public Complete(
            MotionId motionId,
            EventGeneration generation)
            : base(motionId, generation)
        {
        }
    }
}

/// <summary>
/// Exact agenda envelope retained for cancellation and persistence while one
/// maneuver event remains pending.
/// </summary>
public sealed record PendingManeuverScheduleEvent
{
    public PendingManeuverScheduleEvent(
        EventKey eventKey,
        EventGeneration generation,
        ManeuverScheduleEvent payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EventKey = eventKey;
        Generation = generation;
        Payload = payload;
    }

    public EventKey EventKey { get; }

    public EventGeneration Generation { get; }

    public ManeuverScheduleEvent Payload { get; }
}

/// <summary>
/// Exact interruption materialization and generation handoff used to build a
/// replacement plan without resuming the invalidated phase schedule.
/// </summary>
public sealed record ManeuverInterruption(
    SimulationTime Timestamp,
    ShipKinematicState MaterializedState,
    EventGeneration NextGeneration);

/// <summary>
/// Schedule-owned diagnostic for the exact boundary expected by the current
/// phase. The agenda key remains null until proposal commitment binds it.
/// </summary>
public sealed record ManeuverBoundaryDiagnostic(
    MotionId MotionId,
    EventGeneration Generation,
    int PhaseIndex,
    ManeuverPhaseKind PhaseKind,
    SimulationTime Timestamp,
    EventKey? EventKey,
    ManeuverScheduleEvent Payload);

/// <summary>
/// Runtime state for one terminal maneuver. Identity, generation, and analytic
/// plan remain immutable while exact physical boundaries advance one owned
/// phase cursor. Replacement creates another schedule with a later generation.
/// </summary>
public sealed class ScheduledTerminalManeuver
{
    private const int MovementScheduleEffectKind = 1;
    private IReadOnlyList<PendingManeuverScheduleEvent>? _pendingEvents;

    public ScheduledTerminalManeuver(
        MotionId motionId,
        EventGeneration generation,
        ExecutableBoundedTerminalManeuverPlan plan,
        IEnumerable<int>? waypointPhaseIndices = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        MotionId = motionId;
        Generation = generation;
        Plan = plan;
        WaypointPhaseIndices = ValidateWaypointPhaseIndices(
            plan,
            waypointPhaseIndices ?? []);
    }

    /// <summary>
    /// Reconstructs an active phase cursor without replaying earlier boundaries.
    /// Pending agenda keys are validated and bound separately after the owner
    /// validates its checkpoint envelope.
    /// </summary>
    internal static ScheduledTerminalManeuver RestoreActive(
        MotionId motionId,
        EventGeneration generation,
        ExecutableBoundedTerminalManeuverPlan plan,
        int currentPhaseIndex,
        IEnumerable<int>? waypointPhaseIndices = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (currentPhaseIndex < 0 || currentPhaseIndex >= plan.Phases.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentPhaseIndex),
                currentPhaseIndex,
                "An active maneuver phase index must name the executable plan.");
        }

        return new ScheduledTerminalManeuver(
            motionId,
            generation,
            plan,
            waypointPhaseIndices)
        {
            CurrentPhaseIndex = currentPhaseIndex,
        };
    }

    public MotionId MotionId { get; }

    public EventGeneration Generation { get; }

    public ExecutableBoundedTerminalManeuverPlan Plan { get; }

    public IReadOnlyList<int> WaypointPhaseIndices { get; }

    public int CurrentPhaseIndex { get; private set; }

    public bool IsComplete { get; private set; }

    public bool IsInvalidated { get; private set; }

    public bool IsWaypointBoundary(int phaseIndex) =>
        WaypointPhaseIndices.Contains(phaseIndex);

    public ManeuverScheduledPhase? CurrentPhase =>
        IsComplete
            ? null
            : Plan.Phases[CurrentPhaseIndex];

    /// <summary>
    /// Exact remaining agenda envelopes after binding, null before the agenda
    /// owner allocates keys, and empty after successful completion.
    /// </summary>
    public IReadOnlyList<PendingManeuverScheduleEvent>? PendingEvents =>
        _pendingEvents;

    /// <summary>
    /// Describes the exact event expected to end the current phase, including
    /// its agenda key after binding. Completed or invalidated schedules have no
    /// next boundary.
    /// </summary>
    public ManeuverBoundaryDiagnostic? NextBoundary
    {
        get
        {
            if (IsComplete || IsInvalidated)
            {
                return null;
            }

            ManeuverScheduledPhase phase = Plan.Phases[CurrentPhaseIndex];
            PendingManeuverScheduleEvent? pending =
                _pendingEvents is { Count: > 0 }
                    ? _pendingEvents[0]
                    : null;
            ManeuverScheduleEvent payload = pending?.Payload
                ?? (CurrentPhaseIndex < Plan.Phases.Count - 1
                    ? new ManeuverScheduleEvent.PhaseBoundary(
                        MotionId,
                        Generation,
                        CurrentPhaseIndex)
                    : new ManeuverScheduleEvent.Complete(
                        MotionId,
                        Generation));
            return new ManeuverBoundaryDiagnostic(
                MotionId,
                Generation,
                CurrentPhaseIndex,
                phase.Kind,
                phase.EndsAt,
                pending?.EventKey,
                payload);
        }
    }

    /// <summary>
    /// Produces stable agenda work for every unapplied boundary without
    /// advancing the cursor or allocating agenda creation sequences.
    /// </summary>
    public IReadOnlyList<AgendaEventProposal<TEvent>>
        CreateRemainingEventProposals<TEvent>(
            ShipId shipId,
            Func<ManeuverScheduleEvent, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(wrapEvent);
        if (_pendingEvents is not null)
        {
            throw new InvalidOperationException(
                $"Maneuver {MotionId} already has bound agenda events.");
        }

        if (IsComplete)
        {
            return Array.Empty<AgendaEventProposal<TEvent>>();
        }

        var proposals = new List<AgendaEventProposal<TEvent>>(
            Plan.Phases.Count - CurrentPhaseIndex);
        for (int index = CurrentPhaseIndex; index < Plan.Phases.Count; index++)
        {
            ManeuverScheduleEvent maneuverEvent = index < Plan.Phases.Count - 1
                ? new ManeuverScheduleEvent.PhaseBoundary(
                    MotionId,
                    Generation,
                    index)
                : new ManeuverScheduleEvent.Complete(MotionId, Generation);
            proposals.Add(new AgendaEventProposal<TEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.PhysicalCompletion,
                    shipId.Value,
                    MotionId.Value,
                    MovementScheduleEffectKind,
                    index),
                Plan.Phases[index].EndsAt,
                EventPhase.PhysicalCompletion,
                Generation,
                wrapEvent(maneuverEvent)));
        }

        return proposals.AsReadOnly();
    }

    /// <summary>
    /// Binds the agenda keys allocated for every currently remaining proposal.
    /// Validation is atomic: an incomplete or structurally mismatched set is
    /// rejected without publishing partial pending-event state.
    /// </summary>
    public void BindPendingEventKeys(IReadOnlyList<EventKey> eventKeys)
    {
        ArgumentNullException.ThrowIfNull(eventKeys);
        if (IsComplete || _pendingEvents is not null)
        {
            throw new InvalidOperationException(
                $"Maneuver {MotionId} cannot bind another pending event set.");
        }

        int expectedCount = Plan.Phases.Count - CurrentPhaseIndex;
        if (eventKeys.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Maneuver {MotionId} requires {expectedCount} pending event keys, not {eventKeys.Count}.");
        }

        var pending = new PendingManeuverScheduleEvent[expectedCount];
        ulong? previousCreationSequence = null;
        for (int offset = 0; offset < expectedCount; offset++)
        {
            int phaseIndex = CurrentPhaseIndex + offset;
            EventKey eventKey = eventKeys[offset];
            ManeuverScheduledPhase phase = Plan.Phases[phaseIndex];
            if (eventKey.Timestamp != phase.EndsAt
                || eventKey.Phase != EventPhase.PhysicalCompletion)
            {
                throw new InvalidOperationException(
                    $"Agenda event {eventKey} does not match maneuver {MotionId} phase {phaseIndex}.");
            }

            if (previousCreationSequence is { } previous
                && eventKey.CreationSequence <= previous)
            {
                throw new InvalidOperationException(
                    $"Agenda event {eventKey} is not in maneuver {MotionId} creation order.");
            }

            ManeuverScheduleEvent payload = phaseIndex < Plan.Phases.Count - 1
                ? new ManeuverScheduleEvent.PhaseBoundary(
                    MotionId,
                    Generation,
                    phaseIndex)
                : new ManeuverScheduleEvent.Complete(MotionId, Generation);
            pending[offset] = new PendingManeuverScheduleEvent(
                eventKey,
                Generation,
                payload);
            previousCreationSequence = eventKey.CreationSequence;
        }

        _pendingEvents = Array.AsReadOnly(pending);
    }

    /// <summary>
    /// Materializes the active analytic phase at the agenda's current time,
    /// derives the replacement generation, then atomically invalidates all
    /// remaining work. Exact pending-boundary timestamps reject because the
    /// physical-completion phase must advance the cursor first. Cancellation
    /// failure publishes no interruption result and changes no schedule state.
    /// </summary>
    public AgendaCancellationCheck TryInterrupt<TEvent>(
        SimulationTime now,
        EventAgenda<TEvent> agenda,
        Func<ManeuverScheduleEvent, TEvent> wrapEvent,
        out ManeuverInterruption? interruption)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        interruption = null;
        if (agenda.CurrentTime != now)
        {
            throw new InvalidOperationException(
                $"Interruption time {now.Milliseconds} ms does not match agenda time {agenda.CurrentTime.Milliseconds} ms.");
        }

        if (IsInvalidated)
        {
            throw new InvalidOperationException(
                $"Maneuver {MotionId} is already invalidated.");
        }

        ManeuverScheduledPhase phase = CurrentPhase
            ?? throw new InvalidOperationException(
                $"Maneuver {MotionId} has no active phase to interrupt.");
        if (now < phase.StartsAt
            || now >= phase.EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                $"Interruption time must be within active phase {CurrentPhaseIndex} before its pending boundary.");
        }

        ShipKinematicState materialized = Plan.StateAt(now);
        EventGeneration nextGeneration = Generation.Next();
        AgendaCancellationCheck cancellation =
            InvalidateAndCancelPendingEvents(agenda, wrapEvent);
        if (cancellation != AgendaCancellationCheck.Matches)
        {
            return cancellation;
        }

        interruption = new ManeuverInterruption(
            now,
            materialized,
            nextGeneration);
        return AgendaCancellationCheck.Matches;
    }

    /// <summary>
    /// Cancels every exact remaining agenda envelope and invalidates this
    /// schedule only after a complete preflight succeeds. The caller must
    /// materialize interruption state first and create any replacement with
    /// <see cref="EventGeneration.Next"/>. Missing or mismatched work leaves
    /// both the agenda and schedule unchanged.
    /// </summary>
    public AgendaCancellationCheck InvalidateAndCancelPendingEvents<TEvent>(
        EventAgenda<TEvent> agenda,
        Func<ManeuverScheduleEvent, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        if (IsComplete || IsInvalidated || _pendingEvents is null)
        {
            throw new InvalidOperationException(
                $"Maneuver {MotionId} has no active bound schedule to invalidate.");
        }

        var prepared = new List<
            (PendingManeuverScheduleEvent Pending, TEvent Payload)>(
                _pendingEvents.Count);
        foreach (PendingManeuverScheduleEvent pending in _pendingEvents)
        {
            TEvent payload = wrapEvent(pending.Payload);
            AgendaCancellationCheck check = agenda.CheckCancellation(
                pending.EventKey,
                pending.Generation,
                payload);
            if (check != AgendaCancellationCheck.Matches)
            {
                return check;
            }

            prepared.Add((pending, payload));
        }

        // The agenda owner holds exclusive commit authority across this loop,
        // so a post-preflight miss is an invariant failure rather than a retry.
        foreach ((PendingManeuverScheduleEvent pending, TEvent payload)
            in prepared)
        {
            if (!agenda.TryCancelExact(
                pending.EventKey,
                pending.Generation,
                payload))
            {
                throw new InvalidOperationException(
                    $"Prepared cancellation for {pending.EventKey} no longer matches the agenda.");
            }
        }

        _pendingEvents = Array.Empty<PendingManeuverScheduleEvent>();
        IsInvalidated = true;
        return AgendaCancellationCheck.Matches;
    }

    /// <summary>
    /// Dispatches either supported maneuver event through the same generation,
    /// identity, cursor, and timestamp validation used by the typed handlers.
    /// Ignored work publishes no materialized state.
    /// </summary>
    public ScheduledEventDisposition HandleEvent(
        ManeuverScheduleEvent maneuverEvent,
        EventGeneration scheduledGeneration,
        SimulationTime now,
        out ShipKinematicState? materialized)
    {
        ArgumentNullException.ThrowIfNull(maneuverEvent);
        return maneuverEvent switch
        {
            ManeuverScheduleEvent.PhaseBoundary boundary =>
                HandlePhaseBoundary(
                    boundary,
                    scheduledGeneration,
                    now,
                    out materialized),
            ManeuverScheduleEvent.Complete completion =>
                HandleCompletion(
                    completion,
                    scheduledGeneration,
                    now,
                    out materialized),
            _ => throw new InvalidOperationException(
                $"Unsupported maneuver event {maneuverEvent.GetType().Name}."),
        };
    }

    /// <summary>
    /// Materializes and advances one exact non-final phase boundary. Skipped,
    /// duplicate, stale, mistimed, or final-phase boundary work publishes no
    /// state and leaves the cursor unchanged.
    /// </summary>
    public ScheduledEventDisposition HandlePhaseBoundary(
        ManeuverScheduleEvent.PhaseBoundary boundary,
        EventGeneration scheduledGeneration,
        SimulationTime now,
        out ShipKinematicState? materialized)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        materialized = null;
        if (RejectionFor(boundary, scheduledGeneration) is { } rejection)
        {
            return rejection;
        }

        if (IsComplete
            || CurrentPhaseIndex >= Plan.Phases.Count - 1
            || boundary.PhaseIndex != CurrentPhaseIndex
            || now != Plan.Phases[CurrentPhaseIndex].EndsAt)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        materialized = Plan.StateAt(now);
        CurrentPhaseIndex++;
        ConsumeBoundPendingEvent();
        return ScheduledEventDisposition.Applied;
    }

    /// <summary>
    /// Materializes the exact terminal state only when the agenda generation,
    /// payload identity, active generation, final phase cursor, and scheduled
    /// endpoint all match. Ignored work returns no state and changes nothing.
    /// </summary>
    public ScheduledEventDisposition HandleCompletion(
        ManeuverScheduleEvent.Complete completion,
        EventGeneration scheduledGeneration,
        SimulationTime now,
        out ShipKinematicState? materialized)
    {
        ArgumentNullException.ThrowIfNull(completion);
        materialized = null;
        if (RejectionFor(completion, scheduledGeneration) is { } rejection)
        {
            return rejection;
        }

        if (IsComplete
            || CurrentPhaseIndex != Plan.Phases.Count - 1
            || now != Plan.EndsAt)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        materialized = Plan.StateAt(now);
        IsComplete = true;
        ConsumeBoundPendingEvent();
        return ScheduledEventDisposition.Applied;
    }

    /// <summary>
    /// Removes exactly the first bound envelope after its validated handler
    /// advances the authoritative cursor. Unbound unit-level schedules remain
    /// supported until their owner commits agenda proposals.
    /// </summary>
    private void ConsumeBoundPendingEvent()
    {
        if (_pendingEvents is null)
        {
            return;
        }

        _pendingEvents = Array.AsReadOnly(_pendingEvents.Skip(1).ToArray());
    }

    private static ReadOnlyCollection<int> ValidateWaypointPhaseIndices(
        ExecutableBoundedTerminalManeuverPlan plan,
        IEnumerable<int> waypointPhaseIndices)
    {
        int[] indices = waypointPhaseIndices.ToArray();
        int previous = -1;
        foreach (int index in indices)
        {
            if (index <= previous || index < 0 || index >= plan.Phases.Count - 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(waypointPhaseIndices),
                    "Waypoint phase indices must be unique, increasing, and non-terminal.");
            }

            previous = index;
        }

        return new ReadOnlyCollection<int>(indices);
    }

    /// <summary>
    /// Applies common event-envelope validation before a handler inspects its
    /// phase-specific cursor and timestamp. Generation wins over identity so
    /// replaced schedule work remains consistently stale.
    /// </summary>
    private ScheduledEventDisposition? RejectionFor(
        ManeuverScheduleEvent maneuverEvent,
        EventGeneration scheduledGeneration)
    {
        if (scheduledGeneration != maneuverEvent.Generation)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        if (IsInvalidated || Generation != maneuverEvent.Generation)
        {
            return ScheduledEventDisposition.IgnoredStaleGeneration;
        }

        return MotionId != maneuverEvent.MotionId
            ? ScheduledEventDisposition.IgnoredStateMismatch
            : null;
    }
}
