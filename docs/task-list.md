# Project task list

[Project index](../README.md) · [Gameplay integration](gameplay-integration.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Initial roadmap](roadmap.md) · [Simulation architecture](simulation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md)

This is the canonical list of project work. Design documents explain goals,
constraints, and decisions; this file records whether implementation work is
current, upcoming, deferred, or complete.

When new work is discovered, add it here even if it is not appropriate to
address immediately. Link to the relevant design document instead of copying
large design notes into the task. Move completed tasks to the completed section
rather than deleting them.

## Current focus

The current architectural goal is to establish the spatial, command, and
ordering boundaries required for an interactive game. Work roughly from top to
bottom; later tasks may be refined as earlier contracts become concrete.

- [ ] **TASK-028: Establish hierarchical system-space navigation**
  - Introduce systems as distinct local navigable spaces and connectors as
    explicit inter-system transitions.
  - Separate destination intent, deterministic planning, and authoritative
    movement execution; actor orders must not contain `RouteId`.
  - Represent system-local position and scheduled motion authoritatively while
    keeping rendering interpolation non-authoritative.
  - Preserve the Phase 1 graph as a compatibility backend during migration.
  - Proven foundation: scheduled point-to-point movement within one system was
    completed before `TASK-005`.
  - Implemented foundation: typed system-local positions, position
    destinations, `RouteId`-free local planning, authoritative scheduled motion,
    generation-safe cancellation and replacement, and immutable motion
    snapshots.
  - The controller, queue, and multi-leg order foundation required for
    connector traversal was completed by `TASK-006`.
  - Implemented connector slice: immutable directional topology, dedicated
    endpoint/connection/transit identities, deterministic duration-based
    hierarchical planning, discriminated `AtPosition`/local-motion/transit
    snapshots, scheduled emergence, non-interruptible transit cancellation,
    and replacement-order wait/wake behavior.
  - Remaining: runtime connector availability and access, additional
    destination forms, and the later Phase 1 compatibility migration.
  - Context: [Navigation and spatial architecture](navigation-architecture.md)

## Near-term work

- [ ] **TASK-008: Introduce semantic game facts**
  - Keep facts separate from internal completion events.
  - Assign stable ordering or sequence identifiers.
  - Emit initial facts for command acceptance or failure, actor movement, order
    completion, and order cancellation.
  - Define bounded retention and consumption by UI, scripts, and development
    tools.

- [ ] **TASK-009: Split Phase 1 runtime orchestration into explicit systems**
  - Separate production, construction, logistics, and order handling.
  - Define system ownership, handled inputs, emitted facts, and deterministic
    evaluation order.
  - Separate read/evaluate work from buffered effects and authoritative commit
    so independent batches can later execute concurrently.
  - Start with a fixed explicit dispatcher; do not add a dynamic plugin system
    without a demonstrated need.

- [ ] **TASK-024: Establish scale, performance, and concurrency targets**
  - Select target counts for total systems, ships, facilities, factions,
    active scripts, pending events, and retained facts.
  - Include a target and benchmark for many active ships in one crowded system,
    not only galaxy-wide totals.
  - Measure single-thread behavior first, then scaling across worker counts and
    batch layouts while requiring identical authoritative results.
  - Establish evaluation, effect-buffer, deterministic-merge, and ownership
    boundaries before adding concurrent execution or specialized storage.
  - Context: [Concurrency and performance architecture](concurrency-and-performance.md)

- [ ] **TASK-010: Generalize presentation snapshots**
  - Replace fixture-specific presentation assumptions incrementally.
  - Add selection details, controller, current order, reason, and relevant
    recent facts.
  - Expose authoritative system-local spatial and motion state while preserving
    rendering interpolation as non-authoritative presentation state.

- [ ] **TASK-011: Define entity lifecycle and explicit spawning**
  - Distinguish causal construction from immediate scenario or scripted spawn.
  - Define spawn validation, ownership, design, location, initial state, and
    initial order.
  - Define destruction and despawn cleanup for cargo, reservations, orders,
    controllers, and pending events.
  - Preserve deterministic identifier allocation and fact ordering.

- [ ] **TASK-012: Define faction and relationship state**
  - Give organizations the state needed for ownership, hostility, reputation,
    objectives, and strategic priorities.
  - Define the boundary between authoritative knowledge and what a faction
    currently knows.
  - Keep faction-specific scripts above the shared faction and command models.

- [ ] **TASK-013: Define pause, speed, and input timing**
  - Define when commands submitted while paused take effect.
  - Define ordering for multiple commands at one simulation timestamp.
  - Define whether opening dialogue pauses automatically.
  - Keep these as local single-player rules.

- [ ] **TASK-014: Define the authoritative save boundary**
  - Inventory all state required for save and load.
  - Include simulation time, pending agenda, creation sequences, random state,
    system topology, spatial and motion state, controllers, orders, generations,
    objectives, and script or dialogue progress.
  - Defer the final serialization format until the state boundary is tested.

