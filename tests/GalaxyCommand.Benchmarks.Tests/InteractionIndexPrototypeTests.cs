using System.Numerics;
using PrototypeBox = GalaxyCommand.Simulation.InteractionIndexBox;
using System.Text.Json;
using GalaxyCommand.Benchmarks;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class InteractionIndexPrototypeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void BothIndexesMatchClosedBoxScanAcrossLayouts(long width)
    {
        PrototypeBox[] boxes = Enumerable.Range(0, 120).Select(i => Box((ulong)i + 1, i % 3 + 1,
            (i % 12 - 6) * 7, (i / 12 - 5) * 9, i % 4, i % 6)).ToArray();
        foreach (PrototypeBox[] layout in new[] { boxes, boxes.Reverse().ToArray() })
        foreach (IInteractionIndexPrototype index in new IInteractionIndexPrototype[]
        { new SparseGridPrototype(layout, width), new BoundingHierarchyPrototype(layout, 1), new BoundingHierarchyPrototype(layout, 8) })
        foreach (PrototypeBox query in boxes.Select(b => b with { MinX = b.MinX - 3, MinY = b.MinY - 2, MaxX = b.MaxX + 3, MaxY = b.MaxY + 2 }))
            Assert.Equal(Scan(boxes, query), index.Query(query).Candidates);
    }

    [Fact]
    public void HugeSweptBoxesAndQueriesRemainBoundedAndInclusive()
    {
        BigInteger extent = BigInteger.One << 100;
        PrototypeBox[] boxes = [new(new ShipId(1), new SystemId(1), -extent, -extent, extent, extent),
            Box(2, 1, -16, -16, 0, 0), Box(3, 1, 16, 16, 0, 0), Box(4, 2, 0, 0, 0, 0)];
        foreach (IInteractionIndexPrototype index in new IInteractionIndexPrototype[]
            { new SparseGridPrototype(boxes, 16), new BoundingHierarchyPrototype(boxes) })
        foreach (PrototypeBox query in new[] { Box(99, 1, -16, -16, 0, 0), boxes[0], Box(99, 1, 16, 16, 0, 0) })
            Assert.Equal(Scan(boxes, query), index.Query(query).Candidates);
    }

    [Fact]
    public void IndexCopiesInputAndReturnsUniqueOrderedCandidates()
    {
        var boxes = new List<PrototypeBox> { Box(3, 1, 0, 0, 32, 32), Box(1, 1, 0, 0, 0, 0) };
        var grid = new SparseGridPrototype(boxes, 1);
        var tree = new BoundingHierarchyPrototype(boxes);
        boxes.Clear();
        PrototypeBox query = Box(99, 1, 0, 0, 32, 32);
        Assert.Equal<ShipId>([new(1), new(3)], grid.Query(query).Candidates);
        Assert.Equal<ShipId>([new(1), new(3)], tree.Query(query).Candidates);
    }

    [Theory]
    [InlineData(0, 100, 1, 10)]
    [InlineData(1, 1, 2, 10)]
    [InlineData(0, 0, 3, 10000)]
    public void TwoDimensionalIndexWorkloadsMatchPreparedScan(int layout, int spacing, int stride, int range)
    {
        string? expectedDigest = null;
        long? expectedMatches = null;
        foreach (string preset in new[] { "interaction.index-scan", "interaction.index-grid", "interaction.index-hierarchy" })
        {
            string[] args = ["--suite", "full", "--preset", preset,
                "--set", "shipCount=120", "--set", "activeShipCount=60", "--set", "queryCount=12",
                "--set", "warmupIterations=0", "--set", "measuredIterations=2",
                "--set", $"layout={layout}", "--set", $"participantSpacing={spacing}",
                "--set", $"scopeStride={stride}", "--set", $"rangeUnits={range}", "--set", "systemCount=2"];
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.True(BenchmarkApplication.Run(args, output, error) == 0, error.ToString());
            using var report = JsonDocument.Parse(output.ToString());
            var scenario = report.RootElement.GetProperty("Scenarios")[0];
            string digest = scenario.GetProperty("Digest").GetString()!;
            long matches = scenario.GetProperty("Counts").GetProperty("matches").GetInt64();
            expectedDigest ??= digest;
            expectedMatches ??= matches;
            Assert.Equal(expectedDigest, digest);
            Assert.Equal(expectedMatches, matches);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(2100)]
    [InlineData(6000)]
    public void IndexNarrowPhasePreservesExactCurvesAndCallerScope(ulong milliseconds)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-1, -1));
        movement.Add(new ShipId(2), Position(25, 3));
        movement.Add(new ShipId(3), Position(0, 0));
        movement.Add(new ShipId(4), Position(0, 0, 2));
        movement.Add(new ShipId(5), Position(0, 0));
        movement.CommitStartConnector(new ShipId(5), new TravelLeg.Connector(new TransitConnectionId(1),
            Position(0, 0), Position(0, 0, 2), new SimulationDuration(10000)), SimulationTime.Zero, static value => value);
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(-1, -1), ShipVelocity.Zero, ShipHeading.Zero), Position(89, -1), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!.WithAdditionalBoundaries([new SimulationTime(2100)]);
        movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival,
            SimulationTime.Zero, static value => value);
        movement.CommitStartOrReplace(new ShipId(2), new TravelLeg.Local(Position(25, 3), Position(0, 0),
            new SimulationDuration(6000)), SimulationTime.Zero, static value => value);
        var view = movement.CaptureInteractionReadView(new SimulationTime(milliseconds));
        ShipId[] scope = [new(3), new(99), new(2), new(4), new(5), new(3)];
        var expected = InteractionProximityQuery.Prepare(view).PrepareScope(scope);
        foreach (string kind in new[] { "grid", "hierarchy" })
        foreach (long width in new long[] { 1, 16, 128 })
        {
            var query = new PrototypeProximity(view, scope.Reverse(), kind, width);
            foreach (ulong range in new ulong[] { 0, 1, 30, ulong.MaxValue })
            foreach (ShipId source in new ShipId[] { new(1), new(2), new(5), new(99) })
            {
                Assert.Equal(expected.Find(source, range), query.Query(source, range).Matches);
                InteractionPreparationStrategy strategy = kind == "grid"
                    ? InteractionPreparationStrategy.SparseGrid((ulong)width, 4096)
                    : InteractionPreparationStrategy.BoundingBoxHierarchy(4);
                var runtime = InteractionProximityQuery.Prepare(view);
                Assert.Equal(expected.Find(source, range), runtime.PrepareScope(scope.Reverse(), strategy).Find(source, range));
                ShipId[] runtimeCombined = scope.Chunk(2).Reverse()
                    .SelectMany(batch => runtime.PrepareScope(batch, strategy).Find(source, range))
                    .Distinct().OrderBy(id => id.Value).ToArray();
                Assert.Equal(expected.Find(source, range), runtimeCombined);
                ShipId[] combined = scope.Chunk(2).Reverse()
                    .SelectMany(batch => new PrototypeProximity(view, batch, kind, width).Query(source, range).Matches)
                    .Distinct().OrderBy(id => id.Value).ToArray();
                Assert.Equal(expected.Find(source, range), combined);
            }
        }
    }

    [Fact]
    public void SweptBroadPhaseRetainsEveryExactContactAcrossMotionKinds()
    {
        var end = new SimulationTime(4000);
        InteractionPhaseCurve[] curves = [
            InteractionPhaseCurve.From(new LocalMotionSegment(new MotionId(1), new EventGeneration(1),
                Position(-100, -100), Position(100, 100), SimulationTime.Zero, new SimulationTime(1000))),
            InteractionPhaseCurve.From(new LocalMotionSegment(new MotionId(2), new EventGeneration(1),
                Position(100, -100), Position(-100, 100), SimulationTime.Zero, new SimulationTime(1000))),
            InteractionPhaseCurve.From(new AnalyticManeuverSegment(SimulationTime.Zero, end,
                new ShipKinematicState(Position(0, 0), new ShipVelocity(4000, -2000), ShipHeading.Zero),
                new ShipAcceleration(-2000, 2000), ShipAngularRate.Zero)),
            InteractionPhaseCurve.From(new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag,
                SimulationTime.Zero, new ShipKinematicState(Position(0, 0), new ShipVelocity(2000000, 0), ShipHeading.Zero),
                new ManeuverAcceleration(3000000000))),
            InteractionPhaseCurve.Fixed(Position(1, 0), SimulationTime.Zero, end)];
        PrototypeBox[] boxes = curves.Select((curve, i) =>
        {
            var bounds = InteractionSweptBoundsQuery.Find(curve, SimulationTime.Zero, end)!;
            return new PrototypeBox(new ShipId((ulong)i + 1), bounds.System, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
        }).ToArray();
        int contacts = 0;
        foreach (IInteractionIndexPrototype index in new IInteractionIndexPrototype[]
            { new SparseGridPrototype(boxes, 1), new BoundingHierarchyPrototype(boxes, 1), new BoundingHierarchyPrototype(boxes, 4) })
        for (int i = 0; i < curves.Length; i++)
        for (int j = i + 1; j < curves.Length; j++)
        {
            if (InteractionPhaseRangeQuery.Find(curves[i], curves[j], SimulationTime.Zero, end, 1) is null) continue;
            contacts++;
            Assert.Contains(boxes[j].Ship, index.Query(boxes[i].Expand(1)).Candidates);
        }
        Assert.True(contacts > 0);
    }

    [Fact]
    public void WholePlanBoundaryResetAndInsertedEventsRemainCovered()
    {
        var brake = new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero,
            new ShipKinematicState(Position(0, 0), new ShipVelocity(1000, 0), ShipHeading.Zero), new ManeuverAcceleration(1000));
        var original = ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(brake);
        var split = original.WithAdditionalBoundaries([new SimulationTime(500)]);
        foreach (SimulationTime now in new[] { new SimulationTime(500), original.EndsAt })
        {
            var bounds = InteractionSweptBoundsQuery.Find(split, now, now)!;
            Assert.Equal(InteractionSweptBoundsQuery.Find(original, now, now), bounds);
            var box = new PrototypeBox(new ShipId(1), bounds.System, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
            foreach (IInteractionIndexPrototype index in new IInteractionIndexPrototype[]
                { new SparseGridPrototype([box], 1), new BoundingHierarchyPrototype([box]) })
                Assert.Equal<ShipId>([new(1)], index.Query(box).Candidates);
        }
    }

    [Theory]
    [InlineData("layout=2")]
    [InlineData("scopeStride=0")]
    [InlineData("gridWidth=0")]
    [InlineData("treeLeafSize=0")]
    public void InvalidPrototypeSettingsAreRejectedBeforeExecution(string setting)
    {
        foreach (string id in new[] { "interaction.index-scan", "interaction.index-grid", "interaction.index-hierarchy" })
        {
            var request = BenchmarkCommandLine.Parse(["--suite", "full", "--preset", id, "--set", setting]);
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(request));
        }
    }

    [Theory]
    [InlineData(0, 100, 1, 10)]
    [InlineData(1, 1, 2, 10)]
    [InlineData(0, 0, 3, 10000)]
    public void PublicRuntimePresetsMatchReferenceResults(int layout, int spacing, int stride, int range)
    {
        string? expectedDigest = null;
        long? expectedMatches = null;
        foreach (string id in new[] { "interaction.index-scan", "interaction.runtime-scan", "interaction.runtime-grid", "interaction.runtime-hierarchy" })
        {
            string[] args = ["--suite", "full", "--preset", id,
                "--set", "shipCount=120", "--set", "activeShipCount=60", "--set", "queryCount=12",
                "--set", "warmupIterations=0", "--set", "measuredIterations=2",
                "--set", $"layout={layout}", "--set", $"participantSpacing={spacing}",
                "--set", $"scopeStride={stride}", "--set", $"rangeUnits={range}", "--set", "systemCount=2"];
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.True(BenchmarkApplication.Run(args, output, error) == 0, error.ToString());
            using var report = JsonDocument.Parse(output.ToString());
            var scenario = report.RootElement.GetProperty("Scenarios")[0];
            string digest = scenario.GetProperty("Digest").GetString()!;
            long matches = scenario.GetProperty("Counts").GetProperty("matches").GetInt64();
            expectedDigest ??= digest;
            expectedMatches ??= matches;
            Assert.Equal(expectedDigest, digest);
            Assert.Equal(expectedMatches, matches);
        }
    }

    [Theory]
    [InlineData("interaction.runtime-scan")]
    [InlineData("interaction.runtime-grid")]
    [InlineData("interaction.runtime-hierarchy")]
    public void RuntimePresetsRequireOptInAndValidatedTuning(string id)
    {
        BenchmarkPreset preset = BenchmarkPresets.Get(id);
        Assert.Equal("b75076796115dc88", preset.ExpectedDigest);
        Assert.Equal(1, preset.Version);
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(["--preset", id])));
        foreach (string setting in new[] { "gridWidth=0", "gridCellBudget=0", "treeLeafSize=0", "scopeStride=0" })
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(
                BenchmarkCommandLine.Parse(["--suite", "full", "--preset", id, "--set", setting])));
    }

    [Fact]
    public void IndexComparisonPresetsShareVersionedCanonicalDigestAndRequireOptIn()
    {
        foreach (string id in new[] { "interaction.index-scan", "interaction.index-grid", "interaction.index-hierarchy" })
        {
            BenchmarkPreset preset = BenchmarkPresets.Get(id);
            Assert.Equal("b75076796115dc88", preset.ExpectedDigest);
            Assert.Equal(1, preset.Version);
            Assert.True(preset.IsHeavy);
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(["--preset", id])));
        }
    }

    private static SystemPosition Position(long x, long y, ulong system = 1) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));

    private static PrototypeBox Box(ulong id, int system, long x, long y, long dx, long dy) =>
        new(new ShipId(id), new SystemId((ulong)system), x, y, (BigInteger)x + dx, (BigInteger)y + dy);

    private static ShipId[] Scan(IEnumerable<PrototypeBox> boxes, PrototypeBox q) => boxes
        .Where(b => b.System == q.System && b.MinX <= q.MaxX && b.MaxX >= q.MinX && b.MinY <= q.MaxY && b.MaxY >= q.MinY)
        .Select(b => b.Ship).Distinct().OrderBy(id => id.Value).ToArray();
}
