namespace GalaxyCommand.Simulation;

/// <summary>Stable transport inputs and exact releases for one actor-removal cancellation.</summary>
internal sealed record PreparedTransportRemovalCancellation(
    TransportBoard Owner, TransportJob Job, Freighter Freighter,
    TransportJobStatus Status, EventGeneration Generation,
    Inventory Source, Inventory Destination, DemandRequest Demand, Quantity DemandRemaining,
    SupplyOffer? Supply, Quantity? SupplyRemaining,
    IReadOnlyList<InventoryCommitmentRelease> Releases);

public sealed partial class TransportBoard
{
    /// <summary>
    /// Proves cancellation arithmetic and every actual release without mutation.
    /// Consumed source reservations are valid only after loading; destination
    /// capacity belongs only to an unloading job.
    /// </summary>
    internal bool TryPrepareCancellationForEntityRemoval(TransportJob job, Freighter? freighter,
        InventoryRegistry inventories, out PreparedTransportRemovalCancellation? prepared)
    {
        prepared = null;
        if (!ReferenceEquals(GetJob(job.Id), job) || freighter is null
            || freighter.ShipId != job.ShipId || freighter.ActiveJobId != job.Id
            || job.Status is TransportJobStatus.Completed or TransportJobStatus.Cancelled or TransportJobStatus.FailedBeforeLoading
            || !Enum.IsDefined(job.Status)
            || inventories.Get(job.SourceInventoryId) is not { } source
            || inventories.Get(job.DestinationInventoryId) is not { } destination
            || !_demands.TryGetValue(job.DemandRequestId, out DemandRequest? demand)) return false;

        bool loaded = job.Status is TransportJobStatus.WaitingForRouteToDestination
            or TransportJobStatus.TravelingToDestination or TransportJobStatus.WaitingForDestinationCapacity
            or TransportJobStatus.Unloading;
        SupplyOffer? supply = null;
        if (!loaded && !_supplies.TryGetValue(job.SupplyOfferId, out supply)) return false;
        if (job.Generation.Value == ulong.MaxValue
            || ulong.MaxValue - demand.Remaining.Units < job.Quantity.Units
            || supply is not null && ulong.MaxValue - supply.Remaining.Units < job.Quantity.Units) return false;

        var releases = new List<InventoryCommitmentRelease>();
        Reservation? sourceReservation = source.GetReservation(job.SourceReservationId);
        var owner = new ReservationOwner.TransportJob(job.Id);
        if (loaded)
        {
            if (sourceReservation is not null) return false;
        }
        else
        {
            if (sourceReservation is null || sourceReservation.InventoryId != source.Id
                || sourceReservation.Owner != owner || sourceReservation.MaterialId != job.MaterialId
                || sourceReservation.Quantity != job.Quantity) return false;
            releases.Add(new InventoryCommitmentRelease.Material(source, sourceReservation));
        }

        if (job.Status == TransportJobStatus.Unloading)
        {
            if (job.DestinationCapacityReservationId is not { } capacityId
                || destination.GetCapacityReservation(capacityId) is not { } reservation
                || reservation.InventoryId != destination.Id || reservation.Owner != owner
                || reservation.Quantity != job.Quantity) return false;
            releases.Add(new InventoryCommitmentRelease.Capacity(destination, reservation));
        }
        else if (job.DestinationCapacityReservationId is not null) return false;

        prepared = new(this, job, freighter, job.Status, job.Generation,
            source, destination, demand, demand.Remaining, supply, supply?.Remaining,
            Array.AsReadOnly(releases.ToArray()));
        return true;
    }

    /// <summary>Rejects stale jobs, changed accounting, replaced inventories, and reused reservation identities.</summary>
    internal bool CanCommitRemovalCancellation(PreparedTransportRemovalCancellation prepared, InventoryRegistry inventories) =>
        ReferenceEquals(prepared.Owner, this)
        && prepared.Job.Status == prepared.Status && prepared.Job.Generation == prepared.Generation
        && ReferenceEquals(inventories.Get(prepared.Source.Id), prepared.Source)
        && ReferenceEquals(inventories.Get(prepared.Destination.Id), prepared.Destination)
        && prepared.Demand.Remaining == prepared.DemandRemaining
        && prepared.Supply?.Remaining == prepared.SupplyRemaining
        && prepared.Releases.All(release => release.CanCommit())
        && TryPrepareCancellationForEntityRemoval(prepared.Job, prepared.Freighter, inventories, out var current)
        && ReferenceEquals(current!.Demand, prepared.Demand) && ReferenceEquals(current.Supply, prepared.Supply)
        && current.Releases.SequenceEqual(prepared.Releases);
}
