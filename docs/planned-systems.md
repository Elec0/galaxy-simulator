# Planned game-system inventory

[Project index](../README.md) · [Vision](vision.md) · [Player experience](player-experience.md) · [Initial roadmap](roadmap.md) · [Project task list](task-list.md)

## Purpose and boundary

This document is the exhaustive system inventory produced by `TASK-044` and the
record of the completed scope-gap review in `TASK-043`. It is not an
implementation plan or a promise that every deferred system belongs in the
first release.

The inventory is exhaustive relative to the experience and direction stated in
the current project documentation. It includes:

- systems already implemented or designed;
- systems named by the player experience, roadmap, architecture, or tracker;
- systems necessarily implied by those promises, such as an application flow
  for starting, loading, and recovering a game; and
- explicitly deferred systems that the project expects to revisit beyond the
  current roadmap.

It does not add every feature that might appear in the wider space-simulation
genre. A feature with no connection to the documented direction is outside the
inventory until the project owner adds it. Multiplayer and remote simulation
authority are excluded by the project's single-player direction rather than
treated as deferred systems.

Each row names the most specific current owner. A task owns unresolved work; a
design document owns an accepted boundary or an explicit deferral. Rows marked
"inventory deferral" have no approved design task and were deliberately
retained as optional, evidence-gated, or prerequisite-gated work by `TASK-043`.
That label must not be interpreted as permission to infer a contract during
implementation.

