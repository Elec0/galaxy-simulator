# Phase 1 simulation specification

[Project index](../README.md) · [Roadmap](roadmap.md) · [Simulation architecture](simulation-architecture.md) · [Navigation and spatial architecture](navigation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md)

## Purpose

Phase 1 proves that a deterministic, headless physical economy can acquire resources, transport them through a production chain, construct a ship, and expose the causes of shortages.

This specification intentionally excludes systems that are not required to test that loop.

## Scenario

The initial scenario contains:

- Three locations connected by fixed routes
- One raw resource
- A four-stage chain: raw resource, refined material, components, and a completed ship
- One resource producer
- One refining facility
- One component facility
- One ship-construction facility
- Several cargo ships with finite capacity

The names and quantities used by the fixture are test data rather than final game content.

The initial fixture uses Mine, Refinery, and Shipyard locations connected by bidirectional 60-second routes. It models Ore, Alloy, and Components with these configured batches:

- Mine: no inputs, 10 Ore, 30 seconds
- Refinery: 10 Ore to 5 Alloy, 60 seconds
- Component factory at Shipyard: 5 Alloy to 2 Components, 60 seconds
- Shipyard: 4 Components to one capacity-10 freighter, 120 seconds

Facility inventories hold 100 units. Two capacity-10 freighters begin at Mine and Refinery. Loading and unloading use a five-second docking overhead and ten material units per second. One organization owns all initial facilities and ships.

## Included behavior

- Inventory storage and reservation
- Facility input demand and output supply
- A central transport job board
- Deterministic freighter job selection
- Route-based travel and scheduled arrival
- Cargo loading and unloading
- Production queues and scheduled completion
- Ship construction from transported inputs
- Structured decision reasons and simulation metrics

## Excluded behavior

- Currency and market prices
- Combat and ship damage
- Diplomacy and reputation
- Population and crew simulation
- Technology and equipment differences
- Detailed faction strategy
- Continuous spatial movement
- Rendering and user interface

## Navigation boundary

Phase 1 uses a directed multigraph whose nodes are locations and whose edges are routes. Multiple routes may connect the same locations. Bidirectional connections consist of two directed routes, and each route has a stable ID, base travel duration, and enabled state.

Ship and logistics behavior must request routes and travel estimates through an abstract navigation interface. Those systems should not inspect or modify the graph representation directly. This preserves the option to introduce continuous space, gates, dynamic hazards, or a different route model later.

This interface and its `LocationId` and `RouteId` results are part of the Phase
1 acceptance contract, not the target gameplay-order contract. General actor
orders describe destination intent and use the hierarchical system-space model
defined in [Navigation and spatial architecture](navigation-architecture.md).
The graph remains in place during migration so changes to interactive movement
do not accidentally invalidate the economic proof.

Pathfinding selects the enabled path with the lowest total base duration and breaks equal-duration ties deterministically by route IDs. All Phase 1 ships use base route durations; ship-specific speed and access restrictions are deferred behind the navigation boundary.

While travelling, a ship retains its departure, destination, route, departure time, and expected arrival time. The simulation schedules arrival rather than continuously updating position.

Travel is scheduled one directed route leg at a time. Disabling a route does not interrupt a ship already traversing that leg. On arrival at the next location, the ship recalculates its remaining path and waits if no enabled path exists.

## Central job board

Facilities publish finite supply offers and demand requests to a central job board. A concrete transport job is created only when an idle freighter accepts a compatible match. Job creation, source-inventory reservation, and cargo-capacity commitment occur atomically.

An eligible freighter selects a job using a deterministic score based initially on:

- Whether its cargo type and capacity are compatible
- Travel required to reach the source and destination
- Job priority or age
- Quantity it can carry

Phase 1 freighters accept one active job at a time. Candidates are ordered by higher demand priority, older demand, shorter total journey from ship to source to destination, larger deliverable quantity, then stable demand and supply IDs. Unreachable matches are excluded.

### Reservations

Accepting a job reserves both source inventory and freighter cargo capacity. Reserved material remains at the source until loading completes but cannot be claimed by another job.

Each inventory has one shared integer capacity. Every Phase 1 material unit consumes one capacity unit, regardless of material type. Reservations have stable typed IDs and identify their inventory, material, quantity, and owning transport or production job.

A job may be partially fulfilled when a ship cannot carry its full requested quantity. The unfulfilled quantity remains available for another job.

Cancellation or failure releases any reservation that has not already become physical cargo. Once loaded, material belongs to the ship's cargo inventory and must follow an explicit recovery or delivery path.

Loading and unloading have independently configurable integer throughput in material units per simulated second plus a fixed docking overhead at each stop. Operation duration is `docking overhead + ceil(quantity × 1000 / throughput)` milliseconds.

Destination capacity is not reserved during job assignment. If sufficient capacity is unavailable when a freighter arrives, it waits without beginning unloading. When capacity becomes available, the job reserves exactly the required destination capacity and schedules the full unloading operation. Cargo remains physically aboard the ship until unloading completes, at which point cargo and reserved capacity are consumed atomically.

