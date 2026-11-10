namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private EventKey? _economicWake;

    /// <summary>Only changes to retained economic queue or availability need an idle Decision wake.</summary>
    private static bool CommandNeedsEconomicWake(GameplayCommand command) => command is
        EnqueueStationShipCommand or SetStationModuleEnabledCommand
        or ControlStationProjectCommand { Action: StationProjectAction.Cancel };

    /// <summary>Reports exact new agenda demand without allocating, including reuse of an already admitted wake.</summary>
    private int EconomicWakeCreationCapacity()
    {
        if (_economy is null || _agenda.CurrentPhase is not null) return 0;
        var wake = new GameEvent.EconomicReevaluation();
        int pending = _agenda.CountPending(_agenda.CurrentTime, EventPhase.Decision, new(0), wake);
        if (_economicWake is { } retained && pending == 1
            && _agenda.CheckCancellation(retained, new(0), wake) == AgendaCancellationCheck.Matches) return 0;
        if (pending != 0 || _economicWake is not null)
            throw new InvalidOperationException("The economic owner's exact pending wake changed.");
        return 1;
    }

    /// <summary>Rejects exhausted wake identity capacity before the corresponding command or inventory owner changes.</summary>
    private bool CanRequestEconomicReevaluation()
    {
        try { _agenda.EnsureCreationCapacity(EconomicWakeCreationCapacity()); return true; }
        catch (OverflowException) { return false; }
    }

    /// <summary>Applies already admitted wake capacity before other follow-up agenda producers run.</summary>
    private void RequestEconomicReevaluation()
    {
        if (EconomicWakeCreationCapacity() == 0) return;
        _economicWake = _agenda.Schedule(_agenda.CurrentTime, EventPhase.Decision, new(0), new GameEvent.EconomicReevaluation());
    }
}
