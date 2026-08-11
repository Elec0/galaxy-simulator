using System.Collections.ObjectModel;

namespace GalaxyCommand.Benchmarks;

public enum BenchmarkSuite
{
    Smoke,
    Full,
}

public sealed record BenchmarkCommandRequest
{
    public BenchmarkCommandRequest(
        BenchmarkSuite suite,
        IReadOnlyList<string> presetIds,
        string? scenarioFile,
        IReadOnlyDictionary<string, long> overrides,
        bool showHelp,
        bool listPresets)
    {
        Suite = suite;
        PresetIds = presetIds;
        ScenarioFile = scenarioFile;
        Overrides = overrides;
        ShowHelp = showHelp;
        ListPresets = listPresets;
    }

    public BenchmarkSuite Suite { get; }

    public IReadOnlyList<string> PresetIds { get; }

    public string? ScenarioFile { get; }

    public IReadOnlyDictionary<string, long> Overrides { get; }

    public bool ShowHelp { get; }

    public bool ListPresets { get; }
}

public static class BenchmarkCommandLine
{
    public static BenchmarkCommandRequest Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var presets = new List<string>();
        var overrides = new Dictionary<string, long>(StringComparer.Ordinal);
        BenchmarkSuite suite = BenchmarkSuite.Smoke;
        string? scenarioFile = null;
        bool showHelp = false;
        bool listPresets = false;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--list":
                    listPresets = true;
                    break;
                case "--suite":
                    string suiteValue = NextValue(args, ref index, argument);
                    suite = suiteValue switch
                    {
                        "smoke" => BenchmarkSuite.Smoke,
                        "full" => BenchmarkSuite.Full,
                        _ => throw new BenchmarkUsageException(
                            $"Unknown benchmark suite '{suiteValue}'. Expected smoke or full."),
                    };
                    break;
                case "--preset":
                    presets.Add(NextValue(args, ref index, argument));
                    break;
                case "--scenario-file":
                    if (scenarioFile is not null)
                    {
                        throw new BenchmarkUsageException(
                            "--scenario-file may be supplied only once.");
                    }

                    scenarioFile = NextValue(args, ref index, argument);
                    break;
                case "--set":
                    KeyValuePair<string, long> value =
                        BenchmarkScenarioResolver.ParseOverride(
                            NextValue(args, ref index, argument));
                    if (!overrides.TryAdd(value.Key, value.Value))
                    {
                        throw new BenchmarkUsageException(
                            $"Numeric parameter '{value.Key}' was overridden more than once.");
                    }

                    break;
                default:
                    throw new BenchmarkUsageException(
                        $"Unknown benchmark option '{argument}'.");
            }
        }

        if (scenarioFile is not null && presets.Count > 0)
        {
            throw new BenchmarkUsageException(
                "--scenario-file cannot be combined with --preset.");
        }

        return new BenchmarkCommandRequest(
            suite,
            new ReadOnlyCollection<string>(presets),
            scenarioFile,
            new ReadOnlyDictionary<string, long>(overrides),
            showHelp,
            listPresets);
    }

    public static IReadOnlyList<ResolvedBenchmarkScenario> Resolve(
        BenchmarkCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResolvedBenchmarkScenario[] scenarios;
        if (request.ScenarioFile is not null)
        {
            scenarios =
            [
                BenchmarkScenarioResolver.ResolveFile(
                    request.ScenarioFile,
                    request.Overrides),
            ];
        }
        else
        {
            IReadOnlyList<string> presetIds = request.PresetIds.Count > 0
                ? request.PresetIds
                : request.Suite == BenchmarkSuite.Full
                    ? BenchmarkPresets.All.Select(preset => preset.Id).ToArray()
                    : throw new BenchmarkUsageException(
                        "No smoke benchmark remains after retiring the Phase 1 acceptance fixture. "
                        + "Supply '--suite full' or select a preset with '--suite full --preset ID'.");
            scenarios = presetIds
                .Select(id => BenchmarkScenarioResolver.ResolvePreset(id, request.Overrides))
                .ToArray();
        }

        if (request.Suite != BenchmarkSuite.Full)
        {
            ResolvedBenchmarkScenario? heavy =
                scenarios.FirstOrDefault(scenario => scenario.IsHeavy);
            if (heavy is not null)
            {
                throw new BenchmarkUsageException(
                    $"Scenario '{heavy.Id}' is computationally heavy. "
                    + "Supply '--suite full' explicitly to run it.");
            }
        }

        return scenarios;
    }

    public static string HelpText =>
        """
        Galaxy Command deterministic benchmark runner

        Usage:
          dotnet run --project benchmarks/GalaxyCommand.Benchmarks -- [options]

        Options:
          --suite smoke|full       Smoke has no benchmark fixture; full explicitly enables benchmarks.
          --preset ID              Run one preset; may be repeated.
          --scenario-file PATH     Run one versioned JSON scenario file.
          --set NAME=INTEGER       Override one numeric parameter; may be repeated.
          --list                   List available presets without running them.
          --help                   Show this help.

        Human-readable progress is written to stderr. Machine-readable JSON is written to stdout.
        """;

    private static string NextValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new BenchmarkUsageException(
                $"Option '{option}' requires a value.");
        }

        index++;
        return args[index];
    }
}
