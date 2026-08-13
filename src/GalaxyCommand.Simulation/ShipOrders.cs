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
    private readonly IdSequence<ShipOrderId> _ids;

    internal ShipOrderCoordinator()
        : this(new IdSequence<ShipOrderId>())
    {
    }

    private ShipOrderCoordinator(IdSequence<ShipOrderId> ids)
    {
        _ids = ids;
    }

    /// <summary>
    /// Captures every base and override work set in stable ship order together
    /// with the exact order identifier allocator state.
    /// </summary>
    internal ShipOrderCoordinatorCheckpoint CaptureCheckpoint() =>
        new(
            _ids.CaptureCheckpoint(),
            _actors.Select(pair => new ShipActorOrdersCheckpoint(
                pair.Key,
                CaptureWorkSet(pair.Value.Base),
                pair.Value.Override is { } overrideWork
                    ? CaptureWorkSet(overrideWork)
                    : null)));

    /// <summary>
    /// Validates and directly restores order ownership without creating,
    /// transitioning, promoting, or cancelling any saved order.
    /// </summary>
    internal static CheckpointResult<ShipOrderCoordinator> RestoreCheckpoint(
        ShipOrderCoordinatorCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CheckpointResult<IdSequence<ShipOrderId>> idResult =
            IdSequence<ShipOrderId>.RestoreCheckpoint(checkpoint.OrderIds);
        if (!idResult.IsSuccess)
        {
            return Rejected(
                "$.checkpoint.orders.orderIds.nextValue",
                idResult.Failure!.Message);
        }

        var restored = new ShipOrderCoordinator(idResult.Value!);
        var retainedOrderIds = new HashSet<ShipOrderId>();
        const string path = "$.checkpoint.orders.actors";
        for (int index = 0; index < checkpoint.Actors.Count; index++)
        {
            ShipActorOrdersCheckpoint? actor = checkpoint.Actors[index];
            if (actor is null)
            {
                return Rejected(
                    $"{path}[{index}]",
                    "A ship order actor checkpoint is missing.");
            }

            if (actor.ShipId.Value == 0)
            {
                return Rejected(
                    $"{path}[{index}].shipId",
                    "An order actor ship identifier must be nonzero.");
            }

            if (restored._actors.ContainsKey(actor.ShipId))
            {
                return Rejected(
                    $"{path}[{index}].shipId",
                    $"Duplicate order actor {actor.ShipId}.");
            }

            bool hasOverride = actor.Override is not null;
            CheckpointResult<WorkSet> baseResult = RestoreWorkSet(
                actor.Base,
                $"{path}[{index}].base",
                hasOverride ? WorkSetRole.SuspendedBase : WorkSetRole.Current,
                checkpoint.OrderIds,
                retainedOrderIds);
            if (!baseResult.IsSuccess)
            {
                return CheckpointResult<ShipOrderCoordinator>.Rejected(
                    baseResult.Failure!);
            }

            WorkSet? overrideWork = null;
            if (actor.Override is { } overrideCheckpoint)
            {
                CheckpointResult<WorkSet> overrideResult = RestoreWorkSet(
                    overrideCheckpoint,
                    $"{path}[{index}].override",
                    WorkSetRole.Current,
                    checkpoint.OrderIds,
                    retainedOrderIds);
                if (!overrideResult.IsSuccess)
                {
                    return CheckpointResult<ShipOrderCoordinator>.Rejected(
                        overrideResult.Failure!);
                }

                overrideWork = overrideResult.Value;
            }

            restored._actors.Add(
                actor.ShipId,
                new ActorOrders(baseResult.Value!, overrideWork));
        }

        return CheckpointResult<ShipOrderCoordinator>.Success(restored);
    }

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

    /// <summary>
    /// Preserves active, FIFO, and last-terminal roles without flattening their
    /// distinct lifecycle meaning into one order collection.
    /// </summary>
    private static ShipOrderWorkSetCheckpoint CaptureWorkSet(WorkSet work) =>
        new(
            work.Active is { } active ? CaptureOrder(active) : null,
            work.Queue.Select(CaptureOrder),
            work.LastTerminal is { } terminal ? CaptureOrder(terminal) : null);

    private static ShipOrderCheckpoint CaptureOrder(ShipOrder order) =>
        new(
            order.Id,
            order.Source,
            order.Destination,
            order.Status,
            order.Reason,
            order.Plan,
            order.NextLegIndex,
            order.MotionId,
            order.TransitId);

    /// <summary>
    /// Restores one work set while preserving FIFO queue order and enforcing
    /// the active-state rules for its base or override role.
    /// </summary>
    private static CheckpointResult<WorkSet> RestoreWorkSet(
        ShipOrderWorkSetCheckpoint? checkpoint,
        string path,
        WorkSetRole role,
        IdSequenceCheckpoint orderIds,
        HashSet<ShipOrderId> retainedOrderIds)
    {
        if (checkpoint is null)
        {
            return WorkSetRejected(path, "A ship order work set is missing.");
        }

        var restored = new WorkSet();
        if (checkpoint.Active is { } activeCheckpoint)
        {
            CheckpointResult<ShipOrder> activeResult = RestoreOrder(
                activeCheckpoint,
                $"{path}.active",
                orderIds,
                retainedOrderIds);
            if (!activeResult.IsSuccess)
            {
                return CheckpointResult<WorkSet>.Rejected(activeResult.Failure!);
            }

            ShipOrder active = activeResult.Value!;
            bool validActiveStatus = role == WorkSetRole.SuspendedBase
                ? active.Status == ShipOrderStatus.Suspended
                : active.Status is ShipOrderStatus.Active or ShipOrderStatus.Waiting;
            if (!validActiveStatus)
            {
                return WorkSetRejected(
                    $"{path}.active.status",
                    "The active order status does not match its work-set role.");
            }

            restored.Active = active;
        }

        // Every mutation path promotes the FIFO head before returning to a
        // completed boundary, so a queue without an active order is corrupt.
        if (restored.Active is null && checkpoint.Queue.Count != 0)
        {
            return WorkSetRejected(
                $"{path}.queue",
                "A queued order requires an active order ahead of it.");
        }

        for (int index = 0; index < checkpoint.Queue.Count; index++)
        {
            ShipOrderCheckpoint? queuedCheckpoint = checkpoint.Queue[index];
            if (queuedCheckpoint is null)
            {
                return WorkSetRejected(
                    $"{path}.queue[{index}]",
                    "A queued order checkpoint is missing.");
            }

            CheckpointResult<ShipOrder> queuedResult = RestoreOrder(
                queuedCheckpoint,
                $"{path}.queue[{index}]",
                orderIds,
                retainedOrderIds);
            if (!queuedResult.IsSuccess)
            {
                return CheckpointResult<WorkSet>.Rejected(queuedResult.Failure!);
            }

            ShipOrder queued = queuedResult.Value!;
            if (queued.Status != ShipOrderStatus.Queued)
            {
                return WorkSetRejected(
                    $"{path}.queue[{index}].status",
                    "A queued order must have queued status.");
            }

            restored.Queue.Add(queued);
        }

        if (checkpoint.LastTerminal is { } terminalCheckpoint)
        {
            CheckpointResult<ShipOrder> terminalResult = RestoreOrder(
                terminalCheckpoint,
                $"{path}.lastTerminal",
                orderIds,
                retainedOrderIds);
            if (!terminalResult.IsSuccess)
            {
                return CheckpointResult<WorkSet>.Rejected(terminalResult.Failure!);
            }

            ShipOrder terminal = terminalResult.Value!;
            if (terminal.Status is not ShipOrderStatus.Completed
                and not ShipOrderStatus.Cancelled
                and not ShipOrderStatus.Failed)
            {
                return WorkSetRejected(
                    $"{path}.lastTerminal.status",
                    "A last terminal order must have a terminal status.");
            }

            restored.LastTerminal = terminal;
        }

        return CheckpointResult<WorkSet>.Success(restored);
    }

    /// <summary>
    /// Restores one retained order after validating identity, lifecycle state,
    /// plan position, and the active physical-work linkage.
    /// </summary>
    private static CheckpointResult<ShipOrder> RestoreOrder(
        ShipOrderCheckpoint checkpoint,
        string path,
        IdSequenceCheckpoint orderIds,
        HashSet<ShipOrderId> retainedOrderIds)
    {
        if (checkpoint.Id.Value == 0
            || !WasAllocated(checkpoint.Id, orderIds))
        {
            return OrderRejected(
                $"{path}.id",
                "The order identifier was not allocated by the saved sequence.");
        }

        if (!retainedOrderIds.Add(checkpoint.Id))
        {
            return OrderRejected(
                $"{path}.id",
                $"Duplicate retained order {checkpoint.Id}.");
        }

        if (!IsValidSource(checkpoint.Source))
        {
            return OrderRejected(
                $"{path}.source",
                "An order source is missing or invalid.");
        }

        if (!IsValidDestination(checkpoint.Destination))
        {
            return OrderRejected(
                $"{path}.destination",
                "An order destination is missing or invalid.");
        }

        if (checkpoint.Status is not { } status || !Enum.IsDefined(status))
        {
            return OrderRejected(
                $"{path}.status",
                "An order status is missing or invalid.");
        }

        if (checkpoint.Reason is not { } reason
            || !Enum.IsDefined(reason)
            || !IsValidReason(status, reason))
        {
            return OrderRejected(
                $"{path}.reason",
                "The order reason does not match its lifecycle status.");
        }

        CheckpointValidationFailure? stateFailure = ValidateExecutionState(
            checkpoint,
            status,
            path);
        if (stateFailure is not null)
        {
            return CheckpointResult<ShipOrder>.Rejected(stateFailure);
        }

        // Assign saved state directly so restoration emits no transitions and
        // does not advance the next-leg position or allocate physical work.
        var restored = new ShipOrder(
            checkpoint.Id,
            checkpoint.Source!,
            checkpoint.Destination!)
        {
            Status = status,
            Reason = reason,
            Plan = checkpoint.Plan,
            NextLegIndex = checkpoint.NextLegIndex,
            MotionId = checkpoint.MotionId,
            TransitId = checkpoint.TransitId,
        };
        return CheckpointResult<ShipOrder>.Success(restored);
    }

    /// <summary>
    /// Verifies that only an active order retains an executable plan and that
    /// its physical-work identity matches the current leg category.
    /// </summary>
    private static CheckpointValidationFailure? ValidateExecutionState(
        ShipOrderCheckpoint checkpoint,
        ShipOrderStatus status,
        string path)
    {
        if (checkpoint.NextLegIndex < 0)
        {
            return new CheckpointValidationFailure(
                $"{path}.nextLegIndex",
                "The next leg index cannot be negative.");
        }

        if (checkpoint.MotionId is { Value: 0 })
        {
            return new CheckpointValidationFailure(
                $"{path}.motionId",
                "A linked motion identifier must be nonzero.");
        }

        if (checkpoint.TransitId is { Value: 0 })
        {
            return new CheckpointValidationFailure(
                $"{path}.transitId",
                "A linked connector transit identifier must be nonzero.");
        }

        if (status != ShipOrderStatus.Active)
        {
            return checkpoint.Plan is null
                && checkpoint.NextLegIndex == 0
                && checkpoint.MotionId is null
                && checkpoint.TransitId is null
                    ? null
                    : new CheckpointValidationFailure(
                        path,
                        "A non-active order cannot retain a plan, leg position, motion, or transit link.");
        }

        if (checkpoint.Plan is not { } plan || plan.Legs.Count == 0)
        {
            return new CheckpointValidationFailure(
                $"{path}.plan",
                "An active order requires a non-empty travel plan.");
        }

        if (checkpoint.NextLegIndex >= plan.Legs.Count)
        {
            return new CheckpointValidationFailure(
                $"{path}.nextLegIndex",
                "An active order must point to an unfinished plan leg.");
        }

        CheckpointValidationFailure? planFailure = ValidatePlan(plan, path);
        if (planFailure is not null)
        {
            return planFailure;
        }

        return plan.Legs[checkpoint.NextLegIndex] switch
        {
            TravelLeg.Local when checkpoint.TransitId is not null =>
                new CheckpointValidationFailure(
                    $"{path}.transitId",
                    "A local leg cannot retain a connector transit link."),
            TravelLeg.Local when checkpoint.MotionId is null =>
                new CheckpointValidationFailure(
                    $"{path}.motionId",
                    "The current local leg requires a motion link."),
            TravelLeg.Connector when checkpoint.MotionId is not null =>
                new CheckpointValidationFailure(
                    $"{path}.motionId",
                    "A connector leg cannot retain a local motion link."),
            TravelLeg.Connector when checkpoint.TransitId is null =>
                new CheckpointValidationFailure(
                    $"{path}.transitId",
                    "The current connector leg requires a transit link."),
            TravelLeg.Local or TravelLeg.Connector => null,
            _ => new CheckpointValidationFailure(
                $"{path}.plan.legs[{checkpoint.NextLegIndex}]",
                "The current travel leg kind is unsupported."),
        };
    }

    /// <summary>
    /// Ensures saved plan legs form one contiguous path ending at the plan's
    /// stable destination when that destination has a concrete position.
    /// </summary>
    private static CheckpointValidationFailure? ValidatePlan(
        TravelPlan plan,
        string path)
    {
        SystemPosition? priorDestination = null;
        for (int index = 0; index < plan.Legs.Count; index++)
        {
            TravelLeg? leg = plan.Legs[index];
            SystemPosition origin;
            SystemPosition destination;
            switch (leg)
            {
                case TravelLeg.Local local:
                    origin = local.Origin;
                    destination = local.Destination;
                    break;
                case TravelLeg.Connector connector:
                    origin = connector.Origin;
                    destination = connector.Destination;
                    break;
                default:
                    return new CheckpointValidationFailure(
                        $"{path}.plan.legs[{index}]",
                        "A travel plan leg is missing or unsupported.");
            }

            if (priorDestination is { } prior && prior != origin)
            {
                return new CheckpointValidationFailure(
                    $"{path}.plan.legs[{index}]",
                    "Travel plan legs must form a contiguous path.");
            }

            priorDestination = destination;
        }

        bool reachesDestination = plan.Destination switch
        {
            NavigationDestination.Position position =>
                priorDestination == position.Value,
            NavigationDestination.System system =>
                priorDestination?.SystemId == system.SystemId,
            NavigationDestination.Entity => true,
            _ => false,
        };
        return reachesDestination
            ? null
            : new CheckpointValidationFailure(
                $"{path}.plan.destination",
                "The travel plan does not end at its saved destination.");
    }

    /// <summary>
    /// Matches each order status to the reasons reachable through the current
    /// lifecycle transition methods.
    /// </summary>
    private static bool IsValidReason(
        ShipOrderStatus status,
        ShipOrderReason reason) =>
        status switch
        {
            ShipOrderStatus.Queued =>
                reason == ShipOrderReason.QueuedBehindActiveOrder,
            ShipOrderStatus.Active =>
                reason is ShipOrderReason.MovingToDestination
                    or ShipOrderReason.ResumingAfterScriptedOverride,
            ShipOrderStatus.Waiting =>
                reason == ShipOrderReason.WaitingForConnectorTransitCompletion,
            ShipOrderStatus.Suspended =>
                reason == ShipOrderReason.SuspendedByScriptedOverride,
            ShipOrderStatus.Completed =>
                reason == ShipOrderReason.DestinationReached,
            ShipOrderStatus.Cancelled =>
                reason is ShipOrderReason.CancelledByCommand
                    or ShipOrderReason.ReplacedByCommand
                    or ShipOrderReason.ScriptedOverrideEnded,
            ShipOrderStatus.Failed =>
                reason is ShipOrderReason.DestinationBecameUnreachable
                    or ShipOrderReason.TargetRemoved,
            _ => false,
        };

    /// <summary>
    /// Validates the stable local attribution retained by a saved order.
    /// </summary>
    private static bool IsValidSource(CommandSource? source) =>
        source is not null
        && Enum.IsDefined(source.Kind)
        && !string.IsNullOrWhiteSpace(source.Id.Value);

    /// <summary>
    /// Validates every currently supported destination discriminator and its
    /// required nonzero identity.
    /// </summary>
    private static bool IsValidDestination(NavigationDestination? destination) =>
        destination switch
        {
            NavigationDestination.Position position =>
                position.Value.SystemId.Value != 0,
            NavigationDestination.System system => system.SystemId.Value != 0,
            NavigationDestination.Entity entity => entity.EntityId.Value != 0,
            _ => false,
        };

    /// <summary>
    /// Accepts retained identities below the next allocator position, or any
    /// nonzero identity when the allocator is exhausted.
    /// </summary>
    private static bool WasAllocated(
        ShipOrderId id,
        IdSequenceCheckpoint sequence) =>
        sequence.NextValue is not { } next || id.Value < next;

    private static CheckpointResult<ShipOrderCoordinator> Rejected(
        string path,
        string message) =>
        CheckpointResult<ShipOrderCoordinator>.Rejected(
            new CheckpointValidationFailure(path, message));

    private static CheckpointResult<WorkSet> WorkSetRejected(
        string path,
        string message) =>
        CheckpointResult<WorkSet>.Rejected(
            new CheckpointValidationFailure(path, message));

    private static CheckpointResult<ShipOrder> OrderRejected(
        string path,
        string message) =>
        CheckpointResult<ShipOrder>.Rejected(
            new CheckpointValidationFailure(path, message));

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
        internal ActorOrders()
            : this(new WorkSet(), @override: null)
        {
        }

        internal ActorOrders(WorkSet @base, WorkSet? @override)
        {
            Base = @base;
            Override = @override;
        }

        internal WorkSet Base { get; }

        internal WorkSet? Override { get; set; }
    }

    private sealed class WorkSet
    {
        internal ShipOrder? Active { get; set; }

        internal List<ShipOrder> Queue { get; } = [];

        internal ShipOrder? LastTerminal { get; set; }
    }

    private enum WorkSetRole
    {
        Current,
        SuspendedBase,
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
