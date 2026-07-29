using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalaxyCommand.Benchmarks;

public sealed record BenchmarkMachine(
    string Framework,
    string OperatingSystem,
    string ProcessArchitecture,
    int ProcessorCount,
    bool ServerGarbageCollection);

public sealed record BenchmarkTiming(
    double MedianMilliseconds,
    double MinimumMilliseconds,
    double MaximumMilliseconds,
    long MedianAllocatedBytes,
    int Generation0Collections,
    int Generation1Collections,
    int Generation2Collections);

public sealed record BenchmarkScenarioReport(
    string Id,
    string BasePreset,
    int BasePresetVersion,
    bool IsCanonical,
    IReadOnlyDictionary<string, long> Parameters,
    string Digest,
    ulong SimulatedMilliseconds,
    IReadOnlyDictionary<string, long> Counts,
    BenchmarkTiming Timing);

public sealed record BenchmarkRunReport(
    int SchemaVersion,
    DateTimeOffset RecordedAtUtc,
    string Suite,
    BenchmarkMachine Machine,
    IReadOnlyList<BenchmarkScenarioReport> Scenarios);

public static class BenchmarkApplication
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

    public static int Run(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        try
        {
            BenchmarkCommandRequest request = BenchmarkCommandLine.Parse(args);
            if (request.ShowHelp)
            {
                standardOutput.WriteLine(BenchmarkCommandLine.HelpText);
                return 0;
            }

            if (request.ListPresets)
            {
                WritePresets(standardOutput);
                return 0;
            }

            IReadOnlyList<ResolvedBenchmarkScenario> configurations =
                BenchmarkCommandLine.Resolve(request);
            var reports = new List<BenchmarkScenarioReport>(configurations.Count);
            foreach (ResolvedBenchmarkScenario configuration in configurations)
            {
                standardError.WriteLine(
                    $"benchmark_start id={configuration.Id} canonical={configuration.IsCanonical}");
                BenchmarkScenarioReport report = RunScenario(configuration);
                reports.Add(report);
                standardError.WriteLine(
                    $"benchmark_complete id={configuration.Id} "
                    + $"digest={report.Digest} "
                    + $"median_ms={report.Timing.MedianMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
            }

            var run = new BenchmarkRunReport(
                1,
                DateTimeOffset.UtcNow,
                request.Suite.ToString().ToLowerInvariant(),
                CaptureMachine(),
                new ReadOnlyCollection<BenchmarkScenarioReport>(reports));
            standardOutput.WriteLine(JsonSerializer.Serialize(run, JsonOptions));
            return 0;
        }
        catch (BenchmarkUsageException exception)
        {
            standardError.WriteLine($"benchmark_usage_error: {exception.Message}");
            standardError.WriteLine("Use --help for command syntax.");
            return 2;
        }
        catch (Exception exception)
        {
            standardError.WriteLine($"benchmark_failure: {exception}");
            return 1;
        }
    }

    private static BenchmarkScenarioReport RunScenario(
        ResolvedBenchmarkScenario configuration)
    {
        IBenchmarkScenario scenario = BenchmarkScenarioFactory.Create(
            configuration.BasePreset);
        int warmupIterations = configuration.GetInt32(
            BenchmarkParameterNames.WarmupIterations);
        int measuredIterations = configuration.GetInt32(
            BenchmarkParameterNames.MeasuredIterations);
        for (int index = 0; index < warmupIterations; index++)
        {
            scenario.Run(configuration);
        }

        var elapsed = new double[measuredIterations];
        var allocated = new long[measuredIterations];
        int generation0Before = GC.CollectionCount(0);
        int generation1Before = GC.CollectionCount(1);
        int generation2Before = GC.CollectionCount(2);
        ScenarioCorrectnessResult? reference = null;
        for (int index = 0; index < measuredIterations; index++)
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(true);
            long started = Stopwatch.GetTimestamp();
            ScenarioCorrectnessResult result = scenario.Run(configuration);
            elapsed[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated[index] = checked(
                GC.GetTotalAllocatedBytes(true) - allocatedBefore);
            if (reference is null)
            {
                reference = result;
            }
            else
            {
                RequireEquivalent(configuration.Id, reference, result);
            }
        }

        ScenarioCorrectnessResult correctness = reference
            ?? throw new InvalidOperationException(
                $"Scenario '{configuration.Id}' did not execute a measured iteration.");
        if (configuration.ExpectedDigest is { } expected
            && !StringComparer.Ordinal.Equals(expected, correctness.Digest))
        {
            throw new InvalidOperationException(
                $"Scenario '{configuration.Id}' produced digest {correctness.Digest}; "
                + $"expected {expected}.");
        }

        Array.Sort(elapsed);
        Array.Sort(allocated);
        var timing = new BenchmarkTiming(
            Median(elapsed),
            elapsed[0],
            elapsed[^1],
            Median(allocated),
            GC.CollectionCount(0) - generation0Before,
            GC.CollectionCount(1) - generation1Before,
            GC.CollectionCount(2) - generation2Before);
        return new BenchmarkScenarioReport(
            configuration.Id,
            configuration.BasePreset,
            configuration.BasePresetVersion,
            configuration.IsCanonical,
            configuration.Parameters,
            correctness.Digest,
            correctness.SimulatedMilliseconds,
            correctness.Counts,
            timing);
    }

    private static void RequireEquivalent(
        string scenarioId,
        ScenarioCorrectnessResult expected,
        ScenarioCorrectnessResult actual)
    {
        if (!StringComparer.Ordinal.Equals(expected.Digest, actual.Digest)
            || expected.SimulatedMilliseconds != actual.SimulatedMilliseconds
            || expected.Counts.Count != actual.Counts.Count
            || expected.Counts.Any(pair =>
                !actual.Counts.TryGetValue(pair.Key, out long value)
                || value != pair.Value))
        {
            throw new InvalidOperationException(
                $"Scenario '{scenarioId}' produced different correctness results across iterations.");
        }
    }

    private static double Median(double[] sorted) =>
        sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

    private static long Median(long[] sorted) =>
        sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : checked((sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2);

    private static BenchmarkMachine CaptureMachine() =>
        new(
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            System.Runtime.GCSettings.IsServerGC);

    private static void WritePresets(TextWriter writer)
    {
        foreach (BenchmarkPreset preset in BenchmarkPresets.All)
        {
            writer.WriteLine(
                $"{preset.Id}\tv{preset.Version}\t"
                + $"{(preset.IsHeavy ? "full" : "smoke")}");
            foreach ((string name, long value) in preset.Parameters)
            {
                writer.WriteLine(
                    $"  {name}={value.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }
}
