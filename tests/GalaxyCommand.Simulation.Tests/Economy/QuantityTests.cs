using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class QuantityTests
{
    [Fact]
    public void CheckedArithmeticPreservesNonNegativeQuantities()
    {
        Quantity total = new Quantity(10).Add(new Quantity(5));
        Quantity remainder = total.Subtract(new Quantity(4));

        Assert.Equal(new Quantity(11), remainder);
    }

    [Fact]
    public void SubtractRejectsNegativeResult()
    {
        Assert.Throws<InvalidOperationException>(
            () => new Quantity(2).Subtract(new Quantity(3)));
    }
}
