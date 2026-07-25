using System.Buffers.Binary;

namespace GalaxyCommand.Simulation;

public sealed record PhaseOneConfig
{
    public ulong RandomSeed { get; init; }
    public SimulationDuration RouteDuration { get; init; } = new(60_000);
    public SimulationDuration DockingOverhead { get; init; } = new(5_000);
    public TransferRate TransferRate { get; init; } = new(10);
    public Quantity FacilityStorageCapacity { get; init; } = new(100);
    public Quantity FreighterCargoCapacity { get; init; } = new(10);
    public Quantity OreBatch { get; init; } = new(10);
    public Work MineWork { get; init; } = new(30);
    public Quantity RefineryOreInput { get; init; } = new(10);
    public Quantity RefineryAlloyOutput { get; init; } = new(5);
    public Work RefineryWork { get; init; } = new(60);
    public Quantity ComponentAlloyInput { get; init; } = new(5);
    public Quantity ComponentOutput { get; init; } = new(2);
    public Work ComponentWork { get; init; } = new(60);
    public Quantity ShipyardComponentInput { get; init; } = new(4);
    public Work ShipyardWork { get; init; } = new(120);
}

public abstract record ScenarioEventKind
{
    public sealed record Transport(TransportEvent Event) : ScenarioEventKind;
    public sealed record ProductionComplete(FacilityId FacilityId) : ScenarioEventKind;
    public sealed record ConstructionComplete(
        FacilityId FacilityId,
        ConstructionOrderId OrderId) : ScenarioEventKind;
    public sealed record RouteEnabled(RouteId RouteId, bool Enabled) : ScenarioEventKind;
}

public sealed record ScenarioEventRecord(
    SimulationTime Timestamp,
    EventPhase Phase,
    ulong CreationSequence,
    ScenarioEventKind Kind);

public enum DecisionReason
{
    HighestRankedReachableTransport,
    NoEnabledRouteToSource,
    NoEnabledRouteToDestination,
    DestinationCapacityUnavailable,
}

public sealed record DecisionRecord(
    SimulationTime Timestamp,
    ShipId ShipId,
    TransportJobId JobId,
    DecisionReason Reason);

public readonly record struct FacilityTimeMetrics(
    ulong ActiveMilliseconds,
    ulong WaitingMilliseconds,
    ulong OutputBlockedMilliseconds);

public sealed class PhaseOneMetrics
{
    private readonly Dictionary<(FacilityId Facility, MaterialId Material), Quantity> _materialProduced = [];
    private readonly Dictionary<(FacilityId Facility, MaterialId Material), Quantity> _materialConsumed = [];
    private readonly Dictionary<(InventoryId Inventory, MaterialId Material), Quantity> _cargoDelivered = [];
    private readonly SortedDictionary<FacilityId, FacilityTimeMetrics> _facilityTime =
        new(EntityIdComparer<FacilityId>.Instance);

    public IReadOnlyDictionary<(FacilityId Facility, MaterialId Material), Quantity> MaterialProduced =>
        _materialProduced;
    public IReadOnlyDictionary<(FacilityId Facility, MaterialId Material), Quantity> MaterialConsumed =>
        _materialConsumed;
    public IReadOnlyDictionary<(InventoryId Inventory, MaterialId Material), Quantity> CargoDelivered =>
        _cargoDelivered;
    public IReadOnlyDictionary<FacilityId, FacilityTimeMetrics> FacilityTime => _facilityTime;
    public ulong TransportJobsCreated { get; internal set; }
    public ulong TransportJobsCompleted { get; internal set; }
    public ulong TransportJobsPartiallyFulfilled { get; internal set; }
    public ulong TransportJobsCancelled { get; internal set; }
    public ulong TransportJobsFailed { get; internal set; }

    internal Dictionary<(FacilityId Facility, MaterialId Material), Quantity> ProducedMutable =>
        _materialProduced;
    internal Dictionary<(FacilityId Facility, MaterialId Material), Quantity> ConsumedMutable =>
        _materialConsumed;
    internal Dictionary<(InventoryId Inventory, MaterialId Material), Quantity> DeliveredMutable =>
        _cargoDelivered;
    internal SortedDictionary<FacilityId, FacilityTimeMetrics> FacilityTimeMutable => _facilityTime;

