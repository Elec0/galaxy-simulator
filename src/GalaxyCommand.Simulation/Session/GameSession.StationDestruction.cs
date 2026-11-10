namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>Internal committed-cause handoff for destruction outcomes; combat resolution remains with its owning domain.</summary>
    internal StationDestructionResult CommitStationDestruction(StationDestructionRequest request)
    {
        EnsureHealthy();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Trigger is CargoDispositionTrigger.Command command)
        {
            IdSequenceCheckpoint sequence = _commands.CaptureCheckpoint().Sequences;
            if (command.Sequence.Value == 0 || sequence.NextValue is { } next && command.Sequence.Value >= next)
                return new(false, StationDestructionFailure.InvalidInput);
        }
        try { return _runtime.CommitStationDestruction(request); }
        finally { CompletePlayerDisclosureIfIdle(); }
    }
}
