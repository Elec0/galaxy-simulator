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
public readonly record struct EventGeneration(ulong Value);

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
/// Ordered agenda of future domain events.
/// </summary>
public sealed class EventAgenda<TEvent>
{
    private readonly SortedDictionary<EventKey, PendingEvent> _pending = [];
    private ulong _nextCreationSequence;

    public SimulationTime CurrentTime { get; private set; } = SimulationTime.Zero;

    public EventPhase? CurrentPhase { get; private set; }

    public int Count => _pending.Count;

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

    public ScheduledEvent<TEvent>? PopNextThrough(SimulationTime target)
    {
        if (target < CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Target {target.Milliseconds} ms precedes current simulation time {CurrentTime.Milliseconds} ms.");
        }

        if (_pending.Count == 0)
        {
            AdvanceWithoutEvent(target);
            return null;
        }

        KeyValuePair<EventKey, PendingEvent> first = _pending.First();
        if (first.Key.Timestamp > target)
        {
            AdvanceWithoutEvent(target);
            return null;
        }

        _pending.Remove(first.Key);
        CurrentTime = first.Key.Timestamp;
        CurrentPhase = first.Key.Phase;
        return new ScheduledEvent<TEvent>(
            first.Key,
            first.Value.Generation,
            first.Value.Payload);
    }

    private void AdvanceWithoutEvent(SimulationTime target)
    {
        CurrentTime = target;
        CurrentPhase = null;
    }

    private sealed record PendingEvent(EventGeneration Generation, TEvent Payload);
}
