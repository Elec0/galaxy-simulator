using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable acceptance-observation state for the Phase 1 test fixture.
/// </summary>
public sealed record PhaseOneSnapshot(
    SimulationTime Time,
    IReadOnlyList<LocationSnapshot> Locations,
    IReadOnlyList<RouteSnapshot> Routes,
    IReadOnlyList<ShipSnapshot> Ships,
    IReadOnlyList<ConstructionSnapshot> Constructions);

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
    ConstructionDesignId DesignId,
    LocationId Location,
    TransportJobId? ActiveTransportJob,
    TransportJobStatus? TransportStatus,
    SimulationTime? ArrivesAt);

public sealed record ConstructionSnapshot(
    FacilityId FacilityId,
    ConstructionOrderId OrderId,
    ConstructionDesignId DesignId,
    string DesignName,
    ConstructionOrderStatus Status,
    SimulationTime? CompletesAt,
    IReadOnlyDictionary<MaterialId, Quantity> UnmetInputs);

internal static class SnapshotCollection
{
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    public static ReadOnlyDictionary<TKey, TValue> Copy<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> values)
        where TKey : notnull =>
        new(values.ToDictionary(pair => pair.Key, pair => pair.Value));
}
