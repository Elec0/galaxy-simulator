namespace GalaxyCommand.Simulation;

/// <summary>
/// Private aggregate owner for economy, construction, and transport state
/// created from a generic new-game seed.
/// </summary>
internal sealed class SessionEconomyOwner
{
    private readonly SortedDictionary<FacilityId, ConstructionProcess> _constructionProcesses;
    private readonly SortedDictionary<FacilityId, LocationId> _constructionLocations;
    private readonly SortedDictionary<FacilityId, EconomyFacilityCheckpoint> _facilities;
    private readonly SortedDictionary<FacilityId, LocationId> _productionLocations;
    private readonly SortedDictionary<FacilityId, MaterialId> _productionOutputs;
    private readonly SortedDictionary<FacilityId, ProductionLine> _productionLines;
    private readonly ConstructionIdSequences _constructionIds;
    private readonly IdSequence<CapacityReservationId> _capacityReservationIds;
    private readonly IdSequence<ReservationId> _reservationIds;
    private readonly InventoryRegistry _lifecycleInventories;
    private readonly ILogisticsNavigation _navigation;
    private readonly ProductionIdSequences _productionIds;
    private readonly ShipRegistry _ships;
    private readonly TransportBoard _transportBoard;
    private readonly TransportIdSequences _transportIds;
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
        _constructionProcesses = NewDictionary<ConstructionProcess>();
        _constructionLocations = NewDictionary<LocationId>();
        _facilities = NewDictionary<EconomyFacilityCheckpoint>();
        _productionLocations = NewDictionary<LocationId>();
        _productionOutputs = NewDictionary<MaterialId>();
        _productionLines = NewDictionary<ProductionLine>();
        _constructionIds = new ConstructionIdSequences();
        _capacityReservationIds = new IdSequence<CapacityReservationId>();
        _reservationIds = new IdSequence<ReservationId>();
        _productionIds = new ProductionIdSequences();
        _ships = new ShipRegistry();
        _transportBoard = new TransportBoard();
        _transportIds = new TransportIdSequences();
        _transportTiming = setup.TransportTiming;
        _lifecycleInventories = lifecycle.Inventories;
        _navigation = setup.Navigation;

        Dictionary<InventoryId, EconomyFacilitySetup>? facilitiesByInventory =
            setup.MaterialCompatibility is null
                ? null
                : setup.Facilities.ToDictionary(facility => facility.InventoryId);
        foreach (InitialInventorySetup inventorySetup in setup.Inventories)
        {
            Inventory inventory;
            if (setup.MaterialCompatibility is { } compatibility)
            {
                EconomyFacilitySetup facility =
                    facilitiesByInventory![inventorySetup.InventoryId];
                inventory = new Inventory(
                    inventorySetup.InventoryId,
                    new InventoryCustody(
                        new InventoryOwnerReference.Facility(facility.FacilityId),
                        facility.ControllingPrincipalId!.Value),
                    inventorySetup.Capacity,
                    compatibility);
            }
            else
            {
                inventory = new Inventory(
                    inventorySetup.InventoryId,
                    inventorySetup.Capacity);
            }

            foreach ((MaterialId materialId, Quantity quantity) in inventorySetup.StoredMaterials)
            {
                inventory.Add(materialId, quantity);
            }

            lifecycle.RegisterEconomyInventory(inventory);
        }

        var facilities = setup.Facilities.ToDictionary(facility => facility.FacilityId);
        foreach (EconomyFacilitySetup facility in setup.Facilities)
        {
            MaterialId? output = setup.ProductionFacilities
                .FirstOrDefault(production => production.FacilityId == facility.FacilityId)
                ?.Recipe.OutputMaterial;
            _facilities.Add(
                facility.FacilityId,
                new EconomyFacilityCheckpoint(
                    facility.FacilityId,
                    facility.InventoryId,
                    facility.LogisticsLocationId,
                    facility.Position,
                    output));
        }
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

