# Gameplay integration issues and decisions

[Project index](../README.md) · [Project task list](task-list.md) · [Simulation architecture](simulation-architecture.md) · [Navigation and spatial architecture](navigation-architecture.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Concurrency and performance](concurrency-and-performance.md) · [Technical direction](technical-direction.md) · [Initial roadmap](roadmap.md)

This document tracks architectural work needed to turn the current deterministic
simulation into an interactive game that can support player commands, dialogue,
scripted events, faction behavior, direct control of individual actors, and
explicit entity spawning.

Implementation status and deferred work are tracked in the
[project task list](task-list.md). This document records the underlying issues,
constraints, and intended resolutions rather than acting as a second checklist.

## Single-player requirement

Galaxy Command will be strictly single-player. No architectural affordance
should be made for multiplayer, including networking abstractions, replicated
world state, remote authority, client prediction, rollback netcode, lobby or
session services, or multiplayer-compatible restrictions on pausing.

Determinism, command recording, and replay remain valuable for tests, debugging,
saves, balancing, and reproducing simulation behavior. They should not be
designed as multiplayer infrastructure.

## 1. Issues and status

### 1.1 Same-time event ordering does not provide a complete phase barrier

**Status: resolved by `TASK-001`.**

Previously, `EventAgenda<TEvent>` ordered scheduled events by timestamp, phase,
and creation sequence, but `SimulationEngine<TEvent>` called the runtime's
`Reconcile` method after each individual event. This allowed production,
construction, supply and demand publication, or freighter decisions to run
between unrelated earlier-phase events at the same timestamp.

The engine now opens and drains each timestamp phase explicitly. Reconciliation
runs once at the start of the decision phase, after physical completions and
state updates have drained. A bounded stop condition takes effect after the
current timestamp cycle completes.

### 1.2 There is no general gameplay command boundary

**Status: resolved by `TASK-002` and `TASK-003`.**

The bounded Phase 1 acceptance harness can advance the simulation, capture a
snapshot, and schedule one fixture-specific route disruption. It is not the
application-facing boundary.

Without a common command boundary, future systems would either add specialized
methods to the facade or mutate the world directly. Those paths would develop
different validation, ordering, failure, and observability behavior.

The rendering-independent command contract now separates submitted
`GameplayCommand` intent from internal scheduled events. It assigns every
submission a simulation timestamp and monotonically increasing
`CommandSequence`, carries opaque source attribution, returns an explicit
accepted or rejected result, and records both outcomes. `GameSession` now owns
this processor and a clean application runtime independent of the Phase 1
acceptance harness. It advances authoritative time and is the boundary used by
Godot. `TASK-005` added the first accepted commands for moving and cancelling a
player ship; commands without an implemented subsystem handler remain rejected
and recorded.

### 1.3 Authoritative state is publicly mutable

**Status: resolved by `TASK-004`.**

`SimulationWorld` is now internal to the simulation assembly. A one-use setup
capability constructs fixture or future save-loaded state, then is consumed
when it hands the world to its runtime. Neither `GameSession` nor
`PhaseOneScenario` exposes the mutable live aggregate.

Presentation and gameplay callers instead receive immutable snapshots, stable
identifiers, command results, and diagnostic records. They cannot bypass
runtime ordering, validation, event recording, or causal rules through a world
reference.

### 1.4 Integrated behavior is centralized in the Phase 1 runtime

The generic simulation engine is scenario-independent, but the Phase 1 runtime
still owns the combined event vocabulary, event dispatch, reconciliation order,
metrics, shortages, decisions, snapshots, fixture-specific disruption, and
stopping condition.

Adding dialogue, faction planning, combat, objectives, or scripted events
directly to this class would recreate a scenario-specific monolith above the
generic runner.

### 1.5 Scheduled work has an incomplete cancellation contract

**Status: resolved by `TASK-007`.**

Previously, event generation tokens existed but generations were fixed, only
transport payloads carried them, and a transport event could fail an active-job
check before discovering that it was stale. Production completions identified
only a facility, so an old completion could mutate replacement work at that
facility.

Every scheduled activity mutation now carries stable activity identity and an
`EventGeneration` in its `ScheduledEvent`. The runtime validates the referenced
activity, generation, and expected state before mutation, then records an
explicit `ScheduledEventDisposition`.

### 1.6 Processed-event records are not semantic game facts

Current event records primarily describe internal scheduled operations.
Decision records describe a narrow set of logistics decisions. These are useful
for deterministic regression tests but do not form a stable gameplay-facing
history.

Dialogue, objectives, scripts, UI notifications, and faction reactions need
semantic facts such as an actor entering a location, an order failing, an asset
being destroyed, a relationship changing, or an objective completing.

