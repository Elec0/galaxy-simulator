using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverThrustEnvelopeTests
{
    private static readonly ManeuverAcceleration Primary = new(10_000);
    private static readonly ManeuverAcceleration Precision = new(1_000);

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)]
    public void ZeroOrFullPrimaryForwardAllocationIsAdmitted(
        ulong primaryForward)
    {
        Assert.True(Contains(primaryForward, 0, 0));
    }

    [Fact]
    public void PrimaryForwardAllocationAboveItsLimitIsRejected()
    {
        Assert.False(Contains(10_001, 0, 0));
    }

    [Theory]
    [InlineData(-1_000, 0)]
    [InlineData(1_000, 0)]
    [InlineData(0, -1_000)]
    [InlineData(0, 1_000)]
    public void FullPrecisionAllocationIsAdmittedInEveryLocalDirection(
        long precisionForward,
        long precisionLateral)
    {
        Assert.True(Contains(0, precisionForward, precisionLateral));
    }

    [Theory]
    [InlineData(-1_001, 0)]
    [InlineData(1_001, 0)]
    [InlineData(0, -1_001)]
    [InlineData(0, 1_001)]
    public void PrecisionAllocationAboveItsLimitIsRejected(
        long precisionForward,
        long precisionLateral)
    {
        Assert.False(Contains(0, precisionForward, precisionLateral));
    }

    [Fact]
    public void DiagonalPrecisionUsesOneMagnitudeLimit()
    {
        Assert.True(Contains(0, 600, 800));
        Assert.False(Contains(0, 601, 800));
    }

    [Fact]
    public void CombinedPrimaryAndPrecisionUseEllipticalBudget()
    {
        Assert.True(Contains(6_000, 0, 800));
        Assert.False(Contains(6_001, 0, 800));
    }

    [Fact]
    public void CompleteFixedPointRangeIsComparedWithoutOverflow()
    {
        var limit = new ManeuverAcceleration(ulong.MaxValue);
        var allocation = new ShipRelativeThrustAllocation(
            (ulong)long.MaxValue,
            long.MinValue,
            long.MaxValue);

        Assert.True(ManeuverThrustEnvelope.Contains(
            allocation,
            limit,
            limit));
    }

    private static bool Contains(
        ulong primaryForward,
        long precisionForward,
        long precisionLateral) =>
        ManeuverThrustEnvelope.Contains(
            new ShipRelativeThrustAllocation(
                primaryForward,
                precisionForward,
                precisionLateral),
            Primary,
            Precision);
}
