using System.Diagnostics;
using System.Globalization;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal sealed record StationRuntimeBenchmarkProof(ScenarioCorrectnessResult Result,
    IReadOnlyList<GameSessionCheckpoint> Checkpoints, GameSessionCheckpoint Final, IReadOnlyList<GameEventRecord> Events);

/// <summary>Exercises real serial station ownership while optional host workers evaluate exact observation forecasts.</summary>
internal sealed partial class StationRuntimeBenchmarkScenario : IBenchmarkScenario
{
    private static readonly PrincipalId Player = new(1);
    private static readonly PrincipalId Owner = new(2);
    private static readonly CommandSource PlayerSource = new(CommandSourceKind.Player, new("station-benchmark-player"));
    private static readonly CommandSource OwnerSource = new(CommandSourceKind.Autonomous, new("station-benchmark-owner"));
    private static readonly MaterialId Material = new(1);
    private static readonly QualifiedContentKey StorageKey = QualifiedContentKey.Create("benchmark.stations", "station-module", "storage");
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("benchmark.stations", "station", "base");
    private static readonly QualifiedContentKey ModuleKey = QualifiedContentKey.Create("benchmark.stations", "station-module", "sensor");
    private static readonly QualifiedContentKey UpgradeKey = QualifiedContentKey.Create("benchmark.stations", "station-module", "upgraded-sensor");
    private static readonly PhysicalDefinition MaterialDefinition = new(QualifiedContentKey.Create("benchmark.stations", "material", "metal"),
        PhysicalHoldingKind.Fungible, new(1));

    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration) => RunProof(configuration).Result;

    /// <summary>Captures independent partial-work and reserved-recovery witnesses, then hashes all saved authority and exact event evidence.</summary>
    internal static StationRuntimeBenchmarkProof RunProof(ResolvedBenchmarkScenario configuration)
    {
        long allocated = GC.GetTotalAllocatedBytes(false);
        var timer = Stopwatch.StartNew();
        using var run = new StationRun(configuration, CreateSetup(configuration));
        int total = configuration.GetInt32(BenchmarkParameterNames.StationCount);
        int active = configuration.GetInt32(BenchmarkParameterNames.ActiveStationCount);
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int builders = configuration.GetInt32(BenchmarkParameterNames.BuildersPerProject);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds);
        var positions = Enumerable.Range(0, total).Select(index => Position(index % systems + 1, (index / systems + 1) * 100)).ToArray();
        for (int index = 0; index < total; index++)
            run.Submit(new CreateStationSiteCommand(Owner, Builder(systems, builders, index, 0), BaseKey, positions[index], new(0)));
        StationSnapshot[] stations = run.Session.CaptureSnapshot().Stations.ToArray();
        foreach (StationSnapshot station in stations.Take(active)) Assign(run, station, station.Projects[0].Id, systems, builders);
        long goal = ((total + systems - 1L) / systems + 1) * 100;
        for (int index = 0; index < systems; index++)
            run.SubmitPlayer(new MoveShipCommand(new((ulong)index + 1), new NavigationDestination.Position(Position(index + 1, goal)), OrderPlacement.ReplaceAll));
        var measurements = new SortedDictionary<string, BenchmarkDomainMeasurement>(StringComparer.Ordinal)
        { ["setup"] = new(timer.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(false) - allocated) };
        allocated = GC.GetTotalAllocatedBytes(false); timer.Restart();
        var checkpoints = new List<GameSessionCheckpoint>();
        int waves = configuration.GetInt32(BenchmarkParameterNames.DeliveryWaves);
        ulong materials = configuration.GetUInt64(BenchmarkParameterNames.ConstructionMaterials);
        for (int wave = 0; wave < waves; wave++)
        {
            run.AdvanceTo(new((ulong)wave * (duration / 2) / (ulong)waves));
            ulong previous = materials * (ulong)wave / (ulong)waves;
            ulong next = materials * (ulong)(wave + 1) / (ulong)waves;
            run.Store(stations.Take(active), next - previous);
            if (wave == 0)
            {
                run.AdvanceTo(new(1));
                checkpoints.Add(Capture(run.Session));
            }
        }
        run.AdvanceTo(new(duration / 2));
        foreach (StationSnapshot station in Current(run, active))
        {
            Require(station.Phase == StationPhase.Operational, "Base construction did not complete inside its authored phase.");
            run.Submit(new ConstructStationModuleCommand(Owner, station.Id, station.Revision, ModuleKey));
        }
        run.Store(Current(run, active), 3);
        AssignStructural(run, active, systems, builders, StationProjectKind.ModuleConstruction);
        run.AdvanceTo(new(duration * 7 / 12));
        foreach (StationSnapshot station in Current(run, active))
            run.Submit(new UpgradeStationModuleCommand(Owner, station.Id, station.Revision, station.Modules.Single().Id, UpgradeKey));
        AssignStructural(run, active, systems, builders, StationProjectKind.Upgrade);
        run.AdvanceTo(new(duration * 8 / 12));
        bool loss = configuration.Get(BenchmarkParameterNames.DestructionLifecycle) != 0;
        if (loss) RunDestruction(run, active, systems, builders, duration, materials, checkpoints);
        else
        {
        foreach (StationSnapshot station in Current(run, active))
        {
            StationModuleId module = station.Modules.Single().Id;
            run.Submit(new SetStationModuleEnabledCommand(Owner, station.Id, station.Revision, module, false));
            StationSnapshot disabled = run.Session.CaptureSnapshot().Stations.Single(item => item.Id == station.Id);
            run.Submit(new SetStationModuleEnabledCommand(Owner, station.Id, disabled.Revision, module, true));
            StationSnapshot enabled = run.Session.CaptureSnapshot().Stations.Single(item => item.Id == station.Id);
            run.Submit(new DismantleStationModuleCommand(Owner, station.Id, enabled.Revision, module,
                new(Builder(systems, builders, (int)station.Id.Value - 1, 0).Value)));
        }
        AssignStructural(run, active, systems, builders, StationProjectKind.Dismantling);
        run.AdvanceTo(new(duration * 8 / 12 + 1));
        checkpoints.Add(Capture(run.Session));
        run.AdvanceTo(new(duration * 9 / 12));
        foreach (StationSnapshot station in Current(run, active))
        {
            Require(station.Modules.Count == 0, "A dismantled module remained live.");
            run.Submit(new DismantleStationBaseCommand(Owner, station.Id, station.Revision,
                new(Builder(systems, builders, (int)station.Id.Value - 1, 0).Value)));
        }
        AssignStructural(run, active, systems, builders, StationProjectKind.Dismantling);
        run.AdvanceTo(new(duration));
        }
        measurements["advanceAndLifecycle"] = new(timer.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(false) - allocated);
        measurements["checkpointRestoreAndProof"] = new(run.RestoreMilliseconds, run.RestoreAllocated);
        allocated = GC.GetTotalAllocatedBytes(false); timer.Restart();
        GameSessionCheckpoint final = Capture(run.Session);
        GameEventRecord[] events = run.AllEvents();
        var digest = new DeterministicDigest();
        var gameplay = new DeterministicDigest();
        foreach (GameSessionCheckpoint checkpoint in checkpoints) gameplay.Add(StationCheckpointDigest.ComputeGameplay(checkpoint));
        gameplay.Add(StationCheckpointDigest.ComputeGameplay(final));
        foreach (GameSessionCheckpoint checkpoint in checkpoints) digest.Add(StationCheckpointDigest.Compute(checkpoint));
        digest.Add(StationCheckpointDigest.Compute(final));
        digest.Add(StationCheckpointDigest.ComputeNode(events));
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["destructions"] = final.Stations!.Destructions.Count,
            ["stationsCreated"] = total, ["quietSites"] = total - active, ["activeProjects"] = active,
            ["removedStations"] = final.Stations!.Removals.Count, ["restores"] = run.Restores,
            ["commands"] = run.Commands, ["events"] = events.Length,
            ["stationWorkEvents"] = events.LongCount(item => item.Kind is GameEventKind.StationWork),
            ["recoveredMaterials"] = checked((long)final.Lifecycle.Inventories.Inventories.SelectMany(inventory => inventory.FungibleHoldings)
                .Where(item => item.DefinitionKey == MaterialDefinition.Key).Aggregate(0UL, (sum, item) => sum + item.Quantity.Units)),
            ["observedStationChanges"] = final.Facts.RetainedFacts.LongCount(item => item!.PlayerDisclosure?.Fact is ObservedStationChangeFact),
            ["factsCommitted"] = final.Facts.Sequences.NextValue is { } nextFact ? checked((long)nextFact - 1) : long.MaxValue,
        };
        Require(counts["removedStations"] == active, "The active station lifecycle did not finish exactly once.");
        Require(counts["recoveredMaterials"] == active * (loss ? (long)(4 + (materials + 24) / 2) : 4L), "Recovery outputs changed or duplicated.");
        measurements["effectDigest"] = new(timer.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(false) - allocated);
        return new(new(digest.ToString(), duration, counts, measurements, gameplay.ToString()), checkpoints.AsReadOnly(), final, Array.AsReadOnly(events));
    }

    private static StationSnapshot[] Current(StationRun run, int active) => run.Session.CaptureSnapshot().Stations
        .Where(station => station.Id.Value <= (ulong)active).ToArray();

    /// <summary>Assigns each explicit builder to the accepted named project, obtaining the fresh station revision for every command.</summary>
    private static void Assign(StationRun run, StationSnapshot station, StationProjectId project, int systems, int builders)
    {
        for (int builder = 0; builder < builders; builder++)
        {
            StationSnapshot current = run.Session.CaptureSnapshot().Stations.Single(item => item.Id == station.Id);
            run.Submit(new AssignStationBuilderCommand(Owner, station.Id, project, current.Revision,
                Builder(systems, builders, (int)station.Id.Value - 1, builder), new(0)));
        }
    }

    /// <summary>Uses the one newly admitted live structural project rather than inferring work from a completed history.</summary>
    private static void AssignStructural(StationRun run, int active, int systems, int builders, StationProjectKind kind)
    {
        foreach (StationSnapshot station in Current(run, active))
        {
            StationProjectSnapshot project = station.Projects.Single(item => item.Kind == kind
                && item.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled));
            Assign(run, station, project.Id, systems, builders);
        }
    }

    private static ShipId Builder(int systems, int builders, int station, int builder) => new(checked((ulong)(systems + station * builders + builder + 1)));
    private static SystemPosition Position(int system, long x) => new(new((ulong)system), new(new(x), new(0)));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    /// <summary>Requires supported in-memory owner capture without invoking any JSON save bridge.</summary>
    private static GameSessionCheckpoint Capture(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> captured = session.CaptureCheckpoint();
        if (!captured.IsSuccess) throw new InvalidOperationException(captured.Failure!.Message);
        return captured.Value!;
    }
}
