namespace GalaxyCommand.Simulation;

/// <summary>
/// Persistent rendering-independent boundary for one running game.
/// </summary>
public sealed class GameSession : IGameplayCommandHandler
{
    private readonly GameRuntime _runtime;
    private readonly GameplayCommandProcessor _commands;

    public GameSession(
        GameSessionSetup setup,
        ISpatialNavigationPlanner navigation)
    {
        _runtime = new GameRuntime(setup, navigation);
        _commands = new GameplayCommandProcessor(this);
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

    CommandResult IGameplayCommandHandler.Handle(GameplayCommandEnvelope envelope) =>
        _runtime.Handle(envelope);
}
