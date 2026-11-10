# Station runtime performance and determinism

The station workload exercises the live session rather than arithmetic helpers.
It creates explicit sites, assigns builders, delivers material in partial batches,
constructs the base and a module, follows an authored upgrade path, switches the
module off and on, and dismantles the module and base into reserved recovery
inventories. Quiet sites remain live. Moving observers exercise the existing
interaction forecast workers while station ownership and commits remain serial.

The three opt-in presets isolate different shapes:

| Preset | Sites | Active sites | Systems | Base work / material | Delivery batches |
| --- | ---: | ---: | ---: | ---: | ---: |
| `station.many-quiet-sites` | 256 | 8 | 16 | 24 / 12 | 3 |
| `station.one-crowded` | 64 | 64 | 1 | 24 / 12 | 3 |
| `station.partial-deliveries` | 32 | 32 | 4 | 48 / 24 | 6 |

Each preset captures partial work and an active recovery reservation, restores at
an explicit interior time, and continues through final removal. Workload
parameters include builder count and throughput, work and material quantities,
delivery count, update partitions, restore time, and host worker/batch layout.
Small correctness cases compare serial execution with one, two, four and eight
host workers, irregular update partitions, and restoration before and after
removal. These are host-tuning invariance checks, not station concurrency claims.

Setting `destructionLifecycle=1` replaces normal dismantling with a second
explicit fixture. Each active station builds two storage modules, receives a
full physical stock, loses the upgraded sensor and one storage module in one
grouped occurrence, then loses its base and remaining storage in another.
Recovered cargo fields are collected through real ship orders. Intermediate
checkpoints include loss receipts, exact storage windows, random authority,
cargo identities and field membership. Its cargo and storage-loss policies are
authored fixture inputs; they establish no game-content defaults.

## Proof boundary

The full digest traverses the typed in-memory checkpoint graph, including
station recipes and definitions, exact fractional work, assignments, private
receipts, allocators, incoming/outgoing commitment ages, lifecycle inventories,
economic provider archives, random streams, observation evidence, agenda and
full event records. Dictionary keys use canonical value ordering; type tags and
explicit scalar encodings prevent dependence on object identities or culture.
Unsupported executable owners, delegates and cycles fail the proof. Pure
movement adapters are represented by their saved geometry, phase boundaries
and kinematic projections. A stationary discovery key excludes its ship-only
compatibility accessor, which is undefined for that key category.

Mutation checks establish sensitivity to work fractions, module state, recovery
claims, recipe work, command receipts, event sequences and storage-loss windows.
The digest is benchmark evidence, not a save encoder or a validation substitute.
The encoded typed checkpoint boundary remains TASK-116.

A separate gameplay digest supports comparison across wake-envelope formats.
It omits the engine envelope graph, station pending/generation fields, event
creation sequence numbers, and absent or empty optional members. It retains
exact progress, revisions, inventories, claims, causal times and phases, facts,
receipts and lifecycle state. Exact worker, partition and restore comparisons
continue to use the full digest. A matching gameplay digest alone does not
prove agenda equality.

## Wake optimization evidence

A work envelope is now required when cumulative input consumption changes, at
completion, or at the last affordable free work before starvation. Between
those boundaries, accrual exposes earned whole work and its exact fractional
remainder without consuming another input or activating a capability. Revision
increments follow earned whole work, not the number of external updates.

Same-time station work uses contiguous physical waves evaluated against stable
inventory budgets and committed in global project admission order. Foreign
physical envelopes remain exact barriers, retaining earlier economic
reservations and cancellation behavior. Every station effect keeps its original
scheduled cause. This preserves FIFO material contention without reserving a
project's entire future recipe.

The preserved pre-optimization runtime and optimized runtime produced equal
gameplay digests at all fixed witnesses and final checkpoints for each preset:
`703de34e76334545`, `194f6095e52ca5f9`, and `e420d28ddae79e4e`, respectively.
The current full version-2 digests also include the reciprocal target
continuation owner section, including its explicit absence in these presets:

| Preset | Full digest |
| --- | --- |
| Many quiet sites | `285568c294f5e9db` |
| One crowded system | `b56940d4752279bc` |
| Partial deliveries | `8d0fba9b6bf6c88b` |

Their full event digests intentionally differ because fewer envelopes allocate
fewer creation sequences. The station preset baseline therefore uses version 2.

The following warmed Release measurements used .NET 10.0.10, macOS 26.7.1,
Arm64, 11 logical processors, workstation GC, one warmup and three measured
iterations. Both executions used the same updated benchmark proof visitor and
fixture, with the old simulation binary preserved before the timing change.
Times and allocations are informational and include checkpoint hashing and
observation work.

| Preset | Work events before / after | Whole workload ms before / after | Advance and lifecycle ms before / after | Allocated MB before / after |
| --- | ---: | ---: | ---: | ---: |
| Many quiet sites | 256 / 168 | 663.0 / 626.9 | 306.2 / 274.9 | 1207.2 / 1194.6 |
| One crowded system | 2048 / 1344 | 616.4 / 519.8 | 414.6 / 372.9 | 1020.3 / 1070.4 |
| Partial deliveries | 1792 / 1152 | 229.4 / 189.4 | 135.4 / 115.8 | 289.9 / 295.9 |

The change reduces work-event volume by roughly one third in these material-
dense recipes. Allocation does not improve consistently: wave preparation and
proof overhead remain visible, and observation dominates the many-site shape.
Sparse-input timing tests separately establish exact intermediate progress and
starvation behavior without requiring one event per whole work unit. Further
batching or concurrent station evaluation requires new measured evidence and
the same deterministic commit contract.

Reproduce a full preset with:

```sh
dotnet run -c Release --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset station.partial-deliveries
```

Exercise the real loss fixture and a different host layout with:

```sh
dotnet run -c Release --project benchmarks/GalaxyCommand.Benchmarks -- \
  --suite full --preset station.one-crowded \
  --set destructionLifecycle=1 --set workerCount=4 --set batchSize=7 \
  --set advancePartitions=37 --set restoreAtMilliseconds=18001
```

Parameter overrides identify noncanonical runs. The report separates total
measurements from setup, advancement/lifecycle, restore/proof and digest work;
restore/proof is a nested diagnostic and must not be added to the other totals.
