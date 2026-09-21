using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameFactCheckpointTests
{
    private static readonly CommandSource Source = new(
        CommandSourceKind.Player,
        new CommandSourceId("checkpoint-test"));

    [Fact]
    public void RestorePreservesRetainedSuffixAndContinuesSequence()
    {
        var original = new GameFactStore(capacity: 2);
        Commit(original, 1);
        Commit(original, 2);
        Commit(original, 3);

        GameFactStoreCheckpoint checkpoint = original.CaptureCheckpoint();
        CheckpointResult<GameFactStore> result =
            GameFactStore.RestoreCheckpoint(checkpoint);

        Assert.True(result.IsSuccess);
        GameFactStore restored = Assert.IsType<GameFactStore>(result.Value);
        Assert.Equal(
            [2UL, 3UL],
            restored.ReadAfter(null, 10).Facts.Select(fact => fact.Sequence.Value));

        Commit(original, 4);
        Commit(restored, 4);

        GameFactReadResult expected = original.ReadAfter(null, 10);
        GameFactReadResult actual = restored.ReadAfter(null, 10);
        Assert.Equal(expected.Facts, actual.Facts);
        Assert.Equal(expected.OldestRetainedSequence, actual.OldestRetainedSequence);
        Assert.Equal(expected.NewestCommittedSequence, actual.NewestCommittedSequence);
        Assert.Equal(expected.CursorGap, actual.CursorGap);
    }

    [Fact]
    public void RestorePreservesExhaustedSequence()
    {
        GameFactEnvelope retained = Envelope(ulong.MaxValue);
        var checkpoint = new GameFactStoreCheckpoint(
            capacity: 1,
            new IdSequenceCheckpoint(null),
            [retained]);

        CheckpointResult<GameFactStore> result =
            GameFactStore.RestoreCheckpoint(checkpoint);

        Assert.True(result.IsSuccess);
        GameFactStore restored = Assert.IsType<GameFactStore>(result.Value);
        Assert.False(restored.CanCommit(1));
        Assert.Equal(retained, Assert.Single(restored.ReadAfter(null, 10).Facts));
    }

    [Fact]
    public void RestoreRejectsNonContiguousRetainedSuffix()
    {
        var checkpoint = new GameFactStoreCheckpoint(
            capacity: 3,
            new IdSequenceCheckpoint(5),
            [Envelope(2), Envelope(4)]);

        CheckpointResult<GameFactStore> result =
            GameFactStore.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.facts.retained[1].sequence", result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsSuffixThatDoesNotReachNewestSequence()
    {
        var checkpoint = new GameFactStoreCheckpoint(
            capacity: 3,
            new IdSequenceCheckpoint(5),
            [Envelope(2), Envelope(3)]);

        CheckpointResult<GameFactStore> result =
            GameFactStore.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.facts.retained", result.Failure?.Path);
    }

    private static void Commit(GameFactStore store, ulong commandSequence)
    {
        var sequence = new CommandSequence(commandSequence);
        store.Commit(
            new SimulationTime(commandSequence),
            new CommandFactCause(sequence),
            [new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.CommandOutcome,
                    commandSequence,
                    0,
                    0),
                new CommandAcceptedFact(sequence, Source, "test.command"))]);
    }

    private static GameFactEnvelope Envelope(ulong sequence)
    {
        var commandSequence = new CommandSequence(sequence);
        return new GameFactEnvelope(
            new GameFactSequence(sequence),
            new SimulationTime(sequence),
            new CommandFactCause(commandSequence),
            new CommandAcceptedFact(commandSequence, Source, "test.command"));
    }
}
