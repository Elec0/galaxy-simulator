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

    public RunReport AdvanceTo(SimulationTime target) =>
        _runtime.AdvanceTo(target);

    public GameSnapshot CaptureSnapshot() =>
        _runtime.CaptureSnapshot();

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
