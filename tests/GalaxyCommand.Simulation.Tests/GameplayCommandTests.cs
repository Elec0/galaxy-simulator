using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameplayCommandTests
{
    [Fact]
    public void ProcessorAssignsDeterministicOrderAndRecordsEveryResult()
    {
        var handler = new RecordingHandler();
        var processor = new GameplayCommandProcessor(
            handler,
            factRetentionCapacity: 16);
        var player = new CommandSource(
            CommandSourceKind.Player,
            new CommandSourceId("local-player"));

        GameplayCommandRecord accepted = processor.Submit(
            new SimulationTime(25),
            player,
            new TestCommand("accepted"));
        GameplayCommandRecord rejected = processor.Submit(
            new SimulationTime(25),
            player,
            new TestCommand("rejected"));

        Assert.Equal<ulong>(1, accepted.Envelope.Sequence.Value);
        Assert.Equal<ulong>(2, rejected.Envelope.Sequence.Value);
        Assert.Equal(new SimulationTime(25), accepted.Envelope.SubmittedAt);
        Assert.Same(player, accepted.Envelope.Source);
        Assert.Equal(CommandResultStatus.Accepted, accepted.Result.Status);
        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidState, rejected.Result.RejectionCode);
        Assert.Equal("The test state rejects this command.", rejected.Result.Reason);
        Assert.Equal([accepted, rejected], processor.Records);
        Assert.Equal(
            [accepted.Envelope, rejected.Envelope],
            handler.Handled);
        Assert.Collection(
            processor.ReadFactsAfter(null, maximumCount: 10).Facts,
            fact =>
            {
                Assert.Equal<ulong>(1, fact.Sequence.Value);
                Assert.IsType<CommandAcceptedFact>(fact.Fact);
            },
            fact =>
            {
                Assert.Equal<ulong>(2, fact.Sequence.Value);
                var rejectedFact = Assert.IsType<CommandRejectedFact>(
                    fact.Fact);
                Assert.Equal(
                    CommandRejectionCodes.InvalidState,
                    rejectedFact.RejectionCode);
            });
    }

    [Theory]
    [InlineData(CommandSourceKind.Player, "local-player")]
    [InlineData(CommandSourceKind.Autonomous, "organization:4")]
    [InlineData(CommandSourceKind.Dialogue, "intro-contact")]
    [InlineData(CommandSourceKind.Script, "tutorial-01")]
    public void SourceKindsCarryOpaqueStableAttribution(
        CommandSourceKind kind,
        string id)
    {
        var source = new CommandSource(kind, new CommandSourceId(id));

        Assert.Equal(kind, source.Kind);
        Assert.Equal(id, source.Id.Value);
    }

    [Fact]
    public void CommandKindSeparatesIntentFromInternalEventTypes()
    {
        var command = new TestCommand("accepted");

        Assert.Equal("test.command", command.Kind);
        Assert.IsNotType<ScheduledEvent<TestCommand>>(command);
    }

    [Fact]
    public void AcceptedResultCannotContainRejectionDetails()
    {
        CommandResult result = CommandResult.Accepted();

        Assert.Equal(CommandResultStatus.Accepted, result.Status);
        Assert.Null(result.RejectionCode);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void OpaqueIdentifiersRejectBlankValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new CommandSourceId(value));
        Assert.Throws<ArgumentException>(() => new CommandRejectionCode(value));
    }

    [Fact]
    public void ProcessorRejectsMissingSubmissionData()
    {
        var processor = new GameplayCommandProcessor(
            new RecordingHandler(),
            factRetentionCapacity: 16);
        var player = new CommandSource(
            CommandSourceKind.Player,
            new CommandSourceId("local-player"));

        Assert.Throws<ArgumentNullException>(() =>
            processor.Submit(SimulationTime.Zero, null!, new TestCommand("accepted")));
        Assert.Throws<ArgumentNullException>(() =>
            processor.Submit(SimulationTime.Zero, player, null!));
        Assert.Empty(processor.Records);
    }

    [Fact]
    public void ContractObjectsRejectDefaultIdentifiers()
    {
        var command = new TestCommand("accepted");
        var player = new CommandSource(
            CommandSourceKind.Player,
            new CommandSourceId("local-player"));

        Assert.Throws<ArgumentNullException>(() =>
            new CommandSource(CommandSourceKind.Player, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameplayCommandEnvelope(
                default,
                SimulationTime.Zero,
                player,
                command));
        Assert.Throws<ArgumentNullException>(() =>
            CommandResult.Rejected(default, "Missing code."));
    }

    [Fact]
    public void ProcessorRejectsSubmissionTimeMovingBackward()
    {
        var processor = new GameplayCommandProcessor(
            new RecordingHandler(),
            factRetentionCapacity: 16);
        var player = new CommandSource(
            CommandSourceKind.Player,
            new CommandSourceId("local-player"));
        processor.Submit(
            new SimulationTime(25),
            player,
            new TestCommand("accepted"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            processor.Submit(
                new SimulationTime(24),
                player,
                new TestCommand("accepted")));
        Assert.Single(processor.Records);
    }

    private sealed record TestCommand : GameplayCommand
    {
        public TestCommand(string outcome)
            : base("test.command")
        {
            Outcome = outcome;
        }

        public string Outcome { get; }
    }

    private sealed class RecordingHandler : IGameplayCommandHandler
    {
        public List<GameplayCommandEnvelope> Handled { get; } = [];

        public GameplayCommandHandlingResult Handle(
            GameplayCommandEnvelope envelope)
        {
            Handled.Add(envelope);
            var command = Assert.IsType<TestCommand>(envelope.Command);
            CommandResult result = command.Outcome == "accepted"
                ? CommandResult.Accepted()
                : CommandResult.Rejected(
                    CommandRejectionCodes.InvalidState,
                    "The test state rejects this command.");
            return new GameplayCommandHandlingResult(result);
        }
    }
}
