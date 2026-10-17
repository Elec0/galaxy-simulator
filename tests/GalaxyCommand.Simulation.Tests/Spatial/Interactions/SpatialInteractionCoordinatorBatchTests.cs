using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialInteractionCoordinatorBatchTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    [InlineData(8, 16)]
    public void ParallelReevaluationPreservesExactAgendaAndPerInterestHorizons(int workers, int batchSize)
    {
        var (movement, owner) = Fixture();
        using var reference = new SpatialInteractionCoordinator(movement, [owner]);
        using var actual = new SpatialInteractionCoordinator(movement, [owner]);
        var expectedAgenda = new EventAgenda<InteractionForecast>();
        var actualAgenda = new EventAgenda<InteractionForecast>();
        var execution = new InteractionBatchExecution(workers, batchSize);
        Assert.Equal(reference.Reevaluate(expectedAgenda, SimulationTime.Zero, static value => value),
            actual.Reevaluate(actualAgenda, SimulationTime.Zero, static value => value, execution));
        var expected = Drain(expectedAgenda);
        var found = Drain(actualAgenda);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, found);
        Assert.Contains(found, item => item.Payload.From == item.Payload.Through);
        Assert.Contains(found, item => item.Payload.Through == new SimulationTime(1000));
        Assert.Contains(found, item => item.Payload.Through == new SimulationTime(3000));
        Assert.Empty(actual.Reevaluate(actualAgenda, SimulationTime.Zero, static value => value, execution));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FailedEvaluationRetainsTriggersAndAllocatesNoAgendaSequences(int workers)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(10));
        movement.Add(new ShipId(3), Position(10));
        var motion = movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(10), new SimulationDuration(10)),
            SimulationTime.Zero, static value => value);
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("expired", new ShipId(1), new ShipId(2), 0);
        owner.Register("valid", new ShipId(2), new ShipId(3), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        var execution = new InteractionBatchExecution(workers, 1);
        ulong sequence = agenda.CaptureCheckpoint().Value!.NextCreationSequence;
        int wrapped = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.Reevaluate(agenda, new SimulationTime(20), value =>
        {
            wrapped++;
            return value;
        }, execution));
        Assert.Equal(0, wrapped);
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
        Assert.Empty(agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(sequence, agenda.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.NotEmpty(owner.CaptureChanges());
        movement.HandleEvent(motion.EventProposal!.Payload, motion.Motion!.Generation, new SimulationTime(10));
        Assert.Equal(2, coordinator.Reevaluate(agenda, new SimulationTime(20), static value => value, execution).Count);
        Assert.Empty(owner.CaptureChanges());
        Assert.Empty(coordinator.Reevaluate(agenda, new SimulationTime(20), static value => value, execution));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FailedSubmissionRetriesInSerialOrderWithoutLosingInterestChanges(int workers)
    {
        var (movement, owner) = Fixture();
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        int callerThread = Environment.CurrentManagedThreadId;
        int wrapped = 0;
        var failure = new InvalidOperationException("submission failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => coordinator.Reevaluate(agenda, SimulationTime.Zero, forecast =>
        {
            Assert.Equal(callerThread, Environment.CurrentManagedThreadId);
            if (++wrapped == 2) throw failure;
            return forecast;
        }, new InteractionBatchExecution(workers, 3))));
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
        Assert.Empty(agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.NotEmpty(owner.CaptureChanges());
        using var reference = new SpatialInteractionCoordinator(movement, [owner]);
        var expectedAgenda = new EventAgenda<InteractionForecast>();
        Assert.Equal(reference.Reevaluate(expectedAgenda, SimulationTime.Zero, static value => value),
            coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value, new InteractionBatchExecution(workers, 3)));
        Assert.Equal(Drain(expectedAgenda), Drain(agenda));
        Assert.Empty(owner.CaptureChanges());
    }

    [Fact]
    public void RejectedAgendaCommitLeavesSequencesPendingStateAndTriggersUntouched()
    {
        var (movement, owner) = Fixture();
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var invalidAgenda = new EventAgenda<InteractionForecast>();
        invalidAgenda.AdvanceTo(new SimulationTime(4000));
        ulong sequence = invalidAgenda.CaptureCheckpoint().Value!.NextCreationSequence;
        var execution = new InteractionBatchExecution(4, 7);
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.Reevaluate(invalidAgenda, SimulationTime.Zero,
            static value => value, execution));
        Assert.Equal(sequence, invalidAgenda.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.Empty(invalidAgenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
        Assert.NotEmpty(owner.CaptureChanges());
        var repairedAgenda = new EventAgenda<InteractionForecast>();
        Assert.Equal(120, coordinator.Reevaluate(repairedAgenda, SimulationTime.Zero, static value => value, execution).Count);
        Assert.Empty(owner.CaptureChanges());
    }

    [Fact]
    public void MovementOnlyTriggerSurvivesFailedSubmissionAndRearmsOneForecast()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(10));
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        var execution = new InteractionBatchExecution(4, 1);
        Assert.Empty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value, execution));
        Assert.Empty(owner.CaptureChanges());
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(20), new SimulationDuration(1000)),
            SimulationTime.Zero, static value => value);
        Assert.Throws<InvalidOperationException>(() => coordinator.Reevaluate<InteractionForecast>(agenda, SimulationTime.Zero,
            static _ => throw new InvalidOperationException("wrapper failure"), execution));
        Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value, execution));
        Assert.Equal(new SimulationTime(500), agenda.NextEventKey!.Value.Timestamp);
        Assert.Empty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value, execution));
    }

    [Fact]
    public void SequenceCapacityRejectionRetainsCompleteCoordinatorWaveForRetry()
    {
        var (movement, owner) = Fixture();
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var exhausted = EventAgenda<InteractionForecast>.RestoreCheckpoint(new EventAgendaCheckpoint<InteractionForecast>(
            SimulationTime.Zero, ulong.MaxValue - 119, [])).Value!;
        var execution = new InteractionBatchExecution(4, 7);
        Assert.Throws<OverflowException>(() => coordinator.Reevaluate(exhausted, SimulationTime.Zero, static value => value, execution));
        Assert.Equal(0, exhausted.Count);
        Assert.Equal(ulong.MaxValue - 119, exhausted.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
        Assert.NotEmpty(owner.CaptureChanges());
        var repaired = new EventAgenda<InteractionForecast>();
        Assert.Equal(120, coordinator.Reevaluate(repaired, SimulationTime.Zero, static value => value, execution).Count);
        Assert.Equal(120, repaired.Count);
        Assert.Empty(owner.CaptureChanges());
        Assert.Empty(coordinator.Reevaluate(repaired, SimulationTime.Zero, static value => value, execution));
    }

    [Fact]
    public void NullExecutionIsRejectedBeforeConsumingAnyTriggers()
    {
        var (movement, owner) = Fixture();
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Throws<ArgumentNullException>(() => coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value, null!));
        Assert.NotEmpty(owner.CaptureChanges());
        Assert.NotEmpty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
    }

    private static ScheduledEvent<InteractionForecast>[] Drain(EventAgenda<InteractionForecast> agenda)
    {
        var events = new List<ScheduledEvent<InteractionForecast>>();
        while (agenda.PopNextThrough(new SimulationTime(6000)) is { } item) events.Add(item);
        return events.ToArray();
    }

    private static (SpatialMovement Movement, InteractionInterestOwner Owner) Fixture()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(10));
        movement.Add(new ShipId(3), Position(0));
        movement.Add(new ShipId(4), Position(25));
        movement.Add(new ShipId(5), Position(25));
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(20), new SimulationDuration(1000)),
            SimulationTime.Zero, static value => value);
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(90), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!;
        movement.CommitStartTerminalManeuver(new ShipId(3), plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
        var owner = new InteractionInterestOwner("batch-coordinator", new IdSequence<InteractionInterestId>());
        for (int i = 0; i < 40; i++)
        {
            owner.Register($"linear-{i}", new ShipId(1), new ShipId(2), 0);
            owner.Register($"analytic-{i}", new ShipId(3), new ShipId(4), 0);
            owner.Register($"stationary-{i}", new ShipId(4), new ShipId(5), 0);
        }
        return (movement, owner);
    }

    private static SystemPosition Position(long x) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
