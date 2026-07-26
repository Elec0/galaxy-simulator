using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionTests
{
    [Fact]
    public void MoveOrderAdvancesAndCompletesThroughSession()
    {
        GameSession session = GameSessionTestFixture.Create();
        NavigationDestination destination = GameSessionTestFixture.Destination(100, 50);

        GameplayCommandRecord command = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship, destination));

        Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);
        GameShipSnapshot active = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Active, active.CurrentOrder?.Status);
        Assert.Equal(ShipOrderReason.MovingToDestination, active.CurrentOrder?.Reason);
        Assert.Equal(destination, active.CurrentOrder?.Destination);
        Assert.NotNull(active.Motion);

        session.AdvanceTo(new SimulationTime(100));

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(100, 50), completed.Position);
        Assert.Null(completed.Motion);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.Equal(ShipOrderReason.DestinationReached, completed.CurrentOrder?.Reason);
        GameEventRecord movement = Assert.Single(session.EventRecords);
        Assert.Equal(ScheduledEventDisposition.Applied, movement.Disposition);
    }

    [Fact]
    public void ReplacementStartsAtMaterializedCurrentPosition()
    {
        GameSession session = GameSessionTestFixture.Create();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0)));
        session.AdvanceTo(new SimulationTime(50));

        GameplayCommandRecord replacement = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(50, 100)));

        Assert.Equal(CommandResultStatus.Accepted, replacement.Result.Status);
        GameShipSnapshot replaced = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(50, 0), replaced.Motion?.Origin);
        Assert.Equal(GameSessionTestFixture.Position(50, 100), replaced.Motion?.Destination);
        Assert.Equal(new ShipOrderId(2), replaced.CurrentOrder?.Id);

        session.AdvanceTo(new SimulationTime(150));

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(50, 100), completed.Position);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.Collection(
            session.EventRecords,
            stale => Assert.Equal(
                ScheduledEventDisposition.IgnoredStaleGeneration,
                stale.Disposition),
            applied => Assert.Equal(
                ScheduledEventDisposition.Applied,
                applied.Disposition));
    }

    [Fact]
    public void CancellationMaterializesPositionAndInvalidatesArrival()
    {
        GameSession session = GameSessionTestFixture.Create();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0)));
        session.AdvanceTo(new SimulationTime(25));

        GameplayCommandRecord cancellation = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new CancelShipOrderCommand(GameSessionTestFixture.Ship));

        Assert.Equal(CommandResultStatus.Accepted, cancellation.Result.Status);
        GameShipSnapshot cancelled = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(25, 0), cancelled.Position);
        Assert.Null(cancelled.Motion);
        Assert.Equal(ShipOrderStatus.Cancelled, cancelled.CurrentOrder?.Status);
        Assert.Equal(ShipOrderReason.CancelledByCommand, cancelled.CurrentOrder?.Reason);

        session.AdvanceTo(new SimulationTime(100));

        Assert.Equal(
            GameSessionTestFixture.Position(25, 0),
            Assert.Single(session.CaptureSnapshot().Ships).Position);
        Assert.Equal(
            ScheduledEventDisposition.IgnoredStaleGeneration,
            Assert.Single(session.EventRecords).Disposition);
    }

    [Fact]
    public void SameCommandsRemainDeterministicAcrossIncrementalAdvancement()
    {
        GameSession singleRun = GameSessionTestFixture.Create();
        GameSession incremental = GameSessionTestFixture.Create();

        SubmitMove(singleRun);
        SubmitMove(incremental);
        singleRun.AdvanceTo(new SimulationTime(100));
        incremental.AdvanceTo(new SimulationTime(40));
        incremental.AdvanceTo(new SimulationTime(100));

        AssertSnapshotsEqual(
            singleRun.CaptureSnapshot(),
            incremental.CaptureSnapshot());
        Assert.Equal(singleRun.CommandRecords, incremental.CommandRecords);
        Assert.Equal(singleRun.EventRecords, incremental.EventRecords);
    }

    [Fact]
    public void UnsupportedCommandIsRejectedWithoutMutation()
    {
        GameSession session = GameSessionTestFixture.Create();
        GameSnapshot before = session.CaptureSnapshot();

        GameplayCommandRecord record = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new TestCommand());

        Assert.Equal(CommandResultStatus.Rejected, record.Result.Status);
        Assert.Equal(
            CommandRejectionCodes.UnsupportedCommand,
            record.Result.RejectionCode);
        AssertSnapshotsEqual(before, session.CaptureSnapshot());
        Assert.Empty(session.EventRecords);
    }

    [Fact]
    public void FirstOrderSliceRejectsNonPlayerSources()
    {
        GameSession session = GameSessionTestFixture.Create();
        var source = new CommandSource(
            CommandSourceKind.Autonomous,
            new CommandSourceId("test-autonomy"));

        GameplayCommandRecord record = session.SubmitCommand(
            source,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0)));

        Assert.Equal(CommandResultStatus.Rejected, record.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidSource, record.Result.RejectionCode);
        Assert.Null(Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder);
    }

    [Fact]
    public void UnreachableReplacementIsRejectedWithoutDisturbingActiveOrder()
    {
        GameSession session = GameSessionTestFixture.Create();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0)));
        session.AdvanceTo(new SimulationTime(25));
        GameShipSnapshot before = Assert.Single(session.CaptureSnapshot().Ships);
        var unreachable = new NavigationDestination.Position(
            new SystemPosition(
                new SystemId(2),
                new SpatialPosition(
                    new SpatialCoordinate(0),
                    new SpatialCoordinate(0))));

        GameplayCommandRecord rejected = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship, unreachable));

        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidState, rejected.Result.RejectionCode);
        Assert.Equal(before, Assert.Single(session.CaptureSnapshot().Ships));
    }

    [Fact]
    public void PublicSessionBoundaryDoesNotExposeMutableRuntime()
    {
        Assert.Null(typeof(GameSession).GetProperty("Runtime"));
        Assert.Null(typeof(GameSession).GetProperty("Movement"));
        Assert.Null(typeof(GameSession).GetProperty("World"));
    }

    private static void SubmitMove(GameSession session) =>
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 50)));

    private static void AssertSnapshotsEqual(
        GameSnapshot expected,
        GameSnapshot actual)
    {
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Systems, actual.Systems);
        Assert.Equal(expected.Ships, actual.Ships);
    }

    private sealed record TestCommand : GameplayCommand
    {
        internal TestCommand()
            : base("test.unsupported")
        {
        }
    }
}
