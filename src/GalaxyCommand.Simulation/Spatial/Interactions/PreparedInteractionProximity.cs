using System.Collections.Frozen;
using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Immutable exact local positions prepared once from one captured spatial view.</summary>
public sealed class PreparedInteractionProximityView
{
    private readonly FrozenDictionary<ShipId, PreparedInteractionPoint> _points;

    /// <summary>Materializes only current local geometry, retaining no movement mutation authority.</summary>
    internal PreparedInteractionProximityView(InteractionSpatialReadView view)
    {
        Timestamp = view.Timestamp;
        var points = new Dictionary<ShipId, PreparedInteractionPoint>();
        foreach (InteractionSpatialParticipant participant in view.Participants)
        {
            if (InteractionProximityQuery.Instant(participant, Timestamp) is { } curve)
                points.Add(participant.Spatial.ShipId, PreparedInteractionPoint.Capture(curve, Timestamp));
        }
        _points = points.ToFrozenDictionary();
    }

    public SimulationTime Timestamp { get; }

    /// <summary>
    /// Copies, validates, sorts, and deduplicates a caller scope for repeated
    /// queries. Only scoped local targets are retained; source lookup may use
    /// any ship from this captured view. Later caller or owner mutations cannot
    /// change the prepared scope or its geometry.
    /// </summary>
    public PreparedInteractionProximityScope PrepareScope(IEnumerable<ShipId> scope) =>
        PrepareScope(scope, InteractionPreparationStrategy.Scan);

    /// <summary>
    /// Prepares an immutable caller scope with an explicit strategy. Index
    /// construction occurs once; queries retain exact ordered scan results.
    /// Strategy tuning is validated by its factory; null strategies are rejected.
    /// </summary>
    public PreparedInteractionProximityScope PrepareScope(IEnumerable<ShipId> scope, InteractionPreparationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(strategy);
        ShipId[] ids = scope.Distinct().OrderBy(ship => ship.Value).ToArray();
        var targets = new List<(ShipId Id, PreparedInteractionPoint Point)>();
        foreach (ShipId id in ids)
        {
            ArgumentOutOfRangeException.ThrowIfZero(id.Value, nameof(scope));
            if (_points.TryGetValue(id, out PreparedInteractionPoint? point)) targets.Add((id, point));
        }
        return new PreparedInteractionProximityScope(this, targets.ToArray(), strategy);
    }

    internal PreparedInteractionPoint? Get(ShipId id) => _points.GetValueOrDefault(id);
}

/// <summary>Immutable scoped targets sharing the prepared geometry of one view.</summary>
public sealed class PreparedInteractionProximityScope
{
    private static readonly IReadOnlyList<ShipId> Empty = Array.AsReadOnly(Array.Empty<ShipId>());
    private readonly PreparedInteractionProximityView _view;
    private readonly (ShipId Id, PreparedInteractionPoint Point)[] _targets;

    private readonly IInteractionCandidateIndex? _index;

