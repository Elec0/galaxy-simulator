namespace GalaxyCommand.Simulation;

/// <summary>
/// Persistent rendering-independent boundary for one running game.
/// </summary>
public sealed class GameSession : IGameplayCommandHandler
{
    private readonly GameFactStore _facts;
    private readonly ActorOrderRuntimeCoordinator _runtime;
    private readonly DeterministicRandomOwner _random;
    private readonly GameplayCommandProcessor _commands;

    /// <summary>
    /// Creates one clean authoritative session from validated setup, including
    /// the setup's required deterministic random root.
    /// </summary>
    public GameSession(
        GameSessionSetup setup,
        ISpatialNavigationPlanner navigation)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _facts = new GameFactStore(setup.FactRetentionCapacity);
        _runtime = new ActorOrderRuntimeCoordinator(setup, navigation, _facts);
        _random = new DeterministicRandomOwner(setup.RandomRootSeed);
        _commands = new GameplayCommandProcessor(this, _facts);
    }

    /// <summary>
    /// Publishes owners that were restored and validated independently, then
    /// restores command admission against the assembled session boundary.
    /// </summary>
    private GameSession(
        GameFactStore facts,
        ActorOrderRuntimeCoordinator runtime,
        DeterministicRandomOwner random,
        CommandAdmissionCheckpoint commandAdmission)
    {
        _facts = facts;
        _runtime = runtime;
        _random = random;
        CheckpointResult<GameplayCommandProcessor> commands =
            GameplayCommandProcessor.RestoreCheckpoint(
                commandAdmission,
                this,
                facts);
        if (!commands.IsSuccess)
        {
            throw new InvalidOperationException(commands.Failure!.Message);
        }

        _commands = commands.Value!;
    }

    public SimulationTime CurrentTime => _runtime.CurrentTime;

    public IReadOnlyList<GameEventRecord> EventRecords => _runtime.EventRecords;

    public IReadOnlyList<GameplayCommandRecord> CommandRecords => _commands.Records;

    /// <summary>
    /// Reports whether the session remains safe for authoritative mutation and
    /// checkpoint capture.
    /// </summary>
    public bool IsHealthy => _runtime.IsHealthy;

    public ShipId? ResolveShip(EntityId entityId) =>
        _runtime.ResolveShip(entityId);

    public EntityId? ResolveEntity(ShipId shipId) =>
        _runtime.ResolveEntity(shipId);

    internal InventoryCommitBatchResult CommitInventoryMutations(
        IEnumerable<InventoryMutationProposal> proposals)
    {
        EnsureHealthy();
        return _runtime.CommitInventoryMutations(proposals);
    }

    /// <summary>
    /// Removes one live entity through deterministic cross-owner cleanup and
    /// returns the prior receipt when the same removal is repeated.
    /// </summary>
    public EntityRemovalResult RemoveEntity(EntityRemovalRequest request)
    {
        EnsureHealthy();
        return _runtime.RemoveEntity(request);
    }

    /// <summary>
    /// Commits one idempotent batch of directional standing effects and returns
    /// its prior result when the same batch is delivered again.
    /// </summary>
    public StandingChangeBatchResult CommitStandingChanges(
        StandingChangeBatch batch)
    {
        EnsureHealthy();
        return _runtime.CommitStandingChanges(batch);
    }

    /// <summary>
    /// Commits one idempotent batch of mutual diplomacy and explicit grant
    /// effects and returns its prior result when delivered again.
    /// </summary>
    public RelationshipPolicyChangeBatchResult CommitRelationshipPolicyChanges(
        RelationshipPolicyChangeBatch batch)
    {
        EnsureHealthy();
        return _runtime.CommitRelationshipPolicyChanges(batch);
    }

    /// <summary>
    /// Returns the current mutual diplomatic condition for two principals.
    /// </summary>
    public DiplomaticCondition GetDiplomaticCondition(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId) =>
        _runtime.GetDiplomaticCondition(firstPrincipalId, secondPrincipalId);

    /// <summary>
    /// Reports whether an issued grant of the requested kind is currently
    /// effective for the directional principal pair.
    /// </summary>
    public bool HasEffectiveRelationshipGrant(
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind) =>
        _runtime.HasEffectiveRelationshipGrant(
            issuerPrincipalId,
            holderPrincipalId,
            kind);

    public RunReport AdvanceTo(SimulationTime target)
    {
        EnsureHealthy();
        return _runtime.AdvanceTo(target);
    }

    public GameSnapshot CaptureSnapshot()
    {
        EnsureHealthy();
        return _runtime.CaptureSnapshot();
    }

    /// <summary>
    /// Captures a presentation-safe world and observer-scoped relationship and
    /// fact projections after the current authoritative commit boundary.
    /// </summary>
    public GamePresentationSnapshot CapturePresentation(
        GamePresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GameSnapshot world = CaptureSnapshot();
        GameFactReadResult facts = ReadFactsAfter(
            request.FactCursor,
            request.MaximumFactCount);
        return GamePresentationSnapshotFactory.Create(world, request, facts);
    }

    public GameplayCommandRecord SubmitCommand(
        CommandSource source,
        GameplayCommand command)
    {
        EnsureHealthy();
        return _commands.Submit(CurrentTime, source, command);
    }

    public GameFactReadResult ReadFactsAfter(
        GameFactSequence? sequence,
        int maximumCount) =>
        _facts.ReadAfter(sequence, maximumCount);

    /// <summary>
    /// Captures one complete authoritative checkpoint at the current completed
    /// timestamp, or returns a typed failure without mutating the session.
    /// </summary>
    internal CheckpointResult<GameSessionCheckpoint> CaptureCheckpoint()
    {
        GameFactStoreCheckpoint facts = _facts.CaptureCheckpoint();
        CheckpointResult<GameSessionRuntimeCheckpoint> runtime =
            _runtime.CaptureCheckpoint(facts.Capacity);
        if (!runtime.IsSuccess)
        {
            return CheckpointResult<GameSessionCheckpoint>.Rejected(runtime.Failure!);
        }

        GameSessionRuntimeCheckpoint value = runtime.Value!;
        var checkpoint = new GameSessionCheckpoint(
            value.Engine,
            value.RuntimePolicies,
            value.WorldTopology,
            value.Movement,
            value.Control,
            value.Orders,
            value.Lifecycle,
            value.Relationships,
            value.Economy,
            _random.CaptureCheckpoint(),
            facts,
            _commands.CaptureCheckpoint(),
            value.InventoryCommit);
        return CheckpointResult<GameSessionCheckpoint>.Success(checkpoint);
    }

    /// <summary>
    /// Validates and assembles every owner in isolation, publishing a session
    /// only after all owner and cross-owner invariants succeed.
    /// </summary>
    internal static CheckpointResult<GameSession> RestoreCheckpoint(
        GameSessionCheckpoint checkpoint) =>
        RestoreCheckpointCore(checkpoint, definitions: null);

    internal static CheckpointResult<GameSession> RestoreCheckpoint(
        GameSessionCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return RestoreCheckpointCore(checkpoint, definitions);
    }

    internal static CheckpointResult<GameSession> RestoreCheckpoint(
        GameSessionCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        MaterialInventoryCompatibilityMap materialCompatibility)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(materialCompatibility);
        return RestoreCheckpointCore(
            checkpoint,
            definitions,
            materialCompatibility);
    }

    private static CheckpointResult<GameSession> RestoreCheckpointCore(
        GameSessionCheckpoint checkpoint,
        PhysicalDefinitionCatalog? definitions,
        MaterialInventoryCompatibilityMap? materialCompatibility = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Random is null)
        {
            return CheckpointResult<GameSession>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.random",
                    "The deterministic random checkpoint is required."));
        }

        // No current gameplay domain declares a stateful stream. Future owners
        // contribute their complete live-key sets at this aggregate boundary.
        CheckpointResult<DeterministicRandomOwner> random =
            DeterministicRandomOwner.RestoreCheckpoint(
                checkpoint.Random,
                new HashSet<RandomStreamKey>());
        if (!random.IsSuccess)
        {
            return CheckpointResult<GameSession>.Rejected(random.Failure!);
        }

        CheckpointResult<GameFactStore> facts =
            GameFactStore.RestoreCheckpoint(checkpoint.Facts);
        if (!facts.IsSuccess)
        {
            return CheckpointResult<GameSession>.Rejected(facts.Failure!);
        }

        if (checkpoint.Facts.Capacity
            != checkpoint.RuntimePolicies.FactRetentionCapacity)
        {
            return CheckpointResult<GameSession>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.facts.capacity",
                    "Fact capacity disagrees with the runtime policy manifest."));
        }

        var runtimeCheckpoint = new GameSessionRuntimeCheckpoint(
            checkpoint.Engine,
            checkpoint.RuntimePolicies,
            checkpoint.WorldTopology,
            checkpoint.Movement,
            checkpoint.Control,
            checkpoint.Orders,
            checkpoint.Lifecycle,
            checkpoint.Relationships,
            checkpoint.Economy,
            checkpoint.InventoryCommit);
        CheckpointResult<ActorOrderRuntimeCoordinator> runtime = definitions is null
            ? ActorOrderRuntimeCoordinator.RestoreCheckpoint(
                runtimeCheckpoint,
                facts.Value!)
            : materialCompatibility is not null
                ? ActorOrderRuntimeCoordinator.RestoreCheckpoint(
                    runtimeCheckpoint,
                    facts.Value!,
                    definitions,
                    materialCompatibility)
            : ActorOrderRuntimeCoordinator.RestoreCheckpoint(
                runtimeCheckpoint,
                facts.Value!,
                definitions);
        if (!runtime.IsSuccess)
        {
            return CheckpointResult<GameSession>.Rejected(runtime.Failure!);
        }

        try
        {
            return CheckpointResult<GameSession>.Success(
                new GameSession(
                    facts.Value!,
                    runtime.Value!,
                    random.Value!,
                    checkpoint.CommandAdmission));
        }
        catch (InvalidOperationException error)
        {
            return CheckpointResult<GameSession>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.commandAdmission",
                    error.Message));
        }
    }

    GameplayCommandHandlingResult IGameplayCommandHandler.Handle(
        GameplayCommandEnvelope envelope)
    {
        EnsureHealthy();
        return _runtime.Handle(envelope);
    }

    /// <summary>
    /// Prevents authoritative operations after a failed prepared commit.
    /// </summary>
    private void EnsureHealthy() => _runtime.ThrowIfUnhealthy();
}
