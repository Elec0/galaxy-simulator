using System.Collections.Frozen;

namespace GalaxyCommand.Simulation;

/// <summary>Read-only earliest contact evidence for one scoped target; no interest or gameplay eligibility is implied.</summary>
public sealed record InteractionSweptMatch(ShipId Target, SweptRangeContact Contact);

/// <summary>Immutable caller-scoped swept discovery over a fixed captured interval.</summary>
public static class InteractionSweptQuery
{
    /// <summary>
    /// Prepares current local geometry from the captured timestamp through an explicit end.
    /// Known motion stops at the next scheduled boundary. Earlier ends, null views, and
    /// missing active analytic plans are rejected. No owner state or agenda is changed.
    /// </summary>
    public static PreparedInteractionSweptView Prepare(InteractionSpatialReadView view, SimulationTime through)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, view.Timestamp.Milliseconds, nameof(through));
        return new PreparedInteractionSweptView(view, through);
    }
}

/// <summary>Exact captured trajectories and conservative bounds shared by immutable caller scopes.</summary>
public sealed class PreparedInteractionSweptView
{
    private readonly FrozenDictionary<ShipId, PreparedInteractionTrajectory> _trajectories;

    /// <summary>Captures each local trajectory and its fixed-interval bounds once, without retaining receipts as past geometry.</summary>
    internal PreparedInteractionSweptView(InteractionSpatialReadView view, SimulationTime through)
    {
        From = view.Timestamp;
        Through = through;
        var trajectories = new Dictionary<ShipId, PreparedInteractionTrajectory>();
        foreach (InteractionSpatialParticipant participant in view.Participants)
            if (PreparedInteractionTrajectory.Capture(participant, From, Through) is { } trajectory)
                trajectories.Add(participant.Spatial.ShipId, trajectory);
        _trajectories = trajectories.ToFrozenDictionary();
    }

    public SimulationTime From { get; }
    public SimulationTime Through { get; }

    /// <summary>Copies and validates an explicit scope using the default exact bound scan.</summary>
    public PreparedInteractionSweptScope PrepareScope(IEnumerable<ShipId> scope) =>
        PrepareScope(scope, InteractionPreparationStrategy.Scan);

    /// <summary>
    /// Copies, sorts, and deduplicates nonzero identities, retaining only captured local targets.
    /// Explicit index tuning affects cost only. Sources may be outside this scope; null inputs
    /// are rejected. Later caller or movement-owner mutations cannot change prepared geometry.
    /// </summary>
    public PreparedInteractionSweptScope PrepareScope(IEnumerable<ShipId> scope, InteractionPreparationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(strategy);
        var targets = new List<PreparedInteractionTrajectory>();
        foreach (ShipId id in scope.Distinct().OrderBy(id => id.Value))
        {
            ArgumentOutOfRangeException.ThrowIfZero(id.Value, nameof(scope));
            if (_trajectories.TryGetValue(id, out var trajectory)) targets.Add(trajectory);
        }
        return new PreparedInteractionSweptScope(this, targets.ToArray(), strategy);
    }

    internal PreparedInteractionTrajectory? Get(ShipId id) => _trajectories.GetValueOrDefault(id);
}

/// <summary>Immutable fixed-interval target scope with caller-selected conservative lookup.</summary>
public sealed class PreparedInteractionSweptScope
{
    private static readonly IReadOnlyList<InteractionSweptMatch> Empty = Array.AsReadOnly(Array.Empty<InteractionSweptMatch>());
    private readonly PreparedInteractionSweptView _view;
    private readonly PreparedInteractionTrajectory[] _targets;
    private readonly IInteractionCandidateIndex? _index;