If loading fails before material enters ship cargo, the source reservation is released, committed supply and demand are restored, and the freighter becomes idle.

## Production rules

For Phase 1:

- A facility has one active production job per production capability.
- A facility maintains one active job plus a FIFO queue.
- A job reserves inputs incrementally as they arrive and publishes demand only for its unmet amounts.
- A job waits until all required inputs are reserved.
- Complete recipe inputs are consumed when production begins.
- Production uses integer work units and non-zero throughput measured in work units per simulated second. Duration in milliseconds is `ceil(required work × 1000 / throughput)`.
- One complete output batch is created at the scheduled completion time.
- Completion enters a completed-awaiting-storage state if the destination inventory lacks output capacity. A state update caused by newly available capacity retries storage without polling.
- Interrupted production behavior is deferred until interruption exists in the simulation.

Refining and component recipes may repeat automatically while enabled. Ship construction consists of explicit finite orders.

Construction follows the same material and work principles. A completed ship becomes a new persistent entity rather than a counter or abstract reward.

Ship construction uses a finite FIFO construction process rather than a
material-output production line. A Phase 1 `ShipDesign` inherits the shared
construction definition, supplies its material-and-work recipe, and defines
cargo capacity. The shipyard composes the product-neutral construction process
and is responsible only for creating the completed ship. Each shipyard and
constructed ship belongs to a typed organization, although organizations have
no autonomous faction behavior during Phase 1.

Completing construction allocates a persistent ship and cargo inventory at the shipyard location, then registers the ship as an idle freighter available to the transport board.

## Time and event ordering

Simulation time uses integer milliseconds on an authoritative timeline beginning at zero.

Scheduled events are ordered by:

1. Simulation timestamp
2. Event phase: physical completion, state/index update, then decisions
3. Monotonically increasing creation sequence

An event may schedule another event at its current timestamp in the same or a later phase, but it cannot schedule an earlier phase at that timestamp or move simulation time backward. Events must verify their caller-managed generation token and referenced state before applying changes. Invalidated events produce a defined no-op or failure transition rather than mutating stale state.

The Phase 1 acceptance runtime runs on one thread and remains a deterministic
reference path. Concurrent execution is introduced only after profiling, but
new simulation boundaries must preserve the ownership, batching, buffered
effect, and deterministic-commit contract defined in
[Concurrency and performance architecture](concurrency-and-performance.md).

## Numeric representation

Integers are preferred for:

- Simulation time
- Inventory and cargo quantities
- Storage and cargo capacity
- Production work and throughput
- Priority and ordering values

Explicit fixed-point values may be introduced where integer units are not appropriately expressive. Floating-point values are not used for conserved economic quantities.

Entity identifiers use deterministic sequential non-zero `u64` values with separate types and persisted counters for each entity domain.

## Required invariants

- Inventory and cargo quantities never become negative.
- Cargo never exceeds ship capacity.
- Stored inventory never exceeds storage capacity.
- Reserved inventory never exceeds physically available inventory.
- Reserved cargo capacity never exceeds available cargo capacity.
- A material unit exists in exactly one physical location at a time.
- Production cannot begin without consuming its complete required inputs.
- A ship cannot perform mutually exclusive tasks simultaneously.
- Every active transport job has valid participants or transitions to a defined failure state.
- Simulation time never moves backward.

Invariant violations should fail tests immediately and include enough state to identify the responsible transition.

## Observability

Meaningful autonomous actions produce a structured record containing:

- Simulation timestamp
- Acting entity
- Chosen action
- Primary reason
- Related job, demand, production request, or objective

The simulation does not need to retain every rejected alternative. It must retain enough information to explain why cargo moved, why production waited, and why construction succeeded or stalled.

The headless runner reports at least:

- Material produced and consumed by stage
- Transport jobs created, completed, partially fulfilled, cancelled, and failed
- Cargo delivered by material and destination
- Facility active, waiting, and output-blocked time
- End-to-end time from raw resource to completed ship
- Current shortages and their immediate causes

## Determinism

Phase 1 uses an explicit random seed even if the first fixture requires little randomness. Stable entity and event ordering must not depend on hash-map iteration order.

Repeated runs with identical configuration, seed, initial state, and commands should produce the same final-state and event-log digests. Phase 1 uses FNV-1a 64-bit fingerprints with explicit little-endian field encoding. The event-log fingerprint covers canonical processed-event ordering and structured decision records; the final-state fingerprint covers the authoritative time, route availability, inventories, ships, and transport jobs. These fingerprints are regression checks, not security hashes.

Construction-completion event records include the construction facility and
order IDs. Ship state fingerprints include construction design IDs, and
inventory fingerprints include configured capacity, so different constructed
designs cannot collapse to the same state solely because their current cargo
and locations happen to match.

## Acceptance criteria

Phase 1 is complete when:

- The scenario runs for a configured duration without invariant violations.
- Materials pass through every production stage.
- At least one ship is constructed solely from acquired and transported inputs.
- Identical runs produce identical final-state and event-log digests.
- The runner emits the required metrics and decision records.

Performance measurements should be collected from the start, but a final galaxy-scale performance threshold is not required for Phase 1.
