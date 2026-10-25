# Project task list

[Project index](../README.md) · [Gameplay integration](gameplay-integration.md) · [Inventory and cargo](inventory-and-cargo.md) · [Sensor deployables](sensor-deployables.md) · [Dialogue](dialogue.md) · [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Deterministic randomness](deterministic-randomness.md) · [Accessibility](accessibility.md) · [Runtime orchestration](runtime-orchestration.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Individual NPC scope](individual-npc-scope.md) · [Semantic game facts](semantic-game-facts.md) · [Presentation snapshots](presentation-snapshots.md) · [Entity lifecycle and explicit spawning](entity-lifecycle.md) · [Group and fleet commands](group-and-fleet-commands.md) · [Ship maneuver kinematics](ship-maneuver-kinematics.md) · [Source-code organization](source-code-organization.md) · [Scale targets and benchmarks](scale-and-benchmark-targets.md) · [Initial roadmap](roadmap.md) · [Simulation architecture](simulation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md)

This is the canonical list of project work. Design documents explain goals,
constraints, and decisions; this file records whether implementation work is
current, upcoming, deferred, or complete.

When new work is discovered, add it here even if it is not appropriate to
address immediately. Link to the relevant design document instead of copying
large design notes into the task. Move completed tasks to the completed section
rather than deleting them.

## Project status summary

This section is a compact map of completed foundations and the task that owns
each remaining decision or implementation. The task entries below remain the
source of detailed scope and acceptance criteria.

| Area | Completed foundation | Remaining ownership |
| --- | --- | --- |
| Project scope, content, and accessibility | `TASK-023`, `TASK-043`, `TASK-044`, `TASK-045`, `TASK-048`, `TASK-061`, `TASK-063`, and `TASK-083` completed the content, static-scenario, validation, scope-review, localization, comprehensive accessibility, built-in new-game, and development-preview foundations. | `TASK-077` implements the full accessibility application contract; `TASK-078` through `TASK-080` own audio, and `TASK-081` and `TASK-082` retain deferred motion and photosensitivity design. |
| NPCs | `TASK-015` established the ship-only NPC boundary. | `TASK-042` designs NPC decision quality and `TASK-053` autonomous work selection; neither introduces person-level state. |
| Dialogue and randomness | `TASK-016` completed dialogue design. `TASK-021` and `TASK-066` completed deterministic-randomness design and implementation. | `TASK-065` implements dialogue. |
| Saves and application presentation | `TASK-050` completed the preference design and `TASK-084` implemented its shared device-local store. `TASK-049` completed the application shell and minimal map, including public static topology and presentation-only galaxy coordinates. | `TASK-067` implements save-envelope display names; cross-device synchronization is out of scope. `TASK-077` implements the shell and map; `TASK-076` owns future nonpublic topology and connector discovery. |
| One-shot group commands | `TASK-033` completed the authoritative explicit-selection move and current-order cancellation boundary, including the initial formation resolver and Godot selection handoff. | `TASK-086` owns any later persistent group or fleet identity and lifecycle. |
| Inventory and economy | `TASK-041` designed generalized inventory and cargo, and `TASK-069` completed its compatible implementation. Trade uses Credits as the single unified currency. | `TASK-068` completed the [equipment and ship-slot design](equipment-and-ship-slots.md); `TASK-099` owns implementation. `TASK-055` owns trade balance, pricing, and settlement design. |
| Spatial interaction, sensors, and deployables | `TASK-019` completed moving-ship interaction design. `TASK-020` completed fog-of-war design. `TASK-074` completed the deployable, inventory, lifecycle, and sensor-handoff contract; `TASK-075` completed deployment and pickup range and action-order design. `TASK-095` completed deployable runtime, range orders, committed observation inputs and checkpoint continuity; `TASK-106` completed independent ship-definition retention. `TASK-087` completed the shared system-local coordinate-scale and motion-resolution design, while `TASK-089` and `TASK-090` completed ship thrust and maneuver-kinematics design and implementation. `TASK-071` completed shared interaction discovery, timing, deterministic evaluation, and explicit motion handoffs. | `TASK-068` defined installed-equipment capability contributions; `TASK-099` implements them; `TASK-046` retains frozen combat design and `TASK-103` tracks combat activation and active-domain continuation. `TASK-072` owns geometry, collision, and avoidance, `TASK-073` implements fog of war without a general NPC knowledge model, `TASK-107` owns player deployment/pickup controls, `TASK-094` owns player order-failure notifications, and `TASK-088` retains late-term spatial authoring guidance. |
| Application pacing | `TASK-064` completed event-responsive pacing design. | `TASK-038` implements application pause, speed, input timing, and the accepted event-responsive integration. |
| Source-code organization | `TASK-091` reorganized source and tests around the documented simulation authority, client presentation, deterministic execution, and test-harness boundaries without changing simulation behavior or public type identity. | Further namespace or assembly changes require measured need and separately approved work. |

## Current focus

- [ ] **TASK-073: Implement player fog-of-war, sensors, and scouting**
  - Promoted by the project owner on 2026-10-19. The owner selected a staged
    boundary for review, then implementation of approved stages. The linked
    design now proposes ship coverage, live ship presentation/disclosure,
    persistent observation, and full entity integration stages. The owner
    approved the boundary, stage 1, and then stage 2's live contracts and the
    proposed 10 km starter-ship and preview-courier radii on 2026-10-19.
    Station/deployable integration stays open under this task, with
    prerequisites retaining their existing owners.
  - Completed stage 1's pure ship coverage evaluator with explicit fixture
    radii, principal-owned sources, exact geometry, immutable identity sets,
    and deterministic serial source batches across existing spatial indexes.
    All 24 coverage cases pass across existing index and batch layouts.
  - Completed stage 2's explicit sensor definitions and player binding,
    filtered ship presentation/selection, safe commit-time notices, bounded
    disclosure receipts, checkpoint/restore validation, and Godot consumption.
    All 1,467 solution tests and the Release build pass; headless Godot startup
    passes. Four canonical benchmark presets pass; the connector-navigation
    mismatch also occurs at untouched HEAD and is tracked by `TASK-104`.
  - Stages 3 and 4 remain open for persistent discoveries and real station/
    deployable integration. Station design remains `TASK-057`, station runtime
    is `TASK-105`, and deployable runtime completed under `TASK-095`. Consume its
    committed views and sources here. Do not mark this task
    complete until those integrations satisfy the full accepted proof.
  - Implement the accepted `TASK-020` contracts for player-owned ship,
    station, and deployable sensor coverage, live authoritative contacts,
    transient ship disappearance, persistent structure discovery, stale
    last-observed views, refresh, and confirmed absence.
  - Cover moving-to-moving, moving-to-stationary, stationary-to-moving,
    overlapping-source, connector-emergence, creation, removal, fact-disclosure,
    checkpoint, and restore behavior without expanding `TASK-071` with sensor
    outcome policy.
  - Retain a single-thread reference path and prove identical results across
    supported worker, partition, and batch layouts. Do not add NPC fog-of-war,
    allied sensor sharing, sensor equipment modifiers, stealth, occlusion, or
    extrapolated moving contacts.
  - Provide the observer-safe non-player maneuver projection deferred by
    completed `TASK-090`, using the accepted observation boundary without
    exposing private authoritative motion state through facts or diagnostics.
  - Build on completed `TASK-020` and the spatial substrate implemented by
    `TASK-071`; coordinate station design with `TASK-057` and runtime with
    `TASK-105`, deployable design with `TASK-074` and runtime with `TASK-095`,
    equipment contributions with `TASK-068`,
    and the application surface with `TASK-049`. Do not invent either missing
    entity domain inside sensor implementation.
  - Context: [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Moving-ship interactions](moving-ship-interactions.md) · [Presentation snapshots](presentation-snapshots.md) · [Concurrency and performance](concurrency-and-performance.md)

## Near-term work

