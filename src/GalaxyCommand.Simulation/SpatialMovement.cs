using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Authoritative physical state for system-local compatibility motion, analytic
/// maneuvering, and connector transit. Attachment is added with its future
/// owning subsystem.
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

    public sealed record AnalyticManeuver : ShipSpatialState
    {
        public AnalyticManeuver(
            ScheduledTerminalManeuver maneuver,
            ManeuverObjective objective)
        {
            ArgumentNullException.ThrowIfNull(maneuver);
            if (!Enum.IsDefined(objective))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objective),
                    objective,
                    "Unknown maneuver objective.");
            }

            Maneuver = maneuver;
            Objective = objective;
        }

        public ScheduledTerminalManeuver Maneuver { get; }

        public ManeuverObjective Objective { get; }
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

    /// <summary>
    /// Exact agenda entry that completes this active motion, once its proposal
    /// has been committed by the agenda owner.
    /// </summary>
    public EventKey? CompletionEventKey { get; internal set; }

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

    /// <summary>
    /// Exact agenda entry that completes this active transit, once its proposal
    /// has been committed by the agenda owner.
    /// </summary>
    public EventKey? CompletionEventKey { get; internal set; }
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

    public sealed record Maneuver : SpatialMovementEvent
    {
        public Maneuver(
            ShipId shipId,
            ManeuverScheduleEvent maneuverEvent)
            : base(
                shipId,
                maneuverEvent?.Generation
                    ?? throw new ArgumentNullException(nameof(maneuverEvent)))
        {
            Event = maneuverEvent;
        }

        public ManeuverScheduleEvent Event { get; }
    }
}

public sealed record LocalMotionSnapshot(
    MotionId Id,
    EventGeneration Generation,
    SystemPosition Origin,
    SystemPosition Destination,
    SimulationTime DepartedAt,
    SimulationTime ArrivesAt,
    EventKey? CompletionEventKey);

public sealed record ConnectorTransitSnapshot(
    ConnectorTransitId Id,
    EventGeneration Generation,
    TransitConnectionId ConnectionId,
    SystemPosition Source,
    SystemPosition Destination,
    SimulationTime DepartedAt,
    SimulationTime ArrivesAt,
    EventKey? CompletionEventKey);

public sealed record TerminalManeuverSnapshot(
    MotionId MotionId,
    EventGeneration Generation,
    BoundedTerminalPlanKind PlanKind,
    ManeuverObjective Objective,
    int CurrentPhaseIndex,
    ManeuverScheduledPhase? CurrentPhase,
    ManeuverBoundaryDiagnostic? NextBoundary);

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

/// <summary>
/// Result of committing one analytic terminal maneuver. The authoritative
/// schedule is owned immediately, while its future boundaries remain proposals
/// until the agenda owner allocates keys.
/// </summary>
public sealed record TerminalManeuverCommit<TEvent>(
    ScheduledTerminalManeuver Maneuver,
    IReadOnlyList<AgendaEventProposal<TEvent>> EventProposals);

/// <summary>
/// Atomic movement-owner receipt for an invalidated analytic maneuver and its
/// committed replacement. The consuming domain retains reason ownership and
/// uses this exact receipt when buffering semantic facts after physical state
/// has committed.
/// </summary>
public sealed record TerminalManeuverReplacement<TEvent, TReason>(
    MotionId InterruptedMotionId,
    ManeuverObjective InterruptedObjective,
    int InterruptedPhaseIndex,
    ManeuverPhaseKind InterruptedPhaseKind,
    TReason Reason,
    ManeuverInterruption Interruption,
    TerminalManeuverCommit<TEvent> Commit)
    where TReason : struct, Enum;

public abstract record ShipSpatialSnapshotState
{
    private ShipSpatialSnapshotState()
    {
    }

    public sealed record AtPosition(SystemPosition Position) : ShipSpatialSnapshotState;

    public sealed record LocalMotion(
        SystemPosition CurrentPosition,
        LocalMotionSnapshot Motion) : ShipSpatialSnapshotState;

    public sealed record AnalyticManeuver(
        ShipKinematicState CurrentState,
        TerminalManeuverSnapshot Maneuver) : ShipSpatialSnapshotState;

    public sealed record ConnectorTransit(
        ConnectorTransitSnapshot Transit) : ShipSpatialSnapshotState;
}

public sealed record ShipSpatialSnapshot(
    ShipId ShipId,
    ShipVelocity Velocity,
    ShipHeading Heading,
    ShipSpatialSnapshotState State)
{
    public ShipSpatialSnapshot(
        ShipId shipId,
        ShipSpatialSnapshotState state)
        : this(
            shipId,
            ShipVelocity.Zero,
            ShipHeading.Zero,
            state)
    {
    }

    public SystemPosition? Position =>
        State switch
        {
            ShipSpatialSnapshotState.AtPosition atPosition =>
                atPosition.Position,
            ShipSpatialSnapshotState.LocalMotion localMotion =>
                localMotion.CurrentPosition,
            ShipSpatialSnapshotState.AnalyticManeuver maneuver =>
                maneuver.CurrentState.Position,
            ShipSpatialSnapshotState.ConnectorTransit => null,
            _ => throw new InvalidOperationException(
                $"Unsupported spatial snapshot state {State.GetType().Name}."),
        };

    public LocalMotionSnapshot? Motion =>
        (State as ShipSpatialSnapshotState.LocalMotion)?.Motion;

    public TerminalManeuverSnapshot? Maneuver =>
        (State as ShipSpatialSnapshotState.AnalyticManeuver)?.Maneuver;

    public ConnectorTransitSnapshot? Transit =>
        (State as ShipSpatialSnapshotState.ConnectorTransit)?.Transit;
}

