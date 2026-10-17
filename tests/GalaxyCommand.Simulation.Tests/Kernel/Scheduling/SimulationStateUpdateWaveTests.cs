using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SimulationStateUpdateWaveTests
{
    [Fact]
    public void WavePreparationSeesAllPhysicalCompletionsAndDrainsFollowUpBeforeDecision()
    {
        var runtime = new WaveRuntime();
        var engine = new SimulationEngine<int>(runtime);
        var now = new SimulationTime(10);
        engine.Schedule(now, EventPhase.PhysicalCompletion, default, 2);
        engine.Schedule(now, EventPhase.PhysicalCompletion, default, 3);
        engine.Schedule(now, EventPhase.StateUpdate, default, 7);
        RunReport report = engine.RunUntil(now);
        Assert.Equal<int>([0, 5], runtime.PreparedValues);
        Assert.Equal<int>([5, 23], runtime.ValuesAtWaveStart);
        Assert.Equal(2, runtime.Waves.Count);
        Assert.Equal<int>([7, 11], runtime.Waves[0]);
        Assert.Equal<int>([13], runtime.Waves[1]);
        Assert.Equal<int>([0, 36], runtime.DecisionValues);
        Assert.Equal(5, report.EventsProcessed);
        Assert.Equal(5, runtime.RecordedCount);
    }

    private sealed class WaveRuntime : ISimulationRuntime<int>, ISimulationStateUpdateRuntime<int>
    {
        public bool ShouldStop => false;
        internal int Value { get; private set; }
        internal int RecordedCount { get; private set; }
        internal List<int> PreparedValues { get; } = [];
        internal List<int> ValuesAtWaveStart { get; } = [];
        internal List<int[]> Waves { get; } = [];
        internal List<int> DecisionValues { get; } = [];
        public void AccrueTo(SimulationTime now) { }
        public void Reconcile(SimulationTime now, EventAgenda<int> agenda) => DecisionValues.Add(Value);
        public void PrepareStateUpdate(SimulationTime now, EventAgenda<int> agenda)
        {
            PreparedValues.Add(Value);
            if (now.Milliseconds == 10) agenda.Schedule(now, EventPhase.StateUpdate, default, 11);
        }
        public IReadOnlyList<ScheduledEventDisposition> HandleStateUpdateWave(
            IReadOnlyList<ScheduledEvent<int>> events, SimulationTime now, EventAgenda<int> agenda)
        {
            ValuesAtWaveStart.Add(Value);
            Waves.Add(events.Select(item => item.Payload).ToArray());
            foreach (ScheduledEvent<int> item in events) Value += item.Payload;
            if (events.Any(item => item.Payload == 7)) agenda.Schedule(now, EventPhase.StateUpdate, default, 13);
            return events.Select(_ => ScheduledEventDisposition.Applied).ToArray();
        }
        public ScheduledEventDisposition HandleEvent(ScheduledEvent<int> item, SimulationTime now, EventAgenda<int> agenda)
        {
            Value += item.Payload;
            return ScheduledEventDisposition.Applied;
        }
        public void RecordEvent(ScheduledEvent<int> item, ScheduledEventDisposition disposition) => RecordedCount++;
    }
}
