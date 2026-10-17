using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>Caller-owned activity identity; allocation and gameplay membership remain in its domain.</summary>
public readonly record struct InteractionMotionOwner
{
    public InteractionMotionOwner(string domainKind, ulong activityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        ArgumentOutOfRangeException.ThrowIfZero(activityId);
        DomainKind = domainKind;
        ActivityId = activityId;
    }

    public string DomainKind { get; }
    public ulong ActivityId { get; }
}

/// <summary>Exact owner proof that a controlled interval completed at this timestamp.</summary>
public sealed record InteractionMotionCompletionReceipt(InteractionMotionOwner Owner,
    AnalyticManeuverSegment Interval, SpatialRevision PreviousRevision, SpatialRevision CompletedRevision,
    SimulationTime Timestamp);

/// <summary>One participant's exact interval and expected movement revision in an ownership batch.</summary>
public sealed record InteractionMotionRequest(ShipId ShipId, AnalyticManeuverSegment Interval,
    SpatialRevision ExpectedRevision);

/// <summary>One participant's expected movement revision in an ownership release batch.</summary>
public sealed record InteractionMotionReleaseRequest(ShipId ShipId, SpatialRevision ExpectedRevision);

public sealed partial class SpatialMovement
{
    /// <summary>
    /// Claims a stationary handoff or replaces the same owner's interval. Expected revision
    /// prevents stale ownership updates. Control is caller-supplied; no steps or IDs are allocated.
    /// </summary>
    public bool TryCommitInteractionMotion(ShipId shipId, InteractionMotionOwner owner,
        AnalyticManeuverSegment interval, SpatialRevision expectedRevision, SimulationTime now)
    {
        ValidateInteractionOwner(owner);
        PreparedInteractionMotion? prepared = PrepareInteractionMotion(shipId, owner, interval, expectedRevision, now);
        if (prepared is null) return false;
        CommitInteractionMotion(prepared, now);
        return true;
    }