- [ ] **TASK-038: Implement application pause, speed, and input timing**
  - Started the client-side pacing controller, FIFO application-input boundary,
    response-required dialogue pause handoff, and temporary bundled
    line-oriented speed-ladder file: fixed-rate advancement is replaced,
    captured pacing actions and map commands drain in one order at the next
    completed timestamp boundary before further advancement, the Godot client
    presents local pause, step, and dynamically generated preset controls, and
    dialogue may acquire only its own temporary local pause.
  - Implemented the domain-neutral event-responsive core from completed
    `TASK-064`: stable category and subject identities, initial informational
    dialogue and player-asset offscreen-combat defaults, player-policy
    resolution with unavailable-cap warnings and safe fallback, strongest-action
    boundary batching, one-shot pause and speed caps, explainable dispositions,
    and bounded five-second monotonic sliding grace windows with explicit reset.
    Invalid batches cannot partially consume grace state.
  - Added the application-side event-pacing boundary: already-disclosed typed
    notices queue in capture order, drain as one completed-boundary batch before
    same-checkpoint manual pacing input, and retain the latest disposable local
    explanation including contributed, suppressed, and ignored outcomes.
  - Consumes persisted local pacing choices through the shared
    `TASK-084` store: client startup resolves the response-required-dialogue
    preference and event-policy overrides against the validated speed ladder,
    retains unavailable requested caps in storage while safely using defaults
    for this launch, and displays a local configuration diagnostic. The
    preference document remains outside authoritative session and save state.
  - Replace fixed real-time advancement with the accepted pacing state and
    completed-timestamp control checkpoints.
  - Retain player-facing controls that write or explicitly reset device-local
    pacing choices with `TASK-077`, which owns the complete settings surface;
    startup already loads and validates the shared local preferences.
  - Integrate response-required dialogue automatic pause using the accepted
    classification and continuity contract from completed `TASK-016`.
  - Complete the live event-responsive integration by routing owner-provided
    disclosure-safe typed notices through the application boundary. Coordinate
    typed combat-start notices with `TASK-046` rather than inventing combat
    semantics here.
  - Wire live event adapters only after their owners provide typed disclosures:
    informational dialogue remains with `TASK-065` and combat start remains with
    `TASK-046`. Keep persisted pacing overrides in the shared device-local
    preference store completed by `TASK-084`, not a separate pacing-only file.
  - Context: [Time and pacing](time-and-pacing.md)

## Future parking lot