/// <summary>
/// Authoritative owner of ship spatial state for compatibility movement,
/// analytic maneuvering, and connector traversal.
/// </summary>
public sealed class SpatialMovement
{
    private readonly SortedDictionary<ShipId, ActorState> _actors =
        new(EntityIdComparer<ShipId>.Instance);
    private readonly IdSequence<MotionId> _motionIds = new();
    private readonly IdSequence<ConnectorTransitId> _transitIds = new();

    /// <summary>
    /// Creates an empty spatial owner with fresh movement identity sequences.
    /// </summary>
    public SpatialMovement()
    {
    }

    private SpatialMovement(
        IdSequence<MotionId> motionIds,
        IdSequence<ConnectorTransitId> transitIds)
    {
        _motionIds = motionIds;
        _transitIds = transitIds;
    }

    /// <summary>
    /// Captures every actor and the exact motion and transit allocator states
    /// at one completed simulation time.
    /// </summary>
    internal CheckpointResult<SpatialMovementCheckpoint> CaptureCheckpoint(
        SimulationTime currentTime)
    {
        IdSequenceCheckpoint motionIds = _motionIds.CaptureCheckpoint();
        IdSequenceCheckpoint transitIds = _transitIds.CaptureCheckpoint();
        var actors = new List<SpatialActorCheckpoint>(_actors.Count);
        foreach ((ShipId shipId, ActorState actor) in _actors)
        {
            ShipSpatialStateCheckpoint state;
            ShipVelocity checkpointVelocity = actor.Velocity;
            ShipHeading checkpointHeading = actor.Heading;
            switch (actor.State)
            {
                case ShipSpatialState.AtPosition atPosition:
                    if (!IsValidPosition(atPosition.Position))
                    {
                        return CaptureRejected(
                            shipId,
                            "position",
                            "A stationary actor has an invalid system position.");
                    }

                    state = new ShipSpatialStateCheckpoint.AtPosition(
                        atPosition.Position);
                    break;
                case ShipSpatialState.Moving moving:
                    LocalMotionSegment motion = moving.Motion;
                    if (motion.Generation != actor.Generation ||
                        !WasAllocated(motionIds, motion.Id.Value) ||
                        !IsValidCompletionKey(
                            motion.CompletionEventKey,
                            motion.ArrivesAt) ||
                        currentTime < motion.DepartedAt ||
                        currentTime >= motion.ArrivesAt)
                    {
                        return CaptureRejected(
                            shipId,
                            "motion",
                            "An active local motion is outside its time range or lacks its exact completion event key.");
                    }

                    state = new ShipSpatialStateCheckpoint.LocalMotion(
                        motion.Id,
                        motion.Generation,
                        motion.Origin,
                        motion.Destination,
                        motion.DepartedAt,
                        motion.ArrivesAt,
                        motion.CompletionEventKey);
                    break;
                case ShipSpatialState.AnalyticManeuver active:
                    ScheduledTerminalManeuver maneuver = active.Maneuver;
                    ManeuverScheduledPhase? currentPhase = maneuver.CurrentPhase;
                    if (maneuver.Generation != actor.Generation
                        || !WasAllocated(motionIds, maneuver.MotionId.Value)
                        || maneuver.IsComplete
                        || maneuver.IsInvalidated
                        || currentPhase is not { } phase
                        || currentTime < phase.StartsAt
                        || currentTime >= phase.EndsAt
                        || maneuver.PendingEvents is not { Count: > 0 } pending
                        || pending.Count
                            != maneuver.Plan.Phases.Count
                                - maneuver.CurrentPhaseIndex)
                    {
                        return CaptureRejected(
                            shipId,
                            "maneuver",
                            "An active analytic maneuver has invalid identity, generation, phase, timing, or pending-event state.");
                    }

                    ShipKinematicState current = maneuver.Plan.StateAt(
                        currentTime);
                    if (!IsValidPosition(current.Position))
                    {
                        return CaptureRejected(
                            shipId,
                            "maneuver.position",
                            "An active analytic maneuver has an invalid current position.");
                    }

                    checkpointVelocity = current.Velocity;
                    checkpointHeading = current.Heading;
                    state = new ShipSpatialStateCheckpoint.AnalyticManeuver(
                        maneuver.MotionId,
                        maneuver.Generation,
                        maneuver.Plan,
                        active.Objective,
                        maneuver.CurrentPhaseIndex,
                        maneuver.WaypointPhaseIndices,
                        pending.Select(static item => item.EventKey));
                    break;
                case ShipSpatialState.ConnectorTransit traversing:
                    ConnectorTransitSegment transit = traversing.Transit;
                    if (transit.Generation != actor.Generation ||
                        !WasAllocated(transitIds, transit.Id.Value) ||
                        !IsValidCompletionKey(
                            transit.CompletionEventKey,
                            transit.ArrivesAt) ||
                        currentTime < transit.DepartedAt ||
                        currentTime >= transit.ArrivesAt)
                    {
                        return CaptureRejected(
                            shipId,
                            "transit",
                            "An active connector transit is outside its time range or lacks its exact completion event key.");
                    }

                    state = new ShipSpatialStateCheckpoint.ConnectorTransit(
                        transit.Id,
                        transit.Generation,
                        transit.ConnectionId,
                        transit.Source,
                        transit.Destination,
                        transit.DepartedAt,
                        transit.ArrivesAt,
                        transit.CompletionEventKey);
                    break;
                default:
                    return CaptureRejected(
                        shipId,
                        "state",
                        "A spatial actor has an unsupported state.");
            }

            actors.Add(new SpatialActorCheckpoint(
                shipId,
                actor.Generation,
                checkpointVelocity,
                checkpointHeading,
                state));
        }

        return CheckpointResult<SpatialMovementCheckpoint>.Success(
            new SpatialMovementCheckpoint(
                motionIds,
                transitIds,
                actors));
    }

