# Project task list

[Project index](../README.md) · [Gameplay integration](gameplay-integration.md) · [Dialogue](dialogue.md) · [Deterministic randomness](deterministic-randomness.md) · [Runtime orchestration](runtime-orchestration.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Individual NPC scope](individual-npc-scope.md) · [Semantic game facts](semantic-game-facts.md) · [Presentation snapshots](presentation-snapshots.md) · [Entity lifecycle and explicit spawning](entity-lifecycle.md) · [Scale targets and benchmarks](scale-and-benchmark-targets.md) · [Initial roadmap](roadmap.md) · [Simulation architecture](simulation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md)

This is the canonical list of project work. Design documents explain goals,
constraints, and decisions; this file records whether implementation work is
current, upcoming, deferred, or complete.

When new work is discovered, add it here even if it is not appropriate to
address immediately. Link to the relevant design document instead of copying
large design notes into the task. Move completed tasks to the completed section
rather than deleting them.

## Current focus

`TASK-023` is complete. The project now has an accepted gameplay-content,
static-scenario, stable-identity, validation, and trust-boundary design.
The shared content-validation foundation is now implemented.
`TASK-043`, `TASK-044`, and `TASK-045` are complete. The planned-system
inventory and scope-gap review give every identified system an accepted owner,
a tracked design task, or an explicit deferral. Localization boundaries are now
established before broader player-facing game work. `TASK-061` separately owns
comprehensive accessibility design. `TASK-063` completed the format-neutral
models, shared production validation path, immutable catalogs, and headless
validator. `TASK-048` remains the next content
integration dependency and retains built-in content plus static new-game
integration. `TASK-015` established the initial ship-only NPC boundary.
`TASK-042` and `TASK-053` may now design NPC decision quality and autonomous
work selection without introducing person-level state. `TASK-016` completed
the dialogue definition, authority, participant, condition, choice, memory,
continuity, consequence, pacing, save, and presentation design. `TASK-065`
retains dialogue implementation. `TASK-021` completed the deterministic random
root, derived-value, owned-stream, commit, save, and compatibility design;
`TASK-066` retains implementation.

## Near-term work

No task is currently promoted to near-term work. `TASK-048` remains in the
near-term parking-lot horizon until the project owner promotes it.

## Future parking lot

These items are intentionally retained without implying that they should be
worked on now. Promote an item to current or near-term work when its
prerequisites and desired behavior are sufficiently defined.

The parking-lot horizons organize deferred work by likely sequencing. A task
in the **Near term** parking-lot section remains deferred; it is not promoted to
the project-level **Near-term work** section above.

### Near term

- [ ] **TASK-064: Design event-responsive simulation pacing**
  - Define how authoritative or presentation events can request a local pacing
    response without becoming simulation owners or interrupting an in-progress
    timestamp cycle.
  - Establish the trigger categories, player-configurable policy, priority and
    conflict behavior, acknowledgement and override semantics, and visible
    explanation required for event-driven speed changes. Include the desired
    examples of reducing to `1x` when a new dialogue event arrives and pausing
    when offscreen combat begins.
  - Decide how event delivery, knowledge or visibility, repeated events,
    dismissal, save or load, and multiple simultaneous requests interact with
    the player-controlled pacing state. Preserve local single-player ownership,
    deterministic simulation outcomes, and the completed-timestamp checkpoint
    boundary.
  - Coordinate response-required dialogue with `TASK-016`, player knowledge
    and offscreen-event visibility with `TASK-020`, semantic event consumption
    with `TASK-008`, and pacing implementation with `TASK-038`.
  - Context: [Time and pacing](time-and-pacing.md) · [Semantic game facts](semantic-game-facts.md) · [Presentation snapshots](presentation-snapshots.md)

- [ ] **TASK-019: Define interactions between ships in motion**
  - Define how the simulation discovers, schedules, and resolves interactions
    involving ships that remain on authoritative local-motion segments rather
    than requiring them to stop or arrive first.
  - Cover moving-versus-moving range crossing, proximity and swept-path
    queries, exact interaction timestamps, following and interception, target
    replanning, and deterministic handling of simultaneous interactions.
  - Decide when fixed simulation steps, scheduled encounter events, or
    triggered reevaluation apply, including how interactions interrupt,
    preserve, or replace existing motion and what is possible during connector
    transit.
  - Keep crowded-system queries spatially partitionable and results independent
    of worker count, partition shape, batch size, and evaluation completion
    order. Provide the shared physical interaction substrate required by
    combat, sensors, avoidance, inspection, assistance, and other ship-local
    gameplay without defining each domain's outcome policy.
  - Context: [Navigation and spatial architecture](navigation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md) · [Simulation architecture](simulation-architecture.md)

