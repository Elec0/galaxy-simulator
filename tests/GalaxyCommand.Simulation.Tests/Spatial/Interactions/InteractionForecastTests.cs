using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionForecastTests
{
    [Fact]
    public void BriefContactAtArrivalSurvivesThroughExactCompletionReceipt()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-10));
        movement.Add(new ShipId(2), Position(0));
        LocalMotionCommit<SpatialMovementEvent> commit = Start(movement, 1, -10, 10, 1);
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(1)));
        Assert.Equal(new SimulationTime(1), forecast.Candidate.Key.Timestamp);
        Assert.False(forecast.Candidate.Contact.InRangeAtTimestamp);
        movement.HandleEvent(commit.EventProposal!.Payload, commit.Motion!.Generation, new SimulationTime(1));

        Assert.Equal(ScheduledEventDisposition.Applied, forecast.Validate(Interest(),
            movement.CaptureInteractionReadView(new SimulationTime(1)), Interest().Generation));
        Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(new ShipId(1)));
    }

    [Fact]
    public void CompletionReceiptSurvivesSameTimestampNextLegButCannotBeFabricatedByCancellation()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-10));
        movement.Add(new ShipId(2), Position(0));
        LocalMotionCommit<SpatialMovementEvent> commit = Start(movement, 1, -10, 10, 1);
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(1)));
        movement.HandleEvent(commit.EventProposal!.Payload, commit.Motion!.Generation, new SimulationTime(1));
        _ = movement.CommitStartOrReplace(new ShipId(1),
            new TravelLeg.Local(Position(10), Position(20), new SimulationDuration(10)),
            new SimulationTime(1), static value => value);
        Assert.Equal(ScheduledEventDisposition.Applied, forecast.Validate(Interest(),
            movement.CaptureInteractionReadView(new SimulationTime(1)), Interest().Generation));

        var canceled = new SpatialMovement();
        canceled.Add(new ShipId(1), Position(-10));
        canceled.Add(new ShipId(2), Position(0));
        _ = Start(canceled, 1, -10, 10, 1);
        Assert.True(canceled.CommitCancel(new ShipId(1), new SimulationTime(1)));
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration, forecast.Validate(Interest(),
            canceled.CaptureInteractionReadView(new SimulationTime(1)), Interest().Generation));
    }

    [Fact]
    public void ForecastOrderingIsIndependentOfBatchInputOrder()
    {
        SpatialMovement movement = MovingPair();
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        InteractionForecast first = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            Interest(), view, new SimulationTime(20)));
        var secondInterest = new ShipPairInteractionInterest(new InteractionInterestId(2),
            "test", "other", new ShipId(2), new ShipId(1), 5, new EventGeneration(1));
        InteractionForecast second = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            secondInterest, view, new SimulationTime(20)));
        var agenda = new EventAgenda<InteractionForecast>();
        var scheduler = new InteractionForecastScheduler();

        IReadOnlyList<EventKey> keys = scheduler.Commit(agenda, [second, first, second], static value => value);
        Assert.Collection(keys,
            key => Assert.Equal<ulong>(0, key.CreationSequence),
            key => Assert.Equal<ulong>(1, key.CreationSequence));
        agenda.AdvanceTo(new SimulationTime(8));
        agenda.EnterPhase(EventPhase.StateUpdate);
        Assert.Equal(first, agenda.PopNextInCurrentPhase()!.Payload);
        Assert.Equal(second, agenda.PopNextInCurrentPhase()!.Payload);
    }

    [Fact]
    public void ReevaluationOfAnUnchangedTrajectoryDoesNotScheduleAnotherCrossing()
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast first = InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        InteractionForecast reevaluated = InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(new SimulationTime(1)), new SimulationTime(19))!;
        var scheduler = new InteractionForecastScheduler();
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Single(scheduler.Commit(agenda, [first], static value => value));

        Assert.Empty(scheduler.Commit(agenda, [reevaluated], static value => value));

        Assert.Equal(1, agenda.Count);
        Assert.Equal(1, scheduler.PendingCount);
    }

    [Fact]
    public void OldQueuedEventCannotConsumeASupersedingForecastWithTheSameKey()
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast old = InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        _ = movement.CommitStartOrReplace(new ShipId(1),
            new TravelLeg.Local(Position(-10), Position(10), new SimulationDuration(20)),
            SimulationTime.Zero, static value => value);
        InteractionForecast newer = InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        var scheduler = new InteractionForecastScheduler();
        var agenda = new EventAgenda<InteractionForecast>();
        _ = scheduler.Commit(agenda, [old], static value => value);
        _ = scheduler.Commit(agenda, [newer], static value => value);
        InteractionSpatialReadView due = movement.CaptureInteractionReadView(new SimulationTime(8));

        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration,
            scheduler.ValidateDue(old, Interest(), due, Interest().Generation));
        scheduler.Complete(old);
        Assert.Equal(1, scheduler.PendingCount);
        Assert.Equal(ScheduledEventDisposition.Applied,
            scheduler.ValidateDue(newer, Interest(), due, Interest().Generation));
        scheduler.Complete(newer);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Fact]
    public void ForecastCapturesBoundedMotionAndRevisionCorrelation()
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(30)));

        Assert.Equal(new SimulationTime(8), forecast.Candidate.Key.Timestamp);
        Assert.Equal(SimulationTime.Zero, forecast.From);
        Assert.Equal(new SimulationTime(20), forecast.Through);
        Assert.Equal(new SpatialRevision(1), forecast.Source.Revision);
        Assert.Equal(new SpatialRevision(1), forecast.Target.Revision);
        Assert.NotNull(forecast.Source.Spatial.Motion);
        Assert.Equal(ScheduledEventDisposition.Applied, forecast.Validate(
            Interest(), movement.CaptureInteractionReadView(new SimulationTime(8)), Interest().Generation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingAndStationaryParticipantsAreSupportedInEitherRole(bool stationarySource)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-10));
        movement.Add(new ShipId(2), Position(0));
        _ = Start(movement, 1, -10, 10, 20);
        ShipPairInteractionInterest interest = Interest(stationarySource ? 2UL : 1UL,
            stationarySource ? 1UL : 2UL);

        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            interest, movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20)));

        Assert.Equal(new SimulationTime(5), forecast.Candidate.Key.Timestamp);
        Assert.Equal(interest.Source, forecast.Candidate.Interest.Source);
    }

    [Fact]
    public void TwoStationaryParticipantsUseOnlyTheExplicitHorizon()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(5));
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(new SimulationTime(10)), new SimulationTime(15)));

        Assert.Equal(new SimulationTime(10), forecast.Candidate.Key.Timestamp);
        Assert.Equal(new SimulationTime(15), forecast.Through);
        Assert.True(forecast.Candidate.Contact.AlreadyInRange);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("replan")]
    [InlineData("remove")]
    public void OldForecastIsANoopAfterParticipantChanges(string change)
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast forecast = InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        var ship = new ShipId(1);
        if (change == "cancel") movement.CommitCancel(ship, new SimulationTime(1));
        if (change == "remove") movement.CommitRemove(ship, new SimulationTime(1));
        if (change == "replan") _ = movement.CommitStartOrReplace(ship,
            new TravelLeg.Local(Position(-9), Position(-20), new SimulationDuration(20)),
            new SimulationTime(1), static value => value);

        Assert.Equal(change == "remove" ? ScheduledEventDisposition.IgnoredMissingReference
            : ScheduledEventDisposition.IgnoredStaleGeneration,
            forecast.Validate(Interest(), movement.CaptureInteractionReadView(new SimulationTime(8)),
                Interest().Generation));
    }

    [Fact]
    public void CanceledAndChangedInterestsInvalidateForecasts()
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast forecast = InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        InteractionSpatialReadView due = movement.CaptureInteractionReadView(new SimulationTime(8));

        Assert.Equal(ScheduledEventDisposition.IgnoredMissingReference,
            forecast.Validate(null, due, Interest().Generation));
        var updated = new ShipPairInteractionInterest(new InteractionInterestId(1), "test", "contact",
            new ShipId(1), new ShipId(2), 6, new EventGeneration(2));
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration,
            forecast.Validate(updated, due, Interest().Generation));
        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch,
            forecast.Validate(Interest(), due, new EventGeneration(99)));
        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch,
            forecast.Validate(Interest(), movement.CaptureInteractionReadView(new SimulationTime(7)),
                Interest().Generation));
    }

    [Fact]
    public void TransitAndMissingParticipantsCannotProduceLocalForecasts()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        Assert.Null(InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(10)));
        movement.Add(new ShipId(2), Position(0));
        _ = movement.CommitStartConnector(new ShipId(1),
            new TravelLeg.Connector(new TransitConnectionId(1), Position(0), Position(0, 2),
                new SimulationDuration(10)), SimulationTime.Zero, static value => value);
        Assert.Null(InteractionForecast.Evaluate(Interest(),
            movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(10)));
    }

    [Fact]
    public void SchedulingDeduplicatesRepeatedTriggersWithoutAllocatingWorkerSequences()
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast forecast = InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        var scheduler = new InteractionForecastScheduler();
        var agenda = new EventAgenda<InteractionForecast>();

        IReadOnlyList<EventKey> keys = scheduler.Commit(agenda, [forecast, forecast], static value => value);
        Assert.Single(keys);
        Assert.Equal(EventPhase.StateUpdate, keys[0].Phase);
        Assert.Equal<ulong>(0, keys[0].CreationSequence);
        Assert.Empty(scheduler.Commit(agenda, [forecast], static value => value));
        Assert.Equal(1, agenda.Count);
        agenda.AdvanceTo(new SimulationTime(8));
        agenda.EnterPhase(EventPhase.StateUpdate);
        ScheduledEvent<InteractionForecast> due = Assert.IsType<ScheduledEvent<InteractionForecast>>(
            agenda.PopNextInCurrentPhase());
        Assert.Equal(forecast, due.Payload);
        scheduler.Complete(due.Payload);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Fact]
    public void FailedAgendaCommitDoesNotConsumePendingCorrelation()
    {
        SpatialMovement movement = MovingPair();
        InteractionForecast forecast = InteractionForecast.Evaluate(
            Interest(), movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(20))!;
        var scheduler = new InteractionForecastScheduler();
        var agenda = new EventAgenda<InteractionForecast>();
        agenda.AdvanceTo(new SimulationTime(9));

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Commit(agenda, [forecast], static value => value));

        Assert.Equal(0, scheduler.PendingCount);
        Assert.Equal(0, agenda.Count);
    }

    private static SpatialMovement MovingPair()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-10));
        movement.Add(new ShipId(2), Position(10));
        _ = Start(movement, 1, -10, 10, 20);
        _ = Start(movement, 2, 10, -10, 20);
        return movement;
    }

    private static LocalMotionCommit<SpatialMovementEvent> Start(
        SpatialMovement movement, ulong ship, long from, long through, ulong duration) =>
        movement.CommitStartOrReplace(new ShipId(ship),
            new TravelLeg.Local(Position(from), Position(through), new SimulationDuration(duration)),
            SimulationTime.Zero, static value => value);

    private static ShipPairInteractionInterest Interest(ulong source = 1, ulong target = 2) => new(
        new InteractionInterestId(1), "test", "contact", new ShipId(source), new ShipId(target),
        5, new EventGeneration(1));

    private static SystemPosition Position(long x, ulong system = 1) => new(
        new SystemId(system), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
