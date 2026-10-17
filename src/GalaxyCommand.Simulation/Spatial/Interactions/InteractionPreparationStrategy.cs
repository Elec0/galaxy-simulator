namespace GalaxyCommand.Simulation;

internal enum InteractionPreparationKind { Scan, SparseGrid, BoundingBoxHierarchy }

/// <summary>
/// Immutable caller-selected instant or swept scope preparation. Strategy and tuning
/// affect only derived lookup cost, never scope, exact geometry, or owner state.
/// Indexed strategies require explicit tuning; no automatic selector is used.
/// </summary>
public sealed class InteractionPreparationStrategy
{
    private InteractionPreparationStrategy(InteractionPreparationKind kind, ulong width = 0, int budget = 0, int leaves = 0)
    { Kind = kind; CellWidthUnits = width; CellBudget = budget; LeafCapacity = leaves; }

    internal InteractionPreparationKind Kind { get; }
    internal ulong CellWidthUnits { get; }
    internal int CellBudget { get; }
    internal int LeafCapacity { get; }

    /// <summary>The existing ordered exact scan, also used by preparation calls that omit a strategy.</summary>
    public static InteractionPreparationStrategy Scan { get; } = new(InteractionPreparationKind.Scan);

    /// <summary>
    /// Selects a sparse grid with positive cell width and budget. The budget
    /// bounds cells per proxy and empty-cell enumeration per query; oversized
    /// proxies use overflow coverage and large queries scan occupied cells.
    /// Neither fallback changes the requested strategy or contact semantics.
    /// </summary>
    public static InteractionPreparationStrategy SparseGrid(ulong cellWidthUnits, int cellBudget)
    {
        ArgumentOutOfRangeException.ThrowIfZero(cellWidthUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellBudget);
        return new(InteractionPreparationKind.SparseGrid, cellWidthUnits, cellBudget);
    }

    /// <summary>Selects an immutable per-system bounding-box hierarchy with an explicit positive leaf capacity.</summary>
    public static InteractionPreparationStrategy BoundingBoxHierarchy(int leafCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leafCapacity);
        return new(InteractionPreparationKind.BoundingBoxHierarchy, leaves: leafCapacity);
    }
}
