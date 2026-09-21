using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SimulationEngineTests
{
    [Fact]
    public void NewSimulationStartsAtZero()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);

        Assert.Equal(SimulationTime.Zero, simulation.CurrentTime);
    }

    [Fact]
    public void RunUntilProcessesAnIndependentScenarioInAgendaOrder()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        simulation.Schedule(
            new SimulationTime(100),
            EventPhase.Decision,
            new EventGeneration(0),
            new CounterEvent(10));
        simulation.Schedule(
            new SimulationTime(100),
            EventPhase.StateUpdate,
            new EventGeneration(0),
            new CounterEvent(1));

        RunReport report = simulation.RunUntil(new SimulationTime(150));

        Assert.Equal(11, runtime.Value);
        Assert.Equal(
            [EventPhase.StateUpdate, EventPhase.Decision],
            runtime.ProcessedPhases);
        Assert.Equal(SimulationTime.Zero, report.StartTime);
        Assert.Equal(new SimulationTime(150), report.EndTime);
        Assert.Equal(2, report.EventsProcessed);
        Assert.Equal(report.EndTime, simulation.CurrentTime);
    }

    [Fact]
    public void ReconcileRunsAfterAllEarlierPhaseEventsAtTheTimestamp()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        var timestamp = new SimulationTime(100);
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(1));
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(2));

        simulation.RunUntil(timestamp);

        Assert.Equal([0, 3], runtime.ValuesSeenDuringReconciliation);
    }

    [Fact]
    public void SameTimestampWorkDrainsCurrentAndLaterPhasesDeterministically()
    {
        var runtime = new CounterRuntime
        {
            ScheduleFollowUpEvents = true,
        };
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        var timestamp = new SimulationTime(100);
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(1));

        RunReport report = simulation.RunUntil(timestamp);

        Assert.Equal(111, runtime.Value);
        Assert.Equal(
            [
                EventPhase.PhysicalCompletion,
                EventPhase.PhysicalCompletion,
                EventPhase.StateUpdate,
                EventPhase.Decision,
            ],
            runtime.ProcessedPhases);
        Assert.Equal(4, report.EventsProcessed);
    }

    [Fact]
    public void ReconciliationCanScheduleDecisionWorkAtTheCurrentTimestamp()
    {
        var timestamp = new SimulationTime(100);
        var runtime = new CounterRuntime
        {
            ScheduleDecisionDuringReconciliationAt = timestamp,
        };
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(1));

        RunReport report = simulation.RunUntil(timestamp);

        Assert.Equal(1_001, runtime.Value);
        Assert.Equal(
            [EventPhase.PhysicalCompletion, EventPhase.Decision],
            runtime.ProcessedPhases);
        Assert.Equal(2, report.EventsProcessed);
    }

    [Fact]
    public void AccrualOccursOnceForEachReachedTime()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        var timestamp = new SimulationTime(100);
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(1));
        simulation.Schedule(
            timestamp,
            EventPhase.StateUpdate,
            new EventGeneration(0),
            new CounterEvent(10));

        simulation.RunUntil(new SimulationTime(150));

        Assert.Equal(
            [timestamp, new SimulationTime(150)],
            runtime.AccruedTimes);
    }

    [Fact]
    public void EmptyRunBoundariesDoNotTriggerAdditionalReconciliation()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);

        simulation.RunUntil(new SimulationTime(10));
        simulation.RunUntil(new SimulationTime(20));

        Assert.Equal([0], runtime.ValuesSeenDuringReconciliation);
    }

    [Fact]
    public void StopConditionTakesEffectAfterTheTimestampCycleCompletes()
    {
        var runtime = new CounterRuntime
        {
            StopAtValue = 1,
        };
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        var timestamp = new SimulationTime(100);
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(1));
        simulation.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(2));

        RunReport report = simulation.RunUntil(new SimulationTime(200));

        Assert.Equal(3, runtime.Value);
        Assert.Equal(2, report.EventsProcessed);
        Assert.Equal(timestamp, report.EndTime);
        Assert.Equal([0, 3], runtime.ValuesSeenDuringReconciliation);
    }

    [Fact]
    public void RuntimeRecordsIgnoredEventDispositionWithoutMutation()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        simulation.Schedule(
            new SimulationTime(100),
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(
                10,
                ScheduledEventDisposition.IgnoredStaleGeneration));

        RunReport report = simulation.RunUntil(new SimulationTime(100));

        Assert.Equal(0, runtime.Value);
        Assert.Equal(1, report.EventsProcessed);
        Assert.Equal(
            [ScheduledEventDisposition.IgnoredStaleGeneration],
            runtime.Dispositions);
    }

    [Fact]
    public void RunUntilRejectsBackwardTimeTravel()
    {
        var simulation = new SimulationEngine<CounterEvent>(new CounterRuntime());
        simulation.RunUntil(new SimulationTime(100));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => simulation.RunUntil(new SimulationTime(99)));
        Assert.Equal(new SimulationTime(100), simulation.CurrentTime);
    }

    [Fact]
    public void CompletedTimestampCapturesInitializedEngineAndPendingAgenda()
    {
        var runtime = new CounterRuntime();
        var simulation = new SimulationEngine<CounterEvent>(runtime);
        simulation.Schedule(
            new SimulationTime(100),
            EventPhase.PhysicalCompletion,
            new EventGeneration(3),
            new CounterEvent(5));
        simulation.Schedule(
            new SimulationTime(200),
            EventPhase.StateUpdate,
            new EventGeneration(4),
            new CounterEvent(7));

        simulation.RunUntil(new SimulationTime(100));
        CheckpointResult<SimulationEngineCheckpoint<CounterEvent>> capture =
            simulation.CaptureCheckpoint();

        Assert.True(capture.IsSuccess);
        Assert.True(capture.Value!.IsInitialized);
        Assert.Equal(new SimulationTime(100), capture.Value.AccruedThrough);
        Assert.Equal(new SimulationTime(100), capture.Value.Agenda.CurrentTime);
        ScheduledEvent<CounterEvent> pending = Assert.Single(
            capture.Value.Agenda.PendingEvents);
        Assert.Equal(new SimulationTime(200), pending.Key.Timestamp);
        Assert.Equal(new EventGeneration(4), pending.Generation);
        Assert.Equal(new CounterEvent(7), pending.Payload);
    }

    [Fact]
    public void RestoredEngineContinuesWithoutInitializationAccrualOrReplay()
    {
        var uninterruptedRuntime = new CounterRuntime();
        var uninterrupted = new SimulationEngine<CounterEvent>(
            uninterruptedRuntime);
        uninterrupted.Schedule(
            new SimulationTime(100),
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            new CounterEvent(5));
        uninterrupted.RunUntil(new SimulationTime(50));
        SimulationEngineCheckpoint<CounterEvent> checkpoint =
            uninterrupted.CaptureCheckpoint().Value!;
        uninterruptedRuntime.ClearObservations();

        var restoredRuntime = new CounterRuntime();
        CheckpointResult<SimulationEngine<CounterEvent>> restoration =
            SimulationEngine<CounterEvent>.RestoreCheckpoint(
                restoredRuntime,
                checkpoint);

        Assert.True(restoration.IsSuccess);
        Assert.Empty(restoredRuntime.AccruedTimes);
        Assert.Empty(restoredRuntime.ValuesSeenDuringReconciliation);
        Assert.Empty(restoredRuntime.ProcessedPhases);

        RunReport uninterruptedReport = uninterrupted.RunUntil(
            new SimulationTime(150));
        RunReport restoredReport = restoration.Value!.RunUntil(
            new SimulationTime(150));

        Assert.Equal(uninterruptedReport, restoredReport);
        Assert.Equal(uninterruptedRuntime.Value, restoredRuntime.Value);
        Assert.Equal(
            uninterruptedRuntime.AccruedTimes,
            restoredRuntime.AccruedTimes);
        Assert.Equal(
            uninterruptedRuntime.ValuesSeenDuringReconciliation,
            restoredRuntime.ValuesSeenDuringReconciliation);
        Assert.Equal(
            uninterruptedRuntime.ProcessedPhases,
            restoredRuntime.ProcessedPhases);
    }

    [Fact]
    public void RestoreRejectsAccrualThatDoesNotReachCheckpointTime()
    {
        var checkpoint = new SimulationEngineCheckpoint<CounterEvent>(
            isInitialized: true,
            accruedThrough: SimulationTime.Zero,
            new EventAgendaCheckpoint<CounterEvent>(
                new SimulationTime(10),
                nextCreationSequence: 0,
                Array.Empty<ScheduledEvent<CounterEvent>>()));

        CheckpointResult<SimulationEngine<CounterEvent>> restoration =
            SimulationEngine<CounterEvent>.RestoreCheckpoint(
                new CounterRuntime(),
                checkpoint);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.engine.accruedThrough",
            restoration.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsUninitializedEngineAfterTimeZero()
    {
        var timestamp = new SimulationTime(10);
        var checkpoint = new SimulationEngineCheckpoint<CounterEvent>(
            isInitialized: false,
            accruedThrough: timestamp,
            new EventAgendaCheckpoint<CounterEvent>(
                timestamp,
                nextCreationSequence: 0,
                Array.Empty<ScheduledEvent<CounterEvent>>()));

        CheckpointResult<SimulationEngine<CounterEvent>> restoration =
            SimulationEngine<CounterEvent>.RestoreCheckpoint(
                new CounterRuntime(),
                checkpoint);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.engine.isInitialized",
            restoration.Failure!.Path);
    }

    private sealed record CounterEvent(
        int Delta,
        ScheduledEventDisposition Disposition = ScheduledEventDisposition.Applied);

    private sealed class CounterRuntime : ISimulationRuntime<CounterEvent>
    {
        public int Value { get; private set; }

        public List<EventPhase> ProcessedPhases { get; } = [];

        public List<ScheduledEventDisposition> Dispositions { get; } = [];

        public List<int> ValuesSeenDuringReconciliation { get; } = [];

        public List<SimulationTime> AccruedTimes { get; } = [];

        public bool ScheduleFollowUpEvents { get; init; }

        public SimulationTime? ScheduleDecisionDuringReconciliationAt { get; init; }

        public int? StopAtValue { get; init; }

        public bool ShouldStop => StopAtValue is { } threshold && Value >= threshold;

        internal void ClearObservations()
        {
            ProcessedPhases.Clear();
            Dispositions.Clear();
            ValuesSeenDuringReconciliation.Clear();
            AccruedTimes.Clear();
        }

        public void Reconcile(SimulationTime now, EventAgenda<CounterEvent> agenda)
        {
            ValuesSeenDuringReconciliation.Add(Value);
            if (now == ScheduleDecisionDuringReconciliationAt)
            {
                agenda.Schedule(
                    now,
                    EventPhase.Decision,
                    new EventGeneration(0),
                    new CounterEvent(1_000));
            }
        }

        public void AccrueTo(SimulationTime now)
        {
            AccruedTimes.Add(now);
        }

        public ScheduledEventDisposition HandleEvent(
            ScheduledEvent<CounterEvent> scheduled,
            SimulationTime now,
            EventAgenda<CounterEvent> agenda)
        {
            CounterEvent simulationEvent = scheduled.Payload;
            if (simulationEvent.Disposition != ScheduledEventDisposition.Applied)
            {
                return simulationEvent.Disposition;
            }

            Value = checked(Value + simulationEvent.Delta);
            if (ScheduleFollowUpEvents
                && simulationEvent.Delta == 1)
            {
                agenda.Schedule(
                    now,
                    EventPhase.PhysicalCompletion,
                    new EventGeneration(0),
                    new CounterEvent(10));
                agenda.Schedule(
                    now,
                    EventPhase.StateUpdate,
                    new EventGeneration(0),
                    new CounterEvent(100));
                agenda.Schedule(
                    now,
                    EventPhase.Decision,
                    new EventGeneration(0),
                    new CounterEvent(0));
            }

            return ScheduledEventDisposition.Applied;
        }

        public void RecordEvent(
            ScheduledEvent<CounterEvent> simulationEvent,
            ScheduledEventDisposition disposition)
        {
            ProcessedPhases.Add(simulationEvent.Key.Phase);
            Dispositions.Add(disposition);
        }
    }
}
