# Navigation and spatial architecture

[Project index](../README.md) · [Simulation architecture](simulation-architecture.md) · [Player experience](player-experience.md) · [Gameplay integration](gameplay-integration.md) · [Project task list](task-list.md)

## Purpose

Each star system is its own navigable space. Ships can move to positions and
entities within that space. Gates and other transit mechanisms connect distinct
systems without turning the galaxy into one continuous coordinate plane.

The Phase 1 location-and-route graph remains a useful deterministic acceptance
backend, but it is not the target world model. Its graph nodes combine ideas
that the game needs to keep separate: a system, a position, an entity at that
position, and a place where an activity occurs.

This document defines the boundaries that should be established before the
first interactive ship move order. It does not select a final pathfinding
algorithm, collision model, numeric coordinate representation, or scale.

## Core model

### Systems are spatial containers

A system owns a two-dimensional local coordinate space and the spatial entities
currently within it. Ships, stations, gates, resource sites, hazards, and other
physical objects have system-relative spatial state.

There is no authoritative galaxy-wide position that places every system in one
shared travel space. A galaxy-map layout may assign systems display positions,
but those positions are presentation data and do not determine local travel.

An entity that participates in physical activity must be locatable through its
authoritative spatial state. Economic records should refer to the inventory,
facility, or other owning entity rather than copying a second location value
that can disagree with it.

### Connectors join systems

A gate is a physical entity with an endpoint in a system. A transit connection
relates that endpoint to an endpoint in another system and defines direction,
availability, traversal duration, and access requirements.

The general concept is a connector so future transit mechanisms do not have to
pretend to be gates. The first implementation can support gates only. A
bidirectional gate pair may be represented by two directional connections when
their rules or state can differ.

Entering a connector and travelling through it are different from moving
through ordinary local space. A ship must first reach the source endpoint,
satisfy the connector's traversal rules, complete the transition, and emerge at
the destination endpoint.

### Ships have authoritative spatial and motion state

Every ship must be in exactly one physical state:

- Present at a position within a system
- Following a local motion segment within a system
- Traversing a connector between systems
- Attached to another entity through a state such as docking

The exact data types remain an implementation decision, but these states must
not be represented by a nullable collection of unrelated fields that can form
invalid combinations.

A local motion segment records enough authoritative information to determine
where the ship is at a simulation time, including its start, destination,
departure, and expected arrival. Normal travel can therefore use scheduled
completion events without mutating every ship at rendering frequency.

Godot may interpolate from that authoritative segment for smooth display.
Rendered frame positions are not written back into the simulation. Activities
that need intermediate physical interaction, such as active combat or hazard
avoidance, may later use fixed simulation steps without changing the system,
target, or order contracts.

## Navigation contracts

Navigation is divided into intent, planning, and execution. These boundaries
are semantic; the first implementation does not need a general plugin system
or a hierarchy of public interfaces for each paragraph below.

### Destination intent

A movement order describes where the actor should end up, not the path chosen
to get there. Initial destination forms should cover:

- A position within a particular system
- A physical entity, resolved through that entity's current spatial state
- A system when arrival anywhere through a valid connector is sufficient

Docking, following, patrolling, and attacking may use movement internally, but
they remain distinct gameplay orders with their own completion rules. A move
order must not contain Phase 1 `RouteId` values or a preselected list of gates.

The destination remains stable while planning details may change. If a target
entity moves, the owning order system decides when to refresh the resolved
destination rather than silently changing the meaning of all navigation
requests.

### Planning

A navigation request combines:

- The actor and its current authoritative spatial state
- The destination intent
- Relevant movement capabilities and connector access
- The authoritative simulation time and topology state

A successful result is an ordered travel plan whose legs have explicit
semantics. The required initial leg categories are local movement and connector
traversal. Later planning may add approach corridors, jump behavior, or other
mechanisms without changing the movement-order payload.

Planning is hierarchical:

1. If the destination is in the current system, plan only local movement.
2. Otherwise, choose a deterministic sequence of inter-system connectors.
3. For each connector, plan local movement to its source endpoint.
4. Traverse it and continue planning from the destination endpoint.
5. Plan the final local movement to the requested destination.

The inter-system topology graph and a system's local spatial navigation are
separate indexes. A single galaxy-wide graph should not contain every arbitrary
position, station, gate, and local waypoint.

