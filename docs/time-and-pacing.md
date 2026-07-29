# Time and pacing

[Project index](../README.md) · [Simulation architecture](simulation-architecture.md) · [Economy](economy.md) · [Scale targets and benchmarks](scale-and-benchmark-targets.md)

## Separate clocks

The design distinguishes:

- **Wall-clock time:** time experienced outside the simulation
- **Simulation time:** the authoritative time within the galaxy
- **Process duration:** simulation time required to complete an activity

Computer performance determines how quickly simulation work can be calculated. It must not silently change process durations or economic outcomes.

## Player-controlled simulation speed

The player should be able to pause and select from multiple simulation speeds. If the computer cannot calculate a requested speed in real time, simulation time advances more slowly without changing the rules.

The supported maximum speed remains a performance target to be established
through `TASK-024`. The current proposal is documented in
[Scale targets and benchmark architecture](scale-and-benchmark-targets.md).

## Pacing levers

Major timing categories should be independently configurable:

- Travel, docking, and inter-system transit
- Resource extraction and cargo transfer
- Refining and component manufacturing
- Ship and station construction
- Repair and replenishment
- Economic reevaluation
- Faction military and diplomatic planning
- Combat actions

There should also be a global pacing control that can scale compatible categories together. Category-specific controls allow travel or production to be adjusted without changing the entire game.

## Work and throughput

Where practical, industrial duration should derive from required work and effective throughput rather than an isolated timer. Travel should similarly derive from route distance and effective speed.

This allows equipment, damage, technology, and local conditions to modify performance through consistent concepts.

## Configuration and saves

Timing values should be data-driven, versioned, and recorded in saved games. Development builds should make them easy to adjust and compare.

Useful balancing metrics include:

- Typical trip and delivery times
- Facility utilization and queue time
- Time from raw resource to completed ship
- Frequency and duration of shortages
- Replacement time after military losses
- Faction response time after major events

The implementation implications are summarized in [Technical direction](technical-direction.md).
