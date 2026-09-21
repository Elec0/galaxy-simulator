using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record WorldSystemCheckpoint(
    SystemId Id,
    string? Name);

internal sealed record WorldConnectorEndpointCheckpoint(
    ConnectorEndpointId Id,
    SystemId SystemId,
    SpatialCoordinate X,
    SpatialCoordinate Y);

internal sealed record WorldTransitConnectionCheckpoint(
    TransitConnectionId Id,
    ConnectorEndpointId SourceEndpointId,
    ConnectorEndpointId DestinationEndpointId,
    SimulationDuration Duration);

internal sealed class WorldTopologyCheckpoint
{
    internal WorldTopologyCheckpoint(
        IEnumerable<WorldSystemCheckpoint?> systems,
        IEnumerable<WorldConnectorEndpointCheckpoint?> endpoints,
        IEnumerable<WorldTransitConnectionCheckpoint?> connections)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(connections);
        Systems = new ReadOnlyCollection<WorldSystemCheckpoint?>(systems.ToArray());
        Endpoints = new ReadOnlyCollection<WorldConnectorEndpointCheckpoint?>(
            endpoints.ToArray());
        Connections = new ReadOnlyCollection<WorldTransitConnectionCheckpoint?>(
            connections.ToArray());
    }

    internal ReadOnlyCollection<WorldSystemCheckpoint?> Systems { get; }

    internal ReadOnlyCollection<WorldConnectorEndpointCheckpoint?> Endpoints { get; }

    internal ReadOnlyCollection<WorldTransitConnectionCheckpoint?> Connections { get; }
}

internal sealed record NavigationPolicyCheckpoint(
    string? Kind,
    int BehaviorVersion);

internal sealed record TravelTimePolicyCheckpoint(
    string? Kind,
    int BehaviorVersion,
    ulong MillisecondsPerMapUnit);

