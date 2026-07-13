namespace GalaxyCommand.Simulation;

/// <summary>
/// Rendering-independent entry point for the authoritative simulation.
/// </summary>
public sealed class SimulationEngine
{
    public SimulationTime CurrentTime { get; private set; } = SimulationTime.Zero;

    public RunReport RunUntil(SimulationTime target)
    {
        if (target < CurrentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                "The simulation cannot run backward.");
        }

        SimulationTime startTime = CurrentTime;
        CurrentTime = target;

        return new RunReport(startTime, CurrentTime, EventsProcessed: 0);
    }
}