### 1.7 The bounded scenario lifecycle is a proof-of-concept lifecycle

`RunUntilFirstShip` and the Phase 1 stopping condition end advancement when the
first constructed ship appears. This remains correct for the acceptance harness
and its regression fingerprints. Godot now uses the clean `GameSession`
runtime, which has no fixture milestone or Phase 1 world state.

### 1.8 Presentation state is tied to the Phase 1 fixture

**Status: partially resolved by `TASK-005`.**

The clean application snapshot now exposes systems, authoritative ship
positions, active motion segments, and current move-order destination, status,
and reason. Godot no longer consumes `PhaseOneSnapshot`.

The presentation model still lacks controller identity, semantic fact history,
factions, relationships, dialogue state, objectives, and player knowledge.
Those fields should be added only as their authoritative models are introduced.

### 1.9 The Phase 1 navigation boundary exposes its graph model

`INavigation` prevents logistics from modifying `RouteGraph` directly, but its
queries and results still expose `LocationId`, `RouteId`, and `DirectedRoute`.
Ships, transport events, and snapshots consequently treat movement as discrete
graph-edge traversal.

That contract cannot directly represent a ship moving freely within a system
and then using a gate to enter another system. Extending the graph with every
position, station, gate endpoint, and local waypoint would also collapse local
space and inter-system topology into one increasingly expensive model.

## 2. Steps to resolve the current issues

### 2.1 Establish deterministic timestamp and phase processing

The implemented timestamp cycle is:

1. Time-based metrics accrue once when the simulation reaches a timestamp.
2. The `PhysicalCompletion` phase drains in creation-sequence order.
3. The `StateUpdate` phase drains in creation-sequence order.
4. Reconciliation runs once at the start of the `Decision` phase.
5. The `Decision` phase drains in creation-sequence order.
6. Work scheduled for the current phase is appended and drained before phase
   advancement; work may also target a later phase at the same timestamp.
7. Scheduling into an already completed phase at the current timestamp is
   rejected.

Future semantic-fact recording can follow decision processing without weakening
these barriers.

### 2.2 Define the gameplay command contract

The implemented command contract has these rules:

1. A `GameplayCommand` describes requested intent. It is not a scheduled event
   and does not claim that the requested activity has completed.
2. Every submission carries a `CommandSourceKind` and opaque
   `CommandSourceId`. The supported categories are player, autonomous,
   dialogue, and script. These fields provide local attribution and validation
   context; they are not authentication or multiplayer authority.
3. The session assigns a monotonically increasing `CommandSequence` and the
   current authoritative `SimulationTime`. Sequence resolves ordering when
   multiple commands are submitted at the same timestamp.
4. A fixed `IGameplayCommandHandler` validates the source, intent, referenced
   state, and conflicts before applying or scheduling authoritative changes.
5. `Accepted` means immediate validation succeeded and the intent was applied
   or scheduled. It does not guarantee that a resulting order or activity will
   later complete.
6. `Rejected` includes a stable machine-readable `CommandRejectionCode` and a
   concise diagnostic reason. Rejection is the expected result for invalid
   gameplay input; unexpected implementation faults are not converted into
   gameplay rejections.
7. Accepted and rejected submissions are retained as ordered
   `GameplayCommandRecord` values for deterministic tests and debugging.
8. Later activity completion, cancellation, or failure will be reported as
   semantic facts rather than retroactively changing the command result.

Command-specific payloads, validation rules, and rejection codes are introduced
with the subsystem that owns them. This avoids inventing ship-order, dialogue,
or script data structures before those models exist.

### 2.3 Introduce a persistent game-session facade

The rendering-independent `GameSession` facade is now responsible for:

- Accepting validated gameplay commands
- Advancing authoritative simulation time
- Capturing immutable presentation state
- Returning command acceptance or rejection reasons
- Exposing ordered semantic facts for presentation and development tools

Godot uses this boundary rather than the bounded scenario or mutable
`SimulationWorld`. Faction AI, dialogue choices, and scripted behavior should
use the same boundary as their command handlers are introduced. Ordered
semantic facts remain in `TASK-008`.

`GameSessionSetup` explicitly supplies initial systems and spatial ships, while
the navigation policy is injected separately. The session constructs a clean
`GameRuntime` with its own event vocabulary, order owner, and spatial movement
owner. It does not wrap `PhaseOneRuntime`.

### 2.4 Separate setup APIs from runtime mutation

