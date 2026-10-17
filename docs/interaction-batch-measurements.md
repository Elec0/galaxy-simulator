# Interaction batch execution measurements

[Interaction execution contract](moving-ship-interactions.md#opt-in-pure-batch-execution) · [Public swept baseline](interaction-runtime-swept-measurements.md) · [Task list](task-list.md)

On 2026-10-12, TASK-071 added opt-in coarse batches for pure forecasts and
prepared instant/swept queries. Callers explicitly supply positive maximum
worker capacity and batch size. The backend uses bounded .NET parallel execution
on the shared thread pool. One worker uses a direct serial loop. Domain callbacks,
interest/movement ownership, scheduling, and effects remain serial.

## Correctness boundary

The batch APIs collect private outputs, join, and only then expose ordered or
normalized results. Forecasts reduce by canonical candidate key and reject
conflicting correlations. Query batches preserve caller source order and
source duplicates; each result retains its ordinary exact query contract.
A failed pure evaluation returns no partial collection and rethrows the
exception at the earliest caller-input position with its original stack.
Input enumeration and common argument validation occur before worker dispatch.

Fourteen focused simulation tests compare exact forecast evidence and committed
agenda keys at maximum worker capacities 1, 2, 4, and 8, with batch sizes 1, 3,
7, and 16. They compare all prepared index strategies, retain immutable results,
reject invalid tuning and conflicts, and cover empty inputs. A synchronized
barrier test proves overlapping batch execution; another forces a later-input
failure to occur first and still observes the earlier-input exception. The
shared pool controls utilization, so worker capacity is not a guarantee that
exactly that many threads execute each short workload.

Three version 1 `interaction.batch-swept-scan`, `interaction.batch-swept-grid`,
and `interaction.batch-swept-hierarchy` presets exercise the public swept batch
API. Reduced tests compare their digests to ordinary public queries. Canonical
presets retain `418f5c9774f5bbdc`, recorded in the
[canonical report](../benchmarks/results/2026-10-12-batch-swept-canonical.json).
The solution passes 1,295 tests; the Release solution build has zero warnings
or errors. Those tests prove pure API results and a subsequent serial agenda
commit, not concurrent live session reevaluation or domain effects.

## Timed scope and controlled results

Each measured iteration constructs the real movement fixture, captures one
stable view, prepares geometry/scope/index, copies sources, evaluates the full
batch, and digests ordered contacts after join. It includes setup and serial
reduction costs, not separate phase timings or advancing-session throughput.
Allocation is total managed bytes allocated per iteration, not retained or peak
memory. The controlled matrix uses .NET 10.0.10, macOS 26.7.1, Arm64, 11 logical
processors, and workstation garbage collection. Each run is a separate process
with `DOTNET_TieredCompilation=0`, three warmups, and five measured iterations.
The source is the local uncommitted TASK-071 implementation.

The [manifest](../benchmarks/results/2026-10-12-batch-swept-manifest.json)
records all 18 exact commands and report filenames. Twelve sparse mixed-motion
runs use 2,500 ships, 1,250 active, 1,000 queries, distance 20, range 10, and batch
size 64. All retain digest `5c677d303c9e9c4f` and zero contacts. Median whole
iteration time in milliseconds:

| Strategy | 1 worker | 2 workers | 4 workers | 8 workers |
| --- | --- | --- | --- | --- |
| scan | 21.25 | 15.30 | 12.87 | 10.84 |
| grid | 9.83 | 9.46 | 9.35 | 9.33 |
| hierarchy | 10.78 | 10.99 | 11.08 | 11.07 |

Scan benefits from additional worker capacity in this tested repeated-query
workload. Grid's total cost changes little and hierarchy does not benefit.
Fixture construction and index preparation remain serial. Sparse allocations
are about 8.17 MB for scan, 11.73 MB for grid, and 13.15 MB for hierarchy at one
worker, with modest batch-buffer overhead in parallel paths.

Six dense mixed-motion runs use the same ship counts, coincident origins,
100 queries, range 10,000, and batch size 16. Each retains digest
`8cd425103d41300c` and 249,900 contacts. Values below are median whole-iteration
time / median allocated decimal MB:

| Strategy | 1 worker | 4 workers |
| --- | --- | --- |
| scan | 528.14 ms / 1017.65 MB | 265.44 ms / 1017.66 MB |
| grid | 555.56 ms / 1040.22 MB | 269.36 ms / 1040.23 MB |
| hierarchy | 564.91 ms / 1044.62 MB | 278.72 ms / 1044.62 MB |

The dense fixture benefits from worker capacity, but allocates approximately
one GB per iteration in every strategy. The existing exact maneuver solver
constructs trajectory and boundary evaluation data for repeated pairs. This
measurement identifies an allocation investigation, parked as TASK-100; it does
not prove that all allocation comes from one component or that parallel work
reduces retained memory. Index changes alone do not remove the dense output or
exact evaluation cost.

These results justify an opt-in pure batch path, not an automatic worker count,
batch size, index choice, or live-session scheduling policy. Forecast and instant
batch correctness share the tested executor, but their scaling is not measured
by these swept presets. Domain callback parallelism and fine-grained activation
remain outside this slice. Subsequent
[coordinator integration](moving-ship-interactions.md#opt-in-coordinator-reevaluation)
adds per-wave opt-in reevaluation and retry proofs without extending these
measurements to coordinator overhead or advancing-session throughput.
[Fixed session settings](moving-ship-interactions.md#fixed-session-forecast-execution-settings)
subsequently wire this path into session creation and restore while keeping
tuning outside checkpoints. The subsequent
[advancing-session measurements](interaction-session-measurements.md) measure
whole-session worker scaling with replanning and durable-interest churn.

## Reproduction

```sh
dotnet build benchmarks/GalaxyCommand.Benchmarks -c Release
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.batch-swept-scan \
  --set shipCount=2500 --set activeShipCount=1250 \
  --set destinationDistance=20 --set motionKind=1 --set queryCount=1000 \
  --set workerCount=4 --set batchSize=64 \
  --set warmupIterations=3 --set measuredIterations=5
```

The preset's one-worker/64-item settings are benchmark inputs, not application
defaults. Run each worker capacity in its own process, using the manifest to
reproduce the dense and indexed workloads. Timings are informational; digest
and semantic disagreement is the enforced failure.
