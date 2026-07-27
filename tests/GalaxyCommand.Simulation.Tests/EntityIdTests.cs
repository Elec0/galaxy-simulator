using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class EntityIdTests
{
    [Fact]
    public void SequencesAllocateStableAscendingIds()
    {
        var sequence = new IdSequence<ShipId>();

        ShipId first = sequence.Allocate();
        ShipId second = sequence.Allocate();

        Assert.Equal<ulong>(1, first.Value);
        Assert.Equal<ulong>(2, second.Value);
    }

    [Fact]
    public void SeparateTypedSequencesStartIndependently()
    {
        var ships = new IdSequence<ShipId>();
        var locations = new IdSequence<LocationId>();

        Assert.Equal<ulong>(1, ships.Allocate().Value);
        Assert.Equal<ulong>(1, locations.Allocate().Value);
    }

    [Fact]
    public void EntityIdsRejectZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SystemId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MotionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipOrderId(0));
    }
}
