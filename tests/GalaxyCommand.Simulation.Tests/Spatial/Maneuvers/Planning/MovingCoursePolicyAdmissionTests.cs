using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class MovingCoursePolicyAdmissionTests
{
    [Fact]
    public void RequiredUnrepresentableTurnCannotFallBackToHeadingPreservingTranslation()
    {
        var origin = new SystemPosition(new(1), new(new(0), new(0)));
        var destination = new SystemPosition(new(1), new(new(0), new(-100)));
        var start = new ShipKinematicState(origin, new(0, -1_000), ShipHeading.Zero);
        EffectiveShipManeuverCapability capability = new ShipManeuverCapability(10_000,
            new(10_000), null, new(30_000), new(100_000), new(1_000_000_000), new(10_000)).ResolveForMass(10_000);
        Assert.NotNull(BoundedTerminalManeuverPlanner.SelectWithoutMovingTurn(new(0), start, destination, null,
            capability, ManeuverObjective.FastestArrival).ExecutablePlan);

        BoundedTerminalPlanSelection result = BoundedTerminalManeuverPlanner.Select(new(0), start, destination, null,
            capability, ManeuverObjective.FastestArrival);

        Assert.Equal(BoundedTerminalPlanKind.DirectionalPlanner, result.Kind);
        Assert.Null(result.ExecutablePlan);
    }
}
