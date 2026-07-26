# Gameplay integration issues and decisions

[Project index](../README.md) · [Project task list](task-list.md) · [Simulation architecture](simulation-architecture.md) · [Technical direction](technical-direction.md) · [Initial roadmap](roadmap.md)

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

**Status: contract defined by `TASK-002`; session integration is tracked by
`TASK-003`.**

The public Phase 1 facade can advance the simulation, capture a snapshot, expose
the mutable world, and schedule one fixture-specific route disruption. It does
not yet accept general player, autonomous, dialogue, or scripted intent.

Without a common command boundary, future systems would either add specialized
methods to the facade or mutate the world directly. Those paths would develop
different validation, ordering, failure, and observability behavior.

The rendering-independent command contract now separates submitted
`GameplayCommand` intent from internal scheduled events. It assigns every
submission a simulation timestamp and monotonically increasing
`CommandSequence`, carries opaque source attribution, returns an explicit
accepted or rejected result, and records both outcomes. The persistent session
will own this processor and connect it to authoritative command handlers.

### 1.3 Authoritative state is publicly mutable

`PhaseOneScenario.World` exposes `SimulationWorld`, whose public registries and
domain objects include mutating operations. A presentation client or future
gameplay system can therefore change authoritative state without passing
through runtime ordering, validation, event recording, or causal rules.

The same world-building APIs are currently used both to create a fixture and to
represent the running simulation. Setup authority and runtime authority are not
separated.

### 1.4 Integrated behavior is centralized in the Phase 1 runtime

The generic simulation engine is scenario-independent, but the Phase 1 runtime
still owns the combined event vocabulary, event dispatch, reconciliation order,
metrics, shortages, decisions, snapshots, fixture-specific disruption, and
stopping condition.

Adding dialogue, faction planning, combat, objectives, or scripted events
directly to this class would recreate a scenario-specific monolith above the
generic runner.

### 1.5 Scheduled work has an incomplete cancellation contract

Event generation tokens exist, and transport events compare their token with
current job state. Generations are effectively fixed in the current runtime,
however, and there is no general contract for cancelling, replacing, or
invalidating pending work.

Interactive commands, actor destruction, interrupted orders, route changes,
and save restoration will all require predictable stale-event behavior.

### 1.6 Processed-event records are not semantic game facts

Current event records primarily describe internal scheduled operations.
Decision records describe a narrow set of logistics decisions. These are useful
for deterministic regression tests but do not form a stable gameplay-facing
history.

Dialogue, objectives, scripts, UI notifications, and faction reactions need
semantic facts such as an actor entering a location, an order failing, an asset
being destroyed, a relationship changing, or an objective completing.

### 1.7 The public scenario lifecycle is a proof-of-concept lifecycle

`RunUntilFirstShip` and the Phase 1 stopping condition end advancement when the
first constructed ship appears. The Godot client also stops at that point. This
is correct for the current acceptance scenario but cannot be the lifecycle of a
persistent game.

### 1.8 Presentation state is tied to the Phase 1 fixture

The immutable snapshot boundary is appropriate, but `PhaseOneSnapshot` only
contains the state needed to display the current fixture. It does not expose
general entity selection data, current orders and reasons, faction identity,
relationships, dialogue state, objectives, or player knowledge.

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

Create a rendering-independent facade responsible for:

- Accepting validated gameplay commands
- Advancing authoritative simulation time
- Capturing immutable presentation state
- Returning command acceptance or rejection reasons
- Exposing ordered semantic facts for presentation and development tools

Godot, faction AI, dialogue choices, and scripted behavior should use this
boundary rather than mutate `SimulationWorld`.

### 2.4 Separate setup APIs from runtime mutation

Allow fixtures, save loading, and content initialization to construct a world
through privileged setup APIs. Once play begins, prevent external callers from
using those APIs or mutable registries to bypass commands.

The live game session should expose read models and stable identifiers, not the
mutable authoritative world.

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

### 2.6 Complete cancellation and invalidation behavior

Define how an order or activity changes its generation when it is cancelled,
replaced, interrupted, or destroyed. Every scheduled event must verify both its
generation and referenced state before applying a change.

Specify whether a stale event becomes a silent no-op, an internal diagnostic,
or a semantic cancellation/failure fact. Add focused tests for each supported
interruption.

### 2.7 Add semantic facts alongside internal scheduled events

Keep internal events for deterministic future work. Add a separate ordered fact
stream for meaningful completed changes. Facts should have stable identities or
sequence numbers so consumers can process them once.

Scripts and faction systems should react to facts by submitting commands. They
should not receive unrestricted mutation callbacks.

### 2.8 Replace scenario termination with objective state

Keep bounded stopping conditions in headless acceptance scenarios, benchmarks,
and tests. A normal game session should continue until the player quits or an
explicit terminal game state is reached.

Construction milestones, quest completion, victory, and defeat should be
represented as objective or game-state facts rather than implicit engine
termination.

### 2.9 Generalize presentation snapshots incrementally

Retain immutable snapshots, but introduce presentation models organized around
gameplay needs rather than the Phase 1 fixture. Add current order, controller,
reason, and relevant history when the first interactive ship command is
implemented. Add dialogue, faction, objective, and knowledge views only when
their authoritative models exist.

## 3. Issues that need resolution in the near term

### 3.1 Actor control and order lifecycle

The project needs one order model shared by player-controlled and autonomous
actors. It must define command authority, validation, queuing, interruption,
cancellation, completion, failure, and how control returns after a temporary
scripted override.

The first implementation target should be selecting one ship in Godot, issuing
a move order, observing its reason and state, and cancelling or replacing it.

### 3.2 Entity lifecycle and explicit spawning

Creation, destruction, and removal need an authoritative lifecycle. Immediate
scenario or scripted spawning must be distinguished from causal acquisition
through production and construction.

Spawning must define ownership, design, location, initial state, initial order,
deterministic identifier allocation, validation, and emitted facts. Destruction
must clean up cargo, reservations, orders, controllers, and pending events.

### 3.3 Dialogue and scripted-event integration

Dialogue presentation belongs in Godot, while gameplay-affecting conditions,
choices, completion state, and effects belong to the authoritative game
session. A gameplay-affecting choice should submit a normal command.

Scripted behavior needs deterministic triggers, persistent one-time state, and
a restricted set of command effects. It should consume semantic facts rather
than internal transport or production implementation events.

### 3.4 Faction and relationship state

Organizations currently provide identity but not the faction state required by
strategic behavior, dialogue, hostility, ownership transfer, or enemy
classification. Relationships, objectives, priorities, and known information
need authoritative homes before faction-specific scripts are added.

### 3.5 Pause, speed, and input timing

Define when commands submitted while paused take effect, how commands are
ordered when several arrive at the same simulation time, and whether opening
dialogue pauses automatically. These are local single-player rules; no
multiplayer synchronization behavior is needed.

### 3.6 Save, load, and replay state

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
