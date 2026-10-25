namespace GalaxyCommand.Simulation;

internal sealed partial class ShipOrderCoordinator
{
    /// <summary>Emits terminal sensor-action outcomes before actor teardown, without promoting work for a removed actor.</summary>
    internal void FailActionsForActorRemoval(ShipId ship, ICollection<ShipOrderTransition> transitions)
    {
        ActorOrders actor = GetRequired(ship);
        foreach (WorkSet? work in new[] { actor.Base, actor.Override })
        {
            if (work is null) continue;
            foreach (ShipOrder order in work.Queue.Concat(work.Active is { } active ? [active] : []))
                if (order.Action is not null && order.Status is not (ShipOrderStatus.Completed or ShipOrderStatus.Cancelled or ShipOrderStatus.Failed))
                    Transition(ship, order, ShipOrderStatus.Failed, ShipOrderReason.ActionActorRemoved, transitions);
        }
    }

    /// <summary>Retains an active action while its post-completion execution wave is pending, without fabricating movement.</summary>
    internal void AwaitAction(ShipId ship, ShipOrderId orderId, ICollection<ShipOrderTransition> transitions)
    {
        ShipOrder active = GetRequiredActive(ship, orderId);
        if (active.Action is null) throw new InvalidOperationException("Only action orders can await action execution.");
        active.Plan = null;
        active.NextLegIndex = 0;
        active.MotionId = null;
        active.TransitId = null;
        if (active.Reason != ShipOrderReason.AwaitingActionExecution)
            Transition(ship, active, ShipOrderStatus.Active, ShipOrderReason.AwaitingActionExecution, transitions);
    }
}
