using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalaxyCommand.Benchmarks;

public static class BenchmarkParameterNames
{
    public const string WarmupIterations = "warmupIterations";
    public const string MeasuredIterations = "measuredIterations";
    public const string Seed = "seed";
    public const string SimulatedDurationMilliseconds = "simulatedDurationMilliseconds";
    public const string SystemCount = "systemCount";
    public const string ShipCount = "shipCount";
    public const string ActiveShipCount = "activeShipCount";
    public const string CommandCount = "commandCount";
    public const string FactRetentionCapacity = "factRetentionCapacity";
    public const string TravelDurationMilliseconds = "travelDurationMilliseconds";
    public const string DestinationDistance = "destinationDistance";
}

public sealed record BenchmarkPreset
{
    public BenchmarkPreset(
        string id,
        bool isHeavy,
        IReadOnlyDictionary<string, long> parameters,
        string? expectedDigest = null,
        int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        Id = id;
        Version = version;
        IsHeavy = isHeavy;
        Parameters = CopyParameters(parameters);
        ExpectedDigest = expectedDigest;
    }

    public string Id { get; }

    public int Version { get; }

    public bool IsHeavy { get; }

    public IReadOnlyDictionary<string, long> Parameters { get; }

    public string? ExpectedDigest { get; }

    private static ReadOnlyDictionary<string, long> CopyParameters(
        IReadOnlyDictionary<string, long> parameters)
    {
        var copy = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach ((string name, long value) in parameters)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            copy.Add(name, value);
        }

        return new ReadOnlyDictionary<string, long>(copy);
    }
}

public static class BenchmarkPresets
{
    public const string SpatialManyQuiet = "spatial.many-quiet";
    public const string SpatialOneCrowded = "spatial.one-crowded";
    public const string NavigationConnectorVolume = "navigation.connector-volume";
    public const string FactsRetentionAndRead = "facts.retention-and-read";

    private static readonly ReadOnlyDictionary<string, BenchmarkPreset> ValuesById =
        CreateValues();

    public static IReadOnlyList<BenchmarkPreset> All { get; } =
        new ReadOnlyCollection<BenchmarkPreset>(
            ValuesById.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray());

    public static BenchmarkPreset Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return ValuesById.GetValueOrDefault(id)
            ?? throw new BenchmarkUsageException($"Unknown benchmark preset '{id}'.");
    }

    private static ReadOnlyDictionary<string, BenchmarkPreset> CreateValues()
    {
        BenchmarkPreset[] values =
        [
            new(
                SpatialManyQuiet,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.SystemCount, 128),
                    (BenchmarkParameterNames.ShipCount, 10_000),
                    (BenchmarkParameterNames.ActiveShipCount, 5_000),
                    (BenchmarkParameterNames.FactRetentionCapacity, 50_000),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.DestinationDistance, 100)),
                "4a587cefd3335149"),
            new(
                SpatialOneCrowded,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.SystemCount, 1),
                    (BenchmarkParameterNames.ShipCount, 2_500),
                    (BenchmarkParameterNames.ActiveShipCount, 2_500),
                    (BenchmarkParameterNames.FactRetentionCapacity, 25_000),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.DestinationDistance, 100)),
                "2e6e6c8a57013a7d"),
            new(
                NavigationConnectorVolume,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 10_000),
                    (BenchmarkParameterNames.SystemCount, 32),
                    (BenchmarkParameterNames.ShipCount, 1_000),
                    (BenchmarkParameterNames.ActiveShipCount, 1_000),
                    (BenchmarkParameterNames.FactRetentionCapacity, 100_000),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 100),
                    (BenchmarkParameterNames.DestinationDistance, 100)),
                "d76e990b96a552f7"),
            new(
                FactsRetentionAndRead,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.CommandCount, 20_000),
                    (BenchmarkParameterNames.FactRetentionCapacity, 50_000),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.DestinationDistance, 100)),
                "f9bdd5df868167d8"),
        ];
        return new ReadOnlyDictionary<string, BenchmarkPreset>(
            values.ToDictionary(value => value.Id, StringComparer.Ordinal));
    }

    private static ReadOnlyDictionary<string, long> Parameters(
        params (string Name, long Value)[] values) =>
        new ReadOnlyDictionary<string, long>(
            values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal));
}

public sealed class BenchmarkScenarioFile
{
    public int SchemaVersion { get; init; } = 1;

