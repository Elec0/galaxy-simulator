using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public enum OrderPlacement
{
    ReplaceAll,
    Append,
}

public enum ShipOrderStatus
{
    Queued,
    Active,
    Waiting,
    Suspended,
    Completed,
    Cancelled,
    Failed,
}

public enum ShipOrderReason
{
    QueuedBehindActiveOrder,
    MovingToDestination,
    DestinationReached,
    CancelledByCommand,
    ReplacedByCommand,
    SuspendedByScriptedOverride,
    ResumingAfterScriptedOverride,
    ScriptedOverrideEnded,
    DestinationBecameUnreachable,
}

public sealed record ShipOrderSnapshot(
    ShipOrderId Id,
    CommandSource Source,
    NavigationDestination Destination,
    ShipOrderStatus Status,
    ShipOrderReason Reason);

internal enum CancelOrderDisposition
{
    Missing,
    Active,
    Queued,
}

internal sealed class ShipOrderCoordinator
{
    private readonly SortedDictionary<ShipId, ActorOrders> _actors =
        new(EntityIdComparer<ShipId>.Instance);
    private readonly IdSequence<ShipOrderId> _ids = new();

    internal void Add(ShipId shipId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        if (!_actors.TryAdd(shipId, new ActorOrders()))
        {
            throw new InvalidOperationException($"Duplicate order actor {shipId}.");
        }
    }

    internal bool Contains(ShipId shipId) =>
        _actors.ContainsKey(shipId);

    internal ShipOrder Create(
        CommandSource source,
        NavigationDestination destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        return new ShipOrder(_ids.Allocate(), source, destination);
    }

    internal ShipOrder? GetActive(ShipId shipId) =>
        CurrentWork(GetRequired(shipId)).Active;

    internal bool HasActive(ShipId shipId) =>
        GetActive(shipId) is not null;

    internal bool IsActive(ShipId shipId, ShipOrderId orderId) =>
        GetActive(shipId)?.Id == orderId;

    internal bool Contains(ShipId shipId, ShipOrderId orderId)
    {
        WorkSet work = CurrentWork(GetRequired(shipId));
        return work.Active?.Id == orderId
            || work.Queue.Any(order => order.Id == orderId);
    }

    internal void ReplaceAll(ShipId shipId, ShipOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        WorkSet work = CurrentWork(GetRequired(shipId));
        CancelWork(work, ShipOrderReason.ReplacedByCommand);
        Activate(work, order, ShipOrderReason.MovingToDestination);
    }

    internal bool Append(ShipId shipId, ShipOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        WorkSet work = CurrentWork(GetRequired(shipId));
        if (work.Active is null)
        {
            Activate(work, order, ShipOrderReason.MovingToDestination);
            return true;
        }

        order.Status = ShipOrderStatus.Queued;
        order.Reason = ShipOrderReason.QueuedBehindActiveOrder;
        work.Queue.Add(order);
        return false;
    }

    internal CancelOrderDisposition Cancel(
        ShipId shipId,
        ShipOrderId orderId)
    {
        WorkSet work = CurrentWork(GetRequired(shipId));
        if (work.Active?.Id == orderId)
        {
            Finish(
                work,
                work.Active,
                ShipOrderStatus.Cancelled,
                ShipOrderReason.CancelledByCommand);
            Promote(work);
            return CancelOrderDisposition.Active;
        }

        int queuedIndex = work.Queue.FindIndex(order => order.Id == orderId);
        if (queuedIndex < 0)
        {
            return CancelOrderDisposition.Missing;
        }

        ShipOrder queued = work.Queue[queuedIndex];
        work.Queue.RemoveAt(queuedIndex);
        queued.Status = ShipOrderStatus.Cancelled;
        queued.Reason = ShipOrderReason.CancelledByCommand;
        work.LastTerminal = queued;
        return CancelOrderDisposition.Queued;
    }

    internal void SetPlan(
        ShipId shipId,
        ShipOrderId orderId,
        TravelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ShipOrder active = GetRequiredActive(shipId, orderId);
        active.Plan = plan;
        active.NextLegIndex = 0;
        active.MotionId = null;
        active.Status = ShipOrderStatus.Active;
        active.Reason = ShipOrderReason.MovingToDestination;
    }

