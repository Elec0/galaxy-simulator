using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Authoritative physical state for system-local motion and connector transit.
/// Attachment is added with its future owning subsystem.
/// </summary>
public abstract record ShipSpatialState
{
    private ShipSpatialState()
    {
    }

    public sealed record AtPosition : ShipSpatialState
    {
        public AtPosition(SystemPosition position)
        {
            ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
            Position = position;
        }

        public SystemPosition Position { get; }
    }

    public sealed record Moving : ShipSpatialState
    {
        public Moving(LocalMotionSegment motion)
        {
            ArgumentNullException.ThrowIfNull(motion);
            Motion = motion;
        }

        public LocalMotionSegment Motion { get; }
    }

    public sealed record ConnectorTransit : ShipSpatialState
    {
        public ConnectorTransit(ConnectorTransitSegment transit)
        {
            ArgumentNullException.ThrowIfNull(transit);
            Transit = transit;
        }

        public ConnectorTransitSegment Transit { get; }
    }
}

/// <summary>
/// Scheduled authoritative movement between two positions in one system.
/// </summary>
public sealed record LocalMotionSegment
{
    public LocalMotionSegment(
        MotionId id,
        EventGeneration generation,
        SystemPosition origin,
        SystemPosition destination,
        SimulationTime departedAt,
        SimulationTime arrivesAt)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(origin.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(destination.SystemId.Value);
        if (origin.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "Local motion cannot cross a system boundary.",
                nameof(destination));
        }

        if (arrivesAt <= departedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arrivesAt),
                arrivesAt,
                "Local motion must have a positive duration.");
        }

        Id = id;
        Generation = generation;
        Origin = origin;
        Destination = destination;
        DepartedAt = departedAt;
        ArrivesAt = arrivesAt;
    }

    public MotionId Id { get; }

    public EventGeneration Generation { get; }

    public SystemPosition Origin { get; }

    public SystemPosition Destination { get; }

    public SimulationTime DepartedAt { get; }

    public SimulationTime ArrivesAt { get; }

    public SystemPosition PositionAt(SimulationTime time)
    {
        if (time <= DepartedAt)
        {
            return Origin;
        }

        if (time >= ArrivesAt)
        {
            return Destination;
        }

        ulong elapsed = time.Milliseconds - DepartedAt.Milliseconds;
        ulong duration = ArrivesAt.Milliseconds - DepartedAt.Milliseconds;
        return new SystemPosition(
            Origin.SystemId,
            new SpatialPosition(
                Interpolate(Origin.Position.X, Destination.Position.X, elapsed, duration),
                Interpolate(Origin.Position.Y, Destination.Position.Y, elapsed, duration)));
    }

    private static SpatialCoordinate Interpolate(
        SpatialCoordinate origin,
        SpatialCoordinate destination,
        ulong elapsed,
        ulong duration)
    {
        Int128 delta = (Int128)destination.Units - origin.Units;
        bool negative = delta < 0;
        UInt128 magnitude = (UInt128)(negative ? -delta : delta);
        UInt128 scaledMagnitude = magnitude * elapsed / duration;
        Int128 offset = negative
            ? -(Int128)scaledMagnitude
            : (Int128)scaledMagnitude;
        return new SpatialCoordinate(checked((long)((Int128)origin.Units + offset)));
    }
}

/// <summary>
/// Scheduled authoritative traversal between endpoints in distinct systems.
/// A ship in this state has no ordinary system-local position.
/// </summary>
public sealed record ConnectorTransitSegment
{
    public ConnectorTransitSegment(
        ConnectorTransitId id,
        EventGeneration generation,
        TransitConnectionId connectionId,
        SystemPosition source,
        SystemPosition destination,
        SimulationTime departedAt,
        SimulationTime arrivesAt)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(connectionId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(source.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(destination.SystemId.Value);
        if (source.SystemId == destination.SystemId)
        {
            throw new ArgumentException(
                "Connector transit must cross a system boundary.",
                nameof(destination));
        }

        if (arrivesAt <= departedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arrivesAt),
                arrivesAt,
                "Connector transit must have a positive duration.");
        }

        Id = id;
        Generation = generation;
        ConnectionId = connectionId;
        Source = source;
        Destination = destination;
        DepartedAt = departedAt;
        ArrivesAt = arrivesAt;
    }

    public ConnectorTransitId Id { get; }

    public EventGeneration Generation { get; }

    public TransitConnectionId ConnectionId { get; }

    public SystemPosition Source { get; }

    public SystemPosition Destination { get; }

    public SimulationTime DepartedAt { get; }

    public SimulationTime ArrivesAt { get; }
}

