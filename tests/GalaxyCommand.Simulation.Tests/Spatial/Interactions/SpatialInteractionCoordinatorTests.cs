using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialInteractionCoordinatorTests
{
    [Fact]
    public void DepartureReevaluatesDurableInterestThroughTheNextBoundaryWithoutPolling()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(8));
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Empty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        Start(movement);

        Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        InteractionForecast forecast = agenda.PopNextThrough(new SimulationTime(10))!.Payload;
        Assert.Equal(new SimulationTime(8), forecast.Candidate.Key.Timestamp);
        Assert.Equal(new SimulationTime(10), forecast.Through);
        Assert.Empty(coordinator.Reevaluate(agenda, new SimulationTime(1), static value => value));
    }

    [Fact]
    public void UpdatingAndCancelingInterestsUpdatesTheParticipantIndex()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(8));
        movement.Add(new ShipId(3), Position(20));
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(3), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        Start(movement);
        Assert.Empty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        owner.Update(interest.Id, new ShipId(1), new ShipId(2), 0);
        Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        owner.Cancel(interest.Id);
        Assert.Empty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        Assert.Null(owner.Get(interest.Id));
    }

    [Fact]
    public void FailedAgendaCommitRetainsDirtyTriggersForRetry()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(8));
        Start(movement);
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Throws<InvalidOperationException>(() => coordinator.Reevaluate<InteractionForecast>(agenda,
            SimulationTime.Zero, static _ => throw new InvalidOperationException("wrapper failure")));
        Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        Assert.Empty(owner.CaptureChanges());
    }

    [Fact]
    public void StationaryPairsUseAnInstantCheckAndIdenticalWavesDoNotReschedule()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Single(coordinator.Reevaluate(agenda, new SimulationTime(4), static value => value));
        InteractionForecast forecast = agenda.PopNextThrough(new SimulationTime(4))!.Payload;
        Assert.Equal(forecast.From, forecast.Through);
        Assert.Empty(coordinator.Reevaluate(agenda, new SimulationTime(4), static value => value));
    }

    [Fact]
    public void MovingPairsStopAtTheEarlierArrival()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(8));
        Start(movement);
        movement.CommitStartOrReplace(new ShipId(2),
            new TravelLeg.Local(Position(8), Position(4), new SimulationDuration(4)),
            SimulationTime.Zero, static value => value);
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();

        Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));

        InteractionForecast forecast = agenda.PopNextThrough(new SimulationTime(4))!.Payload;
        Assert.Equal(new SimulationTime(4), forecast.Through);
        Assert.Equal(new SimulationTime(4), forecast.Candidate.Key.Timestamp);
    }

    [Fact]
    public void MissingParticipantAdditionWakesAnExistingInterest()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        Assert.Empty(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));
        movement.Add(new ShipId(2), Position(0));

        Assert.Single(coordinator.Reevaluate(agenda, new SimulationTime(1), static value => value));
    }

    [Fact]
    public void DistinctOwnersCannotClaimTheSameSessionIdentity()
    {
        var first = new InteractionInterestOwner("first", new IdSequence<InteractionInterestId>());
        var second = new InteractionInterestOwner("second", new IdSequence<InteractionInterestId>());
        first.Register("contact", new ShipId(1), new ShipId(2), 0);
        second.Register("contact", new ShipId(1), new ShipId(2), 0);

        Assert.Throws<InvalidOperationException>(() => new SpatialInteractionCoordinator(new SpatialMovement(), [first, second]));
    }

    private static void Start(SpatialMovement movement) => movement.CommitStartOrReplace(
        new ShipId(1), new TravelLeg.Local(Position(0), Position(10), new SimulationDuration(10)),
        SimulationTime.Zero, static value => value);

    private static SystemPosition Position(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
