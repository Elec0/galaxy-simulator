using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public sealed record StarSystem
{
    public StarSystem(SystemId id, string name)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    public SystemId Id { get; }

    public string Name { get; }
}

/// <summary>
/// One physical entry or emergence point in a system-local coordinate space.
/// </summary>
public sealed record ConnectorEndpoint
{
    public ConnectorEndpoint(
        ConnectorEndpointId id,
        SystemPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        Id = id;
        Position = position;
    }

    public ConnectorEndpointId Id { get; }

    public SystemPosition Position { get; }
}

/// <summary>
/// One immutable, enabled, universally accessible directional connection.
/// Bidirectional travel is represented by two records.
/// </summary>
public sealed record TransitConnection
{
    public TransitConnection(
        TransitConnectionId id,
        ConnectorEndpointId sourceEndpointId,
        ConnectorEndpointId destinationEndpointId,
        SimulationDuration duration)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(sourceEndpointId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(destinationEndpointId.Value);
        if (duration == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Connector transit must have a positive duration.");
        }

        Id = id;
        SourceEndpointId = sourceEndpointId;
        DestinationEndpointId = destinationEndpointId;
        Duration = duration;
    }

    public TransitConnectionId Id { get; }

    public ConnectorEndpointId SourceEndpointId { get; }

    public ConnectorEndpointId DestinationEndpointId { get; }

    public SimulationDuration Duration { get; }
}

/// <summary>
/// Immutable connector topology shared by setup, planning, and execution.
/// </summary>
public sealed class ConnectorTopology
{
    private readonly IReadOnlyDictionary<ConnectorEndpointId, ConnectorEndpoint> _endpointById;
    private readonly IReadOnlyDictionary<TransitConnectionId, TransitConnection> _connectionById;
    private readonly IReadOnlyDictionary<SystemId, IReadOnlyList<TransitConnection>> _outgoingBySystem;

    public ConnectorTopology(
        IEnumerable<ConnectorEndpoint> endpoints,
        IEnumerable<TransitConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(connections);

        ConnectorEndpoint[] endpointValues = endpoints.ToArray();
        TransitConnection[] connectionValues = connections.ToArray();
        var endpointById = new Dictionary<ConnectorEndpointId, ConnectorEndpoint>();
        foreach (ConnectorEndpoint endpoint in endpointValues)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpointById.TryAdd(endpoint.Id, endpoint))
            {
                throw new ArgumentException(
                    $"Duplicate connector endpoint {endpoint.Id}.",
                nameof(endpoints));
            }
        }

        Array.Sort(
            endpointValues,
            (left, right) => left.Id.Value.CompareTo(right.Id.Value));

        var connectionById = new Dictionary<TransitConnectionId, TransitConnection>();
        var outgoing = new Dictionary<SystemId, List<TransitConnection>>();
        foreach (TransitConnection connection in connectionValues)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (!connectionById.TryAdd(connection.Id, connection))
            {
                throw new ArgumentException(
                    $"Duplicate transit connection {connection.Id}.",
                    nameof(connections));
            }

            if (!endpointById.TryGetValue(
                    connection.SourceEndpointId,
                    out ConnectorEndpoint? source))
            {
                throw new ArgumentException(
                    $"Transit connection {connection.Id} references unknown source endpoint {connection.SourceEndpointId}.",
                    nameof(connections));
            }

            if (!endpointById.TryGetValue(
                    connection.DestinationEndpointId,
                    out ConnectorEndpoint? destination))
            {
                throw new ArgumentException(
                    $"Transit connection {connection.Id} references unknown destination endpoint {connection.DestinationEndpointId}.",
                    nameof(connections));
            }

            if (source.Position.SystemId == destination.Position.SystemId)
            {
                throw new ArgumentException(
                    $"Transit connection {connection.Id} must join distinct systems.",
                    nameof(connections));
            }

            if (!outgoing.TryGetValue(source.Position.SystemId, out List<TransitConnection>? values))
            {
                values = [];
                outgoing.Add(source.Position.SystemId, values);
            }

            values.Add(connection);
        }

        Array.Sort(
            connectionValues,
            (left, right) => left.Id.Value.CompareTo(right.Id.Value));

        Endpoints = new ReadOnlyCollection<ConnectorEndpoint>(endpointValues);
        Connections = new ReadOnlyCollection<TransitConnection>(connectionValues);
        _endpointById = new ReadOnlyDictionary<ConnectorEndpointId, ConnectorEndpoint>(
            endpointById);
        _connectionById = new ReadOnlyDictionary<TransitConnectionId, TransitConnection>(
            connectionById);
        _outgoingBySystem = new ReadOnlyDictionary<SystemId, IReadOnlyList<TransitConnection>>(
            outgoing.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TransitConnection>)
                    new ReadOnlyCollection<TransitConnection>(
                        pair.Value.OrderBy(connection => connection.Id.Value).ToArray())));
    }

    public IReadOnlyList<ConnectorEndpoint> Endpoints { get; }

    public IReadOnlyList<TransitConnection> Connections { get; }

    public ConnectorEndpoint GetEndpoint(ConnectorEndpointId id) =>
        _endpointById.GetValueOrDefault(id)
        ?? throw new KeyNotFoundException($"Unknown connector endpoint {id}.");

    public TransitConnection GetConnection(TransitConnectionId id) =>
        _connectionById.GetValueOrDefault(id)
        ?? throw new KeyNotFoundException($"Unknown transit connection {id}.");

    public IReadOnlyList<TransitConnection> OutgoingFrom(SystemId systemId) =>
        _outgoingBySystem.GetValueOrDefault(systemId)
        ?? Array.Empty<TransitConnection>();
}

