using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Opaque reachability and duration result consumed by logistics assignment and
/// transport. It deliberately does not reveal selected local or connector legs.
/// </summary>
public sealed record LogisticsTravelEstimate
{
    /// <summary>
    /// Creates an opaque estimate for a reachable logistics journey.
    /// </summary>
    public LogisticsTravelEstimate(SimulationDuration duration)
    {
        Duration = duration;
    }

    /// <summary>
    /// Gets the complete estimated journey duration.
    /// </summary>
    public SimulationDuration Duration { get; }
}

/// <summary>
/// Supplies logistics with reachability and travel estimates between its
/// compatibility locations without exposing navigation implementation details.
/// </summary>
public interface ILogisticsNavigation
{
    /// <summary>
    /// Estimates travel for an actor between two logistics locations at one
    /// authoritative planning time, or returns <see langword="null"/> when the
    /// journey cannot currently be planned.
    /// </summary>
    LogisticsTravelEstimate? Estimate(
        ShipId actorId,
        LocationId origin,
        LocationId destination,
        SimulationTime plannedAt);
}

/// <summary>
/// Maps legacy logistics locations to spatial anchors and delegates planning to
/// the hierarchical navigation boundary. Connector and local leg selection
/// remain private to that planner.
/// </summary>
public sealed class HierarchicalLogisticsNavigation : ILogisticsNavigation
{
    private readonly ReadOnlyDictionary<LocationId, SystemPosition> _anchors;
    private readonly ISpatialNavigationPlanner _planner;

    /// <summary>
    /// Creates a spatial logistics adapter from an explicit legacy-location
    /// mapping and a hierarchical planner.
    /// </summary>
    public HierarchicalLogisticsNavigation(
        IReadOnlyDictionary<LocationId, SystemPosition> anchors,
        ISpatialNavigationPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(planner);

        var copiedAnchors = new Dictionary<LocationId, SystemPosition>();
        foreach ((LocationId locationId, SystemPosition position) in anchors)
        {
            ArgumentOutOfRangeException.ThrowIfZero(locationId.Value);
            copiedAnchors.Add(locationId, position);
        }

        _anchors = new ReadOnlyDictionary<LocationId, SystemPosition>(copiedAnchors);
        _planner = planner;
    }

    internal IReadOnlyDictionary<LocationId, SystemPosition> Anchors => _anchors;

    internal ISpatialNavigationPlanner Planner => _planner;

    /// <inheritdoc />
    public LogisticsTravelEstimate? Estimate(
        ShipId actorId,
        LocationId origin,
        LocationId destination,
        SimulationTime plannedAt)
    {
        ArgumentOutOfRangeException.ThrowIfZero(actorId.Value);
        if (!_anchors.TryGetValue(origin, out SystemPosition originPosition)
            || !_anchors.TryGetValue(destination, out SystemPosition destinationPosition))
        {
            return null;
        }

        NavigationPlanResult result = _planner.Plan(
            new NavigationRequest(
                actorId,
                originPosition,
                new NavigationDestination.Position(destinationPosition),
                plannedAt));
        return result switch
        {
            NavigationPlanResult.Planned planned =>
                new LogisticsTravelEstimate(planned.Plan.TotalDuration),
            NavigationPlanResult.Unreachable => null,
            _ => throw new InvalidOperationException("Unsupported navigation plan result."),
        };
    }
}
