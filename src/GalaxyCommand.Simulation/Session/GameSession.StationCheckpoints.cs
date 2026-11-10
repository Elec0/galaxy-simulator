namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>Joins station receipts to aggregate command admission and the session's exact player attribution.</summary>
    private static CheckpointValidationFailure? ValidateStationAdmission(GameSessionCheckpoint checkpoint)
    {
        if (checkpoint.Stations is not { } stations)
            return checkpoint.Engine.Agenda.PendingEvents.Any(item => item.Payload is GameEvent.StationWork)
                ? new("$.checkpoint.stations", "Station work has no saved station authority.") : null;
        if (checkpoint.PlayerObservation is not { } observer || !stations.Setup.CommandAuthorities.Any(authority =>
            authority.Source == observer.PlayerCommandSource && authority.Principal == observer.ObserverPrincipalId))
            return new("$.checkpoint.stations.setup.commandAuthorities", "Station authority must retain the exact session player binding.");
        foreach (StationCommandReceiptCheckpoint receipt in stations.Receipts)
            if (receipt.Request.Sequence.Value == 0
                || checkpoint.CommandAdmission.Sequences.NextValue is { } next && receipt.Request.Sequence.Value >= next
                || receipt.Request.SubmittedAt > checkpoint.Engine.Agenda.CurrentTime)
                return new("$.checkpoint.stations.receipts", "Station receipt command identity was not admitted by this session.");
        foreach (StationDestructionReceipt receipt in stations.Destructions)
            if (receipt.Request.Trigger is CargoDispositionTrigger.Command command
                && (command.Sequence.Value == 0 || checkpoint.CommandAdmission.Sequences.NextValue is { } next
                    && command.Sequence.Value >= next))
                return new("$.checkpoint.stations.destructions", "Station destruction command identity was not admitted by this session.");
        return null;
    }
}
