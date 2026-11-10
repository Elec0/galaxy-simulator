namespace GalaxyCommand.Simulation;

internal sealed partial class SessionEconomyOwner
{
    private readonly SortedDictionary<FacilityId, RetiredEconomyFacilityCheckpoint> _retiredFacilities = NewDictionary<RetiredEconomyFacilityCheckpoint>();

    /// <summary>Resolves immutable terminal order identity for retained command receipts without reviving its provider.</summary>
    internal bool TryResolveArchivedConstructionOrder(FacilityId facility, ConstructionOrderId order, out ConstructionDesignId design)
    {
        var saved = _retiredFacilities.GetValueOrDefault(facility)?.Construction?.Orders.FirstOrDefault(value => value!.Id == order);
        design = saved?.DesignId ?? default;
        return saved is not null;
    }

    /// <summary>Retains immutable descriptors for cross-owner retired station and module validation.</summary>
    internal IReadOnlyList<EconomyFacilityCheckpoint> CaptureRetiredStationFacilities() =>
        Array.AsReadOnly(_retiredFacilities.Values.Select(value => value.Facility).ToArray());

    /// <summary>Returns historical outputs only where the shared physical storage still exists.</summary>
    internal IReadOnlyList<EconomyFacilityCheckpoint> CaptureArchivedProductionSupplyDescriptors() =>
        Array.AsReadOnly(_retiredFacilities.Values.Select(value => value.Facility)
            .Where(value => value.ProductionOutput is not null && _lifecycleInventories.Get(value.InventoryId) is not null).ToArray());

    /// <summary>
    /// Retires only already-cancelled providers. Historical jobs remain addressable, while the runtime's
    /// shared live maps stop evaluating these owners. Physical supply belongs to surviving station storage.
    /// </summary>
    private void ArchiveStationFacilities(StationId station, IReadOnlyList<StationModuleId> modules, bool destroyBase)
    {
        HashSet<StationModuleId> removed = modules.ToHashSet();
        var facilities = _facilities.Values.Where(value => value.StationBinding is { } binding
            && binding.StationId == station && (destroyBase || removed.Contains(binding.ModuleId))).ToArray();
        foreach (var facility in facilities)
        {
            ProductionLineCheckpoint? production = _productionLines.GetValueOrDefault(facility.FacilityId)?.CaptureCheckpoint();
            ConstructionProcessCheckpoint? construction = _constructionProcesses.GetValueOrDefault(facility.FacilityId)?.CaptureCheckpoint();
            if (production?.Jobs.Any(job => job!.Status is not (ProductionJobStatus.Completed or ProductionJobStatus.Cancelled)) == true
                || construction?.Orders.Any(order => order!.Status is not (ConstructionOrderStatus.Completed or ConstructionOrderStatus.Cancelled)) == true)
                throw new InvalidOperationException("A retired facility still owns active work.");
            _retiredFacilities.Add(facility.FacilityId, FreezeArchive(new(facility, production, construction)));
            _facilities.Remove(facility.FacilityId);
            _productionLines.Remove(facility.FacilityId);
            _productionLocations.Remove(facility.FacilityId);
            _productionOutputs.Remove(facility.FacilityId);
            _constructionProcesses.Remove(facility.FacilityId);
            _constructionLocations.Remove(facility.FacilityId);
        }
        if (destroyBase)
        {
            // The last provider may have retired earlier while loaded shipments and physical supply survived.
            HashSet<InventoryId> inventories = facilities.Concat(_retiredFacilities.Values.Select(value => value.Facility)
                .Where(value => value.StationBinding?.StationId == station)).Select(value => value.InventoryId).ToHashSet();
            foreach (var offer in _transportBoard.Supplies.Where(value => inventories.Contains(value.InventoryId))) offer.Remaining = Quantity.Zero;
            foreach (var demand in _transportBoard.Demands.Where(value => inventories.Contains(value.InventoryId))) demand.Remaining = Quantity.Zero;
        }
    }

    /// <summary>Copies nested collections so checkpoints cannot mutate retained terminal authority.</summary>
    private static RetiredEconomyFacilityCheckpoint FreezeArchive(RetiredEconomyFacilityCheckpoint archive) => archive with
    {
        Production = archive.Production is not { } line ? null : line with
        {
            QueuedJobIds = Array.AsReadOnly(line.QueuedJobIds.ToArray()),
            Jobs = Array.AsReadOnly(line.Jobs.Select(job => job is null ? null : job with
            {
                Recipe = job.Recipe is not { } recipe ? null : recipe with { Inputs = Array.AsReadOnly(recipe.Inputs.ToArray()) },
                Reservations = Array.AsReadOnly(job.Reservations.ToArray()),
            }).ToArray()),
        },
        Construction = archive.Construction is not { } process ? null : process with
        {
            QueuedOrderIds = Array.AsReadOnly(process.QueuedOrderIds.ToArray()),
            Orders = Array.AsReadOnly(process.Orders.Select(order => order is null ? null : order with
            { Reservations = Array.AsReadOnly(order.Reservations.ToArray()) }).ToArray()),
            PendingMaterializations = Array.AsReadOnly(process.PendingMaterializations.ToArray()),
            MaterializationReceipts = Array.AsReadOnly(process.MaterializationReceipts.ToArray()),
        },
    };

