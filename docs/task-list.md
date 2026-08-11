# Project task list

[Project index](../README.md) · [Gameplay integration](gameplay-integration.md) · [Runtime orchestration](runtime-orchestration.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Semantic game facts](semantic-game-facts.md) · [Presentation snapshots](presentation-snapshots.md) · [Entity lifecycle and explicit spawning](entity-lifecycle.md) · [Scale targets and benchmarks](scale-and-benchmark-targets.md) · [Initial roadmap](roadmap.md) · [Simulation architecture](simulation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md)

This is the canonical list of project work. Design documents explain goals,
constraints, and decisions; this file records whether implementation work is
current, upcoming, deferred, or complete.

When new work is discovered, add it here even if it is not appropriate to
address immediately. Link to the relevant design document instead of copying
large design notes into the task. Move completed tasks to the completed section
rather than deleting them.

## Current focus

`TASK-014` has defined the authoritative save boundary, and `TASK-039` now
ensures removed entities leave no pending movement completion behind.
`TASK-034` remains required before supported save or load because construction,
economy, and transport must join the clean `GameSession` aggregate.

## Near-term work

## Future parking lot

These items are intentionally retained without implying that they should be
worked on now. Promote an item to current or near-term work when its
prerequisites and desired behavior are sufficiently defined.

- [ ] **TASK-015: Decide the initial meaning and scope of individual NPCs**
  - Decide whether the first NPC model represents ships, person-level
    characters, crew, or multiple categories.

- [ ] **TASK-016: Design dialogue state and presentation**
  - Define availability, conditions, choices, repeatability, memory,
    consequences, response-required classification, and conversation
    continuity under the accepted pause behavior.
  - Keep rendering and interaction in Godot while gameplay effects use normal
    commands.
  - Context: [Time and pacing](time-and-pacing.md)

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
  - Own save schema and serialization migration; `TASK-037` separately owns
    versioned content catalogs and migration of saved content references.

- [ ] **TASK-023: Decide the gameplay content format**
  - Determine which content is code-defined, data-defined, or externally
    scriptable.
  - Revisit modding goals and security constraints at that time.
  - Feed the selected content identity and provenance model into `TASK-037`.

- [ ] **TASK-037: Version content catalogs and migrate saved content references**
  - Begin after `TASK-022` selects save versioning and `TASK-023` selects the
    gameplay content format.
  - Define stable content catalog identities, versions, dependency metadata,
    and source provenance for built-in and mod-provided content.
  - Detect identifier collisions across combined content sources without
    silently replacing definitions, and report both conflicting sources.
  - Define deterministic migrations or clear incompatibility diagnostics when
    an older save references renamed, replaced, removed, or changed content.
  - Context: [Relational simulation architecture](relational-simulation-architecture.md)

