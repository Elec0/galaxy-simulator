namespace GalaxyCommand.Simulation;

/// <summary>Proves which exact outgoing claims the canonical source delta already released.</summary>
internal static class EconomicReservationRelease
{
    internal static bool IsInSourceLoss(InventoryCommitmentRelease release, PreparedCargoSourceLoss loss) =>
        ReferenceEquals(release.Inventory, loss.Source) && release switch
        {
            InventoryCommitmentRelease.Material material => loss.LegacyCleanup.Any(value => ReferenceEquals(value, material.Reservation)),
            InventoryCommitmentRelease.Physical physical => loss.PhysicalCleanup.Any(value => ReferenceEquals(value, physical.Reservation)),
            _ => false,
        };

    internal static bool IsLiveOrReleased(InventoryCommitmentRelease release, PreparedCargoSourceLoss? loss)
    {
        if (loss is null || !loss.WasApplied || !IsInSourceLoss(release, loss)) return release.CanCommit();
        return release switch
        {
            InventoryCommitmentRelease.Material material => release.Inventory.GetReservation(material.Reservation.Id) is null,
            InventoryCommitmentRelease.Physical physical => release.Inventory.GetPhysicalReservation(physical.Reservation.Id) is null,
            _ => false,
        };
    }

    internal static void ApplyRemaining(InventoryCommitmentRelease release, PreparedCargoSourceLoss? loss)
    {
        if (!IsLiveOrReleased(release, loss)) throw new InvalidOperationException("Economic reservation changed during commit.");
        if (loss is { WasApplied: true } && IsInSourceLoss(release, loss)) return;
        switch (release)
        {
            case InventoryCommitmentRelease.Material material: release.Inventory.Release(material.Reservation.Id); break;
            case InventoryCommitmentRelease.Capacity capacity: release.Inventory.ReleaseCapacity(capacity.Reservation.Id); break;
            case InventoryCommitmentRelease.Physical physical:
                if (!release.Inventory.ReleasePhysicalReservation(physical.Reservation.Id, physical.Reservation.Owner).IsAccepted)
                    throw new InvalidOperationException("Economic physical reservation changed during commit.");
                break;
        }
    }
}