    /// <summary>
    /// Proves archives contain terminal history only, disjoint allocated identities, consistent anchors,
    /// and no surviving inventory claims. Missing inventories are legal history only through these descriptors.
    /// </summary>
    private static CheckpointValidationFailure? ValidateArchives(SessionEconomyCheckpoint checkpoint,
        InventoryRegistry inventories, WorldTopology topology, ConstructionDesignCatalog designs)
    {
        const string path = "$.checkpoint.economy.retiredFacilities";
        if (checkpoint.RetiredFacilities is null || checkpoint.FacilityIds is null || checkpoint.LocationIds is null
            || !IdSequence<FacilityId>.RestoreCheckpoint(checkpoint.FacilityIds).IsSuccess
            || !IdSequence<LocationId>.RestoreCheckpoint(checkpoint.LocationIds).IsSuccess)
            return new(path, "Exact provider allocator state and terminal archive registry are required.");
        var facilities = checkpoint.Facilities.Select(value => value!).ToList();
        var facilityIds = facilities.Select(value => value.FacilityId).ToHashSet();
        var jobs = checkpoint.Production.Lines.SelectMany(value => value!.Jobs).Select(value => value!.Id).ToHashSet();
        var orders = checkpoint.Construction.Processes.SelectMany(value => value!.Orders).Select(value => value!.Id).ToHashSet();
        var retiredJobs = new HashSet<ProductionJobId>();
        var retiredOrders = new HashSet<ConstructionOrderId>();
        var systems = topology.Systems.Select(value => value.Id).ToHashSet();
        foreach (var archive in checkpoint.RetiredFacilities)
        {
            if (archive?.Facility is not { } facility || facility.StationBinding is not { } binding
                || binding.StationId.Value == 0 || binding.ModuleId.Value == 0 || binding.StationEntityId.Value == 0
                || facility.InventoryId.Value == 0 || facility.LocationId.Value == 0 || facility.FacilityId.Value == 0
                || !systems.Contains(facility.Position.SystemId) || facility.ProductionOutput is { Value: 0 }
                || !facilityIds.Add(facility.FacilityId))
                return new(path, "Retired facility descriptor is invalid or duplicated.");
            if ((facility.ProductionOutput is not null) != (archive.Production is not null)
                || archive.Production is { } production && (production.FacilityId != facility.FacilityId || production.InventoryId != facility.InventoryId)
                || archive.Construction is { } construction && (construction.FacilityId != facility.FacilityId || construction.InventoryId != facility.InventoryId))
                return new(path, "Retired workflows disagree with their facility descriptor.");
            if (archive.Production is { } line)
            {
                var failure = ProductionCheckpointRestore.ValidateArchive(line, checkpoint.Production.JobIds, jobs, path);
                if (failure is not null) return failure;
                retiredJobs.UnionWith(line.Jobs.Select(value => value!.Id));
            }
            if (archive.Construction is { } process)
            {
                var failure = ConstructionCheckpointRestore.ValidateArchive(process, checkpoint.Construction.OrderIds, orders, designs, path);
                if (failure is not null) return failure;
                retiredOrders.UnionWith(process.Orders.Select(value => value!.Id));
            }
            facilities.Add(facility);
        }
        foreach (var facility in facilities)
            if (!Allocated(facility.FacilityId.Value, checkpoint.FacilityIds) || !Allocated(facility.LocationId.Value, checkpoint.LocationIds))
                return new(path, "Provider allocator state does not cover live and retired identities.");
        foreach (var group in facilities.GroupBy(value => value.LocationId))
        {
            var first = group.First();
            if (group.Skip(1).Any(value => first.StationBinding is not { } binding
                || value.StationBinding?.StationId != binding.StationId || value.StationBinding.StationEntityId != binding.StationEntityId
                || value.InventoryId != first.InventoryId || value.Position != first.Position))
                return new(path, "Retained logistics anchors have conflicting bindings.");
        }
        foreach (var inventory in inventories.CaptureCheckpoint().Inventories)
        {
            if (inventory!.Reservations.Any(value => IsRetired(value.Owner))
                || inventory.CapacityReservations.Any(value => IsRetired(value.Owner))
                || inventory.PhysicalReservations.Any(value => IsRetired(value.Owner)))
                return new(path, "A retired workflow retains a live inventory commitment.");
        }
        return null;

        bool IsRetired(ReservationOwner owner) => owner is ReservationOwner.ProductionJob production && retiredJobs.Contains(production.JobId)
            || owner is ReservationOwner.ConstructionOrder construction && retiredOrders.Contains(construction.OrderId);
    }

    /// <summary>Requires a retained identity to precede the exact saved allocator position.</summary>
    private static bool Allocated(ulong value, IdSequenceCheckpoint sequence) => value != 0
        && (sequence.NextValue is not { } next || value < next);
}
