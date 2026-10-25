namespace GalaxyCommand.Simulation;

/// <summary>Immutable exact source union for persistent targets and event positions, without synthetic entities.</summary>
internal sealed class PlayerCoverageBoundary
{
    private readonly SimulationTime _timestamp;
    private readonly Dictionary<SystemId, CoverageNode> _roots;

    /// <summary>Builds immutable source-region hierarchies, keeping repeated persistent-target queries local.</summary>
    internal PlayerCoverageBoundary(SimulationTime timestamp, (PreparedInteractionPoint Point, ulong Radius)[] sources)
    {
        _timestamp = timestamp;
        _roots = sources.GroupBy(source => source.Point.System).ToDictionary(group => group.Key,
            group => Build(group.OrderBy(source => source.Point.X / source.Point.Denominator).ToArray()));
    }

    /// <summary>Uses inclusive exact distances; removed subjects need no live spatial registration.</summary>
    internal bool Contains(SystemPosition position)
    {
        if (!_roots.TryGetValue(position.SystemId, out CoverageNode? root)) return false;
        PreparedInteractionPoint target = PreparedInteractionPoint.Capture(
            InteractionPhaseCurve.Fixed(position, _timestamp, _timestamp), _timestamp);
        return root.Contains(target);
    }

    /// <summary>Partitions source extents into balanced immutable regions; bounds prune only, exact circles decide coverage.</summary>
    private static CoverageNode Build((PreparedInteractionPoint Point, ulong Radius)[] sources)
    {
        if (sources.Length <= 8)
        {
            InteractionIndexBox bounds = sources.Select(source => source.Point.Bounds(default).Expand(source.Radius))
                .Aggregate(InteractionIndexBox.Union);
            return new CoverageNode(bounds, sources, null, null);
        }
        int half = sources.Length / 2;
        CoverageNode left = Build(sources[..half]);
        CoverageNode right = Build(sources[half..]);
        return new CoverageNode(InteractionIndexBox.Union(left.Bounds, right.Bounds), [], left, right);
    }

    // The shared bounds shape's ship field is unused. These are source regions,
    // never synthetic ship participants or entries in an authoritative registry.
    private sealed record CoverageNode(InteractionIndexBox Bounds,
        (PreparedInteractionPoint Point, ulong Radius)[] Sources, CoverageNode? Left, CoverageNode? Right)
    {
        /// <summary>Rejects disjoint regions before testing exact leaf distances.</summary>
        internal bool Contains(PreparedInteractionPoint point)
        {
            if (point.X < Bounds.MinX * point.Denominator || point.X > Bounds.MaxX * point.Denominator
                || point.Y < Bounds.MinY * point.Denominator || point.Y > Bounds.MaxY * point.Denominator) return false;
            return Left is null ? Sources.Any(source => source.Point.InRange(point, source.Radius))
                : Left.Contains(point) || Right!.Contains(point);
        }
    }
}