    internal TravelLeg? NextLeg(ShipId shipId, ShipOrderId orderId)
    {
        ShipOrder active = GetRequiredActive(shipId, orderId);
        TravelPlan plan = active.Plan
            ?? throw new InvalidOperationException($"Order {orderId} has no travel plan.");
        return active.NextLegIndex < plan.Legs.Count
            ? plan.Legs[active.NextLegIndex]
            : null;
    }

    internal void BindMotion(
        ShipId shipId,
        ShipOrderId orderId,
        MotionId motionId)
    {
        ShipOrder active = GetRequiredActive(shipId, orderId);
        active.MotionId = motionId;
    }

    internal void CompleteLeg(
        ShipId shipId,
        ShipOrderId orderId,
        MotionId? expectedMotionId)
    {
        ShipOrder active = GetRequiredActive(shipId, orderId);
        if (active.MotionId != expectedMotionId)
        {
            throw new InvalidOperationException(
                $"Order {orderId} expected motion {active.MotionId}, not {expectedMotionId}.");
        }

        active.MotionId = null;
        active.NextLegIndex = checked(active.NextLegIndex + 1);
    }

    internal void CompleteActive(ShipId shipId, ShipOrderId orderId)
    {
        WorkSet work = CurrentWork(GetRequired(shipId));
        ShipOrder active = GetRequiredActive(work, shipId, orderId);
        Finish(
            work,
            active,
            ShipOrderStatus.Completed,
            ShipOrderReason.DestinationReached);
        Promote(work);
    }

    internal void FailActive(ShipId shipId, ShipOrderId orderId)
    {
        WorkSet work = CurrentWork(GetRequired(shipId));
        ShipOrder active = GetRequiredActive(work, shipId, orderId);
        Finish(
            work,
            active,
            ShipOrderStatus.Failed,
            ShipOrderReason.DestinationBecameUnreachable);
        Promote(work);
    }

    internal void BeginOverride(ShipId shipId)
    {
        ActorOrders actor = GetRequired(shipId);
        if (actor.Override is not null)
        {
            throw new InvalidOperationException($"Actor {shipId} already has override orders.");
        }

        if (actor.Base.Active is { } active)
        {
            active.Status = ShipOrderStatus.Suspended;
            active.Reason = ShipOrderReason.SuspendedByScriptedOverride;
            active.Plan = null;
            active.NextLegIndex = 0;
            active.MotionId = null;
        }

        actor.Override = new WorkSet();
    }

    internal ShipOrder? EndOverride(
        ShipId shipId,
        ScriptedOverrideReleasePolicy releasePolicy)
    {
        ActorOrders actor = GetRequired(shipId);
        WorkSet overrideWork = actor.Override
            ?? throw new InvalidOperationException($"Actor {shipId} has no override orders.");
        switch (releasePolicy)
        {
            case ScriptedOverrideReleasePolicy.CancelOutstanding:
                CancelWork(overrideWork, ShipOrderReason.ScriptedOverrideEnded);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported scripted override release policy {releasePolicy}.");
        }

        actor.Override = null;

        if (actor.Base.Active is { } suspended)
        {
            if (suspended.Status != ShipOrderStatus.Suspended)
            {
                throw new InvalidOperationException(
                    $"Base order {suspended.Id} was not suspended during override.");
            }

            suspended.Status = ShipOrderStatus.Active;
            suspended.Reason = ShipOrderReason.ResumingAfterScriptedOverride;
        }

        return actor.Base.Active;
    }

    internal bool Remove(ShipId shipId) =>
        _actors.Remove(shipId);

    internal ShipOrderSnapshot? CaptureCurrent(ShipId shipId)
    {
        WorkSet work = CurrentWork(GetRequired(shipId));
        return Snapshot(work.Active ?? work.LastTerminal);
    }

    internal IReadOnlyList<ShipOrderSnapshot> CaptureQueue(ShipId shipId)
    {
        WorkSet work = CurrentWork(GetRequired(shipId));
        return CopySnapshots(work.Queue);
    }

