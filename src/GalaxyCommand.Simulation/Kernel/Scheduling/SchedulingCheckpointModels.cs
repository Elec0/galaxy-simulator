using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

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