    internal PhaseOneMetrics Snapshot()
    {
        var snapshot = new PhaseOneMetrics
        {
            TransportJobsCreated = TransportJobsCreated,
            TransportJobsCompleted = TransportJobsCompleted,
            TransportJobsPartiallyFulfilled = TransportJobsPartiallyFulfilled,
            TransportJobsCancelled = TransportJobsCancelled,
            TransportJobsFailed = TransportJobsFailed,
        };
        foreach (var pair in _materialProduced) snapshot._materialProduced.Add(pair.Key, pair.Value);
        foreach (var pair in _materialConsumed) snapshot._materialConsumed.Add(pair.Key, pair.Value);
        foreach (var pair in _cargoDelivered) snapshot._cargoDelivered.Add(pair.Key, pair.Value);
        foreach (var pair in _facilityTime) snapshot._facilityTime.Add(pair.Key, pair.Value);
        return snapshot;
    }
}

public enum ShortageCause
{
    CommittedDeliveryPending,
    AwaitingSupplyOrReachableRoute,
}

public sealed record ShortageRecord(
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Missing,
    ShortageCause Cause);

public sealed record PhaseOneReport(
    SimulationTime StartTime,
    SimulationTime EndTime,
    ulong EventsProcessed,
    int StartingShipCount,
    int EndingShipCount,
    ShipId? ConstructedShipId,
    PhaseOneMetrics Metrics,
    ulong EventLogDigest,
    ulong FinalStateDigest,
    IReadOnlyList<ShortageRecord> CurrentShortages);

internal abstract record PhaseOneEvent
{
    public sealed record Transport(TransportEvent Event) : PhaseOneEvent;
    public sealed record ProductionComplete(FacilityId FacilityId) : PhaseOneEvent;
    public sealed record ConstructionComplete(
        FacilityId FacilityId,
        ConstructionOrderId OrderId) : PhaseOneEvent;
    public sealed record RouteEnabled(RouteId RouteId, bool Enabled) : PhaseOneEvent;
}

/// <summary>
/// Runtime behavior for the integrated Phase 1 proof-of-concept fixture.
/// </summary>
internal sealed class PhaseOneRuntime : ISimulationRuntime<PhaseOneEvent>
{
    private readonly PhaseOneConfig _config;
    private readonly SimulationWorld _world;
    private readonly EventAgenda<PhaseOneEvent> _agenda = new();
    private readonly SimulationEngine<PhaseOneEvent> _engine;
    private readonly RouteGraph _navigation;
    private readonly InventoryRegistry _inventories;
    private readonly SortedDictionary<FacilityId, ProductionLine> _productionLines;
    private readonly SortedDictionary<FacilityId, MaterialId> _productionOutputs;
    private readonly SortedDictionary<FacilityId, LocationId> _facilityLocations;
    private readonly Shipyard _shipyard;
    private readonly TransportBoard _transportBoard;
    private readonly ShipRegistry _ships;
    private readonly ProductionIdSequences _productionIds;
    private readonly TransportIdSequences _transportIds;
    private readonly IdSequence<ReservationId> _reservationIds;
    private readonly IdSequence<CapacityReservationId> _capacityReservationIds;
    private readonly IdSequence<ShipId> _shipIds;
    private readonly IdSequence<InventoryId> _inventoryIds;
    private readonly RouteId _mineToRefineryRoute;
    private readonly MaterialId[] _knownMaterials;
    private readonly SortedDictionary<LocationId, string> _locationNames;
    private readonly List<ScenarioEventRecord> _eventRecords = [];
    private readonly List<DecisionRecord> _decisionRecords = [];
    private readonly PhaseOneMetrics _metrics = new();
    private ShipId? _constructedShipId;
    private SimulationTime _metricsTime = SimulationTime.Zero;

