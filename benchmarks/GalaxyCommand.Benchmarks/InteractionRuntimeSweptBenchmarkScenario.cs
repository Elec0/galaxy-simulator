using System.Collections.ObjectModel;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal sealed record RuntimeSweptFixture(InteractionSpatialReadView View, ShipId[] Ids, ShipId[] Scope, SimulationTime Through);

/// <summary>Public swept preparation and queries over actual movement-owner captures and boundary-clipped geometry.</summary>
internal sealed class InteractionRuntimeSweptBenchmarkScenario(string kind, bool batched = false) : IBenchmarkScenario
{
    /// <summary>
    /// Times movement fixture setup, capture, preparation, queries, and ordered digesting.
    /// Batch modes retain caller source order and join before digesting; worker tuning
    /// changes execution cost only and is excluded from semantic digest inputs.
    /// </summary>
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        RuntimeSweptFixture fixture = BuildFixture(configuration);
        var prepared = InteractionSweptQuery.Prepare(fixture.View, fixture.Through).PrepareScope(fixture.Scope, kind switch
        {
            "scan" => InteractionPreparationStrategy.Scan,
            "grid" => InteractionPreparationStrategy.SparseGrid(configuration.GetUInt64(BenchmarkParameterNames.GridWidth),
                configuration.GetInt32(BenchmarkParameterNames.GridCellBudget)),
            "hierarchy" => InteractionPreparationStrategy.BoundingBoxHierarchy(configuration.GetInt32(BenchmarkParameterNames.TreeLeafSize)),
            _ => throw new InvalidOperationException("Unknown runtime strategy."),
        });
        int queries = configuration.GetInt32(BenchmarkParameterNames.QueryCount);
        ulong range = configuration.GetUInt64(BenchmarkParameterNames.RangeUnits);
        long seed = configuration.Get(BenchmarkParameterNames.Seed);
        var digest = new DeterministicDigest();
        foreach (string parameter in new[] { BenchmarkParameterNames.ShipCount, BenchmarkParameterNames.ActiveShipCount,
            BenchmarkParameterNames.SystemCount, BenchmarkParameterNames.QueryCount, BenchmarkParameterNames.ScopeStride,
            BenchmarkParameterNames.Layout, BenchmarkParameterNames.MotionKind, BenchmarkParameterNames.ParticipantSpacing,
            BenchmarkParameterNames.DestinationDistance, BenchmarkParameterNames.Seed }) digest.Add(configuration.Get(parameter));
        digest.Add(configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds));
        digest.Add(range); digest.Add(fixture.Through.Milliseconds);
        IReadOnlyList<IReadOnlyList<InteractionSweptMatch>>? batchResults = batched
            ? prepared.FindBatch(Enumerable.Range(0, queries).Select(i => fixture.Ids[(int)(((long)i + seed) % fixture.Ids.Length)]), range,
                new InteractionBatchExecution(configuration.GetInt32(BenchmarkParameterNames.WorkerCount),
                    configuration.GetInt32(BenchmarkParameterNames.BatchSize))) : null;
        long contacts = 0;
        for (int i = 0; i < queries; i++)
        {
            ShipId source = fixture.Ids[(int)(((long)i + seed) % fixture.Ids.Length)];
            IReadOnlyList<InteractionSweptMatch> matches = batchResults is not null ? batchResults[i] : prepared.Find(source, range);
            digest.Add(source.Value); digest.Add((ulong)matches.Count);
            foreach (InteractionSweptMatch match in matches)
            {
                digest.Add(match.Target.Value); digest.Add(match.Contact.Timestamp.Milliseconds);
                digest.Add(match.Contact.InRangeAtTimestamp); digest.Add(match.Contact.AlreadyInRange);
            }
            contacts += matches.Count;
        }
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["ships"] = fixture.Ids.Length, ["activeShips"] = configuration.Get(BenchmarkParameterNames.ActiveShipCount),
            ["systems"] = configuration.Get(BenchmarkParameterNames.SystemCount), ["queries"] = queries,
            ["scopedEntries"] = checked((long)fixture.Scope.Length * queries), ["contacts"] = contacts,
        };
        return new ScenarioCorrectnessResult(digest.ToString(), fixture.Through.Milliseconds, new ReadOnlyDictionary<string, long>(counts));
    }

    /// <summary>
    /// Creates the same lattice/cluster layout as the prototype. Mode zero uses linear motions;
    /// mode one replaces alternating active ships with real bounded maneuvers and an inserted
    /// event. Captured plans are not extrapolated across that event by the public query API.
    /// </summary>
    internal static RuntimeSweptFixture BuildFixture(ResolvedBenchmarkScenario configuration)
    {
        int ships = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int active = configuration.GetInt32(BenchmarkParameterNames.ActiveShipCount);
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int stride = configuration.GetInt32(BenchmarkParameterNames.ScopeStride);
        int layout = configuration.GetInt32(BenchmarkParameterNames.Layout);
        int motion = configuration.GetInt32(BenchmarkParameterNames.MotionKind);
        long spacing = configuration.Get(BenchmarkParameterNames.ParticipantSpacing);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds);
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
            var id = new ShipId((ulong)i + 1);
            ids[i] = id;
            SystemPosition origin = Position(x, y, (ulong)(i % systems) + 1);
            movement.Add(id, origin);
            if (i >= active) continue;
            long sign = rank % 2 == 0 ? 1 : -1;
            SystemPosition target = Position(checked(x + sign * distance), checked(y - sign * distance), origin.SystemId.Value);
            if (motion == 1 && i % 2 == 0)
            {
                var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
                    new ShipKinematicState(origin, ShipVelocity.Zero, ShipHeading.Zero), target, null,
                    new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
                    new ManeuverTurnRate(45000)).ExecutablePlan!;
                ulong waypoint = Math.Min(plan.EndsAt.Milliseconds / 2, duration / 2);
                if (waypoint > 0) plan = plan.WithAdditionalBoundaries([new SimulationTime(waypoint)]);
                movement.CommitStartTerminalManeuver(id, plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
            }
            else movement.CommitStartOrReplace(id, new TravelLeg.Local(origin, target, new SimulationDuration(duration)),
                SimulationTime.Zero, static value => value);
        }
        return new RuntimeSweptFixture(movement.CaptureInteractionReadView(SimulationTime.Zero), ids,
            ids.Where((_, i) => i % stride == 0).ToArray(),
            new SimulationTime(configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds)));
    }

    private static SystemPosition Position(long x, long y, ulong system) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));
}
