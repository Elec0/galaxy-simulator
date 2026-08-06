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
    WaitingForConnectorTransitCompletion,
    DestinationBecameUnreachable,
    TargetRemoved,
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

internal sealed record ShipOrderTransition(
    ShipId ShipId,
    ShipOrderId OrderId,
    CommandSource Source,
    NavigationDestination Destination,
    ShipOrderStatus? PreviousStatus,
    ShipOrderStatus NextStatus,
    ShipOrderReason Reason);

internal sealed record TargetedShipOrder(
    ShipId ShipId,
    ShipOrderId OrderId,
    EntityId TargetEntityId,
    bool WasCurrentActive);

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

    internal void ReplaceAll(
        ShipId shipId,
        ShipOrder order,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(transitions);
        WorkSet work = CurrentWork(GetRequired(shipId));
        CancelWork(
            shipId,
            work,
            ShipOrderReason.ReplacedByCommand,
            transitions);
        Activate(
            shipId,
            work,
            order,
            ShipOrderReason.MovingToDestination,
            transitions);
    }

    internal bool Append(
        ShipId shipId,
        ShipOrder order,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(transitions);
        WorkSet work = CurrentWork(GetRequired(shipId));
        if (work.Active is null)
        {
            Activate(
                shipId,
                work,
                order,
                ShipOrderReason.MovingToDestination,
                transitions);
            return true;
        }

        Transition(
            shipId,
            order,
            ShipOrderStatus.Queued,
            ShipOrderReason.QueuedBehindActiveOrder,
            transitions);
        work.Queue.Add(order);
        return false;
    }

    internal CancelOrderDisposition Cancel(
        ShipId shipId,
        ShipOrderId orderId,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        WorkSet work = CurrentWork(GetRequired(shipId));
        if (work.Active?.Id == orderId)
        {
            Finish(
                shipId,
                work,
                work.Active,
                ShipOrderStatus.Cancelled,
                ShipOrderReason.CancelledByCommand,
                transitions);
            Promote(shipId, work, transitions);
            return CancelOrderDisposition.Active;
        }

        int queuedIndex = work.Queue.FindIndex(order => order.Id == orderId);
        if (queuedIndex < 0)
        {
            return CancelOrderDisposition.Missing;
        }

        ShipOrder queued = work.Queue[queuedIndex];
        work.Queue.RemoveAt(queuedIndex);
        Transition(
            shipId,
            queued,
            ShipOrderStatus.Cancelled,
            ShipOrderReason.CancelledByCommand,
            transitions);
        work.LastTerminal = queued;
        return CancelOrderDisposition.Queued;
    }

    internal void SetPlan(
        ShipId shipId,
        ShipOrderId orderId,
        TravelPlan plan,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(transitions);
        ShipOrder active = GetRequiredActive(shipId, orderId);
        active.Plan = plan;
        active.NextLegIndex = 0;
        active.MotionId = null;
        active.TransitId = null;
        if (active.Status == ShipOrderStatus.Active)
        {
            active.Reason = ShipOrderReason.MovingToDestination;
        }
        else
        {
            Transition(
                shipId,
                active,
                ShipOrderStatus.Active,
                ShipOrderReason.MovingToDestination,
                transitions);
        }
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

    internal void BindTransit(
        ShipId shipId,
        ShipOrderId orderId,
        ConnectorTransitId transitId)
    {
        ShipOrder active = GetRequiredActive(shipId, orderId);
        active.TransitId = transitId;
    }

    internal bool IsBoundTransit(
        ShipId shipId,
        ConnectorTransitId transitId) =>
        GetActive(shipId)?.TransitId == transitId;

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

    internal void CompleteTransit(
        ShipId shipId,
        ShipOrderId orderId,
        ConnectorTransitId expectedTransitId)
    {
        ShipOrder active = GetRequiredActive(shipId, orderId);
        if (active.TransitId != expectedTransitId)
        {
            throw new InvalidOperationException(
                $"Order {orderId} expected connector transit {active.TransitId}, not {expectedTransitId}.");
        }

        active.TransitId = null;
        active.NextLegIndex = checked(active.NextLegIndex + 1);
    }

    internal void WaitForTransitCompletion(
        ShipId shipId,
        ShipOrderId orderId,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ShipOrder active = GetRequiredActive(shipId, orderId);
        active.Plan = null;
        active.NextLegIndex = 0;
        active.MotionId = null;
        active.TransitId = null;
        Transition(
            shipId,
            active,
            ShipOrderStatus.Waiting,
            ShipOrderReason.WaitingForConnectorTransitCompletion,
            transitions);
    }

    internal void CompleteActive(
        ShipId shipId,
        ShipOrderId orderId,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        WorkSet work = CurrentWork(GetRequired(shipId));
        ShipOrder active = GetRequiredActive(work, shipId, orderId);
        Finish(
            shipId,
            work,
            active,
            ShipOrderStatus.Completed,
            ShipOrderReason.DestinationReached,
            transitions);
        Promote(shipId, work, transitions);
    }

    internal void FailActive(
        ShipId shipId,
        ShipOrderId orderId,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        WorkSet work = CurrentWork(GetRequired(shipId));
        ShipOrder active = GetRequiredActive(work, shipId, orderId);
        Finish(
            shipId,
            work,
            active,
            ShipOrderStatus.Failed,
            ShipOrderReason.DestinationBecameUnreachable,
            transitions);
        Promote(shipId, work, transitions);
    }

    internal void BeginOverride(
        ShipId shipId,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ActorOrders actor = GetRequired(shipId);
        if (actor.Override is not null)
        {
            throw new InvalidOperationException($"Actor {shipId} already has override orders.");
        }

        if (actor.Base.Active is { } active)
        {
            Transition(
                shipId,
                active,
                ShipOrderStatus.Suspended,
                ShipOrderReason.SuspendedByScriptedOverride,
                transitions);
            active.Plan = null;
            active.NextLegIndex = 0;
            active.MotionId = null;
            active.TransitId = null;
        }

        actor.Override = new WorkSet();
    }

    internal ShipOrder? EndOverride(
        ShipId shipId,
        ScriptedOverrideReleasePolicy releasePolicy,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ActorOrders actor = GetRequired(shipId);
        WorkSet overrideWork = actor.Override
            ?? throw new InvalidOperationException($"Actor {shipId} has no override orders.");
        switch (releasePolicy)
        {
            case ScriptedOverrideReleasePolicy.CancelOutstanding:
                CancelWork(
                    shipId,
                    overrideWork,
                    ShipOrderReason.ScriptedOverrideEnded,
                    transitions);
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

            Transition(
                shipId,
                suspended,
                ShipOrderStatus.Active,
                ShipOrderReason.ResumingAfterScriptedOverride,
                transitions);
        }

        return actor.Base.Active;
    }

    internal IReadOnlyList<TargetedShipOrder> PrepareTargetRemoval(
        EntityId targetEntityId,
        ShipId removedShipId)
    {
        var targeted = new List<TargetedShipOrder>();
        foreach ((ShipId shipId, ActorOrders actor) in _actors)
        {
            if (shipId == removedShipId)
            {
                continue;
            }

            AddTargetedOrders(
                targeted,
                shipId,
                actor,
                actor.Base,
                targetEntityId);
            if (actor.Override is { } overrideWork)
            {
                AddTargetedOrders(
                    targeted,
                    shipId,
                    actor,
                    overrideWork,
                    targetEntityId);
            }
        }

        return targeted
            .OrderBy(reference => reference.ShipId.Value)
            .ThenBy(reference => reference.OrderId.Value)
            .ToArray();
    }

    internal void ApplyTargetRemoval(
        TargetedShipOrder targeted,
        ICollection<ShipOrderTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(targeted);
        ArgumentNullException.ThrowIfNull(transitions);
        ActorOrders actor = GetRequired(targeted.ShipId);
        (WorkSet Work, ShipOrder Order, bool IsActive)? located =
            FindOrder(actor, targeted.OrderId);
        if (located is not { } match
            || match.Order.Destination is not NavigationDestination.Entity entity
            || entity.EntityId != targeted.TargetEntityId)
        {
            throw new InvalidOperationException(
                $"Prepared targeted order {targeted.OrderId} changed before removal commit.");
        }

        bool isCurrentActive = match.IsActive
            && ReferenceEquals(CurrentWork(actor), match.Work);
        if (isCurrentActive != targeted.WasCurrentActive)
        {
            throw new InvalidOperationException(
                $"Prepared targeted order {targeted.OrderId} changed activity before removal commit.");
        }

        if (match.IsActive)
        {
            Finish(
                targeted.ShipId,
                match.Work,
                match.Order,
                ShipOrderStatus.Failed,
                ShipOrderReason.TargetRemoved,
                transitions);
            if (ReferenceEquals(CurrentWork(actor), match.Work))
            {
                Promote(targeted.ShipId, match.Work, transitions);
            }
            else if (ReferenceEquals(actor.Base, match.Work)
                     && actor.Override is not null)
            {
                PromoteSuspended(targeted.ShipId, match.Work, transitions);
            }

            return;
        }

        if (!match.Work.Queue.Remove(match.Order))
        {
            throw new InvalidOperationException(
                $"Prepared queued order {targeted.OrderId} disappeared before removal commit.");
        }

        Transition(
            targeted.ShipId,
            match.Order,
            ShipOrderStatus.Failed,
            ShipOrderReason.TargetRemoved,
            transitions);
        match.Work.LastTerminal = match.Order;
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
        ShipId shipId,
        WorkSet work,
        ShipOrder order,
        ShipOrderReason reason,
        ICollection<ShipOrderTransition> transitions)
    {
        if (work.Active is not null)
        {
            throw new InvalidOperationException(
                $"Cannot activate order {order.Id} while order {work.Active.Id} is active.");
        }

        Transition(
            shipId,
            order,
            ShipOrderStatus.Active,
            reason,
            transitions);
        work.Active = order;
    }

    private static void Promote(
        ShipId shipId,
        WorkSet work,
        ICollection<ShipOrderTransition> transitions)
    {
        if (work.Active is not null || work.Queue.Count == 0)
        {
            return;
        }

        ShipOrder next = work.Queue[0];
        work.Queue.RemoveAt(0);
        Activate(
            shipId,
            work,
            next,
            ShipOrderReason.MovingToDestination,
            transitions);
    }

    private static void PromoteSuspended(
        ShipId shipId,
        WorkSet work,
        ICollection<ShipOrderTransition> transitions)
    {
        if (work.Active is not null || work.Queue.Count == 0)
        {
            return;
        }

        ShipOrder next = work.Queue[0];
        work.Queue.RemoveAt(0);
        Transition(
            shipId,
            next,
            ShipOrderStatus.Suspended,
            ShipOrderReason.SuspendedByScriptedOverride,
            transitions);
        work.Active = next;
    }

    private static void AddTargetedOrders(
        List<TargetedShipOrder> targeted,
        ShipId shipId,
        ActorOrders actor,
        WorkSet work,
        EntityId targetEntityId)
    {
        if (work.Active is { } active
            && Targets(active, targetEntityId))
        {
            targeted.Add(new TargetedShipOrder(
                shipId,
                active.Id,
                targetEntityId,
                ReferenceEquals(CurrentWork(actor), work)));
        }

        foreach (ShipOrder queued in work.Queue)
        {
            if (Targets(queued, targetEntityId))
            {
                targeted.Add(new TargetedShipOrder(
                    shipId,
                    queued.Id,
                    targetEntityId,
                    false));
            }
        }
    }

    private static bool Targets(ShipOrder order, EntityId targetEntityId) =>
        order.Destination is NavigationDestination.Entity entity
        && entity.EntityId == targetEntityId;

    private static (WorkSet Work, ShipOrder Order, bool IsActive)? FindOrder(
        ActorOrders actor,
        ShipOrderId orderId)
    {
        foreach (WorkSet work in actor.Override is { } overrideWork
                     ? new[] { actor.Base, overrideWork }
                     : new[] { actor.Base })
        {
            if (work.Active is { } active && active.Id == orderId)
            {
                return (work, active, true);
            }

            ShipOrder? queued = work.Queue.FirstOrDefault(order => order.Id == orderId);
            if (queued is not null)
            {
                return (work, queued, false);
            }
        }

        return null;
    }

    private static void CancelWork(
        ShipId shipId,
        WorkSet work,
        ShipOrderReason reason,
        ICollection<ShipOrderTransition> transitions)
    {
        if (work.Active is { } active)
        {
            Finish(
                shipId,
                work,
                active,
                ShipOrderStatus.Cancelled,
                reason,
                transitions);
        }

        foreach (ShipOrder queued in work.Queue)
        {
            Transition(
                shipId,
                queued,
                ShipOrderStatus.Cancelled,
                reason,
                transitions);
            work.LastTerminal = queued;
        }

        work.Queue.Clear();
    }

    private static void Finish(
        ShipId shipId,
        WorkSet work,
        ShipOrder order,
        ShipOrderStatus status,
        ShipOrderReason reason,
        ICollection<ShipOrderTransition> transitions)
    {
        Transition(shipId, order, status, reason, transitions);
        order.Plan = null;
        order.NextLegIndex = 0;
        order.MotionId = null;
        order.TransitId = null;
        if (work.Active == order)
        {
            work.Active = null;
        }

        work.LastTerminal = order;
    }

    private static void Transition(
        ShipId shipId,
        ShipOrder order,
        ShipOrderStatus status,
        ShipOrderReason reason,
        ICollection<ShipOrderTransition> transitions)
    {
        if (order.Status == status && order.Reason == reason)
        {
            return;
        }

        ShipOrderStatus? previousStatus = order.Status;
        order.Status = status;
        order.Reason = reason;
        transitions.Add(new ShipOrderTransition(
            shipId,
            order.Id,
            order.Source,
            order.Destination,
            previousStatus,
            status,
            reason));
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
                order.Status
                    ?? throw new InvalidOperationException(
                        $"Order {order.Id} has not entered its lifecycle."),
                order.Reason
                    ?? throw new InvalidOperationException(
                        $"Order {order.Id} has no lifecycle reason."));

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

    internal ShipOrderStatus? Status { get; set; }

    internal ShipOrderReason? Reason { get; set; }

    internal TravelPlan? Plan { get; set; }

    internal int NextLegIndex { get; set; }

    internal MotionId? MotionId { get; set; }

    internal ConnectorTransitId? TransitId { get; set; }
}
