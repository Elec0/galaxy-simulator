# Galaxy Command

Galaxy Command is a working title for a 2D space simulation and command game. The player begins with one ship, issues orders through a map, and may remain an independent single-ship operator or expand into a fleet and industrial organization.

The central goal is a persistent, understandable galaxy in which ships, stations, production, trade, and faction actions have material causes. Minimal graphics keep the focus on the simulation rather than piloting or navigating a 3D environment.

## Design documents

- [Vision and principles](docs/vision.md)
- [Player experience](docs/player-experience.md)
- [Simulation architecture](docs/simulation-architecture.md)
- [Runtime orchestration and domain ownership](docs/runtime-orchestration.md)
- [Navigation and spatial architecture](docs/navigation-architecture.md)
- [Moving-ship interaction architecture](docs/moving-ship-interactions.md)
- [Actor control and order lifecycle](docs/actor-control-and-orders.md)
- [Individual NPC scope](docs/individual-npc-scope.md)
- [Dialogue state and presentation](docs/dialogue.md)
- [Fog-of-war and scouting](docs/fog-of-war-and-scouting.md)
- [Deterministic randomness and stream ownership](docs/deterministic-randomness.md)
- [Semantic game facts](docs/semantic-game-facts.md)
- [Presentation snapshots](docs/presentation-snapshots.md)
- [Entity lifecycle and explicit spawning](docs/entity-lifecycle.md)
- [Authoritative save boundary](docs/authoritative-save-boundary.md)
- [Save format, versioning, and migration](docs/save-format-and-migration.md)
- [Save slots, autosave, and local preferences](docs/save-slots-and-local-preferences.md)
- [Gameplay content and static new-game composition](docs/gameplay-content.md)
- [Generalized inventory and cargo](docs/inventory-and-cargo.md)
- [Internationalization and localization](docs/internationalization-and-localization.md)
- [Planned game-system inventory](docs/planned-systems.md)
- [Concurrency and performance architecture](docs/concurrency-and-performance.md)
- [Scale targets and benchmark architecture](docs/scale-and-benchmark-targets.md)
- [Economy and production](docs/economy.md)
- [Relational gameplay model](docs/factions.md)
- [Relational simulation architecture](docs/relational-simulation-architecture.md)
- [Time and pacing](docs/time-and-pacing.md)
- [Technical direction](docs/technical-direction.md)
- [Gameplay integration issues and decisions](docs/gameplay-integration.md)
- [Project task list](docs/task-list.md)
- [Initial roadmap](docs/roadmap.md)
- [Phase 1 simulation specification](docs/phase-1-simulation-spec.md)

These documents are an early design draft. Names, quantities, formulas, and technology choices remain open unless explicitly identified as requirements.

## C# development

The active solution contains:

- `GalaxyCommand.Content`: the rendering- and simulation-independent content library
- `GalaxyCommand.Content.Validator`: the headless production content validator
- `GalaxyCommand.Simulation`: the rendering-independent simulation library
- `GalaxyCommand.Simulation.Tests`: deterministic simulation tests

Run the C# verification commands from this directory:

```sh
dotnet restore GalaxyCommand.slnx
dotnet build GalaxyCommand.slnx --no-restore
dotnet test GalaxyCommand.slnx --no-build --no-restore
```

The SDK version is pinned in `global.json`. Shared compiler settings enable
nullable-reference checking and treat warnings as errors.

## Headless content validation

Validate explicitly selected package directories through the same strict
adapters, dependency and reference resolution, canonicalization,
fingerprinting, and immutable catalog construction used by the content library:

```sh
dotnet run --project src/GalaxyCommand.Content.Validator -- \
  --package /path/to/package \
  --allow-kind material \
  --show-package-order \
  --show-keys \
  --show-fingerprints
```

Repeat `--package` and `--allow-kind` as needed. Use `--workers COUNT` to bound
parallel read-only package loading. Diagnostics use stable `GC-CONTENT-NNNN`
codes, and the command does not require Godot or create a game session.

## Deterministic benchmarks

The dedicated benchmark runner writes human-readable progress to stderr and a
machine-readable JSON report to stdout. Phase 1 is now a test-only acceptance
fixture, so every remaining benchmark requires the explicit full-suite option:

```sh
dotnet run --project benchmarks/GalaxyCommand.Benchmarks/GalaxyCommand.Benchmarks.csproj -- --suite full
```

List presets and their numeric defaults with `--list`. Run one heavy preset or
tune named integer parameters with visible overrides:

```sh
dotnet run --project benchmarks/GalaxyCommand.Benchmarks/GalaxyCommand.Benchmarks.csproj -- \
  --suite full \
  --preset spatial.one-crowded \
  --set shipCount=500 \
  --set activeShipCount=500 \
  --set measuredIterations=3
```

For repeatable custom settings, use a versioned scenario file:

```sh
dotnet run --project benchmarks/GalaxyCommand.Benchmarks/GalaxyCommand.Benchmarks.csproj -- \
  --suite full \
  --scenario-file benchmarks/scenarios/example.spatial-one-crowded.json
```

Timing, allocation, and memory results are informational. Canonical digests,
repeated-run agreement, semantic counts, and simulation invariants are enforced.
The accepted benchmark contract and scale envelopes are documented in
[Scale targets and benchmark architecture](docs/scale-and-benchmark-targets.md).

## Godot graphics client

The graphics client is a Godot 4.7.1 .NET project under
`src/GalaxyCommand.Godot`. It references the rendering-independent simulation
library and uses the clean `GameSession` runtime rather than the bounded Phase 1
acceptance scenario. The client advances automatically, renders authoritative
system-local ship motion, and submits move or cancel commands through the same
session boundary used by headless tests.

Click a ship to select and focus it. Shift-click ships to add or remove them
from the local selection, while the focused ship remains the target for the
current single-ship controls. Click empty system space to issue or replace its
destination, Shift-click empty space to append a destination, and right-click
to cancel its active order. The status panel shows the active controller, queue
length, current destination, order state and reason, motion state, and bounded
recent-fact feed status.

Install the .NET edition of Godot, then open
`src/GalaxyCommand.Godot/project.godot` in the editor. From this directory, the
client can also be built and run with:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path src/GalaxyCommand.Godot --build-solutions --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path src/GalaxyCommand.Godot
```

The test-only Phase 1 acceptance scenario runs through mining, transport,
refining, component manufacturing, and construction of a persistent freighter.
It checks material and logistics totals, facility-state timing, current
shortages, structured records, and deterministic event and final-state
fingerprints.
