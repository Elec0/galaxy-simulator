using System.Numerics;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class StationRuntimeBenchmarkTests
{
    [Theory]
    [InlineData("station.many-quiet-sites", "e21d7b29856a7fdc")]
    [InlineData("station.one-crowded", "20d3c733a3ad8d39")]
    [InlineData("station.partial-deliveries", "f9bc7472872ca386")]
    public void CanonicalPresetsPinTheGateFormatVersion(string preset, string digest)
    {
        ResolvedBenchmarkScenario configuration = BenchmarkScenarioResolver.ResolvePreset(preset, new Dictionary<string, long>());
        Assert.Equal(4, configuration.BasePresetVersion);
        Assert.Equal(digest, configuration.ExpectedDigest);
    }

    [Theory]
    [InlineData("station.many-quiet-sites")]
    [InlineData("station.one-crowded")]
    [InlineData("station.partial-deliveries")]
    public void RealStationLifecyclePublishesRecoveryAndPreservesRestoreContinuation(string preset)
    {
        ResolvedBenchmarkScenario configuration = Small(preset);
        StationRuntimeBenchmarkProof proof = StationRuntimeBenchmarkScenario.RunProof(configuration);
        Assert.Equal(2, proof.Result.Counts["removedStations"]);
        Assert.Equal(2, proof.Result.Counts["quietSites"]);
        Assert.Equal(8, proof.Result.Counts["recoveredMaterials"]);
        Assert.True(proof.Result.Counts["stationWorkEvents"] > 0);
        Assert.True(proof.Result.Counts["observedStationChanges"] > 0);
        Assert.Equal(1, proof.Result.Counts["restores"]);
        Assert.NotEmpty(proof.Checkpoints[1].Lifecycle.Inventories.Inventories.SelectMany(inventory => inventory.PhysicalReservations));
    }

    [Theory]
    [InlineData(1, 1, 11, 0)]
    [InlineData(2, 3, 37, 8001)]
    [InlineData(4, 16, 7, 5000)]
    [InlineData(8, 7, 17, 11999)]
    public void HostWorkersAdvancePartitionsAndRestoreDoNotChangeStationEffects(int workers, int batch, int partitions, int restoreAt)
    {
        var scenario = new StationRuntimeBenchmarkScenario();
        ScenarioCorrectnessResult expected = scenario.Run(Small("station.partial-deliveries"));
        ScenarioCorrectnessResult actual = scenario.Run(Small("station.partial-deliveries", new()
        {
            ["workerCount"] = workers, ["batchSize"] = batch, ["advancePartitions"] = partitions,
            ["restoreAtMilliseconds"] = restoreAt,
        }));
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.Counts["stationWorkEvents"], actual.Counts["stationWorkEvents"]);
        Assert.Equal(expected.Counts["recoveredMaterials"], actual.Counts["recoveredMaterials"]);
    }

    [Theory]
    [InlineData(0, 2, 1, 0)]
    [InlineData(2, 1, 37, 9001)]
    [InlineData(4, 7, 19, 10501)]
    public void GroupedStorageThenBaseLossAndCollectionPreserveEveryHostLayout(int workers, int batch, int partitions, int restoreAt)
    {
        StationRuntimeBenchmarkProof expected = StationRuntimeBenchmarkScenario.RunProof(Small("station.partial-deliveries", new()
            { ["destructionLifecycle"] = 1, ["restoreAtMilliseconds"] = 9001 }));
        StationRuntimeBenchmarkProof actual = StationRuntimeBenchmarkScenario.RunProof(Small("station.partial-deliveries", new()
        {
            ["destructionLifecycle"] = 1, ["workerCount"] = workers, ["batchSize"] = batch,
            ["advancePartitions"] = partitions, ["restoreAtMilliseconds"] = restoreAt,
        }));
        Assert.Equal(expected.Result.Digest, actual.Result.Digest);
        Assert.Equal(4, actual.Result.Counts["destructions"]);
        Assert.Equal(38, actual.Result.Counts["recoveredMaterials"]);
        Assert.Empty(actual.Final.Cargo!.Objects);
        Assert.Equal(4, actual.Final.Cargo.Receipts.Count);
        Assert.NotEmpty(actual.Checkpoints.SelectMany(value => value.Stations!.Destructions));
    }

    [Fact]
    public void CheckpointDigestIncludesPrivateWorkModulesClaimsReceiptsEventsAndLossWindows()
    {
        StationRuntimeBenchmarkProof proof = StationRuntimeBenchmarkScenario.RunProof(Small("station.one-crowded"));
        GameSessionCheckpoint partial = proof.Checkpoints[0];
        StationStateCheckpoint site = partial.Stations!.Stations.First(station => station.Projects[0].Assignments.Count != 0);
        StationProjectCheckpoint project = site.Projects[0];
        GameSessionCheckpoint changed = partial with
        {
            Stations = partial.Stations with
            {
                Stations = partial.Stations.Stations.Select(station => station.Id == site.Id
                    ? station with { Projects = [project with { WorkNumerator = project.WorkNumerator + 1 }] } : station).ToArray(),
            },
        };
        Assert.NotEqual(StationCheckpointDigest.Compute(partial), StationCheckpointDigest.Compute(changed));
        GameSessionCheckpoint changedWindow = partial with
        {
            Stations = partial.Stations with
            {
                Stations = partial.Stations.Stations.Select(station => station.Id == site.Id ? station with
                {
                    LossWindow = new StationStorageLossWindow(BigInteger.One, new Dictionary<QualifiedContentKey, BigInteger>()),
                    LossOccurrence = station.LossOccurrence + 1,
                } : station).ToArray(),
            },
        };
        Assert.NotEqual(StationCheckpointDigest.Compute(partial), StationCheckpointDigest.Compute(changedWindow));
        GameSessionCheckpoint claim = proof.Checkpoints[1];
        StationModuleCheckpoint module = claim.Stations!.Stations.SelectMany(station => station.Modules).First();
        Assert.NotEqual(StationCheckpointDigest.ComputeNode(module), StationCheckpointDigest.ComputeNode(module with { OccupiedCapacity = module.OccupiedCapacity + 1 }));
        PhysicalReservation reservation = claim.Lifecycle.Inventories.Inventories.SelectMany(inventory => inventory.PhysicalReservations).First();
        Assert.NotEqual(StationCheckpointDigest.ComputeNode(reservation), StationCheckpointDigest.ComputeNode(reservation with
            { Subject = new PhysicalReservationSubject.IncomingCapacity(new(2)) }));
        StationCommandReceiptCheckpoint receipt = claim.Stations.Receipts[0];
        Assert.NotEqual(StationCheckpointDigest.ComputeNode(receipt), StationCheckpointDigest.ComputeNode(receipt with
            { Request = new(new(receipt.Request.Sequence.Value + 1), receipt.Request.SubmittedAt, receipt.Request.Source, receipt.Request.Command) }));
        GameEventRecord scheduled = proof.Events[0];
        Assert.NotEqual(StationCheckpointDigest.ComputeNode(scheduled), StationCheckpointDigest.ComputeNode(scheduled with { CreationSequence = scheduled.CreationSequence + 1 }));
        Assert.NotEqual(StationCheckpointDigest.ComputeNode(new ConstructionRecipe([], new(1))),
            StationCheckpointDigest.ComputeNode(new ConstructionRecipe([], new(2))));
        Assert.NotEqual(StationCheckpointDigest.ComputeNode(StationStorageLossWindow.Empty),
            StationCheckpointDigest.ComputeNode(new StationStorageLossWindow(BigInteger.One, new Dictionary<QualifiedContentKey, BigInteger>())));
    }

    [Fact]
    public void GameplayDigestIgnoresWakeAllocationButRetainsExactPrivateProgress()
    {
        StationRuntimeBenchmarkProof proof = StationRuntimeBenchmarkScenario.RunProof(Small("station.one-crowded"));
        GameSessionCheckpoint partial = proof.Checkpoints[0];
        EventAgendaCheckpoint<GameEvent> agenda = partial.Engine.Agenda;
        GameSessionCheckpoint changed = partial with
        {
            Engine = new SimulationEngineCheckpoint<GameEvent>(partial.Engine.IsInitialized, partial.Engine.AccruedThrough,
                new(agenda.CurrentTime, agenda.NextCreationSequence + 1, agenda.PendingEvents)),
        };
        Assert.NotEqual(StationCheckpointDigest.Compute(partial), StationCheckpointDigest.Compute(changed));
        Assert.Equal(StationCheckpointDigest.ComputeGameplay(partial), StationCheckpointDigest.ComputeGameplay(changed));
        StationStateCheckpoint station = partial.Stations!.Stations.First(value => value.Projects[0].Assignments.Count != 0);
        changed = partial with { Stations = partial.Stations with { Stations = partial.Stations.Stations.Select(value => value.Id == station.Id
            ? value with { Projects = [value.Projects[0] with { WorkNumerator = value.Projects[0].WorkNumerator + 1 }] } : value).ToArray() } };
        Assert.NotEqual(StationCheckpointDigest.ComputeGameplay(partial), StationCheckpointDigest.ComputeGameplay(changed));
    }

    [Fact]
    public void CanonicalDigestSortsDictionaryKeysAndRejectsDelegates()
    {
        Assert.Equal(StationCheckpointDigest.ComputeNode(new Dictionary<string, ulong> { ["b"] = 2, ["a"] = 1 }),
            StationCheckpointDigest.ComputeNode(new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 2 }));
        Assert.Throws<InvalidOperationException>(() => StationCheckpointDigest.ComputeNode((Action)(() => { })));
    }

    [Theory]
    [InlineData("stationCount", 0)]
    [InlineData("activeStationCount", 5)]
    [InlineData("deliveryWaves", 7)]
    [InlineData("buildersPerProject", 0)]
    [InlineData("advancePartitions", 0)]
    [InlineData("restoreAtMilliseconds", 12000)]
    public void InvalidWorkloadsRejectBeforeSessionCreation(string parameter, long value) =>
        Assert.Throws<BenchmarkUsageException>(() => Small("station.partial-deliveries", new() { [parameter] = value }));

    private static ResolvedBenchmarkScenario Small(string preset, Dictionary<string, long>? additional = null)
    {
        var parameters = new Dictionary<string, long>
        {
            ["stationCount"] = 4, ["activeStationCount"] = 2, ["systemCount"] = 2, ["buildersPerProject"] = 2,
            ["constructionWork"] = 12, ["constructionMaterials"] = 6, ["builderThroughput"] = 3,
            ["deliveryWaves"] = 3, ["simulatedDurationMilliseconds"] = 12000,
            ["workerCount"] = 0, ["batchSize"] = 2, ["advancePartitions"] = 1, ["restoreAtMilliseconds"] = 8001,
        };
        foreach ((string key, long value) in additional ?? []) parameters[key] = value;
        return BenchmarkScenarioResolver.ResolvePreset(preset, parameters);
    }
}
