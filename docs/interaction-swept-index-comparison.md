# Swept spatial interaction index comparison

Historical measurement record: the owner subsequently selected explicit
caller strategies, scan as default, and explicit tuning. The accepted
[prepared swept runtime contract](moving-ship-interactions.md#prepared-swept-runtime-contract)
stops at next scheduled boundaries and uses one fixed interval per preparation.
Runtime swept preparation is now implemented;
[public runtime measurements](interaction-runtime-swept-measurements.md) exercise
its boundary-clipped contract. The whole-plan fixtures below retain their original interval semantics.

[Interaction design](moving-ship-interactions.md) · [Instant index comparison](interaction-index-comparison.md) · [Task list](task-list.md)

This `TASK-071` slice extends the owner-approved benchmark-only grid and
immutable bounding-box hierarchy comparison to long swept bounds and analytic
motion. Production discovery, interests, scheduling, and owner commits remain
on their existing paths. No runtime index or dispatch policy is adopted.

## Workload and exact reference

The version 1 presets `interaction.swept-scan`, `interaction.swept-grid`, and
`interaction.swept-hierarchy` share configuration and contact digest inputs.
Their canonical fixture uses 250 ships, 125 moving, one system, 10 queries,
100 m lattice spacing, 2,000 m linear diagonal displacement, a 1,000 ms motion
interval and requested horizon, range 10 m, scope stride 1, and seed 1.
Default grid width is 16 m and tree leaf size is four. The canonical ordered
contact digest is `418f5c9774f5bbdc`; the final
[canonical report](../benchmarks/results/2026-10-08-swept-index-canonical.json)
verifies all three presets against that enforced digest.

Positions use the same centered two-dimensional lattice or four separated
clusters as the instant comparison. Ship identities interleave systems.
Moving ships alternate direction by their rank within each system. Caller
scope selects every nth identity starting with the first; sources come from
all identities, including those outside the scope. These are experiment
fixtures, not gameplay activity proportions or automatic interest registration.

Each participant contributes conservative bounds prepared once over the
requested interval [0, horizon]. Known geometry clips that interval; nothing
is extrapolated. The scan applies the same closed expanded-box rejection as
the indexes before exact narrow phase. Thus, all three modes must deliver the
same candidates to exact evaluation. The index comparison measures how boxes
are found, not a weakened scan that recomputes bounds or omits broad rejection.

| `motionKind` | Fixture input | Exact evaluation contract |
| ---: | --- | --- |
| 0 | Linear diagonal displacement (+/-d, -/+d) over duration T | Continuous rational phase curve, including brief crossings at rounded timestamps |
| 1 | Quadratic diagonal turnback; v = trunc(sign * 4,000,000 * d / T) mm/s; a = trunc(trunc(-sign * 8,000,000 * d / T) * 1,000 / T) mm/s²; y uses -v and -a | Exact phase geometry and interior extrema; no published-position sampling or inferred committed plan resets |
| 2 | Whole passive-braking plan along x; speed = trunc(2,000,000 * d / T) mm/s; deceleration = trunc(speed * 1,000 / T) + 1 mm/s² | Fractional stop hold and committed original-boundary reset; an inserted midpoint event preserves the underlying curve |

The extra unit of braking deceleration exercises a fractional stop for the
measured fixtures. Whole plans and standalone phase curves use separate
workloads; the adapter rejects their unsupported mixture. Stationary targets
are allowed in each workload. Durations below 1,000 ms and zero-speed braking
fixtures are rejected before measurement. These rules keep experimental
kinematics explicit rather than introducing new movement policy.

All fixtures use existing exact geometry and range APIs. The result digest
includes each source, identity-ordered target, rounded first-contact timestamp,
`AlreadyInRange`, and `InRangeAtTimestamp`, plus fixture parameters. Width,
leaf size, and index traversal do not affect the digest. A past swept contact
whose rounded timestamp lies outside actual contact remains valid evidence.

Grid construction retains the previously documented 4,096-cell safeguard:
large proxies enter an overflow list, and large queries scan occupied cells
instead of enumerating empty spans. Overflow boxes in the queried system are
always tested. Hierarchy nodes conservatively union their leaves. Both modes
reduce duplicate identities before exact evaluation.

## Measurement scope

Each iteration includes fixture construction, exact bound preparation, index
construction, queries, immutable contact results, and digesting. This is not
isolated query time or advancing whole-session throughput. Proxy-box test
counts exclude tree parent traversal and grid cell lookups. Structural counts
report stored proxy references, cells/tree nodes, and overflow entries; they
do not measure retained managed bytes. Allocation is cumulative per iteration.

For controlled comparisons, each preset runs in a separate process with
`DOTNET_TieredCompilation=0`, one warmup, and five measured iterations. This
avoids shared tiered-compilation effects discovered in the instant comparison;
it is an algorithm experiment rather than a prediction of default-runtime
whole-session throughput. Timings are informational. Digests, semantic counts,
and ordered contact evidence must agree.

## Controlled measurements

Measured on 2026-10-08: .NET 10.0.10, macOS 26.7.1, Arm64,
11 reported logical processors, server GC disabled. All 24 comparisons agree
on exact candidates, contacts, timestamps, and flags through their ordered
digests. Five measured iterations agree within every preset. The
[control manifest](../benchmarks/results/2026-10-08-swept-index-manifest.json)
records process isolation, runtime environment, and all 72 individual reports.

Each table uses scan / grid / hierarchy order. Kind names refer to the explicit
fixture contracts above. Timings are whole-iteration medians; raw reports retain
minimum and maximum times. Allocated MB uses decimal units.

### Long sweeps across crowded-system sizes

These fixtures use distance 2,000 m and 10 queries per view.

| Ships | Motion | Median ms: scan / grid / hierarchy | Allocated MB: scan / grid / hierarchy | Exact checks | Contacts | Raw scan / grid / hierarchy |
| ---: | --- | --- | --- | ---: | ---: | --- |
| 250 | Linear | 1.85 / 1.91 / 2.24 | 3.13 / 3.27 / 3.47 | 1,000 | 16 | [scan](../benchmarks/results/2026-10-08-swept-index-250-motion-0-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-250-motion-0-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-250-motion-0-distance-2000-hierarchy.json) |
| 250 | Quadratic | 3.70 / 3.95 / 3.93 | 5.79 / 5.93 / 6.13 | 1,000 | 16 | [scan](../benchmarks/results/2026-10-08-swept-index-250-motion-1-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-250-motion-1-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-250-motion-1-distance-2000-hierarchy.json) |
| 250 | Braking plan | 4.57 / 6.61 / 4.56 | 5.98 / 7.78 / 6.27 | 110 | 40 | [scan](../benchmarks/results/2026-10-08-swept-index-250-motion-2-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-250-motion-2-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-250-motion-2-distance-2000-hierarchy.json) |
| 2,500 | Linear | 6.98 / 7.95 / 9.86 | 10.99 / 11.89 / 15.69 | 3,300 | 15 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-2000-hierarchy.json) |
| 2,500 | Quadratic | 12.48 / 13.42 / 16.00 | 18.09 / 19.11 / 22.94 | 3,300 | 15 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-2000-hierarchy.json) |
| 2,500 | Braking plan | 20.18 / 32.64 / 22.00 | 25.18 / 37.17 / 29.82 | 240 | 115 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-2000-hierarchy.json) |
| 10,000 | Linear | 15.92 / 18.25 / 31.13 | 21.67 / 24.91 / 43.01 | 3,540 | 15 | [scan](../benchmarks/results/2026-10-08-swept-index-10000-motion-0-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-10000-motion-0-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-10000-motion-0-distance-2000-hierarchy.json) |
| 10,000 | Quadratic | 28.96 / 31.27 / 46.92 | 35.89 / 39.01 / 57.10 | 3,540 | 15 | [scan](../benchmarks/results/2026-10-08-swept-index-10000-motion-1-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-10000-motion-1-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-10000-motion-1-distance-2000-hierarchy.json) |
| 10,000 | Braking plan | 59.88 / 138.29 / 74.52 | 67.67 / 113.20 / 88.89 | 240 | 115 | [scan](../benchmarks/results/2026-10-08-swept-index-10000-motion-2-distance-2000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-10000-motion-2-distance-2000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-10000-motion-2-distance-2000-hierarchy.json) |

