using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SimulationTimeTests
{
    [Fact]
    public void AddAdvancesTime()
    {
        SimulationTime result = new SimulationTime(10).Add(new SimulationDuration(25));

        Assert.Equal(new SimulationTime(35), result);
    }

    [Fact]
    public void AddRejectsOverflow()
    {
        Assert.Throws<OverflowException>(
            () => new SimulationTime(ulong.MaxValue).Add(new SimulationDuration(1)));
    }

    [Fact]
    public void DurationAdditionRejectsOverflow()
    {
        Assert.Throws<OverflowException>(
            () => new SimulationDuration(ulong.MaxValue).Add(new SimulationDuration(1)));
    }
}
