using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Measures pure persistent-target crossing batches without presentation, owner mutation, or scheduler work on workers.</summary>
internal sealed class DeployableObservationBenchmarkScenario(bool batched) : IBenchmarkScenario
{
    /// <summary>Builds identical exact linear sweeps and fixed observation positions, then hashes ordered boundary evidence.</summary>
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int count = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int queries = configuration.GetInt32(BenchmarkParameterNames.QueryCount);
        ulong range = configuration.GetUInt64(BenchmarkParameterNames.RangeUnits);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        var movement = new SpatialMovement();
        var sources = new ShipSensorCoverageInput[count];
        var targets = new SystemPosition[count];
        for (int i = 0; i < count; i++)
        {
            var ship = new ShipId((ulong)i + 1);
            SystemPosition origin = ScenarioSetup.Position(i % systems + 1, 0, i / systems * 10);
            SystemPosition to = ScenarioSetup.Position(i % systems + 1, distance, i / systems * 10);
            movement.Add(ship, origin);
            movement.CommitStartOrReplace(ship, new TravelLeg.Local(origin, to, new SimulationDuration(duration)),
                SimulationTime.Zero, static value => value);
            sources[i] = new ShipSensorCoverageInput(new EntityId(ship.Value), ship, new PrincipalId(1), range);
            targets[i] = ScenarioSetup.Position(i % systems + 1, distance / 2, i / systems * 10);
        }
        InteractionBatchExecution? execution = batched ? new InteractionBatchExecution(
            configuration.GetInt32(BenchmarkParameterNames.WorkerCount), configuration.GetInt32(BenchmarkParameterNames.BatchSize)) : null;
        var digest = new DeterministicDigest();
        for (int query = 0; query < queries; query++)
        {
            var now = new SimulationTime((ulong)query * duration / (ulong)queries);
            SimulationTime? next = DeployableObservationCrossings.Next(movement.CaptureInteractionReadView(now), sources,
                targets, new SimulationTime(duration), execution);
            digest.Add(next?.Milliseconds ?? ulong.MaxValue);
        }
        return new ScenarioCorrectnessResult(digest.ToString(), duration, new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["sources"] = count, ["stationaryTargets"] = count, ["crossingQueries"] = queries,
        });
    }
}
