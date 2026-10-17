using System.Text.Json;
using GalaxyCommand.Benchmarks;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class InteractionBenchmarkTests
{
    private const string Preset = "interaction.scoped-proximity";

    [Fact]
    public void ProximityBenchmarkRequiresExplicitFullSuite()
    {
        BenchmarkPreset preset = BenchmarkPresets.Get(Preset);
        Assert.True(preset.IsHeavy);
        Assert.Equal("c09a37599b5eb648", preset.ExpectedDigest);
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(
            BenchmarkCommandLine.Parse(["--preset", Preset])));
    }

    [Fact]
    public void ReducedLinearAndStationaryWorkloadHasExactContactCounts()
    {
        using JsonDocument report = Run("participantSpacing=10", "rangeUnits=0");
        JsonElement scenario = report.RootElement.GetProperty("Scenarios")[0];
        JsonElement counts = scenario.GetProperty("Counts");
        Assert.Equal(4, counts.GetProperty("queries").GetInt64());
        Assert.Equal(16, counts.GetProperty("scopedCandidates").GetInt64());
        Assert.Equal(2, counts.GetProperty("matches").GetInt64());
        Assert.Equal(2, counts.GetProperty("activeShips").GetInt64());
        Assert.Equal(500UL, scenario.GetProperty("SimulatedMilliseconds").GetUInt64());
        Assert.False(scenario.GetProperty("IsCanonical").GetBoolean());
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(100, 0)]
    public void DensityChangesMatchesWithoutChangingCandidateWork(long spacing, long expectedMatches)
    {
        using JsonDocument report = Run($"participantSpacing={spacing}", "rangeUnits=0");
        JsonElement counts = report.RootElement.GetProperty("Scenarios")[0].GetProperty("Counts");
        Assert.Equal(expectedMatches, counts.GetProperty("matches").GetInt64());
        Assert.Equal(16, counts.GetProperty("scopedCandidates").GetInt64());
    }

    [Fact]
    public void DifferentEmptyResultWorkloadsHaveDistinctDigests()
    {
        using JsonDocument first = Run("participantSpacing=100", "rangeUnits=0");
        using JsonDocument second = Run("participantSpacing=200", "rangeUnits=0");
        Assert.NotEqual(first.RootElement.GetProperty("Scenarios")[0].GetProperty("Digest").GetString(),
            second.RootElement.GetProperty("Scenarios")[0].GetProperty("Digest").GetString());
    }

    [Theory]
    [InlineData("participantSpacing=-1")]
    [InlineData("queryCount=0")]
    [InlineData("activeShipCount=5")]
    [InlineData("simulatedDurationMilliseconds=1001")]
    public void InvalidWorkloadIsRejectedBeforeExecution(string invalid)
    {
        var request = BenchmarkCommandLine.Parse(Arguments(invalid));
        BenchmarkUsageException exception = Assert.Throws<BenchmarkUsageException>(() => BenchmarkCommandLine.Resolve(request));
        Assert.Contains(invalid.Split('=')[0], exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(0, 2)]
    public void PreparedBenchmarkMatchesReferenceDigestAndCounts(long spacing, int systems)
    {
        string[] overrides = [$"participantSpacing={spacing}", $"systemCount={systems}", "rangeUnits=10"];
        using JsonDocument reference = Run(overrides);
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = Arguments(overrides).Select(value => value == Preset ? "interaction.prepared-proximity" : value).ToArray();
        Assert.True(BenchmarkApplication.Run(args, output, error) == 0, error.ToString());
        using JsonDocument prepared = JsonDocument.Parse(output.ToString());
        JsonElement expected = reference.RootElement.GetProperty("Scenarios")[0];
        JsonElement actual = prepared.RootElement.GetProperty("Scenarios")[0];
        Assert.Equal(expected.GetProperty("Digest").GetString(), actual.GetProperty("Digest").GetString());
        foreach (JsonProperty count in expected.GetProperty("Counts").EnumerateObject())
            Assert.Equal(count.Value.GetInt64(), actual.GetProperty("Counts").GetProperty(count.Name).GetInt64());
    }

    private static JsonDocument Run(params string[] overrides)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = BenchmarkApplication.Run(Arguments(overrides), output, error);
        Assert.True(code == 0, error.ToString());
        return JsonDocument.Parse(output.ToString());
    }

    private static string[] Arguments(params string[] overrides)
    {
        var args = new List<string> { "--suite", "full", "--preset", Preset };
        string[] settings = ["warmupIterations=0", "measuredIterations=2", "seed=0", "shipCount=4",
            "activeShipCount=2", "queryCount=4", "systemCount=1", "destinationDistance=20",
            "travelDurationMilliseconds=1000", "simulatedDurationMilliseconds=500"];
        var values = settings.Concat(overrides).GroupBy(value => value.Split('=')[0]).Select(group => group.Last());
        foreach (string setting in values) { args.Add("--set"); args.Add(setting); }
        return args.ToArray();
    }
}