public abstract record SpatialMovementEvent
{
    private SpatialMovementEvent(
        ShipId shipId,
        EventGeneration generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ShipId = shipId;
        Generation = generation;
    }

    public ShipId ShipId { get; }

    public EventGeneration Generation { get; }

    public sealed record Arrive : SpatialMovementEvent
    {
        public Arrive(
            ShipId shipId,
            MotionId motionId,
            EventGeneration generation)
            : base(shipId, generation)
        {
            ArgumentOutOfRangeException.ThrowIfZero(motionId.Value);
            MotionId = motionId;
        }

        public MotionId MotionId { get; }
    }

    public sealed record Emerge : SpatialMovementEvent
    {
        public Emerge(
            ShipId shipId,
            ConnectorTransitId transitId,
            EventGeneration generation)
            : base(shipId, generation)
        {
            ArgumentOutOfRangeException.ThrowIfZero(transitId.Value);
            TransitId = transitId;
        }

        public ConnectorTransitId TransitId { get; }
    }
}

public sealed record LocalMotionSnapshot(
    MotionId Id,
    EventGeneration Generation,
    SystemPosition Origin,
    SystemPosition Destination,
    SimulationTime DepartedAt,
    SimulationTime ArrivesAt);

public sealed record ConnectorTransitSnapshot(
    ConnectorTransitId Id,
    EventGeneration Generation,
    TransitConnectionId ConnectionId,
    SystemPosition Source,
    SystemPosition Destination,
    SimulationTime DepartedAt,
    SimulationTime ArrivesAt);

/// <summary>
/// Result of committing one local-motion transition. Future work is returned
/// as an agenda proposal so event sequence allocation remains agenda-owned.
/// </summary>
public sealed record LocalMotionCommit<TEvent>(
    LocalMotionSegment? Motion,
    AgendaEventProposal<TEvent>? EventProposal);

/// <summary>
/// Result of committing one connector traversal. Future work is returned as an
/// agenda proposal so event sequence allocation remains agenda-owned.
/// </summary>
public sealed record ConnectorTransitCommit<TEvent>(
    ConnectorTransitSegment Transit,
    AgendaEventProposal<TEvent> EventProposal);

public abstract record ShipSpatialSnapshotState
{
    private ShipSpatialSnapshotState()
    {
    }

    public sealed record AtPosition(SystemPosition Position) : ShipSpatialSnapshotState;

    public sealed record LocalMotion(
        SystemPosition CurrentPosition,
        LocalMotionSnapshot Motion) : ShipSpatialSnapshotState;

    public sealed record ConnectorTransit(
        ConnectorTransitSnapshot Transit) : ShipSpatialSnapshotState;
}

public sealed record ShipSpatialSnapshot(
    ShipId ShipId,
    ShipSpatialSnapshotState State)
{
    public SystemPosition? Position =>
        State switch
        {
            ShipSpatialSnapshotState.AtPosition atPosition =>
                atPosition.Position,
            ShipSpatialSnapshotState.LocalMotion localMotion =>
                localMotion.CurrentPosition,
            ShipSpatialSnapshotState.ConnectorTransit => null,
            _ => throw new InvalidOperationException(
                $"Unsupported spatial snapshot state {State.GetType().Name}."),
        };

    public LocalMotionSnapshot? Motion =>
        (State as ShipSpatialSnapshotState.LocalMotion)?.Motion;

    public ConnectorTransitSnapshot? Transit =>
        (State as ShipSpatialSnapshotState.ConnectorTransit)?.Transit;
}

/// <summary>
/// Authoritative owner of ship spatial state for scheduled local movement and
/// connector traversal.
/// </summary>
public sealed class SpatialMovement
{
    private readonly SortedDictionary<ShipId, ActorState> _actors =
        new(EntityIdComparer<ShipId>.Instance);
    private readonly IdSequence<MotionId> _motionIds = new();
    private readonly IdSequence<ConnectorTransitId> _transitIds = new();

    public void Add(ShipId shipId, SystemPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        if (!_actors.TryAdd(shipId, new ActorState(position)))
        {
            throw new InvalidOperationException($"Duplicate spatial actor {shipId}.");
        }
    }

    public ShipSpatialState? GetState(ShipId shipId) =>
        _actors.GetValueOrDefault(shipId)?.State;

    public bool Contains(ShipId shipId) =>
        _actors.ContainsKey(shipId);