At 2,500 ships, the grid's linear and
quadratic fixtures store 1,250 moving proxies in overflow. The stationary
proxies occupy cells. At 10,000 ships this becomes 5,000 overflow proxies. Every
same-system overflow proxy must be tested for each source, so overflow limits
storage growth but also limits grid pruning.

Braking plans move only along x. Their 2,000 m boxes remain below the per-proxy
cell cap and create substantial duplicate coverage: at 2,500 ships, the grid
stores 158,750 references in 14,850 occupied cells, versus 2,500 references in
1,927 hierarchy nodes. At 10,000 ships, this becomes 635,000 grid references
in 47,900 cells, versus 10,000 references in 7,711 hierarchy nodes. Grid box-test
counts include repeated cell references, which are deduplicated before exact
narrow phase. Its cell cap bounds each proxy, not total duplication.

### Span, clustering, and caller-scope probes

These cases use 2,500 ships and 10 queries per view. Short distance is 20 m;
huge distance is 100,000 m. Cluster fixtures use 1 m spacing and 2,000 m
displacement. The two-system fixture uses scope stride 2, so scoped targets
belong to the first system while query sources alternate systems.

| Probe | Motion | Median ms: scan / grid / hierarchy | Allocated MB: scan / grid / hierarchy | Exact checks | Contacts | Raw scan / grid / hierarchy |
| --- | --- | --- | --- | ---: | ---: | --- |
| 20 m | Linear | 1.90 / 5.31 / 4.89 | 3.16 / 6.94 / 7.73 | 0 | 0 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-20-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-20-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-20-hierarchy.json) |
| 20 m | Quadratic | 3.88 / 8.30 / 6.17 | 4.80 / 8.56 / 9.40 | 0 | 0 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-20-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-20-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-20-hierarchy.json) |
| 20 m | Braking plan | 9.56 / 11.01 / 12.14 | 12.33 / 14.75 / 16.84 | 0 | 0 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-20-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-20-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-20-hierarchy.json) |
| 100,000 m | Linear | 12.62 / 13.46 / 19.01 | 21.23 / 22.39 / 26.21 | 7,475 | 15 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-100000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-100000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-100000-hierarchy.json) |
| 100,000 m | Quadratic | 24.94 / 26.48 / 27.45 | 35.19 / 36.39 / 40.17 | 7,475 | 15 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-100000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-100000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-100000-hierarchy.json) |
| 100,000 m | Braking plan | 22.87 / 23.55 / 26.06 | 29.12 / 29.89 / 33.66 | 365 | 125 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-100000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-100000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-100000-hierarchy.json) |
| Clusters | Linear | 8.68 / 8.89 / 11.19 | 15.04 / 15.41 / 19.83 | 3,823 | 1,018 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-clusters-motion-0-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-clusters-motion-0-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-clusters-motion-0-hierarchy.json) |
| Clusters | Quadratic | 16.97 / 16.94 / 20.24 | 24.38 / 24.78 / 29.20 | 3,823 | 1,018 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-clusters-motion-1-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-clusters-motion-1-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-clusters-motion-1-hierarchy.json) |
| Two systems, scoped | Linear | 4.03 / 4.41 / 5.02 | 5.89 / 6.33 / 8.11 | 1,241 | 6 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-scoped-two-systems-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-scoped-two-systems-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-scoped-two-systems-hierarchy.json) |

