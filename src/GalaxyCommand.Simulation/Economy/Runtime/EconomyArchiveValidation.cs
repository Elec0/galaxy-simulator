namespace GalaxyCommand.Simulation;

internal static partial class ProductionCheckpointRestore
{
    /// <summary>Validates terminal history without requiring or constructing a live inventory owner.</summary>
    internal static CheckpointValidationFailure? ValidateArchive(ProductionLineCheckpoint line,
        IdSequenceCheckpoint sequence, HashSet<ProductionJobId> globalIds, string path)
    {
        if (line.Throughput.UnitsPerSecond == 0 || line.IsProviderAvailable || line.ActiveJobId is not null
            || line.QueuedJobIds.Count != 0)
            return new(path, "A retired production owner cannot admit or schedule work.");
        foreach (var job in line.Jobs)
        {
            if (job is null || !WasAllocated(job.Id.Value, sequence) || !globalIds.Add(job.Id)
                || job.Status is not (ProductionJobStatus.Completed or ProductionJobStatus.Cancelled)
                || job.Reservations.Count != 0 || (job.Status == ProductionJobStatus.Completed) != job.CompletesAt.HasValue)
                return new(path, "Retired production history has an invalid identity, active work or commitment.");
            var recipe = RestoreRecipe(job.Recipe, path);
            if (!recipe.IsSuccess) return recipe.Failure;
            if (!EconomicJobWorkState.IsValidCheckpoint(job.WorkState, recipe.Value!.RequiredWork, line.Throughput,
                false, false, false, job.Status == ProductionJobStatus.Completed, job.CompletesAt))
                return new(path, "Retired production work progress is inconsistent.");
        }
        return null;
    }
}

internal static partial class ConstructionCheckpointRestore
{
    /// <summary>Validates terminal orders and receipts against resolved immutable designs without reviving work.</summary>
    internal static CheckpointValidationFailure? ValidateArchive(ConstructionProcessCheckpoint process,
        IdSequenceCheckpoint sequence, HashSet<ConstructionOrderId> globalIds, ConstructionDesignCatalog designs, string path)
    {
        if (process.Throughput.UnitsPerSecond == 0 || process.IsProviderAvailable || process.ActiveOrderId is not null
            || process.QueuedOrderIds.Count != 0 || process.PendingMaterializations.Count != 0)
            return new(path, "A retired construction owner cannot admit, schedule or materialize work.");
        var orders = new SortedDictionary<ConstructionOrderId, ConstructionOrder>(EntityIdComparer<ConstructionOrderId>.Instance);
        foreach (var saved in process.Orders)
        {
            if (saved is null || !WasAllocated(saved.Id.Value, sequence) || !globalIds.Add(saved.Id)
                || saved.Status is not (ConstructionOrderStatus.Completed or ConstructionOrderStatus.Cancelled)
                || saved.Reservations.Count != 0 || (saved.Status == ConstructionOrderStatus.Completed) != saved.CompletesAt.HasValue
                || designs.Get(saved.DesignId) is not { } design)
                return new(path, "Retired construction history has an invalid identity, design, active work or commitment.");
            if (!EconomicJobWorkState.IsValidCheckpoint(saved.WorkState, design.Recipe.RequiredWork, process.Throughput,
                false, false, false, saved.Status == ConstructionOrderStatus.Completed, saved.CompletesAt))
                return new(path, "Retired construction work progress is inconsistent.");
            orders.Add(saved.Id, new ConstructionOrder(saved.Id, design)
            {
                Status = saved.Status, CompletesAt = saved.CompletesAt, Generation = saved.Generation, WorkState = saved.WorkState,
            });
        }
        var receipts = RestoreReceipts(process, orders, path);
        if (!receipts.IsSuccess) return receipts.Failure;
        if (!orders.Values.Where(order => order.Status == ConstructionOrderStatus.Completed).Select(order => order.Id).ToHashSet()
            .SetEquals(receipts.Value!.Select(receipt => receipt.Effect.OrderId)))
            return new(path, "Completed retired orders require exactly one immutable materialization receipt.");
        return null;
    }
}
