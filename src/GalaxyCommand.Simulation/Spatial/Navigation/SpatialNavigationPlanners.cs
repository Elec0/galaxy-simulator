using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public interface ILocalTravelTimeEstimator
{
    SimulationDuration Estimate(
        ShipId actorId,
        SystemPosition origin,
        SystemPosition destination);
}

/// <summary>
/// Stable versioned local timing policy that uses Chebyshev map distance.
/// </summary>
public sealed class ChebyshevLocalTravelTimeEstimator : ILocalTravelTimeEstimator
{
    /// <summary>
    /// Creates a registered timing policy with an exact positive map-unit scale.
    /// </summary>
    public ChebyshevLocalTravelTimeEstimator(ulong millisecondsPerMapUnit)
    {
        ArgumentOutOfRangeException.ThrowIfZero(millisecondsPerMapUnit);
        MillisecondsPerMapUnit = millisecondsPerMapUnit;
    }

    public ulong MillisecondsPerMapUnit { get; }

    /// <inheritdoc />
    public SimulationDuration Estimate(
        ShipId actorId,
        SystemPosition origin,
        SystemPosition destination)
    {
        ArgumentOutOfRangeException.ThrowIfZero(actorId.Value);
        if (origin.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "Local travel timing requires positions in the same system.",
                nameof(destination));
        }

        ulong horizontal = Distance(origin.Position.X.Units, destination.Position.X.Units);
        ulong vertical = Distance(origin.Position.Y.Units, destination.Position.Y.Units);
        return new SimulationDuration(
            checked(Math.Max(horizontal, vertical) * MillisecondsPerMapUnit));
    }

    /// <summary>
    /// Computes the exact unsigned magnitude without overflowing signed coordinates.
    /// </summary>
    private static ulong Distance(long first, long second)
    {
        Int128 difference = (Int128)first - second;
        UInt128 magnitude = difference < 0
            ? (UInt128)(-difference)
            : (UInt128)difference;
        return checked((ulong)magnitude);
    }
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

    internal ILocalTravelTimeEstimator TravelTime => _travelTime;

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

    internal ConnectorTopology Topology => _topology;

    internal ILocalTravelTimeEstimator TravelTime => _travelTime;

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
