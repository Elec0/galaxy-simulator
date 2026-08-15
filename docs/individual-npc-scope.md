# Individual NPC scope

[Project index](../README.md) · [Vision](vision.md) · [Player experience](player-experience.md) · [Actor control and order lifecycle](actor-control-and-orders.md) · [Planned game-system inventory](planned-systems.md) · [Project task list](task-list.md)

## Purpose

This document records the boundary accepted for `TASK-015`. It establishes what
an individual NPC means in the initial game. It does not define autonomous
behavior, decision quality, or person-level simulation.

## Accepted decision

In the initial game, an individual NPC is an autonomous ship. A ship is an NPC
when its active control is autonomous. It remains the same persistent ship when
control changes, so a player-owned ship directed by an autonomous controller is
within this boundary, while a player-directed ship is not currently presented as
an NPC.

This is a gameplay and presentation term, not a new entity category. Existing
ship identity, actor control, order, snapshot, save, and deterministic commit
boundaries remain authoritative. `TASK-015` adds no NPC registry, person
record, crew record, or separate command path.

## Deferred personnel scope

The initial game has no individually simulated person, captain, crew member,
employee, population member, or character relationship. It does not infer
hidden person identities from ships, use crew as a prerequisite for ship
operation, or expose person-level skills, loyalty, dialogue memory, injury,
employment, passenger, or population mechanics.

`TASK-062` separately owns any later personnel or person-level decision.
