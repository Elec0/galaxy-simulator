using System.Collections.Frozen;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Explicit benchmark fixtures: standalone phase curves or whole plans, plus stationary targets.</summary>
internal abstract record PrototypeSweepMotion
{
    internal sealed record Curve(InteractionPhaseCurve Geometry) : PrototypeSweepMotion;
    internal sealed record Plan(ExecutableBoundedTerminalManeuverPlan Geometry) : PrototypeSweepMotion;
    internal sealed record Stationary(SystemPosition Position) : PrototypeSweepMotion;
}
internal sealed record PrototypeSweepTrajectory(ShipId Ship, PrototypeSweepMotion Motion);
internal sealed record PrototypeSweepMatch(ShipId Ship, SweptRangeContact Contact);
internal sealed record PrototypeSweepResult(IReadOnlyList<PrototypeSweepMatch> Matches, long Candidates, long BoxTests);

/// <summary>Benchmark-only interval preparation and scoped swept discovery with unchanged exact query APIs.</summary>
internal sealed class PrototypeSweptQueries
{
    private readonly FrozenDictionary<ShipId, PrototypeSweepTrajectory> _inputs;
    private readonly FrozenDictionary<ShipId, PrototypeBox> _bounds;
    private readonly PrototypeBox[] _targets;
    private readonly IInteractionIndexPrototype? _index;
    private readonly SimulationTime _from;
    private readonly SimulationTime _through;