    public PhaseOneRuntime(PhaseOneConfig? config = null)
    {
        _config = config ?? new PhaseOneConfig();
        PhaseOneFixtureState fixture = PhaseOneFixture.Create(_config);
        _world = fixture.World;
        _navigation = _world.Navigation;
        _inventories = _world.Inventories;
        _productionLines = _world.ProductionLines;
        _productionOutputs = _world.ProductionOutputs;
        _facilityLocations = _world.FacilityLocations;
        _transportBoard = _world.TransportBoard;
        _ships = _world.Ships;
        _productionIds = _world.ProductionIds;
        _transportIds = _world.TransportIds;
        _reservationIds = _world.ReservationIds;
        _capacityReservationIds = _world.CapacityReservationIds;
        _shipIds = _world.ShipIds;
        _inventoryIds = _world.InventoryIds;
        _locationNames = _world.LocationNames;
        _shipyard = fixture.Shipyard;
        _mineToRefineryRoute = fixture.MineToRefineryRoute;
        _knownMaterials = fixture.KnownMaterials;
        _engine = new SimulationEngine<PhaseOneEvent>(this, _agenda);
    }

    public IReadOnlyList<ScenarioEventRecord> EventRecords => _eventRecords;
    public IReadOnlyList<DecisionRecord> DecisionRecords => _decisionRecords;
    public SimulationWorld World => _world;

    public PhaseOneSnapshot CaptureSnapshot()
    {
        var ships = new List<ShipSnapshot>();
        foreach (ShipId shipId in _ships.FreighterIds)
        {
            Ship ship = _ships.GetShip(shipId)
                ?? throw new InvalidOperationException($"Missing ship {shipId}.");
            Freighter freighter = _ships.GetFreighter(shipId)
                ?? throw new InvalidOperationException($"Missing freighter {shipId}.");
            TransportJob? job = freighter.ActiveJobId is { } jobId
                ? _transportBoard.GetJob(jobId)
                : null;
            RouteId? currentRoute = job?.CurrentRouteId;
            SimulationTime? arrivesAt = currentRoute is not null
                ? job?.TransitionAt
                : null;
            SimulationTime? departedAt = currentRoute is { } routeId && arrivesAt is { } arrival
                ? TravelDeparture(routeId, arrival)
                : null;

            ships.Add(new ShipSnapshot(
                freighter.ShipId,
                ship.DesignId,
                freighter.LocationId,
                freighter.ActiveJobId,
                job?.Status,
                currentRoute,
                departedAt,
                arrivesAt));
        }

        var constructions = new List<ConstructionSnapshot>();
        if (_shipyard.ActiveOrder is { } activeOrder)
        {
            Inventory inventory = GetInventory(_shipyard.InventoryId);
            constructions.Add(new ConstructionSnapshot(
                _shipyard.FacilityId,
                activeOrder.Id,
                activeOrder.Design.Id,
                activeOrder.Design.Name,
                activeOrder.Status,
                activeOrder.CompletesAt,
                SnapshotCollection.Copy(_shipyard.UnmetInputs(inventory))));
        }

        return new PhaseOneSnapshot(
            _agenda.CurrentTime,
            SnapshotCollection.Copy(_navigation.Locations.Select(location =>
                new LocationSnapshot(location, _locationNames[location]))),
            SnapshotCollection.Copy(_navigation.Routes.Select(route =>
                new RouteSnapshot(
                    route.Id,
                    route.Origin,
                    route.Destination,
                    route.BaseDuration,
                    route.IsEnabled))),
            SnapshotCollection.Copy(ships),
            SnapshotCollection.Copy(constructions));
    }

    public void ScheduleApprovedRouteDisruption()
    {
        foreach ((ulong timestamp, bool enabled) in new[]
        {
            (50_000UL, false),
            (250_000UL, true),
        })
        {
            _engine.Schedule(
                new SimulationTime(timestamp),
                EventPhase.StateUpdate,
                new EventGeneration(0),
                new PhaseOneEvent.RouteEnabled(_mineToRefineryRoute, enabled));
        }
    }

    public PhaseOneReport RunUntilFirstShip(SimulationTime target)
    {
        int startingShipCount = _ships.Count;
        RunReport run = _engine.RunUntil(target);
        return new PhaseOneReport(
            run.StartTime,
            run.EndTime,
            checked((ulong)run.EventsProcessed),
            startingShipCount,
            _ships.Count,
            _constructedShipId,
            _metrics.Snapshot(),
            EventLogDigest(),
            FinalStateDigest(),
            CurrentShortages());
    }

    bool ISimulationRuntime<PhaseOneEvent>.ShouldStop => _constructedShipId is not null;

