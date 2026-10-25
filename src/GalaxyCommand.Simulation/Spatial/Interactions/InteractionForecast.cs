using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Derived bounded contact and the immutable inputs that produced it.</summary>
public sealed record InteractionForecast
{
    private readonly InteractionSpatialParticipant? _target;
    private InteractionForecast(InteractionCandidate candidate,
        InteractionSpatialParticipant source, InteractionSpatialParticipant? target,
        SimulationTime from, SimulationTime through)
    {
        Candidate = candidate;
        Source = source;
        _target = target;
        From = from;
        Through = through;
    }

    public InteractionCandidate Candidate { get; }
    public InteractionSpatialParticipant Source { get; }
    public InteractionSpatialParticipant Target => _target
        ?? throw new InvalidOperationException("A stationary target is not a spatial ship participant.");
    internal SpatialRevision TargetRevision => _target?.Revision
        ?? ((StationaryInteractionInterest)Candidate.Interest).Revision;
    public SimulationTime From { get; }
    public SimulationTime Through { get; }

    /// <summary>Formats stationary evidence without requesting a fictitious target participant.</summary>
    public override string ToString() =>
        $"InteractionForecast {{ Candidate = {Candidate}, Source = {Source}, Target = {_target}, From = {From}, Through = {Through} }}";

    /// <summary>
    /// Evaluates an explicit pair over a bounded stable view without scheduling
    /// or effects. Missing, nonlocal, or different-system participants return
    /// null. Analytic geometry is immutable and horizons stop at the next scheduled boundary.
    /// </summary>
    public static InteractionForecast? Evaluate(ShipPairInteractionInterest interest,
        InteractionSpatialReadView view, SimulationTime through)
    {
        ArgumentNullException.ThrowIfNull(interest);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds,
            view.Timestamp.Milliseconds, nameof(through));

        InteractionSpatialParticipant? source = view.Get(interest.Source);
        InteractionSpatialParticipant? target = view.Get(interest.Target);
        if (source?.Spatial.Position is not { } sourcePosition
            || target?.Spatial.Position is not { } targetPosition
            || sourcePosition.SystemId != targetPosition.SystemId)
        {
            return null;
        }

        if (source.Spatial.State is ShipSpatialSnapshotState.InteractionControlled ||
            target.Spatial.State is ShipSpatialSnapshotState.InteractionControlled)
        {
            var firstTrajectory = PreparedInteractionTrajectory.Capture(source, view.Timestamp, through);
            var secondTrajectory = PreparedInteractionTrajectory.Capture(target, view.Timestamp, through);
            if (firstTrajectory is null || secondTrajectory is null) return null;
            SweptRangeContact? controlledContact = firstTrajectory.Contact(secondTrajectory, interest.RangeUnits);
            return controlledContact is null ? null : new InteractionForecast(
                new InteractionCandidate(sourcePosition.SystemId, interest, controlledContact), source, target,
                new SimulationTime(Math.Max(firstTrajectory.From.Milliseconds, secondTrajectory.From.Milliseconds)),
                new SimulationTime(Math.Min(firstTrajectory.Through.Milliseconds, secondTrajectory.Through.Milliseconds)));
        }

        LocalMotionSegment? first = Segment(source.Spatial.Motion);
        LocalMotionSegment? second = Segment(target.Spatial.Motion);
        ulong start = view.Timestamp.Milliseconds;
        ulong end = through.Milliseconds;
        foreach (LocalMotionSegment? motion in new[] { first, second })
        {
            if (motion is not null)
            {
                start = Math.Max(start, motion.DepartedAt.Milliseconds);
                end = Math.Min(end, motion.ArrivesAt.Milliseconds);
            }
        }

        foreach (InteractionSpatialParticipant participant in new[] { source, target })
        {
            if (participant.Spatial.Maneuver is { } maneuver)
            {
                if (participant.ManeuverPlan is not { } plan)
                    throw new InvalidOperationException("Analytic read input is missing its immutable plan geometry.");
                start = Math.Max(start, plan.StartsAt.Milliseconds);
                end = Math.Min(end, maneuver.NextBoundary?.Timestamp.Milliseconds ?? plan.EndsAt.Milliseconds);
            }
        }

