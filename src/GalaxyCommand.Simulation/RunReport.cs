namespace GalaxyCommand.Simulation;

/// <summary>
/// Result of advancing the authoritative simulation clock.
/// </summary>
public sealed record RunReport(
    SimulationTime StartTime,
    SimulationTime EndTime,
    long EventsProcessed);
