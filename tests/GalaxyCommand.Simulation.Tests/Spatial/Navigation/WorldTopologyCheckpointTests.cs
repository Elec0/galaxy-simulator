using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class WorldTopologyCheckpointTests
{
    [Fact]
    public void RestorePreservesSystemsConnectorsAndPlanningBehavior()
    {
        WorldTopology topology = CreateTopology();

        CheckpointResult<WorldTopology> restoredResult =
            WorldTopology.RestoreCheckpoint(topology.CaptureCheckpoint());

        Assert.True(restoredResult.IsSuccess);
        WorldTopology restored = Assert.IsType<WorldTopology>(restoredResult.Value);
        Assert.Equal(
            topology.Systems.Select(system => (system.Id, system.Name)),
            restored.Systems.Select(system => (system.Id, system.Name)));
        Assert.Equal(topology.Connectors.Endpoints, restored.Connectors.Endpoints);
        Assert.Equal(topology.Connectors.Connections, restored.Connectors.Connections);
        var planner = new HierarchicalNavigationPlanner(
            restored.Connectors,
            new ZeroTravelTimeEstimator());
        var planned = Assert.IsType<NavigationPlanResult.Planned>(planner.Plan(
            new NavigationRequest(
                new ShipId(1),
                Position(1, 0),
                new NavigationDestination.System(new SystemId(3)),
                SimulationTime.Zero)));
        Assert.Equal(
            [new TransitConnectionId(1), new TransitConnectionId(2)],
            planned.Plan.Legs
                .OfType<TravelLeg.Connector>()
                .Select(leg => leg.ConnectionId));
    }

    [Fact]
    public void RestoreAcceptsUnorderedCollectionsAndCanonicalizesCapture()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        var unordered = new WorldTopologyCheckpoint(
            checkpoint.Systems.Reverse(),
            checkpoint.Endpoints.Reverse(),
            checkpoint.Connections.Reverse());

        WorldTopologyCheckpoint recaptured = Assert.IsType<WorldTopology>(
                WorldTopology.RestoreCheckpoint(unordered).Value)
            .CaptureCheckpoint();

        Assert.Equal([1UL, 2UL, 3UL], recaptured.Systems.Select(value => value!.Id.Value));
        Assert.Equal([1UL, 2UL, 3UL, 4UL], recaptured.Endpoints.Select(
            value => value!.Id.Value));
        Assert.Equal([1UL, 2UL], recaptured.Connections.Select(
            value => value!.Id.Value));
    }

    [Fact]
    public void RestoreRejectsDuplicateSystemIdentity()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        WorldSystemCheckpoint first = Assert.IsType<WorldSystemCheckpoint>(
            checkpoint.Systems[0]);
        var corrupt = Copy(
            checkpoint,
            systems: [first, first, .. checkpoint.Systems.Skip(2)]);

        CheckpointResult<WorldTopology> result = WorldTopology.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.topology.systems[1].id", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsEndpointInUnknownSystem()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        WorldConnectorEndpointCheckpoint[] endpoints = checkpoint.Endpoints
            .Select(value => Assert.IsType<WorldConnectorEndpointCheckpoint>(value))
            .ToArray();
        endpoints[0] = endpoints[0] with { SystemId = new SystemId(99) };
        var corrupt = Copy(checkpoint, endpoints: endpoints);

        CheckpointResult<WorldTopology> result = WorldTopology.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.topology.endpoints[0].systemId", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsDuplicateEndpointIdentity()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        WorldConnectorEndpointCheckpoint first =
            Assert.IsType<WorldConnectorEndpointCheckpoint>(checkpoint.Endpoints[0]);
        var corrupt = Copy(
            checkpoint,
            endpoints: [first, first, .. checkpoint.Endpoints.Skip(2)]);

        CheckpointResult<WorldTopology> result = WorldTopology.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.topology.endpoints[1].id", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsConnectionToUnknownEndpoint()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        WorldTransitConnectionCheckpoint[] connections = checkpoint.Connections
            .Select(value => Assert.IsType<WorldTransitConnectionCheckpoint>(value))
            .ToArray();
        connections[0] = connections[0] with
        {
            DestinationEndpointId = new ConnectorEndpointId(99),
        };
        var corrupt = Copy(checkpoint, connections: connections);

        CheckpointResult<WorldTopology> result = WorldTopology.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.topology.connections[0].destinationEndpointId",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsConnectionWithinOneSystem()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        WorldTransitConnectionCheckpoint[] connections = checkpoint.Connections
            .Select(value => Assert.IsType<WorldTransitConnectionCheckpoint>(value))
            .ToArray();
        connections[0] = connections[0] with
        {
            DestinationEndpointId = new ConnectorEndpointId(1),
        };
        var corrupt = Copy(checkpoint, connections: connections);

        CheckpointResult<WorldTopology> result = WorldTopology.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.topology.connections[0]", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsZeroDurationConnection()
    {
        WorldTopologyCheckpoint checkpoint = CreateTopology().CaptureCheckpoint();
        WorldTransitConnectionCheckpoint[] connections = checkpoint.Connections
            .Select(value => Assert.IsType<WorldTransitConnectionCheckpoint>(value))
            .ToArray();
        connections[0] = connections[0] with { Duration = SimulationDuration.Zero };
        var corrupt = Copy(checkpoint, connections: connections);

        CheckpointResult<WorldTopology> result = WorldTopology.RestoreCheckpoint(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.topology.connections[0].duration",
            result.Failure!.Path);
    }

    private static WorldTopology CreateTopology() => new(
        [
            new StarSystem(new SystemId(3), "Third"),
            new StarSystem(new SystemId(1), "First"),
            new StarSystem(new SystemId(2), "Second"),
        ],
        new ConnectorTopology(
            [
                new ConnectorEndpoint(new ConnectorEndpointId(4), Position(3, -10)),
                new ConnectorEndpoint(new ConnectorEndpointId(2), Position(2, -10)),
                new ConnectorEndpoint(new ConnectorEndpointId(3), Position(2, 10)),
                new ConnectorEndpoint(new ConnectorEndpointId(1), Position(1, 10)),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(2),
                    new ConnectorEndpointId(3),
                    new ConnectorEndpointId(4),
                    new SimulationDuration(20)),
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    new SimulationDuration(10)),
            ]));

    private static WorldTopologyCheckpoint Copy(
        WorldTopologyCheckpoint source,
        IEnumerable<WorldSystemCheckpoint?>? systems = null,
        IEnumerable<WorldConnectorEndpointCheckpoint?>? endpoints = null,
        IEnumerable<WorldTransitConnectionCheckpoint?>? connections = null) =>
        new(
            systems ?? source.Systems,
            endpoints ?? source.Endpoints,
            connections ?? source.Connections);

    private static SystemPosition Position(ulong systemId, long x) =>
        new(
            new SystemId(systemId),
            new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));

    private sealed class ZeroTravelTimeEstimator : ILocalTravelTimeEstimator
    {
        /// <inheritdoc />
        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination) => SimulationDuration.Zero;
    }
}
