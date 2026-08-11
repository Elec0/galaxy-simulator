using System.Buffers.Binary;

namespace GalaxyCommand.Simulation
{

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
        public sealed record ProductionComplete(
            FacilityId FacilityId,
            ProductionJobId JobId) : ScenarioEventKind;
        public sealed record ConstructionComplete(
            FacilityId FacilityId,
            ConstructionOrderId OrderId) : ScenarioEventKind;
    }

    public sealed record ScenarioEventRecord(
        SimulationTime Timestamp,
        EventPhase Phase,
        ulong CreationSequence,
        EventGeneration Generation,
        ScheduledEventDisposition Disposition,
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
        public sealed record Economic(EconomicEvent Event) : PhaseOneEvent;
    }
}

namespace GalaxyCommand.Simulation.Acceptance
{

    /// <summary>
    /// Test-only engine composition, fixture adapters, metrics, and reports for
    /// the integrated Phase 1 acceptance proof.
    /// </summary>
    public sealed class PhaseOneScenario : ISimulationRuntime<PhaseOneEvent>
    {
        private readonly PhaseOneConfig _config;
        private readonly bool _stopWhenFirstShipConstructed;
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
        private readonly EconomicRuntimeSystem _economicSystem;
        private readonly MaterialId[] _knownMaterials;
        private readonly SortedDictionary<LocationId, string> _locationNames;
        private readonly List<ScenarioEventRecord> _eventRecords = [];
        private readonly List<DecisionRecord> _decisionRecords = [];
        private readonly PhaseOneMetrics _metrics = new();
        private ShipId? _constructedShipId;
        private SimulationTime _metricsTime = SimulationTime.Zero;

        public PhaseOneScenario(
            PhaseOneConfig? config = null,
            bool stopWhenFirstShipConstructed = true)
        {
            _config = config ?? new PhaseOneConfig();
            _stopWhenFirstShipConstructed = stopWhenFirstShipConstructed;
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
            var constructionProcesses = new SortedDictionary<FacilityId, ConstructionProcess>(
                EntityIdComparer<FacilityId>.Instance)
            {
                [_shipyard.FacilityId] = _shipyard.Process,
            };
            var constructionLocations = new SortedDictionary<FacilityId, LocationId>(
                EntityIdComparer<FacilityId>.Instance)
            {
                [_shipyard.FacilityId] = _shipyard.LocationId,
            };
            _economicSystem = new EconomicRuntimeSystem(
                new EconomicRuntimeCoordinator(
                    _productionLines,
                    _productionOutputs,
                    _facilityLocations,
                    constructionProcesses,
                    constructionLocations,
                    _inventories,
                    _transportBoard,
                _ships,
                    fixture.LogisticsNavigation,
                    _productionIds,
                    _transportIds,
                    _reservationIds,
                    _capacityReservationIds));
            _knownMaterials = fixture.KnownMaterials;
            _engine = new SimulationEngine<PhaseOneEvent>(this, _agenda);
        }

        public IReadOnlyList<ScenarioEventRecord> EventRecords => _eventRecords;
        public IReadOnlyList<DecisionRecord> DecisionRecords => _decisionRecords;
        public SimulationTime CurrentTime => _engine.CurrentTime;

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
                SimulationTime? arrivesAt = job?.Status is TransportJobStatus.TravelingToSource
                    or TransportJobStatus.TravelingToDestination
                    ? job?.TransitionAt
                    : null;