    /// <summary>
    /// Atomically claims or updates all requested participants for one owner at an exclusive
    /// movement commit boundary. Missing actors, stale revisions, or conflicting ownership
    /// return false. Invalid intervals, duplicate/zero IDs, or exhausted counters throw.
    /// All validation precedes mutation; successful commits follow ship identity order.
    /// Empty input succeeds without changes. This does not interrupt navigation schedules.
    /// </summary>
    public bool TryCommitInteractionMotions(IEnumerable<InteractionMotionRequest> requests,
        InteractionMotionOwner owner, SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ValidateInteractionOwner(owner);
        InteractionMotionRequest[] captured = requests.ToArray();
        foreach (InteractionMotionRequest request in captured)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Interval);
            ArgumentOutOfRangeException.ThrowIfZero(request.ShipId.Value);
        }
        Array.Sort(captured, (left, right) => left.ShipId.Value.CompareTo(right.ShipId.Value));
        for (int index = 1; index < captured.Length; index++)
            if (captured[index].ShipId == captured[index - 1].ShipId)
                throw new ArgumentException("Interaction participants must be distinct.", nameof(requests));

        var prepared = new List<PreparedInteractionMotion>(captured.Length);
        foreach (InteractionMotionRequest request in captured)
        {
            PreparedInteractionMotion? item = PrepareInteractionMotion(request.ShipId, owner,
                request.Interval, request.ExpectedRevision, now);
            if (item is null) return false;
            prepared.Add(item);
        }

        // No caller code, geometry evaluation, or counter increments run after preflight.
        // Exclusive owner commit keeps the prepared actor references authoritative.
        foreach (PreparedInteractionMotion item in prepared)
            CommitInteractionMotion(item, now);
        return true;
    }

    /// <summary>Prepares exact geometry and counter capacity without changing state or publishing evidence.</summary>
    private PreparedInteractionMotion? PrepareInteractionMotion(ShipId shipId, InteractionMotionOwner owner,
        AnalyticManeuverSegment interval, SpatialRevision expectedRevision, SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(interval);
        if (!_actors.TryGetValue(shipId, out ActorState? actor) || actor.SpatialRevision != expectedRevision)
            return null;
        if (actor.State is not ShipSpatialState.AtPosition &&
            (actor.State is not ShipSpatialState.InteractionControlled controlled || controlled.Owner != owner))
            return null;
        ValidateInteractionInterval(interval, KinematicStateAt(shipId, now)!.Value, now);
        return new PreparedInteractionMotion(shipId, actor, expectedRevision, actor.SpatialRevision.Next(),
            actor.Generation.Next(), new ShipSpatialState.InteractionControlled(owner, interval),
            actor.State as ShipSpatialState.InteractionControlled);
    }

    /// <summary>Checks exact continuity and endpoint arithmetic before either direct entry or analytic handoff.</summary>
    private static void ValidateInteractionInterval(AnalyticManeuverSegment interval, ShipKinematicState current, SimulationTime now)
    {
        if (interval.StartsAt != now || interval.Start != current)
            throw new ArgumentException("An interaction interval must start at the exact current kinematic state.", nameof(interval));
        _ = interval.StateAt(interval.EndsAt);
        if (!IsValidPosition(interval.Start.Position))
            throw new ArgumentException("An interaction interval requires a valid local position.", nameof(interval));
    }

    private sealed record PreparedInteractionMotion(ShipId ShipId, ActorState Actor, SpatialRevision PreviousRevision,
        SpatialRevision Revision, EventGeneration Generation, ShipSpatialState.InteractionControlled Next,
        ShipSpatialState.InteractionControlled? Previous);

    private void CommitInteractionMotion(PreparedInteractionMotion prepared, SimulationTime now)
    {
        ActorState actor = prepared.Actor;
        CommitSpatialRevision(prepared.ShipId, actor, prepared.Revision, now);
        RecordInteractionCompletion(actor, prepared.Previous, prepared.PreviousRevision, prepared.Revision, now);
        actor.Generation = prepared.Generation;
        actor.Velocity = prepared.Next.Interval.Start.Velocity;
        actor.Heading = prepared.Next.Interval.Start.Heading;
        actor.State = prepared.Next;
    }

    /// <summary>Releases only the matching owner/revision and materializes exact state without choosing a replacement plan.</summary>
    public bool TryReleaseInteractionMotion(ShipId shipId, InteractionMotionOwner owner,
        SpatialRevision expectedRevision, SimulationTime now, out ShipKinematicState? released)
    {
        ValidateInteractionOwner(owner);
        released = null;
        PreparedInteractionRelease? prepared = PrepareInteractionRelease(shipId, owner, expectedRevision, now);
        if (prepared is null) return false;
        CommitInteractionRelease(prepared, now);
        released = prepared.State;
        return true;
    }

    /// <summary>
    /// Releases a complete batch for one owner at an exclusive movement commit boundary.
    /// Missing actors, stale revisions, or mismatched ownership return false with null results.
    /// Invalid input, out-of-bounds evaluation, or exhausted counters throw before mutation.
    /// Success returns immutable identity-ordered exact kinematics, without choosing new plans.
    /// Empty input succeeds with empty results. Only exact-end releases retain completion proof.
    /// </summary>
    public bool TryReleaseInteractionMotions(IEnumerable<InteractionMotionReleaseRequest> requests,
        InteractionMotionOwner owner, SimulationTime now,
        out IReadOnlyDictionary<ShipId, ShipKinematicState>? released)
    {
        released = null;
        ArgumentNullException.ThrowIfNull(requests);
        ValidateInteractionOwner(owner);
        InteractionMotionReleaseRequest[] captured = requests.ToArray();
        foreach (InteractionMotionReleaseRequest request in captured)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfZero(request.ShipId.Value);
        }
        Array.Sort(captured, (left, right) => left.ShipId.Value.CompareTo(right.ShipId.Value));
        for (int index = 1; index < captured.Length; index++)
            if (captured[index].ShipId == captured[index - 1].ShipId)
                throw new ArgumentException("Interaction participants must be distinct.", nameof(requests));

        var prepared = new List<PreparedInteractionRelease>(captured.Length);
        var states = new SortedDictionary<ShipId, ShipKinematicState>(EntityIdComparer<ShipId>.Instance);
        foreach (InteractionMotionReleaseRequest request in captured)
        {
            PreparedInteractionRelease? item = PrepareInteractionRelease(request.ShipId, owner, request.ExpectedRevision, now);
            if (item is null) return false;
            prepared.Add(item);
            states.Add(item.ShipId, item.State);
        }
        var results = new ReadOnlyDictionary<ShipId, ShipKinematicState>(states);

        // Materialization and counter checks must finish for every ship before
        // any ownership or same-timestamp completion evidence is published.
        foreach (PreparedInteractionRelease item in prepared)
            CommitInteractionRelease(item, now);
        released = results;
        return true;
    }

    /// <summary>Prepares an exact release and both counters without changing ownership or receipts.</summary>
    private PreparedInteractionRelease? PrepareInteractionRelease(ShipId shipId, InteractionMotionOwner owner,
        SpatialRevision expectedRevision, SimulationTime now)
    {
        if (!_actors.TryGetValue(shipId, out ActorState? actor) || actor.SpatialRevision != expectedRevision ||
            actor.State is not ShipSpatialState.InteractionControlled controlled || controlled.Owner != owner)
            return null;
        ShipKinematicState state = controlled.Interval.StateAt(now);
        return new PreparedInteractionRelease(shipId, actor, controlled, expectedRevision,
            actor.SpatialRevision.Next(), actor.Generation.Next(), state, new ShipSpatialState.AtPosition(state.Position));
    }

    private sealed record PreparedInteractionRelease(ShipId ShipId, ActorState Actor,
        ShipSpatialState.InteractionControlled Previous, SpatialRevision PreviousRevision,
        SpatialRevision Revision, EventGeneration Generation, ShipKinematicState State, ShipSpatialState.AtPosition Next);

    private void CommitInteractionRelease(PreparedInteractionRelease prepared, SimulationTime now)
    {
        ActorState actor = prepared.Actor;
        CommitSpatialRevision(prepared.ShipId, actor, prepared.Revision, now);
        RecordInteractionCompletion(actor, prepared.Previous, prepared.PreviousRevision, prepared.Revision, now);
        actor.Generation = prepared.Generation;
        actor.State = prepared.Next;
        actor.Velocity = prepared.State.Velocity;
        actor.Heading = prepared.State.Heading;
    }

    private static void ValidateInteractionOwner(InteractionMotionOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.DomainKind);
        ArgumentOutOfRangeException.ThrowIfZero(owner.ActivityId);
    }

    private static void RecordInteractionCompletion(ActorState actor, ShipSpatialState.InteractionControlled? previous,
        SpatialRevision oldRevision, SpatialRevision newRevision, SimulationTime now)
    {
        // Only exact interval completion preserves swept evidence. Mid-interval
        // replacement/release is an interruption, not a completion receipt.
        if (previous is not null && previous.Interval.EndsAt == now)
            actor.InteractionReceipts.Add(new InteractionMotionCompletionReceipt(previous.Owner,
                previous.Interval, oldRevision, newRevision, now));
    }
}