    void ISimulationRuntime<PhaseOneEvent>.Reconcile(
        SimulationTime now,
        EventAgenda<PhaseOneEvent> agenda) =>
        Reconcile(now);

    void ISimulationRuntime<PhaseOneEvent>.AccrueTo(SimulationTime now) =>
        AccrueFacilityTime(now);

    void ISimulationRuntime<PhaseOneEvent>.HandleEvent(
        PhaseOneEvent simulationEvent,
        SimulationTime now,
        EventAgenda<PhaseOneEvent> agenda) =>
        HandleEvent(simulationEvent, now);

    void ISimulationRuntime<PhaseOneEvent>.RecordEvent(
        ScheduledEvent<PhaseOneEvent> simulationEvent)
    {
        _eventRecords.Add(new ScenarioEventRecord(
            simulationEvent.Key.Timestamp,
            simulationEvent.Key.Phase,
            simulationEvent.Key.CreationSequence,
            ToEventKind(simulationEvent.Payload)));
    }

    private SimulationTime TravelDeparture(RouteId routeId, SimulationTime arrivesAt)
    {
        DirectedRoute route = _navigation.GetRoute(routeId)
            ?? throw new InvalidOperationException($"Missing route {routeId}.");
        return new SimulationTime(checked(arrivesAt.Milliseconds - route.BaseDuration.Milliseconds));
    }

