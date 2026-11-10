namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>Returns covered objects carrying the selected original field handle, without expanding private aliases.</summary>
    public IReadOnlyList<CargoObjectView> CaptureVisibleCargoField(DebrisFieldId originalFieldId)
    {
        EnsureHealthy();
        ArgumentOutOfRangeException.ThrowIfZero(originalFieldId.Value);
        return _runtime.CaptureCoveredCargoField(_runtime.PlayerObserverPrincipalId, originalFieldId);
    }

    /// <summary>Inspects only the player's active pickup interactions, including locally reachable targets outside coverage.</summary>
    public IReadOnlyList<CargoCollectionProgressView> CaptureCargoCollectionProgress()
    {
        EnsureHealthy();
        return _runtime.CaptureCargoCollectionProgress(_runtime.PlayerObserverPrincipalId);
    }

    /// <summary>
    /// Captures only cargo currently covered by the registered player's sensor sources.
    /// Hidden objects have no stale map projection, including during local pickup inspection.
    /// </summary>
    public IReadOnlyList<CargoObjectView> CaptureVisibleCargo()
    {
        EnsureHealthy();
        return _runtime.CaptureCoveredCargo(_runtime.PlayerObserverPrincipalId);
    }
}
