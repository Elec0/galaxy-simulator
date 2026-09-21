using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationaryDirectionalTerminalHeadingTests
{
    [Fact]
    public void FinalHeadingCanChangePreferredCandidateToPrecision()
    {
        EffectiveShipManeuverCapability capability = Capability();
        ShipKinematicState start = State(0, 0, 90_000);

        StationaryDirectionalPlanSelection unconstrained =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                start,
                Position(25, 0),
                capability,
                ManeuverObjective.FastestArrival);
        StationaryDirectionalPlanSelection constrained =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                start,
                Position(25, 0),
                new ShipHeading(90_000),
                capability,
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.TurnThenPrimary,
            unconstrained.Kind);
        Assert.Equal(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            constrained.Kind);
        StationaryDirectionalManeuverPlan complete =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                constrained.CompletePlan);
        Assert.Null(complete.FinalTurnPlan);
        Assert.Equal(new ShipHeading(90_000), complete.StateAt(complete.EndsAt).Heading);
        Assert.Equal(
            ManeuverPlanRanking.Rank(complete),
            constrained.SelectedRank);
    }

    [Fact]
    public void SelectedPrimaryCandidateIncludesRequiredFinalTurnInRank()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(0, -90),
                new ShipHeading(180_000),
                Capability(),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.TurnThenPrimary,
            selection.Kind);
        StationaryDirectionalManeuverPlan complete =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                selection.CompletePlan);
        Assert.NotNull(complete.FinalTurnPlan);
        Assert.True(complete.EndsAt > complete.TranslationEndsAt);
        ManeuverCandidateRank translationRank = ManeuverPlanRanking.Rank(
            Assert.IsType<TurnThenSubCruiseManeuverPlan>(
                complete.TurnThenPrimaryPlan));
        ManeuverCandidateRank completeRank = ManeuverPlanRanking.Rank(complete);
        Assert.Equal(translationRank.PhaseCount + 1, completeRank.PhaseCount);
        Assert.Equal(complete.EndsAt, completeRank.ArrivesAt);
        Assert.Equal(completeRank, selection.SelectedRank);

        ShipKinematicState arrived = complete.StateAt(complete.EndsAt);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            arrived,
            Position(0, -90),
            new ShipHeading(180_000)).IsSatisfied);
    }

    [Fact]
    public void FinalTurnUsesAuthoritativeNoSnapStopPosition()
    {
        SystemPosition destination = Position(15, 20);
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 123_000),
                destination,
                new ShipHeading(90_000),
                Capability(),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            selection.Kind);
        StationaryDirectionalManeuverPlan complete =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                selection.CompletePlan);
        ShipKinematicState stopped = complete.StateAt(complete.TranslationEndsAt);
        Assert.Equal(Position(16, 20), stopped.Position);
        StationaryTurnManeuverPlan finalTurn =
            Assert.IsType<StationaryTurnManeuverPlan>(complete.FinalTurnPlan);
        Assert.Equal(stopped.Position, finalTurn.Destination);
        Assert.NotEqual(destination, finalTurn.Destination);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            complete.StateAt(complete.EndsAt),
            destination,
            new ShipHeading(90_000)).IsSatisfied);
    }

    [Fact]
    public void UnschedulableFinalTurnsReturnDirectionalHandoff()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(25, 0),
                new ShipHeading(1_001),
                Capability(turnRate: 3_000_000),
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            StationaryDirectionalPlanKind.DirectionalPlanner,
            selection.Kind);
        Assert.Null(selection.CompletePlan);
        Assert.Null(selection.SelectedRank);
    }

    [Fact]
    public void HeadingFreeSelectionPublishesCompletePlanWithoutFinalTurn()
    {
        StationaryDirectionalPlanSelection selection =
            StationaryDirectionalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0),
                Position(25, 0),
                Capability(),
                ManeuverObjective.FastestArrival);

        StationaryDirectionalManeuverPlan complete =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                selection.CompletePlan);
        Assert.Null(complete.RequestedHeading);
        Assert.Null(complete.FinalTurnPlan);
        Assert.Equal(complete.TranslationEndsAt, complete.EndsAt);
        Assert.Equal(ManeuverPlanRanking.Rank(complete), selection.SelectedRank);
    }

    private static EffectiveShipManeuverCapability Capability(
        ulong turnRate = 45_000) =>
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(10_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(turnRate),
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
