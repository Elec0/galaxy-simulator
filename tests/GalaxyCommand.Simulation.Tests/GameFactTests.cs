using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameFactTests
{
    [Fact]
    public void AcceptedMoveCommitsOutcomeOrderAndMotionFactsInSemanticOrder()
    {
        GameSession session = GameSessionTestFixture.Create();

        GameplayCommandRecord command = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));

        GameFactEnvelope[] facts = ReadAll(session);
        Assert.Collection(
            facts,
            accepted =>
            {
                Assert.Equal<ulong>(1, accepted.Sequence.Value);
                Assert.Equal(SimulationTime.Zero, accepted.Timestamp);
                var cause = Assert.IsType<CommandFactCause>(
                    accepted.Cause);
                Assert.Equal(command.Envelope.Sequence, cause.Sequence);
                var fact = Assert.IsType<CommandAcceptedFact>(accepted.Fact);
                Assert.Equal(command.Envelope.Sequence, fact.CommandSequence);
                Assert.Equal(GameSessionTestFixture.Player, fact.Source);
                Assert.Equal(MoveShipCommand.CommandKind, fact.CommandKind);
            },
            transitioned =>
            {
                Assert.Equal<ulong>(2, transitioned.Sequence.Value);
                var fact = Assert.IsType<ShipOrderTransitionFact>(
                    transitioned.Fact);
                Assert.Equal(GameSessionTestFixture.Ship, fact.ShipId);
                Assert.Equal(new ShipOrderId(1), fact.OrderId);
                Assert.Null(fact.PreviousStatus);
                Assert.Equal(ShipOrderStatus.Active, fact.NextStatus);
                Assert.Equal(
                    ShipOrderReason.MovingToDestination,
                    fact.Reason);
            },
            started =>
            {
                Assert.Equal<ulong>(3, started.Sequence.Value);
                var fact = Assert.IsType<ShipLocalMotionStartedFact>(
                    started.Fact);
                Assert.Equal(GameSessionTestFixture.Ship, fact.ShipId);
                Assert.Equal(new MotionId(1), fact.Motion.Id);
                Assert.Equal(new ShipOrderId(1), fact.OrderId);
                Assert.Equal(
                    GameSessionTestFixture.Position(0, 0),
                    fact.Motion.Origin);
                Assert.Equal(
                    GameSessionTestFixture.Position(100, 0),
                    fact.Motion.Destination);
            });
    }

    [Fact]
    public void RejectedCommandCommitsOnlyCorrelatedOutcomeFact()
    {
        GameSession session = GameSessionTestFixture.Create();

        GameplayCommandRecord command = session.SubmitCommand(
            new CommandSource(
                CommandSourceKind.Autonomous,
                new CommandSourceId("not-controller")),
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));

        GameFactEnvelope envelope = Assert.Single(ReadAll(session));
        var cause = Assert.IsType<CommandFactCause>(envelope.Cause);
        Assert.Equal(command.Envelope.Sequence, cause.Sequence);
        var fact = Assert.IsType<CommandRejectedFact>(envelope.Fact);
        Assert.Equal(command.Envelope.Sequence, fact.CommandSequence);
        Assert.Equal(CommandRejectionCodes.InvalidSource, fact.RejectionCode);
        Assert.Null(
            Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder);
    }

    [Fact]
    public void ArrivalCommitsPhysicalEndBeforeOrderCompletion()
    {
        GameSession session = GameSessionTestFixture.Create();
        SubmitMove(session, 100, 0);

        session.AdvanceTo(new SimulationTime(100));

        GameFactEnvelope[] facts = ReadAll(session);
        Assert.Equal(5, facts.Length);
        GameEventRecord movementEvent = Assert.Single(session.EventRecords);
        var endedEnvelope = facts[3];
        var completedEnvelope = facts[4];
        var cause = Assert.IsType<ScheduledEventFactCause>(
            endedEnvelope.Cause);
        Assert.Equal(
            new EventKey(
                movementEvent.Timestamp,
                movementEvent.Phase,
                movementEvent.CreationSequence),
            cause.Key);
        Assert.Equal(endedEnvelope.Cause, completedEnvelope.Cause);

        var ended = Assert.IsType<ShipLocalMotionEndedFact>(
            endedEnvelope.Fact);
        Assert.Equal(LocalMotionEndReason.Arrived, ended.Reason);
        Assert.Equal(new SimulationTime(100), ended.EndedAt);
        Assert.Equal(
            GameSessionTestFixture.Position(100, 0),
            ended.FinalPosition);

        var completed = Assert.IsType<ShipOrderTransitionFact>(
            completedEnvelope.Fact);
        Assert.Equal(ShipOrderStatus.Active, completed.PreviousStatus);
        Assert.Equal(ShipOrderStatus.Completed, completed.NextStatus);
        Assert.Equal(
            ShipOrderReason.DestinationReached,
            completed.Reason);
    }

    [Fact]
    public void CancellationCommitsOutcomeThenMaterializationThenOrderTransition()
    {
        GameSession session = GameSessionTestFixture.Create();
        SubmitMove(session, 100, 0);
        session.AdvanceTo(new SimulationTime(25));

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new CancelShipOrderCommand(
                GameSessionTestFixture.Ship,
                new ShipOrderId(1)));

        GameFactEnvelope[] facts = ReadAll(session);
        Assert.Equal(6, facts.Length);
        Assert.IsType<CommandAcceptedFact>(facts[3].Fact);
        var ended = Assert.IsType<ShipLocalMotionEndedFact>(
            facts[4].Fact);
        Assert.Equal(LocalMotionEndReason.CancelledByCommand, ended.Reason);
        Assert.Equal(
            GameSessionTestFixture.Position(25, 0),
            ended.FinalPosition);
        var cancelled = Assert.IsType<ShipOrderTransitionFact>(
            facts[5].Fact);
        Assert.Equal(ShipOrderStatus.Active, cancelled.PreviousStatus);
        Assert.Equal(ShipOrderStatus.Cancelled, cancelled.NextStatus);
        Assert.Equal(
            ShipOrderReason.CancelledByCommand,
            cancelled.Reason);
    }

    [Fact]
    public void ReplacedMotionEndsBeforeOldAndNewOrderTransitionsAndNewMotion()
    {
        GameSession session = GameSessionTestFixture.Create();
        SubmitMove(session, 100, 0);
        session.AdvanceTo(new SimulationTime(50));

        SubmitMove(session, 50, 100);

        GameFactEnvelope[] replacement = session.ReadFactsAfter(
            new GameFactSequence(3),
            maximumCount: 10).Facts.ToArray();
        Assert.Collection(
            replacement,
            fact => Assert.IsType<CommandAcceptedFact>(fact.Fact),
            fact =>
            {
                var ended = Assert.IsType<ShipLocalMotionEndedFact>(
                    fact.Fact);
                Assert.Equal(
                    LocalMotionEndReason.ReplacedByCommand,
                    ended.Reason);
                Assert.Equal(
                    GameSessionTestFixture.Position(50, 0),
                    ended.FinalPosition);
            },
            fact =>
            {
                var cancelled = Assert.IsType<ShipOrderTransitionFact>(
                    fact.Fact);
                Assert.Equal(new ShipOrderId(1), cancelled.OrderId);
                Assert.Equal(
                    ShipOrderStatus.Cancelled,
                    cancelled.NextStatus);
            },
            fact =>
            {
                var created = Assert.IsType<ShipOrderTransitionFact>(
                    fact.Fact);
                Assert.Equal(new ShipOrderId(2), created.OrderId);
                Assert.Null(created.PreviousStatus);
                Assert.Equal(ShipOrderStatus.Active, created.NextStatus);
            },
            fact =>
            {
                var started = Assert.IsType<ShipLocalMotionStartedFact>(
                    fact.Fact);
                Assert.Equal(new ShipOrderId(2), started.OrderId);
                Assert.Equal(
                    GameSessionTestFixture.Position(50, 0),
                    started.Motion.Origin);
            });
    }

    [Fact]
    public void EndingOverrideCancelsOverrideOrderBeforeRestoringBaseOrder()
    {
        GameSession session = GameSessionTestFixture.Create();
        SubmitMove(session, 100, 0);
        session.AdvanceTo(new SimulationTime(25));
        var script = new CommandSource(
            CommandSourceKind.Script,
            new CommandSourceId("fact-ordering-script"));
        session.SubmitCommand(
            script,
            new BeginScriptedOverrideCommand(
                GameSessionTestFixture.Ship,
                new ActorOverrideReasonId("fact-ordering"),
                default));
        session.SubmitCommand(
            script,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(25, 100),
                OrderPlacement.ReplaceAll));
        GameFactSequence cursor = session.ReadFactsAfter(
            null,
            maximumCount: 256).NewestCommittedSequence!.Value;

        session.SubmitCommand(
            script,
            new EndScriptedOverrideCommand(
                GameSessionTestFixture.Ship,
                ScriptedOverrideReleasePolicy.CancelOutstanding,
                new ActorControlRevision(1)));

        GameFactEnvelope[] ending = session.ReadFactsAfter(
            cursor,
            maximumCount: 10).Facts.ToArray();
        Assert.Collection(
            ending,
            envelope => Assert.IsType<CommandAcceptedFact>(envelope.Fact),
            envelope =>
            {
                var ended = Assert.IsType<ShipLocalMotionEndedFact>(
                    envelope.Fact);
                Assert.Equal(
                    LocalMotionEndReason.ScriptedOverrideEnded,
                    ended.Reason);
                Assert.Equal(new ShipOrderId(2), ended.OrderId);
            },
            envelope =>
            {
                var cancelled = Assert.IsType<ShipOrderTransitionFact>(
                    envelope.Fact);
                Assert.Equal(new ShipOrderId(2), cancelled.OrderId);
                Assert.Equal(
                    ShipOrderReason.ScriptedOverrideEnded,
                    cancelled.Reason);
                Assert.Equal(
                    ShipOrderStatus.Cancelled,
                    cancelled.NextStatus);
            },
            envelope =>
            {
                var restored = Assert.IsType<ShipOrderTransitionFact>(
                    envelope.Fact);
                Assert.Equal(new ShipOrderId(1), restored.OrderId);
                Assert.Equal(
                    ShipOrderStatus.Suspended,
                    restored.PreviousStatus);
                Assert.Equal(ShipOrderStatus.Active, restored.NextStatus);
            },
            envelope =>
            {
                var started = Assert.IsType<ShipLocalMotionStartedFact>(
                    envelope.Fact);
                Assert.Equal(new ShipOrderId(1), started.OrderId);
            });
    }

    [Fact]
    public void IgnoredStaleEventEmitsNoSemanticFact()
    {
        GameSession session = GameSessionTestFixture.Create();
        SubmitMove(session, 100, 0);
        session.AdvanceTo(new SimulationTime(50));
        SubmitMove(session, 50, 100);
        GameFactEnvelope[] beforeStaleEvent = ReadAll(session);

        session.AdvanceTo(new SimulationTime(100));

        Assert.Equal(beforeStaleEvent, ReadAll(session));
        Assert.Equal(
            ScheduledEventDisposition.IgnoredStaleGeneration,
            Assert.Single(session.EventRecords).Disposition);
    }

    [Fact]
    public void BoundedReadsReportEvictedCursorRanges()
    {
        GameSession session = GameSessionTestFixture.Create(
            factRetentionCapacity: 3);
        SubmitMove(session, 100, 0);
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new UnsupportedTestCommand());
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new UnsupportedTestCommand());

        GameFactReadResult missingBeginning = session.ReadFactsAfter(
            sequence: null,
            maximumCount: 10);
        Assert.True(missingBeginning.CursorGap);
        Assert.Equal(new GameFactSequence(3), missingBeginning.OldestRetainedSequence);
        Assert.Equal(new GameFactSequence(5), missingBeginning.NewestCommittedSequence);
        Assert.Equal(
            [3UL, 4UL, 5UL],
            missingBeginning.Facts.Select(fact => fact.Sequence.Value));

        GameFactReadResult missingAfterOne = session.ReadFactsAfter(
            new GameFactSequence(1),
            maximumCount: 2);
        Assert.True(missingAfterOne.CursorGap);
        Assert.Equal(
            [3UL, 4UL],
            missingAfterOne.Facts.Select(fact => fact.Sequence.Value));

        GameFactReadResult completeSuffix = session.ReadFactsAfter(
            new GameFactSequence(2),
            maximumCount: 10);
        Assert.False(completeSuffix.CursorGap);
        Assert.Equal(
            [3UL, 4UL, 5UL],
            completeSuffix.Facts.Select(fact => fact.Sequence.Value));
    }

    [Fact]
    public void FactHistoryIsDeterministicAcrossIncrementalAdvancement()
    {
        GameSession singleRun = GameSessionTestFixture.Create();
        GameSession incremental = GameSessionTestFixture.Create();
        SubmitMove(singleRun, 100, 0);
        SubmitMove(incremental, 100, 0);

        singleRun.AdvanceTo(new SimulationTime(100));
        incremental.AdvanceTo(new SimulationTime(25));
        incremental.AdvanceTo(new SimulationTime(75));
        incremental.AdvanceTo(new SimulationTime(100));

        Assert.Equal(ReadAll(singleRun), ReadAll(incremental));
    }

    private static void SubmitMove(
        GameSession session,
        long x,
        long y)
    {
        GameplayCommandRecord record = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(x, y),
                OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
    }

    private static GameFactEnvelope[] ReadAll(GameSession session) =>
        session.ReadFactsAfter(
            sequence: null,
            maximumCount: 256).Facts.ToArray();

    private sealed record UnsupportedTestCommand : GameplayCommand
    {
        internal UnsupportedTestCommand()
            : base("test.unsupported-fact-command")
        {
        }
    }
}
