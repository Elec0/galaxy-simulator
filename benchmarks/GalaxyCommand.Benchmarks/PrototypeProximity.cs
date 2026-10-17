using System.Collections.Frozen;
using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal sealed record PrototypeProximityResult(IReadOnlyList<ShipId> Matches, long Candidates, long ExactChecks, long BoxTests);

/// <summary>Benchmark-only scoped broad phase sharing the runtime's prepared exact narrow phase.</summary>
internal sealed class PrototypeProximity
{
    private readonly PreparedInteractionProximityView _view;
    private readonly PreparedInteractionProximityScope _scan;
    private readonly FrozenDictionary<SystemId, long> _targetsPerSystem;
    private readonly IInteractionIndexPrototype? _index;
    private readonly FrozenSet<ShipId> _scopedSources;

    /// <summary>Prepares one view and copies an explicit scope; no production query or owner state changes.</summary>
    internal PrototypeProximity(InteractionSpatialReadView view, IEnumerable<ShipId> scope, string kind, long width = 16, int leafSize = 4)
    {
        _view = InteractionProximityQuery.Prepare(view);
        ShipId[] ids = scope.Distinct().OrderBy(id => id.Value).ToArray();
        _scan = _view.PrepareScope(ids);
        PrototypeBox[] boxes = ids.Select(id => _view.Get(id) is { } point ? PointBox(id, point) : null)
            .OfType<PrototypeBox>().ToArray();
        _scopedSources = boxes.Select(box => box.Ship).ToFrozenSet();
        _targetsPerSystem = boxes.GroupBy(box => box.System).ToFrozenDictionary(group => group.Key, group => group.LongCount());
        _index = kind switch
        {
            "scan" => null,
            "grid" => new SparseGridPrototype(boxes, width),
            "hierarchy" => new BoundingHierarchyPrototype(boxes, leafSize),
            _ => throw new ArgumentException("Unknown prototype kind.", nameof(kind)),
        };
    }

    internal long Entries => _index?.Entries ?? 0;
    internal long Nodes => _index?.Nodes ?? 0;

    /// <summary>Exact inclusive matches in ship order. The index contains only scoped local targets.</summary>
    internal PrototypeProximityResult Query(ShipId source, ulong range)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        if (_view.Get(source) is not { } point) return new(Array.Empty<ShipId>(), 0, 0, 0);
        if (_index is null)
        {
            long checks = _targetsPerSystem.GetValueOrDefault(point.System)
                - (_scopedSources.Contains(source) ? 1 : 0);
            return new(_scan.Find(source, range), checks, checks, 0);
        }
        PrototypeQueryResult broad = _index.Query(PointBox(source, point).Expand(range));
        var matches = new List<ShipId>();
        long exact = 0;
        foreach (ShipId candidate in broad.Candidates)
        {
            if (candidate == source) continue;
            exact++;
            if (point.InRange(_view.Get(candidate)!, range)) matches.Add(candidate);
        }
        return new(Array.AsReadOnly(matches.ToArray()), exact, exact, broad.Visited);
    }

    /// <summary>Outward integer rounding retains fractional point contact, including negative fractions.</summary>
    internal static PrototypeBox PointBox(ShipId id, PreparedInteractionPoint point) =>
        new(id, point.System, Floor(point.X, point.Denominator), Floor(point.Y, point.Denominator),
            -Floor(-point.X, point.Denominator), -Floor(-point.Y, point.Denominator));

    /// <summary>Rounds down rather than toward zero so negative point bounds cannot narrow.</summary>
    private static BigInteger Floor(BigInteger numerator, BigInteger denominator)
    {
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }
}
