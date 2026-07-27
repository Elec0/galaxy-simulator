using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Authoritative physical state for the initial system-space movement slice.
/// Connector transit and attachment are added with their owning subsystems.
/// </summary>
public abstract record ShipSpatialState
{
    private ShipSpatialState()
    {
    }

    public sealed record Present : ShipSpatialState
    {
        public Present(SystemPosition position)
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

public abstract record SpatialMovementEvent
{
    private SpatialMovementEvent(
        ShipId shipId,
        MotionId motionId,
        EventGeneration generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(motionId.Value);
        ShipId = shipId;
        MotionId = motionId;
        Generation = generation;
    }

    public ShipId ShipId { get; }

    public MotionId MotionId { get; }

    public EventGeneration Generation { get; }

    public sealed record Arrive : SpatialMovementEvent
    {
        public Arrive(
            ShipId shipId,
            MotionId motionId,
            EventGeneration generation)
            : base(shipId, motionId, generation)
        {
        }
    }
}

public sealed record LocalMotionSnapshot(
    MotionId Id,
    EventGeneration Generation,
    SystemPosition Origin,
    SystemPosition Destination,
    SimulationTime DepartedAt,
    SimulationTime ArrivesAt);

public sealed record ShipSpatialSnapshot(
    ShipId ShipId,
    SystemPosition Position,
    LocalMotionSnapshot? Motion);

/// <summary>
/// Authoritative owner of ship spatial state for scheduled local movement.
/// </summary>
public sealed class SpatialMovement
{
    private readonly SortedDictionary<ShipId, ActorState> _actors =
        new(EntityIdComparer<ShipId>.Instance);
    private readonly IdSequence<MotionId> _motionIds = new();

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
            ShipSpatialState.Present present => present.Position,
            ShipSpatialState.Moving moving => moving.Motion.PositionAt(time),
            _ => null,
        };
    }

    /// <summary>
    /// Authoritative commit for one already planned local leg. Evaluation
    /// workers produce the leg; the owning coordinator invokes this method.
    /// </summary>
    public LocalMotionSegment? CommitStartOrReplace<TEvent>(
        ShipId shipId,
        TravelLeg.Local leg,
        SimulationTime now,
        EventAgenda<TEvent> agenda,
        Func<SpatialMovementEvent, TEvent> wrapEvent)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        if (now != agenda.CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                $"Movement time {now.Milliseconds} ms does not match agenda time {agenda.CurrentTime.Milliseconds} ms.");
        }

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
            actor.State = new ShipSpatialState.Present(leg.Destination);
            return null;
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
        agenda.Schedule(
            arrivesAt,
            EventPhase.PhysicalCompletion,
            motion.Generation,
            wrappedEvent);
        actor.Generation = generation;
        actor.State = new ShipSpatialState.Moving(motion);
        return motion;
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

        if (movementEvent is not SpatialMovementEvent.Arrive arrive
            || actor.State is not ShipSpatialState.Moving moving
            || moving.Motion.Id != arrive.MotionId
            || moving.Motion.ArrivesAt != now)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        actor.State = new ShipSpatialState.Present(moving.Motion.Destination);
        return ScheduledEventDisposition.Applied;
    }

    public IReadOnlyList<ShipSpatialSnapshot> CaptureSnapshot(SimulationTime now)
    {
        var snapshots = new List<ShipSpatialSnapshot>(_actors.Count);
        foreach ((ShipId shipId, ActorState actor) in _actors)
        {
            switch (actor.State)
            {
                case ShipSpatialState.Present present:
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        present.Position,
                        null));
                    break;
                case ShipSpatialState.Moving moving:
                    LocalMotionSegment motion = moving.Motion;
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        motion.PositionAt(now),
                        new LocalMotionSnapshot(
                            motion.Id,
                            motion.Generation,
                            motion.Origin,
                            motion.Destination,
                            motion.DepartedAt,
                            motion.ArrivesAt)));
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
        if (actor.State is ShipSpatialState.Present present)
        {
            return present.Position;
        }

        var moving = (ShipSpatialState.Moving)actor.State;
        if (now < moving.Motion.DepartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                $"Movement time {now.Milliseconds} ms precedes departure at {moving.Motion.DepartedAt.Milliseconds} ms.");
        }

        SystemPosition position = moving.Motion.PositionAt(now);
        actor.Generation = actor.Generation.Next();
        actor.State = new ShipSpatialState.Present(position);
        return position;
    }

    private static SystemPosition CurrentPosition(
        ActorState actor,
        SimulationTime now) =>
        actor.State switch
        {
            ShipSpatialState.Present present => present.Position,
            ShipSpatialState.Moving moving => moving.Motion.PositionAt(now),
            _ => throw new InvalidOperationException(
                $"Unsupported spatial state {actor.State.GetType().Name}."),
        };

    private sealed class ActorState
    {
        public ActorState(SystemPosition position)
        {
            State = new ShipSpatialState.Present(position);
        }

        public EventGeneration Generation { get; set; } = new(0);

        public ShipSpatialState State { get; set; }
    }
}
