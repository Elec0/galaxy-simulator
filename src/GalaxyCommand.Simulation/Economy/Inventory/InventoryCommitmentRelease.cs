namespace GalaxyCommand.Simulation;

/// <summary>
/// Exact reservation reference claimed by a prepared workflow cancellation.
/// The workflow owner remains responsible for committing the release.
/// </summary>
internal abstract record InventoryCommitmentRelease(Inventory Inventory)
{
    internal sealed record Material(Inventory Inventory, Reservation Reservation) : InventoryCommitmentRelease(Inventory);
    internal sealed record Capacity(Inventory Inventory, CapacityReservation Reservation) : InventoryCommitmentRelease(Inventory);
    internal sealed record Physical(Inventory Inventory, PhysicalReservation Reservation) : InventoryCommitmentRelease(Inventory);

    /// <summary>Rejects replacement reservations even when their values or identifiers were reused.</summary>
    internal bool CanCommit() => this switch
    {
        Material release => ReferenceEquals(Inventory.GetReservation(release.Reservation.Id), release.Reservation),
        Capacity release => ReferenceEquals(Inventory.GetCapacityReservation(release.Reservation.Id), release.Reservation),
        Physical release => ReferenceEquals(Inventory.GetPhysicalReservation(release.Reservation.Id), release.Reservation),
        _ => false,
    };
}

public sealed partial class Inventory
{
    /// <summary>
    /// Requires one live release claim for every commitment in this inventory.
    /// Claims for other inventories are validated by their owning transaction.
    /// </summary>
    internal bool HasExactReleaseCoverage(IReadOnlyList<InventoryCommitmentRelease> releases)
    {
        var material = new HashSet<ReservationId>();
        var capacity = new HashSet<CapacityReservationId>();
        var physical = new HashSet<ReservationId>();
        foreach (InventoryCommitmentRelease release in releases)
        {
            if (!ReferenceEquals(release.Inventory, this)) continue;
            if (!release.CanCommit()) return false;
            bool unique = release switch
            {
                InventoryCommitmentRelease.Material claim => material.Add(claim.Reservation.Id),
                InventoryCommitmentRelease.Capacity claim => capacity.Add(claim.Reservation.Id),
                InventoryCommitmentRelease.Physical claim => physical.Add(claim.Reservation.Id),
                _ => false,
            };
            if (!unique) return false;
        }

        return material.Count == _reservations.Count && capacity.Count == _capacityReservations.Count
            && physical.Count == _physicalReservations.Count;
    }
}
