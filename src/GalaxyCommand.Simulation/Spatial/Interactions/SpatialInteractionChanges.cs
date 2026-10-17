namespace GalaxyCommand.Simulation;

/// <summary>Derived movement notification; a null revision records actor removal.</summary>
public sealed record SpatialInteractionChange(ShipId ShipId, SpatialRevision? Revision);

/// <summary>Opt-in movement-owner buffer for transition-triggered interaction evaluation.</summary>
public sealed class SpatialInteractionChanges : IDisposable
{
    private readonly Dictionary<ShipId, SpatialInteractionChange> _changes = [];
    private Action<SpatialInteractionChanges>? _detach;

    internal SpatialInteractionChanges(Action<SpatialInteractionChanges> detach) => _detach = detach;

    /// <summary>Captures the latest transition per ship in deterministic identity order.</summary>
    public IReadOnlyList<SpatialInteractionChange> Capture() =>
        Array.AsReadOnly(_changes.Values.OrderBy(change => change.ShipId.Value).ToArray());

    /// <summary>Releases captured notices only, preserving transitions committed after capture.</summary>
    public void Acknowledge(IEnumerable<SpatialInteractionChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        SpatialInteractionChange[] captured = changes.ToArray();
        foreach (SpatialInteractionChange change in captured)
            ArgumentNullException.ThrowIfNull(change);
        foreach (SpatialInteractionChange change in captured)
        {
            if (_changes.TryGetValue(change.ShipId, out SpatialInteractionChange? current) &&
                ReferenceEquals(current, change))
                _changes.Remove(change.ShipId);
        }
    }

    /// <summary>Detaches this buffer and releases its derived notices. Repeated disposal is harmless.</summary>
    public void Dispose()
    {
        _detach?.Invoke(this);
        _detach = null;
        _changes.Clear();
    }

    internal void Record(ShipId shipId, SpatialRevision? revision) =>
        _changes[shipId] = new SpatialInteractionChange(shipId, revision);
}
