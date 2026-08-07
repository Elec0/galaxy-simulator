using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ActorControlAndOrderTests
{
    [Fact]
    public void InitialSnapshotExposesBaseAndActiveController()
    {
        GameShipSnapshot ship = Assert.Single(
            GameSessionTestFixture.Create().CaptureSnapshot().Ships);

        Assert.Equal(GameSessionTestFixture.PlayerController, ship.Control.BaseController);
        Assert.Equal(GameSessionTestFixture.PlayerController, ship.Control.ActiveController);
        Assert.Null(ship.Control.TemporaryOverride);
        Assert.Null(ship.Control.TemporaryOverrideReason);
        Assert.Equal(default, ship.Control.Revision);
        Assert.Empty(ship.QueuedOrders);
        Assert.Empty(ship.SuspendedOrders);
    }

    [Fact]
    public void AutonomousBaseControllerUsesTheSameMoveOrder()
    {
        var autonomousSource = new CommandSource(
            CommandSourceKind.Autonomous,
            new CommandSourceId("autonomy:1"));
        var autonomousController = new ActorController(
            ActorControllerKind.Autonomous,
            autonomousSource.Id);
        GameSession session = GameSessionTestFixture.Create(autonomousController);

        GameplayCommandRecord record = session.SubmitCommand(
            autonomousSource,
            MoveTo(100, 0, OrderPlacement.ReplaceAll));

        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
        ShipOrderSnapshot order = Assert.Single(session.CaptureSnapshot().Ships)
            .CurrentOrder!;
        Assert.Equal(autonomousSource, order.Source);
        Assert.Equal(ShipOrderStatus.Active, order.Status);
    }

    [Fact]
    public void DifferentPlayerIdentityCannotControlShip()
    {
        var otherPlayer = new CommandSource(
            CommandSourceKind.Player,
            new CommandSourceId("other-player"));
        GameSession session = GameSessionTestFixture.Create();

        GameplayCommandRecord record = session.SubmitCommand(
            otherPlayer,
            MoveTo(100, 0, OrderPlacement.ReplaceAll));

        Assert.Equal(CommandResultStatus.Rejected, record.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidSource, record.Result.RejectionCode);
        Assert.Null(Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder);
    }

    [Fact]
    public void ScriptCannotBePersistentBaseController()
    {
        var scriptController = new ActorController(
            ActorControllerKind.Script,
            new CommandSourceId("story:setup"));

        Assert.Throws<ArgumentException>(() =>
            new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                scriptController));
    }

    [Fact]
    public void ScriptedOverrideCommandsRequireExplicitReasonAndReleasePolicy()
    {
        Assert.Throws<ArgumentException>(() =>
            new ActorOverrideReasonId(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EndScriptedOverrideCommand(
                GameSessionTestFixture.Ship,
                (ScriptedOverrideReleasePolicy)99,
                default));
    }

    [Fact]
    public void AppendedOrdersRunInFifoOrder()
    {
        GameSession session = GameSessionTestFixture.Create();
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));
        Submit(session, MoveTo(200, 0, OrderPlacement.Append));

        GameShipSnapshot queued = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipOrderId(1), queued.CurrentOrder?.Id);
        ShipOrderSnapshot second = Assert.Single(queued.QueuedOrders);
        Assert.Equal(new ShipOrderId(2), second.Id);
        Assert.Equal(ShipOrderStatus.Queued, second.Status);

        session.AdvanceTo(new SimulationTime(100));

        GameShipSnapshot promoted = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipOrderId(2), promoted.CurrentOrder?.Id);
        Assert.Equal(ShipOrderStatus.Active, promoted.CurrentOrder?.Status);
        Assert.Equal(GameSessionTestFixture.Position(100, 0), promoted.Motion?.Origin);
        Assert.Equal(GameSessionTestFixture.Position(200, 0), promoted.Motion?.Destination);

        session.AdvanceTo(new SimulationTime(200));

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipOrderId(2), completed.CurrentOrder?.Id);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.Equal(GameSessionTestFixture.Position(200, 0), completed.Position);
    }

    [Fact]
    public void CancellingQueuedOrderLeavesActiveMotionUntouched()
    {
        GameSession session = GameSessionTestFixture.Create();
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));
        Submit(session, MoveTo(200, 0, OrderPlacement.Append));
        LocalMotionSnapshot before = Assert.Single(session.CaptureSnapshot().Ships).Motion!;

        GameplayCommandRecord cancellation = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new CancelShipOrderCommand(
                GameSessionTestFixture.Ship,
                new ShipOrderId(2)));

        Assert.Equal(CommandResultStatus.Accepted, cancellation.Result.Status);
        GameShipSnapshot after = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(before, after.Motion);
        Assert.Equal(new ShipOrderId(1), after.CurrentOrder?.Id);
        Assert.Empty(after.QueuedOrders);
    }

    [Fact]
    public void CancellingActiveOrderPromotesQueueAtSameTimestamp()
    {
        GameSession session = GameSessionTestFixture.Create();
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));
        Submit(session, MoveTo(200, 0, OrderPlacement.Append));
        session.AdvanceTo(new SimulationTime(25));

        GameplayCommandRecord cancellation = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new CancelShipOrderCommand(
                GameSessionTestFixture.Ship,
                new ShipOrderId(1)));

        Assert.Equal(CommandResultStatus.Accepted, cancellation.Result.Status);
        GameShipSnapshot promoted = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipOrderId(2), promoted.CurrentOrder?.Id);
        Assert.Equal(GameSessionTestFixture.Position(25, 0), promoted.Motion?.Origin);
        Assert.Equal(new SimulationTime(25), promoted.Motion?.DepartedAt);
        Assert.Empty(promoted.QueuedOrders);
    }

    [Fact]
    public void ReplaceAllDiscardsActiveAndQueuedWork()
    {
        GameSession session = GameSessionTestFixture.Create();
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));
        Submit(session, MoveTo(200, 0, OrderPlacement.Append));
        session.AdvanceTo(new SimulationTime(25));

        Submit(session, MoveTo(25, 100, OrderPlacement.ReplaceAll));

        GameShipSnapshot replaced = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipOrderId(3), replaced.CurrentOrder?.Id);
        Assert.Empty(replaced.QueuedOrders);
        Assert.Equal(GameSessionTestFixture.Position(25, 0), replaced.Motion?.Origin);
        Assert.Equal(GameSessionTestFixture.Position(25, 100), replaced.Motion?.Destination);
    }

    [Fact]
    public void MultiLegPlanContinuesWithoutCompletingOrderAtWaypoint()
    {
        GameSession session = GameSessionTestFixture.Create(
            navigation: new TwoLegPlanner());
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));

        session.AdvanceTo(new SimulationTime(50));

        GameShipSnapshot waypoint = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(50, 0), waypoint.Position);
        Assert.Equal(ShipOrderStatus.Active, waypoint.CurrentOrder?.Status);
        Assert.Equal(GameSessionTestFixture.Position(50, 0), waypoint.Motion?.Origin);
        Assert.Equal(GameSessionTestFixture.Position(100, 0), waypoint.Motion?.Destination);

        session.AdvanceTo(new SimulationTime(100));

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(100, 0), completed.Position);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.Equal(2, session.EventRecords.Count);
    }

    [Fact]
    public void ScriptedOverrideSuspendsAndRestoresBaseWork()
    {
        GameSession session = GameSessionTestFixture.Create();
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));
        Submit(session, MoveTo(200, 0, OrderPlacement.Append));
        session.AdvanceTo(new SimulationTime(25));
        var script = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("story:intro"));

        GameplayCommandRecord begin = session.SubmitCommand(
            script,
            BeginOverride(default));

        Assert.Equal(CommandResultStatus.Accepted, begin.Result.Status);
        GameShipSnapshot overridden = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(script.Id, overridden.Control.ActiveController.Id);
        Assert.Equal(ActorControllerKind.Script, overridden.Control.ActiveController.Kind);
        Assert.Equal(OverrideReason, overridden.Control.TemporaryOverrideReason);
        Assert.Equal(new ActorControlRevision(1), overridden.Control.Revision);
        Assert.Null(overridden.CurrentOrder);
        Assert.Null(overridden.Motion);
        Assert.Collection(
            overridden.SuspendedOrders,
            active =>
            {
                Assert.Equal(new ShipOrderId(1), active.Id);
                Assert.Equal(ShipOrderStatus.Suspended, active.Status);
            },
            queued =>
            {
                Assert.Equal(new ShipOrderId(2), queued.Id);
                Assert.Equal(ShipOrderStatus.Queued, queued.Status);
            });

        GameplayCommandRecord baseRejected = session.SubmitCommand(
            GameSessionTestFixture.Player,
            MoveTo(300, 0, OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Rejected, baseRejected.Result.Status);
        Assert.Equal(
            CommandRejectionCodes.ActorOverridden,
            baseRejected.Result.RejectionCode);

        Submit(session, script, MoveTo(25, 100, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(75));

        GameplayCommandRecord end = session.SubmitCommand(
            script,
            EndOverride(new ActorControlRevision(1)));

        Assert.Equal(CommandResultStatus.Accepted, end.Result.Status);
        GameShipSnapshot restored = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.PlayerController, restored.Control.ActiveController);
        Assert.Null(restored.Control.TemporaryOverride);
        Assert.Null(restored.Control.TemporaryOverrideReason);
        Assert.Equal(new ActorControlRevision(2), restored.Control.Revision);
        Assert.Equal(new ShipOrderId(1), restored.CurrentOrder?.Id);
        Assert.Equal(ShipOrderStatus.Active, restored.CurrentOrder?.Status);
        Assert.Equal(GameSessionTestFixture.Position(25, 50), restored.Motion?.Origin);
        Assert.Equal(GameSessionTestFixture.Position(100, 0), restored.Motion?.Destination);
        Assert.Single(restored.QueuedOrders);
        Assert.Empty(restored.SuspendedOrders);
    }

    [Fact]
    public void ScriptedOverridesCannotNestOrBeReleasedByAnotherScript()
    {
        GameSession session = GameSessionTestFixture.Create();
        var first = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("story:first"));
        var second = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("story:second"));
        Submit(
            session,
            first,
            BeginOverride(default));

        GameplayCommandRecord nested = session.SubmitCommand(
            second,
            BeginOverride(new ActorControlRevision(1)));
        GameplayCommandRecord wrongRelease = session.SubmitCommand(
            second,
            EndOverride(new ActorControlRevision(1)));

        Assert.Equal(CommandResultStatus.Rejected, nested.Result.Status);
        Assert.Equal(CommandRejectionCodes.Conflict, nested.Result.RejectionCode);
        Assert.Equal(CommandResultStatus.Rejected, wrongRelease.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidSource, wrongRelease.Result.RejectionCode);
        Assert.Equal(
            first.Id,
            Assert.Single(session.CaptureSnapshot().Ships).Control.ActiveController.Id);
    }

    [Fact]
    public void StaleControlRevisionCannotBeginOrEndOverride()
    {
        GameSession session = GameSessionTestFixture.Create();
        var script = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("story:revision"));

        GameplayCommandRecord staleBegin = session.SubmitCommand(
            script,
            BeginOverride(new ActorControlRevision(1)));
        Assert.Equal(CommandResultStatus.Rejected, staleBegin.Result.Status);
        Assert.Equal(
            CommandRejectionCodes.StaleControlRevision,
            staleBegin.Result.RejectionCode);
        Assert.Null(
            Assert.Single(session.CaptureSnapshot().Ships).Control.TemporaryOverride);

        Submit(
            session,
            script,
            BeginOverride(default));
        GameplayCommandRecord staleEnd = session.SubmitCommand(
            script,
            EndOverride(default));

        Assert.Equal(CommandResultStatus.Rejected, staleEnd.Result.Status);
        Assert.Equal(
            CommandRejectionCodes.StaleControlRevision,
            staleEnd.Result.RejectionCode);
        Assert.Equal(
            script.Id,
            Assert.Single(session.CaptureSnapshot().Ships).Control.ActiveController.Id);
    }

    [Fact]
    public void QueueAndOverrideSequenceIsIncrementallyDeterministic()
    {
        (
            GameSnapshot Snapshot,
            GameplayCommandRecord[] Commands,
            GameEventRecord[] Events,
            GameFactEnvelope[] Facts)
            singleRun = RunOverrideSequence(incremental: false);
        (
            GameSnapshot Snapshot,
            GameplayCommandRecord[] Commands,
            GameEventRecord[] Events,
            GameFactEnvelope[] Facts)
            incremental = RunOverrideSequence(incremental: true);

        Assert.Equal(singleRun.Snapshot.Time, incremental.Snapshot.Time);
        Assert.Equal(singleRun.Snapshot.Systems, incremental.Snapshot.Systems);
        Assert.Equal(singleRun.Snapshot.Ships.Count, incremental.Snapshot.Ships.Count);
        foreach ((GameShipSnapshot expected, GameShipSnapshot actual) in
            singleRun.Snapshot.Ships.Zip(incremental.Snapshot.Ships))
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Motion, actual.Motion);
            Assert.Equal(expected.Control, actual.Control);
            Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
            Assert.Equal(expected.QueuedOrders, actual.QueuedOrders);
            Assert.Equal(expected.SuspendedOrders, actual.SuspendedOrders);
        }

        Assert.Equal(singleRun.Commands, incremental.Commands);
        Assert.Equal(singleRun.Events, incremental.Events);
        Assert.Equal(singleRun.Facts, incremental.Facts);
    }

    private static MoveShipCommand MoveTo(
        long x,
        long y,
        OrderPlacement placement) =>
        new(
            GameSessionTestFixture.Ship,
            GameSessionTestFixture.Destination(x, y),
            placement);

    private static ActorOverrideReasonId OverrideReason =>
        new("scripted-sequence");

    private static BeginScriptedOverrideCommand BeginOverride(
        ActorControlRevision expectedRevision) =>
        new(
            GameSessionTestFixture.Ship,
            OverrideReason,
            expectedRevision);

    private static EndScriptedOverrideCommand EndOverride(
        ActorControlRevision expectedRevision) =>
        new(
            GameSessionTestFixture.Ship,
            ScriptedOverrideReleasePolicy.CancelOutstanding,
            expectedRevision);

    private static GameplayCommandRecord Submit(
        GameSession session,
        GameplayCommand command) =>
        Submit(session, GameSessionTestFixture.Player, command);

    private static GameplayCommandRecord Submit(
        GameSession session,
        CommandSource source,
        GameplayCommand command)
    {
        GameplayCommandRecord record = session.SubmitCommand(source, command);
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
        return record;
    }

    private static (
        GameSnapshot Snapshot,
        GameplayCommandRecord[] Commands,
        GameEventRecord[] Events,
        GameFactEnvelope[] Facts)
        RunOverrideSequence(bool incremental)
    {
        GameSession session = GameSessionTestFixture.Create();
        var script = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("story:determinism"));
        Submit(session, MoveTo(100, 0, OrderPlacement.ReplaceAll));
        Submit(session, MoveTo(200, 0, OrderPlacement.Append));
        if (incremental)
        {
            session.AdvanceTo(new SimulationTime(10));
        }

        session.AdvanceTo(new SimulationTime(25));
        Submit(
            session,
            script,
            BeginOverride(default));
        Submit(session, script, MoveTo(25, 100, OrderPlacement.ReplaceAll));
        if (incremental)
        {
            session.AdvanceTo(new SimulationTime(50));
        }

        session.AdvanceTo(new SimulationTime(75));
        Submit(
            session,
            script,
            EndOverride(new ActorControlRevision(1)));
        if (incremental)
        {
            session.AdvanceTo(new SimulationTime(150));
            session.AdvanceTo(new SimulationTime(225));
        }

        session.AdvanceTo(new SimulationTime(300));
        return (
            session.CaptureSnapshot(),
            [.. session.CommandRecords],
            [.. session.EventRecords],
            [.. session.ReadFactsAfter(null, maximumCount: 256).Facts]);
    }

    private sealed class TwoLegPlanner : ISpatialNavigationPlanner
    {
        public NavigationPlanResult Plan(NavigationRequest request)
        {
            var destination = Assert.IsType<NavigationDestination.Position>(
                request.Destination);
            long midpointX = request.Origin.Position.X.Units
                + ((destination.Value.Position.X.Units
                    - request.Origin.Position.X.Units) / 2);
            var midpoint = new SystemPosition(
                request.Origin.SystemId,
                new SpatialPosition(
                    new SpatialCoordinate(midpointX),
                    request.Origin.Position.Y));
            var duration = new SimulationDuration(50);
            return new NavigationPlanResult.Planned(
                new TravelPlan(
                    request.Destination,
                    [
                        new TravelLeg.Local(request.Origin, midpoint, duration),
                        new TravelLeg.Local(midpoint, destination.Value, duration),
                    ]));
        }
    }
}
