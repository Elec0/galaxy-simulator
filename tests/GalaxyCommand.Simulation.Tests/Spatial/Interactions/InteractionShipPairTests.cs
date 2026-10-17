using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionShipPairTests
{
    [Fact]
    public void ReversedDiscoveryHasOneCanonicalPair()
    {
        var forward = new InteractionShipPair(new ShipId(3), new ShipId(8));
        var reverse = new InteractionShipPair(new ShipId(8), new ShipId(3));

        Assert.Equal(new ShipId(3), reverse.Lower);
        Assert.Equal(new ShipId(8), reverse.Higher);
        Assert.Equal(forward, reverse);
        Assert.Equal(0, forward.CompareTo(reverse));
        Assert.Single(new HashSet<InteractionShipPair> { forward, reverse });
    }

    [Fact]
    public void OrderingComparesLowerThenHigherWithoutOverflow()
    {
        InteractionShipPair[] pairs =
        [
            new(new ShipId(2), new ShipId(ulong.MaxValue)),
            new(new ShipId(1), new ShipId(ulong.MaxValue)),
            new(new ShipId(1), new ShipId(3)),
        ];

        Assert.Equal(new[] { pairs[2], pairs[1], pairs[0] }, pairs.Order());
    }

    [Fact]
    public void DefaultShipIdentitiesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InteractionShipPair(default, new ShipId(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InteractionShipPair(new ShipId(1), default));
    }

    [Fact]
    public void AShipCannotBeItsOwnInteractionPair()
    {
        Assert.Throws<ArgumentException>(() =>
            new InteractionShipPair(new ShipId(1), new ShipId(1)));
    }
}