## Future parking lot

These items are intentionally retained without implying that they should be
worked on now. Promote an item to current or near-term work when its
prerequisites and desired behavior are sufficiently defined.

- [ ] **TASK-015: Decide the initial meaning and scope of individual NPCs**
  - Decide whether the first NPC model represents ships, person-level
    characters, crew, or multiple categories.

- [ ] **TASK-016: Design dialogue state and presentation**
  - Define availability, conditions, choices, repeatability, memory,
    consequences, and simulation pause behavior.
  - Keep rendering and interaction in Godot while gameplay effects use normal
    commands.

- [ ] **TASK-017: Design deterministic scripted events**
  - Define time-, location-, threshold-, and fact-based triggers.
  - Define one-shot and repeatable persistent state.
  - Define scheduling, checkpointing, wake, cancellation, and persistence
    semantics for long-running scripted behavior; the current runtime does not
    provide long-running script execution.
  - Restrict effects to an approved command vocabulary.
  - Separate development cheats from shipped narrative effects.

- [ ] **TASK-018: Design objectives, missions, victory, and defeat**
  - Represent milestones as persistent state and semantic facts rather than
    implicit engine termination.

- [ ] **TASK-019: Define combat resolution**
  - Define orders, targeting, damage, withdrawal, surrender, capture, and
    destruction.
  - Decide how observed and unobserved combat differ without changing causal
    outcomes.

- [ ] **TASK-020: Define player knowledge and information staleness**
  - Separate complete authoritative state from currently known, observed, or
    outdated information.

- [ ] **TASK-021: Define random-number stream ownership**
  - Decide how systems and scripts receive deterministic randomness.
  - Preserve reproducibility when unrelated systems add random draws.

- [ ] **TASK-022: Select save format, versioning, and migration strategy**
  - Begin only after the authoritative save boundary is understood.

- [ ] **TASK-023: Decide the gameplay content format**
  - Determine which content is code-defined, data-defined, or externally
    scriptable.
  - Revisit modding goals and security constraints at that time.

- [ ] **TASK-025: Define bounded explanation history**
  - Retain enough decisions and facts to explain behavior to the player without
    preserving an unlimited event log.

- [ ] **TASK-026: Expand faction strategic planning**
  - Turn priorities into executable objectives and orders.
  - Make logistical disruption constrain achievable plans.
  - Add faction asymmetry only after one shared planning model is proven.

- [ ] **TASK-027: Evaluate a broader entity storage model**
  - Reconsider ECS or another indexed model only when concrete query or scale
    evidence justifies it.

- [ ] **TASK-029: Add long-running stability and performance suites**
  - Add increasing-scale scenarios, invariant checks, benchmarks, and
    reproducible failure traces.
  - Compare deterministic state and event digests across worker counts, batch
    sizes, and valid partition layouts.

## Completed foundations