    public string ScenarioId { get; init; } = string.Empty;

    public string BasePreset { get; init; } = string.Empty;

    public int BasePresetVersion { get; init; } = 1;

    public Dictionary<string, long> Parameters { get; init; } =
        new(StringComparer.Ordinal);
}

public sealed record ResolvedBenchmarkScenario
{
    public ResolvedBenchmarkScenario(
        string id,
        string basePreset,
        int basePresetVersion,
        bool isHeavy,
        bool isCanonical,
        IReadOnlyDictionary<string, long> parameters,
        string? expectedDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePreset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(basePresetVersion);
        ArgumentNullException.ThrowIfNull(parameters);
        Id = id;
        BasePreset = basePreset;
        BasePresetVersion = basePresetVersion;
        IsHeavy = isHeavy;
        IsCanonical = isCanonical;
        var sortedParameters = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach ((string name, long value) in parameters)
        {
            sortedParameters.Add(name, value);
        }

        Parameters = new ReadOnlyDictionary<string, long>(sortedParameters);
        ExpectedDigest = expectedDigest;
    }

    public string Id { get; }

    public string BasePreset { get; }

    public int BasePresetVersion { get; }

    public bool IsHeavy { get; }

    public bool IsCanonical { get; }

    public IReadOnlyDictionary<string, long> Parameters { get; }

    public string? ExpectedDigest { get; }

    public long Get(string name) =>
        Parameters.TryGetValue(name, out long value)
            ? value
            : throw new BenchmarkUsageException(
                $"Scenario '{Id}' does not define required parameter '{name}'.");

    public int GetInt32(string name) =>
        checked((int)Get(name));

    public ulong GetUInt64(string name) =>
        checked((ulong)Get(name));
}

public static class BenchmarkScenarioResolver
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    public static ResolvedBenchmarkScenario ResolvePreset(
        string presetId,
        IReadOnlyDictionary<string, long>? overrides = null)
    {
        BenchmarkPreset preset = BenchmarkPresets.Get(presetId);
        IReadOnlyDictionary<string, long> appliedOverrides =
            overrides ?? new Dictionary<string, long>();
        Dictionary<string, long> parameters = ApplyOverrides(
            preset.Parameters,
            appliedOverrides);
        var scenario = new ResolvedBenchmarkScenario(
            preset.Id,
            preset.Id,
            preset.Version,
            preset.IsHeavy,
            appliedOverrides.Count == 0,
            parameters,
            appliedOverrides.Count == 0 ? preset.ExpectedDigest : null);
        BenchmarkScenarioValidator.Validate(scenario);
        return scenario;
    }

    public static ResolvedBenchmarkScenario ResolveFile(
        string path,
        IReadOnlyDictionary<string, long>? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        BenchmarkScenarioFile file;
        try
        {
            file = JsonSerializer.Deserialize<BenchmarkScenarioFile>(
                json,
                JsonOptions)
                ?? throw new BenchmarkUsageException(
                    $"Scenario file '{path}' did not contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new BenchmarkUsageException(
                $"Scenario file '{path}' is invalid: {exception.Message}");
        }

        if (file.SchemaVersion != 1)
        {
            throw new BenchmarkUsageException(
                $"Scenario file '{path}' uses unsupported schema version {file.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(file.ScenarioId))
        {
            throw new BenchmarkUsageException(
                $"Scenario file '{path}' must define scenarioId.");
        }

        if (string.IsNullOrWhiteSpace(file.BasePreset))
        {
            throw new BenchmarkUsageException(
                $"Scenario file '{path}' must define basePreset.");
        }

        if (file.Parameters is null)
        {
            throw new BenchmarkUsageException(
                $"Scenario file '{path}' parameters must be a JSON object.");
        }

        BenchmarkPreset preset = BenchmarkPresets.Get(file.BasePreset);
        if (file.BasePresetVersion != preset.Version)
        {
            throw new BenchmarkUsageException(
                $"Scenario file '{path}' requires base preset '{preset.Id}' "
                + $"version {file.BasePresetVersion}, but version {preset.Version} is available.");
        }

        Dictionary<string, long> parameters = ApplyOverrides(
            preset.Parameters,
            file.Parameters);
        parameters = ApplyOverrides(
            parameters,
            overrides ?? new Dictionary<string, long>());
        var scenario = new ResolvedBenchmarkScenario(
            file.ScenarioId,
            preset.Id,
            preset.Version,
            preset.IsHeavy,
            false,
            parameters,
            null);
        BenchmarkScenarioValidator.Validate(scenario);
        return scenario;
    }

    public static Dictionary<string, long> ApplyOverrides(
        IReadOnlyDictionary<string, long> defaults,
        IReadOnlyDictionary<string, long> overrides)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(overrides);
        var resolved = new Dictionary<string, long>(defaults, StringComparer.Ordinal);
        foreach ((string name, long value) in overrides)
        {
            if (!resolved.ContainsKey(name))
            {
                throw new BenchmarkUsageException(
                    $"Unknown numeric parameter '{name}'.");
            }

            resolved[name] = value;
        }

        return resolved;
    }

    public static KeyValuePair<string, long> ParseOverride(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new BenchmarkUsageException(
                $"Invalid numeric override '{value}'. Expected name=value.");
        }

        string name = value[..separator];
        if (!long.TryParse(
                value[(separator + 1)..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed))
        {
            throw new BenchmarkUsageException(
                $"Invalid integer value in override '{value}'.");
        }

        return new KeyValuePair<string, long>(name, parsed);
    }
}

