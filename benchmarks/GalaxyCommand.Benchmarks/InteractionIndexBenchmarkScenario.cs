using System.Collections.ObjectModel;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Two-dimensional prototype and public-runtime comparison with identical fixture and digest inputs.</summary>
internal sealed class InteractionIndexBenchmarkScenario(string kind, bool publicRuntime = false) : IBenchmarkScenario
{
    /// <summary>
    /// Times fixture construction, exact preparation, index construction, scoped
    /// queries, and result digesting. Layout 0 is a centered lattice; layout 1
    /// has four separated lattice clusters. Half the default ships move diagonally.
    /// Public-runtime modes call only Prepare, PrepareScope, and Find. Prototype
    /// modes retain diagnostic counts; structural sizes do not measure retained bytes.
    /// </summary>
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int ships = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int active = configuration.GetInt32(BenchmarkParameterNames.ActiveShipCount);
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int queries = configuration.GetInt32(BenchmarkParameterNames.QueryCount);
        int stride = configuration.GetInt32(BenchmarkParameterNames.ScopeStride);
        int layout = configuration.GetInt32(BenchmarkParameterNames.Layout);
        long spacing = configuration.Get(BenchmarkParameterNames.ParticipantSpacing);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        ulong range = configuration.GetUInt64(BenchmarkParameterNames.RangeUnits);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds);
        var now = new SimulationTime(configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds));
        int side = (int)Math.Ceiling(Math.Sqrt((ships + systems - 1) / systems / (layout == 1 ? 4.0 : 1.0)));
        var movement = new SpatialMovement();
        var ids = new ShipId[ships];
        for (int i = 0; i < ships; i++)
        {
            int rank = i / systems;
            int cluster = layout == 1 ? rank % 4 : 0;
            int local = layout == 1 ? rank / 4 : rank;
            long x = checked((local % side - side / 2) * spacing + (layout == 1 ? (cluster % 2 * 2 - 1) * 10000L : 0));
            long y = checked((local / side - side / 2) * spacing + (layout == 1 ? (cluster / 2 * 2 - 1) * 10000L : 0));
            ids[i] = new ShipId((ulong)i + 1);
            var origin = Position(x, y, (ulong)(i % systems) + 1);
            movement.Add(ids[i], origin);
            if (i < active)
                movement.CommitStartOrReplace(ids[i], new TravelLeg.Local(origin,
                    Position(checked(x + distance), checked(y - distance), origin.SystemId.Value), new SimulationDuration(duration)),
                    SimulationTime.Zero, static value => value);
        }

        ShipId[] scope = ids.Where((_, i) => i % stride == 0).ToArray();
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(now);
        PrototypeProximity? prototype = publicRuntime ? null : new PrototypeProximity(view, scope, kind,
            configuration.Get(BenchmarkParameterNames.GridWidth), configuration.GetInt32(BenchmarkParameterNames.TreeLeafSize));
        PreparedInteractionProximityScope? runtime = publicRuntime
            ? InteractionProximityQuery.Prepare(view).PrepareScope(scope, kind switch
            {
                "scan" => InteractionPreparationStrategy.Scan,
                "grid" => InteractionPreparationStrategy.SparseGrid(configuration.GetUInt64(BenchmarkParameterNames.GridWidth),
                    configuration.GetInt32(BenchmarkParameterNames.GridCellBudget)),
                "hierarchy" => InteractionPreparationStrategy.BoundingBoxHierarchy(configuration.GetInt32(BenchmarkParameterNames.TreeLeafSize)),
                _ => throw new InvalidOperationException("Unknown runtime strategy."),
            }) : null;
        var digest = new DeterministicDigest();
        long seed = configuration.Get(BenchmarkParameterNames.Seed);
        foreach (long value in new long[] { ships, active, systems, queries, stride, layout, spacing, distance, seed }) digest.Add(value);
        digest.Add(duration); digest.Add(range); digest.Add(now.Milliseconds);
        long matches = 0, candidates = 0, exactChecks = 0, boxTests = 0;
        for (int i = 0; i < queries; i++)
        {
            ShipId source = ids[(int)(((long)i + seed) % ships)];
            PrototypeProximityResult? result = prototype?.Query(source, range);
            IReadOnlyList<ShipId> found = runtime?.Find(source, range) ?? result!.Matches;
            digest.Add(source.Value); digest.Add((ulong)found.Count);
            foreach (ShipId target in found) digest.Add(target.Value);
            matches += found.Count;
            if (result is not null)
            {
                candidates += result.Candidates;
                exactChecks += result.ExactChecks;
                boxTests += result.BoxTests;
            }
        }
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["ships"] = ships, ["activeShips"] = active, ["systems"] = systems, ["queries"] = queries,
            ["scopedEntries"] = checked((long)scope.Length * queries), ["matches"] = matches,
        };
        // Instrumentation stays on the prototype path so runtime measurements use only the public API.
        if (prototype is not null)
        {
            counts["broadPhaseCandidates"] = candidates;
            counts["exactChecks"] = exactChecks;
            counts["indexBoxTests"] = boxTests;
            counts["indexEntries"] = prototype.Entries;
            counts["indexNodes"] = prototype.Nodes;
        }
        return new ScenarioCorrectnessResult(digest.ToString(), now.Milliseconds, new ReadOnlyDictionary<string, long>(counts));
    }

    private static SystemPosition Position(long x, long y, ulong system) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));
}
