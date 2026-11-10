namespace GalaxyCommand.Simulation;

internal sealed record PreparedConstructionProviderRemoval(ConstructionProcess Owner, ulong Revision,
    IReadOnlyList<ConstructionProviderJobState> Jobs, IReadOnlyList<InventoryCommitmentRelease> Releases);
internal sealed record PreparedConstructionInputLoss(ConstructionProcess Owner, ulong Revision,
    ConstructionProviderJobState Job, IReadOnlyList<ReservationId> ReservationIds, IReadOnlyList<InventoryCommitmentRelease.Material> Releases);

public sealed partial class ConstructionProcess
{
    internal PreparedConstructionProviderRemoval? PrepareProviderRemoval(Inventory inventory)
    {
        RequireConfiguredInventory(inventory);
        if (_providerRevision == ulong.MaxValue) return null;
        var jobs = CaptureRemovableJobs();
        if (jobs.Any(state => state.Generation.Value == ulong.MaxValue)) return null;
        var releases = new List<InventoryCommitmentRelease>();
        foreach (var state in jobs)
            foreach (ReservationId id in state.Order.AllReservationIds())
            {
                Reservation? reservation = inventory.GetReservation(id);
                if (reservation is null || reservation.Owner != new ReservationOwner.ConstructionOrder(state.Order.Id)) return null;
                releases.Add(new InventoryCommitmentRelease.Material(inventory, reservation));
            }
        return new(this, _providerRevision, Array.AsReadOnly(jobs), releases.AsReadOnly());
    }

    private ConstructionProviderJobState[] CaptureRemovableJobs() => _orders.Values
        .Where(job => job.Status is not (ConstructionOrderStatus.Completed or ConstructionOrderStatus.Cancelled))
        .Select(job => new ConstructionProviderJobState(job, job.Status, job.CompletesAt, job.Generation, job.WorkState)).ToArray();

    internal bool CanApplyProviderRemoval(PreparedConstructionProviderRemoval prepared, PreparedCargoSourceLoss? loss = null) =>
        ReferenceEquals(prepared.Owner, this) && prepared.Revision == _providerRevision
        && prepared.Jobs.SequenceEqual(CaptureRemovableJobs())
        && prepared.Jobs.All(state => state.Order.AllReservationIds().SequenceEqual(prepared.Releases
            .OfType<InventoryCommitmentRelease.Material>().Where(release => release.Reservation.Owner ==
                new ReservationOwner.ConstructionOrder(state.Order.Id)).Select(release => release.Reservation.Id)))
        && prepared.Releases.All(release => EconomicReservationRelease.IsLiveOrReleased(release, loss))
        && (loss is null || loss.WasApplied || loss.CanApply());

    /// <summary>Cancels retained work without refunds; source loss owns its exact outgoing releases.</summary>
    internal void ApplyProviderRemoval(PreparedConstructionProviderRemoval prepared, PreparedCargoSourceLoss? loss = null)
    {
        if (!CanApplyProviderRemoval(prepared, loss)) throw new InvalidOperationException("Construction removal changed after preparation.");
        foreach (var release in prepared.Releases) EconomicReservationRelease.ApplyRemaining(release, loss);
        foreach (var state in prepared.Jobs)
        {
            var job = state.Order;
            job.ClearReservations();
            job.Generation = job.Generation.Next();
            job.Status = ConstructionOrderStatus.Cancelled;
            job.CompletesAt = null;
            job.WorkState = null;
        }
        foreach (var job in prepared.Jobs) _pendingMaterializations.Remove(job.Order.Id);
        ActiveOrder = null;
        _queued.Clear();
        IsProviderAvailable = false;
        _providerRevision++;
    }

    internal PreparedConstructionInputLoss? PrepareInputLoss(IReadOnlyList<InventoryCommitmentRelease.Material> releases)
    {
        if (_providerRevision == ulong.MaxValue || ActiveOrder is not { Status: ConstructionOrderStatus.WaitingForInputs } job
            || job.Generation.Value == ulong.MaxValue || releases.Count == 0
            || releases.Select(release => release.Reservation.Id).Distinct().Count() != releases.Count
            || releases.Any(release => release.Inventory.Id != InventoryId || !release.CanCommit()
                || release.Reservation.Owner != new ReservationOwner.ConstructionOrder(job.Id)
                || !job.AllReservationIds().Contains(release.Reservation.Id))) return null;
        return new(this, _providerRevision, new ConstructionProviderJobState(job, job.Status, job.CompletesAt, job.Generation, job.WorkState), job.AllReservationIds(), Array.AsReadOnly(releases.ToArray()));
    }

    private bool MatchesInputLoss(PreparedConstructionInputLoss prepared) =>
        ReferenceEquals(prepared.Owner, this) && prepared.Revision == _providerRevision
        && prepared.Job == CaptureProviderJob()
        && prepared.ReservationIds.SequenceEqual(prepared.Job.Order.AllReservationIds())
        && prepared.Releases.All(release => prepared.Job.Order.AllReservationIds().Contains(release.Reservation.Id));

    internal bool CanCommitInputLoss(PreparedConstructionInputLoss prepared, PreparedCargoSourceLoss loss) =>
        MatchesInputLoss(prepared) && loss.CanApply()
        && prepared.Releases.All(release => release.CanCommit() && EconomicReservationRelease.IsInSourceLoss(release, loss));

    /// <summary>Invalidates only proven lost links after the inventory delta, allowing normal reacquisition.</summary>
    internal void ApplyInputLossAfterSourceLoss(PreparedConstructionInputLoss prepared, PreparedCargoSourceLoss loss)
    {
        if (!MatchesInputLoss(prepared) || !loss.WasApplied
            || prepared.Releases.Any(release => !EconomicReservationRelease.IsInSourceLoss(release, loss)
                || !EconomicReservationRelease.IsLiveOrReleased(release, loss)))
            throw new InvalidOperationException("Construction input loss changed after preparation.");
        foreach (var release in prepared.Releases) prepared.Job.Order.RemoveReservation(release.Reservation.Id);
        prepared.Job.Order.Generation = prepared.Job.Generation.Next();
        _providerRevision++;
    }
}