- [ ] **TASK-020: Define player knowledge and information staleness**
  - Separate complete authoritative state from currently known, observed, or
    outdated information.
  - Refine the initial dialogue rule that conditions may inspect any approved
    authoritative data: decide which conditions should instead use player
    knowledge, how availability discloses hidden or stale information, and how
    authored content adopts that refinement without silently changing existing
    conversation behavior.
  - Context: [Dialogue](dialogue.md) · [Player experience](player-experience.md)

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

- [ ] **TASK-038: Implement application pause, speed, and input timing**
  - Replace fixed real-time advancement with the accepted pacing state and
    completed-timestamp control checkpoints.
  - Load and validate the mod-configurable speed ladder, preserve local player
    pacing preferences, and drain buffered input deterministically before
    further advancement.
  - Integrate response-required dialogue automatic pause using the accepted
    classification and continuity contract from completed `TASK-016`.
  - Context: [Time and pacing](time-and-pacing.md)

- [ ] **TASK-065: Implement dialogue state and presentation**
  - Implement the accepted authored-definition, validation, authoritative
    conversation, participant, condition, choice, memory, fact, snapshot,
    checkpoint, and restore contracts from `TASK-016`.
  - Add the Godot foreground and pending-conversation surfaces, localized
    dialogue and optional named-person attribution, and response-required
    pacing integration without making presentation state authoritative.
  - Coordinate one normal gameplay-command consequence atomically with a
    choice, retain a single-thread reference path, and prove deterministic
    results across supported worker and batch layouts.
  - Build on completed `TASK-016`. Coordinate content integration with
    `TASK-048`, pacing with `TASK-038` and `TASK-064`, the application shell
    with `TASK-049`, station identity with `TASK-057`, and knowledge refinement
    with `TASK-020`.
  - Context: [Dialogue](dialogue.md) · [Gameplay content](gameplay-content.md) · [Time and pacing](time-and-pacing.md) · [Concurrency and performance](concurrency-and-performance.md)

- [ ] **TASK-066: Implement deterministic random foundations**
  - Implement the accepted 256-bit root seed, versioned SHA-256 canonical
    derivation, stateless named samples, scoped stateful `xoshiro256**`
    streams, and integer sampling capabilities from `TASK-021`.
  - Add aggregate ownership, exact checkpoint and restore, stable stream
    lifecycle, commit-only consumption, algorithm registration, and clear
    rejection of incompatible or malformed state.
  - Prove golden vectors, namespace isolation, unrelated-draw stability,
    rejected-work non-consumption, exact continuation after restore, and
    identical results across supported worker and batch layouts.
  - Begin before the first production gameplay consumer of randomness.
    Coordinate script capabilities with `TASK-017`, procedural generation with
    `TASK-047`, save encoding with `TASK-022`, and compatibility with
    `TASK-060`.
  - Context: [Deterministic randomness](deterministic-randomness.md) · [Concurrency and performance](concurrency-and-performance.md) · [Authoritative save boundary](authoritative-save-boundary.md)

- [ ] **TASK-041: Define generalized inventory, cargo, and ship equipment**
  - Evolve the current material-only inventory model so ships and other owners
    can hold the approved categories of physical items, without treating every
    item as a material unit.
  - Define item identity and stacks, capacity or slot semantics, equipment
    installation and removal, ownership, transfer, reservations, destruction
    disposition, authoritative snapshots, and save-state requirements.
  - Preserve the existing material-production and transport contracts until a
    compatible migration is explicitly designed. Coordinate catalog-defined
    item and equipment data with `TASK-023`, and combat or repair behavior
    with `TASK-046`.

