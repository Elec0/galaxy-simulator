using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable, rendering-independent state for presentation clients.
/// </summary>
public sealed record PhaseOneSnapshot(
    SimulationTime Time,
    IReadOnlyList<LocationSnapshot> Locations,
    IReadOnlyList<RouteSnapshot> Routes,
    IReadOnlyList<ShipSnapshot> Ships);

public sealed record LocationSnapshot(
    LocationId Id,
    string Name);

public sealed record RouteSnapshot(
    RouteId Id,
    LocationId Origin,
    LocationId Destination,
    SimulationDuration Duration,
    bool IsEnabled);

public sealed record ShipSnapshot(
    ShipId Id,
    LocationId Location,
    TransportJobId? ActiveTransportJob,
    TransportJobStatus? TransportStatus,
    RouteId? CurrentRoute,
    SimulationTime? DepartedAt,
    SimulationTime? ArrivesAt);

internal static class SnapshotCollection
{
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}
