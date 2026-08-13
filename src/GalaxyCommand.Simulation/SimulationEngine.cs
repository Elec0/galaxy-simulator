namespace GalaxyCommand.Simulation;

/// <summary>
/// Domain-specific behavior hosted by the simulation engine.
/// </summary>
public interface ISimulationRuntime<TEvent>
{
    bool ShouldStop { get; }

    /// <summary>
    /// Runs once at the start of the decision phase after earlier same-time phases drain.
    /// </summary>
    void Reconcile(SimulationTime now, EventAgenda<TEvent> agenda);

    void AccrueTo(SimulationTime now);

    ScheduledEventDisposition HandleEvent(
        ScheduledEvent<TEvent> simulationEvent,
        SimulationTime now,
        EventAgenda<TEvent> agenda);

    void RecordEvent(
        ScheduledEvent<TEvent> simulationEvent,
        ScheduledEventDisposition disposition);
}

/// <summary>
/// Rendering-independent deterministic event runner.
/// </summary>
public sealed class SimulationEngine<TEvent>
{
    private readonly ISimulationRuntime<TEvent> _runtime;
    private readonly EventAgenda<TEvent> _agenda;
    private SimulationTime _accruedThrough = SimulationTime.Zero;
    private bool _initialized;

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

    internal EventAgenda<TEvent> Agenda => _agenda;

    /// <summary>
    /// Captures exact engine progress and agenda state at a completed timestamp
    /// boundary.
    /// </summary>
    internal CheckpointResult<SimulationEngineCheckpoint<TEvent>> CaptureCheckpoint()
    {
        CheckpointResult<EventAgendaCheckpoint<TEvent>> agenda =
            _agenda.CaptureCheckpoint();
        if (!agenda.IsSuccess)
        {
            return CheckpointResult<SimulationEngineCheckpoint<TEvent>>.Rejected(
                agenda.Failure!);
        }

        if (_accruedThrough != CurrentTime)
        {
            return CheckpointResult<SimulationEngineCheckpoint<TEvent>>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.engine.accruedThrough",
                    "Engine accrual has not reached the completed checkpoint time."));
        }

        return CheckpointResult<SimulationEngineCheckpoint<TEvent>>.Success(
            new SimulationEngineCheckpoint<TEvent>(
                _initialized,
                _accruedThrough,
                agenda.Value!));
    }

    /// <summary>
    /// Validates and restores engine progress and its agenda directly without
    /// initialization, accrual, event dispatch, or sequence allocation.
    /// </summary>
    internal static CheckpointResult<SimulationEngine<TEvent>> RestoreCheckpoint(
        ISimulationRuntime<TEvent> runtime,
        SimulationEngineCheckpoint<TEvent> checkpoint)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.AccruedThrough != checkpoint.Agenda.CurrentTime)
        {
            return CheckpointResult<SimulationEngine<TEvent>>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.engine.accruedThrough",
                    "Restored engine accrual must equal the completed checkpoint time."));
        }

        if (!checkpoint.IsInitialized &&
            checkpoint.Agenda.CurrentTime != SimulationTime.Zero)
        {
            return CheckpointResult<SimulationEngine<TEvent>>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.engine.isInitialized",
                    "An uninitialized engine cannot have advanced beyond time zero."));
        }

        CheckpointResult<EventAgenda<TEvent>> agenda =
            EventAgenda<TEvent>.RestoreCheckpoint(checkpoint.Agenda);
        if (!agenda.IsSuccess)
        {
            return CheckpointResult<SimulationEngine<TEvent>>.Rejected(
                agenda.Failure!);
        }

        var restored = new SimulationEngine<TEvent>(runtime, agenda.Value)
        {
            _accruedThrough = checkpoint.AccruedThrough,
            _initialized = checkpoint.IsInitialized,
        };
        return CheckpointResult<SimulationEngine<TEvent>>.Success(restored);
    }

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
        long eventsProcessed = 0;

        if (!_initialized)
        {
            eventsProcessed = ProcessTimestamp(CurrentTime, eventsProcessed);
            _initialized = true;
        }

        if (_runtime.ShouldStop)
        {
            return new RunReport(startTime, CurrentTime, eventsProcessed);
        }

        while (_agenda.NextEventKey is { } next && next.Timestamp <= target)
        {
            eventsProcessed = ProcessTimestamp(next.Timestamp, eventsProcessed);
            if (_runtime.ShouldStop)
            {
                break;
            }
        }

        if (!_runtime.ShouldStop)
        {
            _agenda.AdvanceTo(target);
            AccrueThrough(target);
        }

        return new RunReport(startTime, CurrentTime, eventsProcessed);
    }

    private long ProcessTimestamp(SimulationTime timestamp, long eventsProcessed)
    {
        _agenda.AdvanceTo(timestamp);
        AccrueThrough(timestamp);

        foreach (EventPhase phase in Enum.GetValues<EventPhase>())
        {
            _agenda.EnterPhase(phase);
            if (phase == EventPhase.Decision)
            {
                _runtime.Reconcile(timestamp, _agenda);
            }

            while (_agenda.PopNextInCurrentPhase() is { } scheduled)
            {
                ScheduledEventDisposition disposition =
                    _runtime.HandleEvent(scheduled, timestamp, _agenda);
                _runtime.RecordEvent(scheduled, disposition);
                eventsProcessed = checked(eventsProcessed + 1);
            }
        }

        _agenda.CompleteTimestamp();

        return eventsProcessed;
    }

    private void AccrueThrough(SimulationTime timestamp)
    {
        if (timestamp <= _accruedThrough)
        {
            return;
        }

        _runtime.AccrueTo(timestamp);
        _accruedThrough = timestamp;
    }
}
