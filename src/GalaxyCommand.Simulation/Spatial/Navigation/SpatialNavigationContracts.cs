using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

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