/// <summary>
/// Immutable authoritative ownership boundary for named systems and their
/// directional connector topology.
/// </summary>
internal sealed class WorldTopology
{
    private readonly ReadOnlyCollection<StarSystem> _systems;

    /// <summary>
    /// Copies and validates a complete topology, including every endpoint's
    /// reference to a registered system.
    /// </summary>
    internal WorldTopology(
        IEnumerable<StarSystem> systems,
        ConnectorTopology connectors)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(connectors);
        StarSystem[] systemValues = systems.ToArray();
        var systemIds = new HashSet<SystemId>();
        foreach (StarSystem system in systemValues)
        {
            ArgumentNullException.ThrowIfNull(system);
            if (!systemIds.Add(system.Id))
            {
                throw new ArgumentException(
                    $"Duplicate system {system.Id}.",
                    nameof(systems));
            }
        }

        foreach (ConnectorEndpoint endpoint in connectors.Endpoints)
        {
            if (!systemIds.Contains(endpoint.Position.SystemId))
            {
                throw new ArgumentException(
                    $"Connector endpoint {endpoint.Id} references unknown system "
                    + $"{endpoint.Position.SystemId}.",
                    nameof(connectors));
            }
        }

        Array.Sort(systemValues, (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        _systems = new ReadOnlyCollection<StarSystem>(systemValues);
        Connectors = connectors;
    }

    internal IReadOnlyList<StarSystem> Systems => _systems;

    internal ConnectorTopology Connectors { get; }

    /// <summary>
    /// Captures immutable system, endpoint, and directional connection values
    /// in stable identity order.
    /// </summary>
    internal WorldTopologyCheckpoint CaptureCheckpoint() =>
        new(
            _systems.Select(system => new WorldSystemCheckpoint(
                system.Id,
                system.Name)),
            Connectors.Endpoints.Select(endpoint =>
                new WorldConnectorEndpointCheckpoint(
                    endpoint.Id,
                    endpoint.Position.SystemId,
                    endpoint.Position.Position.X,
                    endpoint.Position.Position.Y)),
            Connectors.Connections.Select(connection =>
                new WorldTransitConnectionCheckpoint(
                    connection.Id,
                    connection.SourceEndpointId,
                    connection.DestinationEndpointId,
                    connection.Duration)));

    /// <summary>
    /// Validates raw externally editable topology values and constructs an
    /// isolated immutable topology without resolving a replacement catalog.
    /// </summary>
    internal static CheckpointResult<WorldTopology> RestoreCheckpoint(
        WorldTopologyCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        const string systemsPath = "$.checkpoint.topology.systems";
        var systems = new List<StarSystem>(checkpoint.Systems.Count);
        var systemIds = new HashSet<SystemId>();
        for (int index = 0; index < checkpoint.Systems.Count; index++)
        {
            WorldSystemCheckpoint? system = checkpoint.Systems[index];
            if (system is null)
            {
                return Rejected($"{systemsPath}[{index}]", "A system entry is required.");
            }

            if (system.Id.Value == 0 || !systemIds.Add(system.Id))
            {
                return Rejected(
                    $"{systemsPath}[{index}].id",
                    "The system identity is zero or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(system.Name))
            {
                return Rejected(
                    $"{systemsPath}[{index}].name",
                    "The system name is required.");
            }

            systems.Add(new StarSystem(system.Id, system.Name));
        }

        const string endpointsPath = "$.checkpoint.topology.endpoints";
        var endpoints = new List<ConnectorEndpoint>(checkpoint.Endpoints.Count);
        var endpointsById = new Dictionary<ConnectorEndpointId, ConnectorEndpoint>();
        for (int index = 0; index < checkpoint.Endpoints.Count; index++)
        {
            WorldConnectorEndpointCheckpoint? endpoint = checkpoint.Endpoints[index];
            if (endpoint is null)
            {
                return Rejected(
                    $"{endpointsPath}[{index}]",
                    "A connector endpoint entry is required.");
            }

            if (endpoint.Id.Value == 0 || endpointsById.ContainsKey(endpoint.Id))
            {
                return Rejected(
                    $"{endpointsPath}[{index}].id",
                    "The connector endpoint identity is zero or duplicated.");
            }

            if (endpoint.SystemId.Value == 0 || !systemIds.Contains(endpoint.SystemId))
            {
                return Rejected(
                    $"{endpointsPath}[{index}].systemId",
                    "The connector endpoint references an unknown system.");
            }

            var restored = new ConnectorEndpoint(
                endpoint.Id,
                new SystemPosition(
                    endpoint.SystemId,
                    new SpatialPosition(endpoint.X, endpoint.Y)));
            endpoints.Add(restored);
            endpointsById.Add(restored.Id, restored);
        }

        const string connectionsPath = "$.checkpoint.topology.connections";
        var connections = new List<TransitConnection>(checkpoint.Connections.Count);
        var connectionIds = new HashSet<TransitConnectionId>();
        for (int index = 0; index < checkpoint.Connections.Count; index++)
        {
            WorldTransitConnectionCheckpoint? connection = checkpoint.Connections[index];
            if (connection is null)
            {
                return Rejected(
                    $"{connectionsPath}[{index}]",
                    "A transit connection entry is required.");
            }

            if (connection.Id.Value == 0 || !connectionIds.Add(connection.Id))
            {
                return Rejected(
                    $"{connectionsPath}[{index}].id",
                    "The transit connection identity is zero or duplicated.");
            }

            if (!endpointsById.TryGetValue(
                    connection.SourceEndpointId,
                    out ConnectorEndpoint? source))
            {
                return Rejected(
                    $"{connectionsPath}[{index}].sourceEndpointId",
                    "The transit connection references an unknown source endpoint.");
            }

            if (!endpointsById.TryGetValue(
                    connection.DestinationEndpointId,
                    out ConnectorEndpoint? destination))
            {
                return Rejected(
                    $"{connectionsPath}[{index}].destinationEndpointId",
                    "The transit connection references an unknown destination endpoint.");
            }

            if (source.Position.SystemId == destination.Position.SystemId)
            {
                return Rejected(
                    $"{connectionsPath}[{index}]",
                    "A transit connection must join distinct systems.");
            }

            if (connection.Duration == SimulationDuration.Zero)
            {
                return Rejected(
                    $"{connectionsPath}[{index}].duration",
                    "A transit connection requires positive duration.");
            }

            connections.Add(new TransitConnection(
                connection.Id,
                connection.SourceEndpointId,
                connection.DestinationEndpointId,
                connection.Duration));
        }

        return CheckpointResult<WorldTopology>.Success(new WorldTopology(
            systems,
            new ConnectorTopology(endpoints, connections)));
    }

    private static CheckpointResult<WorldTopology> Rejected(
        string path,
        string message) =>
        CheckpointResult<WorldTopology>.Rejected(
            new CheckpointValidationFailure(path, message));
}

/// <summary>
/// Requested navigation destination. New destination categories belong here
/// rather than in actor orders or path-selected travel legs.
/// </summary>