    internal IReadOnlyList<ShipOrderSnapshot> CaptureSuspended(ShipId shipId)
    {
        ActorOrders actor = GetRequired(shipId);
        if (actor.Override is null)
        {
            return Array.Empty<ShipOrderSnapshot>();
        }

        var suspended = new List<ShipOrder>();
        if (actor.Base.Active is { } active)
        {
            suspended.Add(active);
        }

        suspended.AddRange(actor.Base.Queue);
        return CopySnapshots(suspended);
    }

    private static void Activate(
        WorkSet work,
        ShipOrder order,
        ShipOrderReason reason)
    {
        if (work.Active is not null)
        {
            throw new InvalidOperationException(
                $"Cannot activate order {order.Id} while order {work.Active.Id} is active.");
        }

        order.Status = ShipOrderStatus.Active;
        order.Reason = reason;
        work.Active = order;
    }

    private static void Promote(WorkSet work)
    {
        if (work.Active is not null || work.Queue.Count == 0)
        {
            return;
        }

        ShipOrder next = work.Queue[0];
        work.Queue.RemoveAt(0);
        Activate(work, next, ShipOrderReason.MovingToDestination);
    }

    private static void CancelWork(
        WorkSet work,
        ShipOrderReason reason)
    {
        if (work.Active is { } active)
        {
            Finish(work, active, ShipOrderStatus.Cancelled, reason);
        }

        foreach (ShipOrder queued in work.Queue)
        {
            queued.Status = ShipOrderStatus.Cancelled;
            queued.Reason = reason;
            work.LastTerminal = queued;
        }

        work.Queue.Clear();
    }

    private static void Finish(
        WorkSet work,
        ShipOrder order,
        ShipOrderStatus status,
        ShipOrderReason reason)
    {
        order.Status = status;
        order.Reason = reason;
        order.Plan = null;
        order.NextLegIndex = 0;
        order.MotionId = null;
        if (work.Active == order)
        {
            work.Active = null;
        }

        work.LastTerminal = order;
    }

    private ShipOrder GetRequiredActive(
        ShipId shipId,
        ShipOrderId orderId) =>
        GetRequiredActive(CurrentWork(GetRequired(shipId)), shipId, orderId);

    private static ShipOrder GetRequiredActive(
        WorkSet work,
        ShipId shipId,
        ShipOrderId orderId)
    {
        if (work.Active is not { } active || active.Id != orderId)
        {
            throw new InvalidOperationException(
                $"Ship {shipId} has no active order {orderId}.");
        }

        return active;
    }

    private ActorOrders GetRequired(ShipId shipId) =>
        _actors.GetValueOrDefault(shipId)
        ?? throw new KeyNotFoundException($"Unknown order actor {shipId}.");

    private static WorkSet CurrentWork(ActorOrders actor) =>
        actor.Override ?? actor.Base;

    private static ShipOrderSnapshot? Snapshot(ShipOrder? order) =>
        order is null
            ? null
            : new ShipOrderSnapshot(
                order.Id,
                order.Source,
                order.Destination,
                order.Status,
                order.Reason);

    private static ReadOnlyCollection<ShipOrderSnapshot> CopySnapshots(
        IEnumerable<ShipOrder> orders) =>
        new ReadOnlyCollection<ShipOrderSnapshot>(
            orders.Select(order => Snapshot(order)!).ToArray());

    private sealed class ActorOrders
    {
        internal WorkSet Base { get; } = new();

        internal WorkSet? Override { get; set; }
    }

    private sealed class WorkSet
    {
        internal ShipOrder? Active { get; set; }

        internal List<ShipOrder> Queue { get; } = [];

        internal ShipOrder? LastTerminal { get; set; }
    }
}

internal sealed class ShipOrder
{
    internal ShipOrder(
        ShipOrderId id,
        CommandSource source,
        NavigationDestination destination)
    {
        Id = id;
        Source = source;
        Destination = destination;
    }

    internal ShipOrderId Id { get; }

    internal CommandSource Source { get; }

    internal NavigationDestination Destination { get; }

    internal ShipOrderStatus Status { get; set; }

    internal ShipOrderReason Reason { get; set; }

    internal TravelPlan? Plan { get; set; }

    internal int NextLegIndex { get; set; }

    internal MotionId? MotionId { get; set; }
}
