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
        agenda.AdvanceTo(timestamp);
        foreach (EventPhase phase in Enum.GetValues<EventPhase>())
        {
            agenda.EnterPhase(phase);
            while (agenda.PopNextInCurrentPhase() is { } scheduled)
            {
                payloads.Add(scheduled.Payload);
            }
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
        agenda.AdvanceTo(timestamp);
        agenda.EnterPhase(EventPhase.Decision);

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

        agenda.AdvanceTo(timestamp);
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
        agenda.AdvanceTo(timestamp);
        agenda.EnterPhase(EventPhase.StateUpdate);

        ScheduledEvent<string> scheduled = Assert.IsType<ScheduledEvent<string>>(
            agenda.PopNextInCurrentPhase());

        Assert.Equal(generation, scheduled.Generation);
    }

    [Fact]
    public void CurrentPhaseAcceptsSameAndLaterPhaseWork()
    {
        var agenda = new EventAgenda<string>();
        var timestamp = new SimulationTime(10);
        var generation = new EventGeneration(0);
        agenda.AdvanceTo(timestamp);
        agenda.EnterPhase(EventPhase.StateUpdate);

        EventKey samePhase = agenda.Schedule(
            timestamp,
            EventPhase.StateUpdate,
            generation,
            "same phase");
        EventKey laterPhase = agenda.Schedule(
            timestamp,
            EventPhase.Decision,
            generation,
            "later phase");

        Assert.True(samePhase < laterPhase);
        Assert.Equal(samePhase, agenda.NextEventKey);
    }

    [Fact]
    public void AdvanceToRejectsSkippingPendingWork()
    {
        var agenda = new EventAgenda<string>();
        agenda.Schedule(
            new SimulationTime(10),
            EventPhase.PhysicalCompletion,
            new EventGeneration(0),
            "pending");

        Assert.Throws<InvalidOperationException>(() =>
            agenda.AdvanceTo(new SimulationTime(11)));
        Assert.Equal(SimulationTime.Zero, agenda.CurrentTime);
    }
}
