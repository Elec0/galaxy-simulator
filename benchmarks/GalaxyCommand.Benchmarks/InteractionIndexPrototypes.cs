using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal interface IInteractionIndexPrototype
{
    PrototypeQueryResult Query(PrototypeBox query);
    long Entries { get; }
    long Nodes { get; }
}

/// <summary>Benchmark wrapper retaining the original experimental tuning around the shared immutable grid.</summary>
internal sealed class SparseGridPrototype : IInteractionIndexPrototype
{
    private readonly InteractionGridIndex _index;

    internal SparseGridPrototype(IEnumerable<PrototypeBox> boxes, long width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        _index = new InteractionGridIndex(boxes, checked((ulong)width), 4096);
    }

    public PrototypeQueryResult Query(PrototypeBox query) => _index.Query(query);
    public long Entries => _index.Entries;
    public long Nodes => _index.Nodes;
    internal long OverflowEntries => _index.OverflowEntries;
}

/// <summary>Benchmark wrapper preserving configurable leaves around the shared immutable hierarchy.</summary>
internal sealed class BoundingHierarchyPrototype : IInteractionIndexPrototype
{
    private readonly InteractionHierarchyIndex _index;

    internal BoundingHierarchyPrototype(IEnumerable<PrototypeBox> boxes, int leafSize = 4) =>
        _index = new InteractionHierarchyIndex(boxes, leafSize);

    public PrototypeQueryResult Query(PrototypeBox query) => _index.Query(query);
    public long Entries => _index.Entries;
    public long Nodes => _index.Nodes;
}
