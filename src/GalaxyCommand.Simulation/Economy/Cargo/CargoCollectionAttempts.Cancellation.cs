using System.Collections.Frozen;

namespace GalaxyCommand.Simulation;

/// <summary>Exact owned cancellation resources, separate from other workflows' inventory commitments.</summary>
internal sealed record PreparedCargoAttemptCancellation(CargoCollectionAttemptOwner Owner,
    CargoCollectionAttempt Attempt, Inventory Inventory, PhysicalReservation Reservation, IReadOnlySet<EventKey> DueKeys);

internal sealed partial class CargoCollectionAttemptOwner
{
    /// <summary>
    /// Prepares exact attempt work and incoming capacity without releasing resources
    /// or poisoning authority on rejection. The copied due-key set proves which
    /// same-time envelopes were already captured by the current StateUpdate wave.
    /// </summary>
    internal bool TryPrepareCancellation(ShipId ship, IReadOnlySet<EventKey> dueKeys,
        out PreparedCargoAttemptCancellation? prepared)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(dueKeys);
        prepared = null;
        if (!_attempts.TryGetValue(ship, out CargoCollectionAttempt? attempt)) return false;
        try
        {
            Inventory inventory = ValidateReservation(attempt);
            IReadOnlySet<EventKey> copiedDueKeys = dueKeys.ToFrozenSet();
            ValidateWork(attempt, copiedDueKeys);
            prepared = new(this, attempt, inventory, inventory.GetPhysicalReservation(attempt.CapacityReservationId)!, copiedDueKeys);
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (OverflowException) { return false; }
    }

    /// <summary>
    /// Revalidates the exact attempt, inventory instance, reservation reference and
    /// both event envelopes before any participant changes. Other independently
    /// prepared workflow reservations may change without invalidating this proof.
    /// </summary>
    internal bool CanCommitCancellation(PreparedCargoAttemptCancellation prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!_isHealthy || !ReferenceEquals(prepared.Owner, this) || Get(prepared.Attempt.ShipId) != prepared.Attempt) return false;
        try
        {
            Inventory inventory = ValidateReservation(prepared.Attempt);
            if (!ReferenceEquals(inventory, prepared.Inventory)
                || !ReferenceEquals(inventory.GetPhysicalReservation(prepared.Attempt.CapacityReservationId), prepared.Reservation)) return false;
            ValidateWork(prepared.Attempt, prepared.DueKeys);
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (OverflowException) { return false; }
    }

    /// <summary>
    /// Applies only a still-valid cancellation plan, without allocating replacement
    /// events or altering motion. Stale rejection leaves all resources untouched;
    /// unexpected failure after mutation poisons this owner for the session guard.
    /// </summary>
    internal bool ApplyPreparedCancellation(PreparedCargoAttemptCancellation prepared)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CanCommitCancellation(prepared)) return false;
        try
        {
            CancelWork(prepared.Attempt, prepared.DueKeys);
            InventoryReservationResult release = prepared.Inventory.ReleasePhysicalReservation(prepared.Reservation.Id, prepared.Reservation.Owner);
            if (!release.IsAccepted || !ReferenceEquals(release.Reservation, prepared.Reservation)
                || !_attempts.Remove(prepared.Attempt.ShipId))
                throw new InvalidOperationException("Prepared cargo attempt cancellation changed during commit.");
            return true;
        }
        catch { _isHealthy = false; throw; }
    }
}
