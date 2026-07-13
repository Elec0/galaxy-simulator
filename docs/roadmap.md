# Initial roadmap

[Project index](../README.md) · [Technical direction](technical-direction.md) · [Vision](vision.md)

## Phase 1: Headless economic loop

Build a small deterministic scenario containing a few connected locations, resource production, industrial processing, cargo transport, and final construction.

Success means materials move through a complete chain and shortages have understandable causes.

The scenario, rules, invariants, and acceptance criteria are defined in the [Phase 1 simulation specification](phase-1-simulation-spec.md).

## Phase 2: Persistent ships and orders

Represent individually owned ships with routes, cargo, tasks, and scheduled travel. Add player-issued commands and basic autonomous logistics.

Success means one command model can control both a single ship and a small group.

## Phase 3: Minimal map application

Render systems, connections, ships, stations, selections, routes, and time controls. Expose enough state to inspect what each entity is doing.

Success means the headless scenario can be understood and influenced through the map.

## Phase 4: Faction planning and conflict

Add competing faction priorities, military construction, patrols, attacks, losses, and replacement demand.

Success means a logistical disruption changes a faction's achievable plans without scripted outcomes.

## Phase 5: Scale and pacing evaluation

Run increasingly large galaxies at multiple requested simulation speeds. Profile bottlenecks and refine event scheduling, indexing, storage, and update frequency.

Success criteria require explicit scale targets, which have not yet been selected.

## First vertical-slice test

The initial slice should demonstrate a complete causal story:

1. Resources are extracted.
2. Freighters deliver them to production.
3. Components reach a construction facility.
4. A faction orders and builds a ship for a stated reason.
5. Disrupting one link delays construction and changes faction behavior.
6. The UI explains the resulting shortage and decision.

## Deferred work

- Large content catalogs
- Detailed faction personalities
- Narrative campaigns
- Advanced diplomacy
- Modding support
- Final visual style
- Multiplayer

These should not precede evidence that the core simulation and command loop are enjoyable.
