namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministic processing phase for events sharing a timestamp.
/// </summary>
public enum EventPhase
{
    PhysicalCompletion,
    StateUpdate,
    Decision,
}

/// <summary>
/// Caller-managed generation used to recognize stale scheduled events.
/// </summary>
public readonly record struct EventGeneration(ulong Value)
{
    public EventGeneration Next() =>
        new(checked(Value + 1));
}

/// <summary>
/// Deterministic result of validating and handling one scheduled event.
/// Ignored events never mutate authoritative state.
/// </summary>
public enum ScheduledEventDisposition
{
    Applied,
    IgnoredStaleGeneration,
    IgnoredMissingReference,
    IgnoredStateMismatch,
}

/// <summary>
/// Complete deterministic ordering key for a scheduled event.
/// </summary>
public readonly record struct EventKey(
    SimulationTime Timestamp,
    EventPhase Phase,
    ulong CreationSequence) : IComparable<EventKey>
{
    public int CompareTo(EventKey other)
    {
        int timestampComparison = Timestamp.CompareTo(other.Timestamp);
        if (timestampComparison != 0)
        {
            return timestampComparison;
        }

        int phaseComparison = Phase.CompareTo(other.Phase);
        return phaseComparison != 0
            ? phaseComparison
            : CreationSequence.CompareTo(other.CreationSequence);
    }

    public static bool operator <(EventKey left, EventKey right) => left.CompareTo(right) < 0;

    public static bool operator <=(EventKey left, EventKey right) => left.CompareTo(right) <= 0;

    public static bool operator >(EventKey left, EventKey right) => left.CompareTo(right) > 0;

    public static bool operator >=(EventKey left, EventKey right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// An event removed from the pending agenda for processing.
/// </summary>
public sealed record ScheduledEvent<TEvent>(
    EventKey Key,
    EventGeneration Generation,
    TEvent Payload);

/// <summary>
/// Result of checking whether one exact pending agenda entry may be cancelled.
/// </summary>
public enum AgendaCancellationCheck
{
    Matches,
    Missing,
    Mismatch,
}

/// <summary>
/// Ordered agenda of future domain events.
/// </summary>
public sealed class EventAgenda<TEvent>
{
    private readonly SortedDictionary<EventKey, PendingEvent> _pending = [];
    private ulong _nextCreationSequence;

    public SimulationTime CurrentTime { get; private set; } = SimulationTime.Zero;

    public EventPhase? CurrentPhase { get; private set; }

    public int Count => _pending.Count;

    /// <summary>
    /// Earliest pending event without changing agenda state.
    /// </summary>
    public EventKey? NextEventKey => _pending.Count > 0
        ? _pending.First().Key
        : null;

    public EventKey Schedule(
        SimulationTime timestamp,
        EventPhase phase,
        EventGeneration generation,
        TEvent payload)
    {
        if (timestamp < CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                timestamp,
                $"Timestamp {timestamp.Milliseconds} ms precedes current simulation time {CurrentTime.Milliseconds} ms.");
        }

        if (timestamp == CurrentTime && CurrentPhase is { } currentPhase && phase < currentPhase)
        {
            throw new InvalidOperationException(
                $"Cannot schedule phase {phase} after phase {currentPhase} at the current timestamp.");
        }

        ulong creationSequence = _nextCreationSequence;
        _nextCreationSequence = checked(creationSequence + 1);
        var key = new EventKey(timestamp, phase, creationSequence);
        _pending.Add(key, new PendingEvent(generation, payload));
        return key;
    }

    /// <summary>
    /// Checks one pending entry without changing agenda state or allocating a
    /// creation sequence. The payload comparison is exact for the agenda's
    /// event type.
    /// </summary>
    public AgendaCancellationCheck CheckCancellation(
        EventKey key,
        EventGeneration expectedGeneration,
        TEvent expectedPayload)
    {
        if (!_pending.TryGetValue(key, out PendingEvent? pending))
        {
            return AgendaCancellationCheck.Missing;
        }

        return pending.Generation == expectedGeneration
            && EqualityComparer<TEvent>.Default.Equals(pending.Payload, expectedPayload)
            ? AgendaCancellationCheck.Matches
            : AgendaCancellationCheck.Mismatch;
    }

    /// <summary>
    /// Revalidates and removes one exact pending entry without allocating a
    /// creation sequence. A false result leaves the agenda unchanged.
    /// </summary>
    public bool TryCancelExact(
        EventKey key,
        EventGeneration expectedGeneration,
        TEvent expectedPayload)
    {
        if (CheckCancellation(key, expectedGeneration, expectedPayload)
            != AgendaCancellationCheck.Matches)
        {
            return false;
        }

        return _pending.Remove(key);
    }

    /// <summary>
    /// Moves the agenda clock without consuming an event.
    /// </summary>
    public void AdvanceTo(SimulationTime target)
    {
        if (target < CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Target {target.Milliseconds} ms precedes current simulation time {CurrentTime.Milliseconds} ms.");
        }

        if (NextEventKey is { } next && next.Timestamp < target)
        {
            throw new InvalidOperationException(
                $"Cannot advance to {target.Milliseconds} ms while an event remains pending at {next.Timestamp.Milliseconds} ms.");
        }

        CurrentTime = target;
        CurrentPhase = null;
    }

    /// <summary>
    /// Opens a phase at the current timestamp. Completed phases cannot be reopened.
    /// </summary>
    public void EnterPhase(EventPhase phase)
    {
        if (CurrentPhase is { } currentPhase && phase < currentPhase)
        {
            throw new InvalidOperationException(
                $"Cannot return to phase {phase} after phase {currentPhase} at the current timestamp.");
        }

        CurrentPhase = phase;
    }

    /// <summary>
    /// Removes the next event only when it belongs to the open timestamp and phase.
    /// </summary>
    public ScheduledEvent<TEvent>? PopNextInCurrentPhase()
    {
        if (CurrentPhase is not { } currentPhase
            || _pending.Count == 0)
        {
            return null;
        }

        KeyValuePair<EventKey, PendingEvent> first = _pending.First();
        if (first.Key.Timestamp != CurrentTime
            || first.Key.Phase != currentPhase)
        {
            return null;
        }

        _pending.Remove(first.Key);
        return new ScheduledEvent<TEvent>(
            first.Key,
            first.Value.Generation,
            first.Value.Payload);
    }

    /// <summary>
    /// Removes the next event through a target time for direct subsystem processing.
    /// </summary>
    public ScheduledEvent<TEvent>? PopNextThrough(SimulationTime target)
    {
        if (target < CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Target {target.Milliseconds} ms precedes current simulation time {CurrentTime.Milliseconds} ms.");
        }

        if (_pending.Count == 0 || _pending.First().Key.Timestamp > target)
        {
            AdvanceTo(target);
            return null;
        }

        KeyValuePair<EventKey, PendingEvent> first = _pending.First();
        _pending.Remove(first.Key);
        CurrentTime = first.Key.Timestamp;
        CurrentPhase = first.Key.Phase;
        return new ScheduledEvent<TEvent>(
            first.Key,
            first.Value.Generation,
            first.Value.Payload);
    }

    private sealed record PendingEvent(EventGeneration Generation, TEvent Payload);
}
