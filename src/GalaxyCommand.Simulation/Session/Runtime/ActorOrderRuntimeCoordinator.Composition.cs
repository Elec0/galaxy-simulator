namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator : ISimulationRuntime<GameEvent>, ISimulationStateUpdateRuntime<GameEvent>, ISimulationPhysicalCompletionWaveRuntime<GameEvent>, ISimulationTimestampCompletionRuntime, IDisposable
{
    private readonly EventAgenda<GameEvent> _agenda;
    private readonly SimulationEngine<GameEvent> _engine;
    private readonly SpatialMovement _movement;
    private readonly ActorControlRegistry _control;
    private readonly ShipOrderCoordinator _orders;
    private readonly WorldTopology _worldTopology;
    private readonly ISpatialNavigationPlanner _navigation;
    private readonly BasicGroupMoveFormationResolver _groupMoveFormationResolver =
        new BasicGroupMoveFormationResolver();
    private readonly EntityLifecycleOwner _lifecycle;
    private readonly InventoryCommitOwner _inventoryCommit;
    private readonly SessionEconomyOwner? _economy;
    private readonly RelationshipOwner _relationships;
    private readonly GameFactStore _facts;
    private readonly IReadOnlyList<ShipDesign> _shipDefinitions;
    private readonly IReadOnlyList<SensorDeployableDefinition> _sensorDeployableDefinitions;
    private readonly CargoDispositionPolicy? _cargoPolicy;
    private readonly IReadOnlyDictionary<ConstructionDesignId, ShipManeuverCapability>
        _maneuverCapabilities;
    private readonly List<GameEventRecord> _eventRecords = [];
    private bool _isPoisoned;
    private readonly StationRuntimeOwner? _stations;

    internal ActorOrderRuntimeCoordinator(
        GameSessionSetup setup,
        ISpatialNavigationPlanner navigation,
        GameFactStore facts)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(facts);
        _agenda = new EventAgenda<GameEvent>();
        _movement = new SpatialMovement();
        _control = new ActorControlRegistry();
        _orders = new ShipOrderCoordinator();
        _worldTopology = new WorldTopology(setup.Systems, setup.ConnectorTopology);
        _cargoSystemIds = _worldTopology.Systems.Select(system => system.Id).ToHashSet();
        _navigation = navigation;
        _facts = facts;
        _shipDefinitions = new ShipDefinitionCatalog(setup.Ships.Select(ship => ship.Design)
            .Concat(setup.MaterializationPolicies.SelectMany(policy => policy.AllowedDesigns.Values))
            .Concat(setup.Stations?.Definitions.Modules.SelectMany(module => module.ShipConstructionProvider?.AllowedDesigns ?? []) ?? [])).Definitions;
        _sensorDeployableDefinitions = setup.SensorDeployableDefinitions;
        _cargoPolicy = setup.CargoPolicy;
        _cargoDefinitions = new PhysicalDefinitionCatalog(setup.Ships.SelectMany(ship => ship.Cargo)
            .Concat(setup.SensorDeployableDefinitions.Select(definition => definition.Physical))
            .Concat(setup.Economy?.MaterialCompatibility?.Mappings.Values ?? []).Distinct());
        if (setup.Stations is not null)
            _cargoDefinitions = new PhysicalDefinitionCatalog(_cargoDefinitions.Definitions
                .Concat(setup.Stations.Materials.Mappings.Values).Distinct());
        _sensorRadii = BuildSensorRadii(_shipDefinitions);
        _maneuverCapabilities = BuildManeuverCapabilities(_shipDefinitions);
        _relationships = new RelationshipOwner(setup.Relationships);
        _lifecycle = new EntityLifecycleOwner(
            _movement,
            _control,
            _orders,
            setup.MaterializationPolicies,
            setup.Economy?.MaterialCompatibility ?? setup.Stations?.Materials,
            _sensorDeployableDefinitions, _shipDefinitions);

        _lifecycle.RegisterSetup(setup.Ships);
        foreach (InitialStationInventorySetup seed in setup.StationInventories)
            _lifecycle.RegisterOwnedInventory(new Inventory(seed.Id,
                new InventoryCustody.PrincipalControlled(
                    new InventoryOwnerReference.SessionEntity(seed.StationEntityId), seed.PrincipalId),
                seed.Capacity, setup.Stations!.Materials));
        _economy = setup.Economy is null
            ? null
            : new SessionEconomyOwner(setup.Economy, _lifecycle);
        _inventoryCommit = new InventoryCommitOwner(_lifecycle.Inventories);
        _stations = setup.Stations is null ? null : new StationRuntimeOwner(setup.Stations, _lifecycle, _agenda, CaptureStationBuilder, _inventoryCommit);

        _stations?.BindEconomicProviders(_economy, economicWakeCapacity: EconomicWakeCreationCapacity);
        _engine = new SimulationEngine<GameEvent>(this, _agenda);
        _stations?.BindRemovalTargets(CanRemoveStationTargets, ApplyStationTargetRemoval, StationTargetFactCapacity, StationTargetAgendaCapacity);
    }

    private ActorOrderRuntimeCoordinator(
        GameFactStore facts,
        WorldTopology worldTopology,
        ISpatialNavigationPlanner navigation,
        SpatialMovement movement,
        ActorControlRegistry control,
        ShipOrderCoordinator orders,
        EntityLifecycleOwner lifecycle,
        InventoryCommitOwner inventoryCommit,
        RelationshipOwner relationships,
        SessionEconomyOwner? economy,
        SimulationEngineCheckpoint<GameEvent> engineCheckpoint,
        IReadOnlyList<ShipDesign> shipDefinitions,
        IReadOnlyList<SensorDeployableDefinition> sensorDeployableDefinitions,
        CargoDispositionPolicy? cargoPolicy,
        PhysicalDefinitionCatalog? cargoDefinitions,
        StationRuntimeCheckpoint? stations, EventKey? economicWake, StationTargetContinuationCheckpoint? stationTargetContinuation)
    {
        _facts = facts;
        _worldTopology = worldTopology;
        _cargoSystemIds = worldTopology.Systems.Select(system => system.Id).ToHashSet();
        _navigation = navigation;
        _movement = movement;
        _control = control;
        _orders = orders;
        _lifecycle = lifecycle;
        _inventoryCommit = inventoryCommit;
        _relationships = relationships;
        _shipDefinitions = shipDefinitions;
        _sensorDeployableDefinitions = sensorDeployableDefinitions;
        _cargoPolicy = cargoPolicy;
        _cargoDefinitions = cargoDefinitions ?? new PhysicalDefinitionCatalog(sensorDeployableDefinitions.Select(definition => definition.Physical));
        _sensorRadii = BuildSensorRadii(_shipDefinitions);
        _economy = economy;
        _economicWake = economicWake;
        RestoreStationTargetContinuation(stationTargetContinuation);
        _maneuverCapabilities = BuildManeuverCapabilities(_shipDefinitions);
        CheckpointResult<SimulationEngine<GameEvent>> engine =
            SimulationEngine<GameEvent>.RestoreCheckpoint(this, engineCheckpoint);
        if (!engine.IsSuccess)
        {
            throw new InvalidOperationException(engine.Failure!.Message);
        }

        _engine = engine.Value!;
        _agenda = _engine.Agenda;
        if (stations is not null)
        {
            CheckpointResult<StationRuntimeOwner> restoredStations = StationRuntimeOwner.Restore(stations, _lifecycle,
                _agenda, CaptureStationBuilder, CurrentTime, _worldTopology.Systems.Select(system => system.Id),
                _relationships.CaptureSnapshot().Principals.Select(principal => principal.Id), _inventoryCommit, CanRemoveStationTargets);
            if (!restoredStations.IsSuccess) throw new InvalidOperationException(restoredStations.Failure!.Message);
            _stations = restoredStations.Value!;
            _stations.BindEconomicProviders(_economy, restoring: true, economicWakeCapacity: EconomicWakeCreationCapacity);
            _stations.BindRemovalTargets(CanRemoveStationTargets, ApplyStationTargetRemoval, StationTargetFactCapacity, StationTargetAgendaCapacity);
        }
    }

    internal SimulationTime CurrentTime => _engine.CurrentTime;

    internal IReadOnlyList<GameEventRecord> EventRecords => _eventRecords.AsReadOnly();

    internal ShipId? ResolveShip(EntityId entityId) =>
        _lifecycle.Entities.GetShipId(entityId);

    internal EntityId? ResolveEntity(ShipId shipId) =>
        _lifecycle.Entities.GetEntityId(shipId);

    internal InventoryCommitBatchResult CommitInventoryMutations(
        IEnumerable<InventoryMutationProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        InventoryMutationProposal[] submitted = proposals.ToArray();
        foreach (var proposal in submitted) ArgumentNullException.ThrowIfNull(proposal);
        if (submitted.Length != 0 && !CanRequestEconomicReevaluation())
            return _inventoryCommit.RejectBatchForAdmission(submitted, InventoryCommitRejectionReason.IdentifierCapacityExhausted);
        Dictionary<ShipId, SensorActionFailure?> before = CaptureSensorEligibility();
        InventoryCommitBatchResult result = _inventoryCommit.CommitBatch(submitted);
        if (result.Outcomes.Any(outcome => outcome.WasApplied)) RequestEconomicReevaluation();
        InvalidateSensorEligibility(before);
        _stations?.Reconcile(CurrentTime);
        return result;
    }

    /// <summary>
    /// Emits one lifecycle fact for a newly applied materialization and emits
    /// nothing for deferred or idempotently repeated results.
    /// </summary>
    private void CommitMaterializationFact(ConstructionMaterializationCommit commit)
    {
        if (!commit.WasApplied
            || commit.Result is not ConstructionEntityMaterializationResult.Materialized materialized)
        {
            return;
        }

        GameSessionShip ship = _lifecycle.GetRequiredShip(materialized.ShipId);
        SystemPosition position = _movement.PositionAt(materialized.ShipId, CurrentTime)
            ?? throw new InvalidOperationException(
                $"Materialized ship {materialized.ShipId} has no initial position.");
        EventKey eventKey = materialized.Effect.CompletionEventKey
            ?? throw new InvalidOperationException(
                "Session-owned construction materialization has no scheduled completion event.");
        _facts.Commit(
            CurrentTime,
            new ScheduledEventFactCause(eventKey),
            [
                new GameFactProposal(
                    new GameFactProposalKey(
                        GameFactCommitCategory.EntityLifecycle,
                        materialized.EntityId.Value,
                        materialized.ShipId.Value,
                        0),
                    new EntityMaterializedFact(
                        materialized.EntityId,
                        EntityKind.Ship,
                        materialized.ShipId,
                        EntityMaterializationSourceKind.Construction,
                        ship.PrincipalId,
                        ship.DesignId,
                        position)),
            ]);
    }

    public bool ShouldStop => _isPoisoned || _cargo is { IsHealthy: false } || _cargoAttempts is { IsHealthy: false };

    internal bool IsHealthy => !_isPoisoned && !_disposed && _cargo is not { IsHealthy: false } && _cargoAttempts is not { IsHealthy: false };

    /// <summary>
    /// Captures all runtime-owned sections only after engine and movement state
    /// independently prove the current completed timestamp boundary.
    /// </summary>
    internal CheckpointResult<GameSessionRuntimeCheckpoint> CaptureCheckpoint(
        int factRetentionCapacity)
    {
        if (_isPoisoned || _disposed || _cargo is { IsHealthy: false } || _cargoAttempts is { IsHealthy: false })
        {
            return RuntimeRejected(
                "$.checkpoint.health",
                "An unhealthy session cannot produce an authoritative checkpoint.");
        }

        if (NextControlledMotionBoundary() is not null)
            return RuntimeRejected("$.checkpoint.movement",
                "Controlled-motion session checkpoints require domain roster and scheduling persistence, which is not integrated yet.");

        CheckpointResult<SimulationEngineCheckpoint<GameEvent>> engine =
            _engine.CaptureCheckpoint();
        if (!engine.IsSuccess)
        {
            return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(engine.Failure!);
        }

        CheckpointResult<SpatialMovementCheckpoint> movement =
            _movement.CaptureCheckpoint(CurrentTime);
        if (!movement.IsSuccess)
        {
            return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(movement.Failure!);
        }

        CheckpointResult<RuntimePolicyManifestCheckpoint> policies =
            RuntimePolicyManifest.Capture(
                _worldTopology,
                _navigation,
                _lifecycle.MaterializationPolicies,
                factRetentionCapacity,
                _shipDefinitions, _sensorDeployableDefinitions, _cargoPolicy);
        if (!policies.IsSuccess)
        {
            return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(policies.Failure!);
        }

        SessionEconomyCheckpoint? economyCheckpoint = null;
        if (_economy is not null)
        {
            CheckpointResult<SessionEconomyCheckpoint> economy =
                _economy.CaptureCheckpoint(_navigation);
            if (!economy.IsSuccess)
            {
                return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(
                    economy.Failure!);
            }

            economyCheckpoint = economy.Value! with { PendingWake = _economicWake };
        }

        CheckpointResult<CargoDispositionOwnerCheckpoint>? cargo = _cargo?.CaptureCheckpoint();
        if (cargo is not null && !cargo.IsSuccess)
            return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(cargo.Failure!);
        if (_cargoPolicy is not null && cargo is null)
            return RuntimeRejected("$.checkpoint.cargo", "Cargo authority has not been attached.");
        CheckpointResult<CargoCollectionAttemptOwnerCheckpoint>? attempts = _cargoAttempts?.CaptureCheckpoint();
        if (attempts is not null && !attempts.IsSuccess)
            return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(attempts.Failure!);
        var checkpoint = new GameSessionRuntimeCheckpoint(
            engine.Value!,
            policies.Value!,
            _worldTopology.CaptureCheckpoint(),
            movement.Value!,
            _control.CaptureCheckpoint(),
            _orders.CaptureCheckpoint(),
            _lifecycle.CaptureCheckpoint(),
            _relationships.CaptureCheckpoint(),
            economyCheckpoint,
            _inventoryCommit.CaptureCheckpoint(), cargo?.Value, attempts?.Value,
            Array.AsReadOnly(_cargoTargets.Values.OrderBy(target => target.OrderId.Value).ToArray()), _stations?.CaptureCheckpoint(), CaptureStationTargetContinuation());
        CheckpointValidationFailure? ownerFailure = ValidateActorOwnership(checkpoint);
        if (ownerFailure is not null)
        {
            return CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(ownerFailure);
        }

        CheckpointValidationFailure? agendaFailure = ValidateAgendaReferences(
            checkpoint,
            _lifecycle,
            _economy);
        return agendaFailure is null
            ? CheckpointResult<GameSessionRuntimeCheckpoint>.Success(checkpoint)
            : CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(agendaFailure);
    }

    /// <summary>
    /// Validates every runtime owner in isolation, checks their shared ship and
    /// agenda relationships, and constructs an unpublished coordinator only on success.
    /// </summary>
    internal static CheckpointResult<ActorOrderRuntimeCoordinator> RestoreCheckpoint(
        GameSessionRuntimeCheckpoint checkpoint,
        GameFactStore facts) =>
        RestoreCheckpointCore(checkpoint, facts, definitions: null);

    internal static CheckpointResult<ActorOrderRuntimeCoordinator> RestoreCheckpoint(
        GameSessionRuntimeCheckpoint checkpoint,
        GameFactStore facts,
        PhysicalDefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return RestoreCheckpointCore(checkpoint, facts, definitions);
    }

    internal static CheckpointResult<ActorOrderRuntimeCoordinator> RestoreCheckpoint(
        GameSessionRuntimeCheckpoint checkpoint,
        GameFactStore facts,
        PhysicalDefinitionCatalog definitions,
        MaterialInventoryCompatibilityMap materialCompatibility)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(materialCompatibility);
        return RestoreCheckpointCore(
            checkpoint,
            facts,
            definitions,
            materialCompatibility);
    }

    private static CheckpointResult<ActorOrderRuntimeCoordinator> RestoreCheckpointCore(
        GameSessionRuntimeCheckpoint checkpoint,
        GameFactStore facts,
        PhysicalDefinitionCatalog? definitions,
        MaterialInventoryCompatibilityMap? materialCompatibility = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(facts);
        CheckpointResult<WorldTopology> topology =
            WorldTopology.RestoreCheckpoint(checkpoint.WorldTopology);
        if (!topology.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(topology.Failure!);
        }

        CheckpointResult<RelationshipOwner> relationships =
            RelationshipOwner.RestoreCheckpoint(checkpoint.Relationships);
        if (!relationships.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(
                relationships.Failure!);
        }

        CheckpointResult<ResolvedRuntimePolicies> policies = RuntimePolicyManifest.Resolve(
            checkpoint.RuntimePolicies,
            topology.Value!,
            checkpoint.Relationships.Principals.Select(principal => principal!.Id));
        if (!policies.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(policies.Failure!);
        }

        // A deployable's physical meaning is saved with its independent definition.
        // Explicit caller catalogs may add other inventory kinds but cannot replace that meaning.
        if (checkpoint.Stations is { Setup: { } stationSetup })
        {
            foreach ((MaterialId material, PhysicalDefinition physical) in stationSetup.Materials.Mappings)
                if (definitions?.Get(physical.Key) is { } provided && provided != physical
                    || materialCompatibility?.Get(material) is { } mapped && mapped != physical)
                    return RuntimeRejectedOwner("$.checkpoint.stations.setup.materials", "Station material definitions conflict with supplied content.");
            definitions = new PhysicalDefinitionCatalog((definitions?.Definitions ?? [])
                .Concat(stationSetup.Materials.Mappings.Values.Where(physical => definitions?.Get(physical.Key) is null)));
            materialCompatibility ??= stationSetup.Materials;
        }
        if (policies.Value!.SensorDeployableDefinitions.Count != 0)
        {
            foreach (SensorDeployableDefinition sensor in policies.Value.SensorDeployableDefinitions)
                if (definitions?.Get(sensor.Key) is { } provided && provided != sensor.Physical)
                    return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(new CheckpointValidationFailure(
                        "$.checkpoint.runtimePolicies.sensorDeployableDefinitions",
                        "The supplied physical catalog conflicts with the retained sensor definition."));
            definitions = new PhysicalDefinitionCatalog((definitions?.Definitions ?? [])
                .Concat(policies.Value.SensorDeployableDefinitions.Where(sensor => definitions?.Get(sensor.Key) is null)
                    .Select(sensor => sensor.Physical)));
        }

        CheckpointResult<SpatialMovement> movement = SpatialMovement.RestoreCheckpoint(
            checkpoint.Movement,
            checkpoint.Engine.Agenda.CurrentTime);
        if (!movement.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(movement.Failure!);
        }

        CheckpointResult<ActorControlRegistry> control =
            ActorControlRegistry.RestoreCheckpoint(checkpoint.Control);
        if (!control.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(control.Failure!);
        }

        CheckpointResult<ShipOrderCoordinator> orders =
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint.Orders, UnplannedStationContinuations(checkpoint));
        if (!orders.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(orders.Failure!);
        }

        CheckpointResult<EntityLifecycleOwner> lifecycle = EntityLifecycleOwner.RestoreCheckpoint(
            checkpoint.Lifecycle, movement.Value!, control.Value!, orders.Value!,
            policies.Value!.MaterializationPolicies, definitions, materialCompatibility,
            policies.Value.SensorDeployableDefinitions, policies.Value.ShipDefinitions);
        if (!lifecycle.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(lifecycle.Failure!);
        }

        // Lifecycle owns physical identity but has no topology or policy authority.
        // Join those references before exposing the restored session aggregate.
        HashSet<SystemId> cargoSystems = topology.Value!.Systems.Select(system => system.Id).ToHashSet();
        if (checkpoint.Lifecycle.CargoObjects.Count > 0 && (policies.Value.CargoPolicy is null
            || checkpoint.Lifecycle.CargoObjects.Any(cargo => !cargoSystems.Contains(cargo!.Position.SystemId))))
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(new CheckpointValidationFailure(
                "$.checkpoint.lifecycle.cargoObjects", "Cargo objects require an explicit cargo policy and a registered system."));

        CheckpointResult<InventoryCommitOwner>? inventoryCommit =
            checkpoint.InventoryCommit is null
                ? null
                : InventoryCommitOwner.RestoreCheckpoint(
                    checkpoint.InventoryCommit,
                    lifecycle.Value!.Inventories,
                    definitions ?? new PhysicalDefinitionCatalog([]));
        if (inventoryCommit is not null && !inventoryCommit.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(
                inventoryCommit.Failure!);
        }

        CheckpointValidationFailure? ownerFailure = ValidateActorOwnership(checkpoint);
        if (ownerFailure is not null)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(ownerFailure);
        }

        SessionEconomyOwner? economy = null;
        if (checkpoint.Economy is not null)
        {
            CheckpointResult<SessionEconomyOwner> economyResult =
                SessionEconomyOwner.RestoreCheckpoint(
                    checkpoint.Economy,
                    lifecycle.Value!,
                    topology.Value!,
                    policies.Value.Navigation,
                    policies.Value.ShipDefinitions);
            if (!economyResult.IsSuccess)
            {
                return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(
                    economyResult.Failure!);
            }

            economy = economyResult.Value;
        }

        CheckpointValidationFailure? agendaFailure = ValidateAgendaReferences(
            checkpoint,
            lifecycle.Value!,
            economy);
        if (agendaFailure is not null)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(agendaFailure);
        }

        ActorOrderRuntimeCoordinator restored;
        try
        {
            restored = new ActorOrderRuntimeCoordinator(
                facts,
                topology.Value!,
                policies.Value.Navigation,
                movement.Value!,
                control.Value!,
                orders.Value!,
                lifecycle.Value!,
                inventoryCommit?.Value
                    ?? new InventoryCommitOwner(lifecycle.Value!.Inventories),
                relationships.Value!,
                economy,
                checkpoint.Engine,
                policies.Value.ShipDefinitions,
                policies.Value.SensorDeployableDefinitions,
                policies.Value.CargoPolicy, definitions, checkpoint.Stations, checkpoint.Economy?.PendingWake, checkpoint.StationTargetContinuation);
        }
        catch (InvalidOperationException error)
        {
            return RuntimeRejectedOwner("$.checkpoint.engine", error.Message);
        }

        return CheckpointResult<ActorOrderRuntimeCoordinator>.Success(restored);
    }

    /// <summary>
    /// Requires every live ship to appear exactly once in movement, control,
    /// and order ownership, and validates its principal and design references.
    /// </summary>
    private static CheckpointValidationFailure? ValidateActorOwnership(
        GameSessionRuntimeCheckpoint checkpoint)
    {
        HashSet<ShipId> live = checkpoint.Lifecycle.LiveShips
            .Select(ship => ship!.ShipId)
            .ToHashSet();
        HashSet<ShipId> movement = checkpoint.Movement.Actors
            .Select(actor => actor!.ShipId)
            .ToHashSet();
        HashSet<ShipId> control = checkpoint.Control.Actors
            .Select(actor => actor!.ShipId)
            .ToHashSet();
        HashSet<ShipId> orders = checkpoint.Orders.Actors
            .Select(actor => actor!.ShipId)
            .ToHashSet();
        if (!live.SetEquals(movement))
        {
            return new CheckpointValidationFailure(
                "$.checkpoint.movement.actors",
                "Movement ownership must exactly match live lifecycle ships.");
        }

        if (!live.SetEquals(control))
        {
            return new CheckpointValidationFailure(
                "$.checkpoint.control.actors",
                "Control ownership must exactly match live lifecycle ships.");
        }

        if (!live.SetEquals(orders))
        {
            return new CheckpointValidationFailure(
                "$.checkpoint.orders.actors",
                "Order ownership must exactly match live lifecycle ships.");
        }

        HashSet<PrincipalId> principals = checkpoint.Relationships.Principals
            .Select(principal => principal!.Id)
            .ToHashSet();
        var designs = (checkpoint.RuntimePolicies.ShipDefinitions ?? []).ToDictionary(design => design!.Id, design => design!);
        var inventories = checkpoint.Lifecycle.Inventories.Inventories.ToDictionary(inventory => inventory.Id);
        for (int index = 0; index < checkpoint.Lifecycle.LiveShips.Count; index++)
        {
            EntityLifecycleShipCheckpoint ship = checkpoint.Lifecycle.LiveShips[index]!;
            if (!principals.Contains(ship.PrincipalId))
            {
                return new CheckpointValidationFailure(
                    $"$.checkpoint.lifecycle.liveShips[{index}].principalId",
                    "A live ship references an unregistered principal.");
            }

            if (!designs.TryGetValue(ship.DesignId, out ShipDesignPolicyCheckpoint? definition))
            {
                return new CheckpointValidationFailure(
                    $"$.checkpoint.lifecycle.liveShips[{index}].designId",
                    "A live ship references a design absent from the runtime policy manifest.");
            }
            if (inventories[ship.CargoInventoryId].Capacity != definition.CargoCapacity)
                return new CheckpointValidationFailure($"$.checkpoint.lifecycle.liveShips[{index}].cargoInventoryId",
                    "A live cargo capacity disagrees with its independently retained ship definition.");
        }

        return null;
    }

    /// <summary>
    /// Rejects pending work whose payload cannot be resolved by restored owners;
    /// generation-stale work remains valid when its referenced owner still exists.
    /// </summary>
    private static CheckpointValidationFailure? ValidateAgendaReferences(
        GameSessionRuntimeCheckpoint checkpoint,
        EntityLifecycleOwner lifecycle,
        SessionEconomyOwner? economy)
    {
        ScheduledEvent<GameEvent>[] economicWakes = checkpoint.Engine.Agenda.PendingEvents
            .Where(item => item.Payload is GameEvent.EconomicReevaluation).ToArray();
        if (checkpoint.Economy?.PendingWake is { } wake
            ? economicWakes.Length != 1 || economicWakes[0].Key != wake
            : economicWakes.Length != 0)
            return new("$.checkpoint.economy.pendingWake", "The economic wake identity and its exact agenda envelope must agree.");
        CheckpointValidationFailure? targetFailure = ValidateStationTargetContinuation(checkpoint);
        if (targetFailure is not null) return targetFailure;
        CheckpointValidationFailure? movementFailure =
            ValidateActiveMovementEvents(checkpoint);
        if (movementFailure is not null)
        {
            return movementFailure;
        }

        for (int index = 0; index < checkpoint.Engine.Agenda.PendingEvents.Count; index++)
        {
            ScheduledEvent<GameEvent> scheduled =
                checkpoint.Engine.Agenda.PendingEvents[index];
            bool resolved = scheduled.Payload switch
            {
                GameEvent.SpatialMovement spatial =>
                    lifecycle.Entities.GetEntityId(spatial.Event.ShipId) is not null,
                GameEvent.Economic economic =>
                    economy?.ContainsEventReference(economic.Event) == true,
                GameEvent.SpatialInteraction interaction =>
                    scheduled.Key.Phase == EventPhase.StateUpdate
                    && scheduled.Key.Timestamp == interaction.Forecast.Candidate.Key.Timestamp
                    && scheduled.Generation == interaction.Forecast.Candidate.Interest.Generation,
                GameEvent.SensorActionReevaluation reevaluation =>
                    scheduled.Key.Phase is EventPhase.StateUpdate or EventPhase.Decision
                    && reevaluation.Work is { Count: > 0 }
                    && reevaluation.Work.All(work => work is not null)
                    && reevaluation.Work.Select(work => work.Ship).Distinct().Count() == reevaluation.Work.Count
                    && reevaluation.Work.All(work =>
                        work.Ship.Value != 0 && work.Order.Value != 0 && work.Command.Value != 0
                        && (checkpoint.Orders.OrderIds.NextValue is null
                            || work.Order.Value < checkpoint.Orders.OrderIds.NextValue)),
                GameEvent.CargoCollectionCompletion completion => scheduled.Key.Phase == EventPhase.StateUpdate
                    && checkpoint.CargoCollectionAttempts is { } attempts
                    && attempts.Attempts.Any(attempt => attempt is not null && attempt.ShipId == completion.ShipId
                        && attempt.OrderId == completion.OrderId && attempt.Generation == completion.AttemptGeneration
                        && scheduled.Generation == attempt.Generation && attempt.CompletionKey == scheduled.Key),
                GameEvent.CargoCollectionRangeExit exit => scheduled.Key.Phase == EventPhase.StateUpdate
                    && checkpoint.CargoCollectionAttempts is { } exitAttempts
                    && exitAttempts.Attempts.Any(attempt => attempt is not null && attempt.ShipId == exit.ShipId
                        && attempt.OrderId == exit.OrderId && attempt.Generation == exit.AttemptGeneration
                        && scheduled.Generation == attempt.Generation && attempt.ExitKey == scheduled.Key),
                GameEvent.CargoCollectionReevaluation reevaluation => checkpoint.RuntimePolicies.CargoPolicy is not null
                    && scheduled.Key.Phase is EventPhase.StateUpdate or EventPhase.Decision
                    && scheduled.Key.Timestamp == checkpoint.Engine.Agenda.CurrentTime && scheduled.Generation.Value == 0
                    && reevaluation.Work is { Count: > 0 }
                    && reevaluation.Work.All(work => work is not null && work.ShipId.Value != 0 && work.OrderId.Value != 0
                        && work.AcceptedCommand.Value != 0 && (checkpoint.Orders.OrderIds.NextValue is null
                            || work.OrderId.Value < checkpoint.Orders.OrderIds.NextValue))
                    && reevaluation.Work.Select(work => work.ShipId).Distinct().Count() == reevaluation.Work.Count,
                GameEvent.CargoExpiry expiry => scheduled.Key.Phase == EventPhase.StateUpdate
                    && checkpoint.Cargo is { } cargo && cargo.Objects.Any(item => item.EntityId == expiry.ObjectId
                        && item.ExpiryEvent == scheduled.Key && item.Generation == scheduled.Generation),
                GameEvent.EconomicReevaluation => economy is not null && checkpoint.Economy?.PendingWake == scheduled.Key
                    && scheduled.Key.Phase == EventPhase.Decision
                    && scheduled.Key.Timestamp == checkpoint.Engine.Agenda.CurrentTime && scheduled.Generation == new EventGeneration(0),
                GameEvent.StationTargetContinuation => checkpoint.StationTargetContinuation?.Key == scheduled.Key,
                GameEvent.StationWork work => scheduled.Key.Phase == EventPhase.PhysicalCompletion
                    && checkpoint.Stations is { } stations && stations.Stations.Any(station => station.Id == work.Station
                        && station.Projects.Any(project => project.Id == work.Project && project.Pending == scheduled.Key
                            && project.Generation == scheduled.Generation)),
                _ => false,
            };
            if (!resolved)
            {
                return new CheckpointValidationFailure(
                    $"$.checkpoint.engine.agenda.pendingEvents[{index}].payload",
                    "A pending event payload has no restored authoritative owner.");
            }
        }

        return null;
    }

    /// <summary>
    /// Requires each active physical segment to own the exact completion event
    /// key and payload it recorded, while allowing additional stale live-actor events.
    /// </summary>
    private static CheckpointValidationFailure? ValidateActiveMovementEvents(
        GameSessionRuntimeCheckpoint checkpoint)
    {
        for (int index = 0; index < checkpoint.Movement.Actors.Count; index++)
        {
            SpatialActorCheckpoint actor = checkpoint.Movement.Actors[index];
            bool found = actor.State switch
            {
                ShipSpatialStateCheckpoint.AtPosition => true,
                ShipSpatialStateCheckpoint.LocalMotion motion =>
                    checkpoint.Engine.Agenda.PendingEvents.Any(scheduled =>
                        scheduled.Key == motion.CompletionEventKey
                        && scheduled.Generation == motion.Generation
                        && scheduled.Payload is GameEvent.SpatialMovement
                        {
                            Event: SpatialMovementEvent.Arrive arrive,
                        }
                        && arrive.ShipId == actor.ShipId
                        && arrive.MotionId == motion.Id
                        && arrive.Generation == motion.Generation),
                ShipSpatialStateCheckpoint.ConnectorTransit transit =>
                    checkpoint.Engine.Agenda.PendingEvents.Any(scheduled =>
                        scheduled.Key == transit.CompletionEventKey
                        && scheduled.Generation == transit.Generation
                        && scheduled.Payload is GameEvent.SpatialMovement
                        {
                            Event: SpatialMovementEvent.Emerge emerge,
                        }
                        && emerge.ShipId == actor.ShipId
                        && emerge.TransitId == transit.Id
                        && emerge.Generation == transit.Generation),
                ShipSpatialStateCheckpoint.AnalyticManeuver maneuver =>
                    HasExactManeuverEvents(
                        checkpoint.Engine.Agenda.PendingEvents,
                        actor.ShipId,
                        maneuver),
                _ => false,
            };
            if (!found)
            {
                return new CheckpointValidationFailure(
                    $"$.checkpoint.movement.actors[{index}].state",
                    "An active spatial segment has no exact matching agenda completion.");
            }
        }

        return null;
    }

    /// <summary>
    /// Requires every saved analytic boundary to resolve to its exact agenda
    /// key, generation, motion identity, and phase payload in cursor order.
    /// </summary>
    private static bool HasExactManeuverEvents(
        IReadOnlyList<ScheduledEvent<GameEvent>> pendingEvents,
        ShipId shipId,
        ShipSpatialStateCheckpoint.AnalyticManeuver maneuver)
    {
        int expectedCount = maneuver.Plan.Phases.Count
            - maneuver.CurrentPhaseIndex;
        if (maneuver.PendingEventKeys.Count != expectedCount)
        {
            return false;
        }

        for (int offset = 0; offset < expectedCount; offset++)
        {
            int phaseIndex = maneuver.CurrentPhaseIndex + offset;
            EventKey key = maneuver.PendingEventKeys[offset];
            ScheduledEvent<GameEvent>? scheduled = pendingEvents.SingleOrDefault(
                candidate => candidate.Key == key);
            if (scheduled is null
                || scheduled.Generation != maneuver.Generation
                || scheduled.Payload is not GameEvent.SpatialMovement
                {
                    Event: SpatialMovementEvent.Maneuver spatial,
                }
                || spatial.ShipId != shipId
                || spatial.Event.MotionId != maneuver.Id
                || spatial.Event.Generation != maneuver.Generation)
            {
                return false;
            }

            bool payloadMatches = phaseIndex < maneuver.Plan.Phases.Count - 1
                ? spatial.Event is ManeuverScheduleEvent.PhaseBoundary boundary
                    && boundary.PhaseIndex == phaseIndex
                : spatial.Event is ManeuverScheduleEvent.Complete;
            if (!payloadMatches)
            {
                return false;
            }
        }

        return true;
    }

    private static CheckpointResult<GameSessionRuntimeCheckpoint> RuntimeRejected(
        string path,
        string message) =>
        CheckpointResult<GameSessionRuntimeCheckpoint>.Rejected(
            new CheckpointValidationFailure(path, message));

    private static CheckpointResult<ActorOrderRuntimeCoordinator> RuntimeRejectedOwner(
        string path,
        string message) =>
        CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(
            new CheckpointValidationFailure(path, message));

    /// <summary>
    /// Rejects an operation after a post-prepare invariant failure has made
    /// this session unsafe to advance, command, capture, or save.
    /// </summary>

}