- [ ] **TASK-040: Define player-safe recovery from corrupted sessions and content failures**
  - Decide the player-facing flow for a poisoned live session, failed
    checkpoint validation, and unavailable, incompatible, or corrupt built-in
    and mod-provided content.
  - Preserve the authoritative rule that invalid state never resumes, silently
    repairs, or publishes a partial session. Determine the safe recovery
    choices, diagnostic capture, actionable explanation, and how the player
    returns to a verified session without exposing internal exception detail.
  - Define the provenance and compatibility information needed to identify a
    failing content source and the boundary between disabling content for a
    future load and preserving an already-running authoritative session.
  - Add focused failure-injection and continuity tests once the recovery
    contract is accepted. Depends on `TASK-022`, `TASK-023`, and `TASK-037`.
  - Context: [Authoritative save boundary](authoritative-save-boundary.md) · [Version content catalogs](task-list.md#task-037-version-content-catalogs-and-migrate-saved-content-references)

- [ ] **TASK-038: Implement application pause, speed, and input timing**
  - Replace fixed real-time advancement with the accepted pacing state and
    completed-timestamp control checkpoints.
  - Load and validate the mod-configurable speed ladder, preserve local player
    pacing preferences, and drain buffered input deterministically before
    further advancement.
  - Integrate response-required dialogue automatic pause only after `TASK-016`
    defines the corresponding dialogue state and continuity.
  - Context: [Time and pacing](time-and-pacing.md)

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

- [ ] **TASK-030: Define runtime connector availability and access**
  - Define enabled state, actor-specific access requirements, and the authority
    allowed to change either before adding mutable connector state.
  - Define replan, wait, wake, failure, command, fact, and snapshot behavior for
    changes before traversal begins and while transit is active.
  - Begin only when a concrete gameplay system can own availability or access;
    faction relationships in `TASK-012` and scripted behavior in `TASK-017`
    may supply those requirements.
  - Context: [Navigation and spatial architecture](navigation-architecture.md)

- [ ] **TASK-031: Migrate Phase 1 logistics to hierarchical navigation**
  - Define and approve an explicit mapping from legacy `LocationId` nodes to
    systems, spatial entities, inventories, facilities, and connector
    endpoints; do not infer it from the old route graph.
  - Adapt logistics to request reachability and travel estimates without
    selecting graph legs itself.
  - Begin after `TASK-009` establishes orchestration ownership and `TASK-011`
    establishes the required spatial-entity identities.
  - Preserve the existing Phase 1 acceptance fingerprints until an explicitly
    approved fixture migration changes them.
  - Context: [Navigation and spatial architecture](navigation-architecture.md)

- [ ] **TASK-032: Define semantic economy facts**
  - Define gameplay-facing production, construction, and logistics lifecycle
    facts after `TASK-009` establishes their commit owners.
  - Coordinate construction completion with the entity-lifecycle contract from
    `TASK-011`; do not expose Phase 1 diagnostic records as semantic facts.
  - Preserve typed causes, deterministic proposal ordering, bounded retention,
    and the existing distinction between facts and internal effects.
  - Context: [Semantic game facts](semantic-game-facts.md) · [Runtime orchestration](runtime-orchestration.md)

- [ ] **TASK-033: Define selection sets and group or fleet commands**
  - Define whether a command targets a transient presentation selection, a
    persistent group, or both, without conflating either with simulation
    authority.
  - Define deterministic membership ordering, controller eligibility, command
    acceptance, atomic versus partial outcomes, order ownership, cancellation,
    failure, and semantic facts for multi-ship intent.
  - Begin after `TASK-011` establishes entity lifecycle and identity. Build on
    the shared actor-order lifecycle from `TASK-006` and the client-owned
    selection contract in `TASK-010`.
  - Context: [Presentation snapshots](presentation-snapshots.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [ ] **TASK-034: Integrate clean-session economy and transport with entity lifecycle**
  - Begin only after reusable economic and transport owners join the clean
    `GameSession`; do not pull acceptance-only Phase 1 ownership into production.
  - Make construction, economy, and transport state part of the session's
    atomic aggregate so `TASK-014` can support save and load without an
    externally supplied authoritative workflow owner.
  - Add owner-provided prepare and release operations for jobs, reservations,
    capacity commitments, and scheduled work that reference a removed ship or
    cargo inventory.
  - Preserve lifecycle rejection atomicity, deterministic owner ordering,
    missing-reference event behavior, and lifecycle fact ordering.
  - Context: [Entity lifecycle and explicit spawning](entity-lifecycle.md) · [Runtime orchestration](runtime-orchestration.md)

- [ ] **TASK-036: Define piracy and its relationship consequences**
  - Define which acts count as piracy and distinguish piracy from ordinary
    trade, territorial violations, salvage, privateering, and declared war.
  - Define victim, witness, attribution, jurisdiction, stolen-property, mission,
    and retaliation rules before assigning reputation consequences.
  - Define how piracy affects the offender, asset owner, controller, victim,
    territorial authority, and informed third parties without adding automatic
    economic guilt by association.
  - Coordinate with combat in `TASK-019`, player knowledge in `TASK-020`, and
    the accepted relational gameplay model before choosing simulation state or
    commands.
  - Context: [Relational gameplay model](factions.md)

## Completed foundations

- [x] **TASK-039: Cancel removed-entity movement events from the agenda**
  - Active local motion and connector transit retain their scheduled completion
    `EventKey`. Removal prepares, revalidates, and cancels that exact event
    before cross-owner cleanup, with no creation-sequence allocation.
  - Missing or mismatched movement entries reject during preparation; a
    post-prepare mismatch poisons the session and blocks authoritative work or
    checkpoint capture. Ordinary stale events for live actors remain valid
    deterministic no-ops.
  - Added focused agenda identity and sequence tests plus local-motion and
    connector-transit removal continuation coverage.
  - Context: [Authoritative save boundary](authoritative-save-boundary.md#agenda-cancellation-for-entity-removal) · [Entity lifecycle and explicit spawning](entity-lifecycle.md)

- [x] **TASK-014: Define the authoritative save boundary**
  - Defined complete authoritative checkpoint inventory, completed-commit
    capture timing, source-health admission, private direct restoration, and
    non-authoritative exclusions.
  - Defined aggregate admission: supported saves and loads remain blocked until
    `TASK-034` brings construction, economy, and transport into the clean
    `GameSession` aggregate. `TASK-039` cancels removed-entity movement events
    before checkpoint capture and restore implementation.
  - Save encoding and versioning remain in `TASK-022`; versioned content
    catalogs and saved content-reference migration remain in `TASK-037`.
    Future domain owners must supply their own authoritative sections before
    they participate in a supported saved session.
  - Context: [Authoritative save boundary](authoritative-save-boundary.md) · [Relational simulation architecture § Authoritative relationship save inventory](relational-simulation-architecture.md#authoritative-relationship-save-inventory)

- [x] **TASK-013: Define pause, speed, and input timing**
  - Defined quiescent input boundaries, immediate paused command commit,
    same-time command-sequence ordering, and future-only scheduled completion.
  - Defined independent pause and running-speed state, a validated
    mod-configurable ladder with default `1x`, `2x`, `5x`, `10x`, and `30x`
    presets, and no catch-up debt or outcome changes when performance lags.
  - Defined the enabled-by-default player preference for automatically pausing
    response-required dialogue, including manual override and safe restoration
    of the prior speed.
  - Implementation remains in `TASK-038`; dialogue classification and
    continuity remain in `TASK-016`.
  - Context: [Time and pacing](time-and-pacing.md)

- [x] **TASK-012: Translate relational gameplay into simulation architecture**
  - Added `PrincipalId` identity, configurable directional standing, canonical
    mutual diplomacy, explicit standing-dependent grants, and clean-session
    asset ownership without conflating ownership and actor control.
  - Added rejection-atomic, source-scoped idempotent relationship effects,
    stable reduction, immutable diagnostic snapshots, and ordered semantic
    facts while preserving the single-thread deterministic reference path.
  - Added observer-scoped presentation that exposes public diplomacy, incoming
    treatment, and grants issued to the observer without leaking private reverse
    standing through snapshots or facts.
  - Recorded the exact authoritative relationship save inventory and direct
    atomic restoration contract for `TASK-014`. Save encoding remains
    `TASK-022`, content-reference migration remains `TASK-037`, and gameplay
    policy remains with its owning domains, including piracy in `TASK-036`.
  - Context: [Relational gameplay model](factions.md) · [Relational simulation architecture](relational-simulation-architecture.md)

- [x] **TASK-035: Define the relational gameplay model**
  - Established the independent-trader starting position, provisional power
    concept, directional standing, explicit diplomacy, and the initial five
    relationship bands.
  - Defined standing-dependent grants, enforceable restricted space,
    territorial authority, explainable reputation changes, and third-party
    consequences limited to explicit known actions rather than economic
    activity.
  - Defined ownership, control, affiliation, strategic-goal, and information
    boundaries without selecting implementation types or storage.
  - Piracy-specific attribution and relationship design remains in `TASK-036`;
    simulation architecture was completed in `TASK-012`.
  - Context: [Relational gameplay model](factions.md)

- [x] **TASK-011: Define entity lifecycle and explicit spawning**
  - Added session-wide entity identity, prepared setup registration, explicit
    allocator high-water marks, complete ship snapshots, and entity navigation
    destinations.
  - Added durable, idempotent construction materialization with authoritative
    facility policy, stable batching, complete ship/cargo publication, and
    causal lifecycle facts.
  - Added deterministic removal across current clean-session owners, including
    active, queued, and suspended target invalidation, reserved-cargo rejection,
    stale scheduled-event handling, presentation resolution, and removal facts.
  - Future clean-session economic and transport owner cleanup is tracked in
    `TASK-034`; legacy spatial migration remains in `TASK-031`.
  - Context: [Entity lifecycle and explicit spawning](entity-lifecycle.md)

- [x] **TASK-010: Generalize presentation snapshots**
  - Added immutable presentation request, selection, and snapshot read models
    around the existing clean-session world snapshot and cursor-based fact
    query.
  - Selection is a client-owned, `ShipId`-ordered set with an optional focused
    ship. Resolution reports stale IDs without creating entity-lifecycle
    semantics, and only explicitly ship-referencing facts appear in the
    selected-ship subset.
  - Godot now supports Shift-click multi-selection while retaining one focused
    ship for its existing single-ship move and cancel commands. Cursor gaps
    clear its bounded local fact feed and remain visible in status.
  - Group commands remain in `TASK-033`; generic non-ship selection follows
    entity lifecycle work in `TASK-011`.
  - Context: [Presentation snapshots](presentation-snapshots.md)

- [x] **TASK-009: Establish production runtime systems and retire `PhaseOneRuntime`**
  - Added fixed actor and economic coordinators, stable read/evaluate batches,
    typed effects, deterministic reducers and commit owners, shared agenda
    commit, and per-domain measurements on the single-thread reference path.
  - `PhaseOneScenario` now directly composes reusable economic behavior while
    retaining only fixture setup, disruptions, stopping, temporary
    materialization, metrics, reports, and accepted fingerprints.
  - Actor command results, movement, facts, all five canonical benchmark
    digests, and the Phase 1 event/final-state digest remain unchanged.
  - Entity lifecycle completed in `TASK-011`; legacy-location navigation
    migration and semantic economy facts remain in `TASK-031` and `TASK-032`.
  - Context: [Runtime orchestration and domain ownership](runtime-orchestration.md)

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
  - Ordered semantic facts were completed by `TASK-008`.
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
    `TASK-006`; semantic facts were completed by `TASK-008`.
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
    first concrete failure proof. `TASK-008` completed semantic order
    transitions for the current lifecycle.
  - `TASK-011` completed coordinated entity removal and target invalidation on
    top of the actor cleanup boundary.
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
    `TASK-006`; `TASK-008` completed semantic cancellation facts, and
    `TASK-011` completed entity-removal invalidation.
  - Context: [Gameplay integration §1.5 and §2.6](gameplay-integration.md#15-scheduled-work-has-an-incomplete-cancellation-contract)

- [x] **TASK-008: Introduce semantic game facts**
  - `GameSession` exposes typed, immutable facts with one monotonic session
    sequence and immediate command or scheduled-event causes.
  - Command outcomes, order transitions, local motion, and connector transit
    commit through deterministic proposal ordering; ignored events remain
    diagnostics.
  - An explicitly sized bounded window supports cursor reads, limits result
    allocation to requested facts, and reports retention gaps without copying
    facts into world snapshots.
  - Deterministic tests cover rejection, cancellation, replacement, override
    restoration, connector traversal, stale events, retention, and incremental
    advancement. Presentation consumption completed in `TASK-010`; persistent
    scripts remain in `TASK-017`, and save boundaries in `TASK-014`.
  - Context: [Semantic game facts](semantic-game-facts.md)

- [x] **TASK-024: Establish scale, performance, and concurrency targets**
  - Accepted reference and stress envelopes include galaxy-wide and
    crowded-system workloads; `1x` crowded and `30x` mixed-galaxy speeds remain
    informational goals without reference-hardware or timing gates.
  - A dedicated headless runner provides versioned canonical presets, validated
    JSON files and numeric overrides, readable progress, machine-readable
    results, repeatability checks, and committed correctness digests.
  - Normal unit tests retain only fast configuration and smoke correctness
    coverage. Heavy Phase 1, spatial, connector-navigation, and fact-retention
    scenarios require explicit `--suite full` selection.
  - The canonical Release full suite passed all five committed digests across
    repeated iterations on 2026-07-28. Concurrent worker-count and long-running
    suites remain future work under `TASK-029`.
  - Context: [Scale targets and benchmark architecture](scale-and-benchmark-targets.md)

- [x] **TASK-028: Establish hierarchical system-space navigation**
  - Added typed systems and local positions, position and system destinations,
    `RouteId`-free deterministic planning, and authoritative scheduled local
    motion.
  - Added immutable directional connector topology, dedicated endpoint,
    connection, and transit identities, deterministic multi-system planning,
    scheduled emergence, and discriminated spatial snapshots.
  - Cancellation and replacement are generation-safe; connector transit is
    non-interruptible, and replacement orders wait and wake on emergence.
  - Runtime connector availability and access remain in `TASK-030`; `TASK-011`
    completed entity destinations, and Phase 1 migration remains in `TASK-031`.
  - Context: [Navigation and spatial architecture](navigation-architecture.md)

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
