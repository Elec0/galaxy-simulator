namespace GalaxyCommand.Simulation;

internal sealed record RestoredTransportOwner(
    TransportBoard Board,
    ShipRegistry Ships,
    TransportIdSequences Ids,
    IdSequence<ReservationId> ReservationIds,
    IdSequence<CapacityReservationId> CapacityReservationIds,
    TransportTiming Timing)
{
    internal TransportOwnerCheckpoint CaptureCheckpoint() =>
        TransportCheckpointCapture.Capture(
            Board,
            Ships,
            Ids,
            ReservationIds,
            CapacityReservationIds,
            Timing);
}

internal static class TransportCheckpointCapture
{
    /// <summary>
    /// Captures transport state and its shared allocation positions without
    /// evaluating the market, assigning work, or advancing a job.
    /// </summary>
    internal static TransportOwnerCheckpoint Capture(
        TransportBoard board,
        ShipRegistry ships,
        TransportIdSequences ids,
        IdSequence<ReservationId> reservationIds,
        IdSequence<CapacityReservationId> capacityReservationIds,
        TransportTiming timing)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(reservationIds);
        ArgumentNullException.ThrowIfNull(capacityReservationIds);
        return new TransportOwnerCheckpoint(
            ids.CaptureCheckpoint(),
            reservationIds.CaptureCheckpoint(),
            capacityReservationIds.CaptureCheckpoint(),
            new TransportTimingCheckpoint(
                timing.DockingOverhead,
                timing.LoadingRate.UnitsPerSecond,
                timing.UnloadingRate.UnitsPerSecond),
            board.CaptureCheckpoint(),
            ships.CaptureFreighterCheckpoints());
    }
}

internal static class TransportCheckpointRestore
{
    private const string Path = "$.checkpoint.economy.transport";

    /// <summary>
    /// Validates transport state against restored lifecycle and inventory
    /// authority, then directly reconstructs every owner without replay.
    /// </summary>
    internal static CheckpointResult<RestoredTransportOwner> Restore(
        TransportOwnerCheckpoint checkpoint,
        InventoryRegistry inventories,
        IReadOnlyDictionary<ShipId, InventoryId> liveShips)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(liveShips);
        if (checkpoint.Ids is null || checkpoint.ReservationIds is null
            || checkpoint.CapacityReservationIds is null || checkpoint.Timing is null
            || checkpoint.Board is null || checkpoint.Freighters is null)
        {
            return Rejected(Path, "The transport checkpoint is incomplete.");
        }

        CheckpointResult<TransportIdSequences> ids =
            TransportIdSequences.RestoreCheckpoint(checkpoint.Ids);
        if (!ids.IsSuccess)
        {
            return Rejected($"{Path}.ids", ids.Failure!.Message);
        }

        CheckpointResult<IdSequence<ReservationId>> reservationIds =
            IdSequence<ReservationId>.RestoreCheckpoint(checkpoint.ReservationIds);
        if (!reservationIds.IsSuccess)
        {
            return Rejected(
                $"{Path}.reservationIds.nextValue",
                reservationIds.Failure!.Message);
        }

        CheckpointResult<IdSequence<CapacityReservationId>> capacityReservationIds =
            IdSequence<CapacityReservationId>.RestoreCheckpoint(
                checkpoint.CapacityReservationIds);
        if (!capacityReservationIds.IsSuccess)
        {
            return Rejected(
                $"{Path}.capacityReservationIds.nextValue",
                capacityReservationIds.Failure!.Message);
        }

        if (checkpoint.Timing.LoadingUnitsPerSecond == 0
            || checkpoint.Timing.UnloadingUnitsPerSecond == 0)
        {
            return Rejected(
                $"{Path}.timing",
                "Transport loading and unloading rates must be positive.");
        }

