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
/// Inter-system requests remain explicit failures until connector planning is
/// introduced.
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
        var destination = request.Destination as NavigationDestination.Position
            ?? throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Destination,
                "Unsupported navigation destination.");

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
