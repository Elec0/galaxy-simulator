using System.Text.Json;
using GalaxyCommand.Benchmarks;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class CargoComponentBenchmarkTests
{
    private static readonly string[] LossDomains = ["cargo.disposition", "cargo.expiry"];

    [Theory]
    [InlineData(BenchmarkPresets.CargoComponentSeparateLosses)]
    [InlineData(BenchmarkPresets.CargoComponentOverlapChain)]
    [InlineData(BenchmarkPresets.CargoComponentLargeFungible)]
    [InlineData(BenchmarkPresets.CargoComponentContestedSerial)]
    [InlineData(BenchmarkPresets.CargoComponentContestedBatched)]
    public void CanonicalCargoWorkloadMatchesRecordedAuthorityDigest(string preset)
    {
        ResolvedBenchmarkScenario configuration = BenchmarkScenarioResolver.ResolvePreset(preset);
        ScenarioCorrectnessResult actual = BenchmarkScenarioFactory.Create(preset).Run(configuration);
        Assert.NotNull(configuration.ExpectedDigest);
        Assert.Equal(configuration.ExpectedDigest, actual.Digest);
    }
    [Theory]
    [InlineData(BenchmarkPresets.CargoComponentSeparateLosses, 4, 0, 4)]
    [InlineData(BenchmarkPresets.CargoComponentOverlapChain, 1, 3, 1)]
    public void RepeatedLossShapesRetainExactCirclesAliasesReceiptsAndExpiryTombstones(string preset, long fields, long aliases, long tombstones)
    {
        ScenarioCorrectnessResult result = Run(preset, new Dictionary<string, long>
            { ["lossOccurrenceCount"] = 4, ["cargoUnitsPerLoss"] = 100 });
        Assert.Equal(4, result.Counts["lossOccurrences"]);
        Assert.Equal(16, result.Counts["objectsPublished"]);
        Assert.Equal(fields, result.Counts["fieldsAtPublication"]);
        Assert.Equal(4, result.Counts["circlesAtPublication"]);
        Assert.Equal(aliases, result.Counts["retainedAliases"]);
        Assert.Equal(tombstones, result.Counts["retainedTombstones"]);
        Assert.Equal(4, result.Counts["retainedReceipts"]);
        Assert.Equal(16, result.Counts["objectsExpired"]);
        Assert.Equal(0, result.Counts["retainedCircles"]);
        Assert.Equal(0, result.Counts["agendaEntries"]);
        Assert.Equal(200, result.Counts["recoverableUnits"]);
        Assert.Equal(0, result.Counts["sourceRemainingUnits"]);
        Assert.InRange(result.Counts["retainedFacts"], 1, 256);
        Assert.Equal(16, result.Counts["eventsProcessed"]);
        Assert.Equal(0, result.Counts["commandsSubmitted"]);
        Assert.Equal(LossDomains, result.DomainMeasurements!.Keys.Order());
    }

    [Fact]
    public void LargeFungibleQuantityRemainsBoundedBySmallConfiguredObjectCount()
    {
        ScenarioCorrectnessResult result = Run(BenchmarkPresets.CargoComponentLargeFungible,
            new Dictionary<string, long> { ["lossOccurrenceCount"] = 4 });
        Assert.Equal(16, result.Counts["objectsPublished"]);
        Assert.Equal(2_000_000_000_000, result.Counts["recoverableUnits"]);
        Assert.Equal(4, result.Counts["retainedReceipts"]);
        Assert.Equal(16, result.Counts["objectsExpired"]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    public void CrowdedTimersMatchSerialAuthorityAcrossSupportedWorkerAndBatchSettings(int workers, int batch)
    {
        ScenarioCorrectnessResult expected = Run(BenchmarkPresets.CargoComponentContestedSerial,
            new Dictionary<string, long> { ["collectorCount"] = 4 });
        ScenarioCorrectnessResult actual = Run(BenchmarkPresets.CargoComponentContestedBatched,
            new Dictionary<string, long> { ["collectorCount"] = 4, ["workerCount"] = workers, ["batchSize"] = batch });
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.Counts.OrderBy(pair => pair.Key), actual.Counts.OrderBy(pair => pair.Key));
        Assert.Equal(4, actual.Counts["timersStarted"]);
        Assert.Equal(1, actual.Counts["ordersCompleted"]);
        Assert.Equal(3, actual.Counts["ordersFailed"]);
        Assert.Equal(5, actual.Counts["winningShip"]);
        Assert.Equal(3, actual.Counts["objectsRemaining"]);
        Assert.Equal(0, actual.Counts["incomingReservations"]);
        Assert.Equal(3, actual.Counts["agendaEntries"]);
        Assert.True(actual.Counts["retainedFacts"] > 0);
        Assert.True(actual.Counts["eventsProcessed"] >= 4);
        Assert.Equal(5, actual.Counts["commandsSubmitted"]);
    }

    [Fact]
    public void WorkloadAndSeedChangesAffectAuthorityDigest()
    {
        ScenarioCorrectnessResult original = Run(BenchmarkPresets.CargoComponentSeparateLosses,
            new Dictionary<string, long> { ["lossOccurrenceCount"] = 2 });
        ScenarioCorrectnessResult extra = Run(BenchmarkPresets.CargoComponentSeparateLosses,
            new Dictionary<string, long> { ["lossOccurrenceCount"] = 3 });
        ScenarioCorrectnessResult changedSeed = Run(BenchmarkPresets.CargoComponentSeparateLosses,
            new Dictionary<string, long> { ["lossOccurrenceCount"] = 2, ["seed"] = 2 });
        Assert.NotEqual(original.Digest, extra.Digest);
        Assert.NotEqual(original.Digest, changedSeed.Digest);
        Assert.Equal(12, extra.Counts["objectsPublished"]);
    }

    [Theory]
    [InlineData("lossOccurrenceCount", 0)]
    [InlineData("cargoUnitsPerLoss", 1)]
    [InlineData("cargoUnitsPerLoss", 1000000000000001)]
    public void InvalidLossScaleRejectsBeforeFixtureAllocation(string parameter, long value)
    {
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkScenarioResolver.ResolvePreset(
            BenchmarkPresets.CargoComponentSeparateLosses, new Dictionary<string, long> { [parameter] = value }));
    }

    [Fact]
    public void CargoReportIncludesSeparateDomainMeasurementsOutsideCorrectnessCounts()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int result = BenchmarkApplication.Run(["--suite", "full", "--preset", BenchmarkPresets.CargoComponentSeparateLosses,
            "--set", "lossOccurrenceCount=2", "--set", "warmupIterations=0", "--set", "measuredIterations=1"], output, errors);
        Assert.Equal(0, result);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement report = json.RootElement.GetProperty("Scenarios")[0];
        Assert.Equal("available", report.GetProperty("DomainMeasurementsAvailability").GetString());
        JsonElement domain = report.GetProperty("DomainMeasurements").GetProperty("cargo.disposition");
        Assert.True(domain.TryGetProperty("Milliseconds", out _));
        Assert.True(domain.TryGetProperty("AllocatedBytes", out _));
    }

    private static ScenarioCorrectnessResult Run(string preset, IReadOnlyDictionary<string, long> overrides)
    {
        ResolvedBenchmarkScenario scenario = BenchmarkScenarioResolver.ResolvePreset(preset, overrides);
        return BenchmarkScenarioFactory.Create(scenario.BasePreset).Run(scenario);
    }
}
