# Advancing-session interaction measurements

[Session execution contract](moving-ship-interactions.md#fixed-session-forecast-execution-settings) · [Pure swept batch measurements](interaction-batch-measurements.md) · [Task list](task-list.md)

## TASK-095 fixed-position extension

On 2026-10-26, `interaction.stationary-session-serial` and
`interaction.stationary-session-batched` added the same small advancing-session
workload with fixed commanded positions instead of target ships. They exercise
the shared stationary-interest path, including updates, cancellations and stale
forecasts. They do not measure deployable inventory transactions or TASK-073
coverage consumption. The existing ship-pair presets and expected digest remain
unchanged; the new variants have no pinned expected-digest baseline yet.

The fixed-position variants produce digest `4fbb8a2187d6d18b`: 256 registered
interests, 211 updates, 69 cancellations, 361 delivered effects, 128 stale
interaction events and 497 total events. Focused tests compare serial execution
with one and four workers and different batch sizes. Separate sensor-order tests
compare actual deployment facts, entities, events, sources and restored
continuation across forecast execution settings.

The following local characterization used .NET 10.0.10, macOS 26.7.1, Arm64,
11 logical processors and workstation GC. Each configuration ran in a separate
process with tiered compilation disabled, three warmups and five measured
iterations, without overlapping builds or tests. Source was the uncommitted
TASK-095 worktree. Times cover the full synthetic session, including setup and
digest creation; allocations are total managed bytes, rather than retained
memory. These measurements do not select a default worker count.

| Execution | Median ms | Minimum / maximum ms | Allocated bytes per iteration |
| --- | --- | --- | --- |
| Serial constructor | 16.6337 | 16.3128 / 17.3737 | 25,520,560 |
| 1 worker, batch 64 | 15.9750 | 15.2405 / 16.4059 | 25,548,944 |
| 4 workers, batch 16 | 7.4495 | 7.2566 / 8.9051 | 25,572,528 |

Reproduce each configuration after a Release build, substituting the preset and
worker overrides shown above:

```sh
DOTNET_TieredCompilation=0 dotnet run --project benchmarks/GalaxyCommand.Benchmarks \
  --configuration Release --no-build -- --suite full \
  --preset interaction.stationary-session-serial \
  --set warmupIterations=3 --set measuredIterations=5
```

For the batched preset, add `--set workerCount=1 --set batchSize=64` or
`--set workerCount=4 --set batchSize=16`. All three runs must match digest and
semantic counts. Timing and allocation remain informational.

## Original ship-pair presets

On 2026-10-13, TASK-071 added version 1 `interaction.session-serial` and
`interaction.session-batched` benchmark presets. They exercise real session
advancement with analytic motion replacement, durable-interest updates and
cancellations, stale-event rejection, and serial domain effects. The serial
preset uses the original two-argument session constructor. The batched preset
supplies explicit worker capacity and batch size at creation. Both remain heavy
workloads requiring `--suite full`.

## Workload and correctness

Every active ship begins at x=0 and alternates goals at x=100 and x=-100 in its
own system. Stationary targets alternate between x=3 and x=10. Each interest
asks a directed range question, initially range 2, with its own purpose and
session-wide identity. Several purposes intentionally share a ship pair.
The nearer targets deliver contacts before replacement; farther targets leave
pending contacts that become stale when motion is replaced. Four command waves
occur at 0, 1000, 2000, and 3000 ms, followed by advancement to 6000 ms.
Before each replacement wave after the first, interests whose ID plus wave
number is divisible by 11 are canceled; remaining interests divisible by 3 are
updated to range 2 or 3. These operations execute at idle session boundaries.
The synthetic domain records delivered contact evidence through serial effects;
it adds no sensor, combat, docking, or other gameplay outcome.

| Fixture | Ships / active / systems | Interests | Commands | Delivered effects | Stale interaction events | Updates / cancellations | Digest |
| --- | --- | --- | --- | --- | --- | --- | --- |
| small | 32 / 8 / 2 | 256 | 32 | 361 | 128 | 211 / 69 | `1079ca5a77a8341f` |
| medium | 256 / 64 / 4 | 2048 | 256 | 2886 | 1024 | 1676 / 558 | `995aa31fbfc67979` |

The small fixture is the canonical preset, with the same expected digest in
both modes. The digest includes the existing snapshot, command, event, and fact
summary, semantic workload parameters, ordered delivered contact evidence
(system, interest identity/generation, directed roles, range, timestamp, and
contact flags), and remaining interest identities/generations/ranges. Worker
settings and harness iteration counts do not participate. All measured runs
within each fixture agree on digest and semantic counts. Twelve focused tests
compare worker capacities 1, 2, 4, and 8 with different batch sizes, enforce the
canonical digest, reject invalid workloads, and show that changing the number
of replacement waves changes churn evidence and the digest. Existing session
execution tests separately compare checkpoint owners and restore continuation.

## Timed boundary and results

Each timed iteration constructs a fresh session, registers interests and the
recording evaluator, submits commands, advances through all replacement waves,
updates and cancels interests, processes movement and contact events, commits
serial effects, and computes the digest and counts. Session disposal is also
inside the iteration. Times include setup and serial work; no separate capture,
forecast, scheduling, or domain phase timings are available. Simulation
throughput below is 6000 simulated milliseconds divided by whole-iteration
elapsed milliseconds. It describes this bounded synthetic session, including
quiet time after the final wave, rather than a sustained game throughput target.

The controlled matrix uses .NET 10.0.10, macOS 26.7.1, Arm64, 11 logical
processors, and workstation garbage collection. Each configuration runs in a
separate process with `DOTNET_TieredCompilation=0`, three warmups, and five
measured iterations, without overlapping builds or tests. Source is the local
uncommitted TASK-071 implementation. Allocation is total managed bytes per
iteration, not retained or peak memory. Worker capacity is a maximum allowed
by the shared pool, not a count of threads proven active for every wave.

| Fixture | Execution | Median ms | Allocated decimal MB | Simulated seconds per wall second |
| --- | --- | --- | --- | --- |
| small | serial | 15.88 | 22.42 | 377.8 |
| small | 1 worker, batch 64 | 15.74 | 22.45 | 381.1 |
| small | 2 workers, batch 64 | 10.74 | 22.47 | 558.4 |
| small | 4 workers, batch 64 | 8.25 | 22.47 | 727.3 |
| small | 8 workers, batch 64 | 7.58 | 22.47 | 791.6 |
| medium | serial | 131.06 | 180.97 | 45.8 |
| medium | 1 worker, batch 64 | 133.57 | 181.29 | 44.9 |
| medium | 2 workers, batch 64 | 83.30 | 181.43 | 72.0 |
| medium | 4 workers, batch 64 | 64.92 | 181.43 | 92.4 |
| medium | 8 workers, batch 64 | 56.79 | 181.43 | 105.6 |
| medium | 4 workers, batch 16 | 61.16 | 181.45 | 98.1 |
| medium | 4 workers, batch 256 | 69.05 | 181.39 | 86.9 |

At batch size 64, eight-worker execution is about 2.1 times faster for the small
fixture and 2.3 times faster for the medium fixture than the original serial
path. A one-worker batch is close to the original serial path. Medium allocation
remains about 181 MB in every mode; worker execution does not resolve the
allocation investigation parked as TASK-100. Medium four-worker runs vary from
61.16 ms with batch 16 to 69.05 ms with batch 256. These bounded results support
explicit opt-in settings and do not select a universal worker count or batch
size. Domain callbacks and authoritative commits remain serial.

All 1,334 solution tests pass. The Release solution build passes with zero
warnings and errors using a writable temporary NuGet HTTP cache; the initial
build was blocked by the default audit cache's filesystem permissions. Raw
[controlled reports and exact commands](../benchmarks/results/2026-10-13-session-manifest.json)
and the [canonical report](../benchmarks/results/2026-10-13-session-canonical.json)
are retained. The matrix proves agreement for these two workloads, not a
production-scale target, phase-specific bottleneck attribution, consuming-domain
activation, or parallel domain evaluation.

## Reproduction

```sh
NUGET_HTTP_CACHE_PATH=/tmp/task071-nuget-http-cache dotnet build GalaxyCommand.slnx -c Release
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset interaction.session-batched \
  --set shipCount=256 --set activeShipCount=64 --set systemCount=4 \
  --set interestCount=2048 --set workerCount=8 --set batchSize=64 \
  --set warmupIterations=3 --set measuredIterations=5
```