        if (end < start) return null;
        var from = new SimulationTime(start);
        var horizon = new SimulationTime(end);
        SweptRangeContact? contact;
        if (source.ManeuverPlan is not null || target.ManeuverPlan is not null)
        {
            ExecutableBoundedTerminalManeuverPlan plan = source.ManeuverPlan ?? target.ManeuverPlan!;
            ExecutableBoundedTerminalManeuverPlan? otherPlan = source.ManeuverPlan is not null ? target.ManeuverPlan : source.ManeuverPlan;
            LocalMotionSegment? otherMotion = source.ManeuverPlan is not null ? second : first;
            SystemPosition stationary = source.ManeuverPlan is not null ? targetPosition : sourcePosition;
            contact = otherPlan is not null
                ? ManeuverSweptRangeQuery.FindFirstContact(plan, otherPlan, from, horizon, interest.RangeUnits)
                : otherMotion is not null
                    ? ManeuverSweptRangeQuery.FindFirstContact(plan, otherMotion, from, horizon, interest.RangeUnits)
                    : ManeuverSweptRangeQuery.FindFirstContact(plan, stationary, from, horizon, interest.RangeUnits);
        }
        else if (first is not null && second is not null)
        {
            contact = SweptRangeQuery.FindFirstContact(first, second, from, horizon, interest.RangeUnits);
        }
        else if (first is not null || second is not null)
        {
            contact = SweptRangeQuery.FindFirstContact(first ?? second!,
                first is null ? sourcePosition : targetPosition, from, horizon, interest.RangeUnits);
        }
        else
        {
            BigInteger x = (BigInteger)sourcePosition.Position.X.Units - targetPosition.Position.X.Units;
            BigInteger y = (BigInteger)sourcePosition.Position.Y.Units - targetPosition.Position.Y.Units;
            BigInteger radius = interest.RangeUnits;
            contact = x * x + y * y <= radius * radius ? new SweptRangeContact(from, true, true) : null;
        }

