using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed class RestoredProductionOwner
{
    private readonly ReadOnlyDictionary<FacilityId, ProductionLine> _lines;

    internal RestoredProductionOwner(
        ProductionIdSequences ids,
        IDictionary<FacilityId, ProductionLine> lines)
    {
        Ids = ids;
        _lines = new ReadOnlyDictionary<FacilityId, ProductionLine>(
            new SortedDictionary<FacilityId, ProductionLine>(
                lines,
                EntityIdComparer<FacilityId>.Instance));
    }

    internal ProductionIdSequences Ids { get; }

    internal IReadOnlyDictionary<FacilityId, ProductionLine> Lines => _lines;

    internal ProductionOwnerCheckpoint CaptureCheckpoint() =>
        new(
            Ids.CaptureCheckpoint(),
            _lines.Values.Select(line => line.CaptureCheckpoint()).ToArray());
}

internal static class ProductionCheckpointRestore
{
    private const string Path = "$.checkpoint.economy.production";

    /// <summary>
    /// Validates and directly restores production state against the already
    /// restored shared inventories without allocating jobs or replaying work.
    /// </summary>
    internal static CheckpointResult<RestoredProductionOwner> Restore(
        ProductionOwnerCheckpoint checkpoint,
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(inventories);
        CheckpointResult<ProductionIdSequences> idsResult =
            ProductionIdSequences.RestoreCheckpoint(checkpoint.JobIds);
        if (!idsResult.IsSuccess)
        {
            return Rejected($"{Path}.jobIds.nextValue", idsResult.Failure!.Message);
        }

        var facilityIds = new HashSet<FacilityId>();
        var jobIds = new HashSet<ProductionJobId>();
        var lines = new Dictionary<FacilityId, ProductionLine>();
        for (int index = 0; index < checkpoint.Lines.Count; index++)
        {
            ProductionLineCheckpoint? line = checkpoint.Lines[index];
            string linePath = $"{Path}.lines[{index}]";
            if (line is null || line.FacilityId.Value == 0
                || !facilityIds.Add(line.FacilityId))
            {
                return Rejected(
                    $"{linePath}.facilityId",
                    "The production facility identity is missing or duplicated.");
            }

            Inventory? inventory = inventories.Get(line.InventoryId);
            if (inventory is null)
            {
                return Rejected(
                    $"{linePath}.inventoryId",
                    "The production inventory is not restored.");
            }

            if (line.Throughput.UnitsPerSecond == 0)
            {
                return Rejected(
                    $"{linePath}.throughput",
                    "Production throughput must be positive.");
            }

            CheckpointResult<ProductionLine> lineResult = RestoreLine(
                line,
                inventory,
                checkpoint.JobIds,
                jobIds,
                linePath);
            if (!lineResult.IsSuccess)
            {
                return CheckpointResult<RestoredProductionOwner>.Rejected(
                    lineResult.Failure!);
            }

            lines.Add(line.FacilityId, lineResult.Value!);
        }

        return CheckpointResult<RestoredProductionOwner>.Success(
            new RestoredProductionOwner(idsResult.Value!, lines));
    }

    /// <summary>
    /// Reconstructs one line only after its complete job registry, active slot,
    /// FIFO queue, and terminal-state partition have been proven consistent.
    /// </summary>
    private static CheckpointResult<ProductionLine> RestoreLine(
        ProductionLineCheckpoint checkpoint,
        Inventory inventory,
        IdSequenceCheckpoint jobSequence,
        HashSet<ProductionJobId> globalJobIds,
        string path)
    {
        var jobs = new SortedDictionary<ProductionJobId, ProductionJob>(
            EntityIdComparer<ProductionJobId>.Instance);
        for (int index = 0; index < checkpoint.Jobs.Count; index++)
        {
            ProductionJobCheckpoint? saved = checkpoint.Jobs[index];
            string jobPath = $"{path}.jobs[{index}]";
            if (saved is null || !WasAllocated(saved.Id.Value, jobSequence)
                || !globalJobIds.Add(saved.Id))
            {
                return RejectedLine(
                    $"{jobPath}.id",
                    "The production job identity is invalid, duplicated, or unallocated.");
            }

            CheckpointResult<Recipe> recipeResult = RestoreRecipe(saved.Recipe, jobPath);
            if (!recipeResult.IsSuccess)
            {
                return CheckpointResult<ProductionLine>.Rejected(recipeResult.Failure!);
            }

            if (!Enum.IsDefined(saved.Status))
            {
                return RejectedLine($"{jobPath}.status", "The job status is undefined.");
            }

            bool requiresCompletion = saved.Status is ProductionJobStatus.Running
                or ProductionJobStatus.CompletedAwaitingStorage
                or ProductionJobStatus.Completed;
            if (requiresCompletion != saved.CompletesAt.HasValue)
            {
                return RejectedLine(
                    $"{jobPath}.completesAt",
                    "The job completion time disagrees with its status.");
            }

            var job = new ProductionJob(saved.Id, recipeResult.Value!, saved.IsRepeating)
            {
                Status = saved.Status,
                CompletesAt = saved.CompletesAt,
                Generation = saved.Generation,
            };
            CheckpointValidationFailure? reservationFailure = RestoreReservations(
                saved,
                job,
                inventory,
                jobPath);
            if (reservationFailure is not null)
            {
                return CheckpointResult<ProductionLine>.Rejected(reservationFailure);
            }

            if (saved.Status != ProductionJobStatus.WaitingForInputs
                && saved.Reservations.Count != 0)
            {
                return RejectedLine(
                    $"{jobPath}.reservations",
                    "Only a job waiting for inputs may retain reservations.");
            }

            jobs.Add(job.Id, job);
        }

        var linkedReservations = jobs.Values
            .SelectMany(job => job.AllReservationIds())
            .ToHashSet();
        foreach (Reservation reservation in inventory.CaptureCheckpoint().Reservations)
        {
            if (reservation.Owner is ReservationOwner.ProductionJob owner
                && jobs.ContainsKey(owner.JobId)
                && !linkedReservations.Contains(reservation.Id))
            {
                return RejectedLine(
                    $"{path}.jobs",
                    "A production-owned inventory reservation is missing from its job links.");
            }
        }

        if (checkpoint.ActiveJobId is { } activeId
            && (!jobs.TryGetValue(activeId, out ProductionJob? active)
                || active.Status is ProductionJobStatus.Completed
                    or ProductionJobStatus.Cancelled))
        {
            return RejectedLine(
                $"{path}.activeJobId",
                "The active job is missing or terminal.");
        }

        if (checkpoint.ActiveJobId is null && checkpoint.QueuedJobIds.Count != 0)
        {
            return RejectedLine(
                $"{path}.activeJobId",
                "A non-empty production queue requires its promoted active job.");
        }

        var queued = new HashSet<ProductionJobId>();
        for (int index = 0; index < checkpoint.QueuedJobIds.Count; index++)
        {
            ProductionJobId queuedId = checkpoint.QueuedJobIds[index];
            if (!jobs.TryGetValue(queuedId, out ProductionJob? queuedJob)
                || queuedJob.Status != ProductionJobStatus.WaitingForInputs
                || queuedId == checkpoint.ActiveJobId
                || !queued.Add(queuedId))
            {
                return RejectedLine(
                    $"{path}.queuedJobIds[{index}]",
                    "A queued job is missing, duplicated, active, or not waiting.");
            }
        }

        foreach (ProductionJob job in jobs.Values)
        {
            bool assigned = job.Id == checkpoint.ActiveJobId || queued.Contains(job.Id);
            bool terminal = job.Status is ProductionJobStatus.Completed
                or ProductionJobStatus.Cancelled;
            if (assigned == terminal)
            {
                return RejectedLine(
                    $"{path}.jobs",
                    "Every non-terminal job must be active or queued and terminal jobs cannot be assigned.");
            }
        }

        return CheckpointResult<ProductionLine>.Success(ProductionLine.RestoreDirect(
            checkpoint.FacilityId,
            checkpoint.InventoryId,
            checkpoint.Throughput,
            jobs,
            checkpoint.ActiveJobId,
            checkpoint.QueuedJobIds));
    }