- [ ] **TASK-042: Define NPC skills and bounded decision quality**
  - Decide which NPC categories, if any, have skills, competencies, preferences,
    risk tolerance, or other decision-shaping traits, and whether they are
    authored, learned, temporary, or persistent.
  - Define how those traits limit information, evaluate alternatives, or select
    a satisfactory action without requiring every NPC to make the globally
    optimal choice on every decision.
  - Preserve deterministic outcomes by defining stable inputs, tie-breaking,
    decision cadence, state ownership, facts, snapshots, and save requirements;
    do not make results depend on worker count or evaluation completion order.
  - Coordinate the NPC categories with `TASK-015`, player and NPC information
    boundaries with `TASK-020`, and faction objectives and order generation with
    `TASK-026`.

- [ ] **TASK-048: Integrate built-in content and static new-game composition**
  - Build on the format-neutral models, physical-format adapter, production
    validation path, resolved catalogs, and headless validator completed by
    `TASK-063`.
  - Move built-in definitions and the minimal static new-game scenario through
    the disk-backed production path. Retain direct neutral-model builders only
    for focused tests; package and scenario coverage cannot bypass production
    validation or catalog construction.
  - Do not implement content-version compatibility or saved-reference migration;
    consume the resolved catalog and qualified references from `TASK-063` at
    the static session-creation boundary used by `TASK-037`.
  - Context: [Gameplay content and static new-game composition](gameplay-content.md) · [Gameplay integration](gameplay-integration.md) · [Initial roadmap](roadmap.md) · [Save format and migration](save-format-and-migration.md) · [Authoritative save boundary](authoritative-save-boundary.md)

- [ ] **TASK-049: Define the application shell and map experience**
  - Define new-game, load-game, active-session, leave-session, and fatal-startup
    flows for the local desktop application without making Godot an
    authoritative simulation owner.
  - Define the galaxy and system view hierarchy, camera and zoom behavior,
    generic entity selection and inspection, overlays, notification surfaces,
    and stale or removed entity handling needed by the minimal map application.
  - Build on the immutable presentation boundary from `TASK-010`; coordinate
    time controls with `TASK-038`, information visibility with `TASK-020`,
    localization and accessibility with `TASK-045`, and recovery with
    `TASK-040`.
  - Context: [Player experience](player-experience.md) · [Technical direction](technical-direction.md) · [Presentation snapshots](presentation-snapshots.md) · [Initial roadmap](roadmap.md)

- [ ] **TASK-050: Define save slots, autosave, and local preference storage**
  - Define slot naming and discovery, manual-save and autosave behavior,
    cadence, retention, backup visibility, overwrite confirmation, and
    cross-device or external-edit conflict handling around the validated file
    store from `TASK-022`.
  - Define storage and reset behavior for local pacing, presentation,
    localization, and accessibility preferences while keeping them outside the
    authoritative session unless an owning gameplay task explicitly requires
    otherwise.
  - Begin after `TASK-045` defines locale and accessibility ownership;
    coordinate failed-load recovery with `TASK-040` and pacing preferences with
    `TASK-038`.
  - Context: [Save format and migration](save-format-and-migration.md) · [Authoritative save boundary](authoritative-save-boundary.md) · [Time and pacing](time-and-pacing.md)

