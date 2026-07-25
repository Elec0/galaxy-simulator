# Simulation architecture

[Project index](../README.md) · [Technical direction](technical-direction.md) · [Time and pacing](time-and-pacing.md)

## Goals

The simulation should be persistent, inspectable, reproducible, and able to run without rendering. It should support large numbers of individually identifiable ships without updating every behavior at rendering frequency.

## Major layers

### World state

The world contains systems, connections, ships, stations, inventories, ownership, relationships, and other durable state.

Phase 1 represents movement as a graph of locations and routes. Travel behavior will depend on an abstract navigation boundary rather than directly on the graph representation so a continuous spatial model can replace or supplement it later.

`SimulationWorld` is the concrete owner of that durable state. It holds the
navigation graph, inventories, production facilities, construction sites,
design catalog, ships, transport board, and identifier sequences. Scenario
fixtures populate a world through its construction APIs instead of owning
parallel registries.

### Event runner and scenarios

`SimulationEngine<TEvent>` owns deterministic event ordering and clock
advancement. A scenario supplies an `ISimulationRuntime<TEvent>` that
reconciles systems, handles its event vocabulary, accrues time-based metrics,
and declares its stopping condition. The engine has no knowledge of Phase 1
materials, facilities, routes, or victory conditions.

`PhaseOneFixture` builds the proof-of-concept world. `PhaseOneScenario` is the
public facade that binds that fixture to the reusable engine and exposes
reports, event records, and snapshots. Additional scenarios can build a
different `SimulationWorld` and event vocabulary without modifying the runner.

### Physical and logistical activity

Ships travel, carry cargo, mine resources, dock, fight, and perform assigned work. Stations store materials and perform production according to their capabilities.

### Economic coordination

Offers, contracts, shortages, and production demand connect otherwise independent actors. Economic indexes should prevent every ship from repeatedly searching the entire galaxy.

### Strategic planning

Factions evaluate threats and opportunities at a lower frequency than ship movement. Plans create concrete objectives and orders that the physical simulation must carry out.

### Presentation

The application renders a view of the simulation and converts player input into commands. Rendering should not own authoritative simulation state.

## Update model

Different activities should use the update model appropriate to them:

- Fixed steps for activity that requires frequent interaction, such as active combat
- Scheduled events for arrivals, production completion, and other known future changes
- Triggered evaluation when inventories, orders, or threats materially change
- Low-frequency planning for faction strategy and diplomacy
- Visual interpolation between authoritative simulation changes

An entity remains individually simulated even when it is represented by scheduled events rather than continuous polling.

## Reproducibility

The simulation should use explicit seeds and controlled time advancement. Given the same initial state and commands, it should reproduce the same meaningful outcomes wherever practical.

This supports:

- Save and load reliability
- Automated balancing experiments
- Replaying difficult bugs
- Headless long-duration tests
- Comparing changes to simulation behavior

## Observability

Important decisions should produce structured reasons or events that the UI and development tools can inspect. The design should avoid retaining unlimited history, but it should preserve enough recent context to explain behavior.

The economic model is described in [Economy and production](economy.md), while faction-generated objectives are described in [Factions and strategic behavior](factions.md).

Concrete event ordering and initial invariants are defined in the [Phase 1 simulation specification](phase-1-simulation-spec.md).
