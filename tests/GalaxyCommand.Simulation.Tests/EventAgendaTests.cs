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

    [Fact]
    public void EventGenerationAdvancesAndRejectsOverflow()
    {
        Assert.Equal(new EventGeneration(8), new EventGeneration(7).Next());
        Assert.Throws<OverflowException>(() =>
            new EventGeneration(ulong.MaxValue).Next());
    }

    [Fact]
    public void ExactCancellationChecksIdentityAndDoesNotAllocateASequence()
    {
        var agenda = new EventAgenda<string>();
        var generation = new EventGeneration(4);
        EventKey key = agenda.Schedule(
            new SimulationTime(10),
            EventPhase.PhysicalCompletion,
            generation,
            "arrival");

        Assert.Equal(
            AgendaCancellationCheck.Mismatch,
            agenda.CheckCancellation(key, generation, "emergence"));
        Assert.False(agenda.TryCancelExact(key, generation, "emergence"));
        Assert.Equal(
            AgendaCancellationCheck.Missing,
            agenda.CheckCancellation(
                new EventKey(new SimulationTime(10), EventPhase.PhysicalCompletion, 99),
                generation,
                "arrival"));
        Assert.Equal(
            AgendaCancellationCheck.Matches,
            agenda.CheckCancellation(key, generation, "arrival"));
        Assert.True(agenda.TryCancelExact(key, generation, "arrival"));

        EventKey next = agenda.Schedule(
            new SimulationTime(20),
            EventPhase.PhysicalCompletion,
            generation,
            "next");

        Assert.Equal(1UL, next.CreationSequence);
        Assert.Equal(1, agenda.Count);
    }

    [Fact]
    public void AgendaOwnerAllocatesSequencesByStableProposalOrder()
    {
        var agenda = new EventAgenda<string>();
        var proposals = new[]
        {
            new AgendaEventProposal<string>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.LogisticsAssignment,
                    2,
                    1,
                    0,
                    0),
                new SimulationTime(10),
                EventPhase.PhysicalCompletion,
                new EventGeneration(0),
                "second ship"),
            new AgendaEventProposal<string>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ProductionReadiness,
                    1,
                    1,
                    0,
                    0),
                new SimulationTime(20),
                EventPhase.PhysicalCompletion,
                new EventGeneration(0),
                "production"),
            new AgendaEventProposal<string>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.LogisticsAssignment,
                    1,
                    2,
                    0,
                    0),
                new SimulationTime(10),
                EventPhase.PhysicalCompletion,
                new EventGeneration(0),
                "first ship"),
        };

        AgendaCommitResult result = AgendaCommitOwner.Commit(
            agenda,
            proposals.Reverse());

        Assert.Equal([0UL, 1UL, 2UL], result.EventKeys.Select(key => key.CreationSequence));
        agenda.AdvanceTo(new SimulationTime(10));
        agenda.EnterPhase(EventPhase.PhysicalCompletion);
        Assert.Equal(
            "first ship",
            agenda.PopNextInCurrentPhase()?.Payload);
        Assert.Equal(
            "second ship",
            agenda.PopNextInCurrentPhase()?.Payload);
    }

    [Fact]
    public void AgendaOwnerRejectsDuplicateOrderBeforeScheduling()
    {
        var agenda = new EventAgenda<string>();
        var order = new AgendaProposalOrder(
            RuntimeEvaluationWave.ProductionReadiness,
            1,
            1,
            0,
            0);

        Assert.Throws<InvalidOperationException>(() =>
            AgendaCommitOwner.Commit(
                agenda,
                [
                    new AgendaEventProposal<string>(
                        order,
                        new SimulationTime(10),
                        EventPhase.PhysicalCompletion,
                        new EventGeneration(0),
                        "first"),
                    new AgendaEventProposal<string>(
                        order,
                        new SimulationTime(20),
                        EventPhase.PhysicalCompletion,
                        new EventGeneration(0),
                        "second"),
                ]));
        Assert.Equal(0, agenda.Count);
    }
}
