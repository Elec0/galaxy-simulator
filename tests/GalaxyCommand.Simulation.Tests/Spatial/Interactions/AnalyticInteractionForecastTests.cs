using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class AnalyticInteractionForecastTests
{
    [Fact]
    public void ForecastUsesCapturedGeometryAndTheNextObservableBoundary()
    {
        var movement = Movement(25);
        ExecutableBoundedTerminalManeuverPlan plan = Move().WithAdditionalBoundaries([new SimulationTime(2237)]);
        TerminalManeuverCommit<SpatialMovementEvent> commit = Start(movement, plan);
        InteractionSpatialReadView captured = movement.CaptureInteractionReadView(SimulationTime.Zero);
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(Interest(), captured, plan.EndsAt));
        Assert.Equal(new SimulationTime(2237), forecast.Through);
        Assert.Equal(new SimulationTime(2237), forecast.Candidate.Key.Timestamp);
        Assert.False(forecast.Candidate.Contact.InRangeAtTimestamp);
        Apply(movement, commit, 0);
        Assert.Equal(0, captured.Get(new ShipId(1))!.Spatial.Maneuver!.CurrentPhaseIndex);
        Assert.Equal(ScheduledEventDisposition.Applied, forecast.Validate(Interest(),
            movement.CaptureInteractionReadView(new SimulationTime(2237)), Interest().Generation));
    }

    [Fact]
    public void AnalyticAndLinearCompletionReceiptsPreserveABriefCrossingAndSameTimeNextLeg()
    {
        var movement = Movement(89);
        ExecutableBoundedTerminalManeuverPlan plan = Move();
        TerminalManeuverCommit<SpatialMovementEvent> commit = Start(movement, plan);
        Apply(movement, commit, 0);
        LocalMotionCommit<SpatialMovementEvent> linear = movement.CommitStartOrReplace(new ShipId(2),
            new TravelLeg.Local(Position(89), Position(91), new SimulationDuration(1)), new SimulationTime(5999), static value => value);
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(new SimulationTime(5999)), plan.EndsAt));
        Assert.Equal(new SimulationTime(6000), forecast.Candidate.Key.Timestamp);
        Assert.False(forecast.Candidate.Contact.InRangeAtTimestamp);
        Apply(movement, commit, 1);
        movement.HandleEvent(linear.EventProposal!.Payload, linear.Motion!.Generation, plan.EndsAt);
        TerminalManeuverCommit<SpatialMovementEvent> settle = movement.CommitStartTerminalManeuver(new ShipId(1),
            ExecutableBoundedTerminalManeuverPlan.TerminalSettle(plan.EndsAt,
                new ShipKinematicState(Position(90), ShipVelocity.Zero, ShipHeading.Zero)),
            ManeuverObjective.FastestArrival, plan.EndsAt, static value => value);
        Apply(movement, settle, 0);
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(90), Position(100), new SimulationDuration(10)),
            plan.EndsAt, static value => value);
        Assert.Equal(2, movement.CaptureInteractionReadView(plan.EndsAt).Get(new ShipId(1))!.ManeuverReceipts!.Count);
        Assert.Equal(ScheduledEventDisposition.Applied, forecast.Validate(Interest(),
            movement.CaptureInteractionReadView(plan.EndsAt), Interest().Generation));
        Assert.Empty(movement.CaptureInteractionReadView(new SimulationTime(6001)).Get(new ShipId(1))!.ManeuverReceipts!);
    }

    [Fact]
    public void InterruptionInvalidatesForecastWithoutCreatingABoundaryReceipt()
    {
        var movement = Movement(25);
        ExecutableBoundedTerminalManeuverPlan plan = Move();
        TerminalManeuverCommit<SpatialMovementEvent> commit = Start(movement, plan);
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(SimulationTime.Zero), plan.EndsAt));
        var agenda = new EventAgenda<SpatialMovementEvent>();
        movement.BindTerminalManeuverEvents(new ShipId(1), AgendaCommitOwner.Commit(agenda, commit.EventProposals).EventKeys);
        agenda.AdvanceTo(new SimulationTime(1));
        Assert.Equal(AgendaCancellationCheck.Matches, movement.TryInterruptTerminalManeuver(new ShipId(1),
            new SimulationTime(1), agenda, static value => value, out _));
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration, forecast.Validate(Interest(),
            movement.CaptureInteractionReadView(forecast.Candidate.Key.Timestamp), Interest().Generation));
    }

    [Fact]
    public void CoordinatorSchedulesAnalyticContactWithoutAUserSuppliedRenewalHorizon()
    {
        var movement = Movement(25);
        Start(movement, Move());
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        Assert.Equal(new SimulationTime(2237), agenda.PopNextThrough(new SimulationTime(3000))!.Payload.Candidate.Key.Timestamp);
    }

    [Fact]
    public void BothAnalyticRolesForecastTheSameRelativeCrossing()
    {
        var movement = Movement(50);
        Start(movement, Move());
        movement.CommitStartTerminalManeuver(new ShipId(2), Move(50, -40),
            ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        InteractionForecast forward = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(Interest(), view, new SimulationTime(6000)));
        var reverse = new ShipPairInteractionInterest(new InteractionInterestId(2), "test", "reverse",
            new ShipId(2), new ShipId(1), 0, default);
        InteractionForecast backward = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(reverse, view, new SimulationTime(6000)));
        Assert.Equal(new SimulationTime(2237), forward.Candidate.Key.Timestamp);
        Assert.Equal(forward.Candidate.Key.Timestamp, backward.Candidate.Key.Timestamp);
        Assert.Equal(new ShipId(2), backward.Candidate.Interest.Source);
        Assert.Equal(ScheduledEventDisposition.Applied, forward.Validate(Interest(),
            movement.CaptureInteractionReadView(forward.Candidate.Key.Timestamp), Interest().Generation));
    }

    private static void Apply(SpatialMovement movement, TerminalManeuverCommit<SpatialMovementEvent> commit, int index) =>
        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(commit.EventProposals[index].Payload,
            commit.Maneuver.Generation, commit.EventProposals[index].Timestamp));
    private static TerminalManeuverCommit<SpatialMovementEvent> Start(SpatialMovement movement, ExecutableBoundedTerminalManeuverPlan plan) =>
        movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
    private static SpatialMovement Movement(long target)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(target));
        return movement;
    }
    private static ExecutableBoundedTerminalManeuverPlan Move(long origin = 0, long destination = 90) => BoundedTerminalManeuverPlanner.Select(
        SimulationTime.Zero, new ShipKinematicState(Position(origin), ShipVelocity.Zero, ShipHeading.Zero), Position(destination), null,
        new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000), new ManeuverSpeed(30_000), new ManeuverTurnRate(45_000)).ExecutablePlan!;
    private static ShipPairInteractionInterest Interest() => new(new InteractionInterestId(1), "test", "contact",
        new ShipId(1), new ShipId(2), 0, default);
    private static SystemPosition Position(long x) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
