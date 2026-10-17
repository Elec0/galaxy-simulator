# Spatial interaction index prototype comparison

Historical measurement record: on 2026-10-09 the owner approved
[caller-selected instant preparation](moving-ship-interactions.md#caller-selected-instant-preparation)
with scan as the default and explicit tuning. The runtime and benchmark adapters
now share the immutable index implementations. The measurements below retain
their original workload and tuning.

[Interaction design](moving-ship-interactions.md#spatial-index-evaluation-for-owner-selection) · [Prepared scan measurements](interaction-performance-baseline.md) · [Task list](task-list.md)

The owner approved comparing a sparse grid and an immutable bounding-box
hierarchy on 2026-10-08 within `TASK-071`. Both implementations live in the
benchmark project. The production interaction path still uses its existing
reference and prepared scans. No runtime index, tuning policy, or worker
execution has been adopted.

## Contract and measurement scope

The version 1 presets `interaction.index-scan`, `interaction.index-grid`, and
`interaction.index-hierarchy` share fixtures and result digest inputs. Their
canonical sparse digest is `b75076796115dc88`; the final
[canonical report](../benchmarks/results/2026-10-08-index-canonical.json) verifies
all three presets against that enforced digest. Each run includes movement
fixture construction, exact geometry and scope preparation, index construction,
queries, immutable result construction, and digesting. These are complete
iterations, not isolated query timings or whole-session throughput results.
The [design document](moving-ship-interactions.md#prototype-construction-and-fixture-rules)
records the exact layout, negative-coordinate coverage, grid traversal safeguard,
tree split rules, scope rules, and tuning defaults.

Machine: .NET 10.0.10, macOS 26.7.1, Arm64, 11 reported logical processors,
server GC disabled. Initial runs execute scan, grid, then hierarchy with one
warmup and five measured iterations. All 20 initial comparisons agree on
result digests and semantic counts, including identity order. Each preset also
checks its own repeated-iteration counts. Performance counters differ by index
and are not part of the result digest. Raw JSON retains timing ranges,
allocations, structural counts, collection totals, parameters, and machine
metadata. Allocations are cumulative per iteration, not retained or peak memory.

## Initial two-dimensional matrix

Every row uses 100 queries. Sparse lattice spacing is 100 m; cluster spacing
is 1 m within four separated clusters; dense spacing is zero. Half the ships
move diagonally, sampled at 501 ms. The large-range fixture uses a 10,000 m
range, two systems, and every second ship in scope. Consequently, its selected
scope lies in the first system and second-system sources find no targets.
Other rows use a 10 m range, one system, and the full caller scope.

The entries below show median milliseconds for scan/grid/hierarchy, median
allocated MB in the same order, and exact distance checks for scan/indexes.
Both indexes produce the same distance-check count after closed-box pruning.

| Ships | Layout | Median ms: scan / grid / hierarchy | Allocated MB: scan / grid / hierarchy | Exact checks: scan / indexed | Raw report |
| ---: | --- | --- | --- | --- | --- |
| 250 | sparse | 4.01 / 1.51 / 1.59 | 0.58 / 0.80 / 0.93 | 24,900 / 0 | [JSON](../benchmarks/results/2026-10-08-index-comparison-250-sparse.json) |
| 250 | clusters | 4.78 / 3.01 / 3.30 | 0.67 / 1.00 / 1.26 | 24,900 / 3,028 | [JSON](../benchmarks/results/2026-10-08-index-comparison-250-clusters.json) |
| 250 | dense | 4.75 / 14.29 / 16.54 | 0.91 / 3.30 / 3.56 | 24,900 / 24,900 | [JSON](../benchmarks/results/2026-10-08-index-comparison-250-dense.json) |
| 250 | large-range | 2.68 / 5.98 / 4.84 | 0.93 / 1.80 / 1.65 | 6,200 / 6,200 | [JSON](../benchmarks/results/2026-10-08-index-comparison-250-large-range.json) |
| 2,500 | sparse | 41.67 / 14.54 / 18.20 | 5.86 / 7.13 / 10.43 | 249,900 / 0 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-sparse.json) |
| 2,500 | clusters | 42.56 / 27.78 / 32.52 | 6.34 / 8.36 / 12.50 | 249,900 / 21,176 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-clusters.json) |
| 2,500 | dense | 44.25 / 72.15 / 23.20 | 10.21 / 32.52 / 36.77 | 249,900 / 249,900 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-dense.json) |
| 2,500 | large-range | 30.13 / 63.62 / 15.19 | 13.75 / 21.82 / 21.13 | 62,450 / 62,450 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-large-range.json) |
| 10,000 | sparse | 130.40 / 24.79 / 59.84 | 23.20 / 27.40 / 42.81 | 999,900 / 0 | [JSON](../benchmarks/results/2026-10-08-index-comparison-10000-sparse.json) |
| 10,000 | clusters | 126.10 / 39.34 / 54.20 | 23.85 / 25.85 / 45.46 | 999,900 / 24,464 | [JSON](../benchmarks/results/2026-10-08-index-comparison-10000-clusters.json) |
| 10,000 | dense | 87.90 / 73.33 / 111.88 | 40.47 / 134.65 / 155.29 | 999,900 / 999,900 | [JSON](../benchmarks/results/2026-10-08-index-comparison-10000-dense.json) |
| 10,000 | large-range | 105.42 / 48.04 / 53.08 | 58.28 / 90.52 / 89.64 | 249,950 / 249,950 | [JSON](../benchmarks/results/2026-10-08-index-comparison-10000-large-range.json) |

