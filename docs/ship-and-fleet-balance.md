# Ship and fleet balance direction

[Project index](../README.md) · [Equipment and ship slots](equipment-and-ship-slots.md) · [Player experience](player-experience.md) · [Project task list](task-list.md)

## Purpose and decision status

The owner identified X4's Variety and Rebalance Overhaul (VRO) as a reference
for how different ship types should each have a useful place in fleet tactics.
This document records that high-level direction through a system-agnostic
synthesis of the mod author's documented design intent.

The guiding principle is that **a ship's value comes from its role in a fleet
and the situation it faces**. Distinct capabilities, meaningful vulnerabilities,
and complementary roles should make composition and coordination matter.

This is balance direction, not a combat implementation contract. The VRO role
examples below illustrate the reference; they do not select Galaxy Command's
ship taxonomy, numerical balance, combat algorithms, or equipment rules.
Those decisions remain with owner review in `TASK-046`, `TASK-068`, and
`TASK-056`. Continued one-ship play remains part of the project direction.

## Give each ship a useful job and a meaningful vulnerability

Small craft provide harassment, distraction, and concentrated attacks. Medium
combatants deliver damage but need protection. Frigates supply fire support or
carry smaller craft. Destroyers handle a broad range of threats. Battleships
specialize in attacking large, durable targets. Carriers and support ships
extend the fleet's reach and endurance.

These roles create dependencies: powerful ships need escorts, attackers need
opportunities, and escorts need something worth protecting. A balanced fleet
covers weaknesses that become dangerous when a ship operates alone.
[VRO ship design notes](https://sites.google.com/view/vrowiki/ships-changes)

## Make scale change how ships fight

Increasing size brings greater durability, heavier weapons, and longer reach,
alongside reduced maneuverability. Large ships establish a threatening area
around themselves; smaller ships exploit mobility and openings.

Size therefore changes tactical behavior. A larger ship offers capabilities
that smaller ships cannot easily reproduce, while still having weaknesses
smaller craft can exploit.
[VRO ship design notes](https://sites.google.com/view/vrowiki/ships-changes)

## Balance weapons around their intended targets

Weapon effectiveness depends on more than damage output. Range, accuracy,
projectile behavior, and effectiveness against shields, hulls, or components
determine which targets a weapon can handle.

Heavy weapons threaten large ships but struggle against small, agile targets.
Lighter weapons provide protection against those targets. Specialized weapons
can weaken defenses or disable equipment, creating opportunities for other
attackers.

This makes equipment selection a tactical commitment. A ship configured to
destroy heavy targets gives up some ability to defend itself against lighter
threats.
[VRO weapon design notes](https://sites.google.com/view/vrowiki/weapons-changes)

## Preserve specialization through equipment limits

Ships cannot freely carry every useful weapon. Different mounting capabilities
and restrictions on specialized ammunition preserve distinct combat roles.

A small bomber can threaten a much larger opponent because it carries an
appropriate specialist weapon. That capability comes with constraints:
ammunition capacity, delivery requirements, and cost. More destructive
ammunition becomes disproportionately expensive, making concentrated striking
power a resource decision as well as a combat decision.
[VRO missile design notes](https://sites.google.com/view/vrowiki/missiles-changes)

## Make support capacity part of combat strength

Detection, docking space, and resupply capacity contribute to fleet
effectiveness. A support vessel's value includes how long it lets other ships
remain useful.

Faction differences add another layer: some favor speed and striking power,
others durability, shields, or carrier operations. These tendencies produce
different ways of assembling a capable fleet.
[VRO ship design notes](https://sites.google.com/view/vrowiki/ships-changes)

## Make success depend on coordination

The player's ship participates in the same combat relationships as the rest
of the fleet. Drawing attention, supporting allies, and choosing suitable
engagements matter.

VRO creates these incentives primarily through ship and equipment
characteristics; its stated scope leaves combat AI and targeting logic
unchanged. The broader design lesson is that coherent strengths, weaknesses,
and equipment choices can encourage fleet tactics through the consequences of
combat itself.
[VRO overview](https://sites.google.com/view/vrowiki/Home)

## Relationship to Galaxy Command's existing design

The [equipment review](equipment-and-ship-slots.md) already records the owner's
direction for size-based modules, compatibility restrictions, equipment
durability, flexible refitting, and granular modifications with tradeoffs. It
also allows better equipment tiers to be straight upgrades. This balance
reference does not remove those tiers or settle unanswered fitting questions.
Role specialization can guide the tradeoffs between equipment families while
upgrades remain possible within a family.

`TASK-046` owns combat resolution and the rules that make these relationships
work. `TASK-068` owns slots, compatibility, and installed capabilities.
`TASK-056` owns ship acquisition, replacement, upgrades, and continued
one-ship viability. Prices and exchange remain with `TASK-055`, docking with
`TASK-051`, and repair with `TASK-058`.

Exact class boundaries, faction doctrines, weapon behavior, ammunition
economics, support capabilities, and tuning values require owner decisions
when their existing tasks are taken up. The VRO examples are reference
information for those decisions, not approval to implement them now.
