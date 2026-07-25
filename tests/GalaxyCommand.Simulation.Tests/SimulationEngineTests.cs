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

        public bool ShouldStop => false;

        public void Reconcile(SimulationTime now, EventAgenda<CounterEvent> agenda)
        {
        }

        public void AccrueTo(SimulationTime now)
        {
        }

        public void HandleEvent(
            CounterEvent simulationEvent,
            SimulationTime now,
            EventAgenda<CounterEvent> agenda)
        {
            Value = checked(Value + simulationEvent.Delta);
        }

        public void RecordEvent(ScheduledEvent<CounterEvent> simulationEvent)
        {
            ProcessedPhases.Add(simulationEvent.Key.Phase);
        }
    }
}