At 2,500 ships, sparse queries eliminate all 249,900 distance checks. Clusters
reduce them to 21,176, about 92% fewer. Dense and large-range fixtures retain
all same-system scoped targets; their required matches dominate output work.
The indexes allocate more than the prepared scan in every initial row. Candidate
pruning therefore does not imply an allocation improvement.

## Query volume and tuning probes

At 2,500 ships, additional probes use one or 1,000 queries per captured view.
Two cluster probes vary grid width and leaf size. These are experimental
settings, not recommended gameplay parameters. Construction occurs once per
iteration in every case, so query-volume probes expose its amortization without
claiming a separately measured construction time.

| Probe | Median ms: scan / grid / hierarchy | Allocated MB: scan / grid / hierarchy | Raw report |
| --- | --- | --- | --- |
| sparse, 1 queries | 18.12 / 15.62 / 20.71 | 5.86 / 7.05 / 10.37 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-sparse-queries-1.json) |
| clusters, 1 queries | 16.69 / 13.96 / 18.45 | 5.86 / 5.88 / 10.38 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-clusters-queries-1.json) |
| dense, 1 queries | 17.07 / 15.09 / 19.24 | 5.90 / 6.13 / 10.63 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-dense-queries-1.json) |
| sparse, 1,000 queries | 42.39 / 18.31 / 24.02 | 5.90 / 7.85 / 11.02 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-sparse-queries-1000.json) |
| clusters, 1,000 queries | 66.20 / 39.06 / 29.22 | 11.37 / 30.96 / 35.12 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-clusters-queries-1000.json) |
| dense, 1,000 queries | 64.69 / 116.83 / 160.39 | 49.05 / 273.63 / 277.88 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-dense-queries-1000.json) |
| clusters, grid 8 m, leaf 1 | 43.90 / 27.73 / 37.57 | 6.34 / 8.39 / 14.16 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-clusters-width-8-leaf-1.json) |
| clusters, grid 128 m, leaf 16 | 44.22 / 18.47 / 23.68 | 6.34 / 8.01 / 11.41 | [JSON](../benchmarks/results/2026-10-08-index-comparison-2500-clusters-width-128-leaf-16.json) |