    public SystemPosition? PositionAt(ShipId shipId, SimulationTime time)
    {
        ActorState? actor = _actors.GetValueOrDefault(shipId);
        return actor?.State switch
        {
            ShipSpatialState.AtPosition atPosition => atPosition.Position,
            ShipSpatialState.Moving moving => moving.Motion.PositionAt(time),
            ShipSpatialState.ConnectorTransit => null,
            _ => null,
        };
    }

    /// <summary>
    /// Authoritative commit for one already planned local leg. Evaluation
    /// workers produce the leg; the owning coordinator invokes this method.
    /// </summary>
    public LocalMotionCommit<TEvent> CommitStartOrReplace<TEvent>(
        ShipId shipId,
        TravelLeg.Local leg,
        SimulationTime now,
        Func<SpatialMovementEvent, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(wrapEvent);

        ActorState actor = GetRequiredActor(shipId);
        SystemPosition current = CurrentPosition(actor, now);
        if (current != leg.Origin)
        {
            throw new InvalidOperationException(
                $"Ship {shipId} is at {current}, not the planned origin {leg.Origin}.");
        }

        EventGeneration generation = actor.State is ShipSpatialState.Moving
            ? actor.Generation.Next()
            : actor.Generation;
        if (leg.Duration == SimulationDuration.Zero
            || leg.Origin == leg.Destination)
        {
            actor.Generation = generation;
            actor.State = new ShipSpatialState.AtPosition(leg.Destination);
            return new LocalMotionCommit<TEvent>(null, null);
        }

        SimulationTime arrivesAt = now.Add(leg.Duration);
        var motion = new LocalMotionSegment(
            _motionIds.Allocate(),
            generation,
            leg.Origin,
            leg.Destination,
            now,
            arrivesAt);
        TEvent wrappedEvent = wrapEvent(new SpatialMovementEvent.Arrive(
            shipId,
            motion.Id,
            motion.Generation));
        actor.Generation = generation;
        actor.State = new ShipSpatialState.Moving(motion);
        return new LocalMotionCommit<TEvent>(
            motion,
            new AgendaEventProposal<TEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ActorOrders,
                    shipId.Value,
                    motion.Id.Value,
                    EffectKind: 1,
                    LocalOrdinal: 0),
                arrivesAt,
                EventPhase.PhysicalCompletion,
                motion.Generation,
                wrappedEvent));
    }

    /// <summary>
    /// Authoritative commit for one validated connector traversal.
    /// </summary>
    public ConnectorTransitCommit<TEvent> CommitStartConnector<TEvent>(
        ShipId shipId,
        TravelLeg.Connector leg,
        SimulationTime now,
        Func<SpatialMovementEvent, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(wrapEvent);

        ActorState actor = GetRequiredActor(shipId);
        if (actor.State is not ShipSpatialState.AtPosition atPosition
            || atPosition.Position != leg.Origin)
        {
            throw new InvalidOperationException(
                $"Ship {shipId} is not at connector origin {leg.Origin}.");
        }

        SimulationTime arrivesAt = now.Add(leg.Duration);
        var transit = new ConnectorTransitSegment(
            _transitIds.Allocate(),
            actor.Generation,
            leg.ConnectionId,
            leg.Origin,
            leg.Destination,
            now,
            arrivesAt);
        TEvent wrappedEvent = wrapEvent(new SpatialMovementEvent.Emerge(
            shipId,
            transit.Id,
            transit.Generation));
        actor.State = new ShipSpatialState.ConnectorTransit(transit);
        return new ConnectorTransitCommit<TEvent>(
            transit,
            new AgendaEventProposal<TEvent>(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.ActorOrders,
                    shipId.Value,
                    transit.Id.Value,
                    EffectKind: 2,
                    LocalOrdinal: 0),
                arrivesAt,
                EventPhase.PhysicalCompletion,
                transit.Generation,
                wrappedEvent));
    }

    /// <summary>
    /// Authoritative cancellation commit at the current simulation time.
    /// </summary>
    public bool CommitCancel(ShipId shipId, SimulationTime now)
    {
        ActorState actor = GetRequiredActor(shipId);
        if (actor.State is not ShipSpatialState.Moving)
        {
            return false;
        }

        MaterializeForChange(actor, now);
        return true;
    }

    /// <summary>
    /// Authoritative cleanup commit for an actor being removed. Any pending
    /// arrival becomes a deterministic missing-reference no-op.
    /// </summary>
    public bool CommitRemove(ShipId shipId, SimulationTime now)
    {
        if (!_actors.TryGetValue(shipId, out ActorState? actor))
        {
            return false;
        }

        if (actor.State is ShipSpatialState.Moving)
        {
            MaterializeForChange(actor, now);
        }

        return _actors.Remove(shipId);
    }

    public ScheduledEventDisposition HandleEvent(
        SpatialMovementEvent movementEvent,
        EventGeneration scheduledGeneration,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(movementEvent);
        if (scheduledGeneration != movementEvent.Generation)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        if (!_actors.TryGetValue(movementEvent.ShipId, out ActorState? actor))
        {
            return ScheduledEventDisposition.IgnoredMissingReference;
        }

        if (actor.Generation != movementEvent.Generation)
        {
            return ScheduledEventDisposition.IgnoredStaleGeneration;
        }

        switch (movementEvent)
        {
            case SpatialMovementEvent.Arrive arrive
                when actor.State is ShipSpatialState.Moving moving
                    && moving.Motion.Id == arrive.MotionId
                    && moving.Motion.ArrivesAt == now:
                actor.State = new ShipSpatialState.AtPosition(
                    moving.Motion.Destination);
                return ScheduledEventDisposition.Applied;
            case SpatialMovementEvent.Emerge emerge
                when actor.State is ShipSpatialState.ConnectorTransit traversing
                    && traversing.Transit.Id == emerge.TransitId
                    && traversing.Transit.ArrivesAt == now:
                actor.State = new ShipSpatialState.AtPosition(
                    traversing.Transit.Destination);
                return ScheduledEventDisposition.Applied;
            default:
                return ScheduledEventDisposition.IgnoredStateMismatch;
        }
    }

    public IReadOnlyList<ShipSpatialSnapshot> CaptureSnapshot(SimulationTime now)
    {
        var snapshots = new List<ShipSpatialSnapshot>(_actors.Count);
        foreach ((ShipId shipId, ActorState actor) in _actors)
        {
            switch (actor.State)
            {
                case ShipSpatialState.AtPosition atPosition:
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        new ShipSpatialSnapshotState.AtPosition(
                            atPosition.Position)));
                    break;
                case ShipSpatialState.Moving moving:
                    LocalMotionSegment motion = moving.Motion;
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        new ShipSpatialSnapshotState.LocalMotion(
                            motion.PositionAt(now),
                            new LocalMotionSnapshot(
                                motion.Id,
                                motion.Generation,
                                motion.Origin,
                                motion.Destination,
                                motion.DepartedAt,
                                motion.ArrivesAt))));
                    break;
                case ShipSpatialState.ConnectorTransit traversing:
                    ConnectorTransitSegment transit = traversing.Transit;
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        new ShipSpatialSnapshotState.ConnectorTransit(
                            new ConnectorTransitSnapshot(
                                transit.Id,
                                transit.Generation,
                                transit.ConnectionId,
                                transit.Source,
                                transit.Destination,
                                transit.DepartedAt,
                                transit.ArrivesAt))));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported spatial state {actor.State.GetType().Name}.");
            }
        }

        return new ReadOnlyCollection<ShipSpatialSnapshot>(snapshots);
    }

    private ActorState GetRequiredActor(ShipId shipId) =>
        _actors.GetValueOrDefault(shipId)
        ?? throw new KeyNotFoundException($"Unknown spatial actor {shipId}.");

    private static SystemPosition MaterializeForChange(
        ActorState actor,
        SimulationTime now)
    {
        if (actor.State is ShipSpatialState.AtPosition atPosition)
        {
            return atPosition.Position;
        }

        var moving = actor.State as ShipSpatialState.Moving
            ?? throw new InvalidOperationException(
                "Connector transit cannot be materialized into a system-local position.");
        if (now < moving.Motion.DepartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                $"Movement time {now.Milliseconds} ms precedes departure at {moving.Motion.DepartedAt.Milliseconds} ms.");
        }

        SystemPosition position = moving.Motion.PositionAt(now);
        actor.Generation = actor.Generation.Next();
        actor.State = new ShipSpatialState.AtPosition(position);
        return position;
    }

    private static SystemPosition CurrentPosition(
        ActorState actor,
        SimulationTime now) =>
        actor.State switch
        {
            ShipSpatialState.AtPosition atPosition => atPosition.Position,
            ShipSpatialState.Moving moving => moving.Motion.PositionAt(now),
            ShipSpatialState.ConnectorTransit => throw new InvalidOperationException(
                "A ship in connector transit has no system-local position."),
            _ => throw new InvalidOperationException(
                $"Unsupported spatial state {actor.State.GetType().Name}."),
        };

    private sealed class ActorState
    {
        public ActorState(SystemPosition position)
        {
            State = new ShipSpatialState.AtPosition(position);
        }

        public EventGeneration Generation { get; set; } = new(0);

        public ShipSpatialState State { get; set; }
    }
}