Phase 1 fixtures construct their economic world through
`SimulationWorld.Setup`. Calling `Complete` consumes that capability and hands
the internal aggregate to the acceptance runtime. The clean application runtime
uses immutable `GameSessionSetup` input for initial systems and spatial ships.
This is setup-time initialization, not a public runtime spawning API; general
spawn and destruction behavior remains in `TASK-011`.

The live game session and bounded acceptance facade expose read models, stable
identifiers, commands, and records, not the mutable authoritative world.
Regression coverage verifies that rejected commands leave authoritative
snapshot, event, and decision state unchanged.

### 2.5 Split orchestration into explicit simulation systems

Move production, construction, logistics, orders, faction planning, scripts,
objectives, and other behavior behind explicit system boundaries. Define:

- Which event and command types each system handles
- Which authoritative state each system owns or may mutate
- Which facts it emits
- When it evaluates during a timestamp
- Its deterministic ordering relative to other systems

A fixed, explicit dispatcher is sufficient initially. A dynamic plugin system
or general-purpose event bus is not required.

Each system boundary should also define stable read inputs, independently
batchable evaluation work, buffered effects, and deterministic commit rules.
This permits later concurrent execution without allowing worker scheduling or
lock acquisition to determine simulation results. The full contract is in
[Concurrency and performance architecture](concurrency-and-performance.md).

### 2.6 Complete cancellation and invalidation behavior

The implemented cancellation and invalidation contract is:

1. Every independently replaceable scheduled activity owns an
   `EventGeneration`, initially zero.
2. Cancellation, replacement, interruption, or destruction cleanup advances
   the old activity's generation before another activity can reuse its owner.
   Generation overflow is a fatal invariant error rather than wraparound.
3. A scheduled mutation captures both the stable activity identifier and its
   generation. The handler validates, in order, that the reference exists, the
   generation matches, and the activity is still in the expected state.
4. A valid event returns `Applied`. Invalid events are no-ops and return one of
   `IgnoredStaleGeneration`, `IgnoredMissingReference`, or
   `IgnoredStateMismatch`. The disposition is retained with the internal event
   record for deterministic diagnostics.
5. Ignored events do not emit semantic failure or cancellation facts. Explicit
   cancellation commands will emit those facts when the semantic fact stream
   is introduced by `TASK-008`.
6. Cancelling production or construction releases inputs that are still
   reserved. Inputs already consumed when work started are not recreated.
7. Cancelling transport before loading releases the source reservation and
   restores both supply and demand commitments. Interrupting after loading
   restores demand, retains the material in ship cargo, releases destination
   capacity, and leaves the ship at its last authoritative discrete location.
8. Actor destruction must perform activity cleanup before removing the actor.
   Any already-pending event that later observes a missing actor is an
   `IgnoredMissingReference` no-op.

Production, construction, and transport use this contract now. Future actor
orders must use the same generation and disposition rules when `TASK-006` and
`TASK-011` introduce their lifecycle APIs.

### 2.7 Add semantic facts alongside internal scheduled events

Keep internal events for deterministic future work. Add a separate ordered fact
stream for meaningful completed changes. Facts should have stable identities or
sequence numbers so consumers can process them once.

Scripts and faction systems should react to facts by submitting commands. They
should not receive unrestricted mutation callbacks.

### 2.8 Replace scenario termination with objective state

`GameSession` has no fixture milestone stop and continues advancing after the
first constructed ship. Keep bounded stopping conditions in headless acceptance
scenarios, benchmarks, and tests; `Acceptance/PhaseOneScenario` is the current
example. A normal game session should continue until the player quits or an
explicit terminal game state is reached.

Construction milestones, quest completion, victory, and defeat should be
represented as objective or game-state facts rather than implicit engine
termination.

### 2.9 Generalize presentation snapshots incrementally

Retain immutable snapshots, but introduce presentation models organized around
gameplay needs rather than the Phase 1 fixture. `TASK-005` introduced
`GameSnapshot` with system, ship position, motion, current order, destination,
status, and reason. Controller and semantic history remain in `TASK-006` and
`TASK-008`. Add dialogue, faction, objective, and knowledge views only when
their authoritative models exist.

### 2.10 Establish hierarchical spatial navigation

Define systems as distinct local coordinate spaces and connectors such as gates
as explicit inter-system transitions. Separate destination intent, planning,
and movement execution so actor orders never contain graph-selected route
identifiers.

Normal movement should remain deterministic and scheduled by storing an
authoritative motion segment rather than polling every ship at rendering
frequency. Local movement and connector traversal compose as different travel
legs. The Phase 1 graph remains a compatibility backend until its logistics and
acceptance coverage can migrate deliberately.

