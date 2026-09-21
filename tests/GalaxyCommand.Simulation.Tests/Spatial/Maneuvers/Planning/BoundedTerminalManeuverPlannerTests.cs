using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class BoundedTerminalManeuverPlannerTests
{
    [Fact]
    public void CapabilityPathSelectsStrictlyEarlierCruisePlan()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            State(0, 0, 0, 0, 0),
            Position(100_000, 0),
            requestedHeading: null);

        Assert.Equal(BoundedTerminalPlanKind.CruiseTerminal, selection.Kind);
        CruiseTerminalManeuverPlan cruise =
            Assert.IsType<CruiseTerminalManeuverPlan>(
                selection.CruiseTerminalPlan);
        Assert.Equal(cruise.EndsAt, selection.SelectedRank?.ArrivesAt);
        Assert.Null(selection.StationaryDirectionalPlan);
        Assert.NotNull(selection.ExecutablePlan);
    }

    [Fact]
    public void CapabilityPathSelectsCompleteDirectionalTranslation()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            State(0, 0, 0, 0, 90_000),
            Position(25, 0),
            new ShipHeading(90_000));

        Assert.Equal(
            BoundedTerminalPlanKind.StationaryDirectional,
            selection.Kind);
        StationaryDirectionalPlanSelection directional =
            Assert.IsType<StationaryDirectionalPlanSelection>(
                selection.StationaryDirectionalPlan);
        Assert.Equal(
            StationaryDirectionalPlanKind.PrecisionSubCruise,
            directional.Kind);
        Assert.NotNull(directional.CompletePlan);
        Assert.Equal(directional.CompletePlan?.EndsAt, directional.SelectedRank?.ArrivesAt);
        Assert.Null(selection.StationaryTurnPlan);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Fact]
    public void CapabilityPathUsesDirectionalSelectionWithoutFinalHeading()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            State(0, 0, 0, 0, 0),
            Position(25, 0),
            requestedHeading: null);

        Assert.Equal(
            BoundedTerminalPlanKind.StationaryDirectional,
            selection.Kind);
        StationaryDirectionalPlanSelection directional =
            Assert.IsType<StationaryDirectionalPlanSelection>(
                selection.StationaryDirectionalPlan);
        Assert.Equal(
            StationaryDirectionalPlanKind.PrimarySubCruise,
            directional.Kind);
        Assert.Null(directional.CompletePlan?.RequestedHeading);
    }

    [Fact]
    public void CapabilityPathReducesThrustWhenFullRateScheduleIsUnrepresentable()
    {
        EffectiveShipManeuverCapability capability = new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(1_000_000_000_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
            .ResolveForMass(effectiveMassKilograms: 10_000);

        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0, 0, 0),
                Position(2, 0),
                requestedHeading: null,
                capability,
                ManeuverObjective.FastestArrival);

        ExecutableBoundedTerminalManeuverPlan plan =
            Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
                selection.ExecutablePlan);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            plan.StateAt(plan.EndsAt),
            Position(2, 0),
            requestedHeading: null).IsSatisfied);
        Assert.Equal(ShipVelocity.Zero, plan.StateAt(plan.EndsAt).Velocity);
    }

    [Fact]
    public void CapabilityPathReducesThrustAfterDirectionalBraking()
    {
        EffectiveShipManeuverCapability capability = new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(1_000_000_000_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
            .ResolveForMass(effectiveMassKilograms: 10_000);

        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0, 1_000, 0),
                Position(2, 0),
                requestedHeading: null,
                capability,
                ManeuverObjective.FastestArrival);

        ExecutableBoundedTerminalManeuverPlan plan =
            Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
                selection.ExecutablePlan);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            plan.StateAt(plan.EndsAt),
            Position(2, 0),
            requestedHeading: null).IsSatisfied);
    }

    [Fact]
    public void CapabilityPathAppendsRequestedHeadingToReducedThrust()
    {
        EffectiveShipManeuverCapability capability = new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(1_000_000_000_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
            .ResolveForMass(effectiveMassKilograms: 10_000);
        var requestedHeading = new ShipHeading(90_000);

        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0, 0, 0),
                Position(2, 0),
                requestedHeading,
                capability,
                ManeuverObjective.FastestArrival);

        Assert.Equal(BoundedTerminalPlanKind.ReducedThrustTerminal, selection.Kind);
        ReducedThrustTerminalManeuverPlan reduced =
            Assert.IsType<ReducedThrustTerminalManeuverPlan>(
                selection.ReducedThrustTerminalPlan);
        Assert.Equal(ManeuverPhaseKind.Turn, selection.ExecutablePlan?.Phases[^1].Kind);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            reduced.StateAt(reduced.EndsAt),
            Position(2, 0),
            requestedHeading).IsSatisfied);
    }

    [Fact]
    public void CapabilityPathPreservesPureStationaryTurnPrecedence()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            State(0, 0, 0, 0, 350_000),
            Position(0, 0),
            new ShipHeading(35_000));

        Assert.Equal(BoundedTerminalPlanKind.StationaryTurn, selection.Kind);
        Assert.NotNull(selection.StationaryTurnPlan);
        Assert.Null(selection.StationaryDirectionalPlan);
    }

    [Fact]
    public void CapabilityPathPublishesCompleteHeadingFreeMovingPlan()
    {
        EffectiveShipManeuverCapability capability = Capability();
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 1_000, 0, 0),
                Position(90, 0),
                requestedHeading: null,
                capability,
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            BoundedTerminalPlanKind.MovingAlignedTerminal,
            selection.Kind);
        MovingAlignedTerminalManeuverPlan complete =
            Assert.IsType<MovingAlignedTerminalManeuverPlan>(
                selection.MovingAlignedTerminalPlan);
        AlignedSubCruisePlanSelection aligned = complete.TranslationPlan;
        ShortMoveTriangularPlan translation =
            Assert.IsType<ShortMoveTriangularPlan>(aligned.TriangularPlan);
        Assert.Equal(
            capability.PrimaryAcceleration,
            translation.Profile.Acceleration);
        Assert.Equal(
            capability.PrecisionAcceleration,
            translation.Profile.Braking);
        Assert.Null(complete.RequestedHeading);
        Assert.Null(complete.FinalTurnPlan);
        Assert.Equal(complete.TranslationEndsAt, complete.EndsAt);
        Assert.Equal(
            ManeuverPlanRanking.Rank(complete),
            selection.SelectedRank);
        Assert.Null(selection.StationaryDirectionalPlan);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(90_000U)]
    public void CapabilityPathUsesPrecisionForNonCourseMovingHeading(
        uint heading)
    {
        EffectiveShipManeuverCapability capability = Capability();
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 1_000, 0, heading),
                Position(90, 0),
                requestedHeading: null,
                capability,
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            BoundedTerminalPlanKind.MovingAlignedTerminal,
            selection.Kind);
        MovingAlignedTerminalManeuverPlan complete =
            Assert.IsType<MovingAlignedTerminalManeuverPlan>(
                selection.MovingAlignedTerminalPlan);
        AlignedSubCruisePlanSelection aligned = complete.TranslationPlan;
        ShortMoveTriangularPlan translation =
            Assert.IsType<ShortMoveTriangularPlan>(aligned.TriangularPlan);
        Assert.Equal(
            capability.PrecisionAcceleration,
            translation.Profile.Acceleration);
        Assert.Equal(
            capability.PrecisionAcceleration,
            translation.Profile.Braking);
        Assert.Equal(
            new ShipHeading(heading),
            translation.StateAt(translation.EndsAt).Heading);
    }

    [Fact]
    public void CapabilityPathReplansAfterMovingStopMissesDestination()
    {
        EffectiveShipManeuverCapability capability = Capability();
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 2_000, 0, 90_000),
                Position(0, 0),
                requestedHeading: null,
                capability,
                ManeuverObjective.FastestArrival);

        Assert.Equal(
            BoundedTerminalPlanKind.BrakeThenStationaryDirectional,
            selection.Kind);
        BrakeThenStationaryDirectionalManeuverPlan complete =
            Assert.IsType<BrakeThenStationaryDirectionalManeuverPlan>(
                selection.BrakeThenStationaryDirectionalPlan);
        ShipKinematicState stopped = complete.BrakingPhase.StateAt(
            complete.BrakingEndsAt);
        Assert.Equal(Position(2, 0), stopped.Position);
        StationaryDirectionalManeuverPlan stationary =
            Assert.IsType<StationaryDirectionalManeuverPlan>(
                complete.StationaryPlan);
        Assert.Equal(
            stopped,
            stationary.StateAt(stationary.StartsAt));
        ManeuverCandidateRank stationaryRank = ManeuverPlanRanking.Rank(
            stationary);
        ManeuverCandidateRank completeRank = ManeuverPlanRanking.Rank(complete);
        Assert.Equal(
            stationaryRank.PhaseCount + 1,
            completeRank.PhaseCount);
        Assert.Equal(
            (UInt128)2_000 + stationaryRank.PathDistance.Millimeters,
            completeRank.PathDistance.Millimeters);
        Assert.Equal(complete.EndsAt, completeRank.ArrivesAt);
        Assert.Equal(completeRank, selection.SelectedRank);
        Assert.Null(selection.MovingAlignedTerminalPlan);
    }

    [Theory]
    [InlineData(0, 2_000, 0, 2)]
    [InlineData(-2_000, 0, -2, 0)]
    public void CapabilityPathBrakesUnsupportedVelocityBeforeDirectionalTravel(
        long velocityX,
        long velocityY,
        long stoppedX,
        long stoppedY)
    {
        SystemPosition destination = Position(90, 0);
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            State(0, 0, velocityX, velocityY, 0),
            destination,
            new ShipHeading(90_000));

        Assert.Equal(
            BoundedTerminalPlanKind.BrakeThenStationaryDirectional,
            selection.Kind);
        BrakeThenStationaryDirectionalManeuverPlan complete =
            Assert.IsType<BrakeThenStationaryDirectionalManeuverPlan>(
                selection.BrakeThenStationaryDirectionalPlan);
        ShipKinematicState stopped = complete.BrakingPhase.StateAt(
            complete.BrakingEndsAt);
        Assert.Equal(Position(stoppedX, stoppedY), stopped.Position);
        Assert.Equal(ShipVelocity.Zero, stopped.Velocity);
        Assert.Equal(
            complete.BrakingEndsAt,
            complete.StationaryPlan?.StartsAt);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            complete.StateAt(complete.EndsAt),
            destination,
            new ShipHeading(90_000)).IsSatisfied);
        Assert.Equal(
            ManeuverPlanRanking.Rank(complete),
            selection.SelectedRank);
    }

    [Fact]
    public void CapabilityPathAppendsFinalTurnToMovingTranslation()
    {
        BoundedTerminalPlanSelection selection = SelectWithCapability(
            State(0, 0, 1_000, 0, 0),
            Position(90, 0),
            new ShipHeading(90_000));

        Assert.Equal(
            BoundedTerminalPlanKind.MovingAlignedTerminal,
            selection.Kind);
        MovingAlignedTerminalManeuverPlan complete =
            Assert.IsType<MovingAlignedTerminalManeuverPlan>(
                selection.MovingAlignedTerminalPlan);
        Assert.NotNull(complete.FinalTurnPlan);
        Assert.True(complete.EndsAt > complete.TranslationEndsAt);
        ManeuverCandidateRank translationRank = ManeuverPlanRanking.Rank(
            Assert.IsType<ShortMoveTriangularPlan>(
                complete.TranslationPlan.TriangularPlan));
        ManeuverCandidateRank completeRank = ManeuverPlanRanking.Rank(complete);
        Assert.Equal(translationRank.PhaseCount + 1, completeRank.PhaseCount);
        Assert.Equal(complete.EndsAt, completeRank.ArrivesAt);
        Assert.Equal(completeRank, selection.SelectedRank);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            complete.StateAt(complete.EndsAt),
            Position(90, 0),
            new ShipHeading(90_000)).IsSatisfied);
    }

    [Fact]
    public void CapabilityPathRejectsUnknownObjectiveBeforeSelection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelectWithCapability(
                State(0, 0, 0, 0, 0),
                Position(0, 0),
                requestedHeading: null,
                (ManeuverObjective)99));
    }

    [Fact]
    public void SatisfiedRequestedHeadingSettlesBeforePlanning()
    {
        BoundedTerminalPlanSelection selection = Select(
            State(0, 0, 0, 0, 10_000),
            Position(1, 0),
            new ShipHeading(11_000));

        Assert.Equal(BoundedTerminalPlanKind.TerminalSettle, selection.Kind);
        Assert.Equal(ShipVelocity.Zero, selection.SettledState?.Velocity);
        Assert.Null(selection.StationaryTurnPlan);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Fact]
    public void ExactRestZeroDistanceHeadingGoalSelectsStationaryTurn()
    {
        BoundedTerminalPlanSelection selection = Select(
            State(0, 0, 0, 0, 350_000),
            Position(0, 0),
            new ShipHeading(35_000));

        Assert.Equal(BoundedTerminalPlanKind.StationaryTurn, selection.Kind);
        StationaryTurnManeuverPlan plan =
            Assert.IsType<StationaryTurnManeuverPlan>(
                selection.StationaryTurnPlan);
        Assert.Equal(
            new ShipHeading(35_000),
            plan.StateAt(plan.EndsAt).Heading);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Fact]
    public void MovingZeroDistanceHeadingGoalRequiresDirectionalPlanner()
    {
        BoundedTerminalPlanSelection selection = Select(
            State(0, 0, 2_000, 0, 0),
            Position(0, 0),
            new ShipHeading(90_000));

        Assert.Equal(
            BoundedTerminalPlanKind.DirectionalPlanner,
            selection.Kind);
        Assert.Null(selection.SettledState);
        Assert.Null(selection.StationaryTurnPlan);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Fact]
    public void TranslationWithRequestedHeadingRequiresDirectionalPlanner()
    {
        BoundedTerminalPlanSelection selection = Select(
            State(0, 0, 0, 0, 90_000),
            Position(90, 0),
            new ShipHeading(90_000));

        Assert.Equal(
            BoundedTerminalPlanKind.DirectionalPlanner,
            selection.Kind);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Fact]
    public void HeadingFreeTranslationRetainsAlignedSubCruiseSelection()
    {
        BoundedTerminalPlanSelection selection = Select(
            State(0, 0, 0, 0, 123_000),
            Position(90, 0),
            requestedHeading: null);

        Assert.Equal(
            BoundedTerminalPlanKind.AlignedSubCruise,
            selection.Kind);
        Assert.Equal(
            AlignedSubCruisePlanKind.Triangular,
            selection.AlignedSubCruisePlan?.Kind);
        Assert.Null(selection.StationaryTurnPlan);
    }

    [Fact]
    public void UnsupportedHeadingFreeMotionPreservesDirectionalHandoff()
    {
        BoundedTerminalPlanSelection selection = Select(
            State(0, 0, 0, 1_000, 0),
            Position(90, 0),
            requestedHeading: null);

        Assert.Equal(
            BoundedTerminalPlanKind.DirectionalPlanner,
            selection.Kind);
        Assert.Null(selection.AlignedSubCruisePlan);
    }

    [Fact]
    public void CrossSystemDestinationIsRejectedBeforeSelection()
    {
        Assert.Throws<ArgumentException>(() =>
            Select(
                State(0, 0, 0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(90),
                        new SpatialCoordinate(0))),
                requestedHeading: null));
    }

    private static BoundedTerminalPlanSelection Select(
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading) =>
        BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero,
            start,
            destination,
            requestedHeading,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000));

    private static BoundedTerminalPlanSelection SelectWithCapability(
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        ManeuverObjective objective = ManeuverObjective.FastestArrival) =>
        BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero,
            start,
            destination,
            requestedHeading,
            Capability(),
            objective);

    private static EffectiveShipManeuverCapability Capability() =>
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(10_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(30_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
        .ResolveForMass(effectiveMassKilograms: 10_000);

    private static ShipKinematicState State(
        long x,
        long y,
        long velocityX,
        long velocityY,
        uint heading) =>
        new(
            Position(x, y),
            new ShipVelocity(velocityX, velocityY),
            new ShipHeading(heading));

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
