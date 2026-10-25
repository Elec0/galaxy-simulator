using GalaxyCommand.Benchmarks;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class InteractionSessionBenchmarkTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 16)]
    public void StationaryTargetSessionBenchmarkMatchesItsSerialReference(int workers, int batch)
    {
        var serial = BenchmarkScenarioResolver.ResolvePreset("interaction.stationary-session-serial");
        var batched = BenchmarkScenarioResolver.ResolvePreset("interaction.stationary-session-batched",
            new Dictionary<string, long> { ["workerCount"] = workers, ["batchSize"] = batch });
        ScenarioCorrectnessResult expected = BenchmarkScenarioFactory.Create(serial.BasePreset).Run(serial);
        ScenarioCorrectnessResult actual = BenchmarkScenarioFactory.Create(batched.BasePreset).Run(batched);
        Assert.True(expected.Counts.GetValueOrDefault("stationaryInterests") > 0);
        Assert.True(expected.Counts["effects"] > 0);
        Assert.True(expected.Counts["interestsUpdated"] > 0);
        Assert.True(expected.Counts["interestsCanceled"] > 0);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.Counts.OrderBy(pair => pair.Key), actual.Counts.OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 7)]
    [InlineData(4, 64)]
    [InlineData(8, 16)]
    public void AdvancingSessionPreservesContactHistoryAcrossExecutionSettings(int workers, int batch)
    {
        var serial = BenchmarkScenarioResolver.ResolvePreset("interaction.session-serial");
        var parallel = BenchmarkScenarioResolver.ResolvePreset("interaction.session-batched",
            new Dictionary<string, long> { ["workerCount"] = workers, ["batchSize"] = batch });
        var expected = BenchmarkScenarioFactory.Create(serial.BasePreset).Run(serial);
        var actual = BenchmarkScenarioFactory.Create(parallel.BasePreset).Run(parallel);
        Assert.Equal("1079ca5a77a8341f", expected.Digest);
        Assert.Equal(serial.ExpectedDigest, expected.Digest);
        Assert.Equal(1, serial.BasePresetVersion);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.Counts.OrderBy(x => x.Key), actual.Counts.OrderBy(x => x.Key));
        Assert.True(actual.Counts["effects"] > 0);
        Assert.True(actual.Counts["interestsUpdated"] > 0);
        Assert.True(actual.Counts["interestsCanceled"] > 0);
        Assert.True(actual.Counts["interactionEventsIgnored"] > 0);
        Assert.Equal(6000UL, actual.SimulatedMilliseconds);
    }

    [Fact]
    public void ReplanningWorkloadChangesDigestAndChurnEvidence()
    {
        var original = BenchmarkScenarioResolver.ResolvePreset("interaction.session-serial");
        var changed = BenchmarkScenarioResolver.ResolvePreset("interaction.session-serial",
            new Dictionary<string, long> { ["replanCount"] = 2 });
        var first = BenchmarkScenarioFactory.Create(original.BasePreset).Run(original);
        var second = BenchmarkScenarioFactory.Create(changed.BasePreset).Run(changed);
        Assert.NotEqual(first.Digest, second.Digest);
        Assert.True(first.Counts["interestsUpdated"] > second.Counts["interestsUpdated"]);
        Assert.True(first.Counts["interestsCanceled"] > second.Counts["interestsCanceled"]);
        Assert.True(first.Counts["commands"] > second.Counts["commands"]);
    }

    [Theory]
    [InlineData("workerCount", 0)]
    [InlineData("batchSize", 0)]
    [InlineData("interestCount", 0)]
    [InlineData("activeShipCount", 0)]
    [InlineData("activeShipCount", 32)]
    [InlineData("systemCount", 33)]
    [InlineData("replanIntervalMilliseconds", 6001)]
    public void InvalidSessionWorkloadsAreRejectedBeforeExecution(string parameter, long value)
    {
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkScenarioResolver.ResolvePreset(
            "interaction.session-batched", new Dictionary<string, long> { [parameter] = value }));
    }
}
