namespace GalaxyCommand.Simulation;

/// <summary>
/// Read-only navigation behavior used by ships and logistics systems.
/// </summary>
public interface INavigation
{
    DirectedRoute? GetRoute(RouteId routeId);

    RoutePlan? FindRoute(LocationId origin, LocationId destination);
}

/// <summary>
/// One directed connection between locations.
/// </summary>
public sealed class DirectedRoute
{
    internal DirectedRoute(
        RouteId id,
        LocationId origin,
        LocationId destination,
        SimulationDuration baseDuration)
    {
        Id = id;
        Origin = origin;
        Destination = destination;
        BaseDuration = baseDuration;
        IsEnabled = true;
    }

    public RouteId Id { get; }

    public LocationId Origin { get; }

    public LocationId Destination { get; }

    public SimulationDuration BaseDuration { get; }

    public bool IsEnabled { get; internal set; }
}

/// <summary>
/// Deterministic route returned by a navigation query.
/// </summary>
public sealed record RoutePlan(
    IReadOnlyList<RouteId> RouteIds,
    SimulationDuration TotalDuration);

/// <summary>
/// Deterministic directed multigraph used by the Phase 1 navigation backend.
/// </summary>
public sealed class RouteGraph : INavigation, ILogisticsNavigation
{
    private readonly HashSet<LocationId> _locations = [];
    private readonly Dictionary<RouteId, DirectedRoute> _routes = [];
    private readonly Dictionary<LocationId, SortedSet<RouteId>> _outgoing = [];
    private readonly IdSequence<RouteId> _routeIds = new();

    public IEnumerable<LocationId> Locations =>
        _locations.OrderBy(location => location.Value);

    public IEnumerable<DirectedRoute> Routes =>
        _routes.Values.OrderBy(route => route.Id.Value);

    public bool AddLocation(LocationId location)
    {
        bool inserted = _locations.Add(location);
        _outgoing.TryAdd(location, new SortedSet<RouteId>(EntityIdComparer<RouteId>.Instance));
        return inserted;
    }

    public RouteId AddRoute(
        LocationId origin,
        LocationId destination,
        SimulationDuration baseDuration)
    {
        RequireLocation(origin);
        RequireLocation(destination);

        RouteId id = _routeIds.Allocate();
        var route = new DirectedRoute(id, origin, destination, baseDuration);
        _routes.Add(id, route);
        _outgoing[origin].Add(id);
        return id;
    }

    public (RouteId Forward, RouteId Reverse) AddBidirectionalRoutes(
        LocationId first,
        LocationId second,
        SimulationDuration baseDuration)
    {
        RouteId forward = AddRoute(first, second, baseDuration);
        RouteId reverse = AddRoute(second, first, baseDuration);
        return (forward, reverse);
    }

    public DirectedRoute? GetRoute(RouteId routeId) =>
        _routes.GetValueOrDefault(routeId);

    public void SetRouteEnabled(RouteId routeId, bool enabled)
    {
        DirectedRoute route = _routes.GetValueOrDefault(routeId)
            ?? throw new KeyNotFoundException($"Unknown route {routeId}.");
        route.IsEnabled = enabled;
    }

    public RoutePlan? FindRoute(LocationId origin, LocationId destination)
    {
        RequireLocation(origin);
        RequireLocation(destination);

        if (origin == destination)
        {
            return new RoutePlan([], SimulationDuration.Zero);
        }

        var initial = new PathCandidate(SimulationDuration.Zero, [], origin);
        var best = new Dictionary<LocationId, PathCandidate> { [origin] = initial };
        var frontier = new SortedSet<PathCandidate>(PathCandidateComparer.Instance) { initial };

        while (frontier.Count > 0)
        {
            PathCandidate candidate = frontier.Min!;
            frontier.Remove(candidate);
            if (!ReferenceEquals(best.GetValueOrDefault(candidate.Location), candidate))
            {
                continue;
            }

            if (candidate.Location == destination)
            {
                return new RoutePlan(candidate.RouteIds, candidate.TotalDuration);
            }

            if (!_outgoing.TryGetValue(candidate.Location, out SortedSet<RouteId>? routeIds))
            {
                continue;
            }

            foreach (RouteId routeId in routeIds)
            {
                if (!_routes.TryGetValue(routeId, out DirectedRoute? route) || !route.IsEnabled)
                {
                    continue;
                }

                SimulationDuration totalDuration = candidate.TotalDuration.Add(route.BaseDuration);
                var path = new List<RouteId>(candidate.RouteIds) { route.Id };
                var next = new PathCandidate(totalDuration, path, route.Destination);

                if (!best.TryGetValue(next.Location, out PathCandidate? known)
                    || PathCandidateComparer.Instance.Compare(next, known) < 0)
                {
                    if (known is not null)
                    {
                        frontier.Remove(known);
                    }

                    best[next.Location] = next;
                    frontier.Add(next);
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public LogisticsTravelEstimate? Estimate(
        ShipId actorId,
        LocationId origin,
        LocationId destination,
        SimulationTime plannedAt)
    {
        RoutePlan? plan = FindRoute(origin, destination);
        return plan is null ? null : new LogisticsTravelEstimate(plan.TotalDuration);
    }

    private void RequireLocation(LocationId location)
    {
        if (!_locations.Contains(location))
        {
            throw new KeyNotFoundException($"Unknown location {location}.");
        }
    }

    private sealed record PathCandidate(
        SimulationDuration TotalDuration,
        IReadOnlyList<RouteId> RouteIds,
        LocationId Location);

    private sealed class PathCandidateComparer : IComparer<PathCandidate>
    {
        public static readonly PathCandidateComparer Instance = new();

        public int Compare(PathCandidate? left, PathCandidate? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            int durationComparison = left.TotalDuration.CompareTo(right.TotalDuration);
            if (durationComparison != 0)
            {
                return durationComparison;
            }

            int commonLength = Math.Min(left.RouteIds.Count, right.RouteIds.Count);
            for (int index = 0; index < commonLength; index++)
            {
                int routeComparison = left.RouteIds[index].Value.CompareTo(right.RouteIds[index].Value);
                if (routeComparison != 0)
                {
                    return routeComparison;
                }
            }

            int lengthComparison = left.RouteIds.Count.CompareTo(right.RouteIds.Count);
            return lengthComparison != 0
                ? lengthComparison
                : left.Location.Value.CompareTo(right.Location.Value);
        }
    }
}

internal sealed class EntityIdComparer<TId> : IComparer<TId>
    where TId : struct, IEntityId<TId>
{
    public static readonly EntityIdComparer<TId> Instance = new();

    public int Compare(TId left, TId right) => left.Value.CompareTo(right.Value);
}
