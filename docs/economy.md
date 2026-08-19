# Economy and production

[Project index](../README.md) · [Inventory and cargo](inventory-and-cargo.md) · [Simulation architecture](simulation-architecture.md) · [Time and pacing](time-and-pacing.md)

## Purpose

The economy provides the material foundation for expansion and conflict. Production should respond to inventories, transportation, capacity, and loss rather than generating major assets from abstract faction funds alone.

## Production chain

A production chain broadly contains:

1. Resource acquisition
2. Transportation to processing capacity
3. Refining or intermediate manufacturing
4. Component production
5. Final construction or consumption

The exact commodities and recipes are intentionally unspecified in this first draft. The initial set should be small enough that shortages and dependencies remain understandable.

## Stations

A station may provide storage, production, trade, repair, construction, defense, or some combination of these functions. Its behavior depends on installed capabilities and available inputs.

Stations should expose:

- Current inventory and reserved inventory
- Input demand and available output
- Active and queued production
- Throughput and limiting factors
- Docking or loading congestion where relevant

## Trade and logistics

Material must move between locations. Trade may be initiated through market offers, contracts, faction logistics, player orders, or standing automation.

The initial headless simulation will coordinate transportation through a central job board. Producers and consumers publish available supply and unmet demand. Eligible freighters select jobs using a deterministic score based on factors such as distance, urgency, and compatible cargo quantity.

Transport jobs reserve source inventory and cargo capacity so multiple ships cannot claim the same material. Jobs may be partially fulfilled, cancelled, or returned to the board according to explicit state transitions.

Prices can help allocate scarce goods, but price alone should not be the only coordination mechanism. Factions may reserve strategic resources, prioritize military deliveries, or operate internal logistics without treating every transfer as a public-market purchase.

Currency and market pricing are excluded from Phase 1. The first milestone tests the physical economy before adding price feedback.

All trade uses one unified currency called Credits. Credits are an economic
ledger value rather than physical inventory, consume no cargo capacity, and do
not move through material or item transfers. `TASK-055` defines Credit balance
ownership and conservation, pricing, contracts, and atomic settlement with
physical delivery.

## Construction

Shipyards and construction facilities consume delivered materials and production work. A completed ship becomes a real entity with an owner and an initial assignment.

Losses therefore create replacement demand. Destroying infrastructure or interrupting deliveries can delay replacement and influence strategic outcomes.

## Resilience

The economy must tolerate disruption without relying on unexplained spawning. Possible resilience mechanisms include substitution, stockpiles, changed priorities, alternate suppliers, reduced consumption, and recovery construction.

Which mechanisms are available—and under what conditions—remains a balancing decision.

## Evaluation questions

- Can an economy bootstrap from the chosen starting conditions?
- Can it recover after losing an important producer?
- Are shortages visible and understandable?
- Do transportation losses matter without causing constant collapse?
- Can new production create meaningful regional change?

Faction use of the economy is covered in [Factions and strategic behavior](factions.md).

The initial logistics and production rules are defined in the [Phase 1 simulation specification](phase-1-simulation-spec.md).