The short fixtures have no accepted candidates. Even with that strong pruning,
index construction does not amortize across only 10 queries. At huge spans,
braking proxies also enter overflow; storage duplication falls, but overflow
scanning remains required. No correctness contact or candidate is lost.

### Higher query-volume probes

The 2,500-ship short fixtures use 1,000 queries; long fixtures use 100.
Construction still occurs once per view inside timing.

| Span and queries | Motion | Median ms: scan / grid / hierarchy | Allocated MB: scan / grid / hierarchy | Exact checks | Contacts | Raw scan / grid / hierarchy |
| --- | --- | --- | --- | ---: | ---: | --- |
| 20 m, 1,000 queries | Linear | 13.67 / 8.55 / 4.85 | 3.36 / 7.75 / 8.16 | 0 | 0 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-20-queries-1000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-20-queries-1000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-20-queries-1000-hierarchy.json) |
| 20 m, 1,000 queries | Quadratic | 15.16 / 9.35 / 6.72 | 5.00 / 9.39 / 9.92 | 0 | 0 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-20-queries-1000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-20-queries-1000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-20-queries-1000-hierarchy.json) |
| 20 m, 1,000 queries | Braking plan | 21.16 / 11.53 / 13.68 | 12.52 / 15.46 / 17.39 | 0 | 0 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-20-queries-1000-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-20-queries-1000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-20-queries-1000-hierarchy.json) |
| 2,000 m, 100 queries | Linear | 79.48 / 82.86 / 84.30 | 143.78 / 147.53 / 151.32 | 51,772 | 494 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-2000-queries-100-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-2000-queries-100-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-0-distance-2000-queries-100-hierarchy.json) |
| 2,000 m, 100 queries | Quadratic | 172.41 / 176.55 / 175.48 | 265.06 / 268.81 / 272.60 | 51,772 | 494 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-2000-queries-100-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-2000-queries-100-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-1-distance-2000-queries-100-hierarchy.json) |
| 2,000 m, 100 queries | Braking plan | 107.61 / 127.01 / 109.97 | 140.80 / 153.40 / 145.64 | 2,800 | 1,240 | [scan](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-2000-queries-100-scan.json) / [grid](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-2000-queries-100-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-swept-index-2500-motion-2-distance-2000-queries-100-hierarchy.json) |

