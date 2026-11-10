namespace GalaxyCommand.Simulation;

public sealed partial class InventoryCommitOwner
{
    internal bool TryPeekStationRecoveryReservationIdentity(out ReservationId id) => _reservationIds.TryPeek(out id);

    /// <summary>Preflights recovery room against the shared allocator before station project publication.</summary>
    internal bool CanReserveStationRecoveryCapacity(Inventory inventory, Quantity capacity,
        ReservationOwner.StationProject owner, ReservationId expected) =>
        _reservationIds.TryPeek(out ReservationId next) && next == expected && capacity != Quantity.Zero
        && owner.StationId.Value != 0 && owner.ProjectId.Value != 0
        && inventory.Custody is InventoryCustody.PrincipalControlled
        && inventory.CanAdmitIncomingCapacity
        && inventory.GetPhysicalReservation(expected) is null && inventory.GetReservation(expected) is null
        && capacity <= inventory.RemainingCapacity;

    /// <summary>Commits the exact prevalidated recovery claim and advances its shared identity only once.</summary>
    internal void ApplyStationRecoveryCapacityReservation(Inventory inventory, Quantity capacity,
        ReservationOwner.StationProject owner, ReservationId expected)
    {
        if (!CanReserveStationRecoveryCapacity(inventory, capacity, owner, expected))
            throw new InvalidOperationException("Prepared station recovery capacity changed before commit.");
        if (!inventory.ReservePhysical(expected, new PhysicalReservationSubject.IncomingCapacity(capacity), owner).IsAccepted
            || _reservationIds.Allocate() != expected)
            throw new InvalidOperationException("Prevalidated station recovery claim failed during commit.");
    }
}
