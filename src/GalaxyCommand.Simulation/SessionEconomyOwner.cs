namespace GalaxyCommand.Simulation;

/// <summary>
/// Private aggregate owner for economy, construction, and transport state
/// created from a generic new-game seed.
/// </summary>
internal sealed class SessionEconomyOwner
{
    private readonly SortedDictionary<FacilityId, ConstructionProcess> _constructionProcesses =
        new(EntityIdComparer<FacilityId>.Instance);
    private readonly SortedDictionary<FacilityId, LocationId> _constructionLocations =
        new(EntityIdComparer<FacilityId>.Instance);
    private readonly SortedDictionary<FacilityId, LocationId> _productionLocations =
        new(EntityIdComparer<FacilityId>.Instance);
    private readonly SortedDictionary<FacilityId, MaterialId> _productionOutputs =
        new(EntityIdComparer<FacilityId>.Instance);
    private readonly SortedDictionary<FacilityId, ProductionLine> _productionLines =
        new(EntityIdComparer<FacilityId>.Instance);
    private readonly ConstructionIdSequences _constructionIds = new();
    private readonly IdSequence<CapacityReservationId> _capacityReservationIds = new();
    private readonly IdSequence<ReservationId> _reservationIds = new();
    private readonly InventoryRegistry _lifecycleInventories;
    private readonly ProductionIdSequences _productionIds = new();
    private readonly ShipRegistry _ships = new();
    private readonly TransportBoard _transportBoard = new();
    private readonly TransportIdSequences _transportIds = new();
    private readonly TransportTiming _transportTiming;

    /// <summary>
    /// Creates all private economic owners before the session is published.
    /// </summary>
    internal SessionEconomyOwner(
        GameSessionEconomySetup setup,
        EntityLifecycleOwner lifecycle)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(lifecycle);
        _transportTiming = setup.TransportTiming;
        _lifecycleInventories = lifecycle.Inventories;

        foreach (InitialInventorySetup inventorySetup in setup.Inventories)
        {
            var inventory = new Inventory(inventorySetup.InventoryId, inventorySetup.Capacity);
            foreach ((MaterialId materialId, Quantity quantity) in inventorySetup.StoredMaterials)
            {
                inventory.Add(materialId, quantity);
            }

            lifecycle.RegisterEconomyInventory(inventory);
        }

        var facilities = setup.Facilities.ToDictionary(facility => facility.FacilityId);
        foreach (ProductionFacilitySetup productionSetup in setup.ProductionFacilities)
        {
            EconomyFacilitySetup facility = facilities[productionSetup.FacilityId];
            var line = new ProductionLine(
                facility.FacilityId,
                facility.InventoryId,
                productionSetup.Throughput);
            line.Enqueue(_productionIds, productionSetup.Recipe, productionSetup.Repeat);
            _productionLines.Add(facility.FacilityId, line);
            _productionOutputs.Add(facility.FacilityId, productionSetup.Recipe.OutputMaterial);
            _productionLocations.Add(facility.FacilityId, facility.LogisticsLocationId);
        }

        foreach (ConstructionFacilitySetup constructionSetup in setup.ConstructionFacilities)
        {
            EconomyFacilitySetup facility = facilities[constructionSetup.FacilityId];
            _constructionProcesses.Add(
                facility.FacilityId,
                new ConstructionProcess(
                    facility.FacilityId,
                    facility.InventoryId,
                    constructionSetup.Throughput));
            _constructionLocations.Add(facility.FacilityId, facility.LogisticsLocationId);
        }

        foreach (InitialConstructionOrderSetup order in setup.ConstructionOrders)
        {
            _constructionProcesses[order.FacilityId].Enqueue(
                _constructionIds,
                setup.ShipDesigns[order.DesignId]);
        }

        foreach (InitialFreighterSetup freighter in setup.Freighters)
        {
            GameSessionShip ship = lifecycle.GetRequiredShip(freighter.ShipId);
            _ships.AddFreighter(
                freighter.ShipId,
                freighter.LogisticsLocationId,
                ship.CargoInventoryId);
        }

        Runtime = new EconomicRuntimeSystem(new EconomicRuntimeCoordinator(
            _productionLines,
            _productionOutputs,
            _productionLocations,
            _constructionProcesses,
            _constructionLocations,
            lifecycle.Inventories,
            _transportBoard,
            _ships,
            setup.Navigation,
            _productionIds,
            _transportIds,
            _reservationIds,
            _capacityReservationIds));
    }

    internal EconomicRuntimeSystem Runtime { get; }

    internal TransportTiming TransportTiming => _transportTiming;

    internal IReadOnlyDictionary<FacilityId, ConstructionProcess> ConstructionProcesses =>
        _constructionProcesses;

    internal ConstructionProcess GetRequiredConstructionProcess(FacilityId facilityId) =>
        _constructionProcesses.GetValueOrDefault(facilityId)
        ?? throw new InvalidOperationException(
            $"Economy construction facility {facilityId} does not exist.");

    /// <summary>
    /// Reads every non-terminal transport job that references the departing
    /// ship or cargo inventory and proves its release can commit.
    /// </summary>
    internal bool TryPrepareEntityRemoval(
        ShipId shipId,
        InventoryId cargoInventoryId,
        out PreparedEconomyEntityRemoval? prepared)
    {
        Freighter? departingFreighter = _ships.GetFreighter(shipId);
        TransportJob[] jobs = _transportBoard.Jobs
            .Where(job => job.Status is not (
                TransportJobStatus.Completed
                or TransportJobStatus.FailedBeforeLoading
                or TransportJobStatus.Cancelled))
            .Where(job => job.ShipId == shipId
                || job.SourceInventoryId == cargoInventoryId
                || job.DestinationInventoryId == cargoInventoryId)
            .OrderBy(job => job.Id.Value)
            .ToArray();
        foreach (TransportJob job in jobs)
        {
            Freighter? freighter = _ships.GetFreighter(job.ShipId);
            if (!_transportBoard.CanCancelForEntityRemoval(
                    job,
                    freighter,
                    _lifecycleInventories))
            {
                prepared = null;
                return false;
            }
        }

        prepared = new PreparedEconomyEntityRemoval(
            shipId,
            cargoInventoryId,
            departingFreighter is not null,
            jobs.Select(job => job.Id).ToArray());
        return true;
    }

    /// <summary>
    /// Releases prepared transport commitments before lifecycle discards the
    /// departing cargo inventory and removes the freighter capability.
    /// </summary>
    internal void ApplyEntityRemoval(PreparedEconomyEntityRemoval prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        foreach (TransportJobId jobId in prepared.TransportJobIds)
        {
            TransportJob job = _transportBoard.GetJob(jobId)
                ?? throw new InvalidOperationException(
                    $"Prepared transport job {jobId} no longer exists.");
            Freighter freighter = _ships.GetFreighter(job.ShipId)
                ?? throw new InvalidOperationException(
                    $"Prepared transport job {jobId} has no freighter.");
            if (!_transportBoard.CancelOrInterrupt(jobId, freighter, _lifecycleInventories))
            {
                throw new InvalidOperationException(
                    $"Prepared transport job {jobId} became terminal before removal.");
            }
        }

        if (prepared.RemoveFreighter && !_ships.RemoveFreighter(prepared.ShipId))
        {
            throw new InvalidOperationException(
                $"Prepared freighter {prepared.ShipId} no longer exists.");
        }
    }
}

internal sealed record PreparedEconomyEntityRemoval(
    ShipId ShipId,
    InventoryId CargoInventoryId,
    bool RemoveFreighter,
    IReadOnlyList<TransportJobId> TransportJobIds);
