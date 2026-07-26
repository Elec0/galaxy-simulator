namespace GalaxyCommand.Simulation;

/// <summary>
/// Persistent rendering-independent boundary for one running game.
/// </summary>
public sealed class GameSession : IGameplayCommandHandler
{
    private readonly PhaseOneRuntime _runtime;
    private readonly GameplayCommandProcessor _commands;

    public GameSession(PhaseOneConfig? initialConfig = null)
    {
        _runtime = new PhaseOneRuntime(
            initialConfig,
            stopWhenFirstShipConstructed: false);
        _commands = new GameplayCommandProcessor(this);
    }

    public SimulationTime CurrentTime => _runtime.CurrentTime;

    public IReadOnlyList<ScenarioEventRecord> EventRecords => _runtime.EventRecords;

    public IReadOnlyList<DecisionRecord> DecisionRecords => _runtime.DecisionRecords;

    public IReadOnlyList<GameplayCommandRecord> CommandRecords => _commands.Records;

    public RunReport AdvanceTo(SimulationTime target) =>
        _runtime.AdvanceTo(target);

    public PhaseOneSnapshot CaptureSnapshot() =>
        _runtime.CaptureSnapshot();

    public GameplayCommandRecord SubmitCommand(
        CommandSource source,
        GameplayCommand command) =>
        _commands.Submit(CurrentTime, source, command);

    CommandResult IGameplayCommandHandler.Handle(GameplayCommandEnvelope envelope) =>
        CommandResult.Rejected(
            CommandRejectionCodes.UnsupportedCommand,
            $"Gameplay command '{envelope.Command.Kind}' is not supported yet.");
}
