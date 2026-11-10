namespace GalaxyCommand.Simulation.Tests;

public sealed class StationProviderAgendaTests
{
    [Fact]
    public void ProviderPreflightCanResolveAnExactPendingCompletionInsideAPhaseWithoutAllocation()
    {
        var agenda = new EventAgenda<int>();
        EventKey expected = agenda.Schedule(new(2), EventPhase.PhysicalCompletion, new(3), 42);
        ulong sequence = agenda.NextCreationSequence;
        agenda.AdvanceTo(new(1));
        agenda.EnterPhase(EventPhase.PhysicalCompletion);

        Assert.True(agenda.TryFindPending(new(2), EventPhase.PhysicalCompletion, new(3), 42, out EventKey found));
        Assert.Equal(expected, found);
        Assert.Equal(sequence, agenda.NextCreationSequence);
        Assert.Equal(1, agenda.Count);
    }

    [Fact]
    public void AmbiguousOrChangedProviderCompletionsRejectWithoutCancellingAnyEntry()
    {
        var agenda = new EventAgenda<int>();
        agenda.Schedule(new(2), EventPhase.PhysicalCompletion, new(3), 42);
        agenda.Schedule(new(2), EventPhase.PhysicalCompletion, new(3), 42);

        Assert.False(agenda.TryFindPending(new(2), EventPhase.PhysicalCompletion, new(3), 42, out _));
        Assert.False(agenda.TryFindPending(new(2), EventPhase.PhysicalCompletion, new(4), 42, out _));
        Assert.Equal(2, agenda.Count);
    }
}
