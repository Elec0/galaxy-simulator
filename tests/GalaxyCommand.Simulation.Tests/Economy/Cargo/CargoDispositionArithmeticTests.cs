using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDispositionArithmeticTests
{
    [Theory]
    [InlineData(0, 1, 2, 0)]
    [InlineData(1, 1, 2, 0)]
    [InlineData(5, 1, 2, 2)]
    [InlineData(5, 0, 1, 0)]
    [InlineData(5, 1, 1, 5)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue)]
    [InlineData(ulong.MaxValue, 1, 2, 9223372036854775807UL)]
    public void SurvivalFloorsOneSelectedDefinitionWithoutOverflow(
        ulong selected, ulong numerator, ulong denominator, ulong expected)
    {
        Assert.Equal(new Quantity(expected), CargoDispositionArithmetic.SurvivalCount(
            new Quantity(selected), numerator, denominator));
    }

    [Fact]
    public void SurvivalDoesNotCarryRemaindersBetweenOccurrences()
    {
        Quantity first = CargoDispositionArithmetic.SurvivalCount(new Quantity(1), 1, 2);
        Quantity second = CargoDispositionArithmetic.SurvivalCount(new Quantity(1), 1, 2);
        Assert.Equal(Quantity.Zero, first);
        Assert.Equal(Quantity.Zero, second);
        Assert.Equal(new Quantity(1), CargoDispositionArithmetic.SurvivalCount(new Quantity(2), 1, 2));
    }

    [Fact]
    public void SurvivalRejectsInvalidRatiosEvenWhenNothingWasSelected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CargoDispositionArithmetic.SurvivalCount(Quantity.Zero, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CargoDispositionArithmetic.SurvivalCount(Quantity.Zero, 2, 1));
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 1142)]
    [InlineData(34, 5667)]
    [InlineData(99, 14859)]
    [InlineData(100, 15000)]
    [InlineData(200, 15000)]
    [InlineData(ulong.MaxValue, 15000)]
    public void CollectionRoundsUpOnceAndCapsAtFullObjectStorage(ulong storage, ulong expected)
    {
        Assert.Equal(new SimulationDuration(expected), CargoDispositionArithmetic.CollectionDuration(
            new Quantity(storage), new SimulationDuration(1000), new SimulationDuration(15000), new Quantity(100)));
    }

    [Fact]
    public void EveryInitialCurveSizeUsesTheFirstMillisecondAtOrAfterItsExactDuration()
    {
        for (ulong size = 1; size <= 100; size++)
        {
            ulong result = CargoDispositionArithmetic.CollectionDuration(new Quantity(size),
                new SimulationDuration(1000), new SimulationDuration(15000), new Quantity(100)).Milliseconds;
            BigInteger exactNumerator = 99_000 + 14_000 * (size - 1);
            Assert.True((BigInteger)result * 99 >= exactNumerator);
            Assert.True((BigInteger)(result - 1) * 99 < exactNumerator);
        }
    }

    [Fact]
    public void DurationSupportsExplicitPolicyEndpointsAndWideIntermediateProducts()
    {
        Assert.Equal(new SimulationDuration(7), CargoDispositionArithmetic.CollectionDuration(
            new Quantity(50), new SimulationDuration(7), new SimulationDuration(7), new Quantity(100)));
        Assert.Equal(new SimulationDuration(8), CargoDispositionArithmetic.CollectionDuration(
            new Quantity(2), new SimulationDuration(7), new SimulationDuration(10), new Quantity(5)));
        Assert.Equal(new SimulationDuration(ulong.MaxValue - 1), CargoDispositionArithmetic.CollectionDuration(
            new Quantity(ulong.MaxValue - 1), new SimulationDuration(1),
            new SimulationDuration(ulong.MaxValue), new Quantity(ulong.MaxValue)));
        Assert.Equal(new SimulationDuration(ulong.MaxValue), CargoDispositionArithmetic.CollectionDuration(
            new Quantity(ulong.MaxValue), new SimulationDuration(1),
            new SimulationDuration(ulong.MaxValue), new Quantity(ulong.MaxValue)));
    }

    [Theory]
    [InlineData(0, 1, 2, 100)]
    [InlineData(1, 0, 2, 100)]
    [InlineData(1, 2, 1, 100)]
    [InlineData(1, 1, 2, 0)]
    [InlineData(1, 1, 2, 1)]
    public void DurationRejectsInvalidInputs(ulong size, ulong minimum, ulong maximum, ulong cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CargoDispositionArithmetic.CollectionDuration(
            new Quantity(size), new SimulationDuration(minimum), new SimulationDuration(maximum), new Quantity(cap)));
    }

    [Fact]
    public void ExpiryUsesExplicitPositiveLifetimeAndCheckedTimelineAddition()
    {
        Assert.Equal(new SimulationTime(63_072_000_123), CargoDispositionArithmetic.ExpiryAt(
            new SimulationTime(123), new SimulationDuration(63_072_000_000)));
        Assert.Equal(new SimulationTime(ulong.MaxValue), CargoDispositionArithmetic.ExpiryAt(
            new SimulationTime(ulong.MaxValue - 1), new SimulationDuration(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CargoDispositionArithmetic.ExpiryAt(
            SimulationTime.Zero, SimulationDuration.Zero));
        Assert.Throws<OverflowException>(() => CargoDispositionArithmetic.ExpiryAt(
            new SimulationTime(ulong.MaxValue), new SimulationDuration(1)));
    }
}
