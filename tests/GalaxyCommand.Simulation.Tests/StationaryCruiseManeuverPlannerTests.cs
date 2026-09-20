using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationaryCruiseManeuverPlannerTests
{
    [Fact]
    public void LongMoveSelectsStrictlyEarlierCompleteCruisePlan()
    {
        StationaryCruisePlanSelection selection =
            StationaryCruiseManeuverPlanner.Select(
                SimulationTime.Zero,
                State(),
                Position(100_000),
                requestedHeading: null,
                Capability(cruiseSpeed: 1_000_000),
                ManeuverObjective.FastestArrival);

        Assert.Equal(StationaryCruisePlanKind.Cruise, selection.Kind);
        CruiseTerminalManeuverPlan cruise =
            Assert.IsType<CruiseTerminalManeuverPlan>(selection.CruisePlan);
        StationaryDirectionalManeuverPlan subCruise =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                selection.SubCruisePlan?.CompletePlan);
        Assert.True(cruise.EndsAt < subCruise.EndsAt);
        Assert.Equal(
            ManeuverPlanRanking.Rank(cruise),
            selection.SelectedRank);
    }

    [Fact]
    public void ShortMoveRetainsCompleteSubCruisePlan()
    {
        StationaryCruisePlanSelection selection =
            StationaryCruiseManeuverPlanner.Select(
                SimulationTime.Zero,
                State(),
                Position(1_000),
                requestedHeading: null,
                Capability(cruiseSpeed: 1_000_000),
                ManeuverObjective.FastestArrival);

        Assert.Equal(StationaryCruisePlanKind.SubCruise, selection.Kind);
        Assert.NotNull(selection.SubCruisePlan?.CompletePlan);
        Assert.Null(selection.CruisePlan);
        Assert.Equal(
            selection.SubCruisePlan?.SelectedRank,
            selection.SelectedRank);
    }

    [Fact]
    public void EqualCompleteArrivalRetainsSubCruise()
    {
        StationaryCruisePlanSelection selection =
            StationaryCruiseManeuverPlanner.Select(
                SimulationTime.Zero,
                State(),
                Position(100_000),
                requestedHeading: null,
                Capability(cruiseSpeed: 300_000),
                ManeuverObjective.FastestArrival);

        Assert.Equal(StationaryCruisePlanKind.SubCruise, selection.Kind);
        Assert.NotNull(selection.SubCruisePlan?.CompletePlan);
        Assert.Null(selection.CruisePlan);
    }

    private static EffectiveShipManeuverCapability Capability(
        ulong cruiseSpeed) =>
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(10_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(300_000),
            new ManeuverSpeed(cruiseSpeed),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
        .ResolveForMass(10_000);

    private static ShipKinematicState State() =>
        new(Position(0), ShipVelocity.Zero, ShipHeading.Zero);

    private static SystemPosition Position(long x) =>
        GameSessionTestFixture.Position(x, 0);
}
