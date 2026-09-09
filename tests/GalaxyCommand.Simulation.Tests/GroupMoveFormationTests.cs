using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GroupMoveFormationTests
{
    [Fact]
    public void BasicResolverAssignsCanonicalMembersClockwiseAroundDestination()
    {
        var resolver = new BasicGroupMoveFormationResolver();

        IReadOnlyList<SystemPosition> destinations = resolver.Resolve(
            [new ShipId(1), new ShipId(2), new ShipId(3), new ShipId(4)],
            GameSessionTestFixture.Position(1_000, 2_000));

        Assert.Equal(
            [
                GameSessionTestFixture.Position(1_100, 2_000),
                GameSessionTestFixture.Position(1_000, 1_900),
                GameSessionTestFixture.Position(900, 2_000),
                GameSessionTestFixture.Position(1_000, 2_100),
            ],
            destinations);
    }
}
