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
        Assert.Single(runtime.World.Navigation.Locations);
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
    public void RunUntilRejectsBackwardTimeTravel()
    {
        var simulation = new SimulationEngine<CounterEvent>(new CounterRuntime());
        simulation.RunUntil(new SimulationTime(100));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => simulation.RunUntil(new SimulationTime(99)));
        Assert.Equal(new SimulationTime(100), simulation.CurrentTime);
    }

    private sealed record CounterEvent(int Delta);

    private sealed class CounterRuntime : ISimulationRuntime<CounterEvent>
    {
        public CounterRuntime()
        {
            World.AddLocation("Counter");
        }

        public SimulationWorld World { get; } = new();

        public int Value { get; private set; }

        public List<EventPhase> ProcessedPhases { get; } = [];

        public List<int> ValuesSeenDuringReconciliation { get; } = [];

        public List<SimulationTime> AccruedTimes { get; } = [];

        public bool ScheduleFollowUpEvents { get; init; }

        public SimulationTime? ScheduleDecisionDuringReconciliationAt { get; init; }

        public int? StopAtValue { get; init; }

        public bool ShouldStop => StopAtValue is { } threshold && Value >= threshold;

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

        public void HandleEvent(
            CounterEvent simulationEvent,
            SimulationTime now,
            EventAgenda<CounterEvent> agenda)
        {
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
        }

        public void RecordEvent(ScheduledEvent<CounterEvent> simulationEvent)
        {
            ProcessedPhases.Add(simulationEvent.Key.Phase);
        }
    }
}
