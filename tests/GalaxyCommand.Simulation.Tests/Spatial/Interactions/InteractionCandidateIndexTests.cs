using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionCandidateIndexTests
{
    [Fact]
    public void IndexesPreserveClosedCoverageAcrossHugeSpans()
    {
        BigInteger extent = BigInteger.One << 100;
        InteractionIndexBox[] boxes = [new(new ShipId(3), new SystemId(1), -extent, -extent, extent, extent),
            new(new ShipId(2), new SystemId(1), -16, -16, -16, -16),
            new(new ShipId(1), new SystemId(2), -16, -16, -16, -16)];
        var query = new InteractionIndexBox(new ShipId(99), new SystemId(1), -16, -16, -16, -16);
        foreach (IInteractionCandidateIndex index in new IInteractionCandidateIndex[]
            { new InteractionGridIndex(boxes, 16, 1), new InteractionHierarchyIndex(boxes, 1) })
            Assert.Equal<ShipId>([new(2), new(3)], index.Query(query).Candidates);
    }

    [Fact]
    public void BothIndexesPruneDistantTargetsBeforeExactEvaluation()
    {
        InteractionIndexBox[] boxes = Enumerable.Range(1, 100).Select(i => new InteractionIndexBox(new ShipId((ulong)i),
            new SystemId(1), i * 100, i * 100, i * 100, i * 100)).ToArray();
        foreach (IInteractionCandidateIndex index in new IInteractionCandidateIndex[]
            { new InteractionGridIndex(boxes, 16, 4096), new InteractionHierarchyIndex(boxes, 4) })
        {
            var result = index.Query(boxes[0]);
            Assert.Equal<ShipId>([new(1)], result.Candidates);
            Assert.True(result.Visited < boxes.Length);
        }
    }
}
