using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShipOrderCheckpointTests
{
    private static readonly ShipId Ship = new(3);
    private static readonly CommandSource Player = new(
        CommandSourceKind.Player,
        new CommandSourceId("player"));
    private static readonly CommandSource Script = new(
        CommandSourceKind.Script,
        new CommandSourceId("script:intro"));

    [Fact]
    public void RestoreContinuesActivePlanAndPreservesQueueAndAllocator()
    {
        var original = new ShipOrderCoordinator();
        original.Add(Ship);
        var transitions = new List<ShipOrderTransition>();
        ShipOrder active = original.Create(Player, Destination(200));
        original.ReplaceAll(Ship, active, transitions);
        TravelPlan plan = TwoLegPlan();
        original.SetPlan(Ship, active.Id, plan, transitions);
        original.BindMotion(Ship, active.Id, new MotionId(7));
        ShipOrder queued = original.Create(Player, Destination(300));
        original.Append(Ship, queued, transitions);

        ShipOrderCoordinatorCheckpoint checkpoint = original.CaptureCheckpoint();
        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint);

        Assert.True(result.IsSuccess);
        ShipOrderCoordinator restored =
            Assert.IsType<ShipOrderCoordinator>(result.Value);
        Assert.Equal(original.CaptureCurrent(Ship), restored.CaptureCurrent(Ship));
        Assert.Equal(original.CaptureQueue(Ship), restored.CaptureQueue(Ship));
        Assert.Equal(plan.Legs[0], restored.NextLeg(Ship, active.Id));

        restored.CompleteLeg(Ship, active.Id, new MotionId(7));

        Assert.Equal(plan.Legs[1], restored.NextLeg(Ship, active.Id));
        Assert.Equal(new ShipOrderId(3), restored.Create(Player, Destination(400)).Id);
    }

    [Fact]
    public void RestorePreservesBaseAndOverrideWorkSets()
    {
        var original = new ShipOrderCoordinator();
        original.Add(Ship);
        var transitions = new List<ShipOrderTransition>();
        ShipOrder baseOrder = original.Create(Player, Destination(100));
        original.ReplaceAll(Ship, baseOrder, transitions);
        original.BeginOverride(Ship, transitions);
        ShipOrder overrideOrder = original.Create(Script, Destination(200));
        original.ReplaceAll(Ship, overrideOrder, transitions);
        original.SetPlan(Ship, overrideOrder.Id, TwoLegPlan(), transitions);
        original.BindMotion(Ship, overrideOrder.Id, new MotionId(8));

        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(original.CaptureCheckpoint());

        ShipOrderCoordinator restored =
            Assert.IsType<ShipOrderCoordinator>(result.Value);
        Assert.Equal(overrideOrder.Id, restored.CaptureCurrent(Ship)?.Id);
        Assert.Equal(baseOrder.Id, Assert.Single(restored.CaptureSuspended(Ship)).Id);

        ShipOrder? resumed = restored.EndOverride(
            Ship,
            ScriptedOverrideReleasePolicy.CancelOutstanding,
            transitions: []);

        Assert.Equal(baseOrder.Id, resumed?.Id);
        Assert.Equal(ShipOrderStatus.Active, restored.CaptureCurrent(Ship)?.Status);
        Assert.Equal(
            ShipOrderReason.ResumingAfterScriptedOverride,
            restored.CaptureCurrent(Ship)?.Reason);
    }

    [Fact]
    public void RestoreAcceptsUnorderedActorsAndCanonicalizesCapture()
    {
        var original = new ShipOrderCoordinator();
        original.Add(new ShipId(3));
        original.Add(new ShipId(9));
        ShipOrderCoordinatorCheckpoint captured = original.CaptureCheckpoint();
        var reordered = new ShipOrderCoordinatorCheckpoint(
            captured.OrderIds,
            captured.Actors.Reverse());

        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(reordered);

        ShipOrderCoordinator restored =
            Assert.IsType<ShipOrderCoordinator>(result.Value);
        Assert.Equal(
            [3UL, 9UL],
            restored.CaptureCheckpoint().Actors.Select(actor => actor!.ShipId.Value));
    }

    [Fact]
    public void RestoreRejectsDuplicateOrderIdentity()
    {
        ShipOrderCheckpoint order = Order(new ShipOrderId(1));
        var work = new ShipOrderWorkSetCheckpoint(
            Active: order,
            Queue: [order],
            LastTerminal: null);
        var checkpoint = new ShipOrderCoordinatorCheckpoint(
            new IdSequenceCheckpoint(2),
            [new ShipActorOrdersCheckpoint(Ship, work, Override: null)]);

        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.orders.actors[0].base.queue[0].id",
            result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsTransitLinkForLocalLeg()
    {
        TravelPlan plan = TwoLegPlan();
        ShipOrderCheckpoint active = Order(
            new ShipOrderId(1),
            plan,
            TransitId: new ConnectorTransitId(4));
        var checkpoint = new ShipOrderCoordinatorCheckpoint(
            new IdSequenceCheckpoint(2),
            [new ShipActorOrdersCheckpoint(
                Ship,
                new ShipOrderWorkSetCheckpoint(active, [], LastTerminal: null),
                Override: null)]);

        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.orders.actors[0].base.active.transitId",
            result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsQueueWithoutActiveOrder()
    {
        var checkpoint = new ShipOrderCoordinatorCheckpoint(
            new IdSequenceCheckpoint(2),
            [new ShipActorOrdersCheckpoint(
                Ship,
                new ShipOrderWorkSetCheckpoint(
                    Active: null,
                    Queue: [Order(
                        new ShipOrderId(1),
                        Status: ShipOrderStatus.Queued,
                        Reason: ShipOrderReason.QueuedBehindActiveOrder)],
                    LastTerminal: null),
                Override: null)]);

        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.orders.actors[0].base.queue",
            result.Failure?.Path);
    }

    [Fact]
    public void RestorePreservesOrderSequenceExhaustion()
    {
        var checkpoint = new ShipOrderCoordinatorCheckpoint(
            new IdSequenceCheckpoint(null),
            [new ShipActorOrdersCheckpoint(
                Ship,
                new ShipOrderWorkSetCheckpoint(
                    Active: null,
                    Queue: [],
                    LastTerminal: Order(
                        new ShipOrderId(ulong.MaxValue),
                        Status: ShipOrderStatus.Completed,
                        Reason: ShipOrderReason.DestinationReached)),
                Override: null)]);

        CheckpointResult<ShipOrderCoordinator> result =
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint);

        ShipOrderCoordinator restored =
            Assert.IsType<ShipOrderCoordinator>(result.Value);
        Assert.Throws<InvalidOperationException>(() =>
            restored.Create(Player, Destination(500)));
    }

    /// <summary>
    /// Creates a structurally complete active order by default while allowing
    /// corruption tests to replace the state under examination.
    /// </summary>
    private static ShipOrderCheckpoint Order(
        ShipOrderId id,
        TravelPlan? plan = null,
        MotionId? MotionId = null,
        ConnectorTransitId? TransitId = null,
        ShipOrderStatus Status = ShipOrderStatus.Active,
        ShipOrderReason Reason = ShipOrderReason.MovingToDestination)
    {
        TravelPlan? effectivePlan = Status == ShipOrderStatus.Active
            ? plan ?? TwoLegPlan()
            : plan;
        MotionId? effectiveMotion = Status == ShipOrderStatus.Active
            && TransitId is null
                ? MotionId ?? new MotionId(7)
                : MotionId;
        return new ShipOrderCheckpoint(
            id,
            Player,
            Destination(200),
            Status,
            Reason,
            effectivePlan,
            NextLegIndex: 0,
            effectiveMotion,
            TransitId);
    }

    private static NavigationDestination.Position Destination(long x) =>
        new NavigationDestination.Position(Position(x));

    private static TravelPlan TwoLegPlan() =>
        new(
            Destination(200),
            [
                new TravelLeg.Local(
                    Position(0),
                    Position(100),
                    new SimulationDuration(100)),
                new TravelLeg.Local(
                    Position(100),
                    Position(200),
                    new SimulationDuration(100)),
            ]);

    private static SystemPosition Position(long x) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(0)));
}