        Runtime = CreateRuntime();
    }

    private SessionEconomyOwner(
        InventoryRegistry inventories,
        ILogisticsNavigation navigation,
        SortedDictionary<FacilityId, EconomyFacilityCheckpoint> facilities,
        RestoredProductionOwner production,
        RestoredConstructionOwner construction,
        RestoredTransportOwner transport)
    {
        _lifecycleInventories = inventories;
        _navigation = navigation;
        _facilities = facilities;
        _productionLines = NewDictionary<ProductionLine>();
        foreach ((FacilityId id, ProductionLine line) in production.Lines)
        {
            _productionLines.Add(id, line);
        }

        _productionIds = production.Ids;
        _constructionProcesses = NewDictionary<ConstructionProcess>();
        foreach ((FacilityId id, ConstructionProcess process) in construction.Processes)
        {
            _constructionProcesses.Add(id, process);
        }

        _constructionIds = construction.Ids;
        _transportBoard = transport.Board;
        _ships = transport.Ships;
        _transportIds = transport.Ids;
        _reservationIds = transport.ReservationIds;
        _capacityReservationIds = transport.CapacityReservationIds;
        _transportTiming = transport.Timing;
        _productionOutputs = NewDictionary<MaterialId>();
        _productionLocations = NewDictionary<LocationId>();
        _constructionLocations = NewDictionary<LocationId>();
        foreach ((FacilityId id, EconomyFacilityCheckpoint facility) in facilities)
        {
            if (_productionLines.ContainsKey(id))
            {
                _productionOutputs.Add(id, facility.ProductionOutput!.Value);
                _productionLocations.Add(id, facility.LocationId);
            }

            if (_constructionProcesses.ContainsKey(id))
            {
                _constructionLocations.Add(id, facility.LocationId);
            }
        }

        Runtime = CreateRuntime();
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
    /// Reports whether a pending economic event names retained workflow state;
    /// generation and status mismatches remain valid stale-event behavior.
    /// </summary>
    internal bool ContainsEventReference(EconomicEvent economicEvent) =>
        economicEvent switch
        {
            EconomicEvent.ProductionComplete production =>
                _productionLines.TryGetValue(
                    production.FacilityId,
                    out ProductionLine? line)
                && line.GetJob(production.JobId) is not null,
            EconomicEvent.ConstructionComplete construction =>
                _constructionProcesses.TryGetValue(
                    construction.FacilityId,
                    out ConstructionProcess? process)
                && process.GetOrder(construction.OrderId) is not null,
            EconomicEvent.Transport transport =>
                _transportBoard.GetJob(transport.Event.JobId) is not null,
            _ => false,
        };

    /// <summary>
    /// Captures facility configuration and every economic workflow only when
    /// logistics is bound to the same registered spatial planner as the session.
    /// </summary>
    internal CheckpointResult<SessionEconomyCheckpoint> CaptureCheckpoint(
        ISpatialNavigationPlanner navigation)
    {
        if (_navigation is not HierarchicalLogisticsNavigation logistics
            || !ReferenceEquals(logistics.Planner, navigation)
            || !AnchorsMatch(logistics.Anchors))
        {
            return EconomyRejected(
                "$.checkpoint.economy.navigation",
                "Logistics navigation is not registered against the session planner and facility anchors.");
        }

        return CheckpointResult<SessionEconomyCheckpoint>.Success(
            new SessionEconomyCheckpoint(
                _facilities.Values.ToArray(),
                new ProductionOwnerCheckpoint(
                    _productionIds.CaptureCheckpoint(),
                    _productionLines.Values.Select(line => line.CaptureCheckpoint()).ToArray()),
                new ConstructionOwnerCheckpoint(
                    _constructionIds.CaptureCheckpoint(),
                    _constructionProcesses.Values.Select(process =>
                        process.CaptureCheckpoint()).ToArray()),
                TransportCheckpointCapture.Capture(
                    _transportBoard,
                    _ships,
                    _transportIds,
                    _reservationIds,
                    _capacityReservationIds,
                    _transportTiming)));
    }

    /// <summary>
    /// Restores economic owners against lifecycle inventories and resolved
    /// content, publishing none of them unless every facility binding validates.
    /// </summary>
    internal static CheckpointResult<SessionEconomyOwner> RestoreCheckpoint(
        SessionEconomyCheckpoint checkpoint,
        EntityLifecycleOwner lifecycle,
        WorldTopology topology,
        ISpatialNavigationPlanner navigation,
        IEnumerable<ShipMaterializationPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(policies);
        CheckpointResult<SortedDictionary<FacilityId, EconomyFacilityCheckpoint>> facilities =
            RestoreFacilities(checkpoint, lifecycle.Inventories, topology);
        if (!facilities.IsSuccess)
        {
            return CheckpointResult<SessionEconomyOwner>.Rejected(facilities.Failure!);
        }

        var designs = new ConstructionDesignCatalog();
        foreach (ShipDesign design in policies
                     .SelectMany(policy => policy.AllowedDesigns.Values)
                     .GroupBy(design => design.Id)
                     .Select(group => group.First()))
        {
            designs.Add(design);
        }

        CheckpointResult<RestoredProductionOwner> production =
            ProductionCheckpointRestore.Restore(
                checkpoint.Production,
                lifecycle.Inventories);
        if (!production.IsSuccess)
        {
            return CheckpointResult<SessionEconomyOwner>.Rejected(production.Failure!);
        }

        CheckpointResult<RestoredConstructionOwner> construction =
            ConstructionCheckpointRestore.Restore(
                checkpoint.Construction,
                lifecycle.Inventories,
                designs);
        if (!construction.IsSuccess)
        {
            return CheckpointResult<SessionEconomyOwner>.Rejected(construction.Failure!);
        }

        IReadOnlyDictionary<ShipId, InventoryId> liveShips = LiveShips(lifecycle);
        CheckpointResult<RestoredTransportOwner> transport =
            TransportCheckpointRestore.Restore(
                checkpoint.Transport,
                lifecycle.Inventories,
                liveShips);
        if (!transport.IsSuccess)
        {
            return CheckpointResult<SessionEconomyOwner>.Rejected(transport.Failure!);
        }

        CheckpointValidationFailure? bindingFailure = ValidateFacilityBindings(
            facilities.Value!,
            production.Value!,
            construction.Value!);
        if (bindingFailure is not null)
        {
            return CheckpointResult<SessionEconomyOwner>.Rejected(bindingFailure);
        }

        var anchors = facilities.Value!.Values.ToDictionary(
            facility => facility.LocationId,
            facility => facility.Position);
        return CheckpointResult<SessionEconomyOwner>.Success(
            new SessionEconomyOwner(
                lifecycle.Inventories,
                new HierarchicalLogisticsNavigation(anchors, navigation),
                facilities.Value!,
                production.Value!,
                construction.Value!,
                transport.Value!));
    }

    /// <summary>
    /// Validates stable facility, inventory, location, and spatial anchor
    /// identities before any workflow owner is constructed.
    /// </summary>
    private static CheckpointResult<SortedDictionary<FacilityId, EconomyFacilityCheckpoint>>
        RestoreFacilities(
            SessionEconomyCheckpoint checkpoint,
            InventoryRegistry inventories,
            WorldTopology topology)
    {
        var facilities = NewDictionary<EconomyFacilityCheckpoint>();
        var locations = new HashSet<LocationId>();
        HashSet<SystemId> systemIds = topology.Systems.Select(system => system.Id).ToHashSet();
        for (int index = 0; index < checkpoint.Facilities.Count; index++)
        {
            EconomyFacilityCheckpoint? facility = checkpoint.Facilities[index];
            string path = $"$.checkpoint.economy.facilities[{index}]";
            if (facility is null || facility.FacilityId.Value == 0
                || !facilities.TryAdd(facility.FacilityId, facility))
            {
                return RejectFacilities(
                    $"{path}.facilityId",
                    "The economy facility identity is missing or duplicated.");
            }

            if (inventories.Get(facility.InventoryId) is null)
            {
                return RejectFacilities(
                    $"{path}.inventoryId",
                    "The economy facility inventory is not restored.");
            }

            if (facility.LocationId.Value == 0 || !locations.Add(facility.LocationId))
            {
                return RejectFacilities(
                    $"{path}.locationId",
                    "The logistics location identity is missing or duplicated.");
            }

            if (!systemIds.Contains(facility.Position.SystemId))
            {
                return RejectFacilities(
                    $"{path}.position.systemId",
                    "The economy facility anchor names an unknown system.");
            }

            if (facility.ProductionOutput is { Value: 0 })
            {
                return RejectFacilities(
                    path,
                    "The facility position or production output identity is invalid.");
            }
        }

        return CheckpointResult<SortedDictionary<FacilityId, EconomyFacilityCheckpoint>>
            .Success(facilities);
    }

    /// <summary>
    /// Requires each workflow facility to use the inventory and output retained
    /// by its stable facility binding, without inferring missing configuration.
    /// </summary>
    private static CheckpointValidationFailure? ValidateFacilityBindings(
        IReadOnlyDictionary<FacilityId, EconomyFacilityCheckpoint> facilities,
        RestoredProductionOwner production,
        RestoredConstructionOwner construction)
    {
        foreach ((FacilityId id, ProductionLine line) in production.Lines)
        {
            if (!facilities.TryGetValue(id, out EconomyFacilityCheckpoint? facility)
                || facility.InventoryId != line.InventoryId
                || facility.ProductionOutput is null)
            {
                return new CheckpointValidationFailure(
                    "$.checkpoint.economy.facilities",
                    "A production line disagrees with its facility inventory or output binding.");
            }
        }

        foreach (EconomyFacilityCheckpoint facility in facilities.Values)
        {
            if (facility.ProductionOutput is not null
                && !production.Lines.ContainsKey(facility.FacilityId))
            {
                return new CheckpointValidationFailure(
                    "$.checkpoint.economy.facilities",
                    "A production output binding has no restored production line.");
            }
        }

        foreach ((FacilityId id, ConstructionProcess process) in construction.Processes)
        {
            if (!facilities.TryGetValue(id, out EconomyFacilityCheckpoint? facility)
                || facility.InventoryId != process.InventoryId)
            {
                return new CheckpointValidationFailure(
                    "$.checkpoint.economy.facilities",
                    "A construction process disagrees with its facility inventory binding.");
            }
        }

        return null;
    }

    private static Dictionary<ShipId, InventoryId> LiveShips(
        EntityLifecycleOwner lifecycle) =>
        lifecycle.CaptureCheckpoint().LiveShips.ToDictionary(
            ship => ship!.ShipId,
            ship => ship!.CargoInventoryId);

    private bool AnchorsMatch(
        IReadOnlyDictionary<LocationId, SystemPosition> anchors) =>
        anchors.Count == _facilities.Count
        && _facilities.Values.All(facility =>
            anchors.TryGetValue(facility.LocationId, out SystemPosition position)
            && position == facility.Position);

    private EconomicRuntimeSystem CreateRuntime() =>
        new(new EconomicRuntimeCoordinator(
            _productionLines,
            _productionOutputs,
            _productionLocations,
            _constructionProcesses,
            _constructionLocations,
            _lifecycleInventories,
            _transportBoard,
            _ships,
            _navigation,
            _productionIds,
            _transportIds,
            _reservationIds,
            _capacityReservationIds));

    private static SortedDictionary<FacilityId, TValue> NewDictionary<TValue>() =>
        new(EntityIdComparer<FacilityId>.Instance);

    private static CheckpointResult<SessionEconomyCheckpoint> EconomyRejected(
        string path,
        string message) =>
        CheckpointResult<SessionEconomyCheckpoint>.Rejected(
            new CheckpointValidationFailure(path, message));

    private static CheckpointResult<SortedDictionary<FacilityId, EconomyFacilityCheckpoint>>
        RejectFacilities(string path, string message) =>
            CheckpointResult<SortedDictionary<FacilityId, EconomyFacilityCheckpoint>>.Rejected(
                new CheckpointValidationFailure(path, message));

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