## Warmup and process controls

Six follow-up runs reverse preset order and use five warmups with seven
measured iterations. Scan medians change materially, including 2,500 sparse
from 41.67 ms initially to 8.05 ms with reversed order. Candidate counts and
result digests remain identical. Initial timing rankings therefore do not
establish a stable winner; increased warmup alone does not remove shared
compilation effects.

| Ships | Layout | Reversed-order median ms: scan / grid / hierarchy | Raw report |
| ---: | --- | --- | --- |
| 2,500 | sparse | 8.05 / 15.18 / 17.62 | [JSON](../benchmarks/results/2026-10-08-index-confirmation-2500-sparse.json) |
| 2,500 | clusters | 8.79 / 20.37 / 28.51 | [JSON](../benchmarks/results/2026-10-08-index-confirmation-2500-clusters.json) |
| 2,500 | dense | 7.89 / 20.13 / 41.28 | [JSON](../benchmarks/results/2026-10-08-index-confirmation-2500-dense.json) |
| 10,000 | sparse | 21.28 / 23.97 / 43.72 | [JSON](../benchmarks/results/2026-10-08-index-confirmation-10000-sparse.json) |
| 10,000 | clusters | 24.37 / 19.24 / 47.17 | [JSON](../benchmarks/results/2026-10-08-index-confirmation-10000-clusters.json) |
| 10,000 | dense | 32.33 / 68.82 / 94.53 | [JSON](../benchmarks/results/2026-10-08-index-confirmation-10000-dense.json) |

The controlled comparison then runs each preset in a fresh process with
`DOTNET_TieredCompilation=0`, three warmups, and seven measured iterations.
This removes shared tiered-compilation warmup from the comparison, but changes
the runtime compilation configuration. It is an algorithm experiment, not a
prediction of default-runtime whole-session throughput. The
[control manifest](../benchmarks/results/2026-10-08-index-controlled-manifest.json)
records the environment, iteration settings, and individual report paths.
All nine controlled comparisons agree on digests and semantic counts.

| Ships | Layout | Queries | Median ms: scan / grid / hierarchy | Allocated MB: scan / grid / hierarchy | Raw scan / grid / hierarchy |
| ---: | --- | ---: | --- | --- | --- |
| 2,500 | sparse | 100 | 8.21 / 4.26 / 5.60 | 5.36 / 6.72 / 10.02 | [scan](../benchmarks/results/2026-10-08-index-controlled-2500-sparse-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-2500-sparse-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-2500-sparse-hierarchy.json) |
| 2,500 | clusters | 100 | 8.94 / 5.65 / 7.75 | 5.84 / 7.88 / 12.38 | [scan](../benchmarks/results/2026-10-08-index-controlled-2500-clusters-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-2500-clusters-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-2500-clusters-hierarchy.json) |
| 2,500 | dense | 100 | 11.07 / 21.42 / 27.04 | 9.67 / 32.37 / 36.77 | [scan](../benchmarks/results/2026-10-08-index-controlled-2500-dense-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-2500-dense-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-2500-dense-hierarchy.json) |
| 10,000 | sparse | 100 | 33.94 / 18.93 / 28.82 | 21.74 / 27.20 / 42.80 | [scan](../benchmarks/results/2026-10-08-index-controlled-10000-sparse-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-10000-sparse-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-10000-sparse-hierarchy.json) |
| 10,000 | clusters | 100 | 35.13 / 19.10 / 31.72 | 22.27 / 24.86 / 45.45 | [scan](../benchmarks/results/2026-10-08-index-controlled-10000-clusters-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-10000-clusters-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-10000-clusters-hierarchy.json) |
| 10,000 | dense | 100 | 47.93 / 101.29 / 135.99 | 38.89 / 134.84 / 155.28 | [scan](../benchmarks/results/2026-10-08-index-controlled-10000-dense-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-10000-dense-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-10000-dense-hierarchy.json) |
| 2,500 | sparse | 1,000 | 52.18 / 4.59 / 6.07 | 5.41 / 7.43 / 10.61 | [scan](../benchmarks/results/2026-10-08-index-controlled-2500-sparse-queries-1000-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-2500-sparse-queries-1000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-2500-sparse-queries-1000-hierarchy.json) |
| 2,500 | clusters | 1,000 | 63.89 / 25.07 / 28.25 | 10.93 / 30.96 / 35.12 | [scan](../benchmarks/results/2026-10-08-index-controlled-2500-clusters-queries-1000-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-2500-clusters-queries-1000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-2500-clusters-queries-1000-hierarchy.json) |
| 2,500 | dense | 1,000 | 85.90 / 182.39 / 221.41 | 48.60 / 273.63 / 277.88 | [scan](../benchmarks/results/2026-10-08-index-controlled-2500-dense-queries-1000-scan.json) / [grid](../benchmarks/results/2026-10-08-index-controlled-2500-dense-queries-1000-grid.json) / [hierarchy](../benchmarks/results/2026-10-08-index-controlled-2500-dense-queries-1000-hierarchy.json) |

