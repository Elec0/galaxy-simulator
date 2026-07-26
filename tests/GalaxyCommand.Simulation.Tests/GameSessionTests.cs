using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionTests
{
    [Fact]
    public void SessionAdvancesBeyondFirstConstructedShip()
    {
        var session = new GameSession();
        var target = new SimulationTime(1_000_000);

        RunReport report = session.AdvanceTo(target);
        PhaseOneSnapshot snapshot = session.CaptureSnapshot();

        Assert.Equal(target, report.EndTime);
        Assert.Equal(target, session.CurrentTime);
        Assert.Equal(target, snapshot.Time);
        Assert.Equal(3, snapshot.Ships.Count);
        Assert.Contains(snapshot.Ships, ship => ship.Id == new ShipId(3));
        Assert.Empty(snapshot.Constructions);
    }

    [Fact]
    public void SessionKeepsDeterministicStateAcrossIncrementalAdvancement()
    {
        var singleRun = new GameSession();
        var incremental = new GameSession();
        var midpoint = new SimulationTime(700_000);
        var target = new SimulationTime(1_000_000);

        singleRun.AdvanceTo(target);
        incremental.AdvanceTo(midpoint);
        incremental.AdvanceTo(target);

        PhaseOneSnapshot expected = singleRun.CaptureSnapshot();
        PhaseOneSnapshot actual = incremental.CaptureSnapshot();
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Locations, actual.Locations);
        Assert.Equal(expected.Routes, actual.Routes);
        Assert.Equal(expected.Ships, actual.Ships);
        Assert.Equal(expected.Constructions, actual.Constructions);
        Assert.Equal(singleRun.EventRecords, incremental.EventRecords);
        Assert.Equal(singleRun.DecisionRecords, incremental.DecisionRecords);
    }

    [Fact]
    public void UnsupportedCommandIsRejectedAndRecordedAtCurrentTime()
    {
        var session = new GameSession();
        var now = new SimulationTime(50_000);
        session.AdvanceTo(now);
        var source = new CommandSource(
            CommandSourceKind.Player,
            new CommandSourceId("local-player"));

        GameplayCommandRecord record = session.SubmitCommand(
            source,
            new TestCommand());

        Assert.Equal(now, record.Envelope.SubmittedAt);
        Assert.Equal<ulong>(1, record.Envelope.Sequence.Value);
        Assert.Equal(CommandResultStatus.Rejected, record.Result.Status);
        Assert.Equal(
            CommandRejectionCodes.UnsupportedCommand,
            record.Result.RejectionCode);
        Assert.Equal([record], session.CommandRecords);
    }

    [Fact]
    public void SessionDoesNotExposeMutableWorld()
    {
        Assert.Null(typeof(GameSession).GetProperty("World"));
    }

    private sealed record TestCommand : GameplayCommand
    {
        public TestCommand()
            : base("test.unsupported")
        {
        }
    }
}