                ships.Add(new ShipSnapshot(
                    freighter.ShipId,
                    ship.DesignId,
                    freighter.LocationId,
                    freighter.ActiveJobId,
                    job?.Status,
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

        public RunReport AdvanceTo(SimulationTime target) =>
            _engine.RunUntil(target);

        bool ISimulationRuntime<PhaseOneEvent>.ShouldStop =>
            _stopWhenFirstShipConstructed && _constructedShipId is not null;

        void ISimulationRuntime<PhaseOneEvent>.Reconcile(
            SimulationTime now,
            EventAgenda<PhaseOneEvent> agenda) =>
            Reconcile(now);

        void ISimulationRuntime<PhaseOneEvent>.AccrueTo(SimulationTime now) =>
            AccrueFacilityTime(now);

        ScheduledEventDisposition ISimulationRuntime<PhaseOneEvent>.HandleEvent(
            ScheduledEvent<PhaseOneEvent> simulationEvent,
            SimulationTime now,
            EventAgenda<PhaseOneEvent> agenda) =>
            HandleEvent(simulationEvent, now);

        void ISimulationRuntime<PhaseOneEvent>.RecordEvent(
            ScheduledEvent<PhaseOneEvent> simulationEvent,
            ScheduledEventDisposition disposition)
        {
            _eventRecords.Add(new ScenarioEventRecord(
                simulationEvent.Key.Timestamp,
                simulationEvent.Key.Phase,
                simulationEvent.Key.CreationSequence,
                simulationEvent.Generation,
                disposition,
                ToEventKind(simulationEvent.Payload)));
        }

        private ScheduledEventDisposition HandleEvent(
            ScheduledEvent<PhaseOneEvent> scheduled,
            SimulationTime now)
        {
            PhaseOneEvent scenarioEvent = scheduled.Payload;
            switch (scenarioEvent)
            {
                case PhaseOneEvent.Economic economic:
                    return HandleEconomicEvent(
                        economic.Event,
                        scheduled.Key,
                        scheduled.Generation,
                        now);

                default:
                    throw new ArgumentOutOfRangeException(nameof(scheduled));
            }
        }

        private void Reconcile(SimulationTime now)
        {
            EconomicReconciliationResult reconciliation =
                _economicSystem.Reconcile(now, TransportTiming());
            RecordEconomicCommit(now, reconciliation);
        }

        private ScheduledEventDisposition HandleEconomicEvent(
            EconomicEvent economicEvent,
            EventKey eventKey,
            EventGeneration scheduledGeneration,
            SimulationTime now)
        {
            TransportJobStatus? transportStatusBefore =
                economicEvent is EconomicEvent.Transport transportEvent
                ? _transportBoard.GetJob(transportEvent.Event.JobId)?.Status
                : null;
            EconomicEventCommitResult commit = _economicSystem.CommitEvent(
                economicEvent,
                eventKey,
                scheduledGeneration,
                TransportTiming(),
                now);
            switch (commit)
            {
                case EconomicEventCommitResult.Transport transport:
                    RecordTransportEventCommit(
                        economicEvent,
                        transportStatusBefore,
                        transport.Result);
                    break;
                case EconomicEventCommitResult.Production production:
                    if (production.Result.OutputStored is { } output)
                    {
                        IncrementQuantity(
                            _metrics.ProducedMutable,
                            (output.FacilityId, output.MaterialId),
                            output.Quantity);
                    }

                    break;
                case EconomicEventCommitResult.Construction construction:
                    if (construction.Result.Materialization is { } materialization)
                    {
                        _constructedShipId = PhaseOneShipMaterializer.Materialize(
                            materialization,
                            _shipyard,
                            _shipIds,
                            _inventoryIds,
                            _inventories,
                            _ships);
                    }

                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported economic commit {commit.GetType().Name}.");
            }

            return commit.Disposition;
        }

        private void RecordTransportEventCommit(
            EconomicEvent economicEvent,
            TransportJobStatus? statusBefore,
            TransportEventReconciliationResult result)
        {
            if (result.Disposition != ScheduledEventDisposition.Applied)
            {
                return;
            }

            var transport = economicEvent as EconomicEvent.Transport
                ?? throw new InvalidOperationException(
                    "Transport commit did not originate from a transport event.");
            TransportJob after = GetTransportJob(transport.Event.JobId);
            if (statusBefore != TransportJobStatus.Completed
                && after.Status == TransportJobStatus.Completed)
            {
                IncrementQuantity(
                    _metrics.DeliveredMutable,
                    (after.DestinationInventoryId, after.MaterialId),
                    after.Quantity);
                _metrics.TransportJobsCompleted =
                    checked(_metrics.TransportJobsCompleted + 1);
            }

            if (statusBefore != TransportJobStatus.FailedBeforeLoading
                && after.Status == TransportJobStatus.FailedBeforeLoading)
            {
                _metrics.TransportJobsFailed =
                    checked(_metrics.TransportJobsFailed + 1);
            }

            CommitTransportEventProposals(
                result.Continuation.EventProposals,
                RuntimeEvaluationWave.PhysicalCompletion);
        }

        private void RecordEconomicCommit(
            SimulationTime now,
            EconomicReconciliationResult reconciliation)
        {
            foreach (ProductionInputConsumption input
                in reconciliation.Production.Commit.ConsumedInputs)
            {
                IncrementQuantity(
                    _metrics.ConsumedMutable,
                    (input.FacilityId, input.MaterialId),
                    input.Quantity);
            }

            foreach (ConstructionInputConsumption input
                in reconciliation.Construction.Commit.ConsumedInputs)
            {
                IncrementQuantity(
                    _metrics.ConsumedMutable,
                    (input.FacilityId, input.MaterialId),
                    input.Quantity);
            }

            Dictionary<ShipId, TransportJobId> assignedByShip =
                reconciliation.Assignment.Commit.Assignments.ToDictionary(
                    assignment => assignment.ShipId,
                    assignment => assignment.JobId);
            _metrics.TransportJobsCreated = checked(
                _metrics.TransportJobsCreated
                + (ulong)assignedByShip.Count);
            foreach (TransportAdvanceCommit transport
                in reconciliation.TransportAdvance.Commit.Commits)
            {
                if (assignedByShip.TryGetValue(
                        transport.ShipId,
                        out TransportJobId assignedJobId))
                {
                    _decisionRecords.Add(new DecisionRecord(
                        now,
                        transport.ShipId,
                        assignedJobId,
                        DecisionReason.HighestRankedReachableTransport));
                }

                if (transport.Before != transport.After
                    && WaitingReason(transport.After) is { } reason)
                {
                    _decisionRecords.Add(new DecisionRecord(
                        now,
                        transport.ShipId,
                        transport.JobId,
                        reason));
                }
            }

            var eventProposals = new List<AgendaEventProposal<PhaseOneEvent>>();
            foreach (ProductionCompletionProposal completion
                in reconciliation.Production.Commit.CompletionProposals)
            {
                eventProposals.Add(new AgendaEventProposal<PhaseOneEvent>(
                    new AgendaProposalOrder(
                        RuntimeEvaluationWave.ProductionReadiness,
                        completion.FacilityId.Value,
                        completion.JobId.Value,
                        0,
                        0),
                    completion.Timestamp,
                    EventPhase.PhysicalCompletion,
                    completion.Generation,
                    new PhaseOneEvent.Economic(
                        new EconomicEvent.ProductionComplete(
                            completion.FacilityId,
                            completion.JobId))));
            }

            foreach (ConstructionCompletionProposal completion
                in reconciliation.Construction.Commit.CompletionProposals)
            {
                eventProposals.Add(new AgendaEventProposal<PhaseOneEvent>(
                    new AgendaProposalOrder(
                        RuntimeEvaluationWave.ConstructionReadiness,
                        completion.FacilityId.Value,
                        completion.OrderId.Value,
                        0,
                        0),
                    completion.Timestamp,
                    EventPhase.PhysicalCompletion,
                    completion.Generation,
                    new PhaseOneEvent.Economic(
                        new EconomicEvent.ConstructionComplete(
                            completion.FacilityId,
                            completion.OrderId))));
            }

            foreach (TransportEventProposal transport
                in reconciliation.TransportAdvance.Commit.EventProposals)
            {
                eventProposals.Add(new AgendaEventProposal<PhaseOneEvent>(
                    new AgendaProposalOrder(
                        RuntimeEvaluationWave.LogisticsAssignment,
                        transport.ShipId.Value,
                        transport.JobId.Value,
                        TransportEventKind(transport.Event),
                        0),
                    transport.Timestamp,
                    EventPhase.PhysicalCompletion,
                    transport.Generation,
                    new PhaseOneEvent.Economic(
                        new EconomicEvent.Transport(transport.Event))));
            }

            AgendaCommitOwner.Commit(_agenda, eventProposals);
        }

        private static int TransportEventKind(TransportEvent transportEvent) =>
            transportEvent switch
            {
                TransportEvent.Arrive => 0,
                TransportEvent.FinishLoading => 1,
                TransportEvent.FinishUnloading => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(transportEvent)),
            };

        private void CommitTransportEventProposals(
            IEnumerable<TransportEventProposal> transportEvents,
            RuntimeEvaluationWave wave)
        {
            AgendaCommitOwner.Commit(
                _agenda,
                transportEvents.Select(transport =>
                    new AgendaEventProposal<PhaseOneEvent>(
                        new AgendaProposalOrder(
                            wave,
                            transport.ShipId.Value,
                            transport.JobId.Value,
                            TransportEventKind(transport.Event),
                            0),
                        transport.Timestamp,
                        EventPhase.PhysicalCompletion,
                        transport.Generation,
                        new PhaseOneEvent.Economic(
                            new EconomicEvent.Transport(transport.Event)))));
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
            hash.WriteUInt64(record.Generation.Value);
            hash.WriteByte((byte)record.Disposition);
            switch (record.Kind)
            {
                case ScenarioEventKind.Transport transport:
                    hash.WriteByte(0);
                    HashTransportEvent(hash, transport.Event);
                    break;
                case ScenarioEventKind.ProductionComplete production:
                    hash.WriteByte(1);
                    hash.WriteUInt64(production.FacilityId.Value);
                    hash.WriteUInt64(production.JobId.Value);
                    break;
                case ScenarioEventKind.ConstructionComplete construction:
                    hash.WriteByte(2);
                    hash.WriteUInt64(construction.FacilityId.Value);
                    hash.WriteUInt64(construction.OrderId.Value);
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
                PhaseOneEvent.Economic economic => ToEventKind(economic.Event),
                _ => throw new ArgumentOutOfRangeException(nameof(scenarioEvent)),
            };

        private static ScenarioEventKind ToEventKind(EconomicEvent economicEvent) =>
            economicEvent switch
            {
                EconomicEvent.Transport transport =>
                    new ScenarioEventKind.Transport(transport.Event),
                EconomicEvent.ProductionComplete production =>
                    new ScenarioEventKind.ProductionComplete(
                        production.FacilityId,
                        production.JobId),
                EconomicEvent.ConstructionComplete construction =>
                    new ScenarioEventKind.ConstructionComplete(
                        construction.FacilityId,
                        construction.OrderId),
                _ => throw new ArgumentOutOfRangeException(nameof(economicEvent)),
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
}