## Assessment and remaining decision

The grid is the stronger prototype for the tested small-range instant queries:
it has lower controlled median time and allocation than this hierarchy at both
crowded-system sizes. At 2,500 sparse ships and 100 queries, controlled median
whole-iteration time is 8.21 ms for scan, 4.26 ms for grid, and 5.60 ms for
hierarchy. Sparse queries amortize construction more strongly at 1,000 queries.
However, grid allocation is still higher than scan, and dense controlled runs
favor the scan at every measured size and query count. This evidence supports
further grid evaluation for nearby instant discovery; it does not support
replacing all prepared scans with an index.

Both indexes retain conservative coverage in tests across negative coordinates,
closed edges, input orders, grid widths, leaf sizes, caller-scope batches,
connector transit exclusion, analytic motion, fractional positions, original
resets, inserted events, and swept contacts. Oversized boxes and huge query
spans remain bounded by stored grid coverage. At 2,500 clustered ships, the
default grid stores 2,708 proxy references in 32 cells; the hierarchy stores
2,500 proxies in 1,927 nodes. These are structural sizes, not measured retained
managed bytes. Grid duplication and hierarchy construction remain distinct costs.

No general index adoption or automatic scan/index dispatch threshold has been
chosen. Timing sensitivity, the scan's dense-output advantage, and unmeasured
long swept-box workloads make such a decision premature. The subsequent [swept comparison](interaction-swept-index-comparison.md)
measures long linear sweeps, analytic bounds, grid overflow, and higher query
volume. Its evidence informs the still-open decision on preparation strategy
and whether instant and forecast discovery share an index. Actual concurrent execution, retained-byte measurement, and
separate construction/query timings also remain unverified.

## Reproduction

Build once in Release, then compare default fixtures:

```sh
dotnet build benchmarks/GalaxyCommand.Benchmarks -c Release

dotnet run -c Release --no-build --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.index-scan --preset interaction.index-grid \
  --preset interaction.index-hierarchy
```

For the controlled clustered 2,500-ship case, run each kind separately:

```sh
for task071_kind in scan grid hierarchy; do
  DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
    --project benchmarks/GalaxyCommand.Benchmarks -- \
    --suite full --preset "interaction.index-$task071_kind" \
    --set layout=1 --set participantSpacing=1 \
    --set warmupIterations=3 --set measuredIterations=7
done
```

Use `shipCount=10000` together with `activeShipCount=5000` for stress size;
`queryCount=1000` for repeated queries; `participantSpacing=0` with `layout=0`
for dense output. Override `gridWidth` and `treeLeafSize` for tuning experiments,
and `scopeStride` and `systemCount` for caller-scope and system distribution.
