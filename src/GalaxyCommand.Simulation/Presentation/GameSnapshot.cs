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
    RelationshipSnapshot Relationships,
    IReadOnlyList<GameShipSnapshot> Ships)
{
    /// <summary>Gets complete authoritative stationary sensor state for diagnostic consumers.</summary>
    public IReadOnlyList<DeployedSensorSnapshot> DeployedSensors { get; init; } = [];
}

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
    PrincipalId PrincipalId,
    ConstructionDesignId DesignId,
    InventoryId CargoInventoryId,
    Quantity CargoCapacity,
    ShipManeuverCapabilityRevision ManeuverCapabilityRevision,
    ShipSpatialSnapshotState SpatialState,
    ShipVelocity Velocity,
    ShipHeading Heading,
    ActorControlSnapshot Control,
    ShipOrderSnapshot? CurrentOrder,
    IReadOnlyList<ShipOrderSnapshot> QueuedOrders,
    IReadOnlyList<ShipOrderSnapshot> SuspendedOrders)
{
    public GameShipSnapshot(
        EntityId entityId,
        ShipId id,
        PrincipalId principalId,
        ConstructionDesignId designId,
        InventoryId cargoInventoryId,
        Quantity cargoCapacity,
        ShipSpatialSnapshotState spatialState,
        ActorControlSnapshot control,
        ShipOrderSnapshot? currentOrder,
        IReadOnlyList<ShipOrderSnapshot> queuedOrders,
        IReadOnlyList<ShipOrderSnapshot> suspendedOrders)
        : this(
            entityId,
            id,
            principalId,
            designId,
            cargoInventoryId,
            cargoCapacity,
            ShipManeuverCapabilityRevision.Initial,
            spatialState,
            ShipVelocity.Zero,
            ShipHeading.Zero,
            control,
            currentOrder,
            queuedOrders,
            suspendedOrders)
    {
    }

    /// <summary>The captured system-local position; connector transit has no local position.</summary>
    public SystemPosition? Position =>
        SpatialState switch
        {
            ShipSpatialSnapshotState.AtPosition atPosition =>
                atPosition.Position,
            ShipSpatialSnapshotState.LocalMotion localMotion =>
                localMotion.CurrentPosition,
            ShipSpatialSnapshotState.AnalyticManeuver maneuver =>
                maneuver.CurrentState.Position,
            ShipSpatialSnapshotState.InteractionControlled controlled =>
                controlled.CurrentState.Position,
            ShipSpatialSnapshotState.ConnectorTransit => null,
            _ => throw new InvalidOperationException(
                $"Unsupported spatial snapshot state {SpatialState.GetType().Name}."),
        };

    public LocalMotionSnapshot? Motion =>
        (SpatialState as ShipSpatialSnapshotState.LocalMotion)?.Motion;

    public TerminalManeuverSnapshot? Maneuver =>
        (SpatialState as ShipSpatialSnapshotState.AnalyticManeuver)?.Maneuver;

    public ConnectorTransitSnapshot? Transit =>
        (SpatialState as ShipSpatialSnapshotState.ConnectorTransit)?.Transit;
}

internal static class GameSnapshotCollection
{
    internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
        new(values.ToArray());
}
