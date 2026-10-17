# Public swept-query API measurements

[Prepared swept contract](moving-ship-interactions.md#prepared-swept-runtime-contract) · [Whole-plan prototypes](interaction-swept-index-comparison.md) · [Task list](task-list.md)

On 2026-10-12, TASK-071 added version 1 `interaction.runtime-swept-scan`,
`interaction.runtime-swept-grid`, and `interaction.runtime-swept-hierarchy`
presets. These call `InteractionSweptQuery.Prepare`, `PrepareScope`, and `Find`
over real movement-owner captures. They exercise the accepted next-boundary
runtime contract rather than promoting whole-plan prototype horizons.

## Fixture and timed scope

Each iteration creates spatial participants and commits their motions, captures
one stable view at time zero, prepares geometry and a caller scope, runs all
queries, and digests target identity, contact timestamp, and both contact flags.
All setup and digest costs are timed. Preparation and querying are not separately
instrumented, and this is not an advancing session or invalidation workload.
Allocation means total managed bytes allocated per iteration, not retained index
size, peak memory, or resident process memory. `scopedEntries` reports scope size
times query count; it does not count exact pair checks. The public path contains
no prototype diagnostic structures or counters.

Mode 0 uses alternating diagonal linear motions and stationary ships. It retains
the prototype lattice/cluster layout and canonical ordered-contact digest.
Mode 1 uses the same layout but replaces every other active ship with a real
bounded terminal maneuver from rest. These plans use acceleration and braking
10,000, speed 30,000, and turn rate 45,000 in the corresponding maneuver units.
An inserted event occurs at the smaller of half the plan duration and half the
linear duration. The fixture test proves that the first maneuver translates
before this boundary. Runtime preparation stops at that event or any earlier
scheduled phase boundary. The plans, their current state, and event boundary
come from the movement owner; there are no manually assembled snapshots.
Mode 1 is a mixed runtime fixture, not the older prototype's quadratic mode.
Mode 2 is unsupported here; whole-braking prototype fixtures remain available
under the existing `interaction.swept-*` presets.

The canonical public fixture uses 250 ships, 125 active, ten queries, one system,
a centered lattice with 100-unit spacing, distance 2,000, and range 10. Both
linear duration and requested horizon are 1,000 milliseconds. Benchmark callers
explicitly supply grid width 16, cell budget 4,096, and hierarchy leaf capacity
four. These experiment settings are configurable and are not runtime defaults.
Scan remains the runtime default; no automatic selector is introduced.

## Controlled results

The controlled matrix uses 2,500 ships and 1,250 active, with separate processes
for each strategy/workload, `DOTNET_TieredCompilation=0`, three warmups, and five
measured iterations. Reports identify .NET 10.0.10 on macOS 26.7.1, Arm64,
11 logical processors, and workstation garbage collection. Source is the local,
uncommitted TASK-071 implementation. The
[manifest](../benchmarks/results/2026-10-12-runtime-swept-manifest.json) records
all 15 commands and raw report filenames. Each row agrees on ordered contact
digest and every reported semantic count across strategies.

Values are median whole-iteration milliseconds / median allocated decimal MB.

| Workload | Scan | Grid | Hierarchy | Contacts |
| --- | --- | --- | --- | --- |
| linear-long-q10 | 5.21 ms / 5.55 MB | 6.37 ms / 6.53 MB | 7.94 ms / 10.42 MB | 15 |
| linear-short-q1000 | 17.90 ms / 4.79 MB | 8.65 ms / 9.13 MB | 7.20 ms / 9.71 MB | 0 |
| mixed-short-q1000 | 21.10 ms / 8.15 MB | 10.29 ms / 11.71 MB | 10.83 ms / 13.13 MB | 0 |
| dense-q100 | 66.74 ms / 57.39 MB | 80.23 ms / 79.95 MB | 85.00 ms / 84.35 MB | 249900 |
| mixed-clustered-scoped-q100 | 57.40 ms / 87.70 MB | 54.45 ms / 88.65 MB | 54.75 ms / 90.65 MB | 6944 |

The long linear row retains distance 2,000 and ten queries. Short rows use
distance 20 and 1,000 queries, with otherwise matching parameters. The dense
row uses coincident starting positions, range 10,000, and 100 queries. The
clustered mixed row uses distance 20, 100 queries, four clusters per system,
spacing one, two systems, and scope stride two. Sources are selected from all
ships, including those outside the target scope.

Scan has the lowest cost in the low-query long-sweep case and the dense case.
Indexes reduce time on the tested repeated short queries, with hierarchy ahead
on linear motion and grid slightly ahead on the mixed fixture. Both indexes
allocate more than scan in every row. The clustered mixed case spends much more
on exact contacts than the zero-contact sparse cases; its small timing
differences do not establish a preferred policy. Results support the existing
explicit caller choice and scan default. They do not establish portable
crossover thresholds, worker scaling, or whole-session throughput.

## Correctness and reproduction

All three unmodified public presets retain canonical digest `418f5c9774f5bbdc`.
The [canonical report](../benchmarks/results/2026-10-12-runtime-swept-canonical.json)
uses normal runtime compilation settings and checks correctness rather than
providing a controlled timing comparison. Tests compare mixed and linear
captured fixtures against exact boundary-clipped interest forecasts, including
sources outside scope, and enforce valid tuning and heavy-suite opt-in.
All 1,278 solution tests pass, and the Release solution build has zero warnings
or errors. No timing thresholds are asserted in tests.

```sh
dotnet build benchmarks/GalaxyCommand.Benchmarks -c Release
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.runtime-swept-hierarchy \
  --set shipCount=2500 --set activeShipCount=1250 \
  --set destinationDistance=20 --set queryCount=1000 \
  --set warmupIterations=3 --set measuredIterations=5
```

Run each strategy in a separate process. The manifest contains the remaining
workload commands, including `motionKind=1` for mixed runtime motion.