    private void HandleEvent(PhaseOneEvent scenarioEvent, SimulationTime now)
    {
        switch (scenarioEvent)
        {
            case PhaseOneEvent.Transport transport:
                TransportJob before = GetTransportJob(transport.Event.JobId);
                TransportJobStatus beforeStatus = before.Status;
                Freighter freighter = _ships.GetFreighter(before.ShipId)
                    ?? throw new KeyNotFoundException($"Missing freighter {before.ShipId}.");
                _transportBoard.HandleEvent(
                    transport.Event,
                    freighter,
                    _inventories,
                    _capacityReservationIds,
                    _navigation,
                    _agenda,
                    static transportEvent => new PhaseOneEvent.Transport(transportEvent),
                    TransportTiming(),
                    now);
                TransportJob after = GetTransportJob(transport.Event.JobId);
                if (beforeStatus != TransportJobStatus.Completed
                    && after.Status == TransportJobStatus.Completed)
                {
                    IncrementQuantity(
                        _metrics.DeliveredMutable,
                        (after.DestinationInventoryId, after.MaterialId),
                        after.Quantity);
                    _metrics.TransportJobsCompleted = checked(_metrics.TransportJobsCompleted + 1);
                }

                if (beforeStatus != TransportJobStatus.FailedBeforeLoading
                    && after.Status == TransportJobStatus.FailedBeforeLoading)
                {
                    _metrics.TransportJobsFailed = checked(_metrics.TransportJobsFailed + 1);
                }

                break;

            case PhaseOneEvent.ProductionComplete production:
                ProductionLine line = _productionLines[production.FacilityId];
                Inventory inventory = GetInventory(line.InventoryId);
                (MaterialId Material, Quantity Quantity)? output = line.ActiveJob is { } job
                    ? (job.Recipe.OutputMaterial, job.Recipe.OutputQuantity)
                    : null;
                if (line.CompleteActive(_productionIds, inventory, now) && output is { } completed)
                {
                    IncrementQuantity(
                        _metrics.ProducedMutable,
                        (production.FacilityId, completed.Material),
                        completed.Quantity);
                }

                break;

            case PhaseOneEvent.ConstructionComplete construction:
                if (construction.FacilityId != _shipyard.FacilityId
                    || _shipyard.ActiveOrder?.Id != construction.OrderId)
                {
                    break;
                }

                _constructedShipId = _shipyard.CompleteActive(
                    _shipIds,
                    _inventoryIds,
                    _inventories,
                    _ships,
                    now);
                break;

            case PhaseOneEvent.RouteEnabled route:
                _navigation.SetRouteEnabled(route.RouteId, route.Enabled);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenarioEvent));
        }
    }

    private void Reconcile(SimulationTime now)
    {
        PrepareProduction(now);
        PrepareShipyard(now);
        PublishDemands(now);
        PublishSupplies();
        AssignAndRetryFreighters(now);
    }

    private void PrepareProduction(SimulationTime now)
    {
        foreach ((FacilityId facilityId, ProductionLine line) in _productionLines)
        {
            Inventory inventory = GetInventory(line.InventoryId);
            IReadOnlyDictionary<MaterialId, Quantity> inputs = line.ActiveJob?.Recipe.Inputs
                ?? new Dictionary<MaterialId, Quantity>();
            if (line.PrepareActive(_reservationIds, inventory, now) is not { } completesAt)
            {
                continue;
            }

            foreach ((MaterialId materialId, Quantity quantity) in inputs)
            {
                IncrementQuantity(
                    _metrics.ConsumedMutable,
                    (facilityId, materialId),
                    quantity);
            }

            _agenda.Schedule(
                completesAt,
                EventPhase.PhysicalCompletion,
                new EventGeneration(0),
                new PhaseOneEvent.ProductionComplete(facilityId));
        }
    }

    private void PrepareShipyard(SimulationTime now)
    {
        if (_shipyard.ActiveOrder is not { } activeOrder)
        {
            return;
        }

        Inventory inventory = GetInventory(_shipyard.InventoryId);
        IReadOnlyDictionary<MaterialId, Quantity> inputs = activeOrder.Design.Recipe.Inputs;
        if (_shipyard.PrepareActive(_reservationIds, inventory, now) is not { } completesAt)
        {
            return;
        }

        foreach ((MaterialId materialId, Quantity quantity) in inputs)
        {
            IncrementQuantity(
                _metrics.ConsumedMutable,
                (_shipyard.FacilityId, materialId),
                quantity);
        }

        _agenda.Schedule(
            completesAt,
            EventPhase.PhysicalCompletion,
            activeOrder.Generation,
            new PhaseOneEvent.ConstructionComplete(
                _shipyard.FacilityId,
                activeOrder.Id));
    }

    private void PublishDemands(SimulationTime now)
    {
        var requirements = new List<(InventoryId, LocationId, MaterialId, Quantity)>();
        foreach ((FacilityId facilityId, ProductionLine line) in _productionLines)
        {
            Inventory inventory = GetInventory(line.InventoryId);
            LocationId location = _facilityLocations[facilityId];
            foreach ((MaterialId materialId, Quantity quantity) in line.UnmetInputs(inventory))
            {
                requirements.Add((line.InventoryId, location, materialId, quantity));
            }
        }

        Inventory shipyardInventory = GetInventory(_shipyard.InventoryId);
        foreach ((MaterialId materialId, Quantity quantity) in _shipyard.UnmetInputs(shipyardInventory))
        {
            requirements.Add((_shipyard.InventoryId, _shipyard.LocationId, materialId, quantity));
        }

        foreach ((InventoryId inventoryId, LocationId location, MaterialId material, Quantity required)
            in requirements)
        {
            Quantity pending = _transportBoard.PendingDeliveryQuantity(inventoryId, material);
            if (required > pending)
            {
                _transportBoard.PublishDemand(
                    _transportIds,
                    inventoryId,
                    location,
                    material,
                    required.Subtract(pending),
                    new DemandPriority(1),
                    now);
            }
        }
    }

    private void PublishSupplies()
    {
        foreach ((FacilityId facilityId, MaterialId material) in _productionOutputs)
        {
            ProductionLine line = _productionLines[facilityId];
            Inventory inventory = GetInventory(line.InventoryId);
            Quantity available = inventory.Available(material);
            Quantity offered = _transportBoard.OfferedQuantity(line.InventoryId, material);
            if (available > offered)
            {
                _transportBoard.PublishSupply(
                    _transportIds,
                    line.InventoryId,
                    _facilityLocations[facilityId],
                    material,
                    available.Subtract(offered));
            }
        }
    }

    private void AssignAndRetryFreighters(SimulationTime now)
    {
        foreach (ShipId shipId in _ships.FreighterIds.ToArray())
        {
            Freighter freighter = _ships.GetFreighter(shipId)
                ?? throw new KeyNotFoundException($"Missing freighter {shipId}.");
            TransportJobId? existingJobId = freighter.ActiveJobId;
            TransportJobId? jobId = existingJobId ?? _transportBoard.AssignBest(
                _transportIds,
                _reservationIds,
                freighter,
                _inventories,
                _navigation,
                now);
            if (jobId is not { } assignedJobId)
            {
                continue;
            }

            if (existingJobId is null)
            {
                _metrics.TransportJobsCreated = checked(_metrics.TransportJobsCreated + 1);
                _decisionRecords.Add(new DecisionRecord(
                    now,
                    shipId,
                    assignedJobId,
                    DecisionReason.HighestRankedReachableTransport));
            }

            TransportJobStatus before = GetTransportJob(assignedJobId).Status;
            _transportBoard.StartOrRetry(
                assignedJobId,
                freighter,
                _inventories,
                _capacityReservationIds,
                _navigation,
                _agenda,
                static transportEvent => new PhaseOneEvent.Transport(transportEvent),
                TransportTiming(),
                now);
            TransportJobStatus after = GetTransportJob(assignedJobId).Status;
            if (before != after && WaitingReason(after) is { } reason)
            {
                _decisionRecords.Add(new DecisionRecord(now, shipId, assignedJobId, reason));
            }
        }
    }

    private void AccrueFacilityTime(SimulationTime now)
    {
        ulong elapsed = checked(now.Milliseconds - _metricsTime.Milliseconds);
        if (elapsed == 0)
        {
            return;
        }

        foreach ((FacilityId facilityId, ProductionLine line) in _productionLines)
        {
            FacilityTimeMetrics current = _metrics.FacilityTimeMutable.GetValueOrDefault(facilityId);
            FacilityTimeMetrics updated = line.ActiveJob?.Status switch
            {
                ProductionJobStatus.Running => current with
                {
                    ActiveMilliseconds = checked(current.ActiveMilliseconds + elapsed),
                },
                ProductionJobStatus.CompletedAwaitingStorage => current with
                {
                    OutputBlockedMilliseconds = checked(current.OutputBlockedMilliseconds + elapsed),
                },
                _ => current with
                {
                    WaitingMilliseconds = checked(current.WaitingMilliseconds + elapsed),
                },
            };
            _metrics.FacilityTimeMutable[facilityId] = updated;
        }

        FacilityId shipyardId = _shipyard.FacilityId;
        FacilityTimeMetrics shipyardCurrent =
            _metrics.FacilityTimeMutable.GetValueOrDefault(shipyardId);
        _metrics.FacilityTimeMutable[shipyardId] =
            _shipyard.ActiveOrder?.Status == ConstructionOrderStatus.Running
                ? shipyardCurrent with
                {
                    ActiveMilliseconds = checked(shipyardCurrent.ActiveMilliseconds + elapsed),
                }
                : shipyardCurrent with
                {
                    WaitingMilliseconds = checked(shipyardCurrent.WaitingMilliseconds + elapsed),
                };
        _metricsTime = now;
    }

    private List<ShortageRecord> CurrentShortages()
    {
        var shortages = new List<ShortageRecord>();
        foreach ((FacilityId facilityId, ProductionLine line) in _productionLines)
        {
            Inventory inventory = GetInventory(line.InventoryId);
            foreach ((MaterialId materialId, Quantity missing) in line.UnmetInputs(inventory))
            {
                shortages.Add(CreateShortage(
                    line.InventoryId,
                    _facilityLocations[facilityId],
                    materialId,
                    missing));
            }
        }

        Inventory shipyardInventory = GetInventory(_shipyard.InventoryId);
        foreach ((MaterialId materialId, Quantity missing) in _shipyard.UnmetInputs(shipyardInventory))
        {
            shortages.Add(CreateShortage(
                _shipyard.InventoryId,
                _shipyard.LocationId,
                materialId,
                missing));
        }

        return shortages;
    }

    private ShortageRecord CreateShortage(
        InventoryId inventoryId,
        LocationId locationId,
        MaterialId materialId,
        Quantity missing)
    {
        Quantity committed = _transportBoard.CommittedDeliveryQuantity(inventoryId, materialId);
        return new ShortageRecord(
            inventoryId,
            locationId,
            materialId,
            missing,
            committed > Quantity.Zero
                ? ShortageCause.CommittedDeliveryPending
                : ShortageCause.AwaitingSupplyOrReachableRoute);
    }

    private ulong EventLogDigest()
    {
        var hash = new Fnv1a64();
        hash.WriteUInt64((ulong)_eventRecords.Count);
        foreach (ScenarioEventRecord record in _eventRecords)
        {
            HashEventRecord(hash, record);
        }

        hash.WriteUInt64((ulong)_decisionRecords.Count);
        foreach (DecisionRecord record in _decisionRecords)
        {
            hash.WriteUInt64(record.Timestamp.Milliseconds);
            hash.WriteUInt64(record.ShipId.Value);
            hash.WriteUInt64(record.JobId.Value);
            hash.WriteByte((byte)record.Reason);
        }

        return hash.Value;
    }

    private ulong FinalStateDigest()
    {
        var hash = new Fnv1a64();
        hash.WriteUInt64(_agenda.CurrentTime.Milliseconds);
        hash.WriteUInt64(_config.RandomSeed);
        hash.WriteUInt64(_constructedShipId?.Value ?? 0);
        DirectedRoute route = _navigation.GetRoute(_mineToRefineryRoute)
            ?? throw new InvalidOperationException("Approved fixture route is missing.");
        hash.WriteUInt64(route.Id.Value);
        hash.WriteBoolean(route.IsEnabled);

        var inventoryIds = new SortedSet<InventoryId>(EntityIdComparer<InventoryId>.Instance);
        foreach (ProductionLine line in _productionLines.Values)
        {
            inventoryIds.Add(line.InventoryId);
        }

        inventoryIds.Add(_shipyard.InventoryId);
        foreach (ShipId shipId in _ships.FreighterIds)
        {
            Ship ship = _ships.GetShip(shipId)
                ?? throw new InvalidOperationException($"Ship {shipId} is missing.");
            Freighter freighter = _ships.GetFreighter(shipId)
                ?? throw new InvalidOperationException($"Freighter {shipId} is missing.");
            inventoryIds.Add(freighter.CargoInventoryId);
            hash.WriteUInt64(shipId.Value);
            hash.WriteUInt64(ship.DesignId.Value);
            hash.WriteUInt64(freighter.LocationId.Value);
            hash.WriteUInt64(freighter.ActiveJobId?.Value ?? 0);
        }

        foreach (InventoryId inventoryId in inventoryIds)
        {
            Inventory inventory = GetInventory(inventoryId);
            hash.WriteUInt64(inventoryId.Value);
            hash.WriteUInt64(inventory.Capacity.Units);
            hash.WriteUInt64(inventory.TotalStored.Units);
            hash.WriteUInt64(inventory.ReservedCapacity.Units);
            foreach (MaterialId material in _knownMaterials)
            {
                hash.WriteUInt64(material.Value);
                hash.WriteUInt64(inventory.Stored(material).Units);
                hash.WriteUInt64(inventory.Reserved(material).Units);
            }
        }

        foreach (TransportJob job in _transportBoard.Jobs)
        {
            hash.WriteUInt64(job.Id.Value);
            hash.WriteUInt64(job.ShipId.Value);
            hash.WriteUInt64(job.MaterialId.Value);
            hash.WriteUInt64(job.Quantity.Units);
            HashTransportState(hash, job);
        }

        return hash.Value;
    }

    private static void HashEventRecord(Fnv1a64 hash, ScenarioEventRecord record)
    {
        hash.WriteUInt64(record.Timestamp.Milliseconds);
        hash.WriteByte((byte)record.Phase);
        hash.WriteUInt64(record.CreationSequence);
        switch (record.Kind)
        {
            case ScenarioEventKind.Transport transport:
                hash.WriteByte(0);
                HashTransportEvent(hash, transport.Event);
                break;
            case ScenarioEventKind.ProductionComplete production:
                hash.WriteByte(1);
                hash.WriteUInt64(production.FacilityId.Value);
                break;
            case ScenarioEventKind.ConstructionComplete construction:
                hash.WriteByte(2);
                hash.WriteUInt64(construction.FacilityId.Value);
                hash.WriteUInt64(construction.OrderId.Value);
                break;
            case ScenarioEventKind.RouteEnabled route:
                hash.WriteByte(3);
                hash.WriteUInt64(route.RouteId.Value);
                hash.WriteBoolean(route.Enabled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(record));
        }
    }

    private static void HashTransportEvent(Fnv1a64 hash, TransportEvent transportEvent)
    {
        switch (transportEvent)
        {
            case TransportEvent.Arrive arrive:
                hash.WriteByte(0);
                hash.WriteUInt64(arrive.JobId.Value);
                hash.WriteUInt64(arrive.Generation.Value);
                hash.WriteUInt64(arrive.RouteId.Value);
                hash.WriteByte((byte)arrive.Target);
                break;
            case TransportEvent.FinishLoading loading:
                hash.WriteByte(1);
                hash.WriteUInt64(loading.JobId.Value);
                hash.WriteUInt64(loading.Generation.Value);
                break;
            case TransportEvent.FinishUnloading unloading:
                hash.WriteByte(2);
                hash.WriteUInt64(unloading.JobId.Value);
                hash.WriteUInt64(unloading.Generation.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transportEvent));
        }
    }

    private static void HashTransportState(Fnv1a64 hash, TransportJob job)
    {
        hash.WriteByte((byte)job.Status);
        switch (job.Status)
        {
            case TransportJobStatus.TravelingToSource:
            case TransportJobStatus.TravelingToDestination:
                hash.WriteUInt64(job.CurrentRouteId?.Value
                    ?? throw new InvalidOperationException("Traveling job has no route."));
                hash.WriteUInt64(job.TransitionAt?.Milliseconds
                    ?? throw new InvalidOperationException("Traveling job has no arrival time."));
                break;
            case TransportJobStatus.Loading:
            case TransportJobStatus.Unloading:
                hash.WriteUInt64(job.TransitionAt?.Milliseconds
                    ?? throw new InvalidOperationException("Transfer job has no completion time."));
                break;
        }
    }

    private static ScenarioEventKind ToEventKind(PhaseOneEvent scenarioEvent) =>
        scenarioEvent switch
        {
            PhaseOneEvent.Transport transport => new ScenarioEventKind.Transport(transport.Event),
            PhaseOneEvent.ProductionComplete production =>
                new ScenarioEventKind.ProductionComplete(production.FacilityId),
            PhaseOneEvent.ConstructionComplete construction =>
                new ScenarioEventKind.ConstructionComplete(
                    construction.FacilityId,
                    construction.OrderId),
            PhaseOneEvent.RouteEnabled route =>
                new ScenarioEventKind.RouteEnabled(route.RouteId, route.Enabled),
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioEvent)),
        };

    private static DecisionReason? WaitingReason(TransportJobStatus status) =>
        status switch
        {
            TransportJobStatus.WaitingForRouteToSource => DecisionReason.NoEnabledRouteToSource,
            TransportJobStatus.WaitingForRouteToDestination => DecisionReason.NoEnabledRouteToDestination,
            TransportJobStatus.WaitingForDestinationCapacity => DecisionReason.DestinationCapacityUnavailable,
            _ => null,
        };

    private static void IncrementQuantity<TKey>(
        IDictionary<TKey, Quantity> values,
        TKey key,
        Quantity added)
        where TKey : notnull
    {
        Quantity current = values.TryGetValue(key, out Quantity existing)
            ? existing
            : Quantity.Zero;
        values[key] = current.Add(added);
    }

    private TransportTiming TransportTiming() =>
        new(_config.DockingOverhead, _config.TransferRate, _config.TransferRate);

    private Inventory GetInventory(InventoryId inventoryId) =>
        _inventories.Get(inventoryId)
        ?? throw new KeyNotFoundException($"Missing inventory {inventoryId}.");

    private TransportJob GetTransportJob(TransportJobId jobId) =>
        _transportBoard.GetJob(jobId)
        ?? throw new KeyNotFoundException($"Missing transport job {jobId}.");

    private sealed class Fnv1a64
    {
        private const ulong OffsetBasis = 0xcbf29ce484222325;
        private const ulong Prime = 0x00000100000001b3;

        public ulong Value { get; private set; } = OffsetBasis;

        public void WriteByte(byte value)
        {
            Value ^= value;
            Value = unchecked(Value * Prime);
        }

        public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUInt64(ulong value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            foreach (byte item in bytes)
            {
                WriteByte(item);
            }
        }
    }
}