    /// <summary>
    /// Copies sources and runs read-only queries with explicit worker/batch tuning. Results retain
    /// caller source order and duplicates; each inner result retains the ordinary Find contract.
    /// Null inputs are rejected. Evaluation failures rethrow the earliest input failure and
    /// publish no partial outer list. No domain callbacks, scheduling, or effects run on workers.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ShipId>> FindBatch(IEnumerable<ShipId> sources, ulong rangeUnits,
        InteractionBatchExecution execution)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(execution);
        ShipId[] inputs = sources.ToArray();
        return Array.AsReadOnly(execution.Evaluate(inputs.Length, index => Find(inputs[index], rangeUnits)));
    }

    /// <summary>Builds only the selected derived index from scoped exact points; scan constructs no boxes or index.</summary>
    internal PreparedInteractionProximityScope(PreparedInteractionProximityView view,
        (ShipId Id, PreparedInteractionPoint Point)[] targets, InteractionPreparationStrategy strategy)
    {
        _view = view;
        _targets = targets;
        if (strategy.Kind == InteractionPreparationKind.Scan) return;
        InteractionIndexBox[] boxes = targets.Select(target => target.Point.Bounds(target.Id)).ToArray();
        _index = strategy.Kind switch
        {
            InteractionPreparationKind.SparseGrid => new InteractionGridIndex(boxes, strategy.CellWidthUnits, strategy.CellBudget),
            InteractionPreparationKind.BoundingBoxHierarchy => new InteractionHierarchyIndex(boxes, strategy.LeafCapacity),
            _ => throw new InvalidOperationException("Unsupported preparation strategy."),
        };
    }

    /// <summary>
    /// Returns immutable matching identities in ship order using exact inclusive
    /// range. Self, absent, nonlocal, and different-system ships are excluded.
    /// This read-only operation creates no interests, events, or effects and
    /// performs no preparation or shared cache mutation.
    /// </summary>
    public IReadOnlyList<ShipId> Find(ShipId source, ulong rangeUnits)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        if (_view.Get(source) is not { } first) return Empty;
        List<ShipId>? matches = null;
        if (_index is not null)
        {
            foreach (ShipId id in _index.Query(first.Bounds(source).Expand(rangeUnits)).Candidates)
                if (id != source && first.InRange(_view.Get(id)!, rangeUnits)) (matches ??= []).Add(id);
            return matches is null ? Empty : Array.AsReadOnly(matches.ToArray());
        }
        foreach (var target in _targets)
        {
            if (target.Id != source && first.InRange(target.Point, rangeUnits))
                (matches ??= []).Add(target.Id);
        }
        return matches is null ? Empty : Array.AsReadOnly(matches.ToArray());
    }
}

/// <summary>Exact point with a common reduced denominator; independent of published position rounding.</summary>
internal sealed record PreparedInteractionPoint(SystemId System, BigInteger X, BigInteger Y, BigInteger Denominator)
{
    /// <summary>Evaluates one exact curve instant, including the fractional target-position hold.</summary>
    internal static PreparedInteractionPoint Capture(InteractionPhaseCurve curve, SimulationTime now)
    {
        InteractionFraction time = InteractionFraction.Integer(now.Milliseconds);
        InteractionCurvePiece piece = curve.Pieces(now).Last(part => part.From <= time && part.Through >= time);
        // Pieces use now as the polynomial origin, so the constant coefficient
        // is the exact position. A hold piece already contains its target point.
        BigInteger common = BigInteger.GreatestCommonDivisor(piece.Denominator,
            BigInteger.GreatestCommonDivisor(BigInteger.Abs(piece.X[0]), BigInteger.Abs(piece.Y[0])));
        return new PreparedInteractionPoint(curve.System, piece.X[0] / common, piece.Y[0] / common, piece.Denominator / common);
    }

    /// <summary>Outward arbitrary-width integer bounds preserve fractional points and negative coordinates.</summary>
    internal InteractionIndexBox Bounds(ShipId id) => new(id, System,
        Floor(X), Floor(Y), -Floor(-X), -Floor(-Y));

    /// <summary>Outward bounds need mathematical floor, including negative fractional coordinates.</summary>
    private BigInteger Floor(BigInteger numerator)
    {
        BigInteger value = BigInteger.DivRem(numerator, Denominator, out BigInteger remainder);
        return remainder.Sign < 0 ? value - 1 : value;
    }

    /// <summary>Rejects exact axis separation before squaring, avoiding large discarded intermediates.</summary>
    internal bool InRange(PreparedInteractionPoint other, ulong range)
    {
        if (System != other.System) return false;
        BigInteger x;
        BigInteger y;
        BigInteger radius;
        if (Denominator == other.Denominator)
        {
            x = X - other.X;
            y = Y - other.Y;
            radius = (BigInteger)range * Denominator;
        }
        else
        {
            x = X * other.Denominator - other.X * Denominator;
            y = Y * other.Denominator - other.Y * Denominator;
            radius = (BigInteger)range * Denominator * other.Denominator;
        }
        if (BigInteger.Abs(x) > radius || BigInteger.Abs(y) > radius) return false;
        return x * x + y * y <= radius * radius;
    }
}
