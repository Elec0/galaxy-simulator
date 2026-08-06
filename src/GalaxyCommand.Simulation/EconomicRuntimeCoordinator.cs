namespace GalaxyCommand.Simulation;

public sealed class EconomicReconciliationResult
{
    internal EconomicReconciliationResult(
        ProductionReconciliationResult production,
        ConstructionReconciliationResult construction,
        LogisticsPublicationReconciliationResult publication,
        LogisticsAssignmentReconciliationResult assignment,
        TransportAdvanceReconciliationResult transportAdvance)
    {
        Production = production;
        Construction = construction;
        Publication = publication;
        Assignment = assignment;
        TransportAdvance = transportAdvance;
        Measurements = Array.AsReadOnly(
            production.Measurements
                .Concat(construction.Measurements)
                .Concat(publication.Measurements)
                .Concat(assignment.Measurements)
                .Concat(transportAdvance.Measurements)
                .ToArray());
    }

    public ProductionReconciliationResult Production { get; }

    public ConstructionReconciliationResult Construction { get; }

    public LogisticsPublicationReconciliationResult Publication { get; }

    public LogisticsAssignmentReconciliationResult Assignment { get; }

    public TransportAdvanceReconciliationResult TransportAdvance { get; }

    public IReadOnlyList<RuntimeMeasurement> Measurements { get; }
}

/// <summary>
/// Fixed single-thread economic coordinator. It runs explicit production,
/// construction, publication, assignment, and transport-advance waves with
/// commit between waves.
/// </summary>
public sealed class EconomicRuntimeCoordinator
{
    private readonly ConstructionSystem _construction = new();
    private readonly IdSequence<CapacityReservationId> _capacityReservationIds;
    private readonly IReadOnlyDictionary<FacilityId, LocationId> _constructionLocations;
    private readonly IReadOnlyDictionary<FacilityId, ConstructionProcess> _constructionProcesses;
    private readonly InventoryRegistry _inventories;
    private readonly LogisticsSystem _logistics = new();
    private readonly INavigation _navigation;
    private readonly IReadOnlyDictionary<FacilityId, LocationId> _productionLocations;
    private readonly ProductionIdSequences _productionIds;
    private readonly IReadOnlyDictionary<FacilityId, ProductionLine> _productionLines;
    private readonly IReadOnlyDictionary<FacilityId, MaterialId> _productionOutputs;
    private readonly ProductionSystem _production = new();
    private readonly IdSequence<ReservationId> _reservationIds;
    private readonly ShipRegistry _ships;
    private readonly TransportBoard _transportBoard;
    private readonly TransportIdSequences _transportIds;
    private readonly TransportSystem _transport = new();

