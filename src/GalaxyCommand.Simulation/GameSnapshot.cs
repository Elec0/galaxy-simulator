using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable rendering-independent view of one running game.
/// </summary>
public sealed record GameSnapshot(
    SimulationTime Time,
    IReadOnlyList<GameSystemSnapshot> Systems,
    IReadOnlyList<ConnectorEndpointSnapshot> ConnectorEndpoints,
    IReadOnlyList<TransitConnectionSnapshot> TransitConnections,
    IReadOnlyList<GameShipSnapshot> Ships);

public sealed record GameSystemSnapshot(
    SystemId Id,
    string Name);

public sealed record ConnectorEndpointSnapshot(
    ConnectorEndpointId Id,
    SystemPosition Position);

public sealed record TransitConnectionSnapshot(
    TransitConnectionId Id,
    ConnectorEndpointId SourceEndpointId,
    ConnectorEndpointId DestinationEndpointId,
    SimulationDuration Duration);

public sealed record GameShipSnapshot(
    EntityId EntityId,
    ShipId Id,
    OrganizationId OrganizationId,
    ConstructionDesignId DesignId,
    InventoryId CargoInventoryId,
    Quantity CargoCapacity,
    ShipSpatialSnapshotState SpatialState,
    ActorControlSnapshot Control,
    ShipOrderSnapshot? CurrentOrder,
    IReadOnlyList<ShipOrderSnapshot> QueuedOrders,
    IReadOnlyList<ShipOrderSnapshot> SuspendedOrders)
{
    public SystemPosition? Position =>
        SpatialState switch
        {
            ShipSpatialSnapshotState.AtPosition atPosition =>
                atPosition.Position,
            ShipSpatialSnapshotState.LocalMotion localMotion =>
                localMotion.CurrentPosition,
            ShipSpatialSnapshotState.ConnectorTransit => null,
            _ => throw new InvalidOperationException(
                $"Unsupported spatial snapshot state {SpatialState.GetType().Name}."),
        };

    public LocalMotionSnapshot? Motion =>
        (SpatialState as ShipSpatialSnapshotState.LocalMotion)?.Motion;

    public ConnectorTransitSnapshot? Transit =>
        (SpatialState as ShipSpatialSnapshotState.ConnectorTransit)?.Transit;
}

internal static class GameSnapshotCollection
{
    internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
        new(values.ToArray());
}
