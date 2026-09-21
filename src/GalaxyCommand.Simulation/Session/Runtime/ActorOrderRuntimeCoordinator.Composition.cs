namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator : ISimulationRuntime<GameEvent>
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
    private readonly IReadOnlyDictionary<ConstructionDesignId, ShipManeuverCapability>
        _maneuverCapabilities;
    private readonly List<GameEventRecord> _eventRecords = [];
    private bool _isPoisoned;

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
        _navigation = navigation;
        _facts = facts;
        _maneuverCapabilities = BuildManeuverCapabilities(
            setup.Ships.Select(ship => ship.Design)
                .Concat(setup.MaterializationPolicies.SelectMany(
                    policy => policy.AllowedDesigns.Values)));
        _relationships = new RelationshipOwner(setup.Relationships);
        _lifecycle = new EntityLifecycleOwner(
            _movement,
            _control,
            _orders,
            setup.MaterializationPolicies,
            setup.Economy?.MaterialCompatibility);

        _lifecycle.RegisterSetup(setup.Ships);
        _economy = setup.Economy is null
            ? null
            : new SessionEconomyOwner(setup.Economy, _lifecycle);
        _inventoryCommit = new InventoryCommitOwner(_lifecycle.Inventories);

        _engine = new SimulationEngine<GameEvent>(this, _agenda);
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
        SimulationEngineCheckpoint<GameEvent> engineCheckpoint)
    {
        _facts = facts;
        _worldTopology = worldTopology;
        _navigation = navigation;
        _movement = movement;
        _control = control;
        _orders = orders;
        _lifecycle = lifecycle;
        _inventoryCommit = inventoryCommit;
        _relationships = relationships;
        _economy = economy;
        _maneuverCapabilities = BuildManeuverCapabilities(
            lifecycle.MaterializationPolicies.SelectMany(
                policy => policy.AllowedDesigns.Values));
        CheckpointResult<SimulationEngine<GameEvent>> engine =
            SimulationEngine<GameEvent>.RestoreCheckpoint(this, engineCheckpoint);
        if (!engine.IsSuccess)
        {
            throw new InvalidOperationException(engine.Failure!.Message);
        }

        _engine = engine.Value!;
        _agenda = _engine.Agenda;
    }

    internal SimulationTime CurrentTime => _engine.CurrentTime;

    internal IReadOnlyList<GameEventRecord> EventRecords => _eventRecords.AsReadOnly();

    internal ShipId? ResolveShip(EntityId entityId) =>
        _lifecycle.Entities.GetShipId(entityId);

    internal EntityId? ResolveEntity(ShipId shipId) =>
        _lifecycle.Entities.GetEntityId(shipId);

    internal InventoryCommitBatchResult CommitInventoryMutations(
        IEnumerable<InventoryMutationProposal> proposals) =>
        _inventoryCommit.CommitBatch(proposals);

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

    public bool ShouldStop => _isPoisoned;

    internal bool IsHealthy => !_isPoisoned;

    /// <summary>
    /// Captures all runtime-owned sections only after engine and movement state
    /// independently prove the current completed timestamp boundary.
    /// </summary>
    internal CheckpointResult<GameSessionRuntimeCheckpoint> CaptureCheckpoint(
        int factRetentionCapacity)
    {
        if (_isPoisoned)
        {
            return RuntimeRejected(
                "$.checkpoint.health",
                "An unhealthy session cannot produce an authoritative checkpoint.");
        }

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
                factRetentionCapacity);
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

            economyCheckpoint = economy.Value;
        }

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
            _inventoryCommit.CaptureCheckpoint());
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
            ShipOrderCoordinator.RestoreCheckpoint(checkpoint.Orders);
        if (!orders.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(orders.Failure!);
        }

        CheckpointResult<EntityLifecycleOwner> lifecycle = definitions is null
            ? EntityLifecycleOwner.RestoreCheckpoint(
                checkpoint.Lifecycle,
                movement.Value!,
                control.Value!,
                orders.Value!,
                policies.Value!.MaterializationPolicies)
            : materialCompatibility is not null
                ? EntityLifecycleOwner.RestoreCheckpoint(
                    checkpoint.Lifecycle,
                    movement.Value!,
                    control.Value!,
                    orders.Value!,
                    policies.Value!.MaterializationPolicies,
                    definitions,
                    materialCompatibility)
            : EntityLifecycleOwner.RestoreCheckpoint(
                checkpoint.Lifecycle,
                movement.Value!,
                control.Value!,
                orders.Value!,
                policies.Value!.MaterializationPolicies,
                definitions);
        if (!lifecycle.IsSuccess)
        {
            return CheckpointResult<ActorOrderRuntimeCoordinator>.Rejected(lifecycle.Failure!);
        }

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
                    policies.Value.MaterializationPolicies);
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
                checkpoint.Engine);
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
        HashSet<ConstructionDesignId> designs = checkpoint.RuntimePolicies
            .MaterializationPolicies
            .SelectMany(policy => policy!.AllowedDesigns)
            .Select(design => design!.Id)
            .ToHashSet();
        for (int index = 0; index < checkpoint.Lifecycle.LiveShips.Count; index++)
        {
            EntityLifecycleShipCheckpoint ship = checkpoint.Lifecycle.LiveShips[index]!;
            if (!principals.Contains(ship.PrincipalId))
            {
                return new CheckpointValidationFailure(
                    $"$.checkpoint.lifecycle.liveShips[{index}].principalId",
                    "A live ship references an unregistered principal.");
            }

            if (!designs.Contains(ship.DesignId))
            {
                return new CheckpointValidationFailure(
                    $"$.checkpoint.lifecycle.liveShips[{index}].designId",
                    "A live ship references a design absent from the runtime policy manifest.");
            }
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