    public EconomicRuntimeCoordinator(
        IReadOnlyDictionary<FacilityId, ProductionLine> productionLines,
        IReadOnlyDictionary<FacilityId, MaterialId> productionOutputs,
        IReadOnlyDictionary<FacilityId, LocationId> productionLocations,
        IReadOnlyDictionary<FacilityId, ConstructionProcess> constructionProcesses,
        IReadOnlyDictionary<FacilityId, LocationId> constructionLocations,
        InventoryRegistry inventories,
        TransportBoard transportBoard,
        ShipRegistry ships,
        INavigation navigation,
        ProductionIdSequences productionIds,
        TransportIdSequences transportIds,
        IdSequence<ReservationId> reservationIds,
        IdSequence<CapacityReservationId> capacityReservationIds)
    {
        _productionLines = productionLines
            ?? throw new ArgumentNullException(nameof(productionLines));
        _productionOutputs = productionOutputs
            ?? throw new ArgumentNullException(nameof(productionOutputs));
        _productionLocations = productionLocations
            ?? throw new ArgumentNullException(nameof(productionLocations));
        _constructionProcesses = constructionProcesses
            ?? throw new ArgumentNullException(nameof(constructionProcesses));
        _constructionLocations = constructionLocations
            ?? throw new ArgumentNullException(nameof(constructionLocations));
        _inventories = inventories
            ?? throw new ArgumentNullException(nameof(inventories));
        _transportBoard = transportBoard
            ?? throw new ArgumentNullException(nameof(transportBoard));
        _ships = ships
            ?? throw new ArgumentNullException(nameof(ships));
        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));
        _productionIds = productionIds
            ?? throw new ArgumentNullException(nameof(productionIds));
        _transportIds = transportIds
            ?? throw new ArgumentNullException(nameof(transportIds));
        _reservationIds = reservationIds
            ?? throw new ArgumentNullException(nameof(reservationIds));
        _capacityReservationIds = capacityReservationIds
            ?? throw new ArgumentNullException(nameof(capacityReservationIds));
        ValidateConfiguration();
    }

    public EconomicReconciliationResult Reconcile(
        SimulationTime now,
        TransportTiming transportTiming)
    {
        ProductionReconciliationResult production = _production.Reconcile(
            _productionLines,
            _inventories,
            _reservationIds,
            now);
        ConstructionReconciliationResult construction = _construction.Reconcile(
            _constructionProcesses,
            _inventories,
            _reservationIds,
            now);

        LogisticsPublicationReconciliationResult publication =
            _logistics.ReconcilePublication(
                CreateDemandReads(),
                CreateSupplyReads(),
                _transportBoard,
                _transportIds,
                now);
        LogisticsAssignmentReconciliationResult assignment =
            _logistics.ReconcileAssignments(
                _transportBoard,
                _transportIds,
                _reservationIds,
                _ships,
                _inventories,
                _navigation,
                now);
        TransportAdvanceReconciliationResult transportAdvance =
            _transport.Reconcile(
                _transportBoard,
                _ships,
                _inventories,
                _capacityReservationIds,
                _navigation,
                transportTiming,
                now);
        return new EconomicReconciliationResult(
            production,
            construction,
            publication,
            assignment,
            transportAdvance);
    }

    public ConstructionCompletionCommitResult CommitConstructionCompletion(
        FacilityId facilityId,
        ConstructionOrderId orderId,
        EventGeneration generation,
        SimulationTime now,
        EventKey completionEventKey) =>
        ConstructionSystem.CommitCompletion(
            _constructionProcesses,
            facilityId,
            orderId,
            generation,
            now,
            completionEventKey);

    public ProductionCompletionCommitResult CommitProductionCompletion(
        FacilityId facilityId,
        ProductionJobId jobId,
        EventGeneration generation,
        SimulationTime now) =>
        ProductionSystem.CommitCompletion(
            _productionLines,
            _productionIds,
            _inventories,
            facilityId,
            jobId,
            generation,
            now);

    public TransportEventReconciliationResult HandleTransportEvent(
        TransportEvent transportEvent,
        TransportTiming transportTiming,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(transportEvent);
        TransportJob? job = _transportBoard.GetJob(transportEvent.JobId);
        Freighter? freighter = job is null
            ? null
            : _ships.GetFreighter(job.ShipId);
        TransportEventCoreCommit core = _transportBoard.CommitEventCore(
            transportEvent,
            freighter,
            _inventories,
            _navigation,
            now);
        if (core.Disposition != ScheduledEventDisposition.Applied
            || core.ContinuationTarget is not { } target
            || freighter is null)
        {
            return new TransportEventReconciliationResult(
                core.Disposition,
                new TransportAdvanceCommitResult([], 0));
        }

        TransportAdvanceEvaluation evaluation =
            TransportSystem.EvaluateContinuation(
                _transportBoard,
                freighter,
                _inventories,
                target,
                _navigation,
                transportTiming,
                now);
        TransportAdvanceCommitResult continuation = _transport.Commit(
            evaluation,
            _transportBoard,
            _ships,
            _inventories,
            _capacityReservationIds);
        return new TransportEventReconciliationResult(
            core.Disposition,
            continuation);
    }

    private IEnumerable<LogisticsDemandPublicationRead> CreateDemandReads()
    {
        foreach ((FacilityId facilityId, ProductionLine line) in _productionLines)
        {
            Inventory inventory = GetInventory(line.InventoryId);
            LocationId location = _productionLocations[facilityId];
            foreach ((MaterialId materialId, Quantity quantity) in line.UnmetInputs(inventory))
            {
                yield return new LogisticsDemandPublicationRead(
                    line.InventoryId,
                    location,
                    materialId,
                    quantity,
                    _transportBoard.PendingDeliveryQuantity(
                        line.InventoryId,
                        materialId),
                    new DemandPriority(1));
            }
        }

        foreach ((FacilityId facilityId, ConstructionProcess process)
            in _constructionProcesses)
        {
            Inventory inventory = GetInventory(process.InventoryId);
            LocationId location = _constructionLocations[facilityId];
            foreach ((MaterialId materialId, Quantity quantity)
                in process.UnmetInputs(inventory))
            {
                yield return new LogisticsDemandPublicationRead(
                    process.InventoryId,
                    location,
                    materialId,
                    quantity,
                    _transportBoard.PendingDeliveryQuantity(
                        process.InventoryId,
                        materialId),
                    new DemandPriority(1));
            }
        }
    }

    private IEnumerable<LogisticsSupplyPublicationRead> CreateSupplyReads()
    {
        foreach ((FacilityId facilityId, MaterialId materialId) in _productionOutputs)
        {
            ProductionLine line = _productionLines[facilityId];
            Inventory inventory = GetInventory(line.InventoryId);
            yield return new LogisticsSupplyPublicationRead(
                line.InventoryId,
                _productionLocations[facilityId],
                materialId,
                inventory.Available(materialId),
                _transportBoard.OfferedQuantity(line.InventoryId, materialId));
        }
    }

    private Inventory GetInventory(InventoryId inventoryId) =>
        _inventories.Get(inventoryId)
        ?? throw new KeyNotFoundException($"Unknown inventory {inventoryId}.");

    private void ValidateConfiguration()
    {
        foreach ((FacilityId facilityId, ProductionLine line) in _productionLines)
        {
            if (line.FacilityId != facilityId)
            {
                throw new ArgumentException(
                    $"Production key {facilityId} does not match line {line.FacilityId}.",
                    nameof(_productionLines));
            }

            if (!_productionLocations.ContainsKey(facilityId))
            {
                throw new ArgumentException(
                    $"Production facility {facilityId} has no logistics location.",
                    nameof(_productionLocations));
            }
        }

        foreach (FacilityId facilityId in _productionOutputs.Keys)
        {
            if (!_productionLines.ContainsKey(facilityId))
            {
                throw new ArgumentException(
                    $"Production output references unknown facility {facilityId}.",
                    nameof(_productionOutputs));
            }
        }

        foreach ((FacilityId facilityId, ConstructionProcess process)
            in _constructionProcesses)
        {
            if (process.FacilityId != facilityId)
            {
                throw new ArgumentException(
                    $"Construction key {facilityId} does not match process {process.FacilityId}.",
                    nameof(_constructionProcesses));
            }

            if (!_constructionLocations.ContainsKey(facilityId))
            {
                throw new ArgumentException(
                    $"Construction facility {facilityId} has no logistics location.",
                    nameof(_constructionLocations));
            }
        }
    }
}