Plans and estimates must have deterministic tie-breaking. The chosen rules can
include travel time, connector identity, access, risk, or actor policy as those
models become authoritative. An unreachable result includes a stable reason
suitable for order state and player-facing explanation.

### Execution

The order system owns the actor's requested destination and lifecycle. The
movement system owns the currently executing travel leg and its scheduled work.
The navigation planner does not mutate the ship.

Beginning a leg validates that its start state and required topology still
match. Completion validates the leg identity, generation, actor state, and
expected completion time before applying movement. Cancellation, replacement,
or destruction invalidates pending completion through the project's existing
generation contract. Cancelling or replacing local motion first derives and
materializes the ship's authoritative position at the command timestamp, so it
does not snap back to the segment's origin or forward to its destination.

An accepted move command means that the order was valid and could begin or
enter a defined waiting state. It does not promise that all future legs will
remain reachable.

Plans may be refreshed:

- Before the first leg
- At a connector or local-leg boundary
- When an order is resumed from a defined waiting state
- When an authoritative topology or target change explicitly invalidates the
  remaining plan

Disabling a connector or invalidating a local path does not silently teleport
or rewind a ship already executing a valid leg. The movement mechanism defines
whether an active leg is allowed to finish, interrupted into a valid spatial
state, or failed. The initial compatibility behavior may allow an active leg to
finish and replan at its boundary, matching the Phase 1 route-disruption rule.

## Authority and presentation

The authoritative simulation owns:

- System membership and system-local spatial state
- Connector endpoints, topology, enabled state, and access rules
- Active orders, travel plans, motion legs, generations, and timing
- The state required to reproduce a ship's position and future completion

Presentation snapshots expose immutable views of those concepts. The Godot
client decides how to lay out systems on a galaxy map and how to transform
system-local coordinates into the current view. It may interpolate an active
motion segment but may not invent authoritative routes, positions, arrivals, or
gate transitions.

Selecting a ship should make the distinction visible: requested destination,
current leg, later planned legs, expected arrival, and any waiting or failure
reason are separate pieces of information.

## Interaction with logistics

Logistics chooses economic endpoints such as a source inventory and destination
inventory. It should not perform pathfinding itself or store graph edges in the
transport job.

Before assignment, logistics requests deterministic reachability and travel
estimates from navigation using the entities that own those inventories. After
assignment, the ship's order and movement systems execute travel to the source
and destination. Loading and unloading begin only when the ship satisfies the
relevant proximity or attachment requirement.

This separation allows a station to move, a gate to become inaccessible, or a
different movement capability to alter the journey without changing the
material reservation and delivery commitments.

## Initial decisions and deferred choices

The following are architectural decisions:

- Systems are distinct local spaces connected by explicit transit mechanisms.
- Local movement and inter-system traversal are different travel-leg kinds.
- Orders contain destination intent and never contain a graph-selected path.
- System-relative spatial and motion state is authoritative.
- Normal travel remains compatible with scheduled events and deterministic
  interpolation.
- Inter-system and local planning are separate but compose into one travel
  plan.
- The Phase 1 graph remains a compatibility backend, not the future domain
  model.

The following remain deliberately undefined until a concrete gameplay need or
scale target exists:

- Coordinate scalar, precision, units, and world bounds
- Ship acceleration, turning, collision, and formation movement
- Local obstacle representation and pathfinding algorithm
- Gate queueing, congestion, animation, and failure while in transit
- Whether inactive systems use reduced-detail movement
- Combat interaction with scheduled motion
- Sensor knowledge and whether a planner may use undiscovered topology
- Travel-cost policy beyond deterministic duration and availability

## Migration sequence

1. Introduce system identity, system-local positions, and valid ship spatial
   states without removing the Phase 1 graph.
2. Define destination intent and travel-plan results that do not expose
   `RouteId`.
3. Add scheduled local motion between two positions in one system, including
   cancellation, replacement, snapshots, and headless determinism.
4. Implement the first interactive ship move order against that local-motion
   boundary.
5. Adapt Phase 1 logistics to request reachability and estimates without
   selecting graph legs itself. Preserve its existing deterministic acceptance
   fingerprints until an explicitly approved fixture migration.
6. Add gate entities and deterministic connector traversal, then prove a
   multi-system move composed from local and connector legs.
7. Replace Phase 1-specific location and route presentation with general
   system, spatial-entity, plan, and motion snapshots.

The compatibility layer should be removed only after the economic scenario and
interactive movement both use the new contracts with focused regression
coverage.
