namespace GalaxyCommand.Simulation.Tests;

public sealed class SimulationPhysicalCompletionWaveTests
{
    [Fact]
    public void ForeignEconomicReservationIsAnExactBarrierBetweenContiguousWorkWaves()
    {
        var runtime = new WorkRuntime(new Quantity(3));
        var engine = new SimulationEngine<WorkEvent>(runtime);
        engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), new("newer", 2, 2));
        engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), new("economic-reservation", 0, 1));
        engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), new("older", 1, 1));
        engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), new("last", 3, 1));

        engine.RunUntil(new(1000));

        Assert.Equal<string>(["newer", "economic-reservation", "older", "last"], runtime.Committed);
        Assert.Equal(new Quantity(1), runtime.Inventory.Reserved(new(1)));
        Assert.Equal(new Quantity(1), runtime.Inventory.Stored(new(1)));
        Assert.Equal<string>(["newer"], runtime.SuccessfulWork);
    }

    [Fact]
    public void ForeignEnvelopeCanStillCancelLaterWorkThatHasNotEnteredItsWave()
    {
        var runtime = new WorkRuntime(new Quantity(3));
        var engine = new SimulationEngine<WorkEvent>(runtime);
        engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), new("first", 1, 1));
        engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), new("cancel", 0, 0));
        runtime.CancelPayload = new("later", 2, 1);
        runtime.CancelKey = engine.Schedule(new(1000), EventPhase.PhysicalCompletion, new(0), runtime.CancelPayload);

        engine.RunUntil(new(1000));

        Assert.True(runtime.CancelSucceeded);
        Assert.Equal<string>(["first", "cancel"], runtime.Committed);
        Assert.DoesNotContain(runtime.Recorded, item => item.Payload.Name == "later");
    }

    private sealed record WorkEvent(string Name, int Priority, ulong Material);

    private sealed class WorkRuntime : ISimulationRuntime<WorkEvent>, ISimulationPhysicalCompletionWaveRuntime<WorkEvent>
    {
        internal WorkRuntime(Quantity stock) { Inventory = new(new(1), new(10)); Inventory.Add(new(1), stock); }
        internal Inventory Inventory { get; }
        internal List<string> Committed { get; } = [];
        internal List<string> SuccessfulWork { get; } = [];
        internal List<ScheduledEvent<WorkEvent>> Recorded { get; } = [];
        internal EventKey CancelKey { get; set; }
        internal WorkEvent? CancelPayload { get; set; }
        internal bool CancelSucceeded { get; private set; }
        public bool ShouldStop => false;
        public bool HasPhysicalCompletionWaves => true;
        public void Reconcile(SimulationTime now, EventAgenda<WorkEvent> agenda) { }
        public void AccrueTo(SimulationTime now) { }
        public bool IsPhysicalCompletionWaveEvent(ScheduledEvent<WorkEvent> scheduled) => scheduled.Payload.Priority != 0;
        public IReadOnlyList<ScheduledEventDisposition> HandlePhysicalCompletionWave(IReadOnlyList<ScheduledEvent<WorkEvent>> events,
            SimulationTime now, EventAgenda<WorkEvent> agenda)
        {
            ulong available = Inventory.Available(new(1)).Units;
            var accepted = new Dictionary<EventKey, bool>();
            foreach (ScheduledEvent<WorkEvent> item in events.OrderBy(item => item.Payload.Priority))
            {
                bool fits = item.Payload.Material <= available;
                accepted.Add(item.Key, fits);
                if (fits) available -= item.Payload.Material;
            }
            foreach (ScheduledEvent<WorkEvent> item in events.OrderBy(item => item.Payload.Priority))
            {
                Committed.Add(item.Payload.Name);
                if (!accepted[item.Key]) continue;
                Inventory.RemoveAvailable(new(1), new(item.Payload.Material));
                SuccessfulWork.Add(item.Payload.Name);
            }
            return events.Select(item => accepted[item.Key] ? ScheduledEventDisposition.Applied : ScheduledEventDisposition.IgnoredStateMismatch).ToArray();
        }
        public ScheduledEventDisposition HandleEvent(ScheduledEvent<WorkEvent> item, SimulationTime now, EventAgenda<WorkEvent> agenda)
        {
            Committed.Add(item.Payload.Name);
            if (item.Payload.Name == "cancel") CancelSucceeded = agenda.TryCancelExact(CancelKey, new(0), CancelPayload!);
            else Inventory.Reserve(new(1), new(1), new(item.Payload.Material), new ReservationOwner.ProductionJob(new(1)));
            return ScheduledEventDisposition.Applied;
        }
        public void RecordEvent(ScheduledEvent<WorkEvent> item, ScheduledEventDisposition disposition) => Recorded.Add(item);
    }
}