        return contact is null ? null : new InteractionForecast(
            new InteractionCandidate(sourcePosition.SystemId, interest, contact), source, target, from, horizon);
    }

    /// <summary>
    /// Evaluates either target category through the same immutable geometry and agenda path.
    /// Stationary target liveness belongs to the domain that registers and invalidates its intent.
    /// </summary>
    public static InteractionForecast? Evaluate(SpatialInteractionInterest interest,
        InteractionSpatialReadView view, SimulationTime through)
    {
        ArgumentNullException.ThrowIfNull(interest);
        ArgumentNullException.ThrowIfNull(view);
        if (interest is ShipPairInteractionInterest pair) return Evaluate(pair, view, through);
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, view.Timestamp.Milliseconds, nameof(through));
        if (interest is not StationaryInteractionInterest stationary)
            throw new ArgumentException("Unsupported interaction target category.", nameof(interest));
        InteractionSpatialParticipant? source = view.Get(interest.Source);
        if (source is null || PreparedInteractionTrajectory.Capture(source, view.Timestamp, through) is not { } trajectory
            || trajectory.Position.SystemId != stationary.Position.SystemId) return null;
        SweptRangeContact? contact = trajectory.Contact(stationary.Position, interest.RangeUnits);
        return contact is null ? null : new InteractionForecast(
            new InteractionCandidate(stationary.Position.SystemId, stationary, contact), source, null,
            trajectory.From, trajectory.Through);
    }

    /// <summary>
    /// Copies explicit interest inputs and evaluates them against one captured view with explicit
    /// worker/batch tuning. Reduces complete forecasts by canonical key, rejecting conflicts.
    /// Null inputs and earlier horizons are rejected. Evaluation failures rethrow the earliest
    /// caller-input failure; no partial results, scheduling, or owner acknowledgement is published.
    /// </summary>
    public static IReadOnlyList<InteractionForecast> EvaluateBatch(IEnumerable<ShipPairInteractionInterest> interests,
        InteractionSpatialReadView view, SimulationTime through, InteractionBatchExecution execution)
    {
        ArgumentNullException.ThrowIfNull(interests);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, view.Timestamp.Milliseconds, nameof(through));
        ShipPairInteractionInterest[] inputs = interests.ToArray();
        InteractionForecast?[] evaluated = execution.Evaluate(inputs.Length, index => Evaluate(inputs[index], view, through));
        var unique = new SortedDictionary<InteractionCandidateKey, InteractionForecast>();
        foreach (InteractionForecast forecast in evaluated.OfType<InteractionForecast>())
        {
            if (unique.TryGetValue(forecast.Candidate.Key, out var existing) && existing != forecast)
                throw new InvalidOperationException("Conflicting forecasts for one candidate key.");
            unique[forecast.Candidate.Key] = forecast;
        }
        return Array.AsReadOnly(unique.Values.ToArray());
    }

    /// <summary>
    /// Checks due correlation without effects. Applied means valid contact
    /// evidence, not gameplay eligibility. A matching movement-boundary receipt preserves
    /// swept evidence after physical completion at the due timestamp.
    /// </summary>
    public ScheduledEventDisposition Validate(SpatialInteractionInterest? currentInterest,
        InteractionSpatialReadView view, EventGeneration scheduledGeneration)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (scheduledGeneration != Candidate.Interest.Generation || view.Timestamp != Candidate.Key.Timestamp)
            return ScheduledEventDisposition.IgnoredStateMismatch;
        if (currentInterest is null) return ScheduledEventDisposition.IgnoredMissingReference;
        if (currentInterest != Candidate.Interest) return ScheduledEventDisposition.IgnoredStaleGeneration;
        InteractionSpatialParticipant? source = view.Get(currentInterest.Source);
        if (source is null) return ScheduledEventDisposition.IgnoredMissingReference;
        if (currentInterest is StationaryInteractionInterest)
            return Matches(Source, source, view.Timestamp)
                ? ScheduledEventDisposition.Applied : ScheduledEventDisposition.IgnoredStaleGeneration;
        InteractionSpatialParticipant? target = view.Get(((ShipPairInteractionInterest)currentInterest).Target);
        if (target is null) return ScheduledEventDisposition.IgnoredMissingReference;
        return Matches(Source, source, view.Timestamp) && Matches(Target, target, view.Timestamp)
            ? ScheduledEventDisposition.Applied : ScheduledEventDisposition.IgnoredStaleGeneration;
    }

    /// <summary>Copies the authoritative linear trajectory without agenda metadata mutation.</summary>
    private static LocalMotionSegment? Segment(LocalMotionSnapshot? motion) => motion is null ? null : new(
        motion.Id, motion.Generation, motion.Origin, motion.Destination, motion.DepartedAt, motion.ArrivesAt);

    /// <summary>
    /// Accepts an unchanged trajectory or an exact same-time applied boundary
    /// for that trajectory. Interruption or a return to the same position cannot
    /// fabricate the movement owner's successful-boundary evidence.
    /// </summary>
    private static bool Matches(InteractionSpatialParticipant expected,
        InteractionSpatialParticipant current, SimulationTime now)
    {
        if (expected.Revision == current.Revision) return true;
        if (expected.Spatial.State is ShipSpatialSnapshotState.InteractionControlled controlled)
            return current.InteractionReceipts?.Any(receipt => receipt.Timestamp == now &&
                receipt.PreviousRevision == expected.Revision && receipt.Owner == controlled.Owner &&
                ReferenceEquals(receipt.Interval, controlled.Interval)) == true;
        if (expected.Spatial.Maneuver is { } maneuver)
            return current.ManeuverReceipts?.Any(receipt => receipt.Timestamp == now
                && receipt.PreviousRevision == expected.Revision && receipt.MotionId == maneuver.MotionId
                && receipt.Generation == maneuver.Generation && receipt.PhaseIndex == maneuver.CurrentPhaseIndex
                && ReferenceEquals(receipt.Plan, expected.ManeuverPlan)) == true;
        if (expected.Spatial.Motion is not { } motion || current.Completion is not { } receipt
            || receipt.Timestamp != now || receipt.PreviousRevision != expected.Revision)
            return false;

        LocalMotionSnapshot completed = receipt.Motion;
        return completed.Id == motion.Id && completed.Generation == motion.Generation
            && completed.Origin == motion.Origin && completed.Destination == motion.Destination
            && completed.DepartedAt == motion.DepartedAt && completed.ArrivesAt == motion.ArrivesAt;
    }
}