public static class BenchmarkScenarioValidator
{
    public static void Validate(ResolvedBenchmarkScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireRange(scenario, BenchmarkParameterNames.WarmupIterations, 0, 20);
        RequireRange(scenario, BenchmarkParameterNames.MeasuredIterations, 1, 100);
        RequireRange(scenario, BenchmarkParameterNames.Seed, 0, int.MaxValue);
        RequireRange(
            scenario,
            BenchmarkParameterNames.SimulatedDurationMilliseconds,
            1,
            86_400_000);

        switch (scenario.BasePreset)
        {
            case BenchmarkPresets.SpatialManyQuiet:
            case BenchmarkPresets.SpatialOneCrowded:
            case BenchmarkPresets.NavigationConnectorVolume:
                ValidateSpatial(scenario);
                if (scenario.BasePreset == BenchmarkPresets.NavigationConnectorVolume)
                {
                    RequireRange(scenario, BenchmarkParameterNames.SystemCount, 2, 10_000);
                }

                break;
            case BenchmarkPresets.FactsRetentionAndRead:
                RequireRange(scenario, BenchmarkParameterNames.CommandCount, 1, 10_000_000);
                ValidateMovementValues(scenario);
                break;
            default:
                throw new BenchmarkUsageException(
                    $"Scenario '{scenario.Id}' uses unsupported base preset '{scenario.BasePreset}'.");
        }
    }

    private static void ValidateSpatial(ResolvedBenchmarkScenario scenario)
    {
        RequireRange(scenario, BenchmarkParameterNames.SystemCount, 1, 10_000);
        RequireRange(scenario, BenchmarkParameterNames.ShipCount, 1, 10_000_000);
        RequireRange(scenario, BenchmarkParameterNames.ActiveShipCount, 0, 10_000_000);
        if (scenario.Get(BenchmarkParameterNames.ActiveShipCount)
            > scenario.Get(BenchmarkParameterNames.ShipCount))
        {
            throw new BenchmarkUsageException(
                $"Scenario '{scenario.Id}' requires activeShipCount <= shipCount.");
        }

        ValidateMovementValues(scenario);
    }

    private static void ValidateMovementValues(ResolvedBenchmarkScenario scenario)
    {
        RequireRange(
            scenario,
            BenchmarkParameterNames.FactRetentionCapacity,
            1,
            10_000_000);
        RequireRange(
            scenario,
            BenchmarkParameterNames.TravelDurationMilliseconds,
            1,
            86_400_000);
        RequireRange(
            scenario,
            BenchmarkParameterNames.DestinationDistance,
            1,
            int.MaxValue);
    }

    private static void RequireRange(
        ResolvedBenchmarkScenario scenario,
        string name,
        long minimum,
        long maximum)
    {
        long value = scenario.Get(name);
        if (value < minimum || value > maximum)
        {
            throw new BenchmarkUsageException(
                $"Scenario '{scenario.Id}' parameter '{name}' must be between "
                + $"{minimum.ToString(CultureInfo.InvariantCulture)} and "
                + $"{maximum.ToString(CultureInfo.InvariantCulture)}; received "
                + $"{value.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
}

public sealed class BenchmarkUsageException : Exception
{
    public BenchmarkUsageException(string message)
        : base(message)
    {
    }
}
