namespace GalaxyCommand.Simulation;

internal sealed record PreparedProductionProviderRemoval(ProductionLine Owner, ulong Revision,
    IReadOnlyList<ProductionProviderJobState> Jobs, IReadOnlyList<InventoryCommitmentRelease> Releases);
internal sealed record PreparedProductionInputLoss(ProductionLine Owner, ulong Revision,
    ProductionProviderJobState Job, IReadOnlyList<ReservationId> ReservationIds, IReadOnlyList<InventoryCommitmentRelease.Material> Releases);

public sealed partial class ProductionLine
{
    internal PreparedProductionProviderRemoval? PrepareProviderRemoval(Inventory inventory)
    {
        RequireConfiguredInventory(inventory);
        if (_providerRevision == ulong.MaxValue) return null;
        var jobs = CaptureRemovableJobs();
        if (jobs.Any(state => state.Generation.Value == ulong.MaxValue)) return null;
        var releases = new List<InventoryCommitmentRelease>();
        foreach (var state in jobs)
            foreach (ReservationId id in state.Job.AllReservationIds())
            {
                Reservation? reservation = inventory.GetReservation(id);
                if (reservation is null || reservation.Owner != new ReservationOwner.ProductionJob(state.Job.Id)) return null;
                releases.Add(new InventoryCommitmentRelease.Material(inventory, reservation));
            }
        return new(this, _providerRevision, Array.AsReadOnly(jobs), releases.AsReadOnly());
    }

    private ProductionProviderJobState[] CaptureRemovableJobs() => _jobs.Values
        .Where(job => job.Status is not (ProductionJobStatus.Completed or ProductionJobStatus.Cancelled))
        .Select(job => new ProductionProviderJobState(job, job.Status, job.CompletesAt, job.Generation, job.WorkState)).ToArray();

    internal bool CanApplyProviderRemoval(PreparedProductionProviderRemoval prepared, PreparedCargoSourceLoss? loss = null) =>
        ReferenceEquals(prepared.Owner, this) && prepared.Revision == _providerRevision
        && prepared.Jobs.SequenceEqual(CaptureRemovableJobs())
        && prepared.Jobs.All(state => state.Job.AllReservationIds().SequenceEqual(prepared.Releases
            .OfType<InventoryCommitmentRelease.Material>().Where(release => release.Reservation.Owner ==
                new ReservationOwner.ProductionJob(state.Job.Id)).Select(release => release.Reservation.Id)))
        && prepared.Releases.All(release => EconomicReservationRelease.IsLiveOrReleased(release, loss))
        && (loss is null || loss.WasApplied || loss.CanApply());

    /// <summary>Cancels retained work without refunds; source loss owns its exact outgoing releases.</summary>
    internal void ApplyProviderRemoval(PreparedProductionProviderRemoval prepared, PreparedCargoSourceLoss? loss = null)
    {
        if (!CanApplyProviderRemoval(prepared, loss)) throw new InvalidOperationException("Production removal changed after preparation.");
        foreach (var release in prepared.Releases) EconomicReservationRelease.ApplyRemaining(release, loss);
        foreach (var state in prepared.Jobs)
        {
            var job = state.Job;
            job.ClearReservations();
            job.Generation = job.Generation.Next();
            job.Status = ProductionJobStatus.Cancelled;
            job.CompletesAt = null;
            job.WorkState = null;
        }
        ActiveJob = null;
        _queued.Clear();
        IsProviderAvailable = false;
        _providerRevision++;
    }

    internal PreparedProductionInputLoss? PrepareInputLoss(IReadOnlyList<InventoryCommitmentRelease.Material> releases)
    {
        if (_providerRevision == ulong.MaxValue || ActiveJob is not { Status: ProductionJobStatus.WaitingForInputs } job
            || job.Generation.Value == ulong.MaxValue || releases.Count == 0
            || releases.Select(release => release.Reservation.Id).Distinct().Count() != releases.Count
            || releases.Any(release => release.Inventory.Id != InventoryId || !release.CanCommit()
                || release.Reservation.Owner != new ReservationOwner.ProductionJob(job.Id)
                || !job.AllReservationIds().Contains(release.Reservation.Id))) return null;
        return new(this, _providerRevision, new ProductionProviderJobState(job, job.Status, job.CompletesAt, job.Generation, job.WorkState), job.AllReservationIds(), Array.AsReadOnly(releases.ToArray()));
    }

    private bool MatchesInputLoss(PreparedProductionInputLoss prepared) =>
        ReferenceEquals(prepared.Owner, this) && prepared.Revision == _providerRevision
        && prepared.Job == CaptureProviderJob()
        && prepared.ReservationIds.SequenceEqual(prepared.Job.Job.AllReservationIds())
        && prepared.Releases.All(release => prepared.Job.Job.AllReservationIds().Contains(release.Reservation.Id));

    internal bool CanCommitInputLoss(PreparedProductionInputLoss prepared, PreparedCargoSourceLoss loss) =>
        MatchesInputLoss(prepared) && loss.CanApply()
        && prepared.Releases.All(release => release.CanCommit() && EconomicReservationRelease.IsInSourceLoss(release, loss));

    /// <summary>Invalidates only proven lost links after the inventory delta, allowing normal reacquisition.</summary>
    internal void ApplyInputLossAfterSourceLoss(PreparedProductionInputLoss prepared, PreparedCargoSourceLoss loss)
    {
        if (!MatchesInputLoss(prepared) || !loss.WasApplied
            || prepared.Releases.Any(release => !EconomicReservationRelease.IsInSourceLoss(release, loss)
                || !EconomicReservationRelease.IsLiveOrReleased(release, loss)))
            throw new InvalidOperationException("Production input loss changed after preparation.");
        foreach (var release in prepared.Releases) prepared.Job.Job.RemoveReservation(release.Reservation.Id);
        prepared.Job.Job.Generation = prepared.Job.Generation.Next();
        _providerRevision++;
    }
}