    /// <summary>
    /// Copies sources and runs read-only queries with explicit worker/batch tuning. Results retain
    /// caller source order and duplicates; each inner result retains the ordinary Find contract.
    /// Null inputs are rejected. Evaluation failures rethrow the earliest input failure and
    /// publish no partial outer list. No domain callbacks, scheduling, or effects run on workers.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<InteractionSweptMatch>> FindBatch(IEnumerable<ShipId> sources, ulong rangeUnits,
        InteractionBatchExecution execution)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(execution);
        ShipId[] inputs = sources.ToArray();
        return Array.AsReadOnly(execution.Evaluate(inputs.Length, index => Find(inputs[index], rangeUnits)));
    }

    /// <summary>Builds only the selected derived index; scan reuses prepared bounds without constructing an index.</summary>
    internal PreparedInteractionSweptScope(PreparedInteractionSweptView view, PreparedInteractionTrajectory[] targets,
        InteractionPreparationStrategy strategy)
    {
        _view = view;
        _targets = targets;
        _index = strategy.Kind switch
        {
            InteractionPreparationKind.Scan => null,
            InteractionPreparationKind.SparseGrid => new InteractionGridIndex(targets.Select(target => target.Bounds),
                strategy.CellWidthUnits, strategy.CellBudget),
            InteractionPreparationKind.BoundingBoxHierarchy => new InteractionHierarchyIndex(targets.Select(target => target.Bounds),
                strategy.LeafCapacity),
            _ => throw new InvalidOperationException("Unsupported preparation strategy."),
        };
    }

    /// <summary>
    /// Returns immutable earliest contacts in target identity order over each pair's known overlap.
    /// No match makes no guarantee beyond a scheduled boundary. Zero sources are rejected;
    /// missing, nonlocal, self, and different-system participants are excluded. Queries create
    /// no interests, events, effects, or shared cache mutations.
    /// </summary>
    public IReadOnlyList<InteractionSweptMatch> Find(ShipId source, ulong rangeUnits)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        if (_view.Get(source) is not { } first) return Empty;
        InteractionIndexBox query = first.Bounds.Expand(rangeUnits);
        List<InteractionSweptMatch>? matches = null;
        if (_index is null)
        {
            foreach (PreparedInteractionTrajectory target in _targets)
                if (target.Bounds.Ship != source && target.Bounds.Overlaps(query)) Evaluate(target);
        }
        else
        {
            foreach (ShipId id in _index.Query(query).Candidates)
                if (id != source) Evaluate(_view.Get(id)!);
        }
        return matches is null ? Empty : Array.AsReadOnly(matches.ToArray());

        // Both broad-phase paths preserve ship order and delegate to exactly the same pair solver.
        void Evaluate(PreparedInteractionTrajectory target)
        {
            if (first.Contact(target, rangeUnits) is { } contact)
                (matches ??= []).Add(new InteractionSweptMatch(target.Bounds.Ship, contact));
        }
    }
}