    /// <summary>
    /// Validates the closed recipe definition used for future repeated jobs.
    /// </summary>
    private static CheckpointResult<Recipe> RestoreRecipe(
        ProductionRecipeCheckpoint? checkpoint,
        string jobPath)
    {
        string path = $"{jobPath}.recipe";
        if (checkpoint is null || checkpoint.Inputs is null
            || checkpoint.OutputMaterial.Value == 0
            || checkpoint.OutputQuantity == Quantity.Zero)
        {
            return RejectedRecipe(path, "The production recipe is incomplete or invalid.");
        }

        var inputs = new SortedDictionary<MaterialId, Quantity>(
            EntityIdComparer<MaterialId>.Instance);
        for (int index = 0; index < checkpoint.Inputs.Count; index++)
        {
            ConstructionInputPolicyCheckpoint? input = checkpoint.Inputs[index];
            if (input is null || input.MaterialId.Value == 0
                || input.Quantity == Quantity.Zero
                || !inputs.TryAdd(input.MaterialId, input.Quantity))
            {
                return RejectedRecipe(
                    $"{path}.inputs[{index}]",
                    "A recipe input is invalid or duplicated.");
            }
        }

        return CheckpointResult<Recipe>.Success(new Recipe(
            inputs,
            checkpoint.OutputMaterial,
            checkpoint.OutputQuantity,
            checkpoint.RequiredWork));
    }

    /// <summary>
    /// Rebuilds job-local reservation links only when each shared inventory
    /// reservation names the same production job and material.
    /// </summary>
    private static CheckpointValidationFailure? RestoreReservations(
        ProductionJobCheckpoint checkpoint,
        ProductionJob job,
        Inventory inventory,
        string path)
    {
        var ids = new HashSet<ReservationId>();
        foreach ((ProductionReservationLinkCheckpoint? link, int index) in
                 checkpoint.Reservations.Select((value, index) => (value, index)))
        {
            Reservation? reservation = link is null
                ? null
                : inventory.GetReservation(link.ReservationId);
            if (link is null || link.MaterialId.Value == 0 || !ids.Add(link.ReservationId)
                || reservation is null
                || reservation.MaterialId != link.MaterialId
                || reservation.Owner is not ReservationOwner.ProductionJob owner
                || owner.JobId != job.Id)
            {
                return new CheckpointValidationFailure(
                    $"{path}.reservations[{index}]",
                    "The job reservation link disagrees with shared inventory authority.");
            }

            job.AddReservation(link.MaterialId, link.ReservationId);
        }

        return null;
    }

    private static bool WasAllocated(ulong value, IdSequenceCheckpoint sequence) =>
        value != 0 && (sequence.NextValue is not { } next || value < next);

    private static CheckpointResult<T> Reject<T>(string path, string message)
        where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message));

    private static CheckpointResult<RestoredProductionOwner> Rejected(
        string path,
        string message) => Reject<RestoredProductionOwner>(path, message);

    private static CheckpointResult<ProductionLine> RejectedLine(
        string path,
        string message) => Reject<ProductionLine>(path, message);

    private static CheckpointResult<Recipe> RejectedRecipe(
        string path,
        string message) => Reject<Recipe>(path, message);
}
