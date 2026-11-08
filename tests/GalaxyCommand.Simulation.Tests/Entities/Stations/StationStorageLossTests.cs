using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationStorageLossTests
{
    [Theory]
    [InlineData(9, 991, false)]
    [InlineData(10, 990, true)]
    [InlineData(11, 989, true)]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    public void CutoffUsesExactFractionAndSettlesAtEquality(ulong destroyed, ulong surviving, bool expected)
    {
        Assert.Equal(expected, StationStorageLoss.HasReachedCutoff(
            destroyed, new Quantity(surviving), 1, 100));
    }

    [Fact]
    public void RepeatedSmallLossesAccumulateAndCapacityAdditionsDiluteWithoutReset()
    {
        BigInteger accumulated = 4;
        Assert.False(StationStorageLoss.HasReachedCutoff(accumulated, new Quantity(996), 1, 100));
        accumulated += 6;
        Assert.True(StationStorageLoss.HasReachedCutoff(accumulated, new Quantity(990), 1, 100));
        Assert.False(StationStorageLoss.HasReachedCutoff(accumulated, new Quantity(1990), 1, 100));
        accumulated += 10;
        Assert.True(StationStorageLoss.HasReachedCutoff(accumulated, new Quantity(1980), 1, 100));
    }

    [Theory]
    [InlineData(400, 0, 250, 750, 100)]
    [InlineData(1, 0, 1, 99, 1)]
    [InlineData(9, 1, 10, 90, 0)]
    [InlineData(99, 1, 10, 90, 9)]
    [InlineData(0, 5, 10, 90, 0)]
    [InlineData(7, 0, 100, 0, 7)]
    [InlineData(10, 0, 0, 100, 0)]
    public void AdditionalLossRoundsUpAndCreditsOverflowOnce(
        ulong held, ulong credited, ulong destroyed, ulong surviving, ulong expected)
    {
        Assert.Equal(new Quantity(expected), StationStorageLoss.AdditionalLoss(
            new Quantity(held), new Quantity(credited), destroyed, new Quantity(surviving)));
    }

    [Fact]
    public void CurrentStockAtSettlementIncludesDeliveriesAndWithdrawals()
    {
        Assert.Equal(new Quantity(10), StationStorageLoss.AdditionalLoss(new Quantity(100), Quantity.Zero, 10, new Quantity(90)));
        Assert.Equal(new Quantity(20), StationStorageLoss.AdditionalLoss(new Quantity(200), Quantity.Zero, 10, new Quantity(90)));
        Assert.Equal(new Quantity(5), StationStorageLoss.AdditionalLoss(new Quantity(50), Quantity.Zero, 10, new Quantity(90)));
    }

    [Fact]
    public void LossArithmeticSupportsLargeAccumulationAndHeldPlusCreditsWithoutOverflow()
    {
        BigInteger destroyed = BigInteger.One << 160;
        Assert.True(StationStorageLoss.HasReachedCutoff(destroyed, new Quantity(ulong.MaxValue), 1, 100));
        Assert.Equal(new Quantity(ulong.MaxValue), StationStorageLoss.AdditionalLoss(
            new Quantity(ulong.MaxValue), Quantity.Zero, destroyed, new Quantity(ulong.MaxValue)));
        Assert.Equal(Quantity.Zero, StationStorageLoss.AdditionalLoss(
            new Quantity(ulong.MaxValue), new Quantity(ulong.MaxValue), 1, new Quantity(1)));
    }

    [Theory]
    [InlineData(1, 10, 3, 1)]
    [InlineData(11, 10, 3, 2)]
    [InlineData(20, 10, 3, 2)]
    [InlineData(21, 10, 3, 3)]
    [InlineData(50, 10, 3, 3)]
    [InlineData(0, 10, 3, 0)]
    [InlineData(1, 10, 0, 0)]
    [InlineData(ulong.MaxValue, 2, ulong.MaxValue, 9223372036854775808UL)]
    public void OverflowRemovesMinimumWholeUnitsAvailableAtSelectionStep(
        ulong overflow, ulong unitCapacity, ulong available, ulong expected)
    {
        Assert.Equal(new Quantity(expected), StationStorageLoss.OverflowUnits(
            new Quantity(overflow), new Quantity(unitCapacity), new Quantity(available)));
    }

    [Fact]
    public void CallerSuppliedCutoffIsNotReplacedByAStationDefault()
    {
        Assert.True(StationStorageLoss.HasReachedCutoff(1, new Quantity(999), 0, 100));
        Assert.False(StationStorageLoss.HasReachedCutoff(1, new Quantity(999), 1, 100));
        Assert.False(StationStorageLoss.HasReachedCutoff(999, new Quantity(1), 2, 1));
    }

    [Fact]
    public void RejectsInvalidAccountingAndCutoffs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StationStorageLoss.HasReachedCutoff(-1, new Quantity(1), 1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => StationStorageLoss.HasReachedCutoff(1, new Quantity(1), 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StationStorageLoss.AdditionalLoss(new Quantity(1), Quantity.Zero, -1, new Quantity(1)));
        Assert.Throws<ArgumentException>(() => StationStorageLoss.AdditionalLoss(new Quantity(1), new Quantity(1), 0, new Quantity(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => StationStorageLoss.OverflowUnits(new Quantity(1), Quantity.Zero, new Quantity(1)));
    }
}
