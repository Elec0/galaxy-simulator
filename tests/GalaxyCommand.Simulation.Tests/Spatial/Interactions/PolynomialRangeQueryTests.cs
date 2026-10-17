using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PolynomialRangeQueryTests
{
    [Fact]
    public void QuarticTangencyBetweenMillisecondsIsRetained()
    {
        // (4t² - 9)² touches zero at 1.5 ms without changing sign.
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(Find([81, 0, -72, 0, 16], 0, 3));
        Assert.Equal(new SimulationTime(2), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
        Assert.False(contact.AlreadyInRange);
    }

    [Fact]
    public void BriefAcceleratedCrossingDoesNotRequireAnIntegerSampleInRange()
    {
        // (100t² - 2)² - 1 enters and exits during the first millisecond.
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(Find([3, 0, -400, 0, 10_000], 0, 5));
        Assert.Equal(new SimulationTime(1), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
    }

    [Theory]
    [InlineData(0, 6, 1, true, false)]
    [InlineData(2, 6, 2, true, true)]
    [InlineData(3, 6, 4, true, false)]
    [InlineData(5, 5, 5, true, true)]
    public void MultipleContactIntervalsReturnTheFirstInTheClosedHorizon(
        ulong from, ulong through, ulong expected, bool inRange, bool already)
    {
        // (t-1)(t-2)(t-4)(t-5)
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(Find([40, -78, 49, -12, 1], from, through));
        Assert.Equal(new SimulationTime(expected), contact.Timestamp);
        Assert.Equal(inRange, contact.InRangeAtTimestamp);
        Assert.Equal(already, contact.AlreadyInRange);
    }

    [Fact]
    public void NoRootOrRootOutsideHorizonProducesNoContact()
    {
        Assert.Null(Find([1, 0, 0, 0, 1], 0, ulong.MaxValue));
        Assert.Null(Find([4, -4, 1], 0, 1));
        Assert.Null(Find([4, -4, 1], 3, 5));
        Assert.Null(Find([1], 0, 5));
    }

    [Fact]
    public void ConstantAndLinearPredicatesPreserveInclusiveContact()
    {
        Assert.True(Find([0], 4, 4)!.AlreadyInRange);
        Assert.True(Find([-1], 4, 8)!.InRangeAtTimestamp);
        Assert.Equal(new SimulationTime(3), Find([5, -2], 0, 3)!.Timestamp);
        Assert.Equal(new SimulationTime(2), Find([2, -1], 0, 2)!.Timestamp);
    }

    [Fact]
    public void LargeCoefficientsAndTimelineOffsetsDoNotOverflowOrChangeRoots()
    {
        BigInteger scale = BigInteger.One << 256;
        ulong origin = ulong.MaxValue - 4;
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(PolynomialRangeQuery.FindFirstContact(
            new BigInteger[] { 81, 0, -72, 0, 16 }.Select(value => value * scale),
            new SimulationTime(origin), new SimulationTime(origin), new SimulationTime(ulong.MaxValue)));
        Assert.Equal(new SimulationTime(ulong.MaxValue - 2), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void InvalidDegreeOriginAndHorizonAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Find([], 0, 3));
        Assert.Throws<ArgumentException>(() => Find([1, 0, 0, 0, 0, 1], 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Find([1], 3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolynomialRangeQuery.FindFirstContact(
            [1], new SimulationTime(2), SimulationTime.Zero, new SimulationTime(3)));
    }

    [Fact]
    public void FactoredRationalRootOracleCoversMultiplicityAndClippedIntervals()
    {
        int[] numerators = [-3, -1, 1, 2, 3, 5];
        foreach (int first in numerators)
        foreach (int second in numerators)
        foreach (int third in numerators)
        foreach (int fourth in numerators)
        {
            int[] roots = [first, second, third, fourth];
            BigInteger[] polynomial = [1];
            foreach (int root in roots)
            {
                var product = new BigInteger[polynomial.Length + 1];
                for (int index = 0; index < polynomial.Length; index++)
                {
                    product[index] -= root * polynomial[index];
                    product[index + 1] += 2 * polynomial[index];
                }
                polynomial = product;
            }

            for (ulong from = 0; from <= 3; from++)
            {
                BigInteger startValue = roots.Aggregate(BigInteger.One,
                    (product, root) => product * (2 * (BigInteger)from - root));
                int? next = roots.Where(root => root >= 2 * (long)from).Cast<int?>().Min();
                ulong? expected = startValue <= 0 ? from : next is { } root ? (ulong)((root + 1) / 2) : null;
                SweptRangeContact? actual = Find(polynomial, from, 3);
                Assert.Equal(expected, actual?.Timestamp.Milliseconds);
                if (actual is not null) Assert.Equal(startValue <= 0, actual.AlreadyInRange);
            }
        }
    }

    private static SweptRangeContact? Find(BigInteger[] coefficients, ulong from, ulong through) =>
        PolynomialRangeQuery.FindFirstContact(coefficients, SimulationTime.Zero,
            new SimulationTime(from), new SimulationTime(through));

    [Fact]
    public void RationalHorizonRetainsOnlyRootsWithinTheFractionalInterval()
    {
        var zero = InteractionFraction.Integer(0);
        var before = new InteractionFraction(4, 3);
        var root = new InteractionFraction(3, 2);
        Assert.Null(PolynomialRangeQuery.FindFirstContact([81, 0, -72, 0, 16], SimulationTime.Zero, zero, before));
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(PolynomialRangeQuery.FindFirstContact(
            [81, 0, -72, 0, 16], SimulationTime.Zero, zero, root));
        Assert.Equal(new SimulationTime(2), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
        Assert.Null(PolynomialRangeQuery.FindFirstContact([81, 0, -72, 0, 16], SimulationTime.Zero,
            new InteractionFraction(5, 3), InteractionFraction.Integer(3)));
    }

    [Fact]
    public void AlreadyInRangeAtAFractionalStartRoundsUpWithoutClaimingIntegerSampleContact()
    {
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(PolynomialRangeQuery.FindFirstContact(
            [3, -8, 4], new SimulationTime(10), new InteractionFraction(3, 4), InteractionFraction.Integer(1)));
        Assert.Equal(new SimulationTime(11), contact.Timestamp);
        Assert.True(contact.AlreadyInRange);
        Assert.True(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void OpenEndpointExcludesOnlyContactExactlyAtTheResetBoundary()
    {
        var zero = InteractionFraction.Integer(0);
        var end = InteractionFraction.Integer(2);
        Assert.Null(PolynomialRangeQuery.FindFirstContact([4, -4, 1], SimulationTime.Zero, zero, end, false));
        Assert.NotNull(PolynomialRangeQuery.FindFirstContact([81, 0, -72, 0, 16], SimulationTime.Zero, zero, end, false));
        Assert.NotNull(PolynomialRangeQuery.FindFirstContact([4, -4, 1], SimulationTime.Zero, zero, end, true));
    }
}
