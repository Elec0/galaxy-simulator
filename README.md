# Galaxy Command

Galaxy Command is a working title for a 2D space simulation and command game. The player begins with one ship, issues orders through a map, and may remain an independent single-ship operator or expand into a fleet and industrial organization.

The central goal is a persistent, understandable galaxy in which ships, stations, production, trade, and faction actions have material causes. Minimal graphics keep the focus on the simulation rather than piloting or navigating a 3D environment.

## Design documents

- [Vision and principles](docs/vision.md)
- [Player experience](docs/player-experience.md)
- [Simulation architecture](docs/simulation-architecture.md)
- [Navigation and spatial architecture](docs/navigation-architecture.md)
- [Concurrency and performance architecture](docs/concurrency-and-performance.md)
- [Economy and production](docs/economy.md)
- [Factions and strategic behavior](docs/factions.md)
- [Time and pacing](docs/time-and-pacing.md)
- [Technical direction](docs/technical-direction.md)
- [Gameplay integration issues and decisions](docs/gameplay-integration.md)
- [Project task list](docs/task-list.md)
- [Initial roadmap](docs/roadmap.md)
- [Phase 1 simulation specification](docs/phase-1-simulation-spec.md)

These documents are an early design draft. Names, quantities, formulas, and technology choices remain open unless explicitly identified as requirements.

## C# development

The active migration target is a .NET 10 solution containing:

- `GalaxyCommand.Simulation`: the rendering-independent simulation library
- `GalaxyCommand.Cli`: the headless runner used for development and benchmarks
- `GalaxyCommand.Simulation.Tests`: deterministic simulation tests

Run the C# verification commands from this directory:

```sh
dotnet restore GalaxyCommand.slnx
dotnet build GalaxyCommand.slnx --no-restore
dotnet test GalaxyCommand.slnx --no-build --no-restore
dotnet run --project src/GalaxyCommand.Cli/GalaxyCommand.Cli.csproj --no-build --no-restore
```

The SDK version is pinned in `global.json`. Shared compiler settings enable
nullable-reference checking and treat warnings as errors.

## Godot graphics client

The graphics foundation is a Godot 4.7.1 .NET project under
`src/GalaxyCommand.Godot`. It references the rendering-independent simulation
library and renders immutable presentation snapshots from the live Phase 1
scenario. The initial client advances automatically at 30× speed so ship travel
and construction can be observed without changing authoritative simulation
behavior.

Install the .NET edition of Godot, then open
`src/GalaxyCommand.Godot/project.godot` in the editor. From this directory, the
client can also be built and run with:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --headless --path src/GalaxyCommand.Godot --build-solutions --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path src/GalaxyCommand.Godot
```

The C# CLI runs the integrated Phase 1 scenario through mining, transport,
refining, component manufacturing, and construction of a persistent freighter.
Its report includes material and logistics totals, facility-state timing,
current shortages, structured record counts, and deterministic event-log and
final-state fingerprints. The test suite also disables Mine-to-Refinery travel
at 50 simulated seconds and restores it at 250 seconds to verify shortage
visibility and recovery.
