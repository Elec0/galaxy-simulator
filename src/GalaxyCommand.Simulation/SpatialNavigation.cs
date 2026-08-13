using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// One coordinate on an authoritative two-dimensional system map. The scale
/// represented by one unit remains a gameplay and benchmarking decision.
/// </summary>
public readonly record struct SpatialCoordinate(long Units)
{
    public override string ToString() =>
        Units.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One authoritative position within a system-local coordinate space.
/// </summary>
public readonly record struct SpatialPosition(
    SpatialCoordinate X,
    SpatialCoordinate Y);

/// <summary>
/// A position qualified by the system whose coordinate space gives it meaning.
/// </summary>
public readonly record struct SystemPosition
{
    public SystemPosition(SystemId systemId, SpatialPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
        SystemId = systemId;
        Position = position;
    }

    public SystemId SystemId { get; }

    public SpatialPosition Position { get; }
}

/// <summary>
/// One distinct local navigable space.
/// </summary>
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
public abstract record NavigationDestination
{
    private NavigationDestination()
    {
    }

    public sealed record Position : NavigationDestination
    {
        public Position(SystemPosition value)
        {
            ArgumentOutOfRangeException.ThrowIfZero(value.SystemId.Value);
            Value = value;
        }

        public SystemPosition Value { get; }
    }

    public sealed record System : NavigationDestination
    {
        public System(SystemId systemId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
            SystemId = systemId;
        }

        public SystemId SystemId { get; }
    }

    public sealed record Entity : NavigationDestination
    {
        public Entity(EntityId entityId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
            EntityId = entityId;
        }

        public EntityId EntityId { get; }
    }
}

/// <summary>
/// Read-only planning request. Planning never mutates the actor.
/// </summary>
public sealed record NavigationRequest
{
    public NavigationRequest(
        ShipId actorId,
        SystemPosition origin,
        NavigationDestination destination,
        SimulationTime plannedAt)
    {
        ArgumentOutOfRangeException.ThrowIfZero(actorId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(origin.SystemId.Value);
        ArgumentNullException.ThrowIfNull(destination);
        ActorId = actorId;
        Origin = origin;
        Destination = destination;
        PlannedAt = plannedAt;
    }

    public ShipId ActorId { get; }

    public SystemPosition Origin { get; }

    public NavigationDestination Destination { get; }

    public SimulationTime PlannedAt { get; }
}

/// <summary>
/// One path-selected step. These values are internal planning results and are
/// not part of movement-order intent.
/// </summary>
public abstract record TravelLeg
{
    private TravelLeg()
    {
    }

    public abstract SimulationDuration Duration { get; }

    public sealed record Local : TravelLeg
    {
        public Local(
            SystemPosition origin,
            SystemPosition destination,
            SimulationDuration duration)
        {
            ArgumentOutOfRangeException.ThrowIfZero(origin.SystemId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(destination.SystemId.Value);
            if (origin.SystemId != destination.SystemId)
            {
                throw new ArgumentException(
                    "A local travel leg must remain within one system.",
                    nameof(destination));
            }

            Origin = origin;
            Destination = destination;
            Duration = duration;
        }

        public SystemPosition Origin { get; }

        public SystemPosition Destination { get; }

        public override SimulationDuration Duration { get; }
    }

    public sealed record Connector : TravelLeg
    {
        public Connector(
            TransitConnectionId connectionId,
            SystemPosition origin,
            SystemPosition destination,
            SimulationDuration duration)
        {
            ArgumentOutOfRangeException.ThrowIfZero(connectionId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(origin.SystemId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(destination.SystemId.Value);
            if (origin.SystemId == destination.SystemId)
            {
                throw new ArgumentException(
                    "A connector travel leg must cross a system boundary.",
                    nameof(destination));
            }

            if (duration == SimulationDuration.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    duration,
                    "Connector transit must have a positive duration.");
            }

            ConnectionId = connectionId;
            Origin = origin;
            Destination = destination;
            Duration = duration;
        }

        public TransitConnectionId ConnectionId { get; }

        public SystemPosition Origin { get; }

        public SystemPosition Destination { get; }

        public override SimulationDuration Duration { get; }
    }
}

/// <summary>
/// Replaceable internal path for stable destination intent.
/// </summary>
public sealed record TravelPlan
{
    public TravelPlan(
        NavigationDestination destination,
        IEnumerable<TravelLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(legs);
        Destination = destination;
        Legs = new ReadOnlyCollection<TravelLeg>(legs.ToArray());
        SimulationDuration duration = SimulationDuration.Zero;
        foreach (TravelLeg leg in Legs)
        {
            ArgumentNullException.ThrowIfNull(leg);
            duration = duration.Add(leg.Duration);
        }

        TotalDuration = duration;
    }

    public NavigationDestination Destination { get; }

    public IReadOnlyList<TravelLeg> Legs { get; }

    public SimulationDuration TotalDuration { get; }
}

public enum NavigationFailureReason
{
    InterSystemConnectorRequired,
    NoConnectorPath,
    EntityUnavailable,
}

/// <summary>
/// Deterministic planning outcome with a stable failure category.
/// </summary>
public abstract record NavigationPlanResult
{
    private NavigationPlanResult()
    {
    }

    public sealed record Planned : NavigationPlanResult
    {
        public Planned(TravelPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            Plan = plan;
        }

        public TravelPlan Plan { get; }
    }

    public sealed record Unreachable : NavigationPlanResult
    {
        public Unreachable(NavigationFailureReason reason)
        {
            if (!Enum.IsDefined(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown failure reason.");
            }

            Reason = reason;
        }

        public NavigationFailureReason Reason { get; }
    }
}

/// <summary>
/// Supplies actor-specific local travel timing without fixing coordinate scale,
/// speed, acceleration, or collision behavior in the planning contract.
/// </summary>
public interface ILocalTravelTimeEstimator
{
    SimulationDuration Estimate(
        ShipId actorId,
        SystemPosition origin,
        SystemPosition destination);
}

/// <summary>
/// Read-only boundary that turns stable destination intent into replaceable
/// path-selected travel legs.
/// </summary>
public interface ISpatialNavigationPlanner
{
    NavigationPlanResult Plan(NavigationRequest request);
}

/// <summary>
/// RouteId-free planner for the first point-to-point movement slice.
/// Inter-system requests remain explicit failures; use
/// <see cref="HierarchicalNavigationPlanner"/> with connector topology.
/// </summary>
public sealed class DirectLocalNavigationPlanner : ISpatialNavigationPlanner
{
    private readonly ILocalTravelTimeEstimator _travelTime;

    public DirectLocalNavigationPlanner(ILocalTravelTimeEstimator travelTime)
    {
        ArgumentNullException.ThrowIfNull(travelTime);
        _travelTime = travelTime;
    }

    public NavigationPlanResult Plan(NavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Destination is NavigationDestination.System system)
        {
            return request.Origin.SystemId == system.SystemId
                ? new NavigationPlanResult.Planned(
                    new TravelPlan(request.Destination, []))
                : new NavigationPlanResult.Unreachable(
                    NavigationFailureReason.InterSystemConnectorRequired);
        }

        if (request.Destination is not NavigationDestination.Position destination)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Destination,
                "Unsupported navigation destination.");
        }

        if (request.Origin.SystemId != destination.Value.SystemId)
        {
            return new NavigationPlanResult.Unreachable(
                NavigationFailureReason.InterSystemConnectorRequired);
        }

        SimulationDuration duration = _travelTime.Estimate(
            request.ActorId,
            request.Origin,
            destination.Value);
        var leg = new TravelLeg.Local(
            request.Origin,
            destination.Value,
            duration);
        return new NavigationPlanResult.Planned(
            new TravelPlan(
                request.Destination,
                [leg]));
    }
}

/// <summary>
/// Deterministic hierarchical planner over system-local movement and immutable
/// directional connector topology.
/// </summary>
public sealed class HierarchicalNavigationPlanner : ISpatialNavigationPlanner
{
    private readonly ConnectorTopology _topology;
    private readonly ILocalTravelTimeEstimator _travelTime;

    public HierarchicalNavigationPlanner(
        ConnectorTopology topology,
        ILocalTravelTimeEstimator travelTime)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(travelTime);
        _topology = topology;
        _travelTime = travelTime;
    }

    public NavigationPlanResult Plan(NavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SystemId destinationSystem = request.Destination switch
        {
            NavigationDestination.Position position =>
                position.Value.SystemId,
            NavigationDestination.System system =>
                system.SystemId,
            _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Destination,
                    "Unsupported navigation destination."),
        };

        if (request.Origin.SystemId == destinationSystem)
        {
            return request.Destination switch
            {
                NavigationDestination.Position position =>
                    PlannedLocal(request, position.Value),
                NavigationDestination.System =>
                    new NavigationPlanResult.Planned(
                        new TravelPlan(request.Destination, [])),
                _ => throw new InvalidOperationException(
                    "Unsupported navigation destination."),
            };
        }

        SearchState? bestDestination = null;
        var bestByEndpoint = new Dictionary<ConnectorEndpointId, SearchState>();
        var pending = new PriorityQueue<SearchState, SearchState>(
            SearchStateComparer.Instance);
        AddOutgoingCandidates(
            request,
            request.Origin,
            SimulationDuration.Zero,
            [],
            [],
            bestByEndpoint,
            pending);

        while (pending.Count > 0)
        {
            SearchState current = pending.Dequeue();
            if (!ReferenceEquals(
                    bestByEndpoint.GetValueOrDefault(current.ArrivalEndpointId),
                    current))
            {
                continue;
            }

            if (current.Position.SystemId == destinationSystem)
            {
                SearchState completed = CompleteDestination(
                    request,
                    current);
                if (bestDestination is null
                    || CompareSearchState(completed, bestDestination) < 0)
                {
                    bestDestination = completed;
                }
            }

            if (bestDestination is not null
                && current.Duration >= bestDestination.Duration)
            {
                continue;
            }

            AddOutgoingCandidates(
                request,
                current.Position,
                current.Duration,
                current.Legs,
                current.ConnectionPath,
                bestByEndpoint,
                pending);
        }

        return bestDestination is null
            ? new NavigationPlanResult.Unreachable(
                NavigationFailureReason.NoConnectorPath)
            : new NavigationPlanResult.Planned(
                new TravelPlan(request.Destination, bestDestination.Legs));
    }

    private NavigationPlanResult.Planned PlannedLocal(
        NavigationRequest request,
        SystemPosition destination)
    {
        SimulationDuration duration = _travelTime.Estimate(
            request.ActorId,
            request.Origin,
            destination);
        return new NavigationPlanResult.Planned(
            new TravelPlan(
                request.Destination,
                [new TravelLeg.Local(request.Origin, destination, duration)]));
    }

    private SearchState CompleteDestination(
        NavigationRequest request,
        SearchState current)
    {
        if (request.Destination is NavigationDestination.System)
        {
            return current;
        }

        var destination = (NavigationDestination.Position)request.Destination;
        SimulationDuration finalDuration = _travelTime.Estimate(
            request.ActorId,
            current.Position,
            destination.Value);
        return new SearchState(
            current.ArrivalEndpointId,
            destination.Value,
            current.Duration.Add(finalDuration),
            [
                .. current.Legs,
                new TravelLeg.Local(
                    current.Position,
                    destination.Value,
                    finalDuration),
            ],
            current.ConnectionPath);
    }

    private void AddOutgoingCandidates(
        NavigationRequest request,
        SystemPosition current,
        SimulationDuration duration,
        IReadOnlyList<TravelLeg> legs,
        IReadOnlyList<TransitConnectionId> connectionPath,
        IDictionary<ConnectorEndpointId, SearchState> bestByEndpoint,
        PriorityQueue<SearchState, SearchState> pending)
    {
        foreach (TransitConnection connection in
            _topology.OutgoingFrom(current.SystemId))
        {
            ConnectorEndpoint source = _topology.GetEndpoint(
                connection.SourceEndpointId);
            ConnectorEndpoint destination = _topology.GetEndpoint(
                connection.DestinationEndpointId);
            SimulationDuration localDuration = _travelTime.Estimate(
                request.ActorId,
                current,
                source.Position);
            var candidate = new SearchState(
                destination.Id,
                destination.Position,
                duration
                    .Add(localDuration)
                    .Add(connection.Duration),
                [
                    .. legs,
                    new TravelLeg.Local(
                        current,
                        source.Position,
                        localDuration),
                    new TravelLeg.Connector(
                        connection.Id,
                        source.Position,
                        destination.Position,
                        connection.Duration),
                ],
                [.. connectionPath, connection.Id]);
            if (!bestByEndpoint.TryGetValue(destination.Id, out SearchState? best)
                || CompareSearchState(candidate, best) < 0)
            {
                bestByEndpoint[destination.Id] = candidate;
                pending.Enqueue(candidate, candidate);
            }
        }
    }

    private static int CompareSearchState(
        SearchState left,
        SearchState right)
    {
        int duration = left.Duration.CompareTo(right.Duration);
        if (duration != 0)
        {
            return duration;
        }

        int commonLength = Math.Min(
            left.ConnectionPath.Count,
            right.ConnectionPath.Count);
        for (int index = 0; index < commonLength; index++)
        {
            int connection = left.ConnectionPath[index].Value.CompareTo(
                right.ConnectionPath[index].Value);
            if (connection != 0)
            {
                return connection;
            }
        }

        int pathLength = left.ConnectionPath.Count.CompareTo(
            right.ConnectionPath.Count);
        return pathLength != 0
            ? pathLength
            : left.ArrivalEndpointId.Value.CompareTo(
                right.ArrivalEndpointId.Value);
    }

    private sealed record SearchState(
        ConnectorEndpointId ArrivalEndpointId,
        SystemPosition Position,
        SimulationDuration Duration,
        IReadOnlyList<TravelLeg> Legs,
        IReadOnlyList<TransitConnectionId> ConnectionPath);

    private sealed class SearchStateComparer : IComparer<SearchState>
    {
        internal static SearchStateComparer Instance { get; } = new();

        public int Compare(SearchState? left, SearchState? right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            return CompareSearchState(left, right);
        }
    }
}
