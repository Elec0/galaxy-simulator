using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class EventAgendaTests
{
    [Fact]
    public void EventsAreOrderedByTimePhaseAndCreationSequence()
    {
        var agenda = new EventAgenda<string>();
        var generation = new EventGeneration(0);
        var timestamp = new SimulationTime(10);

        agenda.Schedule(timestamp, EventPhase.Decision, generation, "decision");
        agenda.Schedule(timestamp, EventPhase.PhysicalCompletion, generation, "first completion");
        agenda.Schedule(timestamp, EventPhase.PhysicalCompletion, generation, "second completion");

        var payloads = new List<string>();
        while (agenda.PopNextThrough(timestamp) is { } scheduled)
        {
            payloads.Add(scheduled.Payload);
        }

        Assert.Equal(
            ["first completion", "second completion", "decision"],
            payloads);
    }

    [Fact]
    public void CurrentTimestampRejectsAnEarlierPhase()
    {
        var agenda = new EventAgenda<string>();
        var timestamp = new SimulationTime(10);
        agenda.Schedule(timestamp, EventPhase.Decision, new EventGeneration(0), "decision");
        Assert.NotNull(agenda.PopNextThrough(timestamp));

        Assert.Throws<InvalidOperationException>(() =>
            agenda.Schedule(
                timestamp,
                EventPhase.PhysicalCompletion,
                new EventGeneration(0),
                "completion"));
    }

    [Fact]
    public void AdvancingWithoutAnEventResetsThePhase()
    {
        var agenda = new EventAgenda<string>();
        var timestamp = new SimulationTime(10);

        Assert.Null(agenda.PopNextThrough(timestamp));
        agenda.Schedule(
            timestamp,
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            "completion");

        Assert.Equal(1, agenda.Count);
    }

    [Fact]
    public void EventExposesCallerManagedGeneration()
    {
        var agenda = new EventAgenda<string>();
        var generation = new EventGeneration(7);
        var timestamp = new SimulationTime(10);
        agenda.Schedule(timestamp, EventPhase.StateUpdate, generation, "refresh");

        ScheduledEvent<string> scheduled = Assert.IsType<ScheduledEvent<string>>(
            agenda.PopNextThrough(timestamp));

        Assert.Equal(generation, scheduled.Generation);
    }
}
