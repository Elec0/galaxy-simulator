using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionDueWaveTests
{
    [Fact]
    public void DueWaveUsesOneStableViewAndBuffersBeforePendingConsumption()
    {
        var movement = Movement();
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest first = owner.Register("first", new ShipId(1), new ShipId(2), 0);
        ShipPairInteractionInterest second = owner.Register("second", new ShipId(2), new ShipId(1), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        ScheduledEvent<InteractionForecast>[] due = [agenda.PopNextThrough(SimulationTime.Zero)!, agenda.PopNextThrough(SimulationTime.Zero)!];

        InteractionDueWave wave = coordinator.EvaluateDueWave(due.Reverse(), SimulationTime.Zero);

        Assert.Equal(new[] { first.Id, second.Id }, wave.Results.Select(result => result.Event.Payload.Candidate.Interest.Id));
        Assert.All(wave.Results, result => Assert.Equal(ScheduledEventDisposition.Applied, result.Disposition));
        Assert.Equal(2, coordinator.Scheduler.PendingCount);
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(10), new SimulationDuration(10)),
            SimulationTime.Zero, static value => value);
        Assert.Null(wave.View.Get(new ShipId(1))!.Spatial.Motion);
        coordinator.CompleteDueWave(wave);
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
    }

    [Fact]
    public void ForeignAgendaEnvelopeCannotConsumeTheRegisteredPendingForecast()
    {
        var movement = Movement();
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        ScheduledEvent<InteractionForecast> actual = agenda.PopNextThrough(SimulationTime.Zero)!;
        var foreign = actual with { Key = actual.Key with { CreationSequence = 99 } };
        InteractionDueWave wave = coordinator.EvaluateDueWave([foreign], SimulationTime.Zero);
        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch, Assert.Single(wave.Results).Disposition);
        coordinator.CompleteDueWave(wave);
        Assert.Equal(1, coordinator.Scheduler.PendingCount);
        Assert.Equal(ScheduledEventDisposition.Applied,
            Assert.Single(coordinator.EvaluateDueWave([actual], SimulationTime.Zero).Results).Disposition);
    }

    [Fact]
    public void WaveRejectsDuplicateOrMistimedEnvelopesWithoutConsumingPendingWork()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(Movement(), [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        ScheduledEvent<InteractionForecast> due = agenda.PopNextThrough(SimulationTime.Zero)!;
        Assert.Throws<ArgumentException>(() => coordinator.EvaluateDueWave([due, due], SimulationTime.Zero));
        Assert.Throws<ArgumentException>(() => coordinator.EvaluateDueWave([due], new SimulationTime(1)));
        Assert.Equal(1, coordinator.Scheduler.PendingCount);
    }

    [Fact]
    public void CancellationBeforeReevaluationIsObservedByDueWave()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(Movement(), [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        ScheduledEvent<InteractionForecast> due = agenda.PopNextThrough(SimulationTime.Zero)!;
        owner.Cancel(interest.Id);

        InteractionDueWave wave = coordinator.EvaluateDueWave([due], SimulationTime.Zero);

        Assert.Equal(ScheduledEventDisposition.IgnoredMissingReference, Assert.Single(wave.Results).Disposition);
        coordinator.CompleteDueWave(wave);
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
    }

    [Fact]
    public void CompletingBufferedWavePreservesReplacementAndRejectsForeignOwner()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(Movement(), [owner]);
        using var other = new SpatialInteractionCoordinator(Movement(), []);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        ScheduledEvent<InteractionForecast> due = agenda.PopNextThrough(SimulationTime.Zero)!;
        InteractionDueWave wave = coordinator.EvaluateDueWave([due], SimulationTime.Zero);
        owner.Update(interest.Id, new ShipId(1), new ShipId(2), 1);
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);

        Assert.Throws<ArgumentException>(() => other.CompleteDueWave(wave));
        coordinator.CompleteDueWave(wave);

        Assert.Equal(1, coordinator.Scheduler.PendingCount);
        ScheduledEvent<InteractionForecast> replacement = agenda.PopNextThrough(SimulationTime.Zero)!;
        Assert.Equal(ScheduledEventDisposition.Applied,
            Assert.Single(coordinator.EvaluateDueWave([replacement], SimulationTime.Zero).Results).Disposition);
    }

    private static SpatialMovement Movement()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        return movement;
    }
    private static SystemPosition Position(long x) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