The complete contract and migration sequence are in
[Navigation and spatial architecture](navigation-architecture.md).

## 3. Issues that need resolution in the near term

### 3.1 System-space movement boundary

**Status: local movement resolved by `TASK-028` and `TASK-005`.**

The system, spatial-state, destination, planning, and scheduled-motion
boundaries must be established before the first accepted ship move order. The
first order now proves a point-to-point move within one system. Gate traversal
can compose with the same order contract rather than forcing a replacement
later.

### 3.2 Actor control and order lifecycle

**Status: first player move-order slice implemented by `TASK-005`; general
lifecycle remains in `TASK-006`.**

The project needs one order model shared by player-controlled and autonomous
actors. It must define command authority, validation, queuing, interruption,
cancellation, completion, failure, and how control returns after a temporary
scripted override.

The first implementation selects one ship in Godot, issues a position move
order, exposes its reason and state, and supports cancellation and replacement.
Invalid or immediately unreachable requests are command rejections. Waiting and
genuine failure after command acceptance remain undefined until `TASK-006`.
The proposed controller, override, queue, lifecycle, multi-leg, and deterministic
commit contracts are in
[Actor control and order lifecycle](actor-control-and-orders.md).

### 3.3 Entity lifecycle and explicit spawning

Creation, destruction, and removal need an authoritative lifecycle. Immediate
scenario or scripted spawning must be distinguished from causal acquisition
through production and construction.

Spawning must define ownership, design, location, initial state, initial order,
deterministic identifier allocation, validation, and emitted facts. Destruction
must clean up cargo, reservations, orders, controllers, and pending events.

### 3.4 Dialogue and scripted-event integration

Dialogue presentation belongs in Godot, while gameplay-affecting conditions,
choices, completion state, and effects belong to the authoritative game
session. A gameplay-affecting choice should submit a normal command.

Scripted behavior needs deterministic triggers, persistent one-time state, and
a restricted set of command effects. It should consume semantic facts rather
than internal transport or production implementation events.

### 3.5 Faction and relationship state

Organizations currently provide identity but not the faction state required by
strategic behavior, dialogue, hostility, ownership transfer, or enemy
classification. Relationships, objectives, priorities, and known information
need authoritative homes before faction-specific scripts are added.

### 3.6 Pause, speed, and input timing

Define when commands submitted while paused take effect, how commands are
ordered when several arrive at the same simulation time, and whether opening
dialogue pauses automatically. These are local single-player rules; no
multiplayer synchronization behavior is needed.

### 3.7 Save, load, and replay state

Before scripts and orders accumulate significant state, identify everything a
save must preserve: authoritative world state, simulation time, pending agenda,
event creation sequence, random state, controllers, orders, cancellation
generations, objectives, dialogue and script progress, and content version.

The initial requirement is a well-defined authoritative boundary, not a final
serialization format.

## 4. Topics that need more definition

- Whether “individual NPC” initially means individually controlled ships,
  person-level characters, crew, or more than one of these
- The actor order vocabulary and the difference between an order, task, action,
  objective, and standing order
- Coordinate precision and units for system-local authoritative space
- Local pathfinding, collision, acceleration, and formation-movement behavior
- Connector access, congestion, traversal failure, and arrival behavior
- The simulation detail used for movement in inactive or unobserved systems
- Authority precedence among the player, autonomous AI, faction strategy, and
  temporary scripted control
- Which activities may be interrupted immediately and which require a safe
  transition or explicit failure
- Whether dialogue pauses by default and whether simulation-time conditions can
  change while dialogue is open
- Dialogue availability, choice conditions, repeatability, memory, and
  consequences
- Script trigger semantics, including one-shot, repeatable, threshold,
  time-based, location-based, and fact-based triggers
- The permitted effects of scripts and whether development-only effects are
  separated from shipped narrative effects
- When explicit spawning is acceptable and when entities must enter through
  economic construction or another causal process
- Destruction, despawning, capture, surrender, and ownership-transfer behavior
- Combat resolution, especially the treatment of observed and unobserved combat
- Faction relationships, hostility, reputation, membership, and diplomacy
- The boundary between complete authoritative state and what the player or a
  faction currently knows
- Objective, mission, victory, defeat, and non-terminal milestone models
- Random-number stream ownership and how scripted randomness remains
  reproducible
- Save compatibility, content versioning, and migration expectations
- Whether gameplay content will eventually be code-defined, data-defined, or
  externally scriptable
- Modding goals and security constraints, if modding remains a desired feature
- Target scale for actors, factions, active scripts, and retained semantic
  history
- How much recent reasoning and history must remain available for player-facing
  explanations without retaining an unlimited event log