        var timing = new TransportTiming(
            checkpoint.Timing.DockingOverhead,
            new TransferRate(checkpoint.Timing.LoadingUnitsPerSecond),
            new TransferRate(checkpoint.Timing.UnloadingUnitsPerSecond));
        CheckpointResult<RestoredBoard> boardResult = RestoreBoard(
            checkpoint,
            inventories);
        if (!boardResult.IsSuccess)
        {
            return CheckpointResult<RestoredTransportOwner>.Rejected(boardResult.Failure!);
        }

        CheckpointResult<ShipRegistry> shipsResult = RestoreFreighters(
            checkpoint,
            inventories,
            liveShips,
            boardResult.Value!.Jobs);
        if (!shipsResult.IsSuccess)
        {
            return CheckpointResult<RestoredTransportOwner>.Rejected(shipsResult.Failure!);
        }

        CheckpointValidationFailure? relationshipFailure = ValidateRelationships(
            checkpoint,
            inventories,
            boardResult.Value!.Jobs,
            shipsResult.Value!);
        if (relationshipFailure is not null)
        {
            return CheckpointResult<RestoredTransportOwner>.Rejected(relationshipFailure);
        }

        return CheckpointResult<RestoredTransportOwner>.Success(
            new RestoredTransportOwner(
                boardResult.Value.Board,
                shipsResult.Value!,
                ids.Value!,
                reservationIds.Value!,
                capacityReservationIds.Value!,
                timing));
    }

    /// <summary>
    /// Restores market entries before jobs so every job can be checked against
    /// the exact retained supply and demand identities it consumed.
    /// </summary>
    private static CheckpointResult<RestoredBoard> RestoreBoard(
        TransportOwnerCheckpoint checkpoint,
        InventoryRegistry inventories)
    {
        CheckpointResult<SortedDictionary<SupplyOfferId, SupplyOffer>> supplies =
            RestoreSupplies(checkpoint, inventories);
        if (!supplies.IsSuccess)
        {
            return CheckpointResult<RestoredBoard>.Rejected(supplies.Failure!);
        }

        CheckpointResult<SortedDictionary<DemandRequestId, DemandRequest>> demands =
            RestoreDemands(checkpoint, inventories);
        if (!demands.IsSuccess)
        {
            return CheckpointResult<RestoredBoard>.Rejected(demands.Failure!);
        }

        CheckpointResult<SortedDictionary<TransportJobId, TransportJob>> jobs =
            RestoreJobs(checkpoint, supplies.Value!, demands.Value!, inventories);
        if (!jobs.IsSuccess)
        {
            return CheckpointResult<RestoredBoard>.Rejected(jobs.Failure!);
        }

        return CheckpointResult<RestoredBoard>.Success(new RestoredBoard(
            TransportBoard.RestoreDirect(
                supplies.Value!.Values,
                demands.Value!.Values,
                jobs.Value!.Values),
            jobs.Value!));
    }

    /// <summary>
    /// Validates retained offers independently of job linkage because zero
    /// remaining offers remain valid deterministic market history.
    /// </summary>
    private static CheckpointResult<SortedDictionary<SupplyOfferId, SupplyOffer>>
        RestoreSupplies(
            TransportOwnerCheckpoint checkpoint,
            InventoryRegistry inventories)
    {
        var supplies = new SortedDictionary<SupplyOfferId, SupplyOffer>(
            EntityIdComparer<SupplyOfferId>.Instance);
        for (int index = 0; index < checkpoint.Board.Supplies.Count; index++)
        {
            TransportSupplyCheckpoint? saved = checkpoint.Board.Supplies[index];
            string path = $"{Path}.board.supplies[{index}]";
            if (saved is null || !WasAllocated(saved.Id.Value, checkpoint.Ids.OfferIds)
                || saved.InventoryId.Value == 0 || inventories.Get(saved.InventoryId) is null
                || saved.LocationId.Value == 0 || saved.MaterialId.Value == 0
                || !supplies.TryAdd(
                    saved.Id,
                    new SupplyOffer(
                        saved.Id,
                        saved.InventoryId,
                        saved.LocationId,
                        saved.MaterialId,
                        saved.Remaining)))
            {
                return Reject<SortedDictionary<SupplyOfferId, SupplyOffer>>(
                    path,
                    "The supply offer is invalid, duplicated, unallocated, or references unknown inventory.");
            }
        }

        return CheckpointResult<SortedDictionary<SupplyOfferId, SupplyOffer>>.Success(
            supplies);
    }

    /// <summary>
    /// Validates retained demand state, including its stable priority and
    /// creation-time ordering inputs, before assignment can resume.
    /// </summary>
    private static CheckpointResult<SortedDictionary<DemandRequestId, DemandRequest>>
        RestoreDemands(
            TransportOwnerCheckpoint checkpoint,
            InventoryRegistry inventories)
    {
        var demands = new SortedDictionary<DemandRequestId, DemandRequest>(
            EntityIdComparer<DemandRequestId>.Instance);
        for (int index = 0; index < checkpoint.Board.Demands.Count; index++)
        {
            TransportDemandCheckpoint? saved = checkpoint.Board.Demands[index];
            string path = $"{Path}.board.demands[{index}]";
            if (saved is null || !WasAllocated(saved.Id.Value, checkpoint.Ids.DemandIds)
                || saved.InventoryId.Value == 0 || inventories.Get(saved.InventoryId) is null
                || saved.LocationId.Value == 0 || saved.MaterialId.Value == 0
                || !demands.TryAdd(
                    saved.Id,
                    new DemandRequest(
                        saved.Id,
                        saved.InventoryId,
                        saved.LocationId,
                        saved.MaterialId,
                        saved.Remaining,
                        saved.Priority,
                        saved.CreatedAt)))
            {
                return Reject<SortedDictionary<DemandRequestId, DemandRequest>>(
                    path,
                    "The demand is invalid, duplicated, unallocated, or references unknown inventory.");
            }
        }

        return CheckpointResult<SortedDictionary<DemandRequestId, DemandRequest>>.Success(
            demands);
    }

    /// <summary>
    /// Reconstructs jobs only after immutable job fields agree with their market
    /// references and status-specific scheduling shape.
    /// </summary>
    private static CheckpointResult<SortedDictionary<TransportJobId, TransportJob>>
        RestoreJobs(
            TransportOwnerCheckpoint checkpoint,
            SortedDictionary<SupplyOfferId, SupplyOffer> supplies,
            SortedDictionary<DemandRequestId, DemandRequest> demands,
            InventoryRegistry inventories)
    {
        var jobs = new SortedDictionary<TransportJobId, TransportJob>(
            EntityIdComparer<TransportJobId>.Instance);
        for (int index = 0; index < checkpoint.Board.Jobs.Count; index++)
        {
            TransportJobCheckpoint? saved = checkpoint.Board.Jobs[index];
            string path = $"{Path}.board.jobs[{index}]";
            if (saved is null || !WasAllocated(saved.Id.Value, checkpoint.Ids.JobIds)
                || saved.ShipId.Value == 0 || saved.Quantity == Quantity.Zero
                || saved.SourceReservationId.Value == 0
                || !WasAllocated(
                    saved.SourceReservationId.Value,
                    checkpoint.ReservationIds)
                || !Enum.IsDefined(saved.Status)
                || !jobs.TryAdd(saved.Id, RestoreJob(saved)))
            {
                return Reject<SortedDictionary<TransportJobId, TransportJob>>(
                    $"{path}.id",
                    "The transport job identity or fixed data is invalid, duplicated, or unallocated.");
            }

            TransportJob job = jobs[saved.Id];
            if (!supplies.TryGetValue(job.SupplyOfferId, out SupplyOffer? supply)
                || !demands.TryGetValue(job.DemandRequestId, out DemandRequest? demand)
                || supply.InventoryId != job.SourceInventoryId
                || supply.LocationId != job.SourceLocationId
                || supply.MaterialId != job.MaterialId
                || demand.InventoryId != job.DestinationInventoryId
                || demand.LocationId != job.DestinationLocationId
                || demand.MaterialId != job.MaterialId
                || inventories.Get(job.SourceInventoryId) is null
                || inventories.Get(job.DestinationInventoryId) is null)
            {
                return Reject<SortedDictionary<TransportJobId, TransportJob>>(
                    path,
                    "The transport job disagrees with its retained market or inventory references.");
            }

            bool hasTransition = job.Status is TransportJobStatus.TravelingToSource
                or TransportJobStatus.Loading
                or TransportJobStatus.TravelingToDestination
                or TransportJobStatus.Unloading;
            if (hasTransition != job.TransitionAt.HasValue
                || (job.TransitionAt is { } transition && transition < job.AssignedAt))
            {
                return Reject<SortedDictionary<TransportJobId, TransportJob>>(
                    $"{path}.transitionAt",
                    "The job transition time disagrees with its status or assignment time.");
            }

            if (job.DestinationCapacityReservationId is { } capacityId
                && !WasAllocated(capacityId.Value, checkpoint.CapacityReservationIds))
            {
                return Reject<SortedDictionary<TransportJobId, TransportJob>>(
                    $"{path}.destinationCapacityReservationId",
                    "The destination capacity reservation was not allocated.");
            }

            if ((job.Status == TransportJobStatus.Unloading)
                != job.DestinationCapacityReservationId.HasValue)
            {
                return Reject<SortedDictionary<TransportJobId, TransportJob>>(
                    $"{path}.destinationCapacityReservationId",
                    "Only an unloading job may retain destination capacity reservation identity.");
            }
        }

        return CheckpointResult<SortedDictionary<TransportJobId, TransportJob>>.Success(jobs);
    }

    private static TransportJob RestoreJob(TransportJobCheckpoint saved) =>
        new(
            saved.Id,
            saved.ShipId,
            saved.SupplyOfferId,
            saved.DemandRequestId,
            saved.SourceInventoryId,
            saved.SourceLocationId,
            saved.DestinationInventoryId,
            saved.DestinationLocationId,
            saved.MaterialId,
            saved.Quantity,
            saved.SourceReservationId,
            saved.AssignedAt)
        {
            DestinationCapacityReservationId = saved.DestinationCapacityReservationId,
            Generation = saved.Generation,
            Status = saved.Status,
            TransitionAt = saved.TransitionAt,
        };

    /// <summary>
    /// Binds logistics capability to already restored live ship and cargo
    /// identity, preventing a transport-only ship from being invented by load.
    /// </summary>
    private static CheckpointResult<ShipRegistry> RestoreFreighters(
        TransportOwnerCheckpoint checkpoint,
        InventoryRegistry inventories,
        IReadOnlyDictionary<ShipId, InventoryId> liveShips,
        IReadOnlyDictionary<TransportJobId, TransportJob> jobs)
    {
        var ids = new HashSet<ShipId>();
        var freighters = new List<Freighter>();
        for (int index = 0; index < checkpoint.Freighters.Count; index++)
        {
            TransportFreighterCheckpoint? saved = checkpoint.Freighters[index];
            string path = $"{Path}.freighters[{index}]";
            if (saved is null || saved.ShipId.Value == 0 || !ids.Add(saved.ShipId)
                || !liveShips.TryGetValue(saved.ShipId, out InventoryId cargoInventoryId))
            {
                return Reject<ShipRegistry>(
                    $"{path}.shipId",
                    "The freighter is invalid, duplicated, or not a live ship.");
            }

            if (saved.CargoInventoryId != cargoInventoryId
                || inventories.Get(saved.CargoInventoryId) is null)
            {
                return Reject<ShipRegistry>(
                    $"{path}.cargoInventoryId",
                    "The freighter cargo inventory disagrees with lifecycle authority.");
            }

            if (saved.LocationId.Value == 0)
            {
                return Reject<ShipRegistry>(
                    $"{path}.locationId",
                    "The freighter logistics location must be non-zero.");
            }

            if (saved.ActiveJobId is { } activeId
                && (!jobs.TryGetValue(activeId, out TransportJob? active)
                    || active.ShipId != saved.ShipId
                    || IsTerminal(active.Status)))
            {
                return Reject<ShipRegistry>(
                    $"{path}.activeJobId",
                    "The freighter active job is missing, terminal, or owned by another ship.");
            }

            freighters.Add(new Freighter(
                saved.ShipId,
                saved.LocationId,
                saved.CargoInventoryId)
            {
                ActiveJobId = saved.ActiveJobId,
            });
        }

        return CheckpointResult<ShipRegistry>.Success(
            ShipRegistry.RestoreFreightersDirect(freighters));
    }

    /// <summary>
    /// Checks bidirectional job ownership and both inventory commitment kinds
    /// after every individual object has been structurally validated.
    /// </summary>
    private static CheckpointValidationFailure? ValidateRelationships(
        TransportOwnerCheckpoint checkpoint,
        InventoryRegistry inventories,
        IReadOnlyDictionary<TransportJobId, TransportJob> jobs,
        ShipRegistry ships)
    {
        foreach ((TransportJobId jobId, TransportJob job) in jobs)
        {
            Freighter? freighter = ships.GetFreighter(job.ShipId);
            bool active = !IsTerminal(job.Status);
            int freighterIndex = FindFreighterIndex(checkpoint, job.ShipId);
            if ((active && freighter?.ActiveJobId != jobId)
                || (!active && freighter?.ActiveJobId == jobId))
            {
                return new CheckpointValidationFailure(
                    $"{Path}.freighters[{Math.Max(freighterIndex, 0)}].activeJobId",
                    "Every non-terminal job must be the exact active job of its freighter.");
            }

            CheckpointValidationFailure? commitmentFailure = ValidateJobCommitments(
                job,
                inventories,
                freighter,
                FindJobIndex(checkpoint, jobId));
            if (commitmentFailure is not null)
            {
                return commitmentFailure;
            }
        }

        return ValidateNoOrphanedCommitments(checkpoint, inventories, jobs);
    }

    /// <summary>
    /// Validates the phase-dependent handoff from source material reservation
    /// to loaded cargo and then destination capacity reservation.
    /// </summary>
    private static CheckpointValidationFailure? ValidateJobCommitments(
        TransportJob job,
        InventoryRegistry inventories,
        Freighter? freighter,
        int index)
    {
        string path = $"{Path}.board.jobs[{index}]";
        Inventory source = inventories.Get(job.SourceInventoryId)!;
        Reservation? sourceReservation = source.GetReservation(job.SourceReservationId);
        bool needsSourceReservation = job.Status is TransportJobStatus.Assigned
            or TransportJobStatus.WaitingForRouteToSource
            or TransportJobStatus.TravelingToSource
            or TransportJobStatus.Loading;
        bool validSource = sourceReservation is not null
            && sourceReservation.MaterialId == job.MaterialId
            && sourceReservation.Quantity == job.Quantity
            && sourceReservation.Owner is ReservationOwner.TransportJob owner
            && owner.JobId == job.Id;
        if (needsSourceReservation != validSource)
        {
            return new CheckpointValidationFailure(
                $"{path}.sourceReservationId",
                "The source reservation disagrees with the transport phase.");
        }

        Inventory destination = inventories.Get(job.DestinationInventoryId)!;
        CapacityReservation? capacity = job.DestinationCapacityReservationId is { } id
            ? destination.GetCapacityReservation(id)
            : null;
        bool validCapacity = capacity is not null
            && capacity.Quantity == job.Quantity
            && capacity.Owner is ReservationOwner.TransportJob capacityOwner
            && capacityOwner.JobId == job.Id;
        if ((job.Status == TransportJobStatus.Unloading) != validCapacity)
        {
            return new CheckpointValidationFailure(
                $"{path}.destinationCapacityReservationId",
                "The destination capacity reservation disagrees with the transport phase.");
        }

        bool carriesCargo = job.Status is TransportJobStatus.WaitingForRouteToDestination
            or TransportJobStatus.TravelingToDestination
            or TransportJobStatus.WaitingForDestinationCapacity
            or TransportJobStatus.Unloading;
        Inventory? cargo = freighter is null
            ? null
            : inventories.Get(freighter.CargoInventoryId);
        if (carriesCargo
            && (cargo is null || cargo.Available(job.MaterialId) < job.Quantity))
        {
            return new CheckpointValidationFailure(
                $"{path}.status",
                "The job phase requires cargo that is not present in its freighter inventory.");
        }

        return null;
    }

    /// <summary>
    /// Rejects transport-owned inventory commitments that cannot be reached
    /// from the restored job registry, including commitments in a wrong inventory.
    /// </summary>
    private static CheckpointValidationFailure? ValidateNoOrphanedCommitments(
        TransportOwnerCheckpoint checkpoint,
        InventoryRegistry inventories,
        IReadOnlyDictionary<TransportJobId, TransportJob> jobs)
    {
        foreach (InventoryCheckpoint? inventory in inventories.CaptureCheckpoint().Inventories)
        {
            foreach (Reservation? reservation in inventory!.Reservations)
            {
                if (reservation?.Owner is ReservationOwner.TransportJob owner
                    && (!jobs.TryGetValue(owner.JobId, out TransportJob? job)
                        || job.SourceInventoryId != inventory.Id
                        || job.SourceReservationId != reservation.Id))
                {
                    return new CheckpointValidationFailure(
                        $"{Path}.board.jobs",
                        "A transport material reservation is orphaned or stored in the wrong inventory.");
                }
            }

            foreach (CapacityReservation? reservation in inventory.CapacityReservations)
            {
                if (reservation?.Owner is ReservationOwner.TransportJob owner
                    && (!jobs.TryGetValue(owner.JobId, out TransportJob? job)
                        || job.DestinationInventoryId != inventory.Id
                        || job.DestinationCapacityReservationId != reservation.Id))
                {
                    return new CheckpointValidationFailure(
                        $"{Path}.board.jobs",
                        "A transport capacity reservation is orphaned or stored in the wrong inventory.");
                }
            }
        }

        return null;
    }

    private static bool IsTerminal(TransportJobStatus status) =>
        status is TransportJobStatus.Completed
            or TransportJobStatus.FailedBeforeLoading
            or TransportJobStatus.Cancelled;

    private static int FindJobIndex(
        TransportOwnerCheckpoint checkpoint,
        TransportJobId jobId) =>
        checkpoint.Board.Jobs
            .Select((job, index) => (job, index))
            .First(pair => pair.job!.Id == jobId).index;

    private static int FindFreighterIndex(
        TransportOwnerCheckpoint checkpoint,
        ShipId shipId) =>
        checkpoint.Freighters
            .Select((freighter, index) => (freighter, index))
            .FirstOrDefault(pair => pair.freighter?.ShipId == shipId).index;

    private static bool WasAllocated(ulong value, IdSequenceCheckpoint sequence) =>
        value != 0 && (sequence.NextValue is not { } next || value < next);

    private static CheckpointResult<T> Reject<T>(string path, string message)
        where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message));

    private static CheckpointResult<RestoredTransportOwner> Rejected(
        string path,
        string message) => Reject<RestoredTransportOwner>(path, message);

    private sealed record RestoredBoard(
        TransportBoard Board,
        IReadOnlyDictionary<TransportJobId, TransportJob> Jobs);
}