- [x] **TASK-001: Define deterministic same-time processing**
  - The engine drains explicit physical-completion, state-update, and decision
    phases, reconciles once at the decision barrier, and completes the current
    timestamp before honoring a stop condition.
  - Same-time work may target the current or a later phase but cannot reopen a
    completed phase.
  - Context: [Gameplay integration §1.1 and §2.1](gameplay-integration.md#11-same-time-event-ordering-does-not-provide-a-complete-phase-barrier)

- [x] **TASK-002: Define the gameplay command contract**
  - Gameplay intent is distinct from scheduled events and receives deterministic
    sequence, simulation-time, source, result, and diagnostic records.
  - Source attribution supports player, autonomous, dialogue, and script
    callers without authentication or multiplayer-authority semantics.
  - Persistent-session integration was completed by `TASK-003`.
  - Context: [Gameplay integration §1.2 and §2.2](gameplay-integration.md#12-there-is-no-general-gameplay-command-boundary)

- [x] **TASK-003: Introduce a persistent game-session facade**
  - `GameSession` accepts and records gameplay commands, advances without a
    fixture milestone stop, captures immutable presentation state, and is the
    boundary used by Godot.
  - `PhaseOneScenario` remains isolated under `Acceptance/` as a bounded
    regression harness.
  - Ordered semantic facts remain in `TASK-008`.
  - Context: [Gameplay integration §1.2, §1.7, and §2.3](gameplay-integration.md#12-there-is-no-general-gameplay-command-boundary)

- [x] **TASK-004: Separate setup authority from runtime mutation**
  - `SimulationWorld` is internal and can only be created through a one-use
    setup capability reserved for fixture and future save-load construction.
  - Neither `GameSession` nor the acceptance facade exposes the mutable live
    world; callers use snapshots, stable identifiers, commands, and records.
  - Rejected-command tests prove authoritative snapshot, event, and decision
    state remains unchanged.
  - Context: [Gameplay integration §1.3 and §2.4](gameplay-integration.md#13-authoritative-state-is-publicly-mutable)

- [x] **TASK-005: Implement the first interactive ship order**
  - `GameSession` now owns a clean application runtime rather than the Phase 1
    acceptance runtime. Explicit setup seeds systems and spatial ships without
    introducing a general spawning API.
  - Player move and cancel commands drive one current destination order through
    deterministic local planning and authoritative scheduled movement.
  - Completion, cancellation, replacement, command rejection, immutable
    snapshots, and stale-arrival handling are covered headlessly.
  - Godot selects a ship, submits or replaces a position destination, cancels
    with right-click, and displays the order, reason, destination, and motion.
  - Queues, controller changes, and the broader lifecycle were completed by
    `TASK-006`; semantic facts remain in `TASK-008`.
  - Context: [Gameplay integration §2.3, §2.9, and §3.2](gameplay-integration.md#23-introduce-a-persistent-game-session-facade)

- [x] **TASK-006: Define actor control and order lifecycle**
  - Actors now have explicit player or autonomous base controllers, exact
    command-source eligibility, stable control revisions, and one non-nesting
    temporary scripted override with an opaque reason ID and explicit
    cancel-outstanding release policy.
  - One shared order coordinator supports explicit replace-all or FIFO append,
    stable order-ID cancellation, queued promotion, suspension, restoration,
    terminal reasons, and immutable active, queued, and suspended snapshots.
  - A private plan executor advances multiple local travel legs without
    confusing leg completion with order completion.
  - Move and cancel evaluation produce immutable proposals before deterministic
    owner commit; autonomous and player sources use the same order model.
  - Godot uses click to replace, Shift-click to append, and right-click to
    cancel the active order while displaying controller and queue state; route
    overlays are limited to active or waiting orders and clear on cancellation
    or completion.
  - Waiting and failed states are defined; `TASK-028` proves transit waiting
    and emergence wake, while target invalidation in `TASK-011` retains the
    first concrete failure proof. Semantic transition facts remain in
    `TASK-008`.
  - An internal coordinated actor-cleanup boundary invalidates pending movement
    before removing spatial, control, and order ownership; destruction policy
    and commands remain in `TASK-011`.
  - The current runtime still does not execute long-running scripted behavior;
    scheduling, checkpointing, and persistence remain in `TASK-017`.
  - Context: [Actor control and order lifecycle](actor-control-and-orders.md)

- [x] **TASK-007: Complete scheduled-event cancellation and invalidation**
  - Production, construction, and transport activities advance generations
    during cancellation or interruption and validate identity, generation, and
    expected state before scheduled mutation.
  - Ignored events are deterministic no-ops with recorded stale-generation,
    missing-reference, or state-mismatch diagnostics.
  - Actor movement and order cancellation apply this contract through
    `TASK-006`; destruction cleanup remains in `TASK-011` and semantic
    cancellation facts remain in `TASK-008`.
  - Context: [Gameplay integration §1.5 and §2.6](gameplay-integration.md#15-scheduled-work-has-an-incomplete-cancellation-contract)

- [x] **DONE-001: Establish the project vision and modular design documents**
  - The project defines a persistent, map-command, materially causal,
    simulation-first single-player game direction.

- [x] **DONE-002: Implement the deterministic Phase 1 economic loop**
  - Mining, refining, component production, transport, shortages, recovery, and
    ship construction run as an integrated headless scenario.

- [x] **DONE-003: Port the authoritative simulation to C# and .NET**
  - The solution contains the rendering-independent simulation library, CLI,
    and deterministic test project.

- [x] **DONE-004: Establish deterministic event agenda ordering**
  - Scheduled events have stable timestamp, phase, and creation-sequence
    ordering.
  - The phase-barrier follow-up was completed by `TASK-001`.

- [x] **DONE-005: Extract the reusable generic simulation engine**
  - `SimulationEngine<TEvent>` is independent of Phase 1 materials, routes,
    facilities, and success conditions.

- [x] **DONE-006: Centralize durable scenario state in `SimulationWorld`**
  - The world owns navigation, inventories, production, construction, ships,
    transport state, designs, and identifier sequences.
  - Runtime mutation access was restricted by `TASK-004`.

- [x] **DONE-007: Generalize construction into a product-neutral process**
  - Shared construction reserves inputs, queues work, tracks completion, and
    delegates product materialization.

- [x] **DONE-008: Add immutable Phase 1 presentation snapshots**
  - Rendering consumes copied simulation state and does not make continuous
    display coordinates authoritative.

- [x] **DONE-009: Add the Godot graphics foundation**
  - The Godot client references the simulation library, advances the live
    scenario, and displays locations, routes, ships, and construction progress.

- [x] **DONE-010: Add deterministic regression and subsystem tests**
  - The suite covers time, event ordering, navigation, inventory, production,
    transport, construction, snapshots, and the integrated Phase 1 scenario.
