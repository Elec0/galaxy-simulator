using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalaxyCommand.Benchmarks;

public static class BenchmarkParameterNames
{
    public const string InterestCount = "interestCount";
    public const string WorkerCount = "workerCount";
    public const string BatchSize = "batchSize";
    public const string WarmupIterations = "warmupIterations";
    public const string MeasuredIterations = "measuredIterations";
    public const string Seed = "seed";
    public const string SimulatedDurationMilliseconds = "simulatedDurationMilliseconds";
    public const string SystemCount = "systemCount";
    public const string ShipCount = "shipCount";
    public const string ActiveShipCount = "activeShipCount";
    public const string CommandCount = "commandCount";
    public const string ReplanCount = "replanCount";
    public const string ReplanIntervalMilliseconds = "replanIntervalMilliseconds";
    public const string FactRetentionCapacity = "factRetentionCapacity";
    public const string TravelDurationMilliseconds = "travelDurationMilliseconds";
    public const string MotionKind = "motionKind";
    public const string Layout = "layout";
    public const string ScopeStride = "scopeStride";
    public const string GridWidth = "gridWidth";
    public const string GridCellBudget = "gridCellBudget";
    public const string TreeLeafSize = "treeLeafSize";
    public const string QueryCount = "queryCount";
    public const string RangeUnits = "rangeUnits";
    public const string ParticipantSpacing = "participantSpacing";
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
    public const string StationarySessionSerial = "interaction.stationary-session-serial";
    public const string StationarySessionBatched = "interaction.stationary-session-batched";
    public const string InteractionSessionSerial = "interaction.session-serial";
    public const string InteractionSessionBatched = "interaction.session-batched";
    public const string InteractionBatchSweptScan = "interaction.batch-swept-scan";
    public const string InteractionBatchSweptGrid = "interaction.batch-swept-grid";
    public const string InteractionBatchSweptHierarchy = "interaction.batch-swept-hierarchy";
    public const string InteractionRuntimeSweptScan = "interaction.runtime-swept-scan";
    public const string InteractionRuntimeSweptGrid = "interaction.runtime-swept-grid";
    public const string InteractionRuntimeSweptHierarchy = "interaction.runtime-swept-hierarchy";
    public const string InteractionSweptScan = "interaction.swept-scan";
    public const string InteractionSweptGrid = "interaction.swept-grid";
    public const string InteractionSweptHierarchy = "interaction.swept-hierarchy";
    public const string InteractionRuntimeScan = "interaction.runtime-scan";
    public const string InteractionRuntimeGrid = "interaction.runtime-grid";
    public const string InteractionRuntimeHierarchy = "interaction.runtime-hierarchy";
    public const string InteractionIndexScan = "interaction.index-scan";
    public const string InteractionIndexGrid = "interaction.index-grid";
    public const string InteractionIndexHierarchy = "interaction.index-hierarchy";
    public const string InteractionPreparedProximity = "interaction.prepared-proximity";
    public const string InteractionScopedProximity = "interaction.scoped-proximity";
    public const string SpatialManyQuiet = "spatial.many-quiet";
    public const string SpatialOneCrowded = "spatial.one-crowded";
    public const string NavigationConnectorVolume = "navigation.connector-volume";
    public const string FactsRetentionAndRead = "facts.retention-and-read";
    public const string ManeuverRepeatedReplanning = "maneuver.repeated-replanning";

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
                InteractionScopedProximity,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 500),
                    (BenchmarkParameterNames.SystemCount, 1),
                    (BenchmarkParameterNames.ShipCount, 2_500),
                    (BenchmarkParameterNames.ActiveShipCount, 1_250),
                    (BenchmarkParameterNames.QueryCount, 100),
                    (BenchmarkParameterNames.RangeUnits, 10),
                    (BenchmarkParameterNames.ParticipantSpacing, 100),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.DestinationDistance, 20)),
                "c09a37599b5eb648"),
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
                "92f748f87de3d720"),
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
                "2e7b8e468f47a265"),
            new(
                NavigationConnectorVolume,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 1_000_000),
                    (BenchmarkParameterNames.SystemCount, 32),
                    (BenchmarkParameterNames.ShipCount, 1_000),
                    (BenchmarkParameterNames.ActiveShipCount, 1_000),
                    (BenchmarkParameterNames.FactRetentionCapacity, 100_000),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 100),
                    (BenchmarkParameterNames.DestinationDistance, 100)),
                "673c9af7f54a80e4",
                version: 2),
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
                "9ffab4f2262b68b6"),
            new(
                ManeuverRepeatedReplanning,
                true,
                Parameters(
                    (BenchmarkParameterNames.WarmupIterations, 1),
                    (BenchmarkParameterNames.MeasuredIterations, 5),
                    (BenchmarkParameterNames.Seed, 1),
                    (BenchmarkParameterNames.SimulatedDurationMilliseconds, 5_000_000),
                    (BenchmarkParameterNames.ReplanCount, 100),
                    (BenchmarkParameterNames.ReplanIntervalMilliseconds, 50_000),
                    (BenchmarkParameterNames.FactRetentionCapacity, 10_000),
                    (BenchmarkParameterNames.TravelDurationMilliseconds, 1_000),
                    (BenchmarkParameterNames.DestinationDistance, 1_000_000)),
                "603601f07a12a8c1"),
        ];
        BenchmarkPreset reference = values.Single(value => value.Id == InteractionScopedProximity);
        BenchmarkPreset prepared = new(InteractionPreparedProximity, true, reference.Parameters, reference.ExpectedDigest);
        var indexParameters = new Dictionary<string, long>(reference.Parameters)
        {
            [BenchmarkParameterNames.Layout] = 0,
            [BenchmarkParameterNames.ScopeStride] = 1,
            [BenchmarkParameterNames.GridWidth] = 16,
            [BenchmarkParameterNames.TreeLeafSize] = 4,
            [BenchmarkParameterNames.SimulatedDurationMilliseconds] = 501,
        };
        BenchmarkPreset[] indexes =
        [
            new(InteractionIndexScan, true, indexParameters, "b75076796115dc88"),
            new(InteractionIndexGrid, true, indexParameters, "b75076796115dc88"),
            new(InteractionIndexHierarchy, true, indexParameters, "b75076796115dc88"),
        ];
        var runtimeParameters = new Dictionary<string, long>(indexParameters)
        {
            [BenchmarkParameterNames.GridCellBudget] = 4096,
        };
        BenchmarkPreset[] runtime =
        [
            new(InteractionRuntimeScan, true, runtimeParameters, "b75076796115dc88"),
            new(InteractionRuntimeGrid, true, runtimeParameters, "b75076796115dc88"),
            new(InteractionRuntimeHierarchy, true, runtimeParameters, "b75076796115dc88"),
        ];
        var sweptParameters = new Dictionary<string, long>(indexParameters)
        {
            [BenchmarkParameterNames.ShipCount] = 250,
            [BenchmarkParameterNames.ActiveShipCount] = 125,
            [BenchmarkParameterNames.QueryCount] = 10,
            [BenchmarkParameterNames.DestinationDistance] = 2000,
            [BenchmarkParameterNames.SimulatedDurationMilliseconds] = 1000,
            [BenchmarkParameterNames.MotionKind] = 0,
        };
        BenchmarkPreset[] sweeps =
        [
            new(InteractionSweptScan, true, sweptParameters, "418f5c9774f5bbdc"),
            new(InteractionSweptGrid, true, sweptParameters, "418f5c9774f5bbdc"),
            new(InteractionSweptHierarchy, true, sweptParameters, "418f5c9774f5bbdc"),
        ];
        var runtimeSweptParameters = new Dictionary<string, long>(sweptParameters)
        {
            [BenchmarkParameterNames.GridCellBudget] = 4096,
        };
        BenchmarkPreset[] runtimeSweeps =
        [
            new(InteractionRuntimeSweptScan, true, runtimeSweptParameters, "418f5c9774f5bbdc"),
            new(InteractionRuntimeSweptGrid, true, runtimeSweptParameters, "418f5c9774f5bbdc"),
            new(InteractionRuntimeSweptHierarchy, true, runtimeSweptParameters, "418f5c9774f5bbdc"),
        ];
        var batchParameters = new Dictionary<string, long>(runtimeSweptParameters)
        {
            [BenchmarkParameterNames.WorkerCount] = 1,
            [BenchmarkParameterNames.BatchSize] = 64,
        };
        BenchmarkPreset[] batchSweeps =
        [
            new(InteractionBatchSweptScan, true, batchParameters, "418f5c9774f5bbdc"),
            new(InteractionBatchSweptGrid, true, batchParameters, "418f5c9774f5bbdc"),
            new(InteractionBatchSweptHierarchy, true, batchParameters, "418f5c9774f5bbdc"),
        ];
        var sessionParameters = Parameters(
            (BenchmarkParameterNames.WarmupIterations, 1),
            (BenchmarkParameterNames.MeasuredIterations, 5),
            (BenchmarkParameterNames.Seed, 1),
            (BenchmarkParameterNames.SimulatedDurationMilliseconds, 6000),
            (BenchmarkParameterNames.SystemCount, 2),
            (BenchmarkParameterNames.ShipCount, 32),
            (BenchmarkParameterNames.ActiveShipCount, 8),
            (BenchmarkParameterNames.InterestCount, 256),
            (BenchmarkParameterNames.ReplanCount, 4),
            (BenchmarkParameterNames.ReplanIntervalMilliseconds, 1000),
            (BenchmarkParameterNames.FactRetentionCapacity, 10000),
            (BenchmarkParameterNames.TravelDurationMilliseconds, 1000),
            (BenchmarkParameterNames.DestinationDistance, 100),
            (BenchmarkParameterNames.RangeUnits, 2));
        var sessionBatchParameters = new Dictionary<string, long>(sessionParameters)
        {
            [BenchmarkParameterNames.WorkerCount] = 1,
            [BenchmarkParameterNames.BatchSize] = 64,
        };
        BenchmarkPreset[] sessions =
        [
            new(InteractionSessionSerial, true, sessionParameters, "1079ca5a77a8341f"),
            new(InteractionSessionBatched, true, sessionBatchParameters, "1079ca5a77a8341f"),
            new(StationarySessionSerial, true, sessionParameters),
            new(StationarySessionBatched, true, sessionBatchParameters),
        ];
        return new ReadOnlyDictionary<string, BenchmarkPreset>(
            values.Append(prepared).Concat(indexes).Concat(runtime).Concat(sweeps).Concat(runtimeSweeps).Concat(batchSweeps).Concat(sessions).ToDictionary(value => value.Id, StringComparer.Ordinal));
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
    /// <summary>Rejects invalid workloads before allocation or timed execution.</summary>
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
            case BenchmarkPresets.StationarySessionBatched:
            case BenchmarkPresets.InteractionSessionBatched:
                RequireRange(scenario, BenchmarkParameterNames.WorkerCount, 1, 1024);
                RequireRange(scenario, BenchmarkParameterNames.BatchSize, 1, int.MaxValue);
                goto case BenchmarkPresets.InteractionSessionSerial;
            case BenchmarkPresets.StationarySessionSerial:
            case BenchmarkPresets.InteractionSessionSerial:
                RequireRange(scenario, BenchmarkParameterNames.SystemCount, 1, 10000);
                RequireRange(scenario, BenchmarkParameterNames.ShipCount, 2, 1000000);
                RequireRange(scenario, BenchmarkParameterNames.ActiveShipCount,
                    scenario.Get(BenchmarkParameterNames.SystemCount),
                    scenario.Get(BenchmarkParameterNames.ShipCount) - scenario.Get(BenchmarkParameterNames.SystemCount));
                RequireRange(scenario, BenchmarkParameterNames.InterestCount, 1, 1000000);
                RequireRange(scenario, BenchmarkParameterNames.RangeUnits, 0, int.MaxValue);
                ValidateRepeatedReplanning(scenario);
                break;
            case BenchmarkPresets.SpatialManyQuiet:
            case BenchmarkPresets.SpatialOneCrowded:
            case BenchmarkPresets.NavigationConnectorVolume:
                ValidateSpatial(scenario);
                if (scenario.BasePreset == BenchmarkPresets.NavigationConnectorVolume)
                {
                    RequireRange(scenario, BenchmarkParameterNames.SystemCount, 2, 10_000);
                }

                break;
            case BenchmarkPresets.InteractionSweptScan:
            case BenchmarkPresets.InteractionSweptGrid:
            case BenchmarkPresets.InteractionSweptHierarchy:
                ValidateProximity(scenario);
                RequireRange(scenario, BenchmarkParameterNames.Layout, 0, 1);
                RequireRange(scenario, BenchmarkParameterNames.ScopeStride, 1, 10000);
                RequireRange(scenario, BenchmarkParameterNames.GridWidth, 1, int.MaxValue);
                RequireRange(scenario, BenchmarkParameterNames.TreeLeafSize, 1, 1024);
                RequireRange(scenario, BenchmarkParameterNames.MotionKind, 0, 2);
                RequireRange(scenario, BenchmarkParameterNames.TravelDurationMilliseconds, 1000, 86400000);
                if (scenario.Get(BenchmarkParameterNames.MotionKind) == 2
                    && scenario.Get(BenchmarkParameterNames.DestinationDistance) * 2000000 / scenario.Get(BenchmarkParameterNames.TravelDurationMilliseconds) == 0)
                    throw new BenchmarkUsageException("Braking fixture requires a nonzero initial speed.");
                break;
            case BenchmarkPresets.InteractionBatchSweptScan:
            case BenchmarkPresets.InteractionBatchSweptGrid:
            case BenchmarkPresets.InteractionBatchSweptHierarchy:
                RequireRange(scenario, BenchmarkParameterNames.WorkerCount, 1, 1024);
                RequireRange(scenario, BenchmarkParameterNames.BatchSize, 1, int.MaxValue);
                goto case BenchmarkPresets.InteractionRuntimeSweptScan;
            case BenchmarkPresets.InteractionRuntimeSweptScan:
            case BenchmarkPresets.InteractionRuntimeSweptGrid:
            case BenchmarkPresets.InteractionRuntimeSweptHierarchy:
                RequireRange(scenario, BenchmarkParameterNames.MotionKind, 0, 1);
                RequireRange(scenario, BenchmarkParameterNames.TravelDurationMilliseconds, 1000, 86400000);
                goto case BenchmarkPresets.InteractionRuntimeScan;
            case BenchmarkPresets.InteractionRuntimeScan:
            case BenchmarkPresets.InteractionRuntimeGrid:
            case BenchmarkPresets.InteractionRuntimeHierarchy:
                RequireRange(scenario, BenchmarkParameterNames.GridCellBudget, 1, int.MaxValue);
                goto case BenchmarkPresets.InteractionIndexScan;
            case BenchmarkPresets.InteractionIndexScan:
            case BenchmarkPresets.InteractionIndexGrid:
            case BenchmarkPresets.InteractionIndexHierarchy:
                ValidateProximity(scenario);
                RequireRange(scenario, BenchmarkParameterNames.Layout, 0, 1);
                RequireRange(scenario, BenchmarkParameterNames.ScopeStride, 1, 10000);
                RequireRange(scenario, BenchmarkParameterNames.GridWidth, 1, int.MaxValue);
                RequireRange(scenario, BenchmarkParameterNames.TreeLeafSize, 1, 1024);
                break;
            case BenchmarkPresets.InteractionPreparedProximity:
            case BenchmarkPresets.InteractionScopedProximity:
                ValidateProximity(scenario);
                break;
            case BenchmarkPresets.FactsRetentionAndRead:
                RequireRange(scenario, BenchmarkParameterNames.CommandCount, 1, 10_000_000);
                ValidateMovementValues(scenario);
                break;
            case BenchmarkPresets.ManeuverRepeatedReplanning:
                ValidateRepeatedReplanning(scenario);
                break;
            default:
                throw new BenchmarkUsageException(
                    $"Scenario '{scenario.Id}' uses unsupported base preset '{scenario.BasePreset}'.");
        }
    }

    /// <summary>Keeps the sampled instant within known linear motion and bounds explicit query work.</summary>
    private static void ValidateProximity(ResolvedBenchmarkScenario scenario)
    {
        RequireRange(scenario, BenchmarkParameterNames.SystemCount, 1, 10_000);
        RequireRange(scenario, BenchmarkParameterNames.ShipCount, 1, 10_000_000);
        RequireRange(scenario, BenchmarkParameterNames.ActiveShipCount, 0, scenario.Get(BenchmarkParameterNames.ShipCount));
        RequireRange(scenario, BenchmarkParameterNames.QueryCount, 1, 1_000_000);
        RequireRange(scenario, BenchmarkParameterNames.RangeUnits, 0, long.MaxValue);
        RequireRange(scenario, BenchmarkParameterNames.ParticipantSpacing, 0, int.MaxValue);
        RequireRange(scenario, BenchmarkParameterNames.DestinationDistance, 1, int.MaxValue);
        RequireRange(scenario, BenchmarkParameterNames.TravelDurationMilliseconds, 1, 86_400_000);
        if (scenario.Get(BenchmarkParameterNames.SimulatedDurationMilliseconds)
            > scenario.Get(BenchmarkParameterNames.TravelDurationMilliseconds))
            throw new BenchmarkUsageException("simulatedDurationMilliseconds must remain within travelDurationMilliseconds for proximity sampling.");
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

    /// <summary>
    /// Keeps every replacement inside the configured simulation window so the
    /// timed workload never relies on an implicit extension of benchmark time.
    /// </summary>
    private static void ValidateRepeatedReplanning(
        ResolvedBenchmarkScenario scenario)
    {
        RequireRange(scenario, BenchmarkParameterNames.ReplanCount, 1, 1_000_000);
        RequireRange(
            scenario,
            BenchmarkParameterNames.ReplanIntervalMilliseconds,
            1,
            86_400_000);
        ValidateMovementValues(scenario);
        long lastReplanAt = checked(
            (scenario.Get(BenchmarkParameterNames.ReplanCount) - 1)
            * scenario.Get(BenchmarkParameterNames.ReplanIntervalMilliseconds));
        if (lastReplanAt
            > scenario.Get(BenchmarkParameterNames.SimulatedDurationMilliseconds))
        {
            throw new BenchmarkUsageException(
                $"Scenario '{scenario.Id}' requires every timed replan to occur within simulatedDurationMilliseconds.");
        }
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