- [ ] **TASK-107: Design and implement player deployment/pickup controls**
  - The owner selected runtime/content integration first for TASK-095. Define
    and review the player interaction before implementing deployable selection,
    placement and pickup controls through its existing command boundary.
    Player order-failure notification remains TASK-094, and observation remains
    TASK-073. This task is not part of TASK-095's runtime delivery.
  - Context: [Initial deployable delivery](sensor-deployables.md#initial-content-adaptation-and-delivery)

- [ ] **TASK-105: Implement the approved station entity runtime**
  - Begin after `TASK-057` settles station composition and lifecycle design.
    Implement its approved identity, ownership, creation/removal, observable
    state, and checkpoint contracts; do not treat existing economic facilities
    as stations or select missing station policy inside `TASK-073`.
  - Supply concrete stationary source/target inputs and typed observable views
    for `TASK-073` stages 3 and 4. Sensor capability and observable fields must
    come from the approved station design, without inheriting ship tuning.
  - Context: [Station design ownership](#mid-term) · [Fog-of-war stages](fog-of-war-and-scouting.md#approved-stages-and-completion-boundaries)

- [ ] **TASK-104: Reconcile the connector-navigation benchmark expectation**
  - The canonical `navigation.connector-volume` fixture produces
    `88e79d6112f53750` while its registered expectation is `673c9af7f54a80e4`.
    TASK-073 verification reproduced the same mismatch from an untouched
    `1a5a3d1` HEAD snapshot on 2026-10-19, independently of observation changes.
  - Investigate intended fixture behavior and digest coverage before updating
    the expectation or changing navigation. Preserve this as a visible failing
    benchmark rather than replacing its expected hash merely to pass.
  - Context: [Scale and benchmark targets](scale-and-benchmark-targets.md)

- [ ] **TASK-103: Implement combat activation and active-domain continuation**
  - Added when TASK-071 closed on 2026-10-17 to track runtime work not covered
    by TASK-046's design scope. Begin only after the relevant TASK-046 contracts
    are settled and approved; its design remains frozen until owner resumption.
  - Build on completed TASK-071: implement order-backed authorized admission,
    domain-owned engagement identity and complete membership claims, fixed-step
    agenda ownership, deterministic evaluation/commit, and exact motion renewal.
  - Integrate combat order holds, pursuit/reentry, controller authority, scripted
    overrides, removal, elapsed settlement, and release/replanning according to
    the accepted domain contract. Preserve reject-until-explicit-release guards
    until owner-approved domain transitions can coordinate every affected owner.
  - Implement cleanup of queued and in-flight stale steps, durable cadence and
    settlement correlation, domain state/checkpoints, and cross-owner restore
    validation with exact pending keys. Prove continuation and exactly-once
    outcomes through restore, membership changes, interruption, and removal.
  - Integrate current-schema saved representations with TASK-037's compatibility
    and migration boundary. Do not treat movement-component restore as full active
    engagement persistence or persist derived indexes and host worker settings.
  - Keep TASK-101/TASK-102's admission/targeting extensions separate and retain
    a serial reference path with deterministic batch/worker equivalence tests.
  - Context: [Combat activation](combat-activation.md) · [Shared runtime and deferred scope](moving-ship-interactions.md#closure-recommendation-and-deferred-scope)

- [ ] **TASK-046: Define combat resolution**
  - Design frozen by the owner on 2026-10-16 for later resumption under this
    task. Accepted contracts below remain recorded; do not continue combat design
    as a prerequisite to the shared foundation completed by TASK-071. Runtime
    activation and active-domain continuation are now tracked by TASK-103.
  - Started the combat activation design slice on 2026-10-13 after owner
    selection as TASK-071's first consuming domain. Accepted multi-ship engagement
    membership with one engagement controlling each ship, dedicated session-wide
    EngagementId, explicit fixed step preserved through restore, authorized intent
    for entry, exit before replacement navigation, and rejection of competing
    admission. TASK-101 revisits the last decision later.
  - Accepted immediate entry motion evaluation with cadence anchored to entry,
    first-valid complete admission in canonical contact/effect order, and exact-time
    materialization from the committed interval curve on exit on 2026-10-13.
  - Accepted immediate surviving-engagement motion reevaluation on mid-interval
    roster changes, prior-interval settlement before scheduled-boundary membership
    changes, and termination when no authorized combat pair remains on 2026-10-13.
    Survivors release through the exact-time handoff; original cadence is retained
    while the engagement remains viable.
  - Accepted order-backed combat intent with identity/generation, explicit complete
    create/join claims, smallest contributing eligible contact key as origin,
    and exactly-once elapsed settlement before roster changes with a durable cursor
    on 2026-10-13. Current order records are movement-only and lack a generation;
    define the combat-order/revision extension here and coordinate saved schema
    changes with TASK-037 rather than assuming existing fields.
  - Accepted Active-only combat authorization, a separate continuation condition
    that preserves intent beyond entry range, and ordinary-navigation pursuit for
    a still-valid Active combat order after disengagement on 2026-10-13. Waiting,
    suspension, and terminal work grant no authorization; replacement work wins
    and terminal orders are never revived.
  - Accepted one-actor/one-target-ship combat commands using ReplaceAll/Append
    on 2026-10-14. Combat policy may admit the hostile target without its own
    order; the attacker gains no controller or steering authority over it. Each
    additional ally requires an independent current Active combat order and
    validated controller authority, retained in complete-claim correlation.
    TASK-102 defers group/area targeting.
  - Reviewed defender-order integration on 2026-10-14: current suspension is
    script-override-specific and cancellation promotes queued work immediately.
    A combat hold/release must explicitly integrate with these owners and preserve
    controller/override authority.
  - Accepted defender work suspension with a combat-specific reason, retained
    queue/controller, defender-owned versioned response policy without an automatic
    attack order, and exact-state release revalidation on 2026-10-14. Valid retained
    work gets a fresh plan; recoverably blocked work waits with a reason/wake;
    unachievable work fails. Canceled or replaced work is never revived.
  - Reviewed override handlers on 2026-10-14: begin/end directly end local motion,
    and override end immediately resumes ordinary work after control restoration.
    Reconcile those paths and current ActorControlRevision validation with combat
    ownership.
  - Accepted override transitions during combat with current authority revalidation
    on 2026-10-14. Combat release resumes only eligible current override work while
    an override remains; override release restores base control/work but retains
    ordinary-motion hold while combat remains authorized. Each release clears only
    its own constraint; stale authority cannot resume work.
  - Reviewed checkpoint owner boundaries on 2026-10-14: orders already retain
    base/override work, control retains ActorControlRevision, and movement owns
    kinematics, but session/movement checkpoints have no active-combat component
    or combat movement variant.
  - Accepted a single combat-membership binding with derived order holds, full
    structured blocking-cause projection, and preserved exact pending step keys
    on 2026-10-14. Defined cross-owner liveness, authorization, spatial, cadence,
    settlement, and agenda-allocation restore invariants; compiled record layouts
    remain open.
  - Accepted cancellation of all queued engagement steps before removing engagement
    state on 2026-10-16, including old-generation envelopes. Defined restore rejection
    for any queued step naming an absent engagement, live-engagement stale no-ops,
    preservation of unrelated keys/allocators, and in-flight stale-work guards.
    Concrete cleanup bookkeeping, step payload, and runtime proofs remain open.
  - Settle combat order revision/schema, defender response mechanics/policy selection,
    compiled owner/checkpoint/projection layouts and cleanup bookkeeping,
    combat-aware preflight/order transitions,
    completion/expiry/retargeting, continuation triggers/reentry,
    bounded pursuit and idle/coasting policy, discrete outcomes/cooldowns/
    accumulation, and checkpoint/fact/projection contracts before TASK-103
    implements the consuming domain. TASK-071 completed the shared handoff. Collision and docking decisions remain
    deferred with TASK-072 and TASK-051.
  - Define combat orders, targeting policy, damage, withdrawal, surrender,
    capture, and destruction.
  - Decide how observed and unobserved combat differ without changing causal
    outcomes.
  - Build combat engagement and pursuit on the moving-ship interaction contract
    from completed `TASK-019`; do not introduce a separate combat-only position
    or motion model.
  - Use the owner-selected VRO reference for complementary ship roles,
    target-specific weapons, and fleet support. Exact mechanics and tuning
    remain for owner review.
  - Context: [Combat activation and movement handoff](combat-activation.md) · [Ship and fleet balance direction](ship-and-fleet-balance.md)

- [ ] **TASK-102: Define group and area combat targeting**
  - The owner's 2026-10-14 first combat command is one acting ship against one
    explicit target ship using ordinary placement. Keep group and area targeting
    out of that initial TASK-046 payload.
  - Later define explicit-selection/group admission, controller validation,
    partial or atomic command results, target-selection and area semantics,
    order correlation, and deterministic outcomes before extending combat commands.
    Preserve independent per-ship order ownership and TASK-101's separate review
    of engagement transfer/merging; do not infer either from a selection UI.
  - Context: [Combat command and participant authority](combat-activation.md#first-combat-order-command-and-participant-authority) · [Group commands](group-and-fleet-commands.md).

- [ ] **TASK-101: Revisit competing combat-engagement admission**
  - Added at the owner's request on 2026-10-13. The initial TASK-046 contract
    rejects admission of an engaged ship to a different engagement; each ship
    retains one movement-controlling engagement.
  - Revisit the gameplay need for admission rejection, deliberate transfer,
    engagement merging, or another policy after the first combat lifecycle has
    evidence. Define deterministic membership conflicts, kinematic handoff,
    queued-step invalidation, intent continuity, and checkpoint continuation for
    any approved change. Do not introduce shared motion ownership implicitly.
  - Context: [Combat activation](combat-activation.md#accepted-owner-decisions).

- [ ] **TASK-100: Investigate repeated exact maneuver-query allocations**
  - Profile the dense mixed swept-query fixture, which allocates about one GB
    per 100-query iteration at 2,500 ships. Identify repeated trajectory and
    boundary preparation costs before choosing an optimization. Preserve exact
    continuous geometry, committed resets, contact flags, immutable ownership,
    and worker-count invariance; measure allocation as well as time.
  - Context: [Interaction batch measurements](interaction-batch-measurements.md).

These items are intentionally retained without implying that they should be
worked on now. Promote an item to current or near-term work when its
prerequisites and desired behavior are sufficiently defined.

The parking-lot horizons organize deferred work by likely sequencing. A task
in the **Near term** parking-lot section remains deferred; it is not promoted to
the project-level **Near-term work** section above.

- [ ] **TASK-099: Implement equipment loadouts and refit integration**
  - Implement the accepted `TASK-068` definitions, stable instance/slot custody,
    compatibility, fixed removal, condition curves, enhancement profiles,
    capability composition/replanning, facts, disclosure, checkpoints, and
    deterministic validation criteria through the shared content/session path.
  - Implement normal queued refit intent with no advance reservations and atomic
    execution-time validation, one final loadout publication, causal outcomes,
    event-driven invalidation, and the accepted control-override boundary.
  - Coordinate station service/settlement contracts with `TASK-097` and
    `TASK-055`, observation integration with `TASK-073`, and notifications with
    `TASK-094`. Do not invent missing station, combat, repair, or UI policy;
    fitting-screen UI design remains `TASK-098` and ship-loss disposition `TASK-096`.
  - Retain single-thread execution and prove identical state, IDs, resources,
    order outcomes, motion, facts, and save continuation across supported worker,
    partition, and batch layouts; enable concurrency only with benchmark evidence.
  - Context: [Equipment and ship slots](equipment-and-ship-slots.md)

- [ ] **TASK-098: Design the fitting-screen UI**
  - Design the selected-ship/station refit screen, which opens immediately when
    the player requests a refit and applies a temporary local pause.
  - Capture and restore the complete pre-screen pause/speed state on approval,
    cancellation, and exit: manual pause remains manual pause; a running game
    returns to its previous speed. Coordinate pacing ownership with `TASK-038`.
  - Provide a local draft with no authoritative changes, explicit approval that
    submits ordinary queued refit intent, and cancellation/exit even for an
    invalid draft. Distinguish approval from later instantaneous execution.
  - Support reversing equipment choices within a fitting session when parts
    are available, grouped slots, hull/slot sizes, multi-slot occupancy,
    mandatory-group validation, tiers, and modification tradeoffs. Avoid a
    giant finished-equipment market or excessive fitting-budget complexity.
  - Decide previews for capability changes, compatibility, condition, component
    and Credit settlement, and typed rejection/cancellation explanations;
    cover queued or invalidated intent without giving the UI simulation authority.
  - Coordinate availability and settlement disclosures with `TASK-097`, outcomes
    with `TASK-094`, observer-safe fields with `TASK-073`, and localization and
    accessibility with their accepted contracts. Keep UI design separate from
    equipment implementation; detailed interactions require owner review.
  - Context: [Equipment and ship slots](equipment-and-ship-slots.md) · [Time and pacing](time-and-pacing.md) · [Accessibility](accessibility.md)

- [ ] **TASK-097: Design station refitting and equipment reclamation services**
  - Own station details moved out of `TASK-068`: ships refit only at stations
    with the requisite service module, using the shared equipment vocabulary.
    Coordinate station/module composition with `TASK-057` and docking with
    `TASK-051`; ship slots remain ship-specific.
  - Retain the owner's direction: stations construct equipment directly onto
    ships from component inputs rather than selling individual finished parts.
    Removed equipment is dismantled into building parts sold to the station.
    Recovery and prices should allow experimentation without large losses.
  - Permit restoring a removed equipment choice within the same fitting session
    when enough parts are available. Decide recipe/recovery quantities,
    damaged-equipment reclamation, modification rebuilding, and net settlement
    with `TASK-055`, preserving material causality and no duplicated value.
  - Define station service identity, ship/size eligibility, docking or proximity,
    requesting authority, ownership changes, source/destination inventories,
    resource availability, prices/quotes, provider capacity, and service removal.
  - Supply an atomic preparation/validation/commit contract for the equipment
    owner, inventory, and Credit settlement. Fitting is instantaneous for now,
    can be queued, and cancels with notification when eligibility is lost.
    Keep whole-refit rejection atomic and preserve accepted deterministic priority.
  - Consume the accepted reservation-free Q15 policy: no draft/queued refit
    reservations; validate resources and settlement atomically at execution,
    respecting other workflows' commitments. Define invalidation triggers,
    typed outcomes, facts, checkpoints,
    restoration, and deterministic contention without silently adding advance
    reservations or work duration. UI remains `TASK-098`.
  - Context: [Equipment and ship slots](equipment-and-ship-slots.md) · [Inventory and cargo](inventory-and-cargo.md) · [Economy](economy.md)

- [ ] **TASK-096: Define installed-equipment disposition on ship destruction**
  - After ship destruction is implemented through combat and lifecycle work,
    decide installed-item disposition, pending fitting-order cleanup, and any
    resource or settlement commitments without silently creating salvage.
  - Preserve atomic owner cleanup, causal facts, and checkpoint continuity.
    Equipment destroyed while its ship remains live stays installed and
    nonfunctional under `TASK-068`; this follow-up owns ship-loss disposition.
  - Context: [Equipment and ship slots](equipment-and-ship-slots.md) · [Entity lifecycle](entity-lifecycle.md)

- [ ] **TASK-094: Define and implement player order-failure notifications**
  - Provide a player-facing notification when an accepted order fails,
    including deploy and pickup orders from `TASK-075` and fitting cancellation
    or failure from `TASK-068`, using authoritative
    order outcomes and typed failure reasons.
  - Defer notification policy and presentation until owner review; preserve
    semantic failure facts and terminal order state independently of this UI.
  - Context: [Sensor deployables](sensor-deployables.md#execution-and-failure) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Semantic game facts](semantic-game-facts.md)


- [ ] **TASK-032: Define semantic economy facts**
  - Define gameplay-facing production, construction, and logistics lifecycle
    facts after `TASK-009` establishes their commit owners.
  - Coordinate construction completion with the entity-lifecycle contract from
    `TASK-011`; do not expose Phase 1 diagnostic records as semantic facts.
  - Preserve typed causes, deterministic proposal ordering, bounded retention,
    and the existing distinction between facts and internal effects.
  - Context: [Semantic game facts](semantic-game-facts.md) · [Runtime orchestration](runtime-orchestration.md)

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
    with `TASK-049`, station identity with `TASK-057`, and the narrow
    fog-of-war predicates accepted by completed `TASK-020`.
  - Context: [Dialogue](dialogue.md) · [Gameplay content](gameplay-content.md) · [Time and pacing](time-and-pacing.md) · [Concurrency and performance](concurrency-and-performance.md)

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
  - Coordinate the NPC categories with `TASK-015`, retain the authoritative NPC
    read boundary and player-only fog-of-war accepted by completed `TASK-020`,
    and coordinate faction objectives and order generation with `TASK-026`.

- [ ] **TASK-053: Define autonomous ship work selection**
  - Define how non-player ships discover, evaluate, select, and stop ordinary
    work through the same commands and order lifecycle used by player intent.
  - Separate ship-local policy from faction strategy, define authoritative
    policy ownership, stable inputs, cadence, contention and tie-breaking,
    bounded retry, explanation, snapshots, and save requirements.
  - Build on `TASK-006` and the production domain owners from `TASK-009` and
    `TASK-034`; retain the authoritative autonomous-read boundary accepted by
    completed `TASK-020`, and coordinate faction objectives with `TASK-026`
    and bounded competence with `TASK-042`.
  - Context: [Vision](vision.md) · [Player experience](player-experience.md) · [Runtime orchestration](runtime-orchestration.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

### Mid term

- [ ] **TASK-086: Define persistent groups and fleet lifecycle**
  - Define any durable group or fleet identity, membership creation and edit
    authority, membership revisions, order correlation, cancellation,
    replacement, member loss, snapshots, checkpoints, saves, and semantic facts
    without reusing client-local selection as authoritative state.
  - Build on completed `TASK-033` one-shot commands and `TASK-006` per-ship
    order ownership. Do not introduce a second order queue, networking,
    replication, remote authority, prediction, or rollback behavior.
  - Context: [Group and fleet commands](group-and-fleet-commands.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Presentation snapshots](presentation-snapshots.md)

- [ ] **TASK-067: Add player-visible save names to the strict save envelope**
  - Extend the versioned `TASK-022` save envelope with the non-authoritative,
    player-editable display name accepted by `TASK-050`.
  - Define and implement strict current-schema validation and deterministic
    migration behavior for existing saves without inventing an authoritative
    value, changing compatibility decisions, or affecting continuation.
  - Keep the display name separate from slot-path validation, save identity,
    and all authoritative checkpoint state. Coordinate discovery and stale-file
    revision tokens with the application implementation of `TASK-050`.
  - Context: [Save slots, autosave, and local preferences](save-slots-and-local-preferences.md) · [Save format and migration](save-format-and-migration.md)

- [ ] **TASK-077: Implement the desktop application shell and minimal map**
  - Implement the accepted `TASK-049` application contract in the Godot client:
    validated new-game and load admission, active-session and leave flows,
    safe failure states, public-static galaxy topology, system maps, local
    camera state, generic selection and inspection, overlays, and activity
    presentation.
  - Replace the hard-coded demonstration-session lifecycle without making
    Godot an authoritative simulation owner. Consume only immutable
    presentation snapshots, semantic facts, validated content, and approved
    save interfaces; retain selection, focus, camera, and feed state locally.
  - Implement the accepted `TASK-061` standard-surface semantics, focus,
    semantic map list, remapping safety, visual modes, settings, and evidence
    hooks as part of the application rather than redefining their behavior.
    Do not absorb audio from `TASK-078` through `TASK-080`, reduced-motion
    design from `TASK-081`, or photosensitivity design from `TASK-082`.
  - Build on the production static new-game composition completed by
    `TASK-048`; begin after `TASK-037` provides saved-content compatibility,
    `TASK-038` provides
    pacing checkpoints, and `TASK-073` provides fog-of-war presentation
    records. Consume `TASK-040` recovery outcomes and `TASK-061` accessibility
    behavior without redefining either contract.
  - Context: [Application shell and map experience](application-shell-and-map-experience.md) · [Presentation snapshots](presentation-snapshots.md) · [Gameplay content](gameplay-content.md) · [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Time and pacing](time-and-pacing.md)

- [ ] **TASK-078: Define the initial supplemental audio presentation**
  - Define the non-authoritative audio categories, visible-event mapping,
    mixing and mute behavior, content ownership, lifecycle, and device-local
    volume preferences for the initial Windows and macOS application.
  - Keep audio supplemental to visible presentation without adding simulation,
    snapshot, checkpoint, or save authority. Coordinate application integration
    with `TASK-077`, local preferences with completed `TASK-050`, and the
    accessibility boundary with `TASK-061`.
  - `TASK-079` implements the accepted foundation. `TASK-080` separately owns
    optional accessibility audio cues after that implementation exists.
  - Context: [Accessibility](accessibility.md) · [Application shell and map experience](application-shell-and-map-experience.md) · [Save slots and local preferences](save-slots-and-local-preferences.md)

- [ ] **TASK-079: Implement the initial supplemental audio presentation**
  - Implement the accepted `TASK-078` categories, visible-event mapping,
    mixing, mute, content, lifecycle, and device-local preference contracts for
    Windows and macOS.
  - Verify that audio remains supplemental to visible presentation and does not
    enter simulation, snapshots, checkpoints, or saves.
  - Coordinate application integration with `TASK-077` and preference storage
    with completed `TASK-050`. Do not absorb the optional accessibility cue
    decisions retained by `TASK-080`.
  - Context: [Accessibility](accessibility.md) · [Application shell and map experience](application-shell-and-map-experience.md) · [Save slots and local preferences](save-slots-and-local-preferences.md)

- [ ] **TASK-080: Define supplemental accessibility audio cues**
  - After `TASK-079` provides the initial audio foundation, decide which visual
    events benefit from optional supplemental audio cues and whether each cue
    uses tones, speech, or another approved presentation.
  - Define settings, redundant visible equivalents, priority, interruption,
    overlap, and acceptance evidence without turning a cue into authoritative
    state or the sole carrier of gameplay information.
  - Coordinate screen-reader output with `TASK-061` and future event owners so
    cues do not duplicate, mask, or disclose information unavailable through
    the accepted presentation boundary.
  - Context: [Accessibility](accessibility.md) · [Semantic game facts](semantic-game-facts.md) · [Presentation snapshots](presentation-snapshots.md)

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
  - Coordinate with combat in `TASK-046`, player visibility from completed
    `TASK-020`, and the accepted relational gameplay model before choosing
    simulation state or commands.
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
  - Define compatibility and migration for the qualified maneuver-design
    references saved by completed `TASK-090`, rejecting an incompatible
    reference before session publication.
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

- [ ] **TASK-051: Define docking, undocking, and berth capacity**
  - Owner explicitly deferred docking decisions on 2026-10-13 while combat
    defines the first TASK-071 activation contract. Preserve this task for later.
  - Define approach, access validation, berth or docking-capacity allocation,
    queueing, arrival, docked state, undocking, cancellation, and failure for
    ships interacting with stations or other facilities.
  - Define how docking composes with motion, orders, loading and unloading,
    permissions, facility removal, facts, snapshots, checkpoints, and
    deterministic congestion resolution.
  - Build on the moving-ship interaction timing from completed `TASK-019`;
    coordinate access with `TASK-030`, group intent with `TASK-033`, and station
    ownership with `TASK-057`.
  - Context: [Player experience](player-experience.md) · [Economy](economy.md) · [Navigation architecture](navigation-architecture.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [ ] **TASK-052: Define standing orders and player-configurable automation**
  - Define recurring trade, mining, patrol, defense, and route intent without
    introducing a second command or order lifecycle.
  - Decide policy configuration, eligible actors, cadence and wake triggers,
    replacement and suspension, bounded failure and retry, player override,
    explanation, snapshots, and save requirements.
  - Build on `TASK-006`; coordinate group targets with `TASK-033`, player-facing
    fog-of-war with completed `TASK-020`, and domain-specific orders with their
    owning tasks.
  - Context: [Player experience](player-experience.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [ ] **TASK-054: Define general resource acquisition and deposits**
  - Evolve the acceptance-only mining source into production gameplay with
    explicit deposits or other sources, discoverability, extraction capability,
    work scheduling, depletion or renewal, interruption, output ownership, and
    resulting inventory.
  - Define content, commands, facts, snapshots, checkpoints, and deterministic
    batching without promoting the Phase 1 fixture into session authority.
  - Coordinate item design with completed `TASK-041`, inventory behavior with
    completed `TASK-069`, equipment with `TASK-068`, resource discoverability
    with completed `TASK-020`, completed movement-interaction design from
    `TASK-019`, and autonomous selection with `TASK-053`.
  - Context: [Economy](economy.md) · [Simulation architecture](simulation-architecture.md) · [Runtime orchestration](runtime-orchestration.md)

- [ ] **TASK-055: Define trade, contracts, Credits, prices, and markets**
  - Define purchase, sale, procurement, and transport agreements for player and
    autonomous actors, including offer identity, eligibility, acceptance,
    reservation, fulfillment, cancellation, failure, and explanation.
  - Use the single unified currency Credits. Define Credit balance ownership
    and conservation, pricing and price discovery, transaction atomicity,
    public-market versus internal logistics boundaries, relationship effects,
    snapshots, saves, and deterministic contention. Credits are not physical
    inventory and consume no cargo capacity.
  - Build on the tradable inventory categories defined by completed `TASK-041`
    and their implementation in completed `TASK-069`; coordinate logistics
    with `TASK-009`, docking with `TASK-051`, stale observed market state with
    completed `TASK-020`, and economic facts with `TASK-032`.
  - Context: [Player experience](player-experience.md) · [Economy](economy.md) · [Factions](factions.md)

- [ ] **TASK-057: Define station composition, construction, and expansion**
  - Define persistent facilities composed from storage, production, trade,
    repair, construction, defense, and service capabilities, with explicit
    content identity and authoritative ownership.
  - Define how station construction and expansion consume material and work,
    choose location, allocate identity, change regional capacity, handle
    interruption or removal, and join commands, facts, snapshots, and saves.
  - Station refit/reclamation service design is tracked in `TASK-097`, using
    the shared equipment vocabulary without putting station details in `TASK-068`.
    Runtime implementation of the approved entity contract is tracked by
    `TASK-105`; this task retains station design ownership.
  - Begin after `TASK-068` defines installed equipment; completed `TASK-063`
    supplies content catalogs. Coordinate docking with `TASK-051`, territory with
    `TASK-059`, and strategic construction with `TASK-026`.
  - Context: [Vision](vision.md) · [Player experience](player-experience.md) · [Economy](economy.md) · [Entity lifecycle](entity-lifecycle.md)

### Far term

- [ ] **TASK-081: Define reduced-motion behavior for concrete visual effects**
  - After an owning application or visual-effects task proposes motion,
    animation, automatic camera movement, or scrolling presentation, inventory
    those effects and define which stop, simplify, or remain when reduced
    motion is enabled.
  - Define the device-local preference, immediate application behavior,
    interaction with pacing and focus, and XAG 117 acceptance evidence without
    changing simulation time or authoritative outcomes.
  - Complete before any affected visual effect is accepted for release. Keep
    implementation with the task that owns each effect rather than creating a
    second presentation owner.
  - Context: [Accessibility](accessibility.md) · [Application shell and map experience](application-shell-and-map-experience.md) · [Time and pacing](time-and-pacing.md)

- [ ] **TASK-082: Define flashing and photosensitivity safeguards**
  - Before an owning task introduces blinking, flashing, strobing, or rapid
    high-contrast changes, inventory the proposed effects and define safe
    defaults, prohibited behavior, any player-facing controls, and XAG 118
    acceptance evidence.
  - Keep the safeguards device-local and presentation-only. They must not
    change simulation timing, semantic facts, authoritative state, or saves.
  - Complete before any affected effect is accepted for release, with
    implementation remaining in the task that owns that effect.
  - Context: [Accessibility](accessibility.md) · [Application shell and map experience](application-shell-and-map-experience.md)

- [ ] **TASK-088: Define late-term system-local coordinate authoring guidance**
  - After the coordinate-scale contract and its principal gameplay consumers
    are established, document representative placement patterns and examples
    for ships, stations, connector endpoints, resource sites, and later spatial
    content.
  - Help authors choose believable and numerically safe separations without
    adding warnings for implausible but otherwise valid arrangements or
    changing strict format, reference, conversion, and coordinate-envelope
    validation.
  - Build on `TASK-087` and measured experience from `TASK-071`, `TASK-072`,
    `TASK-073`, and `TASK-075`; do not block those tasks on late-term authoring
    guidance.
  - Context: [Authoritative system-local coordinate scale](system-local-coordinate-scale.md) · [Gameplay content](gameplay-content.md)

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
    composition design, completed `TASK-048` implements that shared boundary, and
    completed `TASK-066` provides the deterministic-randomness foundation designed by
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
  - Begin after `TASK-068` defines equipment and `TASK-055` defines exchange;
    coordinate construction with `TASK-034` and combat losses with `TASK-046`.
  - Balance reference: [Ship and fleet roles](ship-and-fleet-balance.md),
    preserving continued one-ship viability alongside fleet specialization.
  - Context: [Player experience](player-experience.md) · [Economy](economy.md) · [Entity lifecycle](entity-lifecycle.md)

- [ ] **TASK-058: Define repair and maintenance**
  - Define damage states that can be repaired, required facilities or field
    capabilities, material and equipment inputs, reservations, scheduled work,
    interruption, completion, failure, and resulting asset availability.
  - Preserve material causality and use ordinary orders, facts, snapshots, and
    checkpoints rather than silently restoring damaged assets.
  - Begin after damage in `TASK-046`, generalized inventory in `TASK-041`,
    equipment in `TASK-068`, and station capabilities in `TASK-057`; coordinate
    docking with `TASK-051`.
  - Context: [Economy](economy.md) · [Player experience](player-experience.md)

- [ ] **TASK-059: Define territory claiming and political expansion**
  - Define territory identities, boundaries, controlling authority, claim and
    loss conditions, restricted-space policy, and the player path from an
    independent principal to a territorial power without making expansion
    mandatory.
  - Define how stations, knowledge, conflict, permissions, law, facts,
    snapshots, and saves participate without deriving every right from
    directional standing.
  - Begin after fog-of-war implementation in `TASK-073`, station expansion in
    `TASK-057`, and combat in `TASK-046`; coordinate strategic ownership with
    `TASK-026` and mutable connector access with `TASK-030`.
  - Context: [Player experience](player-experience.md) · [Relational gameplay model](factions.md) · [Relational simulation architecture](relational-simulation-architecture.md)

- [ ] **TASK-060: Define deterministic compatibility across platforms and versions**
  - Decide the supported reproducibility guarantee across operating systems,
    architectures, .NET versions, and game versions, including numeric,
    collection, hashing, serialization, and algorithm-version boundaries.
  - Define compatibility evidence, canonical digest scope, expected rejection
    versus migration behavior, and how single-thread and concurrent reference
    results are compared without promising unsupported bitwise identity.
  - Build on the bit-exact integer random contract from completed `TASK-021`
    and use implementation evidence from completed `TASK-066` plus broader evidence from
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

- [ ] **TASK-070: Evaluate sub-`1x` player-controlled simulation speeds**
  - Revisit whether the configurable running-speed ladder should permit
    positive multipliers below `1x`, while retaining pause as a separate
    application state and preserving deterministic simulation outcomes.
  - Decide the player-facing need, default presets, speed-step behavior around
    pause and `1x`, configuration validation, and interaction with automatic
    pacing requests before changing the accepted `TASK-013` contract.
  - The current accepted minimum running speed remains `1x` unless this task is
    promoted and approved.
  - Context: [Time and pacing](time-and-pacing.md)

- [ ] **TASK-072: Define ship geometry, physical collision, and avoidance**
  - Owner explicitly deferred collision and avoidance decisions on 2026-10-13
    while combat defines the first TASK-071 activation contract.
  - Decide whether ships remain point positions for generic movement or have
    authoritative two-dimensional occupied geometry, and whether ships may
    pass through one another when no gameplay domain requests an interaction.
  - Define collision discovery, avoidance intent and priority, overlap or
    impact resolution, motion interruption and replanning, simultaneous
    contacts, fixed-step participation, connector boundaries, facts,
    snapshots, checkpoints, saves, and deterministic batching if physical
    collision becomes authoritative.
  - Build on the moving-range and swept-path contract from completed `TASK-019`
    and its implementation in `TASK-071`. Do not widen either task with
    collision policy before this task is promoted and approved.
  - Context: [Moving-ship interactions](moving-ship-interactions.md) · [Navigation architecture](navigation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md)

- [ ] **TASK-076: Define nonpublic galaxy topology and connector discovery**
  - Define which system and connector identities, names, presentation
    positions, and route relationships can be nonpublic; how the player
    discovers, refreshes, retains, or loses that information; and which
    observations persist across saves.
  - Define observer-scoped topology snapshots, map behavior for unknown or
    stale topology, semantic facts, restore behavior, and deterministic
    disclosure without leaking hidden systems or connections through entity
    coverage, presentation layout, route previews, or local map history.
  - Build on the public-static-topology baseline in `TASK-049`; coordinate
    player observation with completed `TASK-020` and its `TASK-073`
    implementation, scenario content completed by `TASK-048`, and saved-content
    compatibility with `TASK-037`. Do not change the initial public map until
    this task is promoted and accepted.
  - Context: [Application shell and map experience](application-shell-and-map-experience.md) · [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Gameplay content](gameplay-content.md) · [Presentation snapshots](presentation-snapshots.md)

## Completed foundations

- [x] **TASK-095: Implement sensor deployables and deployment/pickup orders**
  - Completed the owner-approved runtime/content delivery on 2026-10-26:
    Satellite and Advanced Satellite definitions, explicit initial cargo in the
    built-in game and development preview, and independent ship pickup ranges.
  - Implemented atomic item/entity transactions, shared stationary range
    interests, moving approach execution, ReplaceAll/Append, execution-time
    revalidation, terminal outcomes and deterministic command-order contention.
    Actions reserve nothing and execute at most once per ship per evaluation wave.
  - Added typed committed views/sources, safe historical notices, direct
    checkpoint continuation and idempotency. TASK-106 records the independent
    definition retention shared by starting and constructed ships.
  - Verified 1,570 Release tests, a clean Release build, both content packages,
    Godot headless build/startup, six existing canonical benchmark digests and
    stationary serial/worker comparisons. TASK-104's known navigation digest
    mismatch remains visible and unchanged.
  - Observation integration remains TASK-073, player controls TASK-107, failure
    notifications TASK-094, and general encoded save admission TASK-037.
  - Context: [Runtime delivery and verification](sensor-deployables.md#runtime-delivery-and-verification) · [Shared range measurements](interaction-session-measurements.md#task-095-fixed-position-extension)

- [x] **TASK-106: Retain setup-only ship definitions at the checkpoint boundary**
  - Completed within TASK-095 on 2026-10-26 after owner approval. One independent
    catalog retains definitions for starting and constructed ships; construction
    policies reference design IDs instead of owning post-construction operation.
  - Proved capture, direct restore, movement and recapture without producing
    policy, facility aggregate or production history, plus missing/conflicting
    definition rejection. Initial deployable cargo restores from retained sensor
    definitions. All 1,500 solution tests pass; general encoded save admission
    and catalog migration remain TASK-037. TASK-095 records the completed runtime
    delivery.
  - Context: [Independent ship definitions](entity-lifecycle.md#construction-materialization-handoff)

- [x] **TASK-071: Implement moving-ship interaction discovery and timing**
  - Completed on 2026-10-17 with owner acceptance of the shared-foundation scope:
    exact continuous and instant discovery, durable session-wide interests,
    invalidation and completion correlation, canonical candidate reduction,
    deterministic scheduling, stable domain evaluation, and serial effects.
  - Added immutable prepared scopes, caller-selected scan/grid/hierarchy queries,
    opt-in bounded pure worker batches, deterministic failure handling, and
    retained benchmark fixtures, digests, and measurement reports.
  - Added exact controlled intervals, atomic participant entry/update/release and
    analytic cancellation handoff, component checkpoints, guarded session APIs,
    interval-end advancement stops, mutation exclusion, and snapshot projection.
  - All 1,406 solution tests pass; Release build has zero warnings/errors.
    Active-domain scheduling, gameplay membership/order policy, and full active
    session persistence are not implemented here: design stays frozen under
    TASK-046 and implementation is tracked by TASK-103. Save compatibility stays
    TASK-037; allocation investigation stays TASK-100. TASK-101/TASK-102 retain
    later admission and targeting decisions; collision/docking remain TASK-072/TASK-051.
  - Context: [Shared-runtime acceptance and deferred scope](moving-ship-interactions.md#closure-recommendation-and-deferred-scope)

- [x] **TASK-068: Define equipment, ship slots, installation, and removal**
  - Completed the owner-approved design on 2026-10-06: discrete equipment
    identity, grouped stable hull slots, exact-size multi-slot occupancy,
    explicit removal policy, mandatory-group validation, and reservation-free
    atomic refitting with one final loadout publication.
  - Defined deterministic damage curves/fact cadence, bounded enhancement
    profiles, typed capability composition/replanning, queue invalidation,
    observer-safe disclosure, checkpoint continuity, and acceptance criteria.
  - Implementation remains `TASK-099`; station services are `TASK-097`,
    fitting-screen UI is `TASK-098`, repair remains `TASK-058`, and ship-loss
    equipment disposition remains `TASK-096`.
  - Context: [Equipment and ship slots](equipment-and-ship-slots.md)

- [x] **TASK-075: Define deployable deployment and pickup ranges**
  - Confirmed on 2026-10-01: item-defined 2,500-meter deployment range and
    ship-defined 5,000-meter pickup range, both inclusive, with out-of-range
    orders moving into range before acting.
  - Defined moving execution, revalidation without reservations, terminal
    failure on lost eligibility, immediate queue progression on success,
    deterministic contention, and facts, snapshot, checkpoint/save, and
    verification requirements. Implementation remains `TASK-095`; player
    failure notification remains deferred to `TASK-094`.
  - Context: [Sensor deployables](sensor-deployables.md#deployment-and-pickup-range-design-task-075)

- [x] **TASK-093: Apply a provisional course-turn threshold**
  - Added a temporary 50-meter Euclidean threshold for exact-rest off-heading
    moves. At or beyond it, the ship turns toward its resolved course before
    translation, including when rounded non-cardinal primary thrust is
    unavailable and precision translation remains necessary.
  - Kept sub-50-meter precision adjustments heading-preserving. The threshold
    is explicitly provisional and will be replaced by a ship-size-derived rule
    when `TASK-072` defines ship geometry.
  - Context: [Ship thrust, maneuver kinematics, and short moves](ship-maneuver-kinematics.md) · [Authoritative system-local coordinate scale](system-local-coordinate-scale.md)

- [x] **TASK-092: Render heading-aware Godot ship glyphs**
  - Replaced each system-map ship dot with a centered equilateral triangle.
    Its tip follows the immutable authoritative local heading after the map's
    existing screen-coordinate projection, without changing simulation state
    or ship selection behavior.
  - Added framework-independent client geometry coverage for all cardinal
    headings. Client tests, the Godot build, and headless preview startup pass.
  - Context: [Presentation snapshots](presentation-snapshots.md) · [Source-code organization](source-code-organization.md)

- [x] **TASK-091: Reorganize source and tests by ownership boundary**
  - Reorganized simulation production and tests around kernel, session, actor,
    spatial, economy, entity, relationship, persistence, loading,
    presentation, and diagnostic ownership while preserving the
    `GalaxyCommand.Simulation` namespace and public type identities.
  - Extracted framework-independent application behavior into
    `GalaxyCommand.Client`, reduced Godot to its rendering and input adapters,
    and created dedicated client, validator, and benchmark test projects with
    direct production references and no linked-source compilation.
  - Split the large fact, relationship, spatial-navigation, checkpoint, and
    runtime-coordinator files along their documented responsibilities. Final
    validation passed all 976 tests, including deterministic simulation and
    benchmark-configuration coverage, plus the Godot build and headless
    startup. No follow-up task remains.
  - Context: [Source-code organization](source-code-organization.md)

- [x] **TASK-090: Implement ship thrust, maneuver kinematics, and short moves**
  - Completed fixed-point authored and effective base capabilities, heading and
    velocity state, deterministic analytic planning, sub-cruise and cruise
    movement, fly-through routes, cancellation and replacement, facts,
    snapshots, checkpoints, connector approaches, and optional final headings.
  - Completed focused numeric-boundary, event-phase, checkpoint, diagnostic,
    semantic-fact, and deterministic-layout validation. Installed-equipment
    contributions, interaction-owned fine-grained activation, observer-safe
    projection, and saved-reference migration remain with `TASK-068`,
    `TASK-071`, `TASK-073`, and `TASK-037`, respectively.
  - Context: [Ship thrust, maneuver kinematics, and short moves](ship-maneuver-kinematics.md) · [Authoritative system-local coordinate scale](system-local-coordinate-scale.md) · [Navigation architecture](navigation-architecture.md) · [Moving-ship interactions](moving-ship-interactions.md) · [Concurrency and performance](concurrency-and-performance.md)

- [x] **TASK-089: Define ship thrust, maneuver kinematics, and short moves**
  - Confirmed authored mass, acceleration, speed caps, turn rate, spool duration,
    optional passive deceleration, fixed-point units, mass scaling, derived
    primary and precision acceleration, and open-ended typed equipment
    contributions without cargo mass, fuel, or damage-driven mass changes.
  - Confirmed clockwise degree-based heading, independent turning and velocity,
    analytic piecewise-constant maneuver phases, passive drag, fastest-arrival
    and shortest-path policies, fly-through waypoints, short triangular moves,
    exact arrival tolerances, and planned or forced cruise braking.
  - Confirmed deterministic interaction transitions, same-time ordering,
    persistence, observer-safe presentation, compatibility, test, and benchmark
    contracts. Implementation completed by `TASK-090`.
  - Context: [Ship thrust, maneuver kinematics, and short moves](ship-maneuver-kinematics.md) · [Authoritative system-local coordinate scale](system-local-coordinate-scale.md) · [Navigation architecture](navigation-architecture.md) · [Moving-ship interactions](moving-ship-interactions.md) · [Concurrency and performance](concurrency-and-performance.md)

- [x] **TASK-087: Define authoritative system-local coordinate scale**
  - Confirmed one-meter integer coordinates, Euclidean range checks, the
    inclusive `±2^50`-meter per-axis envelope, wide checked arithmetic, exact
    unit-aware authoring, adaptive player-facing distance units, and migration
    expectations.
  - Confirmed analytic scheduled cruise with temporary local fixed-step
    encounters, moving spool, separate cruise and sub-cruise speeds, exact
    transition timing, and deterministic ownership boundaries.
  - Implementation remains with `TASK-071`, `TASK-072`, `TASK-073`, and
    `TASK-075`; `TASK-090` is complete and `TASK-088` retains late-term spatial authoring
    guidance.
  - Context: [Authoritative system-local coordinate scale](system-local-coordinate-scale.md) · [Navigation architecture](navigation-architecture.md) · [Moving-ship interactions](moving-ship-interactions.md) · [Concurrency and performance](concurrency-and-performance.md)

- [x] **TASK-033: Define selection sets and group or fleet commands**
  - Implemented `MoveShipGroupCommand` and `CancelShipGroupCommand` with
    explicit canonical selection snapshots, exact active-controller eligibility,
    all-or-nothing preflight, deterministic ascending commit, and ordinary
    independent per-ship order ownership.
  - Added a replaceable basic resolver: a single ship moves to the selected
    point; otherwise ascending ship IDs occupy a 100-unit clockwise ring from
    east. Existing command outcomes and per-ship facts explain every result;
    no group identity, group order, or history exists.
  - Godot now snapshots local multi-selection for replacement move and
    current-order cancellation, while Shift-append remains focused-ship-only.
    Focused simulation and client tests plus the Godot build passed. `TASK-086`
    owns any future persistent group or fleet lifecycle.
  - Context: [Group and fleet commands](group-and-fleet-commands.md) · [Presentation snapshots](presentation-snapshots.md) · [Actor control and order lifecycle](actor-control-and-orders.md)

- [x] **TASK-083: Build a development-only Godot visual preview and scenario**
  - Added validated optional `connectorEndpoints` and `transitConnections`
    scenario data, plus a three-system development-only package excluded from
    publish output.
  - Implemented separate local galaxy and system views with pan,
    cursor-centred zoom, selection, inspection, local movement routes, and a
    bounded observer-visible activity feed. The preview consumes immutable
    presentation snapshots and validated layout without adding a second
    simulation authority, save/load, recovery, final accessibility/settings,
    audio, or fog-of-war behavior.
  - Added focused coordinate-transform and loader proof. Simulation and
    Godot-side suites, Godot build, content validation, headless startup, and
    project-owner interactive visual review passed.
  - Context: [Application shell and map experience](application-shell-and-map-experience.md) · [Presentation snapshots](presentation-snapshots.md) · [Gameplay content](gameplay-content.md) · [Time and pacing](time-and-pacing.md)

- [x] **TASK-085: Reconcile the built-in starter-ship position acceptance**
  - Restored the built-in `minimal` scenario's starter ship to system position
    `(0, 0)`, matching the production-pipeline acceptance test.
  - Retained the existing content-validation and static new-game loading path.
  - Context: [Gameplay content](gameplay-content.md)

- [x] **TASK-084: Implement the shared device-local preference store**
  - Implemented a versioned device-local JSON store that keeps typed pacing
    preferences and opaque presentation, localization, or accessibility
    categories outside saves, checkpoints, content identity, and authoritative
    simulation state.
  - Added default-on-missing behavior, safe fallback for unreadable, invalid,
    incomplete, and unsupported stores, a non-sensitive reset diagnostic, and
    explicit pacing, category, and reset-all operations. Ordinary writes never
    replace a store that cannot first be read safely.
  - Established schema version 1. There is no previously shipped schema to
    migrate; later versions must add an explicit in-memory migration before
    becoming supported.
  - `TASK-038` consumes the typed pacing section, while `TASK-077` and later
    presentation owners may use opaque categories without creating a second
    local-preference file.
  - Context: [Save slots and local preferences](save-slots-and-local-preferences.md) · [Time and pacing](time-and-pacing.md) · [Accessibility](accessibility.md)

- [x] **TASK-061: Define comprehensive accessibility behavior**
  - Defined the Windows and macOS platform, NVDA and VoiceOver, semantic
    control, focus, semantic map-list, keyboard and mouse, controller,
    remapping-safety, visual-mode, scaling, reflow, onboarding, settings, and
    device-local preference contracts.
  - Defined default, high-contrast dark, high-contrast light, protanopia,
    deuteranopia, and tritanopia behavior; bounded in-game brightness; editable
    presets; safe visual-setting reversion; and non-color cues.
  - Accepted the Xbox Accessibility Guidelines baseline, executable platform
    matrix, contrast thresholds, automated and independent manual evidence,
    relevant-user or specialist review, and project-owner final acceptance.
  - `TASK-077` implements the application contract. `TASK-078` through
    `TASK-080` own audio, `TASK-081` retains reduced-motion design, and
    `TASK-082` retains flashing and photosensitivity design.
  - Context: [Accessibility](accessibility.md) · [Internationalization and localization](internationalization-and-localization.md) · [Application shell and map experience](application-shell-and-map-experience.md) · [Save slots and local preferences](save-slots-and-local-preferences.md)

- [x] **TASK-064: Design event-responsive simulation pacing**
  - Defined local per-category Ignore, Pause, and configured-speed-cap actions;
    caps never accelerate or resume the simulation. Informational dialogue
    defaults to Ignore, while response-required dialogue retains its existing
    preference and temporary pause contract.
  - Defined disclosure-safe offscreen combat pacing: combat-start initially
    pauses only when a player-owned asset is involved and outside the local
    current view, while the combat domain retains exact start semantics.
  - Defined strongest-response boundary batching, one-shot general responses,
    full manual override, visible explanations, and five-second local monotonic
    sliding grace windows keyed by category and stable subject.
  - Kept policies in device-local preferences and grace windows and explanation
    history out of checkpoints and saves. Loading does not replay historical
    notices for pacing.
  - Implementation remains `TASK-038`; typed combat integration coordinates
    with `TASK-046`.
  - Context: [Time and pacing](time-and-pacing.md) · [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Save slots, autosave, and local preferences](save-slots-and-local-preferences.md)

- [x] **TASK-049: Define the application shell and map experience**
  - Defined local desktop startup, new-game, load, active-session,
    leave-session, recovery handoff, and fatal-startup flows without assigning
    simulation authority to Godot.
  - Defined public static galaxy topology with authored presentation-only
    coordinates; galaxy and system map hierarchy; local pan, zoom, and camera
    lifetime; generic entity selection and inspection; overlays; activity; and
    stale, confirmed-missing, and removed-entity handling.
  - `TASK-077` implements the application contract. `TASK-076` retains future
    nonpublic topology and connector discovery; completed `TASK-048` supplies
    static new-game composition, while `TASK-037`, `TASK-038`, `TASK-040`, and
    `TASK-073` retain their respective dependencies. Completed `TASK-061`
    supplies the accessibility contract.
  - Context: [Application shell and map experience](application-shell-and-map-experience.md)

- [x] **TASK-048: Integrate built-in content and static new-game composition**
  - Added an ordinary built-in `galaxy-command.core` disk package containing
    the approved player principal, standing policy, starter ship design, and
    minimal one-system, one-ship static scenario.
  - Added an all-or-nothing static new-game loader that uses the production
    adapters, validation, reference resolution, canonical catalogs, and stable
    scenario-local identity ordering before publishing `GameSessionSetup`.
  - Kept the random root and fact-retention policy caller-owned, retained the
    resolved content set beside the composed setup for `TASK-037`, and rejected
    malformed scenario mappings without publishing content or setup.
  - Replaced the Godot client's hard-coded starting definitions and instances
    with the shipped package path. Three focused tests, the full 580-test suite,
    Release build, scoped formatting, and a Godot headless startup smoke pass.
  - Content-version compatibility and saved-reference migration remain
    `TASK-037`; procedural generation remains `TASK-047`.
  - Context: [Gameplay content and static new-game composition](gameplay-content.md) · [Gameplay integration](gameplay-integration.md) · [Save format and migration](save-format-and-migration.md)

- [x] **TASK-069: Implement generalized inventory and cargo**
  - Implemented qualified physical definitions, fungible holdings, stable
    discrete instances, explicit custody, checked integer capacity, typed
    reservations, atomic transfers, and explicit destruction or transfer
    disposition across session-owned inventories.
  - Added locale-neutral presentation snapshots, catalog-aware checkpoints and
    restore, stable typed outcomes, durable idempotency receipts, and
    accepted-only item and reservation identity allocation.
  - Preserved production and transport behavior through an explicit validated
    one-to-one material compatibility mapping with facility and ship custody.
  - Integrated one authoritative commit owner with the session runtime and
    proved identical holdings, commitments, allocator states, outcomes, facts,
    receipts, and restore behavior across single-thread and parallel
    worker-buffer layouts with varied batch sizes.
  - Content admission was completed by `TASK-048`; saved-reference compatibility
    remains `TASK-037`, and installed equipment remains `TASK-068`.
  - Context: [Generalized inventory and cargo](inventory-and-cargo.md) · [Gameplay content](gameplay-content.md) · [Authoritative save boundary](authoritative-save-boundary.md) · [Concurrency and performance](concurrency-and-performance.md)

- [x] **TASK-074: Define sensor deployables and their placement lifecycle**
  - Defined portable discrete sensor items that atomically materialize into
    fresh stationary deployed entities and return equivalent new items on
    authorized pickup, with no deployed identity retained through redeployment.
  - Defined ownership, authorized ship interaction, fixed sensor capability,
    valid system-local placement, no initial occupancy rule, deterministic
    contention, facts, observed presentation, checkpoint and save state, and
    the committed sensor-source handoff consumed by `TASK-073`.
  - Range and action-order design completed by `TASK-075`; deferred
    combat targeting, destruction, and disposition to `TASK-046`.
  - Context: [Sensor deployables](sensor-deployables.md) · [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Entity lifecycle](entity-lifecycle.md) · [Inventory and cargo](inventory-and-cargo.md)

- [x] **TASK-020: Define player fog-of-war and information staleness**
  - Chose player-facing spatial fog-of-war rather than a general belief,
    confidence, or knowledge-propagation model for every principal. NPC
    decision quality remains separate and may read approved authoritative
    state.
  - Defined circular coverage from player-owned ships, stations, and
    deployables; live authoritative presentation while covered; always-current
    owned assets; transient non-owned ships with no ghost contacts; and
    persistent last-observed stations and stationary deployables.
  - Defined refresh, confirmed absence without inferred cause or time,
    commit-time fact disclosure, narrow dialogue visibility predicates,
    complete discovery checkpoint state, derived coverage rebuild, and stable
    deterministic observation commit.
  - Deferred sensor sharing, NPC fog-of-war, misinformation, confidence,
    stealth, occlusion, equipment modifiers, communications delay,
    moving-contact extrapolation, automatic forgetting, and unspecified
    entity-category discovery.
  - Implementation remains `TASK-073`; deployable entity design remains
    `TASK-074`; equipment contributions remain `TASK-068`; the reusable
    application surface remains `TASK-049`.
  - Context: [Fog-of-war and scouting](fog-of-war-and-scouting.md) · [Player experience](player-experience.md) · [Moving-ship interactions](moving-ship-interactions.md) · [Dialogue](dialogue.md) · [Presentation snapshots](presentation-snapshots.md)

- [x] **TASK-019: Define interactions between ships in motion**
  - Defined domain-requested proximity and analytic swept-path evaluation over
    authoritative local-motion segments, with fractional crossings rounded up
    to the millisecond timeline without losing brief between-millisecond
    passes.
  - Defined durable interaction interests, triggered forecast invalidation,
    state-update-phase evaluation, explicit motion effects, following and
    interception replanning, opt-in fixed steps, connector-transit exclusion,
    and canonical deterministic candidate ordering.
  - `TASK-071` retains implementation. `TASK-072` separately owns ship geometry,
    physical collision, and avoidance.
  - Context: [Moving-ship interactions](moving-ship-interactions.md) · [Navigation and spatial architecture](navigation-architecture.md) · [Concurrency and performance](concurrency-and-performance.md) · [Simulation architecture](simulation-architecture.md)

- [x] **TASK-041: Define generalized inventory and cargo**
  - Defined fungible materials and ordinary cargo plus stable discrete physical
    items, with qualified content identity and no authoritative stack IDs.
  - Defined one integer cargo-capacity dimension, explicit physical custody,
    fungible, instance, and incoming-capacity reservations, atomic transfers,
    and explicit destruction or transfer disposition.
  - Kept Credits, people, permissions, objectives, nonphysical mission state,
    and equipment installation outside inventory. Trade uses the single unified
    currency Credits under `TASK-055`; equipment and ship slots remain
    `TASK-068`.
  - Defined immutable presentation views, complete checkpoint and restore state,
    exact content-reference migration, material-ledger compatibility, and
    deterministic proposal commit independent of worker or batch layout.
  - Implementation is complete in `TASK-069`; content-reference migration
    remains `TASK-037`.
  - Context: [Generalized inventory and cargo](inventory-and-cargo.md) · [Gameplay content](gameplay-content.md) · [Economy](economy.md) · [Concurrency and performance](concurrency-and-performance.md)

- [x] **TASK-050: Define save slots, autosave, and local preference storage**
  - Defined stable manual-slot IDs, player-editable display names, distinct
    autosaves, and manual-save retention. The display name is non-authoritative
    save-envelope metadata; its strict-schema implementation remains `TASK-067`.
  - Defined locally configurable autosaves: a five-minute real-time default,
    five retained by default, ranges of one through 60 minutes and one through
    20 saves, explicit disablement, completed-boundary capture, no paused write,
    and rotation that never evicts manual slots.
  - Required one explicit backup recovery choice, confirmation before manual
    overwrite, and revision-token detection of local external edits. A stale
    save flow requires reload, save as new, or explicit overwrite. Cross-device
    synchronization and external-sync conflict resolution are out of scope.
  - Defined one versioned device-local preference JSON store, in-memory
    migration, safe default use and explicit reset after invalid or unsupported
    data, category reset, and reset-all. Preferences remain outside saves and
    authoritative state.
  - `TASK-040` retains failed-load recovery, `TASK-038` pacing behavior, and
    `TASK-067` save-envelope display names. Completed `TASK-061` defines the
    accessibility preferences consumed through this store.
  - Context: [Save slots, autosave, and local preferences](save-slots-and-local-preferences.md) · [Save format and migration](save-format-and-migration.md) · [Time and pacing](time-and-pacing.md)

- [x] **TASK-066: Implement deterministic random foundations**
  - Implemented the required immutable 256-bit root seed, versioned SHA-256
    canonical derivation, stateless named samples, scoped stateful
    `xoshiro256**` streams, and integer sampling capabilities from `TASK-021`.
  - Added session-owned aggregate state, exact live-stream checkpoint and
    restore, strict algorithm and malformed-state rejection, commit-only draw
    consumption, and expected-state stream retirement.
  - Required saved live streams to match owning-domain declarations exactly;
    domains own identity non-reuse and the aggregate confirms references are
    clear before retirement without retaining retired-key tombstones.
  - Proved golden vectors, root and namespace isolation, unbiased sampling,
    rejected-work non-consumption, retirement safety, exact continuation, and
    worker, batch, and partition independence with 55 focused test cases. The
    full 423-test simulation suite, 27 content tests, Release build, formatting,
    Godot headless build, and all canonical benchmark digests pass.
  - Script use remains `TASK-017`, procedural generation remains `TASK-047`,
    and broader cross-platform and cross-version compatibility remains
    `TASK-060`.
  - Context: [Deterministic randomness](deterministic-randomness.md) · [Concurrency and performance](concurrency-and-performance.md) · [Authoritative save boundary](authoritative-save-boundary.md)

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
  - Implementation was completed by `TASK-066`; procedural generation consumes
    the separate generation scope in `TASK-047`; cross-platform compatibility
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
  - Implementation remains in `TASK-065`; completed `TASK-020` adds only narrow
    fog-of-war predicates without reinterpreting existing authoritative
    conditions; station identity remains in `TASK-057`.
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
  - Built-in content and static new-game integration were completed by
    `TASK-048`; catalog compatibility and saved-reference migration remain
    `TASK-037`.
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
  - Established the localization-related accessibility baseline; completed
    `TASK-061` subsequently defined comprehensive accessibility modes and
    acceptance criteria.
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
    new-game integration were completed by `TASK-048`; catalog compatibility
    and saved-reference migration remain `TASK-037`; procedural new-game
    generation remains `TASK-047`.
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
  - `TASK-063` supplies resolved content catalogs and `TASK-048` supplies static
    new-game composition. General saved sessions remain unavailable until
    `TASK-037` adds catalog compatibility and saved-reference migration.
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
