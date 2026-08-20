# Relational gameplay model

[Project index](../README.md) · [Vision](vision.md) · [Player experience](player-experience.md) · [Economy](economy.md) · [Time and pacing](time-and-pacing.md) · [Relational simulation architecture](relational-simulation-architecture.md) · [Project task list](task-list.md)

## Purpose

The galaxy needs powers that own assets, pursue interests, react to the player,
and have meaningful relationships with one another. Before deciding how those
concepts are represented in simulation code, this document defines the gameplay
they should produce.

This is the approved first design pass for `TASK-035`. It uses **power** as
a provisional term for any player or non-player participant that can own assets
and have relationships. It does not decide whether a power is ultimately called
an organization, faction, polity, company, government, or something else. It
also does not decide whether those terms describe one concept or several.

**Decision status:** Accepted by the project owner on 2026-08-05.

**Implementation status:** No implementation was added by `TASK-035`. `TASK-012`
is translating this gameplay contract in
[Relational simulation architecture](relational-simulation-architecture.md)
without extending it.

## Starting reference: X4

The first model should be similar enough to *X4: Foundations* to provide a
recognizable baseline without copying its mechanics exactly. In X4:

- Factions own ships and stations, compete with enemies, support allies, expand,
  and may claim sectors.
- Actions such as trading, completing missions, attacking enemies, committing
  crimes, or attacking faction property affect the player's reputation.
- Reputation bands change practical treatment, including hostility, docking and
  trade access, ship and equipment access, licenses, discounts, and information.
- Territorial ownership gives a faction practical authority, including rules
  about illegal wares and policing.

These features are valuable because relationships change what the player can
do, not merely the color of a name. Galaxy Command should retain that causal
connection while improving legibility, material consequences, and the ability
for the galaxy to react without relying entirely on scripts.

Reference material:

