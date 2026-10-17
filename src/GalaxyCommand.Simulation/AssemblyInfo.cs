using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GalaxyCommand.Simulation.Tests")]
// Benchmarks reuse exact geometry and reference solvers without exposing instrumentation through runtime APIs.
[assembly: InternalsVisibleTo("GalaxyCommand.Benchmarks")]
[assembly: InternalsVisibleTo("GalaxyCommand.Benchmarks.Tests")]
