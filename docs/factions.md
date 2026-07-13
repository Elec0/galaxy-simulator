# Factions and strategic behavior

[Project index](../README.md) · [Economy](economy.md) · [Player experience](player-experience.md)

## Purpose

Factions turn economic and military conditions into long-term action. They should behave autonomously while remaining understandable enough that the player can anticipate, manipulate, or oppose them.

## Strategic concerns

A faction may evaluate:

- Access to important resources
- Production capacity and bottlenecks
- Trade-route safety
- Military strength and replacement capacity
- Territorial opportunities
- Relationships and current commitments
- Known player and rival activity

These concerns produce priorities rather than directly spawning results.

## From plans to actions

A strategic objective should decompose into tasks the rest of the simulation can execute. For example, a desire to protect a trade corridor may create requirements for reconnaissance, additional escorts, replacement ships, fuel or supplies, and patrol orders.

If the required ships or materials are unavailable, the faction must adapt, wait, reprioritize, or choose a smaller plan.

## Decision model

The initial design should favor explicit rules and utility scores over opaque behavior. Decisions should record their major inputs so they can be inspected during development and, where appropriate, by the player.

Faction planning should occur at controlled intervals or in response to significant events. It should not continuously reconsider every possible action.

## Asymmetry

Factions may eventually differ through priorities, risk tolerance, preferred industries, doctrine, diplomacy, or organizational structure. Early prototypes should prove that one general planning model works before adding extensive faction-specific behavior.

## Player relationship

The player may trade with, work for, join, influence, threaten, or compete with factions. The exact political model remains open, but factions should respond to the player's material impact rather than only to scripted reputation changes.

Timing of strategic evaluations is covered in [Time and pacing](time-and-pacing.md).
