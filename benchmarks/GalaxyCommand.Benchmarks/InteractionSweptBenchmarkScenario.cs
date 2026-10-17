using System.Collections.ObjectModel;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Benchmark-only swept bounds, index construction, and exact contact comparison over explicit scopes.</summary>
internal sealed class InteractionSweptBenchmarkScenario(string kind) : IBenchmarkScenario
{
    /// <summary>
    /// Builds two-dimensional fixtures with alternating motion directions. Kind
    /// 0 is linear, 1 is a quadratic turnback with interior extrema, and 2 is a
    /// whole passive-braking plan with a fractional stop and inserted event.
    /// Geometry and indexes are prepared once for [0, requested horizon]. All
    /// setup, narrow-phase queries, and contact digesting remain inside timing.
    /// </summary>
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int ships = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int active = configuration.GetInt32(BenchmarkParameterNames.ActiveShipCount);
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int queries = configuration.GetInt32(BenchmarkParameterNames.QueryCount);
        int stride = configuration.GetInt32(BenchmarkParameterNames.ScopeStride);
        int layout = configuration.GetInt32(BenchmarkParameterNames.Layout);
        int motion = configuration.GetInt32(BenchmarkParameterNames.MotionKind);
        long spacing = configuration.Get(BenchmarkParameterNames.ParticipantSpacing);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds);
        ulong range = configuration.GetUInt64(BenchmarkParameterNames.RangeUnits);
        var through = new SimulationTime(configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds));
        var end = new SimulationTime(duration);
        int side = (int)Math.Ceiling(Math.Sqrt((ships + systems - 1) / systems / (layout == 1 ? 4.0 : 1.0)));
        var paths = new PrototypeSweepTrajectory[ships];
        for (int i = 0; i < ships; i++)
        {
            int rank = i / systems;
            int cluster = layout == 1 ? rank % 4 : 0;
            int local = layout == 1 ? rank / 4 : rank;
            long x = checked((local % side - side / 2) * spacing + (layout == 1 ? (cluster % 2 * 2 - 1) * 10000L : 0));
            long y = checked((local / side - side / 2) * spacing + (layout == 1 ? (cluster / 2 * 2 - 1) * 10000L : 0));
            var id = new ShipId((ulong)i + 1);
            SystemPosition position = Position(x, y, (ulong)(i % systems) + 1);
            PrototypeSweepMotion geometry = new PrototypeSweepMotion.Stationary(position);
            if (i < active)
            {
                long sign = rank % 2 == 0 ? 1 : -1;
                if (motion == 0)
                    geometry = new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(new LocalMotionSegment(
                        new MotionId(id.Value), new EventGeneration(1), position,
                        Position(checked(x + sign * distance), checked(y - sign * distance), position.SystemId.Value), SimulationTime.Zero, end)));
                else if (motion == 1)
                {
                    // Integer kinematics are the fixture input. Bounds preserve the
                    // resulting exact curve, including its interior turnback.
                    long velocity = checked(sign * 4_000_000 * distance / (long)duration);
                    long acceleration = checked(-sign * 8_000_000 * distance / (long)duration * 1000 / (long)duration);
                    geometry = new PrototypeSweepMotion.Curve(InteractionPhaseCurve.From(new AnalyticManeuverSegment(
                        SimulationTime.Zero, end, new ShipKinematicState(position, new ShipVelocity(velocity, -velocity), ShipHeading.Zero),
                        new ShipAcceleration(acceleration, -acceleration), ShipAngularRate.Zero)));
                }
                else
                {
                    long speed = checked(2_000_000 * distance / (long)duration);
                    long deceleration = checked(speed * 1000 / (long)duration + 1);
                    var plan = ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(new DecelerationManeuverSegment(
                        ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero,
                        new ShipKinematicState(position, new ShipVelocity(sign * speed, 0), ShipHeading.Zero), new ManeuverAcceleration(checked((ulong)deceleration))));
                    if (plan.EndsAt.Milliseconds > 1)
                        plan = plan.WithAdditionalBoundaries([new SimulationTime(plan.EndsAt.Milliseconds / 2)]);
                    geometry = new PrototypeSweepMotion.Plan(plan);
                }
            }
            paths[i] = new PrototypeSweepTrajectory(id, geometry);
        }

        ShipId[] scope = paths.Where((_, i) => i % stride == 0).Select(path => path.Ship).ToArray();
        var prepared = new PrototypeSweptQueries(paths, scope, SimulationTime.Zero, through, kind,
            configuration.Get(BenchmarkParameterNames.GridWidth), configuration.GetInt32(BenchmarkParameterNames.TreeLeafSize));
        var digest = new DeterministicDigest();
        long seed = configuration.Get(BenchmarkParameterNames.Seed);
        foreach (long value in new long[] { ships, active, systems, queries, stride, layout, motion, spacing, distance, seed }) digest.Add(value);
        digest.Add(duration); digest.Add(range); digest.Add(through.Milliseconds);
        long contacts = 0, candidates = 0, boxTests = 0;
        for (int i = 0; i < queries; i++)
        {
            ShipId source = paths[(int)(((long)i + seed) % ships)].Ship;
            PrototypeSweepResult result = prepared.Query(source, range);
            digest.Add(source.Value); digest.Add((ulong)result.Matches.Count);
            foreach (PrototypeSweepMatch match in result.Matches)
            {
                digest.Add(match.Ship.Value); digest.Add(match.Contact.Timestamp.Milliseconds);
                digest.Add(match.Contact.InRangeAtTimestamp); digest.Add(match.Contact.AlreadyInRange);
            }
            contacts += result.Matches.Count;
            candidates += result.Candidates;
            boxTests += result.BoxTests;
        }
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["ships"] = ships, ["activeShips"] = active, ["systems"] = systems, ["queries"] = queries,
            ["scopedEntries"] = checked((long)scope.Length * queries), ["contacts"] = contacts,
            ["broadPhaseCandidates"] = candidates, ["exactChecks"] = candidates, ["indexBoxTests"] = boxTests,
            ["indexEntries"] = prepared.Entries, ["indexNodes"] = prepared.Nodes, ["gridOverflowEntries"] = prepared.OverflowEntries,
        };
        return new ScenarioCorrectnessResult(digest.ToString(), through.Milliseconds, new ReadOnlyDictionary<string, long>(counts));
    }

    private static SystemPosition Position(long x, long y, ulong system) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));
}