/// <summary>Current local immutable geometry bounded by the captured motion owner's next observable event.</summary>
internal sealed record PreparedInteractionTrajectory(InteractionIndexBox Bounds, SimulationTime From, SimulationTime Through,
    SystemPosition Position, LocalMotionSegment? Linear, ExecutableBoundedTerminalManeuverPlan? Plan,
    AnalyticManeuverSegment? Controlled = null)
{
    /// <summary>
    /// Clips requested geometry to known motion and scheduled boundaries. Inserted events limit
    /// search but preserve original plan curves; missing active plans fail rather than falling back
    /// to rounded published positions. Completion receipts never extend this future interval.
    /// </summary>
    internal static PreparedInteractionTrajectory? Capture(InteractionSpatialParticipant participant, SimulationTime from, SimulationTime through)
    {
        if (participant.Spatial.Position is not { } position) return null;
        LocalMotionSegment? linear = null;
        ExecutableBoundedTerminalManeuverPlan? plan = null;
        AnalyticManeuverSegment? controlled = null;
        if (participant.Spatial.Maneuver is { } maneuver)
        {
            plan = participant.ManeuverPlan ?? throw new InvalidOperationException("Analytic read input is missing its immutable plan geometry.");
            from = new SimulationTime(Math.Max(from.Milliseconds, plan.StartsAt.Milliseconds));
            through = new SimulationTime(Math.Min(through.Milliseconds,
                Math.Min(plan.EndsAt.Milliseconds, maneuver.NextBoundary?.Timestamp.Milliseconds ?? plan.EndsAt.Milliseconds)));
        }
        else if (participant.Spatial.State is ShipSpatialSnapshotState.InteractionControlled active)
        {
            controlled = active.Interval;
            from = new SimulationTime(Math.Max(from.Milliseconds, controlled.StartsAt.Milliseconds));
            through = new SimulationTime(Math.Min(through.Milliseconds, controlled.EndsAt.Milliseconds));
        }
        else if (participant.Spatial.Motion is { } motion)
        {
            linear = new LocalMotionSegment(motion.Id, motion.Generation, motion.Origin, motion.Destination, motion.DepartedAt, motion.ArrivesAt);
            from = new SimulationTime(Math.Max(from.Milliseconds, motion.DepartedAt.Milliseconds));
            through = new SimulationTime(Math.Min(through.Milliseconds, motion.ArrivesAt.Milliseconds));
        }
        if (through < from) return null;
        InteractionSweptBounds? bounds = plan is not null
            ? InteractionSweptBoundsQuery.Find(plan, from, through)
            : InteractionSweptBoundsQuery.Find(linear is not null ? InteractionPhaseCurve.From(linear)
                : controlled is not null ? InteractionPhaseCurve.From(controlled) : InteractionPhaseCurve.Fixed(position, from, through), from, through);
        return bounds is null ? null : new PreparedInteractionTrajectory(
            new InteractionIndexBox(participant.Spatial.ShipId, bounds.System, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY),
            from, through, position, linear, plan, controlled);
    }

    /// <summary>Searches the closed known overlap using existing mixed-motion solvers and original-boundary reset semantics.</summary>
    internal SweptRangeContact? Contact(PreparedInteractionTrajectory other, ulong range)
    {
        var from = new SimulationTime(Math.Max(From.Milliseconds, other.From.Milliseconds));
        var through = new SimulationTime(Math.Min(Through.Milliseconds, other.Through.Milliseconds));
        if (through < from || Bounds.System != other.Bounds.System) return null;
        if (Controlled is not null || other.Controlled is not null)
        {
            PreparedInteractionTrajectory controlled = Controlled is not null ? this : other;
            PreparedInteractionTrajectory target = Controlled is not null ? other : this;
            return target.Controlled is not null
                ? ManeuverSweptRangeQuery.FindFirstContact(controlled.Controlled!, target.Controlled, from, through, range)
                : target.Plan is not null
                    ? ManeuverSweptRangeQuery.FindFirstContact(controlled.Controlled!, target.Plan, from, through, range)
                    : target.Linear is not null
                        ? ManeuverSweptRangeQuery.FindFirstContact(controlled.Controlled!, target.Linear, from, through, range)
                        : ManeuverSweptRangeQuery.FindFirstContact(controlled.Controlled!, target.Position, from, through, range);
        }
        if (Plan is not null || other.Plan is not null)
        {
            PreparedInteractionTrajectory analytic = Plan is not null ? this : other;
            PreparedInteractionTrajectory target = Plan is not null ? other : this;
            return target.Plan is not null
                ? ManeuverSweptRangeQuery.FindFirstContact(analytic.Plan!, target.Plan, from, through, range)
                : target.Linear is not null
                    ? ManeuverSweptRangeQuery.FindFirstContact(analytic.Plan!, target.Linear, from, through, range)
                    : ManeuverSweptRangeQuery.FindFirstContact(analytic.Plan!, target.Position, from, through, range);
        }
        if (Linear is not null && other.Linear is not null)
            return SweptRangeQuery.FindFirstContact(Linear, other.Linear, from, through, range);
        if (Linear is not null || other.Linear is not null)
            return SweptRangeQuery.FindFirstContact(Linear ?? other.Linear!, Linear is null ? Position : other.Position, from, through, range);
        // Fixed pairs have no later earlier contact; one exact instant suffices for the whole captured overlap.
        return InteractionPhaseRangeQuery.Find(InteractionPhaseCurve.Fixed(Position, from, from),
            InteractionPhaseCurve.Fixed(other.Position, from, from), from, from, range);
    }
}
