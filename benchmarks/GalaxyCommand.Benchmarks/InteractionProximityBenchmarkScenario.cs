using System.Collections.ObjectModel;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Single-thread caller-scoped query baseline with linear and stationary participants.</summary>
internal sealed class InteractionProximityBenchmarkScenario(bool prepared = false) : IBenchmarkScenario
{
    /// <summary>
    /// Includes fixture construction, movement commits, view capture, queries,
    /// preparation when selected, and result digesting in the timed iteration.
    /// Prepared and reference modes share identical fixtures and digest inputs.
    /// It samples one instant and
    /// does not advance a session or register an all-pairs interaction workload.
    /// </summary>
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int ships = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int active = configuration.GetInt32(BenchmarkParameterNames.ActiveShipCount);
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int queries = configuration.GetInt32(BenchmarkParameterNames.QueryCount);
        long spacing = configuration.Get(BenchmarkParameterNames.ParticipantSpacing);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        ulong range = configuration.GetUInt64(BenchmarkParameterNames.RangeUnits);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds);
        var now = new SimulationTime(configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds));
        var movement = new SpatialMovement();
        var scope = new ShipId[ships];
        for (int index = 0; index < ships; index++)
        {
            var id = new ShipId((ulong)index + 1);
            scope[index] = id;
            var origin = new SystemPosition(new SystemId((ulong)(index % systems) + 1),
                new SpatialPosition(new SpatialCoordinate(checked((long)(index / systems) * spacing)), new SpatialCoordinate(0)));
            movement.Add(id, origin);
            if (index < active)
            {
                var target = new SystemPosition(origin.SystemId, new SpatialPosition(
                    new SpatialCoordinate(checked(origin.Position.X.Units + distance)), origin.Position.Y));
                movement.CommitStartOrReplace(id, new TravelLeg.Local(origin, target, new SimulationDuration(duration)),
                    SimulationTime.Zero, static value => value);
            }
        }

        InteractionSpatialReadView view = movement.CaptureInteractionReadView(now);
        PreparedInteractionProximityScope? preparedScope = prepared
            ? InteractionProximityQuery.Prepare(view).PrepareScope(scope) : null;
        var digest = new DeterministicDigest();
        long seed = configuration.Get(BenchmarkParameterNames.Seed);
        // Bind empty query results to the actual fixture, while iteration counts
        // remain timing settings and do not change correctness evidence.
        digest.Add((long)ships);
        digest.Add((long)active);
        digest.Add((long)systems);
        digest.Add((long)queries);
        digest.Add(spacing);
        digest.Add(distance);
        digest.Add(duration);
        digest.Add(range);
        digest.Add(seed);
        digest.Add(now.Milliseconds);
        long matches = 0;
        for (int query = 0; query < queries; query++)
        {
            ShipId source = scope[(int)(((long)query + seed) % ships)];
            IReadOnlyList<ShipId> result = preparedScope is not null
                ? preparedScope.Find(source, range) : InteractionProximityQuery.Find(view, source, scope, range);
            digest.Add(source.Value);
            digest.Add((ulong)result.Count);
            foreach (ShipId target in result) digest.Add(target.Value);
            matches = checked(matches + result.Count);
        }
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["ships"] = ships, ["activeShips"] = active, ["systems"] = systems,
            ["queries"] = queries, ["scopedCandidates"] = checked((long)queries * ships), ["matches"] = matches,
        };
        return new ScenarioCorrectnessResult(digest.ToString(), now.Milliseconds,
            new ReadOnlyDictionary<string, long>(counts));
    }
}
