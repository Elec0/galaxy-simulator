using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed record CheckpointValidationFailure(
    string Path,
    string Message);

internal sealed class CheckpointResult<T>
    where T : class
{
    private CheckpointResult(T? value, CheckpointValidationFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    internal T? Value { get; }

    internal CheckpointValidationFailure? Failure { get; }

    internal bool IsSuccess => Value is not null;

    internal static CheckpointResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CheckpointResult<T>(value, null);
    }

    internal static CheckpointResult<T> Rejected(
        CheckpointValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new CheckpointResult<T>(null, failure);
    }
}

internal sealed class EventAgendaCheckpoint<TEvent>
{
    internal EventAgendaCheckpoint(
        SimulationTime currentTime,
        ulong nextCreationSequence,
        IEnumerable<ScheduledEvent<TEvent>> pendingEvents)
    {
        ArgumentNullException.ThrowIfNull(pendingEvents);
        CurrentTime = currentTime;
        NextCreationSequence = nextCreationSequence;
        PendingEvents = new ReadOnlyCollection<ScheduledEvent<TEvent>>(
            pendingEvents.ToArray());
    }

    internal SimulationTime CurrentTime { get; }

    internal ulong NextCreationSequence { get; }

    internal ReadOnlyCollection<ScheduledEvent<TEvent>> PendingEvents { get; }
}

internal sealed class SimulationEngineCheckpoint<TEvent>
{
    internal SimulationEngineCheckpoint(
        bool isInitialized,
        SimulationTime accruedThrough,
        EventAgendaCheckpoint<TEvent> agenda)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        IsInitialized = isInitialized;
        AccruedThrough = accruedThrough;
        Agenda = agenda;
    }

    internal bool IsInitialized { get; }

    internal SimulationTime AccruedThrough { get; }

    internal EventAgendaCheckpoint<TEvent> Agenda { get; }
}
