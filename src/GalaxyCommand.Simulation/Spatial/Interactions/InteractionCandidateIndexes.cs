using System.Collections.Frozen;
using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Derived conservative closed box. It carries identity, never owner mutation authority.</summary>
internal sealed record InteractionIndexBox(ShipId Ship, SystemId System, BigInteger MinX, BigInteger MinY, BigInteger MaxX, BigInteger MaxY)
{
    internal bool Overlaps(InteractionIndexBox other) => System == other.System && MinX <= other.MaxX
        && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;

    internal InteractionIndexBox Expand(ulong range) => this with
        { MinX = MinX - range, MinY = MinY - range, MaxX = MaxX + range, MaxY = MaxY + range };

    internal static InteractionIndexBox Union(InteractionIndexBox a, InteractionIndexBox b) => a with
        { MinX = BigInteger.Min(a.MinX, b.MinX), MinY = BigInteger.Min(a.MinY, b.MinY),
          MaxX = BigInteger.Max(a.MaxX, b.MaxX), MaxY = BigInteger.Max(a.MaxY, b.MaxY) };
}

internal sealed record InteractionIndexQueryResult(IReadOnlyList<ShipId> Candidates, long Visited);
internal interface IInteractionCandidateIndex
{
    InteractionIndexQueryResult Query(InteractionIndexBox query);
    long Entries { get; }
    long Nodes { get; }
}

/// <summary>
/// Immutable sparse occupied cells. Large proxies use an overflow list and
/// large queries scan occupied cells, bounding work independently of empty space.
/// The caller supplies the cell width and traversal budget explicitly.
/// </summary>
internal sealed class InteractionGridIndex : IInteractionCandidateIndex
{
    private readonly int _cellBudget;
    private readonly ulong _width;
    private readonly FrozenDictionary<(SystemId System, BigInteger X, BigInteger Y), InteractionIndexBox[]> _cells;
    private readonly InteractionIndexBox[] _overflow;

    /// <summary>Copies coverage into closed overlapping cells using mathematical floor for negative coordinates.</summary>
    internal InteractionGridIndex(IEnumerable<InteractionIndexBox> boxes, ulong width, int cellBudget)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellBudget);
        _cellBudget = cellBudget;
        _width = width;
        var cells = new Dictionary<(SystemId, BigInteger, BigInteger), List<InteractionIndexBox>>();
        var overflow = new List<InteractionIndexBox>();
        foreach (InteractionIndexBox box in boxes)
        {
            var (x0, y0, x1, y1) = Cells(box);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) > _cellBudget) { overflow.Add(box); continue; }
            for (BigInteger x = x0; x <= x1; x++)
            for (BigInteger y = y0; y <= y1; y++)
            {
                var key = (box.System, x, y);
                if (!cells.TryGetValue(key, out var entries)) cells.Add(key, entries = []);
                entries.Add(box);
            }
        }
        _cells = cells.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _overflow = overflow.ToArray();
        Entries = _cells.Values.Sum(values => (long)values.Length) + _overflow.Length;
        Nodes = _cells.Count;
    }

    public long Entries { get; }
    public long Nodes { get; }
    internal long OverflowEntries => _overflow.Length;

    /// <summary>Returns deduplicated ordered box overlaps; traversal never visits an unbounded empty cell rectangle.</summary>
    public InteractionIndexQueryResult Query(InteractionIndexBox query)
    {
        var result = new HashSet<ShipId>();
        long visited = 0;
        var (x0, y0, x1, y1) = Cells(query);
        if ((x1 - x0 + 1) * (y1 - y0 + 1) <= Math.Min(_cellBudget, _cells.Count))
        {
            for (BigInteger x = x0; x <= x1; x++)
            for (BigInteger y = y0; y <= y1; y++)
                if (_cells.TryGetValue((query.System, x, y), out var entries)) Visit(entries);
        }
        else
        {
            foreach (var cell in _cells)
                if (cell.Key.System == query.System && cell.Key.X >= x0 && cell.Key.X <= x1
                    && cell.Key.Y >= y0 && cell.Key.Y <= y1) Visit(cell.Value);
        }
        Visit(_overflow.Where(box => box.System == query.System));
        return new InteractionIndexQueryResult(Array.AsReadOnly(result.OrderBy(id => id.Value).ToArray()), visited);

        // A proxy may occupy several cells. Identity reduction must precede narrow-phase evaluation.
        void Visit(IEnumerable<InteractionIndexBox> entries)
        {
            foreach (InteractionIndexBox box in entries) { visited++; if (box.Overlaps(query)) result.Add(box.Ship); }
        }
    }

    private (BigInteger, BigInteger, BigInteger, BigInteger) Cells(InteractionIndexBox box) =>
        (Floor(box.MinX), Floor(box.MinY), Floor(box.MaxX), Floor(box.MaxY));

    /// <summary>C# division truncates toward zero; negative fractions belong to the preceding cell.</summary>
    private BigInteger Floor(BigInteger value)
    {
        BigInteger quotient = BigInteger.DivRem(value, _width, out BigInteger remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }
}

/// <summary>Immutable per-system median-split hierarchy. Tree shape never becomes an outcome tie-breaker.</summary>
internal sealed class InteractionHierarchyIndex : IInteractionCandidateIndex
{
    private sealed record Node(InteractionIndexBox Bounds, Node? Left, Node? Right, InteractionIndexBox[]? Leaves);
    private readonly FrozenDictionary<SystemId, Node> _roots;

    /// <summary>Copies input, splitting the widest bound axis with stable identity ties and configurable leaf size.</summary>
    internal InteractionHierarchyIndex(IEnumerable<InteractionIndexBox> boxes, int leafSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leafSize);
        long nodes = 0;
        long entries = 0;
        _roots = boxes.GroupBy(box => box.System).ToFrozenDictionary(group => group.Key, group => Build(group.ToArray()));
        Nodes = nodes;
        Entries = entries;

        // Each parent unions all child coverage; leaf size affects traversal, never candidate eligibility.
        Node Build(InteractionIndexBox[] items)
        {
            nodes++;
            InteractionIndexBox bounds = items.Aggregate(InteractionIndexBox.Union);
            if (items.Length <= leafSize) { entries += items.Length; return new Node(bounds, null, null, items); }
            bool x = bounds.MaxX - bounds.MinX >= bounds.MaxY - bounds.MinY;
            InteractionIndexBox[] ordered = items.OrderBy(item => x ? item.MinX + item.MaxX : item.MinY + item.MaxY)
                .ThenBy(item => item.Ship.Value).ToArray();
            int middle = ordered.Length / 2;
            return new Node(bounds, Build(ordered[..middle]), Build(ordered[middle..]), null);
        }
    }

    public long Entries { get; }
    public long Nodes { get; }

    /// <summary>Prunes conservative parent boxes and returns identity-ordered leaf overlaps independent of tree layout.</summary>
    public InteractionIndexQueryResult Query(InteractionIndexBox query)
    {
        var result = new HashSet<ShipId>();
        long visited = 0;
        if (_roots.TryGetValue(query.System, out var root)) Visit(root);
        return new InteractionIndexQueryResult(Array.AsReadOnly(result.OrderBy(id => id.Value).ToArray()), visited);

        // Closed parent overlap can only prune impossible leaves; output order comes from identity reduction.
        void Visit(Node node)
        {
            if (!node.Bounds.Overlaps(query)) return;
            if (node.Leaves is { } leaves)
                foreach (InteractionIndexBox box in leaves) { visited++; if (box.Overlaps(query)) result.Add(box.Ship); }
            else { Visit(node.Left!); Visit(node.Right!); }
        }
    }
}
