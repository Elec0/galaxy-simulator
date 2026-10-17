# Spatial interaction proximity measurements

[Moving-ship interactions](moving-ship-interactions.md) · [Scale targets](scale-and-benchmark-targets.md) · [Task list](task-list.md)

Measured on 2026-10-08 for `TASK-071`. These results compare single-thread
caller-scoped instant queries before
spatial-index selection. Both paths retain continuous exact geometry.

## Workload and timed scope

Each iteration constructs a spatial movement fixture, starts linear motion for
half the ships, captures one view at 500 ms, runs 100 queries, and digests the
ordered results. Each caller explicitly supplies the complete ship scope.
All ships occupy one system. Linear legs move 20 m over 1,000 ms; the inclusive
query range is 10 m. Source identities follow the configured seed of 1.

Sparse ships start 100 m apart. Dense ships start at one position, so every
other ship matches the queried source at this timestamp. These are controlled
fixtures, not proposed gameplay activity proportions or automatic all-pairs
interest registration.

Timing includes setup, movement commits, view capture, scope sorting, bounds,
exact range checks, result construction, and digesting. It is not query-only
timing or an advancing whole-session throughput result. Each run uses one
warmup iteration and five measured iterations; digests and counts must agree.
Allocated bytes are cumulative allocation per iteration, not resident or peak
memory. Timings remain informational and never gate correctness.

## Initial reference measurements

Machine: .NET 10.0.10, macOS 26.7.1,
Arm64, 11 reported logical processors;
server GC: false. Raw reports retain machine metadata,
parameters, digest, counts, timing range, allocations, and collection totals.

| Ships | Layout | Scoped entries | Matches | Median ms | Min to max ms | Median allocated MB | Raw report |
| ---: | --- | ---: | ---: | ---: | --- | ---: | --- |
| 250 | Sparse | 25,000 | 0 | 48.42 | 46.21 to 56.66 | 20.17 | [JSON](../benchmarks/results/2026-10-08-proximity-250-spacing-100.json) |
| 250 | Dense | 25,000 | 24,900 | 123.42 | 51.47 to 130.11 | 115.21 | [JSON](../benchmarks/results/2026-10-08-proximity-250-spacing-0.json) |
| 2,500 | Sparse | 250,000 | 0 | 134.57 | 132.58 to 144.15 | 213.65 | [JSON](../benchmarks/results/2026-10-08-proximity-2500-spacing-100.json) |
| 2,500 | Dense | 250,000 | 249,900 | 455.36 | 438.78 to 533.18 | 1156.69 | [JSON](../benchmarks/results/2026-10-08-proximity-2500-spacing-0.json) |
| 10,000 | Sparse | 1,000,000 | 0 | 520.70 | 518.91 to 566.78 | 860.30 | [JSON](../benchmarks/results/2026-10-08-proximity-10000-spacing-100.json) |
| 10,000 | Dense | 1,000,000 | 999,900 | 1760.94 | 1749.05 to 1815.01 | 4625.45 | [JSON](../benchmarks/results/2026-10-08-proximity-10000-spacing-0.json) |

The 2,500-ship sparse default is the canonical version 1 preset, with digest
`c09a37599b5eb648`. Other sizes and dense layouts are explicitly overridden
non-canonical runs. The small dense run has a wide timing range; this limited
warmup does not establish a precise steady-state cost.

## Prepared geometry comparison

On 2026-10-08 the owner selected immutable geometry preparation once per view
and caller-scope preparation once for repeated queries. The sibling
`interaction.prepared-proximity` preset uses the same fixtures, parameters,
version 1 canonical digest, and result digest inputs as the reference preset.
Each timed iteration includes preparation, setup, queries, and digesting.
Both presets ran sequentially in the same process for each layout, with one
warmup and five measured iterations. All six comparisons agree on digests and
semantic counts, including all ordered target identities.

| Ships | Layout | Reference median ms | Prepared median ms | Prepared min to max ms | Reference allocated MB | Prepared allocated MB | Raw comparison |
| ---: | --- | ---: | ---: | --- | ---: | ---: | --- |
| 250 | Sparse | 54.58 | 4.14 | 3.74 to 4.88 | 20.17 | 0.50 | [JSON](../benchmarks/results/2026-10-08-prepared-comparison-250-spacing-100.json) |
| 250 | Dense | 144.11 | 5.37 | 4.93 to 5.76 | 115.21 | 1.09 | [JSON](../benchmarks/results/2026-10-08-prepared-comparison-250-spacing-0.json) |
| 2,500 | Sparse | 268.30 | 26.26 | 20.71 to 34.11 | 213.20 | 4.56 | [JSON](../benchmarks/results/2026-10-08-prepared-comparison-2500-spacing-100.json) |
| 2,500 | Dense | 466.69 | 20.45 | 19.45 to 25.29 | 1156.69 | 13.23 | [JSON](../benchmarks/results/2026-10-08-prepared-comparison-2500-spacing-0.json) |
| 10,000 | Sparse | 595.66 | 20.77 | 16.32 to 86.43 | 860.30 | 18.41 | [JSON](../benchmarks/results/2026-10-08-prepared-comparison-10000-spacing-100.json) |
| 10,000 | Dense | 1881.26 | 52.38 | 37.88 to 85.64 | 4625.45 | 52.67 | [JSON](../benchmarks/results/2026-10-08-prepared-comparison-10000-spacing-0.json) |

At 2,500 ships, allocation falls by about 98% for sparse scopes and 99% for
dense scopes. These are cumulative whole-iteration allocations. Prepared exact
points remove repeated curve construction and polynomial range evaluation;
prepared ordered scopes remove repeated sorting. Exact axis rejection avoids
squaring discarded distant pairs. Dense result construction remains part of
the measured cost.

Reference timings vary from the earlier baseline, and prepared runs also show
outliers. The fixed preset order and limited warmup do not establish precise
steady-state speedups. These results support retaining preparation; they do
not establish spatial-index performance, worker scaling, or the break-even
query count for preparation. Each query still scans its prepared target scope.
Index type remains an owner decision. Analytic forecasting, invalidation churn,
index coverage, and whole-session throughput still need separate workloads.

## Reproduction

Run the sparse reference preset in Release:

```sh
dotnet run -c Release --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.scoped-proximity
```

Use the dense scenario file, or override ship and active-ship counts together:

```sh
dotnet run -c Release --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --scenario-file benchmarks/scenarios/example.interaction-dense.json \
  --set shipCount=10000 --set activeShipCount=5000
```

Compare both paths with identical overrides:

```sh
dotnet run -c Release --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.scoped-proximity \
  --preset interaction.prepared-proximity \
  --set shipCount=2500 --set activeShipCount=1250 --set participantSpacing=0
```