- [ ] **TASK-053: Define autonomous ship work selection**
  - Define how non-player ships discover, evaluate, select, and stop ordinary
    work through the same commands and order lifecycle used by player intent.
  - Separate ship-local policy from faction strategy, define authoritative
    policy ownership, stable inputs, cadence, contention and tie-breaking,
    bounded retry, explanation, snapshots, and save requirements.
  - Build on `TASK-006` and the production domain owners from `TASK-009` and
    `TASK-034`; coordinate information with `TASK-020`, faction objectives with
    `TASK-026`, and bounded competence with `TASK-042`.
  - Context: [Vision](vision.md) · [Player experience](player-experience.md) · [Runtime orchestration](runtime-orchestration.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [ ] **TASK-061: Define comprehensive accessibility behavior**
  - Define input remapping, keyboard and controller equivalence, focus
    navigation, assistive-technology and screen-reader behavior, contrast and
    non-color cues, reduced motion and flashing, captions, audio cues, and
    supported text-scale behavior.
  - Establish platform capability boundaries, user-visible modes, interactions
    between settings, and acceptance evidence without making accessibility
    preferences authoritative simulation or save state.
  - Build on the localized text, layout, semantic-label, and expansion baseline
    from `TASK-045`; coordinate application controls with `TASK-049` and local
    preference storage with `TASK-050`.
  - Context: [Internationalization and localization](internationalization-and-localization.md) · [Player experience](player-experience.md) · [Presentation snapshots](presentation-snapshots.md)

### Mid term

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

- [ ] **TASK-025: Define bounded explanation history**
  - Retain enough decisions and facts to explain behavior to the player without
    preserving an unlimited event log.

- [ ] **TASK-026: Expand faction strategic planning**
  - Turn priorities into executable objectives and orders.
  - Make logistical disruption constrain achievable plans.
  - Add faction asymmetry only after one shared planning model is proven.

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
  - Restore whole-simulation disruption coverage after this contract is
    implemented. It must use authoritative connector state, not a Phase 1
    test-only route-toggle shim.
  - Begin only when a concrete gameplay system can own availability or access;
    faction relationships in `TASK-012` and scripted behavior in `TASK-017`
    may supply those requirements.
  - Context: [Navigation and spatial architecture](navigation-architecture.md)

- [ ] **TASK-036: Define piracy and its relationship consequences**
  - Define which acts count as piracy and distinguish piracy from ordinary
    trade, territorial violations, salvage, privateering, and declared war.
  - Define victim, witness, attribution, jurisdiction, stolen-property, mission,
    and retaliation rules before assigning reputation consequences.
  - Define how piracy affects the offender, asset owner, controller, victim,
    territorial authority, and informed third parties without adding automatic
    economic guilt by association.
  - Coordinate with combat in `TASK-046`, player knowledge in `TASK-020`, and
    the accepted relational gameplay model before choosing simulation state or
    commands.
  - Context: [Relational gameplay model](factions.md)

- [ ] **TASK-037: Version content catalogs and migrate saved content references**
  - Begin after `TASK-022` selects save versioning, `TASK-023` selects the
    gameplay content format, and completed `TASK-063` supplies the resolved
    catalog and qualified-reference boundary.
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

- [ ] **TASK-046: Define combat resolution**
  - Define combat orders, targeting policy, damage, withdrawal, surrender,
    capture, and destruction.
  - Decide how observed and unobserved combat differ without changing causal
    outcomes.
  - Build combat engagement and pursuit on the moving-ship interaction contract
    from `TASK-019`; do not introduce a separate combat-only position or motion
    model.

- [ ] **TASK-051: Define docking, undocking, and berth capacity**
  - Define approach, access validation, berth or docking-capacity allocation,
    queueing, arrival, docked state, undocking, cancellation, and failure for
    ships interacting with stations or other facilities.
  - Define how docking composes with motion, orders, loading and unloading,
    permissions, facility removal, facts, snapshots, checkpoints, and
    deterministic congestion resolution.
  - Begin after `TASK-019` supplies moving-ship interaction timing; coordinate
    access with `TASK-030`, group intent with `TASK-033`, and station ownership
    with `TASK-057`.
  - Context: [Player experience](player-experience.md) · [Economy](economy.md) · [Navigation architecture](navigation-architecture.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [ ] **TASK-052: Define standing orders and player-configurable automation**
  - Define recurring trade, mining, patrol, defense, and route intent without
    introducing a second command or order lifecycle.
  - Decide policy configuration, eligible actors, cadence and wake triggers,
    replacement and suspension, bounded failure and retry, player override,
    explanation, snapshots, and save requirements.
  - Build on `TASK-006`; coordinate group targets with `TASK-033`, knowledge
    limits with `TASK-020`, and domain-specific orders with their owning tasks.
  - Context: [Player experience](player-experience.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [ ] **TASK-054: Define general resource acquisition and deposits**
  - Evolve the acceptance-only mining source into production gameplay with
    explicit deposits or other sources, discoverability, extraction capability,
    work scheduling, depletion or renewal, interruption, output ownership, and
    resulting inventory.
  - Define content, commands, facts, snapshots, checkpoints, and deterministic
    batching without promoting the Phase 1 fixture into session authority.
  - Coordinate items and equipment with `TASK-041`, player knowledge with
    `TASK-020`, movement interactions with `TASK-019`, and autonomous selection
    with `TASK-053`.
  - Context: [Economy](economy.md) · [Simulation architecture](simulation-architecture.md) · [Runtime orchestration](runtime-orchestration.md)

- [ ] **TASK-055: Define trade, contracts, currency, prices, and markets**
  - Define purchase, sale, procurement, and transport agreements for player and
    autonomous actors, including offer identity, eligibility, acceptance,
    reservation, fulfillment, cancellation, failure, and explanation.
  - Decide currency ownership and conservation, pricing and price discovery,
    transaction atomicity, public-market versus internal logistics boundaries,
    relationship effects, snapshots, saves, and deterministic contention.
  - Begin after `TASK-041` defines tradable inventory categories; coordinate
    logistics with `TASK-009`, docking with `TASK-051`, knowledge with
    `TASK-020`, and economic facts with `TASK-032`.
  - Context: [Player experience](player-experience.md) · [Economy](economy.md) · [Factions](factions.md)

- [ ] **TASK-057: Define station composition, construction, and expansion**
  - Define persistent facilities composed from storage, production, trade,
    repair, construction, defense, and service capabilities, with explicit
    content identity and authoritative ownership.
  - Define how station construction and expansion consume material and work,
    choose location, allocate identity, change regional capacity, handle
    interruption or removal, and join commands, facts, snapshots, and saves.
  - Begin after `TASK-041` defines installed equipment; completed `TASK-063`
    supplies content catalogs. Coordinate docking with `TASK-051`, territory with
    `TASK-059`, and strategic construction with `TASK-026`.
  - Context: [Vision](vision.md) · [Player experience](player-experience.md) · [Economy](economy.md) · [Entity lifecycle](entity-lifecycle.md)

### Far term

- [ ] **TASK-027: Evaluate a broader entity storage model**
  - Reconsider ECS or another indexed model only when concrete query or scale
    evidence justifies it.

- [ ] **TASK-047: Define procedural new-game generation**
  - Generate a complete new-game composition that passes the same production
    validation and session-creation boundary as a static authored scenario;
    do not create a second initialization or authority path.
  - Define player-selectable generation inputs, constraints, failure behavior,
    reproducibility, seed and random-stream ownership, algorithm versioning,
    stable identity assignment, and independence from worker count or
    generation completion order.
  - Decide which authored definitions and scenario fragments a generator may
    select or compose without changing their content identities or silently
    manufacturing incompatible definitions.
  - Begin only after `TASK-023` establishes the static content and new-game
    composition design, `TASK-048` implements that shared boundary, and
    `TASK-066` implements the deterministic-randomness foundation designed by
    completed `TASK-021`. Procedural generation is intentionally deferred until
    later gameplay and world-shape requirements provide concrete constraints.
  - Context: [Gameplay content](gameplay-content.md) · [Deterministic randomness](deterministic-randomness.md) · [Simulation architecture](simulation-architecture.md) · [Initial roadmap](roadmap.md)

- [ ] **TASK-056: Define ship acquisition, replacement, and upgrades**
  - Define how the player and autonomous principals purchase, receive,
    construct, replace, sell, and improve ships without bypassing material
    construction, ownership, or content compatibility.
  - Define acquisition eligibility and cost, initial control and orders,
    equipment changes, transfer and failure behavior, semantic facts,
    snapshots, checkpoints, and the viability of continued one-ship play.
  - Begin after `TASK-041` defines equipment and `TASK-055` defines exchange;
    coordinate construction with `TASK-034` and combat losses with `TASK-046`.
  - Context: [Player experience](player-experience.md) · [Economy](economy.md) · [Entity lifecycle](entity-lifecycle.md)

- [ ] **TASK-058: Define repair and maintenance**
  - Define damage states that can be repaired, required facilities or field
    capabilities, material and equipment inputs, reservations, scheduled work,
    interruption, completion, failure, and resulting asset availability.
  - Preserve material causality and use ordinary orders, facts, snapshots, and
    checkpoints rather than silently restoring damaged assets.
  - Begin after damage in `TASK-046`, generalized inventory in `TASK-041`, and
    station capabilities in `TASK-057`; coordinate docking with `TASK-051`.
  - Context: [Economy](economy.md) · [Player experience](player-experience.md)

- [ ] **TASK-059: Define territory claiming and political expansion**
  - Define territory identities, boundaries, controlling authority, claim and
    loss conditions, restricted-space policy, and the player path from an
    independent principal to a territorial power without making expansion
    mandatory.
  - Define how stations, knowledge, conflict, permissions, law, facts,
    snapshots, and saves participate without deriving every right from
    directional standing.
  - Begin after player knowledge in `TASK-020`, station expansion in `TASK-057`,
    and combat in `TASK-046`; coordinate strategic ownership with `TASK-026`
    and mutable connector access with `TASK-030`.
  - Context: [Player experience](player-experience.md) · [Relational gameplay model](factions.md) · [Relational simulation architecture](relational-simulation-architecture.md)

- [ ] **TASK-060: Define deterministic compatibility across platforms and versions**
  - Decide the supported reproducibility guarantee across operating systems,
    architectures, .NET versions, and game versions, including numeric,
    collection, hashing, serialization, and algorithm-version boundaries.
  - Define compatibility evidence, canonical digest scope, expected rejection
    versus migration behavior, and how single-thread and concurrent reference
    results are compared without promising unsupported bitwise identity.
  - Build on the bit-exact integer random contract from completed `TASK-021`
    and use implementation evidence from `TASK-066` plus broader evidence from
    `TASK-029`; coordinate save and content compatibility with `TASK-022` and
    `TASK-037`.
  - Context: [Technical direction](technical-direction.md) · [Deterministic randomness](deterministic-randomness.md) · [Simulation architecture](simulation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md) · [Save format and migration](save-format-and-migration.md)

- [ ] **TASK-062: Define personnel, crew, and person-level simulation if required**
  - Begin only when a concrete gameplay need cannot be expressed through the
    accepted ship-only NPC model from `TASK-015`.
  - Decide whether captains, crew, employees, passengers, population members,
    or other person-level categories exist; do not infer any category from a
    ship merely because it is autonomous.
  - Define identity, ship and organization relationships, authority, knowledge,
    skills, employment, lifecycle, snapshots, saves, scale targets, and
    deterministic evaluation and commit before adding person-level state.
  - Coordinate dialogue with `TASK-016`, NPC decision quality with `TASK-042`,
    factions with `TASK-026`, and inventory or passenger cargo only if their
    owning contracts require it.
  - Context: [Individual NPC scope](individual-npc-scope.md) · [Vision](vision.md) · [Player experience](player-experience.md)

## Completed foundations

- [x] **TASK-021: Define random-number stream ownership**
  - Defined one resolved 256-bit session root with domain-separated generation
    and runtime scopes, versioned SHA-256 canonical derivation, and bit-exact
    `xoshiro256**` stateful streams.
  - Chose a hybrid model of stateless values keyed by stable decision and named
    sample identity plus stateful streams keyed by domain, owner, and purpose.
    No global or worker-owned stream exists.
  - Defined integer-only initial sampling, commit-only state consumption,
    script capability scoping, exact checkpoint continuation, algorithm-version
    compatibility, and independence from unrelated draws or work layout.
  - Save-scumming prevention, cryptographic secrecy, floating distributions,
    and general probability scripting remain outside the contract.
  - Implementation remains in `TASK-066`; procedural generation consumes the
    separate generation scope in `TASK-047`; cross-platform compatibility
    remains in `TASK-060`.
  - Context: [Deterministic randomness and stream ownership](deterministic-randomness.md)

- [x] **TASK-016: Design dialogue state and presentation**
  - Defined immutable dialogue definitions and authoritative conversation
    instances with stable participant bindings, nodes, choices, conditions,
    repeatability, structural memory, consequences, facts, checkpoints, and
    deterministic commit boundaries.
  - Supported ship, station, and principal respondents plus optional localized
    named-person attribution that remains presentation text rather than a
    simulated person.
  - Defined continuous foreground and suspended conversation behavior,
    deterministic pending order, response-required automatic-pause
    integration, and atomic coordination with at most one normal gameplay
    command consequence.
  - Kept explicit player and trusted-session initiation inside normal command
    admission; deferred fact-, time-, location-, and threshold-triggered
    initiation to `TASK-017`.
  - Implementation remains in `TASK-065`; player-knowledge refinement remains
    in `TASK-020`; station identity remains in `TASK-057`.
  - Context: [Dialogue state and presentation](dialogue.md)

- [x] **TASK-063: Implement the format-neutral content validation foundation**
  - Added independent content, validator, and test projects with immutable
    package, definition, static-scenario, reference, diagnostic, neutral-value,
    resolved-catalog, and fingerprint models that expose no JSON or simulation
    types.
  - Implemented strict bounded UTF-8 JSON readers and stable writers, explicit
    package document loading, trusted content-kind registration, dependency and
    reference resolution, cycle and collision detection, canonical ordering,
    immutable publication, and SHA-256 package and catalog fingerprints.
  - Added a Godot-free production validator with stable diagnostic codes and
    optional package-order, qualified-key, and fingerprint inspection.
  - Proved stable catalogs, fingerprints, and diagnostics across package and
    document order and worker counts with 27 focused tests. The full 395-test
    suite, Release build, formatting verification, Godot headless build, and all
    four canonical benchmark digests pass.
  - Built-in content and static new-game integration remain `TASK-048`; catalog
    compatibility and saved-reference migration remain `TASK-037`.
  - Context: [Gameplay content and static new-game composition](gameplay-content.md) · [Concurrency and performance](concurrency-and-performance.md)

- [x] **TASK-015: Decide the initial meaning and scope of individual NPCs**
  - Established that the initial NPC is an individually identifiable autonomous
    ship, using the existing ship, controller, order, snapshot, save, and
    deterministic commit boundaries rather than a new entity category.
  - Deferred persons, captains, crew, employees, passengers, and population to
    `TASK-062`; `TASK-042` and `TASK-053` retain the later work for ship
    decision quality and autonomous work selection.
  - Context: [Individual NPC scope](individual-npc-scope.md)

- [x] **TASK-045: Plan internationalization before game-layer expansion**
  - Defined locale selection and fallback, gettext catalog and stable-key
    ownership, typed parameter and plural formatting, localized authored
    content, right-to-left and text-expansion behavior, font coverage, and
    focused validation evidence.
  - Kept locale, translated strings, fonts, layout, and accessibility
    preferences outside deterministic simulation, snapshots, content identity,
    and authoritative saves. Preserved invariant authored fallbacks for
    headless inspection.
  - Established the localization-related accessibility baseline and assigned
    comprehensive accessibility modes and acceptance criteria to `TASK-061`.
  - Context: [Internationalization and localization](internationalization-and-localization.md)

- [x] **TASK-043: Review gameplay systems for scope gaps**
  - Reviewed the `TASK-044` inventory against the player experience, roadmap,
    gameplay, economy, faction, navigation, lifecycle, information,
    presentation, save, and simulation architecture documents.
  - Classified each unowned area as a confirmed contract gap or a deliberate
    optional, evidence-gated, or prerequisite-gated deferral. Added
    `TASK-049` through `TASK-060` for the confirmed gaps without widening
    existing task boundaries.
  - Context: [Planned game-system inventory](planned-systems.md#task-043-scope-gap-review)

- [x] **TASK-044: Inventory the planned game systems in documentation**
  - Added an exhaustive inventory of established, planned, and deliberately
    deferred systems across the player application, content, simulation,
    navigation, actors, economy, factions, conflict, narrative, and supporting
    architecture.
  - Assigned every inventory entry to an accepted design, tracked task, or
    explicit inventory deferral without treating deferred systems as
    implementation commitments. Recorded the strict single-player exclusions
    separately so they cannot be mistaken for missing systems.
  - Supplied the canonical input used by the completed `TASK-043` review of
    gameplay contract and ownership gaps.
  - Context: [Planned game-system inventory](planned-systems.md)

- [x] **TASK-023: Define gameplay content categories and format boundaries**
  - Accepted strict UTF-8 JSON behind a replaceable format adapter, one
    format-neutral validation and catalog-construction path, qualified authored
    identities, immutable disk-loaded definitions, static new-game composition,
    deterministic package composition, and no initial external executable
    content.
  - Required production-grade headless validation, canonical fingerprints for
    rapid iteration, and invariant fallback strings for headless inspection
    without making wording simulation authority or save data.
  - Format-neutral models, production validation, catalogs, and headless
    validation were completed by `TASK-063`; built-in content and static
    new-game integration remain `TASK-048`; catalog compatibility and
    saved-reference migration remain `TASK-037`; procedural new-game generation
    remains `TASK-047`.
  - Context: [Gameplay content and static new-game composition](gameplay-content.md)

- [x] **TASK-022: Select save format, versioning, and migration strategy**
  - Selected externally editable strict UTF-8 JSON, stable current-schema
    writing, bounded decoding, typed rejection, and deterministic contiguous
    one-way migration mechanics.
  - Implemented complete internal authoritative checkpoints and direct isolated
    restoration for every currently admitted `GameSession` owner, including
    cross-owner validation and continuation equivalence.
  - Added atomic same-directory file publication with durable file and directory
    synchronization, one explicit backup, portable validated slot identifiers,
    symbolic-link rejection, and cleanup that preserves the prior committed
    primary across pre-publication failures. The full 368-test suite passes.
  - `TASK-063` now supplies resolved content catalogs. General saved sessions
    remain unavailable until `TASK-048` integrates the static new-game
    composition and `TASK-037` adds catalog compatibility and saved-reference
    migration.
  - Context: [Save format, versioning, and migration](save-format-and-migration.md) · [Authoritative save boundary](authoritative-save-boundary.md)

- [x] **TASK-034: Integrate clean-session economy and transport with entity lifecycle**
  - Added a generic immutable new-game economy seed and private session-owned
    production, construction, logistics, transport, inventory, and freighter
    state without importing the Phase 1 acceptance fixture.
  - Routed economic work through the shared deterministic agenda, materialized
    completed construction through lifecycle with its scheduled event as the
    semantic cause, and preserved stable facility allocation order.
  - Removal prepares and releases affected transport commitments before cargo
    disposal. The public session no longer accepts caller-owned construction
    processes or pending materialization workflows, completing aggregate
    admission for the save boundary defined by `TASK-014`.
  - Context: [Entity lifecycle and explicit spawning](entity-lifecycle.md) · [Runtime orchestration](runtime-orchestration.md) · [Authoritative save boundary](authoritative-save-boundary.md)

- [x] **TASK-031: Migrate Phase 1 logistics to hierarchical navigation**
  - Approved and implemented the explicit Mine, Refinery, and Shipyard mapping
    to systems, anchored facility and inventory entities, initial ships, and
    directional connector endpoints. See [Navigation and spatial
    architecture](navigation-architecture.md#approved-phase-1-acceptance-mapping).
  - Added opaque `ILogisticsNavigation` reachability and duration estimates.
    Assignment and transport no longer inspect, retain, or schedule `RouteId`
    graph legs; the Phase 1 test fixture now uses hierarchical navigation.
  - The approved fixture migration removes route disruption coverage until
    `TASK-030` supplies authoritative connector availability. The acceptance
    baseline is 45 processed events, event digest `175eac5bd99a0695`, and
    final-state digest `424bec2061b0e8f9`.
  - Phase 1 remains a test-only whole-simulation acceptance fixture. Its former
    CLI entrypoint and `baseline.phase-one` benchmark are retired. `TASK-034`
    completed clean-session economic and transport composition.
  - Context: [Navigation and spatial architecture](navigation-architecture.md)

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
  - Defined aggregate admission. `TASK-034` subsequently brought construction,
    economy, and transport into the clean `GameSession` aggregate, and
    `TASK-039` cancels removed-entity movement events before checkpoint capture
    and restore implementation.
  - Save encoding and versioning were completed by `TASK-022`; versioned content
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
  - Implementation remains in `TASK-038`; completed `TASK-016` supplies the
    dialogue classification and continuity contract.
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
    atomic restoration contract for `TASK-014`. Save encoding was completed by
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
  - Clean-session economic and transport owner cleanup was completed by
    `TASK-034`; legacy spatial migration was completed by `TASK-031`.
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
    migration completed in `TASK-031`, and semantic economy facts remain in
    `TASK-032`.
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
  - `PhaseOneScenario` remains isolated under the test project's `Acceptance/`
    directory as a bounded regression harness.
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
    completed entity destinations, and Phase 1 migration completed in `TASK-031`.
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
