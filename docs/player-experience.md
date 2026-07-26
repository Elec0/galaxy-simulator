# Player experience

[Project index](../README.md) · [Vision](vision.md) · [Navigation and spatial architecture](navigation-architecture.md) · [Economy](economy.md)

## Starting position

The player begins with a single owned ship and limited resources. The exact starting roles remain open, but the command model should support activities such as hauling, mining, scouting, escorting, and trading.

The player does not steer the ship directly. Instead, they select it on the system or galaxy map and issue orders.

On a system map, the player may choose a position or entity as a destination.
For travel to another system, navigation composes local movement with the
required gate or other connector transitions. The player specifies the desired
destination rather than manually encoding those travel legs in the order.

## Command model

Initial orders may include:

- Move to a location or system
- Dock at a station
- Buy, sell, load, or unload cargo
- Mine or collect a resource
- Escort or follow another ship
- Patrol or defend an area
- Attack, withdraw, or avoid hostiles

As ownership grows, the same interactions apply to groups, fleets, routes, and standing orders. Automation should be optional: a player may closely direct one ship or delegate routine work across many ships.

## Information model

Selecting an entity should expose its current state and relevant history. Depending on the entity, this may include:

- Current order and reason for that order
- Route and estimated arrival time
- Cargo, inventory, or production queue
- Owner, relationships, and known threats
- Recent transactions and material sources
- Current faction or player objective

The interface should distinguish what the player knows from the galaxy's complete internal state. The eventual role of sensors, scouting, and stale information remains an open design question.

## Progression

Progression is primarily economic and organizational rather than character-level. Potential paths include:

- Improving or replacing the initial ship
- Purchasing additional ships
- Establishing repeatable trade and mining operations
- Building stations and production capacity
- Hiring or configuring automation
- Forming fleets and controlling territory
- Cooperating or competing with factions

The game should not require expansion. Remaining a small independent operator should stay viable and meaningful.

## Player impact

Player actions enter the same systems used by autonomous actors. Buying materials can create shortages, destroying a freighter can interrupt production, and adding a factory can alter regional trade.

See [Factions and strategic behavior](factions.md) for how non-player powers respond.
