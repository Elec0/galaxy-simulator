using System.Text.Json;
using GalaxyCommand.Benchmarks;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class InteractionSweptBenchmarkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SweptIndexesMatchExactContactEvidenceAndScopeBatches(int motionKind)
    {
        PrototypeSweepTrajectory[] trajectories = Trajectories(motionKind);
        ShipId[] scope = [new(2), new(3), new(4), new(3), new(99)];
        var from = SimulationTime.Zero;
        var through = new SimulationTime(1000);
        PrototypeSweepMatch[] expected = [new(new ShipId(2), Contact(trajectories[0].Motion, trajectories[1].Motion, from, through)!),
            new(new ShipId(3), Contact(trajectories[0].Motion, trajectories[2].Motion, from, through)!)];
        Assert.All(expected, match => Assert.NotNull(match.Contact));
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        foreach (long width in new long[] { 1, 16, 128 })
        {
            var queries = new PrototypeSweptQueries(trajectories.Reverse(), scope.Reverse(), from, through, kind, width);
            Assert.Equal(expected, queries.Query(new ShipId(1), 1).Matches);
            Assert.Empty(queries.Query(new ShipId(99), 1).Matches);
            PrototypeSweepMatch[] batches = scope.Chunk(2).Reverse()
                .SelectMany(batch => new PrototypeSweptQueries(trajectories, batch, from, through, kind, width).Query(new ShipId(1), 1).Matches)
                .Distinct().OrderBy(match => match.Ship.Value).ToArray();
            Assert.Equal(expected, batches);
        }
    }

    [Fact]
    public void OversizedDiagonalSweepsUseOverflowWithoutLosingContact()
    {
        var end = new SimulationTime(1000);
        SystemPosition Point(long x, long y) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));
        PrototypeSweepTrajectory[] paths = [new(new ShipId(1), new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(
            new LocalMotionSegment(new MotionId(1), new EventGeneration(1), Point(-10000, -10000), Point(10000, 10000), SimulationTime.Zero, end)))),
            new(new ShipId(2), new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(
            new LocalMotionSegment(new MotionId(2), new EventGeneration(1), Point(10000, -10000), Point(-10000, 10000), SimulationTime.Zero, end))))];
        var grid = new PrototypeSweptQueries(paths, [new ShipId(2)], SimulationTime.Zero, end, "grid", 16);
        Assert.Equal(1, grid.OverflowEntries);
        Assert.Equal<PrototypeSweepMatch>([new(new ShipId(2), new SweptRangeContact(new SimulationTime(500), true, false))], grid.Query(new ShipId(1), 0).Matches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void VersionedSweptWorkloadsAgreeOnOrderedContactDigest(int motionKind)
    {
        string? expected = null;
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] args = ["--suite", "full", "--preset", $"interaction.swept-{kind}",
                "--set", "shipCount=4", "--set", "activeShipCount=2", "--set", "queryCount=4",
                "--set", "participantSpacing=0", "--set", $"motionKind={motionKind}",
                "--set", "warmupIterations=0", "--set", "measuredIterations=2"];
            Assert.True(BenchmarkApplication.Run(args, output, error) == 0, error.ToString());
            using var report = JsonDocument.Parse(output.ToString());
            var scenario = report.RootElement.GetProperty("Scenarios")[0];
            string digest = scenario.GetProperty("Digest").GetString()!;
            expected ??= digest;
            Assert.Equal(expected, digest);
            Assert.Equal(12, scenario.GetProperty("Counts").GetProperty("contacts").GetInt64());
        }
    }

    [Theory]
    [InlineData("motionKind=3")]
    [InlineData("travelDurationMilliseconds=999")]
    [InlineData("gridWidth=0")]
    public void InvalidSweptWorkloadIsRejectedBeforeExecution(string setting)
    {
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(
                ["--suite", "full", "--preset", $"interaction.swept-{kind}", "--set", setting])));
    }

    [Fact]
    public void ZeroSpeedBrakingFixtureIsRejectedBeforeExecution()
    {
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(
            ["--suite", "full", "--preset", "interaction.swept-grid", "--set", "motionKind=2",
                "--set", "destinationDistance=1", "--set", "travelDurationMilliseconds=86400000"])));
    }

    [Fact]
    public void BriefContactKeepsEvidenceAtItsRoundedTimestamp()
    {
        var end = new SimulationTime(1);
        PrototypeSweepTrajectory[] paths = [new(new ShipId(1), new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(
            new LocalMotionSegment(new MotionId(1), new EventGeneration(1), Position(-1), Position(2), SimulationTime.Zero, end)))),
            new(new ShipId(2), new PrototypeSweepMotion.Stationary(Position(0)))];
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            var queries = new PrototypeSweptQueries(paths, [new ShipId(2)], SimulationTime.Zero, end, kind, 1);
            Assert.Equal<PrototypeSweepMatch>([new(new ShipId(2), new SweptRangeContact(end, false, false))], queries.Query(new ShipId(1), 0).Matches);
        }
    }

    [Fact]
    public void SweptPresetsShareCanonicalDigestAndRequireFullSuite()
    {
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            string id = $"interaction.swept-{kind}";
            var preset = BenchmarkPresets.Get(id);
            Assert.Equal(1, preset.Version);
            Assert.Equal("418f5c9774f5bbdc", preset.ExpectedDigest);
            Assert.True(preset.IsHeavy);
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(["--preset", id])));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PartialHorizonPreservesExactClosedIntervalEvidence(int kind)
    {
        var paths = Trajectories(kind);
        var from = new SimulationTime(200);
        var through = new SimulationTime(450);
        PrototypeSweepMatch[] expected = paths.Skip(1).Take(2)
            .Select(path => (path.Ship, Result: Contact(paths[0].Motion, path.Motion, from, through)))
            .Where(pair => pair.Result is not null).Select(pair => new PrototypeSweepMatch(pair.Ship, pair.Result!)).ToArray();
        foreach (string mode in new[] { "scan", "grid", "hierarchy" })
        {
            var query = new PrototypeSweptQueries(paths, [new ShipId(2), new ShipId(3)], from, through, mode);
            Assert.Equal(expected, query.Query(new ShipId(1), 1).Matches);
        }
    }

    [Fact]
    public void WholePlanQueryAtCompletionUsesCommittedReset()
    {
        var paths = Trajectories(2);
        var plan = Assert.IsType<PrototypeSweepMotion.Plan>(paths[0].Motion).Geometry;
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            var query = new PrototypeSweptQueries(paths, [new ShipId(3)], plan.EndsAt, plan.EndsAt, kind);
            Assert.Equal<PrototypeSweepMatch>([new(new ShipId(3), new SweptRangeContact(plan.EndsAt, true, true))], query.Query(new ShipId(1), 0).Matches);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PublicSweptPresetsAgreeOnContactEvidence(int motionKind)
    {
        string? expected = null;
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] args = ["--suite", "full", "--preset", $"interaction.runtime-swept-{kind}",
                "--set", "shipCount=4", "--set", "activeShipCount=2", "--set", "queryCount=4",
                "--set", "participantSpacing=0", "--set", $"motionKind={motionKind}",
                "--set", "warmupIterations=0", "--set", "measuredIterations=2"];
            Assert.True(BenchmarkApplication.Run(args, output, error) == 0, error.ToString());
            using var report = JsonDocument.Parse(output.ToString());
            var scenario = report.RootElement.GetProperty("Scenarios")[0];
            string digest = scenario.GetProperty("Digest").GetString()!;
            expected ??= digest;
            Assert.Equal(expected, digest);
            Assert.Equal(12, scenario.GetProperty("Counts").GetProperty("contacts").GetInt64());
        }
    }

    [Theory]
    [InlineData("gridCellBudget=0")]
    [InlineData("motionKind=2")]
    [InlineData("treeLeafSize=0")]
    public void PublicSweptPresetsRejectInvalidTuningAndUnsupportedFixtures(string setting)
    {
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            string id = $"interaction.runtime-swept-{kind}";
            Assert.True(BenchmarkPresets.Get(id).IsHeavy);
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(["--preset", id])));
            Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse(
                ["--suite", "full", "--preset", id, "--set", setting])));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PublicSweptFixtureMatchesExactBoundaryClippedForecasts(int motionKind)
    {
        var configuration = BenchmarkCommandLine.Resolve(BenchmarkCommandLine.Parse([
            "--suite", "full", "--preset", "interaction.runtime-swept-scan",
            "--set", "shipCount=12", "--set", "activeShipCount=6", "--set", "participantSpacing=10",
            "--set", "destinationDistance=20", "--set", "scopeStride=2", "--set", $"motionKind={motionKind}"]))[0];
        RuntimeSweptFixture fixture = InteractionRuntimeSweptBenchmarkScenario.BuildFixture(configuration);
        var view = InteractionSweptQuery.Prepare(fixture.View, fixture.Through);
        foreach (ShipId source in fixture.Ids)
        foreach (ulong range in new ulong[] { 0, 10 })
        {
            InteractionSweptMatch[] expected = fixture.Scope.Where(id => id != source).Select(id =>
            {
                var interest = new ShipPairInteractionInterest(new InteractionInterestId(1), "test", "contact", source, id, range, default);
                return InteractionForecast.Evaluate(interest, fixture.View, fixture.Through) is { } result
                    ? new InteractionSweptMatch(id, result.Candidate.Contact) : null;
            }).OfType<InteractionSweptMatch>().ToArray();
            foreach (var strategy in new[] { InteractionPreparationStrategy.Scan,
                InteractionPreparationStrategy.SparseGrid(16, 4096), InteractionPreparationStrategy.BoundingBoxHierarchy(4) })
                Assert.Equal(expected, view.PrepareScope(fixture.Scope, strategy).Find(source, range));
        }
        if (motionKind == 1)
        {
            var participant = fixture.View.Get(new ShipId(1))!;
            Assert.True(participant.Spatial.Maneuver!.NextBoundary!.Timestamp < fixture.Through);
            Assert.True(participant.Spatial.Maneuver.NextBoundary.Timestamp < participant.ManeuverPlan!.EndsAt);
            Assert.NotEqual(participant.Spatial.Position, participant.ManeuverPlan.StateAt(participant.Spatial.Maneuver.NextBoundary.Timestamp).Position);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void BatchedSweptPresetsMatchPublicReferenceDigest(int workers)
    {
        foreach (string kind in new[] { "scan", "grid", "hierarchy" })
        {
            string? expected = null;
            foreach (string prefix in new[] { "runtime-swept", "batch-swept" })
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var args = new List<string> { "--suite", "full", "--preset", $"interaction.{prefix}-{kind}",
                    "--set", "shipCount=12", "--set", "activeShipCount=6", "--set", "queryCount=12",
                    "--set", "participantSpacing=10", "--set", "motionKind=1",
                    "--set", "warmupIterations=0", "--set", "measuredIterations=2" };
                if (prefix == "batch-swept") args.AddRange(["--set", $"workerCount={workers}", "--set", "batchSize=3"]);
                Assert.True(BenchmarkApplication.Run(args, output, error) == 0, error.ToString());
                using var report = JsonDocument.Parse(output.ToString());
                string digest = report.RootElement.GetProperty("Scenarios")[0].GetProperty("Digest").GetString()!;
                expected ??= digest;
                Assert.Equal(expected, digest);
            }
        }
    }

    private static PrototypeSweepTrajectory[] Trajectories(int kind)
    {
        var first = State(-10, 20000);
        var second = State(10, -20000);
        PrototypeSweepMotion a = kind switch
        {
            0 => new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(new LocalMotionSegment(new MotionId(1),
                new EventGeneration(1), Position(-10), Position(10), SimulationTime.Zero, new SimulationTime(1000)))),
            1 => new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(new AnalyticManeuverSegment(SimulationTime.Zero,
                new SimulationTime(1000), State(-10, 40000), new ShipAcceleration(-80000, 0), ShipAngularRate.Zero))),
            _ => new PrototypeSweepMotion.Plan(ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(
                new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero, first,
                    new ManeuverAcceleration(20001))).WithAdditionalBoundaries([new SimulationTime(500)])),
        };
        PrototypeSweepMotion b = kind switch
        {
            0 => new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(new LocalMotionSegment(new MotionId(2),
                new EventGeneration(1), Position(10), Position(-10), SimulationTime.Zero, new SimulationTime(1000)))),
            1 => new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(new AnalyticManeuverSegment(SimulationTime.Zero,
                new SimulationTime(1000), State(10, -40000), new ShipAcceleration(80000, 0), ShipAngularRate.Zero))),
            _ => new PrototypeSweepMotion.Plan(ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(
                new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero, second,
                    new ManeuverAcceleration(20001)))),
        };
        return [new(new ShipId(1), a), new(new ShipId(2), b), new(new ShipId(3), new PrototypeSweepMotion.Stationary(Position(0))),
            new(new ShipId(4), new PrototypeSweepMotion.Stationary(Position(0, 2)))];
    }

    // Keep the expected evidence on the original exact query APIs, outside index code.
    private static SweptRangeContact? Contact(PrototypeSweepMotion a, PrototypeSweepMotion b, SimulationTime from, SimulationTime through) => (a,b) switch
    {
        (PrototypeSweepMotion.Curve x, PrototypeSweepMotion.Curve y) => InteractionPhaseRangeQuery.Find(x.Geometry, y.Geometry, from, through, 1),
        (PrototypeSweepMotion.Curve x, PrototypeSweepMotion.Stationary y) => InteractionPhaseRangeQuery.Find(x.Geometry,
            InteractionPhaseCurve.Fixed(y.Position, from, through), from, through, 1),
        (PrototypeSweepMotion.Plan x, PrototypeSweepMotion.Plan y) => ManeuverSweptRangeQuery.FindFirstContact(x.Geometry, y.Geometry, from, through, 1),
        (PrototypeSweepMotion.Plan x, PrototypeSweepMotion.Stationary y) => ManeuverSweptRangeQuery.FindFirstContact(x.Geometry, y.Position, from, through, 1),
        _ => throw new InvalidOperationException(),
    };

    private static ShipKinematicState State(long x, long velocity) => new(Position(x), new ShipVelocity(velocity, 0), ShipHeading.Zero);
    private static SystemPosition Position(long x, ulong system = 1) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
