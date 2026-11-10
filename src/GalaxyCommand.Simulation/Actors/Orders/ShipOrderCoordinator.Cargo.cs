namespace GalaxyCommand.Simulation;

internal sealed partial class ShipOrderCoordinator
{
    /// <summary>Marks timed collection without withdrawing or replacing the movement owner's committed approach.</summary>
    internal void MarkCargoCollecting(ShipId ship, ShipOrderId order, ICollection<ShipOrderTransition> transitions)
    {
        ShipOrder active = GetRequiredActive(ship, order);
        if (active.Action is not ShipActionIntent.Cargo) throw new InvalidOperationException("Only cargo actions can collect.");
        if (active.Reason != ShipOrderReason.CollectingCargo)
            Transition(ship, active, ShipOrderStatus.Active, ShipOrderReason.CollectingCargo, transitions);
    }

    /// <summary>Resumes approach after elapsed collection is discarded, retaining any still-owned motion.</summary>
    internal void ResumeCargoApproach(ShipId ship, ShipOrderId order, ICollection<ShipOrderTransition> transitions)
    {
        ShipOrder active = GetRequiredActive(ship, order);
        if (active.Action is not ShipActionIntent.Cargo) throw new InvalidOperationException("Only cargo actions can resume collection approach.");
        if (active.MotionId is null && active.TransitId is null)
        {
            active.Plan = null;
            active.NextLegIndex = 0;
        }
        if (active.Reason != ShipOrderReason.MovingToDestination)
            Transition(ship, active, ShipOrderStatus.Active, ShipOrderReason.MovingToDestination, transitions);
    }
}