## Player application and presentation

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Application lifecycle and shell | Start the desktop application, choose a new or saved session, leave a running session safely, and surface fatal startup failures. | `TASK-049`; `TASK-040` separately owns recovery from invalid sessions or content. |
| Static new-game creation | Select validated packages and a static scenario, then create one complete session through the production admission boundary. | [Gameplay content](gameplay-content.md); implementation in `TASK-048`. |
| Procedural new-game creation | Convert player-selected generation inputs and a deterministic seed into the same validated composition used by static starts. | `TASK-047`, explicitly deferred. |
| Save and load | Capture, encode, publish, validate, migrate, and directly restore complete authoritative sessions. | [Authoritative save boundary](authoritative-save-boundary.md), [save format and migration](save-format-and-migration.md), completed `TASK-014` and `TASK-022`, then `TASK-037`. |
| Save-slot experience and autosave | Present slots, choose autosave cadence and retention, and resolve local or cross-device file conflicts without weakening save validation. | `TASK-050`; mechanics remain outside the authoritative format owned by [save format and migration](save-format-and-migration.md#deferred-choices). |
| Failed-session and content recovery | Explain poisoned live state, invalid checkpoints, and missing or incompatible content, then return the player to a verified session. | `TASK-040`. |
| Map rendering and view hierarchy | Render galaxy topology, individual system spaces, connectors, ships, stations, motion, and useful overlays at appropriate zoom levels. | `TASK-049`, building on [technical direction](technical-direction.md) and [presentation snapshots](presentation-snapshots.md). |
| Input, selection, and inspection | Translate map interaction into commands; support selection, focus, entity details, relevant history, and stale-selection handling. | Completed `TASK-010` supplies the ship-selection foundation; `TASK-049` owns the broader application experience and `TASK-033` owns group intent. |
| Group, fleet, and route interaction | Distinguish transient selections from persistent groups, issue shared intent, and display atomic or partial outcomes. | `TASK-033`; fleet formations remain an inventory deferral under navigation and movement. |
| Notifications and explanation views | Present facts, command outcomes, shortages, decisions, and bounded history with useful grouping and priority. | [Semantic game facts](semantic-game-facts.md), `TASK-025` for retained explanation, and `TASK-049` for application presentation. |
| Time controls | Pause, resume, select simulation speed, buffer input at quiescent boundaries, and automatically pause for response-required dialogue when enabled. | [Time and pacing](time-and-pacing.md), completed `TASK-013`; application implementation in `TASK-038`. |
| Localization and text layout | Resolve stable presentation keys into locale-sensitive wording, formatting, pluralization, fonts, right-to-left layout, and expansion-safe UI. | Completed `TASK-045`; see [internationalization and localization](internationalization-and-localization.md). |
| Accessibility | Define accessible input and presentation behavior without placing accessibility settings in authoritative simulation state. | [Internationalization and localization](internationalization-and-localization.md) establishes the text and layout baseline; comprehensive modes and acceptance criteria belong to `TASK-061`. |
| Visual effects and final visual style | Clarify state, movement, conflict, and feedback while preserving the simulation-first 2D presentation direction. | Explicitly deferred by [vision](vision.md), [roadmap](roadmap.md), and [presentation snapshots](presentation-snapshots.md). |
| Audio and music | Provide non-authoritative sound, alert, ambience, and music behavior if the final presentation requires it. | Inventory deferral beyond the current roadmap. No simulation contract is implied. |
| Local player preferences | Retain presentation, accessibility, pacing, and other local settings while separating them from authoritative game state. | `TASK-050`, coordinated with `TASK-038`, completed `TASK-045`, and `TASK-061`; [authoritative save boundary](authoritative-save-boundary.md) excludes purely local settings. |

## Content, authoring, and compatibility

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Content packages and manifests | Describe explicit content documents, dependencies, package identity, and optional presentation assets through one built-in and external path. | [Gameplay content](gameplay-content.md), completed `TASK-023` and `TASK-063`; built-in integration remains `TASK-048`. |
| Declarative definitions and catalogs | Validate stable qualified identities and construct immutable catalogs for materials, designs, principals, policies, and later domain content. | [Gameplay content](gameplay-content.md); completed `TASK-063`. |
| Content validation and diagnostics | Headlessly parse, validate, resolve, canonicalize, fingerprint, and report content without publishing partial state. | [Gameplay content](gameplay-content.md); completed `TASK-063`. |
| Content compatibility and migration | Detect incompatible, missing, renamed, replaced, or changed definitions referenced by saved sessions and apply only explicit migrations. | `TASK-037`. |
| Authored scenario tooling | Support rapid edit, validate, and rerun workflows for static scenarios without creating a weaker validation path. | Completed `TASK-063` supplies the basic headless command; built-in scenario integration remains `TASK-048`; watch mode, editor integration, and richer tools are explicitly deferred by [gameplay content](gameplay-content.md). |
| Mod distribution and management | Package, discover, install, update, sign, and select external content and its dependencies. | Explicitly deferred by [gameplay content](gameplay-content.md); no remote repository or mod-manager contract is planned yet. |
| External scripted content | Add constrained authored behavior only after triggers, capabilities, scheduling, determinism, persistence, and trust are defined. | `TASK-017`; external executable assemblies remain excluded by [gameplay content](gameplay-content.md). |

## Simulation foundation and lifecycle

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Deterministic event agenda and time | Order scheduled work by stable time, phase, and sequence; prevent completed phases from reopening. | [Simulation architecture](simulation-architecture.md), completed `TASK-001` and `DONE-004`. |
| Persistent game session | Own the authoritative application runtime, command entry, advancement, snapshots, facts, and checkpoint boundary. | Completed `TASK-003`, `TASK-004`, `TASK-014`, and `TASK-034`; [runtime orchestration](runtime-orchestration.md). |
| Gameplay commands | Admit player, autonomous, dialogue, and script intent through one deterministic command and outcome model. | Completed `TASK-002`; [actor control and orders](actor-control-and-orders.md). |
| Domain orchestration and commit | Separate stable reads and batchable evaluation from typed effects and deterministic authoritative commit. | Completed `TASK-009`; [runtime orchestration](runtime-orchestration.md). |
| Entity identity, spawning, and removal | Allocate stable identities, validate registration, materialize construction, remove entities, and invalidate cross-owner work. | Completed `TASK-011`, `TASK-034`, and `TASK-039`; [entity lifecycle](entity-lifecycle.md). |
| Scheduled cancellation and invalidation | Prevent stale scheduled work from mutating replaced, cancelled, or removed state. | Completed `TASK-007` and `TASK-039`. |
| Authoritative checkpoints | Inventory every owner and policy required to reproduce future results and restore them without replay. | Completed `TASK-014` and `TASK-022`; every later owner must join this boundary. |
| Deterministic randomness | Assign stable versioned random streams whose results do not shift when unrelated systems add draws or work is repartitioned. | Defined by [Deterministic randomness and stream ownership](deterministic-randomness.md) in completed `TASK-021`; implementation in `TASK-066`. |
| Semantic game facts | Publish typed, ordered, bounded gameplay meaning independently from internal scheduled events and diagnostics. | Completed `TASK-008`; domain vocabularies remain with their owning systems. |
| Bounded explanation history | Retain enough causes and decisions for player explanation without an unlimited event log. | `TASK-025`. |
| Diagnostics, replay, and telemetry | Support reproducible debugging and operational evidence without confusing diagnostics with game facts or saves. | Deterministic traces are covered by [technical direction](technical-direction.md) and tests; full replay, telemetry transport, and archives are explicitly deferred by [semantic game facts](semantic-game-facts.md). |
| Deterministic concurrency | Preserve identical results across worker counts, batch layouts, partition shapes, work stealing, and completion order. | [Concurrency and performance](concurrency-and-performance.md); later implementation and evidence in `TASK-029`, with cross-platform and version guarantees in `TASK-060`. |
| Scale, benchmarks, and stability | Exercise accepted scale envelopes, measure throughput and allocation, compare digests, and retain a single-thread reference path. | Completed `TASK-024`; long-running and concurrent suites in `TASK-029`. |
| Entity and data storage | Supply indexed storage for actual query patterns without committing every domain to ECS. | [Technical direction](technical-direction.md); evidence-driven reevaluation in `TASK-027`. |

## Space, movement, and physical interaction

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Galaxy topology | Represent systems as local spaces joined by stable directional connector endpoints and connections. | [Navigation architecture](navigation-architecture.md), completed `TASK-028`. |
| System-local movement | Plan destination intent and execute authoritative scheduled motion without making rendered interpolation authoritative. | [Navigation architecture](navigation-architecture.md), completed `TASK-005` and `TASK-028`. |
| Inter-system travel | Compose local approach, connector transit, emergence, and continuation behind opaque plans. | [Navigation architecture](navigation-architecture.md), completed `TASK-028` and `TASK-031`. |
| Dynamic connectors and access | Change connector availability or actor access with explicit authority, waiting, wake, replan, failure, fact, and save behavior. | `TASK-030`. |
| Moving-ship interactions | Discover and resolve range crossing, proximity, following, interception, assistance, and other interactions while ships remain in motion. | `TASK-019`. |
| Collision and avoidance | Define collision geometry, swept interaction, avoidance policy, and any fixed-step participation. | Explicitly deferred by [navigation architecture](navigation-architecture.md); physical substrate begins with `TASK-019`. |
| Hazards and environmental effects | Represent spatial hazards, exposure, route cost, interruption, and consequences if required by later gameplay. | Inventory deferral beyond the current roadmap; navigation already reserves hazard-aware planning and interruption boundaries. |
| Docking, undocking, and berths | Approach a facility, obtain access or capacity, transition between moving and docked state, and expose congestion. | `TASK-051`, after the shared moving-interaction substrate in `TASK-019`. |
| Formations and fleet movement | Preserve group intent, relative movement policy, replanning, and deterministic membership behavior. | Inventory deferral after `TASK-019` and `TASK-033`; formation movement is explicitly deferred by [navigation architecture](navigation-architecture.md). |
| Sensors, detection, and scouting | Determine what an observer can detect, identify, track, or lose, including stale positions and undiscovered topology. | `TASK-020`; detailed sensor mechanics are explicitly deferred by [player experience](player-experience.md) and [navigation architecture](navigation-architecture.md). |
| Reduced-detail inactive simulation | Reduce work for unobserved or inactive systems without changing causal outcomes. | Explicitly deferred by [navigation architecture](navigation-architecture.md) and [concurrency and performance](concurrency-and-performance.md), pending evidence. |

## Actors, control, and automation

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Actor control | Distinguish player and autonomous base control, validate command source, and support bounded scripted overrides. | Completed `TASK-006`; [actor control and orders](actor-control-and-orders.md). |
| Order queues and lifecycle | Replace, append, promote, suspend, restore, cancel, wait, fail, and complete actor intent through one shared model. | Completed `TASK-006`. |
| Standing orders and recurring automation | Let the player delegate repeatable trade, mining, patrol, defense, and other routine work without creating a second command model. | `TASK-052`, building on completed `TASK-006`. |
| Autonomous ship behavior | Select ordinary ship work through the same command and order boundaries used by player intent. | `TASK-053`, coordinated with `TASK-020`, `TASK-026`, and `TASK-042`. |
| Individual NPC scope | Treat an NPC as an individually identifiable autonomous ship without creating a new entity category. | Completed `TASK-015`; [individual NPC scope](individual-npc-scope.md). |
| NPC competence and preference | Bound information and decision quality through skills, preferences, risk tolerance, cadence, and deterministic satisfactory choices. | `TASK-042`. |
| Crew, population, and personnel | Represent people, employment, population, or crew-level effects if the later game requires them. | Explicitly outside the first version in [vision](vision.md) and [Phase 1](phase-1-simulation-spec.md); separately deferred to `TASK-062` by [individual NPC scope](individual-npc-scope.md). |

## Economy, assets, and industry

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Materials and physical inventory | Conserve typed material quantities, capacity, reservations, transfers, and destruction disposition. | [Economy](economy.md), Phase 1 foundation, completed `TASK-034`; generalization in `TASK-041`. |
| General cargo, items, and equipment | Model approved physical item categories, stacks or identities, capacities or slots, installation, removal, transfer, and save references. | `TASK-041`. |
| Resource acquisition | Mine or collect resources with explicit deposits, capability, work, depletion or renewal policy, and resulting inventory. | Basic Phase 1 mining remains acceptance-only; production gameplay belongs to `TASK-054`. |
| Logistics and hauling | Match supply to demand, reserve inventory and cargo, load, travel, unload, recover from interruption, and expose shortages. | [Economy](economy.md), completed `TASK-009`, `TASK-031`, and `TASK-034`; facts in `TASK-032`. |
| Production and refining | Reserve and consume inputs, schedule work, publish outputs, queue recipes, and recover from blocked storage or interruption. | [Economy](economy.md), completed Phase 1 and `TASK-009`; semantic facts in `TASK-032`. |
| Construction and shipbuilding | Queue finite construction, consume material and work, create persistent assets, and connect losses to replacement demand. | [Economy](economy.md), completed `DONE-007`, `TASK-011`, and `TASK-034`. |
| Stations and installed capabilities | Combine storage, production, trade, repair, construction, defense, and service capabilities in persistent facilities. | Production and construction foundations exist; generalized station composition belongs to `TASK-057`. |
| Trade offers and contracts | Publish supply, demand, procurement, and transport opportunities for player or autonomous acceptance. | Phase 1 supplies an acceptance-only job-board foundation; production contract policy belongs to `TASK-055`. |
| Currency, prices, and markets | Allocate scarce goods through prices while allowing internal logistics and strategic reservation outside a public market. | Excluded from Phase 1; the gameplay contract belongs to `TASK-055`. |
| Ownership and asset transfer | Track who owns each asset separately from who controls or is affiliated with it, including purchase and later transfer. | [Factions](factions.md), completed `TASK-012`; generalized transfer semantics remain in `TASK-041` and later owning domains. |
| Ship acquisition and upgrades | Purchase, construct, replace, improve, and equip ships while keeping one-ship play viable. | Construction foundation exists; the progression contract belongs to `TASK-056` after `TASK-041` and `TASK-055`. |
| Station construction and expansion | Build production capacity and stations from material inputs, assign ownership, and alter the regional economy. | `TASK-057`, after content and generalized installed-capability prerequisites. |
| Repair and maintenance | Consume time, capacity, materials, and possibly equipment to restore damaged assets. | `TASK-058`, after `TASK-041`, `TASK-046`, and `TASK-057`. |
| Salvage and wreck recovery | Turn destroyed or abandoned assets into attributable recoverable items without bypassing ownership, crime, or inventory rules. | Inventory deferral coordinated with `TASK-036`, `TASK-041`, and `TASK-046`. |
| Economic resilience | Use stockpiles, substitution, alternate suppliers, reprioritization, reduced consumption, and recovery construction to respond to disruption. | Balancing and policy are explicitly deferred by [economy](economy.md); strategic ownership belongs to `TASK-026`. |
| Economic facts and explanation | Publish typed production, construction, logistics, transaction, and shortage facts for UI and strategic consumers. | `TASK-032`, with bounded history in `TASK-025`. |

## Powers, relationships, territory, and strategy

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Principals, ownership, control, and affiliation | Identify accountable participants and keep asset ownership, command control, and membership concepts distinct. | [Factions](factions.md), [relational architecture](relational-simulation-architecture.md), completed `TASK-012` and `TASK-035`. |
| Directional standing and reputation | Track asymmetric treatment, explain changes, and map standing bands to gameplay consequences. | Completed `TASK-012` and `TASK-035`; domain-specific consequences remain with their owning systems. |
| Diplomacy | Represent mutual diplomatic condition separately from directional standing and allow later policy-driven changes. | Completed foundation in `TASK-012`; autonomous war, peace, negotiation, and treaties are explicitly deferred by [factions](factions.md). |
| Permissions, licenses, and credentials | Grant explicit docking, trade, equipment, information, territorial, or other rights without deriving all authority from a reputation number. | Foundation in completed `TASK-012`; concrete grant catalogs remain explicitly deferred by [relational architecture](relational-simulation-architecture.md). |
| Territory and borders | Identify territorial authority, publish rules, enforce restricted space, and eventually support claims or loss of control. | Foundation in completed `TASK-012`; the concrete territory and claiming contract belongs to `TASK-059`. |
| Law, crime, policing, and contraband | Define jurisdictions, prohibited acts and goods, witnesses, attribution, enforcement, fines, warrants, and consequences. | Explicitly deferred by [factions](factions.md); piracy subset in `TASK-036`. |
| Piracy and privateering | Distinguish piracy, ordinary trade, salvage, territorial violation, privateering, and war, then apply attribution and relationship consequences. | `TASK-036`. |
| Strategic goals and planning | Evaluate threats and opportunities, select priorities, convert them into executable objectives and orders, and respect material constraints. | [Factions](factions.md); `TASK-026`. |
| Faction logistics and procurement | Reserve strategic resources, prioritize deliveries, construct forces, and recover from disrupted supply without abstract spawning. | [Economy](economy.md), [factions](factions.md), and `TASK-026`; shared economy owners already exist. |
| Faction asymmetry and personality | Vary goals, preferences, risk, and policy only after one shared deterministic planning model is proven. | Explicitly deferred by [roadmap](roadmap.md) and `TASK-026`; NPC decision traits coordinate with `TASK-042`. |
| Membership and organization hierarchy | Allow joining or founding powers, subsidiaries, citizenship, internal organizations, or other affiliation structures. | Explicitly deferred by [factions](factions.md) and [relational architecture](relational-simulation-architecture.md). |
| Territory growth and player political progression | Claim space and grow from independent operator into a territorial power without making expansion mandatory. | `TASK-059`, gated on station, knowledge, and conflict prerequisites. |
| Player and faction knowledge | Separate authoritative truth from detected, reported, remembered, public, private, and stale information. | `TASK-020`; accepted relationship snapshots provide only an initial scoped boundary. |

## Conflict, narrative, and goals

| System | Planned responsibility | Current ownership or disposition |
| --- | --- | --- |
| Combat engagement | Admit attack, defense, patrol, escort, withdrawal, and avoidance intent and resolve moving engagements without a combat-only motion model. | `TASK-046`, built on `TASK-019`. |
| Damage, destruction, and loss | Apply damage, disable or destroy assets, invalidate work, dispose of cargo, and create replacement demand and facts. | `TASK-046`; entity removal foundation in completed `TASK-011` and `TASK-039`. |
| Surrender, capture, and control reassignment | End conflict without destruction and transfer control or ownership through explicit policy. | `TASK-046`; relationship architecture explicitly defers transfer policy to the combat owner. |
| Observed and unobserved conflict | Preserve identical causal outcomes while varying only player knowledge, presentation, and possibly simulation detail proven safe. | `TASK-046` with `TASK-020`; [technical direction](technical-direction.md) leaves the exact treatment open. |
| Objectives, missions, victory, and defeat | Represent persistent milestones and outcomes as authoritative state and facts rather than engine termination. | `TASK-018`. |
| Dialogue and conversations | Define availability, conditions, choices, memory, consequences, continuity, and response-required pacing behavior. | Defined by [Dialogue state and presentation](dialogue.md) in completed `TASK-016`; implementation in `TASK-065`, coordinated with `TASK-038` and `TASK-045`. |
| Scripted events | Trigger deterministic one-shot or repeatable behavior from time, location, thresholds, or facts through an approved command vocabulary. | `TASK-017`. |
| Narrative campaigns | Compose dialogue, objectives, and scripted events into longer authored arcs only after their shared systems exist. | Explicitly deferred beyond the first version by [vision](vision.md) and [roadmap](roadmap.md). |
| Technology and research | Gate capabilities, equipment, production, or progression through discoverable or unlockable technology if later gameplay requires it. | Inventory deferral beyond the current roadmap; large technology catalogs are outside the first version in [vision](vision.md). |

## TASK-043 scope-gap review

The `TASK-043` review compared this inventory with the player experience,
roadmap, gameplay integration, economy, factions, navigation, lifecycle,
information, presentation, save, content, and simulation architecture. A gap
was confirmed only when the documented experience promises the behavior, or an
accepted system needs an explicit owner before that behavior can enter the
production session.

The review added separate tasks rather than widening accepted owners:

| Confirmed gap | New owner | Why it is a contract rather than an implementation detail |
| --- | --- | --- |
| Application shell, broader map views, and presentation interaction | `TASK-049` | The roadmap requires a playable minimal map application, while the current Godot client and `TASK-010` prove only a bounded ship interaction slice. |
| Save slots, autosave, and local preferences | `TASK-050` | File integrity is accepted, but player-visible save policy and non-authoritative preference ownership were explicitly left to the application. |
| Docking, undocking, and capacity | `TASK-051` | Docking is an initial player order and station congestion is promised economy state, but no current owner defines the transition or contention rules. |
| Standing orders and recurring player automation | `TASK-052` | Optional delegation across many ships is part of the command experience and cannot be inferred from the one-shot order lifecycle. |
| Autonomous ship work selection | `TASK-053` | An autonomous galaxy requires ordinary actors to choose work, which is distinct from faction goal selection and from NPC competence traits. |
| General resource acquisition | `TASK-054` | Mining is a starting activity and first production-chain step, while the existing implementation is an acceptance-only fixture. |
| Trade, contracts, currency, prices, and markets | `TASK-055` | Buying, selling, and trading are initial player actions; Phase 1 deliberately omitted the exchange and pricing contract. |
| Ship acquisition, replacement, and upgrades | `TASK-056` | Economic progression promises additional or improved ships, but construction and inventory foundations do not define acquisition policy. |
| Station composition, construction, and expansion | `TASK-057` | Industrial expansion is a promised progression path, while existing production and construction owners do not define persistent facility composition. |
| Repair and maintenance | `TASK-058` | Station repair capability and persistent combat damage require a material recovery contract rather than implicit restoration. |
| Territory claiming and political expansion | `TASK-059` | Territorial progression is promised, while the relational foundation intentionally left identities, boundaries, and claim policy unowned. |
| Cross-platform and version reproducibility | `TASK-060` | The architecture promises reproducibility but explicitly leaves its compatibility envelope unresolved. |

The review retained the following as deliberate deferrals: final visual style,
audio, mod distribution, full replay and telemetry, collision and avoidance,
hazards, formation geometry, reduced-detail inactive simulation, detailed
sensors, crew and population, salvage and wreck recovery, law and policing,
membership hierarchies, autonomous diplomacy, narrative campaigns, and
technology or research. These areas are optional, evidence-gated, or depend on
later gameplay contracts. Their inventory rows remain sufficient ownership
until a concrete scenario requires promotion to a tracker task.

## Cross-cutting ownership rules

Every system in this inventory must preserve the following project-wide
contracts when it is promoted into design or implementation:

- The game remains strictly single-player. No system introduces networking,
  replication, remote authority, client prediction, rollback, or lobbies.
- Authoritative ownership is explicit. Presentation, content files, test
  fixtures, and worker-local evaluation do not become live authority.
- Simulation results are independent of worker count, batching, partitioning,
  work stealing, and completion order. A single-thread reference path remains.
- New domain owners join command, fact, snapshot, lifecycle, checkpoint,
  restore, and content boundaries as their behavior requires.
- Stable IDs, reason codes, and content keys cross authoritative boundaries;
  localized prose, icons, layout, and rendered interpolation do not.
- Acceptance-only fixtures prove behavior but do not define production session
  ownership or APIs.

## Explicit non-systems

The following are not planned systems. Listing them prevents a later review
from mistaking their absence for a documentation gap:

- multiplayer, networking, replication, remote authority, client prediction,
  rollback netcode, and lobbies;
- direct first-person or arcade-style ship piloting;
- a detailed 3D environment; and
- authoritative hot reload of content inside a running session.

If project direction changes, the owner must update the vision and canonical
task list before any of these become planned work.

## Maintenance rule

`docs/task-list.md` remains the canonical work and status tracker. This document
is the canonical system inventory and scope-gap review record. When a later
decision adds a planned system, removes one, or promotes an inventory deferral
into owned work, update the relevant row and add or revise the corresponding
tracker task rather than scattering a second inventory elsewhere.
