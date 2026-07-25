namespace GalaxyCommand.Simulation;

/// <summary>
/// Domain-specific behavior hosted by the simulation engine.
/// </summary>
public interface ISimulationRuntime<TEvent>
{
    bool ShouldStop { get; }

    void Reconcile(SimulationTime now, EventAgenda<TEvent> agenda);

    void AccrueTo(SimulationTime now);

    void HandleEvent(TEvent simulationEvent, SimulationTime now, EventAgenda<TEvent> agenda);

    void RecordEvent(ScheduledEvent<TEvent> simulationEvent);
}

/// <summary>
/// Rendering-independent deterministic event runner.
/// </summary>
public sealed class SimulationEngine<TEvent>
{
    private readonly ISimulationRuntime<TEvent> _runtime;
    private readonly EventAgenda<TEvent> _agenda;

    public SimulationEngine(
        ISimulationRuntime<TEvent> runtime,
        EventAgenda<TEvent>? agenda = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _agenda = agenda ?? new EventAgenda<TEvent>();
    }

    public SimulationTime CurrentTime => _agenda.CurrentTime;

    public int PendingEventCount => _agenda.Count;

    public EventKey Schedule(
        SimulationTime timestamp,
        EventPhase phase,
        EventGeneration generation,
        TEvent payload) =>
        _agenda.Schedule(timestamp, phase, generation, payload);

    public RunReport RunUntil(SimulationTime target)
    {
        if (target < CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                "The simulation cannot run backward.");
        }

        SimulationTime startTime = CurrentTime;
        _runtime.Reconcile(startTime, _agenda);
        long eventsProcessed = 0;
        if (_runtime.ShouldStop)
        {
            return new RunReport(startTime, CurrentTime, eventsProcessed);
        }

        while (_agenda.PopNextThrough(target) is { } scheduled)
        {
            SimulationTime now = scheduled.Key.Timestamp;
            _runtime.AccrueTo(now);
            _runtime.HandleEvent(scheduled.Payload, now, _agenda);
            _runtime.RecordEvent(scheduled);
            eventsProcessed = checked(eventsProcessed + 1);
            if (_runtime.ShouldStop)
            {
                break;
            }

            _runtime.Reconcile(now, _agenda);
        }

        _runtime.AccrueTo(CurrentTime);
        return new RunReport(startTime, CurrentTime, eventsProcessed);
    }
}
