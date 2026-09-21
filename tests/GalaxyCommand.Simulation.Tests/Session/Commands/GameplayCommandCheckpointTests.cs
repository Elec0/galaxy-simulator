using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameplayCommandCheckpointTests
{
    private static readonly CommandSource Source = new(
        CommandSourceKind.Player,
        new CommandSourceId("checkpoint-test"));

    [Fact]
    public void RestoreContinuesAdmissionWithoutRestoringDiagnosticRecords()
    {
        var originalFacts = new GameFactStore(capacity: 8);
        var original = new GameplayCommandProcessor(
            new AcceptingHandler(),
            originalFacts);
        original.Submit(
            new SimulationTime(25),
            Source,
            new TestCommand());

        CommandAdmissionCheckpoint checkpoint = original.CaptureCheckpoint();
        CheckpointResult<GameFactStore> factResult =
            GameFactStore.RestoreCheckpoint(originalFacts.CaptureCheckpoint());
        CheckpointResult<GameplayCommandProcessor> result =
            GameplayCommandProcessor.RestoreCheckpoint(
                checkpoint,
                new AcceptingHandler(),
                Assert.IsType<GameFactStore>(factResult.Value));

        Assert.True(result.IsSuccess);
        GameplayCommandProcessor restored =
            Assert.IsType<GameplayCommandProcessor>(result.Value);
        Assert.Empty(restored.Records);

        GameplayCommandRecord continued = restored.Submit(
            new SimulationTime(25),
            Source,
            new TestCommand());

        Assert.Equal<ulong>(2, continued.Envelope.Sequence.Value);
        Assert.Equal(
            [1UL, 2UL],
            restored.ReadFactsAfter(null, 10).Facts
                .Select(fact => fact.Sequence.Value));
    }

    [Fact]
    public void RestorePreservesLastAdmittedTime()
    {
        var checkpoint = new CommandAdmissionCheckpoint(
            new IdSequenceCheckpoint(2),
            new SimulationTime(25));
        CheckpointResult<GameplayCommandProcessor> result =
            GameplayCommandProcessor.RestoreCheckpoint(
                checkpoint,
                new AcceptingHandler(),
                new GameFactStore(capacity: 8));

        GameplayCommandProcessor restored =
            Assert.IsType<GameplayCommandProcessor>(result.Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => restored.Submit(
            new SimulationTime(24),
            Source,
            new TestCommand()));
    }

    [Fact]
    public void RestorePreservesSequenceExhaustion()
    {
        var checkpoint = new CommandAdmissionCheckpoint(
            new IdSequenceCheckpoint(null),
            new SimulationTime(25));
        CheckpointResult<GameplayCommandProcessor> result =
            GameplayCommandProcessor.RestoreCheckpoint(
                checkpoint,
                new AcceptingHandler(),
                new GameFactStore(capacity: 8));

        GameplayCommandProcessor restored =
            Assert.IsType<GameplayCommandProcessor>(result.Value);

        Assert.Throws<InvalidOperationException>(() => restored.Submit(
            new SimulationTime(25),
            Source,
            new TestCommand()));
    }

    [Theory]
    [InlineData(1UL, 25L)]
    [InlineData(2UL, null)]
    public void RestoreRejectsInconsistentAdmissionProgress(
        ulong nextSequence,
        long? lastSubmittedAt)
    {
        var checkpoint = new CommandAdmissionCheckpoint(
            new IdSequenceCheckpoint(nextSequence),
            lastSubmittedAt is { } value
                ? new SimulationTime(checked((ulong)value))
                : null);

        CheckpointResult<GameplayCommandProcessor> result =
            GameplayCommandProcessor.RestoreCheckpoint(
                checkpoint,
                new AcceptingHandler(),
                new GameFactStore(capacity: 8));

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.commandAdmission", result.Failure?.Path);
    }

    private sealed record TestCommand() : GameplayCommand("test.command");

    private sealed class AcceptingHandler : IGameplayCommandHandler
    {
        public GameplayCommandHandlingResult Handle(
            GameplayCommandEnvelope envelope) =>
            new(CommandResult.Accepted());
    }
}