    /// <summary>
    /// Validates and directly restores spatial actors and allocator positions
    /// without starting motion, scheduling completion, or allocating identity.
    /// </summary>
    internal static CheckpointResult<SpatialMovement> RestoreCheckpoint(
        SpatialMovementCheckpoint checkpoint,
        SimulationTime currentTime)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CheckpointResult<IdSequence<MotionId>> motionIds =
            IdSequence<MotionId>.RestoreCheckpoint(checkpoint.MotionIds);
        if (!motionIds.IsSuccess)
        {
            return RestoreRejected(
                "motionIds",
                motionIds.Failure!.Message);
        }

        CheckpointResult<IdSequence<ConnectorTransitId>> transitIds =
            IdSequence<ConnectorTransitId>.RestoreCheckpoint(
                checkpoint.TransitIds);
        if (!transitIds.IsSuccess)
        {
            return RestoreRejected(
                "transitIds",
                transitIds.Failure!.Message);
        }

        var restored = new SpatialMovement(motionIds.Value!, transitIds.Value!);
        for (int index = 0; index < checkpoint.Actors.Count; index++)
        {
            SpatialActorCheckpoint? actor = checkpoint.Actors[index];
            if (actor is null || actor.ShipId.Value == 0 || actor.State is null)
            {
                return RestoreRejected(
                    $"actors[{index}]",
                    "A spatial actor has missing or invalid identity or state.");
            }

            if (restored._actors.ContainsKey(actor.ShipId))
            {
                return RestoreRejected(
                    $"actors[{index}].shipId",
                    "Spatial actor identities must be unique.");
            }

            CheckpointValidationFailure? failure = RestoreState(
                checkpoint,
                actor,
                currentTime,
                index,
                out ShipSpatialState? state);
            if (failure is not null)
            {
                return CheckpointResult<SpatialMovement>.Rejected(failure);
            }

            // The generation and active segment are restored together so stale
            // agenda work retains its original comparison boundary.
            restored._actors.Add(
                actor.ShipId,
                new ActorState(
                    actor.Generation,
                    state!,
                    actor.Velocity,
                    actor.Heading));
        }

