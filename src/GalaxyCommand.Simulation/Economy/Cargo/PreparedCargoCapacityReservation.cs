namespace GalaxyCommand.Simulation;

public sealed partial class InventoryCommitOwner
{
    internal bool TryPeekCargoReservationIdentity(out ReservationId id) => _reservationIds.TryPeek(out id);

    /// <summary>Preflights an attempt's incoming capacity against the shared reservation allocator without mutation.</summary>
    internal bool CanReserveCargoCapacity(Inventory inventory, Quantity capacity, ReservationOwner.CargoCollection owner, ReservationId expected)
        => _reservationIds.TryPeek(out ReservationId next) && next == expected && capacity != Quantity.Zero
            && owner.ShipId.Value != 0 && owner.OrderId.Value != 0 && inventory.Custody is InventoryCustody.PrincipalControlled
            && inventory.GetPhysicalReservation(expected) is null && inventory.GetReservation(expected) is null
            && capacity <= inventory.RemainingCapacity;

    /// <summary>Consumes one prevalidated capacity reservation identity at the attempt's serial owner commit.</summary>
    internal void ApplyCargoCapacityReservation(Inventory inventory, Quantity capacity, ReservationOwner.CargoCollection owner, ReservationId expected)
    {
        if (!CanReserveCargoCapacity(inventory, capacity, owner, expected))
            throw new InvalidOperationException("Prepared cargo capacity reservation changed before commit.");
        if (!inventory.ReservePhysical(expected, new PhysicalReservationSubject.IncomingCapacity(capacity), owner).IsAccepted
            || _reservationIds.Allocate() != expected)
            throw new InvalidOperationException("Prevalidated cargo capacity reservation failed during commit.");
    }
}