    /// <summary>
    /// Copies inputs and prepares conservative bounds once for the explicit
    /// interval. Standalone curves and whole plans are separate fixture kinds;
    /// unsupported mixed inputs are rejected rather than inventing a query contract.
    /// </summary>
    internal PrototypeSweptQueries(IEnumerable<PrototypeSweepTrajectory> trajectories, IEnumerable<ShipId> scope,
        SimulationTime from, SimulationTime through, string kind, long width = 16, int leafSize = 4)
    {
        ArgumentNullException.ThrowIfNull(trajectories);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds);
        _from = from;
        _through = through;
        _inputs = trajectories.ToFrozenDictionary(item => item.Ship);
        foreach (ShipId id in _inputs.Keys) ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        if (_inputs.Values.Any(item => item.Motion is PrototypeSweepMotion.Curve)
            && _inputs.Values.Any(item => item.Motion is PrototypeSweepMotion.Plan))
            throw new ArgumentException("Standalone phase curves and whole plans must use separate benchmark fixtures.", nameof(trajectories));
        _bounds = _inputs.Values.Select(item => Bounds(item, from, through)).OfType<PrototypeBox>()
            .ToFrozenDictionary(box => box.Ship);
        ShipId[] ids = scope.Distinct().OrderBy(id => id.Value).ToArray();
        foreach (ShipId id in ids) ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        _targets = ids.Select(id => _bounds.GetValueOrDefault(id)).OfType<PrototypeBox>().ToArray();
        _index = kind switch
        {
            "scan" => null,
            "grid" => new SparseGridPrototype(_targets, width),
            "hierarchy" => new BoundingHierarchyPrototype(_targets, leafSize),
            _ => throw new ArgumentException("Unknown prototype kind.", nameof(kind)),
        };
    }

    internal long Entries => _index?.Entries ?? 0;
    internal long Nodes => _index?.Nodes ?? 0;
    internal long OverflowEntries => _index is SparseGridPrototype grid ? grid.OverflowEntries : 0;

    /// <summary>
    /// All modes reject the same closed boxes before exact contact evaluation.
    /// Index traversal changes box-test cost, never target scope, contact evidence, or ordering.
    /// </summary>
    internal PrototypeSweepResult Query(ShipId source, ulong range)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        if (!_bounds.TryGetValue(source, out PrototypeBox? first)) return new(Array.Empty<PrototypeSweepMatch>(), 0, 0);
        PrototypeBox query = first.Expand(range);
        IReadOnlyList<ShipId> candidates;
        long boxTests = 0;
        if (_index is null)
        {
            var found = new List<ShipId>();
            foreach (PrototypeBox target in _targets)
            {
                if (target.Ship == source || target.System != first.System) continue;
                boxTests++;
                if (target.Overlaps(query)) found.Add(target.Ship);
            }
            candidates = found;
        }
        else
        {
            PrototypeQueryResult broad = _index.Query(query);
            candidates = broad.Candidates;
            boxTests = broad.Visited;
        }
        var matches = new List<PrototypeSweepMatch>();
        long exact = 0;
        foreach (ShipId target in candidates)
        {
            if (target == source) continue;
            exact++;
            if (Contact(_inputs[source].Motion, _inputs[target].Motion, _from, _through, range) is { } contact)
                matches.Add(new PrototypeSweepMatch(target, contact));
        }
        return new(Array.AsReadOnly(matches.ToArray()), exact, boxTests);
    }

    /// <summary>Reuses outward arbitrary-width extrema and whole-plan boundary resets; never samples or extrapolates.</summary>
    private static PrototypeBox? Bounds(PrototypeSweepTrajectory input, SimulationTime from, SimulationTime through)
    {
        InteractionSweptBounds? bounds = input.Motion switch
        {
            PrototypeSweepMotion.Curve curve => InteractionSweptBoundsQuery.Find(curve.Geometry, from, through),
            PrototypeSweepMotion.Plan plan => InteractionSweptBoundsQuery.Find(plan.Geometry, from, through),
            PrototypeSweepMotion.Stationary fixedPoint => InteractionSweptBoundsQuery.Find(
                InteractionPhaseCurve.Fixed(fixedPoint.Position, from, through), from, through),
            _ => throw new InvalidOperationException("Unsupported fixture motion."),
        };
        return bounds is null ? null : new PrototypeBox(input.Ship, bounds.System, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
    }

    /// <summary>Dispatches the supported immutable fixture shapes to the existing exact phase and whole-plan queries.</summary>
    private static SweptRangeContact? Contact(PrototypeSweepMotion first, PrototypeSweepMotion second,
        SimulationTime from, SimulationTime through, ulong range) => (first, second) switch
    {
        (PrototypeSweepMotion.Curve a, PrototypeSweepMotion.Curve b) => InteractionPhaseRangeQuery.Find(a.Geometry, b.Geometry, from, through, range),
        (PrototypeSweepMotion.Plan a, PrototypeSweepMotion.Plan b) => ManeuverSweptRangeQuery.FindFirstContact(a.Geometry, b.Geometry, from, through, range),
        (PrototypeSweepMotion.Plan a, PrototypeSweepMotion.Stationary b) => ManeuverSweptRangeQuery.FindFirstContact(a.Geometry, b.Position, from, through, range),
        (PrototypeSweepMotion.Stationary a, PrototypeSweepMotion.Plan b) => ManeuverSweptRangeQuery.FindFirstContact(b.Geometry, a.Position, from, through, range),
        (PrototypeSweepMotion.Curve a, PrototypeSweepMotion.Stationary b) => InteractionPhaseRangeQuery.Find(a.Geometry,
            InteractionPhaseCurve.Fixed(b.Position, from, through), from, through, range),
        (PrototypeSweepMotion.Stationary a, PrototypeSweepMotion.Curve b) => InteractionPhaseRangeQuery.Find(
            InteractionPhaseCurve.Fixed(a.Position, from, through), b.Geometry, from, through, range),
        (PrototypeSweepMotion.Stationary a, PrototypeSweepMotion.Stationary b) => InteractionPhaseRangeQuery.Find(
            InteractionPhaseCurve.Fixed(a.Position, from, through), InteractionPhaseCurve.Fixed(b.Position, from, through), from, through, range),
        _ => throw new InvalidOperationException("Unsupported fixture motion pair."),
    };
}