        return CheckpointResult<SpatialMovement>.Success(restored);
    }

    public void Add(
        ShipId shipId,
        SystemPosition position,
        ShipHeading? heading = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        if (!_actors.TryAdd(
                shipId,
                new ActorState(position, heading ?? ShipHeading.Zero)))
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
            ShipSpatialState.AnalyticManeuver maneuver =>
                maneuver.Maneuver.Plan.StateAt(time).Position,
            ShipSpatialState.ConnectorTransit => null,
            _ => null,
        };
    }

    /// <summary>
    /// Evaluates one actor's complete system-local kinematic state without
    /// mutating its analytic cursor. Connector transit has no local state.
    /// </summary>
    public ShipKinematicState? KinematicStateAt(
        ShipId shipId,
        SimulationTime time)
    {
        ActorState? actor = _actors.GetValueOrDefault(shipId);
        return actor?.State switch
        {
            ShipSpatialState.AtPosition atPosition => new ShipKinematicState(
                atPosition.Position,
                actor.Velocity,
                actor.Heading),
            ShipSpatialState.Moving moving => new ShipKinematicState(
                moving.Motion.PositionAt(time),
                actor.Velocity,
                actor.Heading),
            ShipSpatialState.AnalyticManeuver maneuver =>
                maneuver.Maneuver.Plan.StateAt(time),
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
            actor.Velocity = ShipVelocity.Zero;
            actor.State = new ShipSpatialState.AtPosition(leg.Destination);
            return new LocalMotionCommit<TEvent>(null, null);
        }

        ShipVelocity velocity = CompatibilityVelocity(leg);
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
        actor.Velocity = velocity;
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
    /// Commits an already selected analytic maneuver from the actor's exact
    /// stationary-owner state. The new schedule becomes authoritative before
    /// its proposals receive agenda keys; callers must bind the committed keys
    /// before capture, replacement, or removal.
    /// </summary>
    public TerminalManeuverCommit<TEvent> CommitStartTerminalManeuver<TEvent>(
        ShipId shipId,
        ExecutableBoundedTerminalManeuverPlan plan,
        ManeuverObjective objective,
        SimulationTime now,
        Func<SpatialMovementEvent, TEvent> wrapEvent,
        IEnumerable<int>? waypointPhaseIndices = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        ValidateObjective(objective);
        ActorState actor = GetRequiredActor(shipId);
        if (actor.State is not ShipSpatialState.AtPosition atPosition)
        {
            throw new InvalidOperationException(
                $"Ship {shipId} must be at a system position before starting an analytic maneuver.");
        }

        var current = new ShipKinematicState(
            atPosition.Position,
            actor.Velocity,
            actor.Heading);
        ValidatePlanStart(plan, now, current);

        var maneuver = new ScheduledTerminalManeuver(
            _motionIds.Allocate(),
            actor.Generation,
            plan,
            waypointPhaseIndices);
        IReadOnlyList<AgendaEventProposal<TEvent>> proposals =
            maneuver.CreateRemainingEventProposals(
                shipId,
                maneuverEvent => wrapEvent(
                    new SpatialMovementEvent.Maneuver(
                        shipId,
                        maneuverEvent)));
        actor.Velocity = current.Velocity;
        actor.Heading = current.Heading;
        actor.State = new ShipSpatialState.AnalyticManeuver(
            maneuver,
            objective);
        return new TerminalManeuverCommit<TEvent>(maneuver, proposals);
    }

    /// <summary>
    /// Materializes and invalidates the actor's active analytic schedule, then
    /// commits an already selected replacement from that exact state. The
    /// consuming owner supplies the typed semantic reason and receives the
    /// interrupted motion and phase identity for later fact commit. A failed
    /// cancellation leaves ownership, identity allocation, and actor state
    /// unchanged.
    /// </summary>
    public AgendaCancellationCheck TryReplaceTerminalManeuver<TEvent, TReason>(
        ShipId shipId,
        ExecutableBoundedTerminalManeuverPlan replacementPlan,
        ManeuverObjective objective,
        TReason reason,
        SimulationTime now,
        EventAgenda<TEvent> agenda,
        Func<SpatialMovementEvent, TEvent> wrapEvent,
        out TerminalManeuverReplacement<TEvent, TReason>? replacement)
        where TReason : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(replacementPlan);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        ValidateObjective(objective);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown maneuver-interruption reason.");
        }

        replacement = null;
        ActorState actor = GetRequiredActor(shipId);
        var active = actor.State as ShipSpatialState.AnalyticManeuver
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no analytic maneuver to replace.");
        ManeuverScheduledPhase interruptedPhase = active.Maneuver.CurrentPhase
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no active maneuver phase to replace.");
        int interruptedPhaseIndex = active.Maneuver.CurrentPhaseIndex;
        ShipKinematicState expectedStart = active.Maneuver.Plan.StateAt(now);
        ValidatePlanStart(replacementPlan, now, expectedStart);

        AgendaCancellationCheck cancellation = active.Maneuver.TryInterrupt(
            now,
            agenda,
            maneuverEvent => wrapEvent(
                new SpatialMovementEvent.Maneuver(shipId, maneuverEvent)),
            out ManeuverInterruption? interruption);
        if (cancellation != AgendaCancellationCheck.Matches)
        {
            return cancellation;
        }

        var maneuver = new ScheduledTerminalManeuver(
            _motionIds.Allocate(),
            interruption!.NextGeneration,
            replacementPlan);
        IReadOnlyList<AgendaEventProposal<TEvent>> proposals =
            maneuver.CreateRemainingEventProposals(
                shipId,
                maneuverEvent => wrapEvent(
                    new SpatialMovementEvent.Maneuver(
                        shipId,
                        maneuverEvent)));
        actor.Generation = interruption.NextGeneration;
        actor.Velocity = interruption.MaterializedState.Velocity;
        actor.Heading = interruption.MaterializedState.Heading;
        actor.State = new ShipSpatialState.AnalyticManeuver(
            maneuver,
            objective);
        var commit = new TerminalManeuverCommit<TEvent>(maneuver, proposals);
        replacement = new TerminalManeuverReplacement<TEvent, TReason>(
            active.Maneuver.MotionId,
            active.Objective,
            interruptedPhaseIndex,
            interruptedPhase.Kind,
            reason,
            interruption,
            commit);
        return AgendaCancellationCheck.Matches;
    }

    /// <summary>
    /// Materializes an active analytic maneuver at the agenda's current time,
    /// atomically cancels all later boundaries, and leaves the actor at that
    /// exact position with the next generation. Cancellation mismatch changes
    /// neither owner.
    /// </summary>
    public AgendaCancellationCheck TryInterruptTerminalManeuver<TEvent>(
        ShipId shipId,
        SimulationTime now,
        EventAgenda<TEvent> agenda,
        Func<SpatialMovementEvent, TEvent> wrapEvent,
        out ManeuverInterruption? interruption)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(wrapEvent);
        interruption = null;
        ActorState actor = GetRequiredActor(shipId);
        var active = actor.State as ShipSpatialState.AnalyticManeuver
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no analytic maneuver to interrupt.");
        AgendaCancellationCheck cancellation = active.Maneuver.TryInterrupt(
            now,
            agenda,
            maneuverEvent => wrapEvent(
                new SpatialMovementEvent.Maneuver(shipId, maneuverEvent)),
            out ManeuverInterruption? materialized);
        if (cancellation != AgendaCancellationCheck.Matches)
        {
            return cancellation;
        }

        ManeuverInterruption result = materialized
            ?? throw new InvalidOperationException(
                "A successful maneuver interruption produced no materialized state.");
        actor.Generation = result.NextGeneration;
        actor.Velocity = result.MaterializedState.Velocity;
        actor.Heading = result.MaterializedState.Heading;
        actor.State = new ShipSpatialState.AtPosition(
            result.MaterializedState.Position);
        interruption = result;
        return AgendaCancellationCheck.Matches;
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
            || !ManeuverArrival.IsPositionWithinArrivalTolerance(
                atPosition.Position,
                leg.Origin))
        {
            throw new InvalidOperationException(
                $"Ship {shipId} is not within arrival tolerance of connector origin {leg.Origin}.");
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
        actor.Velocity = ShipVelocity.Zero;
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
    /// Records the key allocated for the active movement completion after the
    /// agenda owner commits its proposal.
    /// </summary>
    internal void BindCompletionEvent(ShipId shipId, EventKey eventKey)
    {
        ActorState actor = GetRequiredActor(shipId);
        switch (actor.State)
        {
            case ShipSpatialState.Moving moving:
                BindCompletionEvent(moving.Motion, eventKey);
                return;
            case ShipSpatialState.ConnectorTransit transit:
                BindCompletionEvent(transit.Transit, eventKey);
                return;
            default:
                throw new InvalidOperationException(
                    $"Ship {shipId} has no active movement to bind to {eventKey}.");
        }
    }

    /// <summary>
    /// Binds every key allocated for the active analytic maneuver after the
    /// agenda owner commits its complete proposal set.
    /// </summary>
    internal void BindTerminalManeuverEvents(
        ShipId shipId,
        IReadOnlyList<EventKey> eventKeys)
    {
        ArgumentNullException.ThrowIfNull(eventKeys);
        ActorState actor = GetRequiredActor(shipId);
        var active = actor.State as ShipSpatialState.AnalyticManeuver
            ?? throw new InvalidOperationException(
                $"Ship {shipId} has no analytic maneuver to bind.");
        active.Maneuver.BindPendingEventKeys(eventKeys);
    }

    /// <summary>
    /// Returns the exact scheduled completion associated with an active actor,
    /// or null when the actor is stationary.
    /// </summary>
    internal PendingMovementCompletion? GetPendingCompletion(ShipId shipId)
    {
        ActorState actor = GetRequiredActor(shipId);
        return actor.State switch
        {
            ShipSpatialState.Moving moving => new PendingMovementCompletion.Arrival(
                shipId,
                moving.Motion.Id,
                moving.Motion.Generation,
                GetRequiredCompletionEventKey(moving.Motion)),
            ShipSpatialState.ConnectorTransit transit => new PendingMovementCompletion.Emergence(
                shipId,
                transit.Transit.Id,
                transit.Transit.Generation,
                GetRequiredCompletionEventKey(transit.Transit)),
            ShipSpatialState.AnalyticManeuver => throw new InvalidOperationException(
                "Analytic maneuvers own multiple pending boundaries."),
            ShipSpatialState.AtPosition => null,
            _ => throw new InvalidOperationException(
                $"Unsupported spatial state {actor.State.GetType().Name}."),
        };
    }

    /// <summary>
    /// Authoritative cleanup commit for an actor whose exact pending movement
    /// completion has already been cancelled by the lifecycle coordinator.
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
                actor.Velocity = ShipVelocity.Zero;
                return ScheduledEventDisposition.Applied;
            case SpatialMovementEvent.Emerge emerge
                when actor.State is ShipSpatialState.ConnectorTransit traversing
                    && traversing.Transit.Id == emerge.TransitId
                    && traversing.Transit.ArrivesAt == now:
                actor.State = new ShipSpatialState.AtPosition(
                    traversing.Transit.Destination);
                actor.Velocity = ShipVelocity.Zero;
                return ScheduledEventDisposition.Applied;
            case SpatialMovementEvent.Maneuver maneuverEvent
                when actor.State is ShipSpatialState.AnalyticManeuver active
                    && active.Maneuver.MotionId
                        == maneuverEvent.Event.MotionId:
                ScheduledEventDisposition disposition =
                    active.Maneuver.HandleEvent(
                        maneuverEvent.Event,
                        scheduledGeneration,
                        now,
                        out ShipKinematicState? materialized);
                if (disposition != ScheduledEventDisposition.Applied)
                {
                    return disposition;
                }

                ShipKinematicState state = materialized
                    ?? throw new InvalidOperationException(
                        "Applied maneuver event produced no kinematic state.");
                actor.Velocity = state.Velocity;
                actor.Heading = state.Heading;
                if (active.Maneuver.IsComplete)
                {
                    actor.State = new ShipSpatialState.AtPosition(
                        state.Position);
                }

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
                        actor.Velocity,
                        actor.Heading,
                        new ShipSpatialSnapshotState.AtPosition(
                            atPosition.Position)));
                    break;
                case ShipSpatialState.Moving moving:
                    LocalMotionSegment motion = moving.Motion;
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        actor.Velocity,
                        actor.Heading,
                        new ShipSpatialSnapshotState.LocalMotion(
                            motion.PositionAt(now),
                            new LocalMotionSnapshot(
                                motion.Id,
                                motion.Generation,
                                motion.Origin,
                                motion.Destination,
                                motion.DepartedAt,
                                motion.ArrivesAt,
                                motion.CompletionEventKey))));
                    break;
                case ShipSpatialState.AnalyticManeuver active:
                    ScheduledTerminalManeuver maneuver = active.Maneuver;
                    ShipKinematicState current = maneuver.Plan.StateAt(now);
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        current.Velocity,
                        current.Heading,
                        new ShipSpatialSnapshotState.AnalyticManeuver(
                            current,
                            new TerminalManeuverSnapshot(
                                maneuver.MotionId,
                                maneuver.Generation,
                                maneuver.Plan.Kind,
                                active.Objective,
                                maneuver.CurrentPhaseIndex,
                                maneuver.CurrentPhase,
                                maneuver.NextBoundary))));
                    break;
                case ShipSpatialState.ConnectorTransit traversing:
                    ConnectorTransitSegment transit = traversing.Transit;
                    snapshots.Add(new ShipSpatialSnapshot(
                        shipId,
                        actor.Velocity,
                        actor.Heading,
                        new ShipSpatialSnapshotState.ConnectorTransit(
                            new ConnectorTransitSnapshot(
                                transit.Id,
                                transit.Generation,
                                transit.ConnectionId,
                                transit.Source,
                                transit.Destination,
                                transit.DepartedAt,
                                transit.ArrivesAt,
                                transit.CompletionEventKey))));
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

    /// <summary>
    /// Gives the compatibility segment an exact fixed-point velocity at its
    /// boundary, using nearest-unit rounding with half units away from zero.
    /// </summary>
    private static ShipVelocity CompatibilityVelocity(TravelLeg.Local leg) =>
        new(
            CompatibilityVelocityComponent(
                leg.Destination.Position.X.Units,
                leg.Origin.Position.X.Units,
                leg.Duration.Milliseconds),
            CompatibilityVelocityComponent(
                leg.Destination.Position.Y.Units,
                leg.Origin.Position.Y.Units,
                leg.Duration.Milliseconds));

    private static long CompatibilityVelocityComponent(
        long destinationMeters,
        long originMeters,
        ulong durationMilliseconds)
    {
        Int128 deltaMeters = (Int128)destinationMeters - originMeters;
        bool negative = deltaMeters < 0;
        UInt128 numerator = (UInt128)(negative ? -deltaMeters : deltaMeters)
            * 1_000_000u;
        UInt128 quotient = numerator / durationMilliseconds;
        UInt128 remainder = numerator % durationMilliseconds;
        if (remainder * 2 >= durationMilliseconds)
        {
            quotient++;
        }

        if (quotient > long.MaxValue)
        {
            throw new OverflowException(
                "The compatibility motion velocity exceeds signed fixed-point range.");
        }

        long magnitude = (long)quotient;
        return negative ? -magnitude : magnitude;
    }

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

    /// <summary>
    /// Binds the already-allocated arrival key only when it names the exact
    /// physical-completion time for this motion.
    /// </summary>
    private static void BindCompletionEvent(
        LocalMotionSegment motion,
        EventKey eventKey)
    {
        if (motion.CompletionEventKey is not null
            || eventKey.Timestamp != motion.ArrivesAt
            || eventKey.Phase != EventPhase.PhysicalCompletion)
        {
            throw new InvalidOperationException(
                $"Arrival event {eventKey} does not match motion {motion.Id}.");
        }

        motion.CompletionEventKey = eventKey;
    }

    /// <summary>
    /// Binds the already-allocated emergence key only when it names the exact
    /// physical-completion time for this transit.
    /// </summary>
    private static void BindCompletionEvent(
        ConnectorTransitSegment transit,
        EventKey eventKey)
    {
        if (transit.CompletionEventKey is not null
            || eventKey.Timestamp != transit.ArrivesAt
            || eventKey.Phase != EventPhase.PhysicalCompletion)
        {
            throw new InvalidOperationException(
                $"Emergence event {eventKey} does not match transit {transit.Id}.");
        }

        transit.CompletionEventKey = eventKey;
    }

    /// <summary>
    /// Retrieves the key required to prove that a moving actor can be removed
    /// without leaving its arrival event behind.
    /// </summary>
    private static EventKey GetRequiredCompletionEventKey(
        LocalMotionSegment motion) =>
        motion.CompletionEventKey
        ?? throw new InvalidOperationException(
            $"Active motion {motion.Id} has no scheduled completion event.");

    /// <summary>
    /// Retrieves the key required to prove that a transiting actor can be
    /// removed without leaving its emergence event behind.
    /// </summary>
    private static EventKey GetRequiredCompletionEventKey(
        ConnectorTransitSegment transit) =>
        transit.CompletionEventKey
        ?? throw new InvalidOperationException(
            $"Active connector transit {transit.Id} has no scheduled completion event.");

    private static void ValidateObjective(ManeuverObjective objective)
    {
        if (!Enum.IsDefined(objective))
        {
            throw new ArgumentOutOfRangeException(
                nameof(objective),
                objective,
                "Unknown maneuver objective.");
        }
    }

    /// <summary>
    /// Requires a selected plan to begin at the commit boundary from the
    /// actor's exact authoritative kinematic state before identity allocation
    /// or schedule mutation occurs.
    /// </summary>
    private static void ValidatePlanStart(
        ExecutableBoundedTerminalManeuverPlan plan,
        SimulationTime now,
        ShipKinematicState expected)
    {
        if (plan.StartsAt != now || plan.StateAt(now) != expected)
        {
            throw new InvalidOperationException(
                "The analytic maneuver plan does not begin from the actor's exact current state and time.");
        }
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

    /// <summary>
    /// Validates one discriminated actor state and reconstructs active segment
    /// objects with their original completion keys.
    /// </summary>
    private static CheckpointValidationFailure? RestoreState(
        SpatialMovementCheckpoint checkpoint,
        SpatialActorCheckpoint actor,
        SimulationTime currentTime,
        int index,
        out ShipSpatialState? restored)
    {
        string path = $"$.checkpoint.spatial.actors[{index}].state";
        restored = null;
        switch (actor.State)
        {
            case ShipSpatialStateCheckpoint.AtPosition atPosition:
                if (!IsValidPosition(atPosition.Position))
                {
                    return new CheckpointValidationFailure(
                        path,
                        "A stationary actor has an invalid system position.");
                }

                restored = new ShipSpatialState.AtPosition(atPosition.Position);
                return null;
            case ShipSpatialStateCheckpoint.LocalMotion motion:
                if (motion.Id.Value == 0 ||
                    !WasAllocated(checkpoint.MotionIds, motion.Id.Value) ||
                    motion.Generation != actor.Generation ||
                    !IsValidPosition(motion.Origin) ||
                    !IsValidPosition(motion.Destination) ||
                    motion.Origin.SystemId != motion.Destination.SystemId ||
                    motion.ArrivesAt <= motion.DepartedAt ||
                    currentTime < motion.DepartedAt ||
                    currentTime >= motion.ArrivesAt ||
                    !IsValidCompletionKey(
                        motion.CompletionEventKey,
                        motion.ArrivesAt))
                {
                    return new CheckpointValidationFailure(
                        path,
                        "An active local motion has invalid identity, generation, position, timing, allocator, or completion-key data.");
                }

                var restoredMotion = new LocalMotionSegment(
                    motion.Id,
                    motion.Generation,
                    motion.Origin,
                    motion.Destination,
                    motion.DepartedAt,
                    motion.ArrivesAt)
                {
                    // The agenda allocated this key before capture. Rebinding
                    // through normal commit code would allocate duplicate work.
                    CompletionEventKey = motion.CompletionEventKey,
                };
                restored = new ShipSpatialState.Moving(restoredMotion);
                return null;
            case ShipSpatialStateCheckpoint.AnalyticManeuver maneuver:
                if (maneuver.Id.Value == 0
                    || !WasAllocated(checkpoint.MotionIds, maneuver.Id.Value)
                    || maneuver.Generation != actor.Generation
                    || maneuver.Plan is null
                    || !Enum.IsDefined(maneuver.Objective)
                    || maneuver.CurrentPhaseIndex < 0
                    || maneuver.CurrentPhaseIndex >= maneuver.Plan.Phases.Count
                    || maneuver.PendingEventKeys.Count
                        != maneuver.Plan.Phases.Count
                            - maneuver.CurrentPhaseIndex)
                {
                    return new CheckpointValidationFailure(
                        path,
                        "An active analytic maneuver has invalid identity, generation, objective, phase, allocator, or pending-key data.");
                }

                ManeuverScheduledPhase phase =
                    maneuver.Plan.Phases[maneuver.CurrentPhaseIndex];
                if (currentTime < phase.StartsAt || currentTime >= phase.EndsAt)
                {
                    return new CheckpointValidationFailure(
                        path,
                        "An active analytic maneuver is outside its current phase time range.");
                }

                ShipKinematicState current;
                ScheduledTerminalManeuver restoredManeuver;
                try
                {
                    current = maneuver.Plan.StateAt(currentTime);
                    restoredManeuver = ScheduledTerminalManeuver.RestoreActive(
                        maneuver.Id,
                        maneuver.Generation,
                        maneuver.Plan,
                        maneuver.CurrentPhaseIndex,
                        maneuver.WaypointPhaseIndices);
                    restoredManeuver.BindPendingEventKeys(
                        maneuver.PendingEventKeys);
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        or InvalidOperationException
                        or OverflowException)
                {
                    return new CheckpointValidationFailure(
                        path,
                        $"An active analytic maneuver cannot be reconstructed: {exception.Message}");
                }

                if (!IsValidPosition(current.Position)
                    || actor.Velocity != current.Velocity
                    || actor.Heading != current.Heading)
                {
                    return new CheckpointValidationFailure(
                        path,
                        "An active analytic maneuver does not reproduce its saved kinematic state.");
                }

                restored = new ShipSpatialState.AnalyticManeuver(
                    restoredManeuver,
                    maneuver.Objective);
                return null;
            case ShipSpatialStateCheckpoint.ConnectorTransit transit:
                if (transit.Id.Value == 0 ||
                    !WasAllocated(checkpoint.TransitIds, transit.Id.Value) ||
                    transit.Generation != actor.Generation ||
                    transit.ConnectionId.Value == 0 ||
                    !IsValidPosition(transit.Source) ||
                    !IsValidPosition(transit.Destination) ||
                    transit.Source.SystemId == transit.Destination.SystemId ||
                    transit.ArrivesAt <= transit.DepartedAt ||
                    currentTime < transit.DepartedAt ||
                    currentTime >= transit.ArrivesAt ||
                    !IsValidCompletionKey(
                        transit.CompletionEventKey,
                        transit.ArrivesAt))
                {
                    return new CheckpointValidationFailure(
                        path,
                        "An active connector transit has invalid identity, generation, connection, position, timing, allocator, or completion-key data.");
                }

                var restoredTransit = new ConnectorTransitSegment(
                    transit.Id,
                    transit.Generation,
                    transit.ConnectionId,
                    transit.Source,
                    transit.Destination,
                    transit.DepartedAt,
                    transit.ArrivesAt)
                {
                    // Preserve the already committed emergence event rather
                    // than scheduling another completion during restore.
                    CompletionEventKey = transit.CompletionEventKey,
                };
                restored = new ShipSpatialState.ConnectorTransit(
                    restoredTransit);
                return null;
            default:
                return new CheckpointValidationFailure(
                    path,
                    "A spatial actor has an unsupported state.");
        }
    }

    private static bool IsValidPosition(SystemPosition position) =>
        position.SystemId.Value != 0;

    private static bool WasAllocated(
        IdSequenceCheckpoint sequence,
        ulong value) =>
        sequence.NextValue is not { } next || value < next;

    private static bool IsValidCompletionKey(
        EventKey? key,
        SimulationTime arrivesAt) =>
        key is { } value &&
        value.Timestamp == arrivesAt &&
        value.Phase == EventPhase.PhysicalCompletion;

    private static CheckpointResult<SpatialMovementCheckpoint> CaptureRejected(
        ShipId shipId,
        string field,
        string message) =>
        CheckpointResult<SpatialMovementCheckpoint>.Rejected(
            new CheckpointValidationFailure(
                $"$.checkpoint.spatial.actors[{shipId.Value}].{field}",
                message));

    private static CheckpointResult<SpatialMovement> RestoreRejected(
        string field,
        string message) =>
        CheckpointResult<SpatialMovement>.Rejected(
            new CheckpointValidationFailure(
                $"$.checkpoint.spatial.{field}",
                message));

    private sealed class ActorState
    {
        public ActorState(SystemPosition position, ShipHeading heading)
        {
            State = new ShipSpatialState.AtPosition(position);
            Heading = heading;
        }

        public ActorState(
            EventGeneration generation,
            ShipSpatialState state,
            ShipVelocity velocity,
            ShipHeading heading)
        {
            Generation = generation;
            State = state;
            Velocity = velocity;
            Heading = heading;
        }

        public EventGeneration Generation { get; set; } = new(0);

        public ShipVelocity Velocity { get; set; }

        public ShipHeading Heading { get; set; }

        public ShipSpatialState State { get; set; }
    }
}

internal abstract record PendingMovementCompletion(
    ShipId ShipId,
    EventGeneration Generation,
    EventKey EventKey)
{
    internal sealed record Arrival(
        ShipId ShipId,
        MotionId MotionId,
        EventGeneration Generation,
        EventKey EventKey)
        : PendingMovementCompletion(ShipId, Generation, EventKey);

    internal sealed record Emergence(
        ShipId ShipId,
        ConnectorTransitId TransitId,
        EventGeneration Generation,
        EventKey EventKey)
        : PendingMovementCompletion(ShipId, Generation, EventKey);
}
