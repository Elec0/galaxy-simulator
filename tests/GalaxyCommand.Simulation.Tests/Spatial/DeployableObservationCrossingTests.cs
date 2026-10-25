using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DeployableObservationCrossingTests
{
    [Fact]
    public void CoverageHierarchyMatchesExactSourceUnionAcrossSystemsAndSourceOrders()
    {
        var sources = Enumerable.Range(0, 100).Select(index =>
        {
            var position = new SystemPosition(new SystemId((ulong)(index % 3 + 1)),
                new SpatialPosition(new SpatialCoordinate(-1_000_000_000L + index * 100), new SpatialCoordinate(index % 7 * 100)));
            return (Point: PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(position,
                SimulationTime.Zero, SimulationTime.Zero), SimulationTime.Zero), Radius: (ulong)(index % 5 * 100));
        }).ToArray();
        var first = new PlayerCoverageBoundary(SimulationTime.Zero, sources);
        var second = new PlayerCoverageBoundary(SimulationTime.Zero, sources.Reverse().ToArray());
        for (ulong system = 1; system <= 4; system++)
        for (long x = -1_000_000_050; x < -999_989_950; x += 50)
        {
            var position = new SystemPosition(new SystemId(system), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
            PreparedInteractionPoint target = PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(position,
                SimulationTime.Zero, SimulationTime.Zero), SimulationTime.Zero);
            bool expected = sources.Any(source => source.Point.InRange(target, source.Radius));
            Assert.Equal(expected, first.Contains(position));
            Assert.Equal(expected, second.Contains(position));
        }
    }

    [Fact]
    public void FractionalExitRetainsTheLastCoveredIntegralBoundary()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), GameSessionTestFixture.Position(0, 0));
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.Position(100, 0), new SimulationDuration(101)), SimulationTime.Zero, static value => value);
        SimulationTime? next = DeployableObservationCrossings.Next(movement.CaptureInteractionReadView(new SimulationTime(41)),
            [new ShipSensorCoverageInput(new EntityId(1), new ShipId(1), new PrincipalId(1), 10)],
            [GameSessionTestFixture.Position(50, 0)], new SimulationTime(101), null);
        Assert.Equal(new SimulationTime(60), next);
    }
}
