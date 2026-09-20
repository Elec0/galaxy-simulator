namespace GalaxyCommand.Simulation;

public abstract record GameEventKind
{
    private GameEventKind()
    {
    }

    public sealed record SpatialMovement(SpatialMovementEvent Event) : GameEventKind;

    public sealed record Economic(EconomicEvent Event) : GameEventKind;
}

public sealed record GameEventRecord(
    SimulationTime Timestamp,
    EventPhase Phase,
    ulong CreationSequence,
    EventGeneration Generation,
    ScheduledEventDisposition Disposition,
    GameEventKind Kind);

internal abstract record GameEvent
{
    private GameEvent()
    {
    }

    internal sealed record SpatialMovement(SpatialMovementEvent Event) : GameEvent;

    internal sealed record Economic(EconomicEvent Event) : GameEvent;
}

internal sealed record PreparedMovementCancellation(
    EventKey EventKey,
    EventGeneration Generation,
    GameEvent Event);

/// <summary>
/// Fixed persistent coordinator for actor commands, orders, movement, spatial
/// events, and their semantic facts.
/// </summary>
internal sealed class ActorOrderRuntimeCoordinator : ISimulationRuntime<GameEvent>
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
    internal void ThrowIfUnhealthy()
    {
        if (_isPoisoned)
        {
            throw new InvalidOperationException(
                "The game session is unhealthy after an invariant failure.");
        }
    }

    internal RunReport AdvanceTo(SimulationTime target) =>
        _engine.RunUntil(target);

    internal GameSnapshot CaptureSnapshot()
    {
        IReadOnlyList<ShipSpatialSnapshot> spatial =
            _movement.CaptureSnapshot(CurrentTime);
        return new GameSnapshot(
            CurrentTime,
            GameSnapshotCollection.Copy(_worldTopology.Systems.Select(system =>
                new GameSystemSnapshot(system.Id, system.Name))),
            GameSnapshotCollection.Copy(_worldTopology.Connectors.Endpoints.Select(endpoint =>
                new ConnectorEndpointSnapshot(
                    endpoint.Id,
                    endpoint.Position))),
            GameSnapshotCollection.Copy(_worldTopology.Connectors.Connections.Select(connection =>
                new TransitConnectionSnapshot(
                    connection.Id,
                    connection.SourceEndpointId,
                    connection.DestinationEndpointId,
                    connection.Duration))),
            _relationships.CaptureSnapshot(),
            GameSnapshotCollection.Copy(spatial.Select(ship =>
            {
                GameSessionShip record = _lifecycle.GetRequiredShip(ship.ShipId);
                Inventory cargo = _lifecycle.GetRequiredCargo(ship.ShipId);
                return new GameShipSnapshot(
                    _lifecycle.Entities.GetEntityId(ship.ShipId)
                        ?? throw new InvalidOperationException(
                            $"Ship {ship.ShipId} has no live entity registration."),
                    ship.ShipId,
                    record.PrincipalId,
                    record.DesignId,
                    record.CargoInventoryId,
                    cargo.Capacity,
                    record.ManeuverCapabilityRevision,
                    ship.State,
                    ship.Velocity,
                    ship.Heading,
                    _control.Capture(ship.ShipId),
                    _orders.CaptureCurrent(ship.ShipId),
                    _orders.CaptureQueue(ship.ShipId),
                    _orders.CaptureSuspended(ship.ShipId));
            })));
    }

    internal GameplayCommandHandlingResult Handle(
        GameplayCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Command switch
        {
            MoveShipCommand move => HandleMove(envelope.Source, move),
            MoveShipGroupCommand groupMove => HandleGroupMove(envelope.Source, groupMove),
            CancelShipOrderCommand cancel => HandleCancel(envelope.Source, cancel),
            CancelShipGroupCommand groupCancel => HandleGroupCancel(envelope.Source, groupCancel),
            BeginScriptedOverrideCommand begin =>
                HandleBeginOverride(envelope.Source, begin),
            EndScriptedOverrideCommand end =>
                HandleEndOverride(envelope.Source, end),
            _ => new GameplayCommandHandlingResult(
                CommandResult.Rejected(
                    CommandRejectionCodes.UnsupportedCommand,
                    $"Gameplay command '{envelope.Command.Kind}' is not supported yet.")),
        };
    }

    /// <summary>
    /// Applies a prepared entity removal, invalidates inbound entity-target
    /// orders, and commits their facts before the removal fact.
    /// </summary>
    internal EntityRemovalResult RemoveEntity(EntityRemovalRequest request)
    {
        ThrowIfUnhealthy();
        EntityRemovalPreparation preparation = _lifecycle.PrepareRemoval(
            request,
            permitOwnerReleasedCommitments: _economy is not null);
        if (preparation is EntityRemovalPreparation.Resolved resolved)
        {
            return resolved.Value;
        }

        PreparedEntityRemoval removal =
            ((EntityRemovalPreparation.Prepared)preparation).Value;
        PreparedEconomyEntityRemoval? economyRemoval = null;
        if (_economy is not null
            && !_economy.TryPrepareEntityRemoval(
                removal.ShipId,
                removal.CargoInventoryId,
                out economyRemoval))
        {
            return new EntityRemovalResult.Rejected(
                request,
                EntityRemovalRejectionReason.OwnerConflict);
        }

        EntityRemovalResult.Rejected? cancellationRejection =
            PrepareMovementCancellations(removal, out PreparedMovementCancellation[] cancellations);
        if (cancellationRejection is not null)
        {
            return cancellationRejection;
        }

        foreach (PreparedMovementCancellation cancellation in cancellations)
        {
            if (_agenda.TryCancelExact(
                cancellation.EventKey,
                cancellation.Generation,
                cancellation.Event))
            {
                continue;
            }

            _isPoisoned = true;
            throw new InvalidOperationException(
                $"Prepared cancellation for {cancellation.EventKey} no longer matches the agenda.");
        }

        if (_economy is not null && economyRemoval is not null)
        {
            try
            {
                _economy.ApplyEntityRemoval(economyRemoval);
            }
            catch
            {
                _isPoisoned = true;
                throw;
            }
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        TargetedShipOrder[] applyOrder = removal.InboundOrders
            .OrderBy(targeted => targeted.WasCurrentActive)
            .ThenBy(targeted => targeted.ShipId.Value)
            .ThenBy(targeted => targeted.OrderId.Value)
            .ToArray();
        foreach (TargetedShipOrder targeted in applyOrder)
        {
            if (targeted.WasCurrentActive)
            {
                EndActiveLocalMotion(
                    targeted.ShipId,
                    LocalMotionEndReason.TargetRemoved,
                    factProposals);
            }

            _orders.ApplyTargetRemoval(targeted, transitions);
        }

        EntityRemovalResult result = _lifecycle.ApplyRemoval(removal, CurrentTime);
        foreach (ShipId shipId in removal.InboundOrders
                     .Where(targeted => targeted.WasCurrentActive)
                     .Select(targeted => targeted.ShipId)
                     .Distinct()
                     .OrderBy(shipId => shipId.Value))
        {
            StartOrContinueOrders(shipId, transitions, factProposals);
        }

        AddOrderTransitionProposals(
            transitions
                .OrderBy(transition => transition.ShipId.Value)
                .ThenBy(transition => transition.OrderId.Value),
            factProposals);
        var removed = (EntityRemovalResult.Removed)result;
        factProposals.Add(new GameFactProposal(
            new GameFactProposalKey(
                GameFactCommitCategory.EntityLifecycle,
                removed.Request.EntityId.Value,
                removed.ShipId.Value,
                0),
            new EntityRemovedFact(
                removed.Request.EntityId,
                EntityKind.Ship,
                removed.ShipId,
                removed.Request.Reason,
                removed.Request.CargoDisposition)));
        _facts.Commit(
            CurrentTime,
            new EntityRemovalFactCause(removed.Request),
            factProposals);
        return result;
    }

    /// <summary>
    /// Commits prepared relationship state and publishes one fact for each
    /// changed directional pair in stable principal order.
    /// </summary>
    internal StandingChangeBatchResult CommitStandingChanges(
        StandingChangeBatch batch)
    {
        StandingChangePreparation preparation =
            _relationships.PrepareStandingChanges(batch);
        if (preparation is StandingChangePreparation.Resolved resolved)
        {
            return resolved.Result;
        }

        PreparedStandingChange prepared =
            ((StandingChangePreparation.Prepared)preparation).Value;
        if (!_facts.CanCommit(prepared.ChangedOutcomes.Count))
        {
            return new StandingChangeBatchResult.Rejected(
                batch.Id,
                StandingChangeRejectionReason.FactSequenceExhausted);
        }

        StandingChangeBatchResult result =
            _relationships.ApplyStandingChanges(prepared);
        _facts.Commit(
            CurrentTime,
            new StandingChangeFactCause(batch.Id),
            prepared.ChangedOutcomes.Select(outcome => new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.Relationship,
                    outcome.AssessingPrincipalId.Value,
                    outcome.SubjectPrincipalId.Value,
                    0),
                new StandingChangedFact(outcome))));
        return result;
    }

    /// <summary>
    /// Commits one prepared diplomacy and grant batch and publishes changed
    /// outcomes in deterministic relationship order.
    /// </summary>
    internal RelationshipPolicyChangeBatchResult CommitRelationshipPolicyChanges(
        RelationshipPolicyChangeBatch batch)
    {
        RelationshipPolicyChangePreparation preparation =
            _relationships.PreparePolicyChanges(batch);
        if (preparation is RelationshipPolicyChangePreparation.Resolved resolved)
        {
            return resolved.Result;
        }

        PreparedRelationshipPolicyChange prepared =
            ((RelationshipPolicyChangePreparation.Prepared)preparation).Value;
        int factCount = prepared.Result.DiplomaticOutcomes.Count(value => value.Changed)
            + prepared.Result.GrantOutcomes.Count;
        if (!_facts.CanCommit(factCount))
        {
            return new RelationshipPolicyChangeBatchResult.Rejected(
                batch.Id,
                RelationshipPolicyChangeRejectionReason.FactSequenceExhausted);
        }

        RelationshipPolicyChangeBatchResult result =
            _relationships.ApplyPolicyChanges(prepared);
        IEnumerable<GameFactProposal> diplomaticFacts = prepared.Result
            .DiplomaticOutcomes
            .Where(outcome => outcome.Changed)
            .Select(outcome => new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.RelationshipDiplomacy,
                    outcome.LowerPrincipalId.Value,
                    outcome.UpperPrincipalId.Value,
                    0),
                new DiplomaticConditionChangedFact(outcome)));
        IEnumerable<GameFactProposal> grantFacts = prepared.Result.GrantOutcomes
            .Select(outcome => new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.RelationshipGrant,
                    outcome.Id.Value,
                    0,
                    0),
                outcome.ResultingIssued
                    ? new RelationshipGrantIssuedFact(outcome)
                    : new RelationshipGrantRevokedFact(outcome)));
        _facts.Commit(
            CurrentTime,
            new RelationshipPolicyChangeFactCause(batch.Id),
            diplomaticFacts.Concat(grantFacts));
        return result;
    }

    internal DiplomaticCondition GetDiplomaticCondition(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId) =>
        _relationships.GetDiplomaticCondition(firstPrincipalId, secondPrincipalId);

    internal bool HasEffectiveRelationshipGrant(
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind) =>
        _relationships.HasEffectiveGrant(issuerPrincipalId, holderPrincipalId, kind);

    public void Reconcile(SimulationTime now, EventAgenda<GameEvent> agenda)
    {
        if (_economy is null)
        {
            return;
        }

        EconomicReconciliationResult reconciliation = _economy.Runtime.Reconcile(
            now,
            _economy.TransportTiming);
        var proposals = new List<AgendaEventProposal<GameEvent>>();
        foreach (ProductionCompletionProposal completion in
                 reconciliation.Production.Commit.CompletionProposals)
        {
            proposals.Add(new AgendaEventProposal<GameEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ProductionReadiness,
                    completion.FacilityId.Value,
                    completion.JobId.Value,
                    EffectKind: 0,
                    LocalOrdinal: 0),
                completion.Timestamp,
                EventPhase.PhysicalCompletion,
                completion.Generation,
                new GameEvent.Economic(new EconomicEvent.ProductionComplete(
                    completion.FacilityId,
                    completion.JobId))));
        }

        foreach (ConstructionCompletionProposal completion in
                 reconciliation.Construction.Commit.CompletionProposals)
        {
            proposals.Add(new AgendaEventProposal<GameEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ConstructionReadiness,
                    completion.FacilityId.Value,
                    completion.OrderId.Value,
                    EffectKind: 0,
                    LocalOrdinal: 0),
                completion.Timestamp,
                EventPhase.PhysicalCompletion,
                completion.Generation,
                new GameEvent.Economic(new EconomicEvent.ConstructionComplete(
                    completion.FacilityId,
                    completion.OrderId))));
        }

        AddTransportEventProposals(
            reconciliation.TransportAdvance.Commit.EventProposals,
            proposals);
        if (proposals.Count > 0)
        {
            _ = AgendaCommitOwner.Commit(agenda, proposals);
        }
    }

    public void AccrueTo(SimulationTime now)
    {
    }

    /// <inheritdoc/>
    public ScheduledEventDisposition HandleEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        SimulationTime now,
        EventAgenda<GameEvent> agenda)
    {
        if (simulationEvent.Payload is GameEvent.Economic economic)
        {
            return HandleEconomicEvent(simulationEvent, economic.Event, now, agenda);
        }

        if (simulationEvent.Payload is not GameEvent.SpatialMovement spatial)
        {
            throw new InvalidOperationException(
                $"Unsupported game event {simulationEvent.Payload.GetType().Name}.");
        }

        LocalMotionSegment? endingMotion = spatial.Event switch
        {
            SpatialMovementEvent.Arrive arrive
                when _movement.GetState(arrive.ShipId)
                    is ShipSpatialState.Moving moving
                    && moving.Motion.Id == arrive.MotionId =>
                moving.Motion,
            _ => null,
        };
        ConnectorTransitSegment? completingTransit = spatial.Event switch
        {
            SpatialMovementEvent.Emerge emerge
                when _movement.GetState(emerge.ShipId)
                    is ShipSpatialState.ConnectorTransit traversing
                    && traversing.Transit.Id == emerge.TransitId =>
                traversing.Transit,
            _ => null,
        };
        ScheduledTerminalManeuver? endingManeuver = spatial.Event switch
        {
            SpatialMovementEvent.Maneuver maneuver
                when maneuver.Event is ManeuverScheduleEvent.Complete
                    && _movement.GetState(maneuver.ShipId)
                        is ShipSpatialState.AnalyticManeuver active
                    && active.Maneuver.MotionId == maneuver.Event.MotionId =>
                active.Maneuver,
            _ => null,
        };
        ScheduledTerminalManeuver? phaseBoundaryManeuver = spatial.Event switch
        {
            SpatialMovementEvent.Maneuver maneuver
                when maneuver.Event is ManeuverScheduleEvent.PhaseBoundary
                    && _movement.GetState(maneuver.ShipId)
                        is ShipSpatialState.AnalyticManeuver active
                    && active.Maneuver.MotionId == maneuver.Event.MotionId =>
                active.Maneuver,
            _ => null,
        };
        ScheduledTerminalManeuver? waypointManeuver =
            phaseBoundaryManeuver is { } boundaryManeuver
                && spatial.Event is SpatialMovementEvent.Maneuver
                {
                    Event: ManeuverScheduleEvent.PhaseBoundary waypointBoundary,
                }
                && boundaryManeuver.IsWaypointBoundary(
                    waypointBoundary.PhaseIndex)
                    ? boundaryManeuver
                    : null;
        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        ScheduledEventDisposition disposition = _movement.HandleEvent(
            spatial.Event,
            simulationEvent.Generation,
            now);
        if (disposition == ScheduledEventDisposition.Applied)
        {
            bool continueOrders = true;
            switch (spatial.Event)
            {
                case SpatialMovementEvent.Arrive arrive:
                    {
                        ShipOrder active = _orders.GetActive(arrive.ShipId)
                            ?? throw new InvalidOperationException(
                                $"Ship {arrive.ShipId} completed local motion without an active order.");
                        LocalMotionSegment motion = endingMotion
                            ?? throw new InvalidOperationException(
                                $"Applied arrival for ship {arrive.ShipId} had no matching motion.");
                        factProposals.Add(PhysicalWorkEndedProposal(
                            arrive.ShipId,
                            motion.Id.Value,
                            new ShipLocalMotionEndedFact(
                                arrive.ShipId,
                                Snapshot(motion),
                                motion.Destination,
                                now,
                                LocalMotionEndReason.Arrived,
                                active.Id)));
                        _orders.CompleteLeg(
                            arrive.ShipId,
                            active.Id,
                            arrive.MotionId);
                        break;
                    }
                case SpatialMovementEvent.Maneuver
                {
                    Event: ManeuverScheduleEvent.PhaseBoundary boundary,
                }:
                    if (phaseBoundaryManeuver is { } transitioning)
                    {
                        AddCruiseTransitionProposal(
                            spatial.Event.ShipId,
                            boundary,
                            transitioning,
                            now,
                            factProposals);
                    }

                    if (waypointManeuver is { } continuing)
                    {
                        ShipId shipId = spatial.Event.ShipId;
                        ShipOrder active = _orders.GetActive(shipId)
                            ?? throw new InvalidOperationException(
                                $"Ship {shipId} reached a waypoint without an active order.");
                        TravelLeg.Local waypointLeg = _orders.NextLeg(
                            shipId,
                            active.Id) as TravelLeg.Local
                            ?? throw new InvalidOperationException(
                                $"Ship {shipId} reached a local waypoint without a local route leg.");
                        ShipKinematicState reached = continuing.Plan.StateAt(now);
                        if (!ManeuverArrival.IsFlyThroughWaypointReached(
                            reached,
                            waypointLeg.Destination))
                        {
                            throw new InvalidOperationException(
                                $"Ship {shipId} waypoint boundary did not satisfy its positional tolerance.");
                        }

                        _orders.CompleteLeg(
                            shipId,
                            active.Id,
                            continuing.MotionId);
                        _orders.BindMotion(
                            shipId,
                            active.Id,
                            continuing.MotionId);
                        factProposals.Add(WaypointArrivedProposal(
                            shipId,
                            continuing.MotionId.Value,
                            boundary.PhaseIndex,
                            new ShipWaypointArrivedFact(
                                shipId,
                                continuing.MotionId,
                                waypointLeg.Destination,
                                reached.Position,
                                now,
                                active.Id)));
                    }

                    // Other physical boundaries update spatial state only;
                    // the bound order owns the complete terminal maneuver.
                    continueOrders = false;
                    break;
                case SpatialMovementEvent.Maneuver maneuver
                    when maneuver.Event is ManeuverScheduleEvent.Complete:
                    {
                        ShipOrder active = _orders.GetActive(maneuver.ShipId)
                            ?? throw new InvalidOperationException(
                                $"Ship {maneuver.ShipId} completed a maneuver without an active order.");
                        ScheduledTerminalManeuver completed = endingManeuver
                            ?? throw new InvalidOperationException(
                                $"Applied completion for ship {maneuver.ShipId} had no matching maneuver.");
                        ShipKinematicState final = completed.Plan.StateAt(now);
                        factProposals.Add(PhysicalWorkEndedProposal(
                            maneuver.ShipId,
                            completed.MotionId.Value,
                            new ShipLocalMotionEndedFact(
                                maneuver.ShipId,
                                Snapshot(completed),
                                final.Position,
                                now,
                                LocalMotionEndReason.Arrived,
                                active.Id)));
                        _orders.CompleteLeg(
                            maneuver.ShipId,
                            active.Id,
                            completed.MotionId);
                        break;
                    }
                case SpatialMovementEvent.Emerge emerge:
                    {
                        ConnectorTransitSegment transit = completingTransit
                            ?? throw new InvalidOperationException(
                                $"Applied emergence for ship {emerge.ShipId} had no matching transit.");
                        ShipOrderId? transitOrderId = null;
                        if (_orders.GetActive(emerge.ShipId) is { } active
                            && _orders.IsBoundTransit(
                                emerge.ShipId,
                                emerge.TransitId))
                        {
                            transitOrderId = active.Id;
                            _orders.CompleteTransit(
                                emerge.ShipId,
                                active.Id,
                                emerge.TransitId);
                        }

                        factProposals.Add(PhysicalWorkEndedProposal(
                            emerge.ShipId,
                            transit.Id.Value,
                            new ShipConnectorTransitCompletedFact(
                                emerge.ShipId,
                                Snapshot(transit),
                                now,
                                transitOrderId)));
                        break;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported spatial event {spatial.Event.GetType().Name}.");
            }

            if (continueOrders)
            {
                StartOrContinueOrders(
                    spatial.Event.ShipId,
                    transitions,
                    factProposals);
                AddOrderTransitionProposals(transitions, factProposals);
            }

            if (factProposals.Count > 0)
            {
                _facts.Commit(
                    now,
                    new ScheduledEventFactCause(simulationEvent.Key),
                    factProposals);
            }
        }

        return disposition;
    }

    public void RecordEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        ScheduledEventDisposition disposition)
    {
        GameEventKind kind = simulationEvent.Payload switch
        {
            GameEvent.SpatialMovement spatial =>
                new GameEventKind.SpatialMovement(spatial.Event),
            GameEvent.Economic economic => new GameEventKind.Economic(economic.Event),
            _ => throw new InvalidOperationException(
                $"Unsupported game event {simulationEvent.Payload.GetType().Name}."),
        };
        _eventRecords.Add(new GameEventRecord(
            simulationEvent.Key.Timestamp,
            simulationEvent.Key.Phase,
            simulationEvent.Key.CreationSequence,
            simulationEvent.Generation,
            disposition,
            kind));
    }

    /// <summary>
    /// Commits one private economic completion, acknowledges completed ship
    /// construction through lifecycle, and schedules any transport continuation.
    /// </summary>
    private ScheduledEventDisposition HandleEconomicEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        EconomicEvent economicEvent,
        SimulationTime now,
        EventAgenda<GameEvent> agenda)
    {
        if (_economy is null)
        {
            return ScheduledEventDisposition.IgnoredMissingReference;
        }

        EconomicEventCommitResult result = _economy.Runtime.CommitEvent(
            economicEvent,
            simulationEvent.Key,
            simulationEvent.Generation,
            _economy.TransportTiming,
            now);
        if (result is EconomicEventCommitResult.Construction
            {
                Result.Materialization: { } materialization,
            })
        {
            ConstructionProcess source = _economy.GetRequiredConstructionProcess(
                materialization.FacilityId);
            ConstructionMaterializationCommit commit = _lifecycle.MaterializeConstruction(
                source,
                materialization,
                now);
            CommitMaterializationFact(commit);
        }

        if (result is EconomicEventCommitResult.Transport transport)
        {
            var proposals = new List<AgendaEventProposal<GameEvent>>();
            AddTransportEventProposals(transport.Result.Continuation.EventProposals, proposals);
            if (proposals.Count > 0)
            {
                _ = AgendaCommitOwner.Commit(agenda, proposals);
            }
        }

        return result.Disposition;
    }

    /// <summary>
    /// Converts committed transport continuation proposals into the session's
    /// deterministic agenda order without exposing its agenda to the owner.
    /// </summary>
    private static void AddTransportEventProposals(
        IEnumerable<TransportEventProposal> transportEvents,
        List<AgendaEventProposal<GameEvent>> destination)
    {
        foreach (TransportEventProposal transportEvent in transportEvents)
        {
            destination.Add(new AgendaEventProposal<GameEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.LogisticsAssignment,
                    transportEvent.ShipId.Value,
                    transportEvent.JobId.Value,
                    EffectKind: TransportEventKindOrder(transportEvent.Event),
                    LocalOrdinal: 0),
                transportEvent.Timestamp,
                EventPhase.PhysicalCompletion,
                transportEvent.Generation,
                new GameEvent.Economic(new EconomicEvent.Transport(
                    transportEvent.Event))));
        }
    }

    /// <summary>
    /// Provides a fixed local ordering for distinct same-job transport events.
    /// </summary>
    private static int TransportEventKindOrder(TransportEvent transportEvent) =>
        transportEvent switch
        {
            TransportEvent.Arrive => 0,
            TransportEvent.FinishLoading => 1,
            TransportEvent.FinishUnloading => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(transportEvent)),
        };

    /// <summary>
    /// Evaluates and commits one ordinary move through the per-ship order
    /// lifecycle, buffering all resulting semantic facts for command commit.
    /// </summary>
    private GameplayCommandHandlingResult HandleMove(
        CommandSource source,
        MoveShipCommand command)
    {
        MoveOrderEvaluation evaluation = EvaluateMove(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        MoveOrderProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted move-order evaluation produced no proposal.");
        CommitMove(proposal, transitions, factProposals);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Commits one group move only after every member has passed the shared
    /// stable-state preflight, preserving all-or-nothing replacement behavior.
    /// </summary>
    private GameplayCommandHandlingResult HandleGroupMove(
        CommandSource source,
        MoveShipGroupCommand command)
    {
        GroupMoveOrderEvaluation evaluation = EvaluateGroupMove(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        IReadOnlyList<MoveOrderProposal> proposals = evaluation.Proposals
            ?? throw new InvalidOperationException(
                "Accepted group move evaluation produced no proposals.");

        // Commit only after every member has read the same state. Evaluating
        // during this loop would turn a rejected one-shot command into a
        // partial replacement of earlier members' work.
        foreach (MoveOrderProposal proposal in proposals)
        {
            CommitMove(proposal, transitions, factProposals);
        }

        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Applies an already-preflighted move proposal and buffers its domain
    /// effects. It must not revalidate controller or route state because a
    /// group command commits all members against one earlier stable view.
    /// </summary>
    private void CommitMove(
        MoveOrderProposal proposal,
        List<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        ShipOrder order = _orders.Create(
            proposal.Source,
            proposal.Destination,
            proposal.RequestedHeading);
        switch (proposal.Placement)
        {
            case OrderPlacement.ReplaceAll:
                EndActiveLocalMotion(
                    proposal.ShipId,
                    LocalMotionEndReason.ReplacedByCommand,
                    factProposals);
                _orders.ReplaceAll(
                    proposal.ShipId,
                    order,
                    transitions);
                if (proposal.Plan is { } replacementPlan)
                {
                    _orders.SetPlan(
                        proposal.ShipId,
                        order.Id,
                        replacementPlan,
                        transitions);
                }

                StartOrContinueOrders(
                    proposal.ShipId,
                    transitions,
                    factProposals);
                break;
            case OrderPlacement.Append:
                bool becameActive = _orders.Append(
                    proposal.ShipId,
                    order,
                    transitions);
                if (becameActive)
                {
                    if (proposal.Plan is { } appendedPlan)
                    {
                        _orders.SetPlan(
                            proposal.ShipId,
                            order.Id,
                            appendedPlan,
                            transitions);
                    }

                    StartOrContinueOrders(
                        proposal.ShipId,
                        transitions,
                        factProposals);
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported order placement {proposal.Placement}.");
        }
    }

    /// <summary>
    /// Evaluates and commits one ordinary order cancellation through the
    /// per-ship lifecycle, including active-motion materialization when needed.
    /// </summary>
    private GameplayCommandHandlingResult HandleCancel(
        CommandSource source,
        CancelShipOrderCommand command)
    {
        CancelOrderEvaluation evaluation = EvaluateCancel(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        CancelOrderProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted cancel-order evaluation produced no proposal.");
        CommitCancellation(proposal, transitions, factProposals);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Cancels every eligible selected current order after shared preflight;
    /// idle selected members remain deliberate no-ops.
    /// </summary>
    private GameplayCommandHandlingResult HandleGroupCancel(
        CommandSource source,
        CancelShipGroupCommand command)
    {
        GroupCancelOrderEvaluation evaluation = EvaluateGroupCancel(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        IReadOnlyList<CancelOrderProposal> proposals = evaluation.Proposals
            ?? throw new InvalidOperationException(
                "Accepted group cancellation evaluation produced no proposals.");
        foreach (CancelOrderProposal proposal in proposals)
        {
            CommitCancellation(proposal, transitions, factProposals);
        }

        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Cancels an already-resolved current or queued order, buffering its
    /// physical and lifecycle effects. Group cancellation supplies only active
    /// proposals, while individual cancellation may also supply queued work.
    /// </summary>
    private void CommitCancellation(
        CancelOrderProposal proposal,
        List<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        if (proposal.WasActive)
        {
            EndActiveLocalMotion(
                proposal.ShipId,
                LocalMotionEndReason.CancelledByCommand,
                factProposals);
        }

        CancelOrderDisposition disposition =
            _orders.Cancel(
                proposal.ShipId,
                proposal.OrderId,
                transitions);
        if (disposition == CancelOrderDisposition.Missing)
        {
            throw new InvalidOperationException(
                $"Evaluated order {proposal.OrderId} disappeared before commit.");
        }

        if (disposition == CancelOrderDisposition.Active)
        {
            StartOrContinueOrders(
                proposal.ShipId,
                transitions,
                factProposals);
        }
    }

    private MoveOrderEvaluation EvaluateMove(
        CommandSource source,
        MoveShipCommand command)
    {
        if (RejectIneligible(command.ShipId, source) is { } rejection)
        {
            return new MoveOrderEvaluation(null, rejection);
        }

        SystemPosition? origin = _movement.PositionAt(
            command.ShipId,
            CurrentTime);
        if (origin is null)
        {
            if (_movement.GetState(command.ShipId)
                is not ShipSpatialState.ConnectorTransit)
            {
                throw new InvalidOperationException(
                    $"Controlled ship {command.ShipId} has no spatial state.");
            }

            return new MoveOrderEvaluation(
                new MoveOrderProposal(
                    command.ShipId,
                    source,
                    command.Destination,
                    command.Placement,
                    command.RequestedHeading,
                    null),
                null);
        }

        NavigationPlanResult result = Plan(
            command.ShipId,
            origin.Value,
            command.Destination);
        if (result is NavigationPlanResult.Unreachable unreachable)
        {
            return new MoveOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.InvalidState,
                    $"Destination is unreachable: {unreachable.Reason}."));
        }

        TravelPlan plan = ((NavigationPlanResult.Planned)result).Plan;
        ValidateExecutablePlan(origin.Value, command.Destination, plan);
        return new MoveOrderEvaluation(
            new MoveOrderProposal(
                command.ShipId,
                source,
                command.Destination,
                command.Placement,
                command.RequestedHeading,
                plan),
            null);
    }

    /// <summary>
    /// Resolves every group member before mutation so a stale, uncontrolled, or
    /// unreachable member rejects the complete one-shot move.
    /// </summary>
    private GroupMoveOrderEvaluation EvaluateGroupMove(
        CommandSource source,
        MoveShipGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlyList<SystemPosition> destinations = _groupMoveFormationResolver.Resolve(
            command.ShipIds,
            command.Destination);
        if (destinations.Count != command.ShipIds.Count)
        {
            return new GroupMoveOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.InvalidIntent,
                    "The group formation resolver did not return one destination per ship."));
        }

        var proposals = new List<MoveOrderProposal>(command.ShipIds.Count);
        for (int index = 0; index < command.ShipIds.Count; index++)
        {
            MoveOrderEvaluation member = EvaluateMove(
                source,
                new MoveShipCommand(
                    command.ShipIds[index],
                    new NavigationDestination.Position(destinations[index]),
                    OrderPlacement.ReplaceAll));
            if (member.Rejection is { } rejection)
            {
                return new GroupMoveOrderEvaluation(
                    null,
                    CommandResult.Rejected(
                        rejection.RejectionCode
                            ?? CommandRejectionCodes.InvalidState,
                        $"Group move rejected for ship {command.ShipIds[index]}: {rejection.Reason}"));
            }

            proposals.Add(member.Proposal
                ?? throw new InvalidOperationException(
                    "Accepted group move member evaluation produced no proposal."));
        }

        return new GroupMoveOrderEvaluation(proposals, null);
    }

    private CancelOrderEvaluation EvaluateCancel(
        CommandSource source,
        CancelShipOrderCommand command)
    {
        if (RejectIneligible(command.ShipId, source) is { } rejection)
        {
            return new CancelOrderEvaluation(null, rejection);
        }

        if (!_orders.Contains(command.ShipId, command.OrderId))
        {
            return new CancelOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.OrderNotFound,
                    $"Ship {command.ShipId} has no active or queued order {command.OrderId}."));
        }

        return new CancelOrderEvaluation(
            new CancelOrderProposal(
                command.ShipId,
                command.OrderId,
                _orders.IsActive(command.ShipId, command.OrderId)),
            null);
    }

    /// <summary>
    /// Validates every selected ship before collecting its current order. Idle
    /// members deliberately contribute no proposal after the shared admission
    /// succeeds, so they are no-ops rather than partial cancellation failures.
    /// </summary>
    private GroupCancelOrderEvaluation EvaluateGroupCancel(
        CommandSource source,
        CancelShipGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        var proposals = new List<CancelOrderProposal>();
        foreach (ShipId shipId in command.ShipIds)
        {
            if (RejectIneligible(shipId, source) is { } rejection)
            {
                return new GroupCancelOrderEvaluation(
                    null,
                    CommandResult.Rejected(
                        rejection.RejectionCode
                            ?? CommandRejectionCodes.InvalidState,
                        $"Group cancellation rejected for ship {shipId}: {rejection.Reason}"));
            }

            ShipOrder? active = _orders.GetActive(shipId);
            if (active is not null)
            {
                proposals.Add(new CancelOrderProposal(shipId, active.Id, WasActive: true));
            }
        }

        return new GroupCancelOrderEvaluation(proposals, null);
    }

    private GameplayCommandHandlingResult HandleBeginOverride(
        CommandSource source,
        BeginScriptedOverrideCommand command)
    {
        BeginOverrideEvaluation evaluation = EvaluateBeginOverride(
            source,
            command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        BeginOverrideProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted begin-override evaluation produced no proposal.");
        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        EndActiveLocalMotion(
            proposal.ShipId,
            LocalMotionEndReason.SuspendedByScriptedOverride,
            factProposals);
        _orders.BeginOverride(proposal.ShipId, transitions);
        _control.BeginOverride(
            proposal.ShipId,
            proposal.Source,
            proposal.Reason);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    private GameplayCommandHandlingResult HandleEndOverride(
        CommandSource source,
        EndScriptedOverrideCommand command)
    {
        EndOverrideEvaluation evaluation = EvaluateEndOverride(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        EndOverrideProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted end-override evaluation produced no proposal.");
        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        EndActiveLocalMotion(
            proposal.ShipId,
            LocalMotionEndReason.ScriptedOverrideEnded,
            factProposals);
        _orders.EndOverride(
            proposal.ShipId,
            proposal.ReleasePolicy,
            transitions);
        _control.EndOverride(proposal.ShipId);
        StartOrContinueOrders(
            proposal.ShipId,
            transitions,
            factProposals);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    private BeginOverrideEvaluation EvaluateBeginOverride(
        CommandSource source,
        BeginScriptedOverrideCommand command)
    {
        ActorOverrideValidation validation = _control.ValidateBeginOverride(
            command.ShipId,
            source,
            command.ExpectedRevision);
        CommandResult? rejection = RejectInvalidOverride(
            validation,
            command.ShipId);
        return rejection is null
            ? new BeginOverrideEvaluation(
                new BeginOverrideProposal(
                    command.ShipId,
                    source,
                    command.Reason),
                null)
            : new BeginOverrideEvaluation(null, rejection);
    }

    private EndOverrideEvaluation EvaluateEndOverride(
        CommandSource source,
        EndScriptedOverrideCommand command)
    {
        ActorOverrideValidation validation = _control.ValidateEndOverride(
            command.ShipId,
            source,
            command.ExpectedRevision);
        CommandResult? rejection = RejectInvalidOverride(
            validation,
            command.ShipId);
        return rejection is null
            ? new EndOverrideEvaluation(
                new EndOverrideProposal(
                    command.ShipId,
                    command.ReleasePolicy),
                null)
            : new EndOverrideEvaluation(null, rejection);
    }

    /// <summary>
    /// Advances one active order until it either completes, waits for transit,
    /// or publishes exactly one bound physical movement schedule.
    /// </summary>
    private void StartOrContinueOrders(
        ShipId shipId,
        ICollection<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        while (_orders.GetActive(shipId) is { } active)
        {
            SystemPosition? availablePosition = _movement.PositionAt(
                shipId,
                CurrentTime);
            if (availablePosition is null)
            {
                if (_movement.GetState(shipId)
                    is not ShipSpatialState.ConnectorTransit)
                {
                    throw new InvalidOperationException(
                        $"Order actor {shipId} has no spatial state.");
                }

                _orders.WaitForTransitCompletion(
                    shipId,
                    active.Id,
                    transitions);
                return;
            }

            SystemPosition current = availablePosition.Value;
            if (active.Plan is null)
            {
                NavigationPlanResult result = Plan(
                    shipId,
                    current,
                    active.Destination);
                if (result is NavigationPlanResult.Unreachable)
                {
                    _orders.FailActive(
                        shipId,
                        active.Id,
                        transitions);
                    continue;
                }

                TravelPlan plan = ((NavigationPlanResult.Planned)result).Plan;
                ValidateExecutablePlan(current, active.Destination, plan);
                _orders.SetPlan(
                    shipId,
                    active.Id,
                    plan,
                    transitions);
            }

            TravelLeg? nextLeg = _orders.NextLeg(shipId, active.Id);
            if (nextLeg is null)
            {
                if (RuntimeDestinationSatisfied(
                        shipId,
                        current,
                        active.Destination,
                        active.RequestedHeading))
                {
                    _orders.CompleteActive(
                        shipId,
                        active.Id,
                        transitions);
                    continue;
                }

                // A route can legitimately contain no local leg when its
                // destination is already reached. The remaining heading goal
                // still owns a physical stationary maneuver before completion.
                if (active.RequestedHeading is { } heading
                    && DestinationSatisfied(current, active.Destination))
                {
                    ShipKinematicState start = _movement.KinematicStateAt(
                        shipId,
                        CurrentTime)
                        ?? throw new InvalidOperationException(
                            $"Ship {shipId} has no system-local kinematic state.");
                    EffectiveShipManeuverCapability capability =
                        ResolveManeuverCapability(shipId);
                    BoundedTerminalPlanSelection headingSelection =
                        BoundedTerminalManeuverPlanner.Select(
                            CurrentTime,
                            start,
                            current,
                            heading,
                            capability,
                            ManeuverObjective.FastestArrival);
                    ExecutableBoundedTerminalManeuverPlan headingPlan =
                        headingSelection.ExecutablePlan
                        ?? throw new InvalidOperationException(
                            $"Terminal heading for ship {shipId} produced no executable analytic plan.");
                    StartTerminalManeuver(
                        shipId,
                        active,
                        headingPlan,
                        waypointPhaseIndices: [],
                        factProposals);
                    return;
                }

                throw new InvalidOperationException(
                    $"Order {active.Id} exhausted its plan before reaching its destination.");
            }

            switch (nextLeg)
            {
                case TravelLeg.Local local:
                    {
                        if (local.Duration == SimulationDuration.Zero
                            || local.Origin == local.Destination)
                        {
                            _orders.CompleteLeg(shipId, active.Id, null);
                            continue;
                        }

                        ShipKinematicState start = _movement.KinematicStateAt(
                            shipId,
                            CurrentTime)
                            ?? throw new InvalidOperationException(
                                $"Ship {shipId} has no system-local kinematic state.");
                        EffectiveShipManeuverCapability capability =
                            ResolveManeuverCapability(shipId);
                        TravelPlan route = active.Plan
                            ?? throw new InvalidOperationException(
                                $"Order {active.Id} has no travel plan.");
                        TravelLeg[] remaining = route.Legs
                            .Skip(active.NextLegIndex)
                            .ToArray();
                        TravelLeg.Local[] remainingLocal = remaining
                            .OfType<TravelLeg.Local>()
                            .ToArray();
                        bool hasMultipleLocalLegs = remaining.Length > 1
                            && remainingLocal.Length == remaining.Length;
                        ShipHeading? terminalHeading = hasMultipleLocalLegs
                            || remaining.Length == 1
                                ? active.RequestedHeading
                                : null;
                        // A connector boundary requires an exact terminal local
                        // state at its source endpoint. Plan that approach as an
                        // ordinary maneuver instead of treating the complete
                        // cross-system route as one local fly-through path.
                        SystemPosition terminalDestination = hasMultipleLocalLegs
                            ? remainingLocal[^1].Destination
                            : local.Destination;
                        BoundedTerminalPlanSelection selection =
                            BoundedTerminalManeuverPlanner.Select(
                                CurrentTime,
                                start,
                                terminalDestination,
                                terminalHeading,
                                capability,
                                ManeuverObjective.FastestArrival);
                        if (selection.ExecutablePlan is not { } plan)
                        {
                            throw new InvalidOperationException(
                                $"Local route for ship {shipId} produced no executable analytic plan.");
                        }

                        IReadOnlyList<int> waypointPhaseIndices = [];
                        if (hasMultipleLocalLegs)
                        {
                            SystemPosition[] waypoints = remainingLocal[..^1]
                                .Select(static leg => leg.Destination)
                                .ToArray();
                            if (!WaypointManeuverPlan.TryCreate(
                                    plan,
                                    start,
                                    terminalDestination,
                                    waypoints,
                                    out WaypointManeuverPlan? waypointPlan))
                            {
                                SystemPosition[] destinations = remainingLocal
                                    .Select(static leg => leg.Destination)
                                    .ToArray();
                                if (!WaypointManeuverPlan.TryCreateRoute(
                                        CurrentTime,
                                        start,
                                        destinations,
                                        capability,
                                        ManeuverObjective.FastestArrival,
                                        terminalHeading,
                                        out waypointPlan))
                                {
                                    throw new InvalidOperationException(
                                        $"Waypoint route for ship {shipId} produced no executable analytic plan.");
                                }
                            }

                            plan = waypointPlan!.Plan;
                            waypointPhaseIndices = waypointPlan.WaypointPhaseIndices;
                        }

                        StartTerminalManeuver(
                            shipId,
                            active,
                            plan,
                            waypointPhaseIndices,
                            factProposals);
                        return;
                    }
                case TravelLeg.Connector connector:
                    {
                        ConnectorTransitCommit<GameEvent> commit =
                            _movement.CommitStartConnector(
                                shipId,
                                connector,
                                CurrentTime,
                                movement =>
                                    (GameEvent)new GameEvent.SpatialMovement(movement));
                        ConnectorTransitSegment transit = commit.Transit;
                        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
                            _agenda,
                            [commit.EventProposal]);
                        _movement.BindCompletionEvent(
                            shipId,
                            AssertSingleEventKey(agendaCommit));
                        _orders.BindTransit(shipId, active.Id, transit.Id);
                        factProposals.Add(PhysicalWorkStartedProposal(
                            shipId,
                            transit.Id.Value,
                            new ShipConnectorTransitStartedFact(
                                shipId,
                                Snapshot(transit),
                                active.Id)));
                        return;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported travel leg {nextLeg.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Commits and binds one already-selected analytic schedule, retaining the
    /// active order link and any route waypoint phase identities atomically.
    /// </summary>
    private void StartTerminalManeuver(
        ShipId shipId,
        ShipOrder active,
        ExecutableBoundedTerminalManeuverPlan plan,
        IReadOnlyList<int> waypointPhaseIndices,
        List<GameFactProposal> factProposals)
    {
        TerminalManeuverCommit<GameEvent> commit =
            _movement.CommitStartTerminalManeuver(
                shipId,
                plan,
                ManeuverObjective.FastestArrival,
                CurrentTime,
                movement =>
                    (GameEvent)new GameEvent.SpatialMovement(movement),
                waypointPhaseIndices);
        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
            _agenda,
            commit.EventProposals);
        _movement.BindTerminalManeuverEvents(
            shipId,
            agendaCommit.EventKeys);
        _orders.BindMotion(
            shipId,
            active.Id,
            commit.Maneuver.MotionId);
        factProposals.Add(PhysicalWorkStartedProposal(
            shipId,
            commit.Maneuver.MotionId.Value,
            new ShipLocalMotionStartedFact(
                shipId,
                Snapshot(commit.Maneuver),
                active.Id)));
    }

    /// <summary>
    /// Resolves the current base design at its authored mass. Typed equipment
    /// contributions remain owned by TASK-068 and can replace this lookup
    /// without changing maneuver planning or schedule ownership.
    /// </summary>
    private EffectiveShipManeuverCapability ResolveManeuverCapability(
        ShipId shipId)
    {
        GameSessionShip ship = _lifecycle.GetRequiredShip(shipId);
        ShipManeuverCapability capability = _maneuverCapabilities.GetValueOrDefault(
            ship.DesignId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no maneuver capability for design {ship.DesignId}.");
        return capability.ResolveForMass(capability.BaseMassKilograms);
    }

    private void EndActiveLocalMotion(
        ShipId shipId,
        LocalMotionEndReason reason,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(factProposals);
        ShipSpatialState? state = _movement.GetState(shipId);
        if (state is not ShipSpatialState.Moving
            && state is not ShipSpatialState.AnalyticManeuver)
        {
            return;
        }

        ShipOrder active = _orders.GetActive(shipId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has local motion without an active order.");
        LocalMotionSnapshot motion;
        switch (state)
        {
            case ShipSpatialState.Moving moving:
                motion = Snapshot(moving.Motion);
                if (!_movement.CommitCancel(shipId, CurrentTime))
                {
                    throw new InvalidOperationException(
                        $"Ship {shipId} local motion disappeared before cancellation commit.");
                }

                break;
            case ShipSpatialState.AnalyticManeuver analytic:
                motion = Snapshot(analytic.Maneuver);
                bool wasCruising = analytic.Maneuver.CurrentPhase?.Kind
                    == ManeuverPhaseKind.CruiseTravel;
                AgendaCancellationCheck cancellation =
                    _movement.TryInterruptTerminalManeuver(
                        shipId,
                        CurrentTime,
                        _agenda,
                        movement =>
                            (GameEvent)new GameEvent.SpatialMovement(movement),
                        out ManeuverInterruption? interruption);
                if (cancellation != AgendaCancellationCheck.Matches
                    || interruption is null)
                {
                    throw new InvalidOperationException(
                        $"Ship {shipId} maneuver cancellation did not match its pending agenda events.");
                }

                if (wasCruising
                    && reason == LocalMotionEndReason.ReplacedByCommand)
                {
                    ShipKinematicState dropout = interruption.MaterializedState;
                    factProposals.Add(PhysicalCruiseTransitionProposal(
                        shipId,
                        analytic.Maneuver.MotionId.Value,
                        analytic.Maneuver.CurrentPhaseIndex,
                        new ShipCruiseDroppedOutFact(
                            shipId,
                            analytic.Maneuver.MotionId,
                            dropout.Position,
                            dropout.Velocity,
                            CurrentTime,
                            active.Id)));
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported local movement state {state.GetType().Name}.");
        }

        SystemPosition finalPosition = _movement.PositionAt(shipId, CurrentTime)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no materialized position after cancelling local motion.");
        factProposals.Add(PhysicalWorkEndedProposal(
            shipId,
            motion.Id.Value,
            new ShipLocalMotionEndedFact(
                shipId,
                motion,
                finalPosition,
                CurrentTime,
                reason,
                active.Id)));
    }

    /// <summary>
    /// Prepares exact cancellation records for the removed actor's active
    /// movement and verifies their identity against the agenda without
    /// changing either owner.
    /// </summary>
    private EntityRemovalResult.Rejected? PrepareMovementCancellations(
        PreparedEntityRemoval removal,
        out PreparedMovementCancellation[] cancellations)
    {
        if (_movement.GetState(removal.ShipId)
            is ShipSpatialState.AnalyticManeuver analytic)
        {
            IReadOnlyList<PendingManeuverScheduleEvent> pending =
                analytic.Maneuver.PendingEvents
                ?? throw new InvalidOperationException(
                    $"Ship {removal.ShipId} has an unbound analytic maneuver.");
            var prepared = new PreparedMovementCancellation[pending.Count];
            for (int index = 0; index < pending.Count; index++)
            {
                PendingManeuverScheduleEvent boundary = pending[index];
                GameEvent expected = new GameEvent.SpatialMovement(
                    new SpatialMovementEvent.Maneuver(
                        removal.ShipId,
                        boundary.Payload));
                AgendaCancellationCheck boundaryCheck = _agenda.CheckCancellation(
                    boundary.EventKey,
                    boundary.Generation,
                    expected);
                if (boundaryCheck != AgendaCancellationCheck.Matches)
                {
                    cancellations = [];
                    EntityRemovalRejectionReason reason =
                        boundaryCheck == AgendaCancellationCheck.Missing
                            ? EntityRemovalRejectionReason.PendingMovementEventMissing
                            : EntityRemovalRejectionReason.PendingMovementEventMismatch;
                    return new EntityRemovalResult.Rejected(
                        removal.Request,
                        reason);
                }

                prepared[index] = new PreparedMovementCancellation(
                    boundary.EventKey,
                    boundary.Generation,
                    expected);
            }

            Array.Sort(prepared, static (left, right) =>
                left.EventKey.CompareTo(right.EventKey));
            cancellations = prepared;
            return null;
        }

        PendingMovementCompletion? completion =
            _movement.GetPendingCompletion(removal.ShipId);
        if (completion is null)
        {
            cancellations = [];
            return null;
        }

        GameEvent expectedEvent = completion switch
        {
            PendingMovementCompletion.Arrival arrival =>
                new GameEvent.SpatialMovement(new SpatialMovementEvent.Arrive(
                    arrival.ShipId,
                    arrival.MotionId,
                    arrival.Generation)),
            PendingMovementCompletion.Emergence emergence =>
                new GameEvent.SpatialMovement(new SpatialMovementEvent.Emerge(
                    emergence.ShipId,
                    emergence.TransitId,
                    emergence.Generation)),
            _ => throw new InvalidOperationException(
                $"Unsupported pending movement completion {completion.GetType().Name}."),
        };
        AgendaCancellationCheck check = _agenda.CheckCancellation(
            completion.EventKey,
            completion.Generation,
            expectedEvent);
        if (check != AgendaCancellationCheck.Matches)
        {
            cancellations = [];
            EntityRemovalRejectionReason reason = check == AgendaCancellationCheck.Missing
                ? EntityRemovalRejectionReason.PendingMovementEventMissing
                : EntityRemovalRejectionReason.PendingMovementEventMismatch;
            return new EntityRemovalResult.Rejected(removal.Request, reason);
        }

        cancellations =
        [
            new PreparedMovementCancellation(
                completion.EventKey,
                completion.Generation,
                expectedEvent),
        ];
        Array.Sort(cancellations, static (left, right) =>
            left.EventKey.CompareTo(right.EventKey));
        return null;
    }

    /// <summary>
    /// Extracts the one key created for a single movement completion proposal.
    /// </summary>
    private static EventKey AssertSingleEventKey(AgendaCommitResult commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return commit.EventKeys.Count == 1
            ? commit.EventKeys[0]
            : throw new InvalidOperationException(
                "A movement completion proposal must create exactly one agenda event.");
    }

    private static void AddOrderTransitionProposals(
        IEnumerable<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        int ordinal = 0;
        foreach (ShipOrderTransition transition in transitions)
        {
            factProposals.Add(new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.OrderTransition,
                    transition.ShipId.Value,
                    transition.OrderId.Value,
                    ordinal),
                new ShipOrderTransitionFact(
                    transition.ShipId,
                    transition.OrderId,
                    transition.Source,
                    transition.Destination,
                    transition.PreviousStatus,
                    transition.NextStatus,
                    transition.Reason,
                    transition.RequestedHeading)));
            ordinal = checked(ordinal + 1);
        }
    }

    private static GameFactProposal PhysicalWorkEndedProposal(
        ShipId shipId,
        ulong activityId,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalWorkEnded,
                shipId.Value,
                activityId,
                0),
            fact);

    private static GameFactProposal PhysicalWorkStartedProposal(
        ShipId shipId,
        ulong activityId,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalWorkStarted,
                shipId.Value,
                activityId,
                0),
            fact);

    private static GameFactProposal WaypointArrivedProposal(
        ShipId shipId,
        ulong activityId,
        int phaseIndex,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalWaypoint,
                shipId.Value,
                activityId,
                phaseIndex),
            fact);

    /// <summary>
    /// Emits only the two semantic cruise transitions. Boundaries inserted for
    /// waypoints inside a spool or cruise phase retain the same adjacent phase
    /// kind and therefore remain diagnostic-only.
    /// </summary>
    private void AddCruiseTransitionProposal(
        ShipId shipId,
        ManeuverScheduleEvent.PhaseBoundary boundary,
        ScheduledTerminalManeuver maneuver,
        SimulationTime now,
        List<GameFactProposal> factProposals)
    {
        int nextIndex = boundary.PhaseIndex + 1;
        if (nextIndex >= maneuver.Plan.Phases.Count)
        {
            return;
        }

        ManeuverPhaseKind ending = maneuver.Plan.Phases[boundary.PhaseIndex].Kind;
        ManeuverPhaseKind starting = maneuver.Plan.Phases[nextIndex].Kind;
        bool entersCruise = ending == ManeuverPhaseKind.MovingSpool
            && starting == ManeuverPhaseKind.CruiseTravel;
        bool dropsOut = ending == ManeuverPhaseKind.CruiseTravel
            && starting == ManeuverPhaseKind.ActiveBrake;
        if (!entersCruise && !dropsOut)
        {
            return;
        }

        ShipOrder order = _orders.GetActive(shipId)
            ?? throw new InvalidOperationException(
                $"Ship {shipId} crossed a cruise boundary without an active order.");
        ShipKinematicState state = maneuver.Plan.StateAt(now);
        GameFact fact = entersCruise
            ? new ShipCruiseEnteredFact(
                shipId,
                maneuver.MotionId,
                state.Position,
                state.Velocity,
                now,
                order.Id)
            : new ShipCruiseDroppedOutFact(
                shipId,
                maneuver.MotionId,
                state.Position,
                state.Velocity,
                now,
                order.Id);
        factProposals.Add(PhysicalCruiseTransitionProposal(
            shipId,
            maneuver.MotionId.Value,
            boundary.PhaseIndex,
            fact));
    }

    private static GameFactProposal PhysicalCruiseTransitionProposal(
        ShipId shipId,
        ulong activityId,
        int phaseIndex,
        GameFact fact) =>
        new(
            new GameFactProposalKey(
                GameFactCommitCategory.PhysicalCruiseTransition,
                shipId.Value,
                activityId,
                phaseIndex),
            fact);

    private static LocalMotionSnapshot Snapshot(LocalMotionSegment motion) =>
        new(
            motion.Id,
            motion.Generation,
            motion.Origin,
            motion.Destination,
            motion.DepartedAt,
            motion.ArrivesAt,
            motion.CompletionEventKey);

    /// <summary>
    /// Projects the complete analytic schedule through the existing local-work
    /// fact contract. Internal phase keys remain diagnostic-only; the final key
    /// represents completion of the complete local movement.
    /// </summary>
    private static LocalMotionSnapshot Snapshot(
        ScheduledTerminalManeuver maneuver)
    {
        EventKey? completionKey = maneuver.PendingEvents is { Count: > 0 } pending
            ? pending[^1].EventKey
            : null;
        return new LocalMotionSnapshot(
            maneuver.MotionId,
            maneuver.Generation,
            maneuver.Plan.StateAt(maneuver.Plan.StartsAt).Position,
            maneuver.Plan.StateAt(maneuver.Plan.EndsAt).Position,
            maneuver.Plan.StartsAt,
            maneuver.Plan.EndsAt,
            completionKey);
    }

    private static ConnectorTransitSnapshot Snapshot(
        ConnectorTransitSegment transit) =>
        new(
            transit.Id,
            transit.Generation,
            transit.ConnectionId,
            transit.Source,
            transit.Destination,
            transit.DepartedAt,
            transit.ArrivesAt,
            transit.CompletionEventKey);

    /// <summary>
    /// Builds one stable maneuver-capability catalog from initial and
    /// materializable ship definitions. Reusing an identifier with different
    /// authored movement behavior is rejected before any command can plan.
    /// </summary>
    private static Dictionary<ConstructionDesignId, ShipManeuverCapability>
        BuildManeuverCapabilities(IEnumerable<ShipDesign> designs)
    {
        ArgumentNullException.ThrowIfNull(designs);
        var capabilities = new Dictionary<
            ConstructionDesignId,
            ShipManeuverCapability>();
        foreach (ShipDesign design in designs)
        {
            ArgumentNullException.ThrowIfNull(design);
            if (capabilities.TryGetValue(
                    design.Id,
                    out ShipManeuverCapability? existing)
                && existing != design.ManeuverCapability)
            {
                throw new InvalidOperationException(
                    $"Ship design {design.Id} has conflicting maneuver capabilities.");
            }

            capabilities[design.Id] = design.ManeuverCapability;
        }

        return capabilities;
    }

    private NavigationPlanResult Plan(
        ShipId shipId,
        SystemPosition origin,
        NavigationDestination destination)
    {
        NavigationDestination planningDestination = destination;
        if (destination is NavigationDestination.Entity entity)
        {
            ShipId? targetShipId = _lifecycle.Entities.GetShipId(entity.EntityId);
            SystemPosition? targetPosition = targetShipId is { } target
                ? _movement.PositionAt(target, CurrentTime)
                : null;
            if (targetPosition is null)
            {
                return new NavigationPlanResult.Unreachable(
                    NavigationFailureReason.EntityUnavailable);
            }

            planningDestination = new NavigationDestination.Position(
                targetPosition.Value);
        }

        NavigationPlanResult result = _navigation.Plan(new NavigationRequest(
            shipId,
            origin,
            planningDestination,
            CurrentTime));
        return result is NavigationPlanResult.Planned planned
            && planningDestination != destination
            ? new NavigationPlanResult.Planned(
                new TravelPlan(destination, planned.Plan.Legs))
            : result;
    }

    private void ValidateExecutablePlan(
        SystemPosition origin,
        NavigationDestination destination,
        TravelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Destination != destination)
        {
            throw new InvalidOperationException(
                "Navigation returned a plan for a different destination.");
        }

        SystemPosition expectedOrigin = origin;
        foreach (TravelLeg travelLeg in plan.Legs)
        {
            switch (travelLeg)
            {
                case TravelLeg.Local local:
                    if (local.Origin != expectedOrigin)
                    {
                        throw new InvalidOperationException(
                            $"Travel plan leg begins at {local.Origin}, expected {expectedOrigin}.");
                    }

                    expectedOrigin = local.Destination;
                    break;
                case TravelLeg.Connector connector:
                    ValidateConnectorLeg(expectedOrigin, connector);
                    expectedOrigin = connector.Destination;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The current runtime cannot execute {travelLeg.GetType().Name}.");
            }
        }

        bool destinationSatisfied = DestinationSatisfied(
            expectedOrigin,
            destination);
        if (!destinationSatisfied)
        {
            throw new InvalidOperationException(
                $"Travel plan ends at {expectedOrigin}, which does not satisfy {destination}.");
        }
    }

    private void ValidateConnectorLeg(
        SystemPosition expectedOrigin,
        TravelLeg.Connector leg)
    {
        TransitConnection connection = _worldTopology.Connectors.GetConnection(
            leg.ConnectionId);
        ConnectorEndpoint source = _worldTopology.Connectors.GetEndpoint(
            connection.SourceEndpointId);
        ConnectorEndpoint destination = _worldTopology.Connectors.GetEndpoint(
            connection.DestinationEndpointId);
        if (leg.Origin != expectedOrigin
            || leg.Origin != source.Position
            || leg.Destination != destination.Position
            || leg.Duration != connection.Duration)
        {
            throw new InvalidOperationException(
                $"Connector leg {leg.ConnectionId} does not match authoritative topology.");
        }
    }

    private bool DestinationSatisfied(
        SystemPosition current,
        NavigationDestination destination) =>
        destination switch
        {
            NavigationDestination.Position position =>
                current == position.Value,
            NavigationDestination.System system =>
                current.SystemId == system.SystemId,
            NavigationDestination.Entity entity =>
                _lifecycle.Entities.GetShipId(entity.EntityId) is { } targetShipId
                && _movement.PositionAt(targetShipId, CurrentTime) == current,
            _ => false,
        };

    /// <summary>
    /// Applies the maneuver contract's terminal position and velocity
    /// tolerances after an analytic leg, while retaining exact legacy rules
    /// for system and entity destinations.
    /// </summary>
    private bool RuntimeDestinationSatisfied(
        ShipId shipId,
        SystemPosition current,
        NavigationDestination destination,
        ShipHeading? requestedHeading) =>
        destination switch
        {
            NavigationDestination.Position position =>
                _movement.KinematicStateAt(shipId, CurrentTime) is { } state
                && ManeuverArrival.EvaluateTerminal(
                    state,
                    position.Value,
                    requestedHeading).IsSatisfied,
            _ => DestinationSatisfied(current, destination)
                && (requestedHeading is null
                    || _movement.KinematicStateAt(shipId, CurrentTime) is { } state
                    && ManeuverArrival.EvaluateTerminal(
                        state,
                        current,
                        requestedHeading).IsSatisfied),
        };

    private CommandResult? RejectIneligible(
        ShipId shipId,
        CommandSource source) =>
        _control.CheckCommand(shipId, source) switch
        {
            ActorCommandEligibility.Eligible => null,
            ActorCommandEligibility.MissingActor => CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {shipId}."),
            ActorCommandEligibility.ActorOverridden => CommandResult.Rejected(
                CommandRejectionCodes.ActorOverridden,
                $"Ship {shipId} is under a temporary scripted override."),
            ActorCommandEligibility.IneligibleSource => CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                $"Command source {source.Kind}:{source.Id} does not control ship {shipId}."),
            _ => throw new InvalidOperationException("Unknown actor command eligibility."),
        };

    private static CommandResult? RejectInvalidOverride(
        ActorOverrideValidation validation,
        ShipId shipId) =>
        validation switch
        {
            ActorOverrideValidation.Valid => null,
            ActorOverrideValidation.MissingActor => CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {shipId}."),
            ActorOverrideValidation.InvalidSource => CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                $"Command source cannot change the override for ship {shipId}."),
            ActorOverrideValidation.Conflict => CommandResult.Rejected(
                CommandRejectionCodes.Conflict,
                $"Ship {shipId} has conflicting override state."),
            ActorOverrideValidation.StaleRevision => CommandResult.Rejected(
                CommandRejectionCodes.StaleControlRevision,
                $"Ship {shipId} control revision has changed."),
            _ => throw new InvalidOperationException("Unknown actor override validation."),
        };

    private sealed record MoveOrderProposal(
        ShipId ShipId,
        CommandSource Source,
        NavigationDestination Destination,
        OrderPlacement Placement,
        ShipHeading? RequestedHeading,
        TravelPlan? Plan);

    private sealed record MoveOrderEvaluation(
        MoveOrderProposal? Proposal,
        CommandResult? Rejection);

    private sealed record GroupMoveOrderEvaluation(
        IReadOnlyList<MoveOrderProposal>? Proposals,
        CommandResult? Rejection);

    private readonly record struct CancelOrderProposal(
        ShipId ShipId,
        ShipOrderId OrderId,
        bool WasActive);

    private sealed record CancelOrderEvaluation(
        CancelOrderProposal? Proposal,
        CommandResult? Rejection);

    private sealed record GroupCancelOrderEvaluation(
        IReadOnlyList<CancelOrderProposal>? Proposals,
        CommandResult? Rejection);

    private sealed record BeginOverrideProposal(
        ShipId ShipId,
        CommandSource Source,
        ActorOverrideReasonId Reason);

    private sealed record BeginOverrideEvaluation(
        BeginOverrideProposal? Proposal,
        CommandResult? Rejection);

    private sealed record EndOverrideProposal(
        ShipId ShipId,
        ScriptedOverrideReleasePolicy ReleasePolicy);

    private sealed record EndOverrideEvaluation(
        EndOverrideProposal? Proposal,
        CommandResult? Rejection);
}
