# Technical direction

[Project index](../README.md) · [Simulation architecture](simulation-architecture.md) · [Initial roadmap](roadmap.md)

## Current direction

Use C# and .NET for the authoritative simulation and keep it independent from rendering. The earlier Rust Phase 1 implementation remains temporarily as a behavioral reference during migration.

A likely workspace division is:

- A headless simulation library
- A desktop application for rendering and input
- A command-line simulation runner for benchmarks and long-duration tests

## Rendering and application framework

Godot with C# is the leading candidate for the 2D application, but the UI framework remains an unresolved choice. The simulation should avoid depending on rendering-specific types or schedules so it remains testable and replaceable.

Simple indexed .NET collections may be preferable for some economic structures. ECS should be adopted where entity-oriented queries provide a clear benefit, not as a requirement for every subsystem.

## Performance strategy

The first performance goal is to avoid unnecessary work:

- Use scheduled events for known future changes
- Index economic opportunities by relevant region and commodity
- Cache routes and invalidate them when topology changes
- Run strategic planning less frequently than movement
- Avoid galaxy-wide searches by individual ships
- Render only the information needed at the current zoom level

Parallel processing should follow profiling and deterministic-design needs rather than being added automatically.

Phase 1 will execute deterministically on one thread. Simulation time, inventory, cargo, production work, and other conserved quantities should prefer integers. Explicit fixed-point values may be used where integer units are not sufficiently expressive. Floating-point values should not be used for conserved economic quantities.

## Testing

The headless simulation should support:

- Deterministic scenario tests
- Economic invariants and conservation checks
- Long-running stability tests
- Performance benchmarks at increasing scales
- Recorded commands and event traces for reproduction

## Unresolved choices

- Final application and UI framework
- Entity storage model
- Save-file format and migration strategy
- Determinism guarantees across platforms and versions
- Initial scale targets for systems, stations, and ships
- Exact treatment of observed and unobserved combat

The first milestone should collect evidence for these choices rather than settle all of them in advance.

The implementation contract for that milestone is the [Phase 1 simulation specification](phase-1-simulation-spec.md).
