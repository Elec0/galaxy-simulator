# Public instant-query API measurements

[Interaction design](moving-ship-interactions.md#caller-selected-instant-preparation) · [Prototype comparison](interaction-index-comparison.md) · [Task list](task-list.md)

On 2026-10-09, TASK-071 added version 1 `interaction.runtime-scan`,
`interaction.runtime-grid`, and `interaction.runtime-hierarchy` presets. These
call the public `InteractionProximityQuery.Prepare`, `PrepareScope`, and `Find`
APIs, using the same two-dimensional fixture and digest inputs as the existing
index comparison. They measure the caller-selected runtime path without the
prototype adapter's extra diagnostic structures or counters.

## Workload and measurement boundary

Each timed iteration constructs movement fixtures, commits active linear
motions, captures one read view, prepares geometry and one caller scope, runs
the requested queries, and digests ordered results. It includes all those costs;
it does not isolate preparation from query duration or measure an advancing
session. Query-volume comparisons expose amortization without claiming a
separate phase timing. Allocation is total managed bytes allocated during the
iteration, not retained index size, peak memory, or process resident memory.

The canonical fixture has 2,500 ships, 1,250 active diagonal movements, one
system, a centered lattice with 100-unit spacing, 100 queries, range 10,
and a 501-millisecond sample within a 1,000-millisecond motion. The full caller
scope includes all ships. Benchmark calls explicitly supply grid width 16,
grid cell budget 4,096, or hierarchy leaf capacity four. These settings are
experiment inputs and can be overridden with `gridWidth`, `gridCellBudget`,
and `treeLeafSize`; they are not runtime defaults or automatic selection rules.

Controlled comparisons use separate processes for each strategy and workload,
`DOTNET_TieredCompilation=0`, three warmup iterations, and five measured
iterations. Reports identify .NET 10.0.10 on macOS 26.7.1, Arm64, 11 logical
processors, and workstation garbage collection. The source is the local,
uncommitted TASK-071 implementation. Each row below compares identical
parameters and agrees on digest and semantic counts across all three strategies.
The [manifest](../benchmarks/results/2026-10-09-runtime-instant-manifest.json)
records all 15 exact commands and their raw report filenames.
The `scopedEntries` count is query count multiplied by scope size, not the
number of exact checks performed by an index.

## Results

Values are median whole-iteration milliseconds and median allocated decimal MB.

| Workload | Scan | Grid | Hierarchy |
| --- | --- | --- | --- |
| sparse-q1 | 3.33 ms / 4.58 MB | 4.14 ms / 6.15 MB | 6.05 ms / 9.43 MB |
| sparse-q100 | 8.85 ms / 4.58 MB | 4.61 ms / 6.22 MB | 5.38 ms / 9.49 MB |
| sparse-q1000 | 50.11 ms / 4.58 MB | 4.46 ms / 6.83 MB | 5.90 ms / 10.01 MB |
| dense-q100 | 15.89 ms / 21.24 MB | 29.05 ms / 44.12 MB | 32.71 ms / 48.52 MB |
| clustered-scoped-q100 | 4.80 ms / 4.63 MB | 3.94 ms / 5.45 MB | 4.76 ms / 7.46 MB |

Sparse rows change only query count and return zero matches. The dense row
uses coincident starting positions and range 10,000, returning 249,900 matches.
The clustered row uses four clusters per system, spacing one, two systems,
and scope stride two. Sources remain selected from the full view, including
outside-scope sources; it returns 6,202 matches.

Scan has the lowest cost for one sparse query. Grid construction amortizes in
the tested 100- and 1,000-query sparse cases. Scan wins the dense case, where
indexes cannot avoid exact matches and add candidate reduction and traversal.
Grid has a smaller advantage in the scoped clustered case. Both indexes
allocate more than scan in every tested row. These observations support explicit
caller selection with scan retained as the default; they do not establish a
portable crossover threshold or a universally preferred index.

## Correctness and reproduction

All three unmodified canonical presets pass the existing expected digest
`b75076796115dc88`; the
[canonical report](../benchmarks/results/2026-10-09-runtime-instant-canonical.json)
uses the normal runtime compilation settings and is a correctness check,
not a controlled timing comparison. Reduced tests compare public-runtime
presets against the reference fixture for sparse, dense, clustered, scoped,
and multi-system queries, and reject invalid tuning before execution.
The solution passes 1,259 tests; the Release solution build has zero warnings
or errors. Timings remain informational and have no test thresholds.

```sh
dotnet build benchmarks/GalaxyCommand.Benchmarks -c Release
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.runtime-grid \
  --set warmupIterations=3 --set measuredIterations=5 --set queryCount=100
```

Run each strategy in its own process. Use the manifest commands to reproduce
the other workloads. Existing prototype and swept presets remain available;
this slice adds no prepared swept runtime API or concurrent execution.
