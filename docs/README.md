# Documentation index

[Project overview and development](../README.md) · [Task list](task-list.md)

Start with [Vision and principles](direction/vision.md) and
[Player experience](direction/player-experience.md), then read
[Simulation architecture](architecture/simulation-architecture.md) for the
technical overview. Use the [task list](task-list.md) for current priorities and
implementation status; the [roadmap](direction/roadmap.md) describes the broader
project phases.

Documents record their own decision status. A design or acceptance specification
does not by itself establish that its implementation is complete.

## Organization

Keep documents directly inside these five folders, without further nesting.
Use headings in this index to group related topics. Shared foundations belong in
architecture; system-specific gameplay and technical contracts stay together in
systems. Measurement records live in validation alongside their workload and
acceptance references. The task list remains the canonical work tracker.

## Direction

Project goals, intended player experience, scope, and overall technical direction.

- [Vision and principles](direction/vision.md)
- [Player experience](direction/player-experience.md)
- [Planned game-system inventory](direction/planned-systems.md)
- [Initial roadmap](direction/roadmap.md)
- [Technical direction](direction/technical-direction.md)

## Architecture

Shared technical foundations and boundaries used across game systems.

### Runtime and state

- [Simulation architecture](architecture/simulation-architecture.md)
- [Source-code organization](architecture/source-code-organization.md)
- [Runtime orchestration and domain ownership](architecture/runtime-orchestration.md)
- [Concurrency and performance architecture](architecture/concurrency-and-performance.md)
- [Entity lifecycle and explicit spawning](architecture/entity-lifecycle.md)
- [Deterministic randomness and stream ownership](architecture/deterministic-randomness.md)
- [Semantic game facts](architecture/semantic-game-facts.md)
- [Presentation snapshots](architecture/presentation-snapshots.md)

### Persistence

- [Authoritative save boundary](architecture/authoritative-save-boundary.md)
- [Save format, versioning, and migration](architecture/save-format-and-migration.md)

### Space and movement

- [Navigation and spatial architecture](architecture/navigation-architecture.md)
- [Authoritative system-local coordinate scale](architecture/system-local-coordinate-scale.md)
- [Ship thrust, maneuver kinematics, and short moves](architecture/ship-maneuver-kinematics.md)
- [Moving-ship interaction architecture](architecture/moving-ship-interactions.md)

### Integration decisions

- [Gameplay integration issues and decisions](architecture/gameplay-integration.md)

## Game systems

Gameplay rules and their system-specific technical contracts live together.

### Economy and assets

- [Economy and production](systems/economy.md)
- [Station composition, construction, and expansion](systems/stations.md)
- [Generalized inventory and cargo](systems/inventory-and-cargo.md)
- [Shared cargo disposition and collection](systems/cargo-disposition-and-collection.md)
- [Equipment and ship slots](systems/equipment-and-ship-slots.md)
- [Ship and fleet balance direction](systems/ship-and-fleet-balance.md)

### Relationships and orders

- [Relational gameplay model](systems/factions.md)
- [Relational simulation architecture](systems/relational-simulation-architecture.md)
- [Actor control and order lifecycle](systems/actor-control-and-orders.md)
- [Group and fleet commands](systems/group-and-fleet-commands.md)
- [Individual NPC scope](systems/individual-npc-scope.md)

### Dialogue, observation, and combat

- [Dialogue state and presentation](systems/dialogue.md)
- [Sensor deployables and placement lifecycle](systems/sensor-deployables.md)
- [Fog-of-war and scouting](systems/fog-of-war-and-scouting.md)
- [Combat activation and movement handoff](systems/combat-activation.md)

## Application

Player-facing application behavior, presentation requirements, and content composition.

- [Application shell and map experience](application/application-shell-and-map-experience.md)
- [Time and pacing](application/time-and-pacing.md)
- [Save slots, autosave, and local preferences](application/save-slots-and-local-preferences.md)
- [Accessibility](application/accessibility.md)
- [Internationalization and localization](application/internationalization-and-localization.md)
- [Gameplay content and static new-game composition](application/gameplay-content.md)

## Validation

Acceptance specifications, benchmark targets, and recorded measurements. Measurement documents retain the context and limits of their original runs.

- [Phase 1 simulation specification](validation/phase-1-simulation-spec.md)
- [Scale targets and benchmark architecture](validation/scale-and-benchmark-targets.md)
- [Spatial interaction proximity measurements](validation/interaction-performance-baseline.md)
- [Spatial interaction index prototype comparison](validation/interaction-index-comparison.md)
- [Swept spatial interaction index comparison](validation/interaction-swept-index-comparison.md)
- [Public instant-query API measurements](validation/interaction-runtime-index-measurements.md)
- [Public swept-query API measurements](validation/interaction-runtime-swept-measurements.md)
- [Interaction batch execution measurements](validation/interaction-batch-measurements.md)
- [Advancing-session interaction measurements](validation/interaction-session-measurements.md)