- [X4 factions](https://wiki.egosoft.com/X4%20Foundations%20Wiki/Manual%20and%20Guides/Objects%20in%20the%20Game%20Universe/Factions/)
- [X4 reputation effects](https://wiki.egosoft.com/X4%20Foundations%20Wiki/Manual%20and%20Guides/Objects%20in%20the%20Game%20Universe/Factions/%28Player%29/Reputation%20Effects/)
- [X4 sector ownership](https://wiki.egosoft.com/X4%20Foundations%20Wiki/Manual%20and%20Guides/Objects%20in%20the%20Game%20Universe/Systems%2C%20Sectors%20and%20Zones/Sector%20Ownership/)

## Desired player experience

Relationships should make the galaxy easier to understand and more interesting
to influence. The player should be able to answer:

- Who owns this ship, station, facility, or territory?
- How does that power currently regard me?
- What am I allowed to do here, and why?
- What did I do to improve or damage this relationship?
- Who is hostile to whom, and what behavior follows from that hostility?
- What does this power currently want, and how is that goal constrained by its
  actual ships, facilities, materials, and knowledge?
- What could I do to change the situation?

The system should support several play styles. A player may remain an
independent trader, become a trusted contractor, exploit a conflict without
joining it, align with one power, antagonize several powers, or grow into a
power that owns fleets, stations, and territory.

Relationship changes should create opportunities and consequences without
forcing the player into territorial expansion or faction membership.

## Design principles

### Relationships have gameplay consequences

A relationship should affect concrete permissions and behavior. Possible
consequences include:

- Whether assets ignore, warn, inspect, intercept, or attack one another
- Docking and trade access
- Access to contracts, missions, equipment, designs, or services
- Price treatment or commercial priority
- Permission to build, mine, salvage, patrol, or police in controlled space
- Information sharing
- Assistance when threatened
- Whether an act is treated as an accident, crime, provocation, or act of war

Not every relationship band needs every consequence, but a relationship that
changes nothing should not be prominent player-facing state.

### Material actions matter

Relationships should respond to what actually happens in the simulation.
Delivering scarce material, protecting freighters, destroying infrastructure,
violating territorial rules, and completing an explicitly hostile objective
should matter because those actions affect a power's ability to pursue its
goals. Ordinary economic activity does not create negative standing merely
because it benefits another power.

Scripted rewards and penalties may still exist, but they should name a clear
reason and use the same player-facing relationship model.

### Treatment is predictable

The player should normally be warned before crossing an important threshold.
The interface should explain current treatment, the most important recent
causes, and the likely consequence of a proposed action when that consequence
is reasonably knowable.

Surprise can come from incomplete information, conflicting interests, or a
deliberate betrayal. It should not come from hidden arithmetic or an unexplained
global hostility switch.

### Powers obey the same material world

Non-player powers should not receive ships, control territory, or recover from
losses solely because a relationship value says they should. Relationships and
strategic goals may create demand, permissions, priorities, and orders, but
execution still depends on real assets, production, transport, travel, and
combat outcomes.

### Information is part of the game

The simulation may know the complete truth while a power or player knows only a
subset. Decisions should be based on information available to the decision
maker. The player should be able to distinguish confirmed information,
reported information, and old information when that distinction affects a
choice.

The detailed observation and staleness model remains `TASK-020`. This task must
still decide what relationship information is public, private, or discoverable.

## Accepted relationship experience

### Standing

**Accepted decision:** Give each power a directional standing toward another
power. Present it through a small number of meaningful bands rather than making
the raw score the primary concept.

The initial bands are:

| Band | Expected treatment |
| --- | --- |
| Hostile | Property may be attacked or seized; ordinary access is denied |
| Adversarial | Access is heavily restricted; suspicious actions trigger intervention |
| Neutral | Basic peaceful access and trade may be available |
| Favorable | Better access, contracts, information, and commercial treatment |
| Allied | High trust, strategic access, and possible mutual assistance |

The initial bands are Hostile, Adversarial, Neutral, Favorable, and Allied. The
exact thresholds remain open. A continuous value may support progress within a
band, but players should make decisions based on understandable treatment
rather than memorizing numbers.

Standing is directional. One power may value or tolerate another more than the
feeling is returned. This supports unequal dependence, fear, opportunism, and
one-sided access policies.

### Diplomatic condition

**Accepted decision:** Do not make standing alone declare wars or alliances.
Peace, war, alliance, ceasefire, and similar mutual commitments are explicit
diplomatic conditions. They may influence standing and treatment, but changing
one does not silently rewrite the other.

This distinction allows:

- Two powers to dislike one another without being at war
- A reluctant alliance based on shared need
- A ceasefire without friendship
- Friendly powers to compete economically
- A war to end while distrust remains

The first playable version may support only peace and war. More detailed
diplomacy should be added only when it creates a concrete player choice.

### Permissions and credentials

**Accepted decision:** Use relationship bands to make permissions available, but
represent important permissions as explicit grants. Examples include military
procurement, restricted-space access, construction rights, policing authority,
or privileged information.

This produces the X4-like satisfaction of earning access while allowing
special cases such as a temporary permit, a mission-specific clearance, or a
revoked license. Ordinary docking and trade may depend directly on current
treatment rather than requiring a separate credential.

Initial grants depend on standing thresholds. A grant can be issued only while
its threshold is satisfied and remains usable only while that threshold remains
satisfied. Later gameplay may introduce exceptional grants with different
rules when a concrete need exists.

Restricted space is an enforceable boundary. A ship approaching a known
restricted boundary should receive enough information to avoid an accidental
breach. Once an unauthorized ship crosses that boundary, the controlling power
may fire upon it and attempt to destroy it. This rule allows restricted space to
support blockades and sieges rather than acting only as a reputation gate.

### Territorial authority

**Accepted decision:** Controlled territory should grant practical authority, not
absolute ownership of everything inside it. The controlling power may set
access rules, define prohibited activity, police violations, grant extraction
or construction rights, and respond to military presence.

Other powers may still own stations or ships within controlled territory.
Territorial control should therefore create relationships and conflicts rather
than erasing existing ownership.

The rule that determines territorial control is deferred until stations,
claiming infrastructure, and combat provide concrete candidates. Control should
ultimately depend on material presence and the ability to maintain it.

### Reputation change

**Accepted decision:** Change standing through attributed, explainable outcomes.
Potential positive causes include:

- Completing a requested objective
- Delivering scarce or strategically valuable material
- Trading consistently without violating restrictions
- Defending assets or territory from a recognized threat
- Destroying a declared enemy when the act is observed and welcomed
- Returning property, rescuing assets, or honoring an agreement

Potential negative causes include:

- Attacking, capturing, sabotaging, or destroying property
- Trespassing after warning
- Smuggling or violating territorial rules
- Breaking a contract or abandoning a commitment
- Completing a known mission or other explicitly defined action against the
  power's interests

Routine profitable trade should build familiarity or modest goodwill, but it
should not by itself turn every frequent customer into an ally. High trust
should require increasingly meaningful actions or explicit commitments.

Piracy introduces additional questions about attribution, victims, witnesses,
jurisdiction, stolen property, privateering, and retaliation. Those questions
are separated into `TASK-036` rather than being answered implicitly here.

**Accepted decision:** Standing should not drift toward neutral merely because
time passes. If temporary anger, suspicion, or gratitude is desired, model it
as a short-lived incident or modifier with an explained expiry. Durable history
should remain durable until something changes it.

### Third-party consequences

**Accepted decision:** Do not automatically copy allies and enemies through the
relationship graph. Economic activity with another power does not cause
negative standing, including when the trading partner is an enemy. A
third-party penalty requires an explicit, predefined action such as completing
a known hostile mission or violating a declared commitment. It applies only
when:

1. The second power knows or credibly believes the action occurred.
2. The action is one of the defined causes that power treats negatively.
3. The player could reasonably understand that consequence before committing.

This avoids surprising cascades where one transaction changes half the galaxy.
It also gives information, secrecy, and attribution meaningful roles.

## Ownership, control, and affiliation

Gameplay needs to distinguish at least three ideas even if the final terminology
uses fewer top-level concepts:

- **Ownership:** Who possesses an asset and receives its benefits or losses?
- **Control:** Who currently chooses the asset's orders?
- **Affiliation:** Which larger group, if any, the owner or controller is
  associated with?

**Accepted decision:** Ownership should not automatically answer control or
affiliation questions. A hired ship, delegated fleet, captured station,
scripted takeover, or protected independent trader may separate them.

The first version does not need corporate hierarchies, citizens, subsidiaries,
or internal politics. The design should not prevent those possibilities by
declaring too early that every asset owner is also a sovereign faction.

### Player position

**Accepted decision:** The player begins as an independent trader capable of
owning assets and receiving relationships, similar to X4's player faction
behavior, without requiring the fiction that a one-ship operator is already a
state.

Other powers can regard the player's operation as a single accountable party
for ordinary gameplay. The game may later allow the player to found, join, or
represent a larger organization, but that is not required for the initial
relationship loop.

Actions by a player-owned asset should normally be attributed to the player's
operation. Concealed identity, rogue subordinates, false flags, and individual
criminal responsibility are deferred until they support a concrete gameplay
need.

## Strategic goals and behavior

A power's goals should explain why it creates demand or issues orders. Examples
include:

- Secure access to a scarce resource
- Replace lost transport capacity
- Protect a production chain or trade corridor
- Expand construction capacity
- Defend or contest territory
- Improve relations with a useful neighbor
- Deter, contain, or weaken a threat

**Accepted decision:** Goals should create requirements for ordinary gameplay
systems rather than directly creating results. A goal to protect a corridor may
request reconnaissance, escorts, replacement ships, supplies, and patrol
orders. If those resources are unavailable, the power must wait, reduce the
plan, reprioritize, or fail.

This task defines what goals should mean to the player. `TASK-026` owns the
later planning model that selects goals and turns them into executable work.
`TASK-018` retains missions, player objectives, victory, and defeat.

Faction-specific personality or doctrine should change priorities and choices,
not bypass shared relationship, economy, command, or combat rules.

## Relationship information and legibility

**Accepted decision:** Show the player a qualitative standing band, progress
toward nearby treatment changes, current permissions, diplomatic condition,
and a bounded list of important causes. Exact internal arithmetic may remain a
development view.

Information can fall into three initial categories:

| Category | Recommended visibility |
| --- | --- |
| Public | Declared wars, public alliances, territorial controller, published access rules |
| Known to the player | The player's treatment, granted permissions, warnings, and directly observed incidents |
| Discoverable | Another power's goals, private agreements, assessments, and unobserved incidents |

The player should not need espionage to learn whether a station will allow
docking or whether local authorities consider the player's ship hostile.
Conversely, complete knowledge of every strategic goal would remove much of the
value of scouting, trade intelligence, and political discovery.

## Representative gameplay scenarios

These scenarios constrain the simulation architecture and should later become
the basis for gameplay acceptance tests.

### Independent trader

The player begins neutral to a regional power. Repeated deliveries address a
real shortage and gradually improve treatment. Better standing unlocks more
valuable contracts and restricted equipment, but routine commerce alone cannot
produce an alliance.

### Border incident

The player approaches restricted space. The controlling power identifies the
boundary and warns the player that breaching it will provoke lethal force.
Turning away resolves the encounter. Crossing without the required grant makes
the offending ship a target for destruction, allowing the same rule to support
a blockade or siege.

### Profiting from conflict

Two powers are at war. The player trades with both. Economic activity does not
cause negative standing merely because one trading partner is an enemy. A
negative consequence requires a known mission, a broken commitment, or another
explicitly defined hostile action.

### Material assistance

A power loses freighters and cannot supply a shipyard. The player escorts a
convoy or supplies replacement material. The resulting improvement is tied to
the avoided shortage and is more meaningful than an arbitrary mission reward.

### Growing into a territorial power

The player eventually establishes infrastructure in unclaimed space. If the
game recognizes territorial control, it grants authority and responsibilities:
setting limited rules, protecting traffic, and accepting diplomatic
consequences. The player is not required to take this path.

### Relationship conflict

The player has favorable standing with two rival powers. One requests action
against the other. The game communicates the likely consequences before the
player commits. Refusing may disappoint the requester; accepting may harm the
other relationship if the act is observed and attributed.

## Accepted project-owner decisions

The project owner accepted the following initial answers on 2026-08-05. These
decisions establish the first gameplay model without choosing its code shape.

### Q1. What kinds of participant should exist at the start?

Use one provisional gameplay concept, a power that can own assets and have
relationships. The player begins as an independent trader. Introduce distinct
participant concepts only when a scenario proves that ownership, diplomacy,
affiliation, or strategy must belong to different parties.

### Q2. Should standing be directional?

Yes. Mutual diplomatic conditions coexist with directional standing and
treatment.

### Q3. Should the player see a number?

Lead with named bands, concrete consequences, and visible progress. Keep exact
arithmetic out of the normal interface unless playtesting shows that players
cannot plan without it.

### Q4. Which relationship bands are needed?

Begin with Hostile, Adversarial, Neutral, Favorable, and Allied. Each band must
produce distinct treatment or a distinct player choice.

### Q5. Can routine trade eventually create an alliance?

No. Trade can reach favorable commercial standing, while alliance requires
high-impact assistance, shared commitments, or explicit diplomatic action.
Consistent behavior may cause another power to court the actor or propose an
alliance. Either party may initiate that step, regardless of their relative
size or strength.

### Q6. How forgiving should accidental harm be?

Use graduated incidents. Minor or isolated harm produces a warning, temporary
suspicion, restitution opportunity, or limited penalty. Repeated or severe
harm escalates treatment. Deliberate attacks and destruction remain serious.
A clearly marked restricted-space breach after warning is deliberate for this
purpose and may provoke lethal force.

### Q7. Should relationship state decay over time?

Durable standing does not decay automatically. Temporary incidents expire in a
visible and explainable way.

### Q8. How much should helping an enemy matter?

Economic activity does not produce negative standing merely because it helps
an enemy. Negative third-party consequences initially come only from known
missions or other explicit, predefined actions whose consequences can be
communicated to the player.

### Q9. Should permissions unlock automatically at thresholds?

Ordinary treatment may change automatically. Important rights are explicit
grants whose availability and continued use depend on standing thresholds.

### Q10. Should non-player diplomacy change during the first version?

Allow authored starting conditions and explicit changes caused by major
gameplay or scripts, but defer fully autonomous diplomacy until the economic
and military consequences can be evaluated. Static relationships are not the
final direction.

### Q11. What does the player know about other powers?

Public diplomatic conditions and territorial rules are known. Strategic goals,
private attitudes, and unobserved incidents require contact, observation, or
information sharing. `TASK-020` defines the detailed acquisition and staleness
rules.

### Q12. Can the player join another power?

Defer formal membership. Initial progression works through standing, contracts,
permissions, and ownership without requiring a membership hierarchy.

### Q13. Can the player create a faction or claim territory?

Preserve this as an eventual path, but do not require it for the one-ship or
small-fleet game. Define claiming only after stations and territorial conflict
exist.

### Q14. Are reputation and diplomacy enough to describe every relationship?

Treat them as the initial public model. Add obligations, debts, fear,
ideological alignment, commercial dependence, or personal relationships only
when a concrete scenario cannot be expressed clearly without one of them.

## Boundaries with other tasks

- `TASK-012` will translate approved concepts into deterministic simulation
  state and behavior boundaries. It must not introduce new gameplay policy.
- `TASK-018` owns missions, persistent player objectives, victory, and defeat.
- Completed `TASK-019` defines the shared physical contract for interactions
  between moving ships; `TASK-046` owns combat, surrender, capture, and
  destruction policy.
- `TASK-020` owns detailed player knowledge, observation, and staleness.
- `TASK-026` owns strategic planning and the conversion of goals into work.
- `TASK-030` owns mutable connector availability and access once this design
  supplies a concrete relational requirement.
- `TASK-032` owns semantic economic facts.
- `TASK-036` owns piracy-specific attribution and relationship consequences.

## Explicitly deferred

This first accepted pass does not decide:

- Final terminology or data structures
- Internal organizations, subsidiaries, governments, or citizenship
- Individual NPC relationships
- Espionage, covert identity, false flags, or propaganda
- Detailed law, crime, fines, warrants, contraband catalogs, or piracy rules
- Negotiation interfaces or treaty drafting
- Autonomous declaration of war and peace
- Territory-claim algorithms
- Combat escalation, capture, surrender, or reparations
- Save representation or content format
- Multiplayer, remote authority, or reputation synchronization

These should be promoted only when an approved gameplay scenario requires
them.
