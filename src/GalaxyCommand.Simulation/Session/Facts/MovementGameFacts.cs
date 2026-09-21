using System.Collections.ObjectModel;
using System.Globalization;

namespace GalaxyCommand.Simulation;

public enum LocalMotionEndReason
{
    Arrived,
    CancelledByCommand,
    ReplacedByCommand,
    SuspendedByScriptedOverride,
    ScriptedOverrideEnded,
    TargetRemoved,
}

public sealed record ShipLocalMotionStartedFact : GameFact
{
    public ShipLocalMotionStartedFact(
        ShipId shipId,
        LocalMotionSnapshot motion,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(motion);
        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Motion = motion;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public LocalMotionSnapshot Motion { get; }

    public ShipOrderId? OrderId { get; }
}

public sealed record ShipLocalMotionEndedFact : GameFact
{
    public ShipLocalMotionEndedFact(
        ShipId shipId,
        LocalMotionSnapshot motion,
        SystemPosition finalPosition,
        SimulationTime endedAt,
        LocalMotionEndReason reason,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentOutOfRangeException.ThrowIfZero(finalPosition.SystemId.Value);
        if (endedAt < motion.DepartedAt || endedAt > motion.ArrivesAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAt),
                endedAt,
                "Motion end time must fall within the scheduled segment.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown local-motion end reason.");
        }

        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Motion = motion;
        FinalPosition = finalPosition;
        EndedAt = endedAt;
        Reason = reason;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public LocalMotionSnapshot Motion { get; }

    public SystemPosition FinalPosition { get; }

    public SimulationTime EndedAt { get; }

    public LocalMotionEndReason Reason { get; }

    public ShipOrderId? OrderId { get; }
}

/// <summary>
/// Semantic arrival at one queued local waypoint while the same maneuver
/// continues toward its admitted next destination.
/// </summary>
public sealed record ShipWaypointArrivedFact : GameFact
{
    public ShipWaypointArrivedFact(
        ShipId shipId,
        MotionId motionId,
        SystemPosition waypoint,
        SystemPosition reachedPosition,
        SimulationTime arrivedAt,
        ShipOrderId orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(motionId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(waypoint.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(reachedPosition.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        if (waypoint.SystemId != reachedPosition.SystemId)
        {
            throw new ArgumentException(
                "A reached waypoint position must remain in the waypoint system.",
                nameof(reachedPosition));
        }

        ShipId = shipId;
        MotionId = motionId;
        Waypoint = waypoint;
        ReachedPosition = reachedPosition;
        ArrivedAt = arrivedAt;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public MotionId MotionId { get; }

    public SystemPosition Waypoint { get; }

    public SystemPosition ReachedPosition { get; }

    public SimulationTime ArrivedAt { get; }

    public ShipOrderId OrderId { get; }
}

/// <summary>
/// Semantic record of a moving spool completing and cruise velocity becoming
/// authoritative at the shared phase boundary.
/// </summary>
public sealed record ShipCruiseEnteredFact : GameFact
{
    public ShipCruiseEnteredFact(
        ShipId shipId,
        MotionId motionId,
        SystemPosition position,
        ShipVelocity velocity,
        SimulationTime enteredAt,
        ShipOrderId orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(motionId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        ShipId = shipId;
        MotionId = motionId;
        Position = position;
        Velocity = velocity;
        EnteredAt = enteredAt;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public MotionId MotionId { get; }

    public SystemPosition Position { get; }

    public ShipVelocity Velocity { get; }

    public SimulationTime EnteredAt { get; }

    public ShipOrderId OrderId { get; }
}

/// <summary>
/// Semantic record of planned cruise travel ending and maximum sub-cruise
/// velocity becoming authoritative at the shared phase boundary.
/// </summary>
public sealed record ShipCruiseDroppedOutFact : GameFact
{
    public ShipCruiseDroppedOutFact(
        ShipId shipId,
        MotionId motionId,
        SystemPosition position,
        ShipVelocity velocity,
        SimulationTime droppedOutAt,
        ShipOrderId orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(motionId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        ShipId = shipId;
        MotionId = motionId;
        Position = position;
        Velocity = velocity;
        DroppedOutAt = droppedOutAt;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public MotionId MotionId { get; }

    public SystemPosition Position { get; }

    public ShipVelocity Velocity { get; }

    public SimulationTime DroppedOutAt { get; }

    public ShipOrderId OrderId { get; }
}

public sealed record ShipConnectorTransitStartedFact : GameFact
{
    public ShipConnectorTransitStartedFact(
        ShipId shipId,
        ConnectorTransitSnapshot transit,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(transit);
        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Transit = transit;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public ConnectorTransitSnapshot Transit { get; }

    public ShipOrderId? OrderId { get; }
}

public sealed record ShipConnectorTransitCompletedFact : GameFact
{
    public ShipConnectorTransitCompletedFact(
        ShipId shipId,
        ConnectorTransitSnapshot transit,
        SimulationTime completedAt,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(transit);
        if (completedAt != transit.ArrivesAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt),
                completedAt,
                "Connector transit must complete at its scheduled arrival.");
        }

        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Transit = transit;
        CompletedAt = completedAt;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public ConnectorTransitSnapshot Transit { get; }

    public SimulationTime CompletedAt { get; }

    public ShipOrderId? OrderId { get; }
}

/// <summary>
/// One immutable semantic fact with authoritative order and cause.
/// </summary>
