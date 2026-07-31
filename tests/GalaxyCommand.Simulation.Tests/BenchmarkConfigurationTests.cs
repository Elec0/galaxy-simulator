using GalaxyCommand.Benchmarks;

namespace GalaxyCommand.Simulation.Tests;

public sealed class BenchmarkConfigurationTests
{
    [Fact]
    public void DefaultSmokeSuiteResolvesOnlyFastBaseline()
    {
        BenchmarkCommandRequest request = BenchmarkCommandLine.Parse([]);

        ResolvedBenchmarkScenario scenario = Assert.Single(
            BenchmarkCommandLine.Resolve(request));

        Assert.Equal(BenchmarkPresets.PhaseOneBaseline, scenario.Id);
        Assert.False(scenario.IsHeavy);
        Assert.True(scenario.IsCanonical);
    }

    [Fact]
    public void HeavyPresetRequiresExplicitFullSuite()
    {
        BenchmarkCommandRequest request = BenchmarkCommandLine.Parse(
            ["--preset", BenchmarkPresets.SpatialOneCrowded]);

        BenchmarkUsageException exception = Assert.Throws<BenchmarkUsageException>(
            () => BenchmarkCommandLine.Resolve(request));

        Assert.Contains("--suite full", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FullSuiteAllowsHeavyPresetWithoutRunningIt()
    {
        BenchmarkCommandRequest request = BenchmarkCommandLine.Parse(
            [
                "--suite",
                "full",
                "--preset",
                BenchmarkPresets.SpatialOneCrowded,
            ]);

        ResolvedBenchmarkScenario scenario = Assert.Single(
            BenchmarkCommandLine.Resolve(request));

        Assert.True(scenario.IsHeavy);
    }

    [Fact]
    public void NumericOverridesAreVisibleAndMakeRunNonCanonical()
    {
        BenchmarkCommandRequest request = BenchmarkCommandLine.Parse(
            [
                "--set",
                $"{BenchmarkParameterNames.MeasuredIterations}=1",
            ]);

        ResolvedBenchmarkScenario scenario = Assert.Single(
            BenchmarkCommandLine.Resolve(request));

        Assert.Equal(1, scenario.GetInt32(BenchmarkParameterNames.MeasuredIterations));
        Assert.False(scenario.IsCanonical);
        Assert.Null(scenario.ExpectedDigest);
    }

    [Fact]
    public void UnknownNumericOverrideIsRejected()
    {
        BenchmarkCommandRequest request = BenchmarkCommandLine.Parse(
            ["--set", "notAParameter=1"]);

        BenchmarkUsageException exception = Assert.Throws<BenchmarkUsageException>(
            () => BenchmarkCommandLine.Resolve(request));

        Assert.Contains("Unknown numeric parameter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveShipCountCannotExceedShipCount()
    {
        var overrides = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [BenchmarkParameterNames.ShipCount] = 10,
            [BenchmarkParameterNames.ActiveShipCount] = 11,
        };

        BenchmarkUsageException exception = Assert.Throws<BenchmarkUsageException>(
            () => BenchmarkScenarioResolver.ResolvePreset(
                BenchmarkPresets.SpatialOneCrowded,
                overrides));

        Assert.Contains(
            "activeShipCount <= shipCount",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ScenarioFileAndCommandLineOverridesUseDocumentedPrecedence()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": 1,
                  "scenarioId": "test.custom-crowded",
                  "basePreset": "spatial.one-crowded",
                  "basePresetVersion": 1,
                  "parameters": {
                    "shipCount": 20,
                    "activeShipCount": 10
                  }
                }
                """);
            var commandLineOverrides = new Dictionary<string, long>(
                StringComparer.Ordinal)
            {
                [BenchmarkParameterNames.ActiveShipCount] = 5,
            };

            ResolvedBenchmarkScenario scenario =
                BenchmarkScenarioResolver.ResolveFile(
                    path,
                    commandLineOverrides);

            Assert.Equal("test.custom-crowded", scenario.Id);
            Assert.Equal(20, scenario.GetInt32(BenchmarkParameterNames.ShipCount));
            Assert.Equal(5, scenario.GetInt32(BenchmarkParameterNames.ActiveShipCount));
            Assert.Equal(1, scenario.GetInt32(BenchmarkParameterNames.SystemCount));
            Assert.False(scenario.IsCanonical);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SmokeApplicationRunsFastCorrectnessScenario()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = BenchmarkApplication.Run(
            [
                "--set",
                $"{BenchmarkParameterNames.WarmupIterations}=0",
                "--set",
                $"{BenchmarkParameterNames.MeasuredIterations}=1",
            ],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "\"Id\": \"baseline.phase-one\"",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"SchemaVersion\": 2",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"DomainMeasurementsAvailability\": \"unavailable\"",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("benchmark_failure", error.ToString(), StringComparison.Ordinal);
    }
}
