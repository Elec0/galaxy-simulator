namespace GalaxyCommand.Simulation;

/// <summary>
/// Persistent rendering-independent boundary for one running game.
/// </summary>
public sealed class GameSession : IGameplayCommandHandler
{
    private readonly GameFactStore _facts;
    private readonly ActorOrderRuntimeCoordinator _runtime;
    private readonly GameplayCommandProcessor _commands;

    public GameSession(
        GameSessionSetup setup,
        ISpatialNavigationPlanner navigation)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _facts = new GameFactStore(setup.FactRetentionCapacity);
        _runtime = new ActorOrderRuntimeCoordinator(setup, navigation, _facts);
        _commands = new GameplayCommandProcessor(this, _facts);
    }

    public SimulationTime CurrentTime => _runtime.CurrentTime;

    public IReadOnlyList<GameEventRecord> EventRecords => _runtime.EventRecords;

    public IReadOnlyList<GameplayCommandRecord> CommandRecords => _commands.Records;

    public ShipId? ResolveShip(EntityId entityId) =>
        _runtime.ResolveShip(entityId);

    public EntityId? ResolveEntity(ShipId shipId) =>
        _runtime.ResolveEntity(shipId);

    /// <summary>
    /// Materializes one completed construction effect through the session
    /// lifecycle and records its semantic fact after successful commit.
    /// </summary>
    public ConstructionEntityMaterializationResult MaterializeConstruction(
        ConstructionProcess source,
        ConstructionMaterializationEffect effect) =>
        _runtime.MaterializeConstruction(source, effect);

    /// <summary>
    /// Materializes all pending construction effects in stable facility and
    /// order order, returning one result for each proposed effect.
    /// </summary>
    public IReadOnlyList<ConstructionEntityMaterializationResult>
        MaterializePendingConstruction(
            IEnumerable<ConstructionProcess> sources) =>
        _runtime.MaterializePendingConstruction(sources);

    /// <summary>
    /// Removes one live entity through deterministic cross-owner cleanup and
    /// returns the prior receipt when the same removal is repeated.
    /// </summary>
    public EntityRemovalResult RemoveEntity(EntityRemovalRequest request) =>
        _runtime.RemoveEntity(request);

    /// <summary>
    /// Commits one idempotent batch of directional standing effects and returns
    /// its prior result when the same batch is delivered again.
    /// </summary>
    public StandingChangeBatchResult CommitStandingChanges(
        StandingChangeBatch batch) =>
        _runtime.CommitStandingChanges(batch);

    /// <summary>
    /// Commits one idempotent batch of mutual diplomacy and explicit grant
    /// effects and returns its prior result when delivered again.
    /// </summary>
    public RelationshipPolicyChangeBatchResult CommitRelationshipPolicyChanges(
        RelationshipPolicyChangeBatch batch) =>
        _runtime.CommitRelationshipPolicyChanges(batch);

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

    public RunReport AdvanceTo(SimulationTime target) =>
        _runtime.AdvanceTo(target);

    public GameSnapshot CaptureSnapshot() =>
        _runtime.CaptureSnapshot();

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
        GameplayCommand command) =>
        _commands.Submit(CurrentTime, source, command);

    public GameFactReadResult ReadFactsAfter(
        GameFactSequence? sequence,
        int maximumCount) =>
        _facts.ReadAfter(sequence, maximumCount);

    GameplayCommandHandlingResult IGameplayCommandHandler.Handle(
        GameplayCommandEnvelope envelope) =>
        _runtime.Handle(envelope);
}