## Assessment and runtime decision

No structure wins every tested workload. Prepared bound scans generally have
lower median time and allocation with 10 queries per view. At 1,000 short-sweep
queries, both indexes amortize construction and beat scan: for linear motion,
13.67 ms scan, 8.55 ms grid, and 4.85 ms hierarchy. The hierarchy also wins the
quadratic short-sweep fixture; grid wins short whole-plan braking. At 100 long
queries, scan remains fastest for all three motion kinds. Every indexed case
allocates more than the prepared bound scan.

Together with the instant comparison, these results support making preparation
strategy explicit before runtime integration. The recommended owner-review
proposal is caller-selected immutable preparation: scan, sparse grid, or
bounding-box hierarchy, with existing scan behavior retained for callers that
do not select an index. This is a performance choice only; it would not change
caller scopes, exact geometry, gameplay eligibility, durable interests, or
canonical commit order. The hierarchy remains a viable short-sweep candidate,
so the evidence does not justify dropping it based on instant queries alone.
This proposal has not been adopted or implemented.

An alternative is a deterministic automatic selector based on measured scope,
bound extent, query volume, and occupancy. These experiments do not establish
safe thresholds for that selector. Runtime integration, selection ownership,
parameter defaults, and any automatic dispatch remain owner decisions.

Tests cover reference-equivalent contact evidence, reversed input order,
caller-scope batches, different systems, partial closed horizons, original
committed resets, inserted events, huge overflow coverage, and brief contact
whose rounded timestamp is outside actual contact. No workers, durable index
state, interaction registration, invalidation-churn workload, or whole-session
throughput measurement is introduced. Retained managed bytes and separate
construction/query timing remain unavailable.

## Reproduction

Build in Release, then run each preset in its own process for the default
long-linear comparison:

```sh
dotnet build benchmarks/GalaxyCommand.Benchmarks -c Release
for task071_kind in scan grid hierarchy; do
  DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
    --project benchmarks/GalaxyCommand.Benchmarks -- \
    --suite full --preset "interaction.swept-$task071_kind"
done
```

For crowded size add `--set shipCount=2500 --set activeShipCount=1250`.
Use 10,000 / 5,000 for stress size. Set `motionKind=1` for quadratic motion
or `motionKind=2` for braking plans. `destinationDistance=20` or `100000`
changes span; `queryCount=100` or `1000` changes amortization. Cluster probes
use `layout=1` with `participantSpacing=1`; caller-scope probes use
`systemCount=2` with `scopeStride=2`. All other defaults remain unchanged.
