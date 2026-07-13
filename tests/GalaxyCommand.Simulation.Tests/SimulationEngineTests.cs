using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SimulationEngineTests
{
    [Fact]
    public void NewSimulationStartsAtZero()
    {
        var simulation = new SimulationEngine();

        Assert.Equal(SimulationTime.Zero, simulation.CurrentTime);
    }

    [Fact]
    public void RunUntilAdvancesTheAuthoritativeClock()
    {
        var simulation = new SimulationEngine();

        RunReport report = simulation.RunUntil(new SimulationTime(100));

        Assert.Equal(SimulationTime.Zero, report.StartTime);
        Assert.Equal(new SimulationTime(100), report.EndTime);
        Assert.Equal(report.EndTime, simulation.CurrentTime);
        Assert.Equal(0, report.EventsProcessed);
    }

    [Fact]
    public void RunUntilRejectsBackwardTimeTravel()
    {
        var simulation = new SimulationEngine();
        simulation.RunUntil(new SimulationTime(100));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => simulation.RunUntil(new SimulationTime(99)));
        Assert.Equal(new SimulationTime(100), simulation.CurrentTime);
    }
}
