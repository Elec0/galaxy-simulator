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

    public ConstructionEntityMaterializationResult MaterializeConstruction(
        ConstructionProcess source,
        ConstructionMaterializationEffect effect) =>
        _runtime.MaterializeConstruction(source, effect);

    public IReadOnlyList<ConstructionEntityMaterializationResult>
        MaterializePendingConstruction(
            IEnumerable<ConstructionProcess> sources) =>
        _runtime.MaterializePendingConstruction(sources);

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
