using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationaryDirectionalManeuverPlannerTests
{
    [Theory]
    [InlineData(ManeuverObjective.FastestArrival)]
    [InlineData(ManeuverObjective.ShortestPath)]
    public void ExactCourseSelectsPrimaryAccelerationWithPrecisionBraking(
        ManeuverObjective objective)
    {
        EffectiveShipManeuverCapability capability = Capability();

        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(25, 0),
                capability,
                objective);

        Assert.Equal(
            StationaryDirectionalPlanKind.PrimarySubCruise,
            selection.Kind);
        PrimarySubCruiseManeuverPlan plan =
            Assert.IsType<PrimarySubCruiseManeuverPlan>(
                selection.PrimarySubCruisePlan);
        Assert.Null(selection.TurnThenPrimaryPlan);
        Assert.Null(selection.PrecisionSubCruisePlan);
        Assert.Equal(capability.PrimaryAcceleration, plan.PrimaryAcceleration);
        Assert.Equal(capability.PrecisionAcceleration, plan.BrakingAcceleration);
        ShortMoveTriangularPlan translation =
            Assert.IsType<ShortMoveTriangularPlan>(
                plan.TranslationPlan.TriangularPlan);
        Assert.Equal(capability.PrimaryAcceleration, translation.Profile.Acceleration);
        Assert.Equal(capability.PrecisionAcceleration, translation.Profile.Braking);
        Assert.Equal(new ShipHeading(0), plan.StateAt(plan.EndsAt).Heading);
        Assert.Equal(
            ManeuverPlanRanking.Rank(plan),
            selection.SelectedRank);
    }

    [Fact]
    public void ExactCourseTurnSelectsTurnThenPrimaryCandidate()
    {
        EffectiveShipManeuverCapability capability = Capability();

        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(0, -90),
                capability,
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.TurnThenPrimary,
            selection.Kind);
        TurnThenSubCruiseManeuverPlan plan =
            Assert.IsType<TurnThenSubCruiseManeuverPlan>(
                selection.TurnThenPrimaryPlan);
        ShortMoveTriangularPlan translation =
            Assert.IsType<ShortMoveTriangularPlan>(
                plan.TranslationPlan.TriangularPlan);
        Assert.Equal(capability.PrimaryAcceleration, translation.Profile.Acceleration);
        Assert.Equal(capability.PrecisionAcceleration, translation.Profile.Braking);
        Assert.Equal(
            ManeuverPlanRanking.Rank(plan),
            selection.SelectedRank);
    }

    [Fact]
    public void ShortHalfTurnSelectsFasterPrecisionCandidate()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(-2, 0),
                Capability(),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            selection.Kind);
        PrecisionSubCruiseManeuverPlan plan =
            Assert.IsType<PrecisionSubCruiseManeuverPlan>(
                selection.PrecisionSubCruisePlan);
        Assert.Equal(
            ManeuverPlanRanking.Rank(plan),
            selection.SelectedRank);
    }

    [Fact]
    public void InexactDiscreteCourseTurnFallsBackToPrecision()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(3, -4),
                Capability(),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            selection.Kind);
        Assert.NotNull(selection.PrecisionSubCruisePlan);
        Assert.Null(selection.PrimarySubCruisePlan);
        Assert.Null(selection.TurnThenPrimaryPlan);
    }

    [Fact]
    public void FiftyMeterInexactCourseSelectsTurnThenPrecision()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(30, -40),
                Capability(),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.TurnThenPrecisionSubCruise,
            selection.Kind);
    }

    [Fact]
    public void SatisfiedDestinationSettlesWithoutPublishingCandidateRank()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 45_000),
                Position(1, 0),
                Capability(),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.TerminalSettle,
            selection.Kind);
        Assert.NotNull(selection.SettledState);
        Assert.Null(selection.SelectedRank);
        Assert.Null(selection.PrimarySubCruisePlan);
        Assert.Null(selection.TurnThenPrimaryPlan);
        Assert.Null(selection.PrecisionSubCruisePlan);
    }

    [Fact]
    public void UnrepresentableBoundedSchedulesReturnDirectionalHandoff()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(2, 0),
                Capability(baseAcceleration: 1_000_000_000_000),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.DirectionalPlanner,
            selection.Kind);
        Assert.Null(selection.SelectedRank);
    }

    [Fact]
    public void InvalidInputsAreRejectedBeforeCandidateSelection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(25, 0),
                null!,
                ManeuverObjective.FastestArrival));
        Assert.Throws<ArgumentException>(() =>
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0) with { Velocity = new ShipVelocity(1, 0) },
                Position(25, 0),
                Capability(),
                ManeuverObjective.FastestArrival));
        Assert.Throws<ArgumentException>(() =>
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(25),
                        new SpatialCoordinate(0))),
                Capability(),
                ManeuverObjective.FastestArrival));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(25, 0),
                Capability(),
                (ManeuverObjective)99));
    }

    private static EffectiveShipManeuverCapability Capability(
        ulong baseAcceleration = 10_000) =>
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(baseAcceleration),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
        .ResolveForMass(effectiveMassKilograms: 10_000);

    private static ShipKinematicState State(long x, long y, uint heading) =>
        new(Position(x, y), ShipVelocity.Zero, new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
