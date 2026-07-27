using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable rendering-independent view of one running game.
/// </summary>
public sealed record GameSnapshot(
    SimulationTime Time,
    IReadOnlyList<GameSystemSnapshot> Systems,
    IReadOnlyList<GameShipSnapshot> Ships);

public sealed record GameSystemSnapshot(
    SystemId Id,
    string Name);

public sealed record GameShipSnapshot(
    ShipId Id,
    SystemPosition Position,
    LocalMotionSnapshot? Motion,
    ShipOrderSnapshot? CurrentOrder);

internal static class GameSnapshotCollection
{
    internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
        new(values.ToArray());
}
