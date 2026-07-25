# Project task list

[Project index](../README.md) · [Gameplay integration](gameplay-integration.md) · [Initial roadmap](roadmap.md) · [Simulation architecture](simulation-architecture.md)

This is the canonical list of project work. Design documents explain goals,
constraints, and decisions; this file records whether implementation work is
current, upcoming, deferred, or complete.

When new work is discovered, add it here even if it is not appropriate to
address immediately. Link to the relevant design document instead of copying
large design notes into the task. Move completed tasks to the completed section
rather than deleting them.

## Current focus

The current architectural goal is to establish the command and ordering
boundaries required for an interactive game. Work roughly from top to bottom;
later tasks may be refined as earlier contracts become concrete.

- [ ] **TASK-002: Define the gameplay command contract**
  - Separate requested intent from internal scheduled events.
  - Define command acceptance, rejection, validation, ordering, and failure
    results.
  - Define how player, AI, dialogue, and scripts identify their command source
    without introducing multiplayer authority concepts.
  - Record commands sufficiently for deterministic tests and debugging.
  - Context: [Gameplay integration §1.2 and §2.2](gameplay-integration.md#12-there-is-no-general-gameplay-command-boundary)

- [ ] **TASK-003: Introduce a persistent game-session facade**
  - Accept gameplay commands.
  - Advance authoritative simulation time without a fixture-specific success
    stop.
  - Capture immutable presentation state.
  - Expose ordered semantic facts and command results.
  - Keep `PhaseOneScenario` available as a bounded acceptance scenario.

- [ ] **TASK-004: Separate setup authority from runtime mutation**
  - Keep privileged world construction available to fixtures and save loading.
  - Prevent presentation and gameplay callers from mutating the live
    `SimulationWorld` directly.
  - Replace the public mutable-world boundary with queries, snapshots, stable
    identifiers, and commands.
  - Add tests proving rejected commands cannot partially mutate state.

- [ ] **TASK-005: Implement the first interactive ship order**
  - Select a ship in Godot.
  - Submit a move order through the game-session facade.
  - Display the current order, destination, state, and reason.
  - Support cancelling or replacing the order.
  - Verify the same command sequence produces the same result headlessly and
    through the Godot-facing session boundary.

## Near-term work

- [ ] **TASK-006: Define actor control and order lifecycle**
  - Define player control, autonomous control, and temporary scripted override.
  - Define order states, queuing, interruption, cancellation, completion, and
    failure.
  - Define how control returns after an override ends.
  - Use one order model for player-controlled and autonomous actors.

- [ ] **TASK-007: Complete scheduled-event cancellation and invalidation**
  - Define when event generations change.
  - Validate generation and referenced state before every scheduled mutation.
  - Define stale-event no-op, diagnostic, and fact-emission behavior.
  - Cover order replacement, actor destruction, and activity interruption.

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
  - Start with a fixed explicit dispatcher; do not add a dynamic plugin system
    without a demonstrated need.

- [ ] **TASK-010: Generalize presentation snapshots**
  - Replace fixture-specific presentation assumptions incrementally.
  - Add selection details, controller, current order, reason, and relevant
    recent facts.
  - Preserve rendering interpolation as non-authoritative state.

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
    controllers, orders, generations, objectives, and script or dialogue
    progress.
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

- [ ] **TASK-024: Establish scale and performance targets**
  - Select target counts for locations, ships, facilities, factions, active
    scripts, pending events, and retained facts.
  - Profile before introducing parallel processing or specialized storage.

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

- [ ] **TASK-028: Evaluate continuous space or richer navigation**
  - Preserve the existing navigation boundary so dynamic hazards, gates, access
    rules, or continuous movement can be introduced without rewriting actor
    orders.

- [ ] **TASK-029: Add long-running stability and performance suites**
  - Add increasing-scale scenarios, invariant checks, benchmarks, and
    reproducible failure traces.

## Completed foundations

- [x] **TASK-001: Define deterministic same-time processing**
  - The engine drains explicit physical-completion, state-update, and decision
    phases, reconciles once at the decision barrier, and completes the current
    timestamp before honoring a stop condition.
  - Same-time work may target the current or a later phase but cannot reopen a
    completed phase.
  - Context: [Gameplay integration §1.1 and §2.1](gameplay-integration.md#11-same-time-event-ordering-does-not-provide-a-complete-phase-barrier)

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
  - Restricting runtime mutation is tracked by `TASK-004`.

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
