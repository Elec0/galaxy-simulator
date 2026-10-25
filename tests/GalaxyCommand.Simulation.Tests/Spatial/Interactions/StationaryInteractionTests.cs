using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationaryInteractionTests
{
    [Fact]
    public void StationaryCandidateDiagnosticsDoNotDemandAShipPairIdentity()
    {
        var key = new InteractionCandidateKey(new SystemId(1), SimulationTime.Zero,
            new ShipId(1), new EntityId(2), new InteractionInterestId(3));
        Assert.False(string.IsNullOrWhiteSpace(key.ToString()));
    }

    [Fact]
    public void ScopedMotionCapturePreservesExactGeometryWithoutUnrelatedActors()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-10));
        movement.Add(new ShipId(2), Position(50));
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(-10), Position(10),
            new SimulationDuration(30)), SimulationTime.Zero, static value => value);
        var now = new SimulationTime(7);

        InteractionSpatialReadView scoped = movement.CaptureInteractionReadView(now, [new ShipId(1)]);

        Assert.Single(scoped.Participants);
        Assert.Null(scoped.Get(new ShipId(2)));
        Assert.Equal(movement.CaptureInteractionReadView(now).Get(new ShipId(1))!.Spatial, scoped.Get(new ShipId(1))!.Spatial);
    }

    [Fact]
    public void FixedPointInterestUsesTheSharedSchedulerWithoutATargetShip()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(-10));
        _ = movement.CommitStartOrReplace(new ShipId(1),
            new TravelLeg.Local(Position(-10), Position(10), new SimulationDuration(20)),
            SimulationTime.Zero, static value => value);
        var owner = new InteractionInterestOwner("deployable", new IdSequence<InteractionInterestId>());
        StationaryInteractionInterest interest = owner.RegisterStationary("deploy", new ShipId(1), Position(0), 5);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();

        EventKey key = Assert.Single(coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value));

        Assert.Equal(new SimulationTime(5), key.Timestamp);
        agenda.AdvanceTo(key.Timestamp);
        agenda.EnterPhase(EventPhase.StateUpdate);
        ScheduledEvent<InteractionForecast> scheduled = agenda.PopNextInCurrentPhase()!;
        Assert.Equal(interest, scheduled.Payload.Candidate.Interest);
        Assert.False(string.IsNullOrWhiteSpace(scheduled.Payload.ToString()));
        Assert.Single(movement.CaptureInteractionReadView(key.Timestamp).Participants);
        InteractionDueResult due = Assert.Single(coordinator.EvaluateDueWave([scheduled], key.Timestamp).Results);
        Assert.Equal(ScheduledEventDisposition.Applied, due.Disposition);
    }

    [Fact]
    public void StationaryEntityRevisionInvalidatesOldContactEvidence()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        var owner = new InteractionInterestOwner("deployable", new IdSequence<InteractionInterestId>());
        StationaryInteractionInterest initial = owner.RegisterStationary("pickup", new ShipId(1), Position(0), 5,
            new EntityId(2), new SpatialRevision(1));
        InteractionForecast forecast = InteractionForecast.Evaluate(initial,
            movement.CaptureInteractionReadView(SimulationTime.Zero), SimulationTime.Zero)!;

        StationaryInteractionInterest revised = owner.UpdateStationary(initial.Id, new ShipId(1), Position(0), 5,
            new EntityId(2), new SpatialRevision(2));

        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration, forecast.Validate(revised,
            movement.CaptureInteractionReadView(SimulationTime.Zero), initial.Generation));
        Assert.True(owner.Cancel(revised.Id));
        Assert.Equal(ScheduledEventDisposition.IgnoredMissingReference, forecast.Validate(owner.GetInterest(revised.Id),
            movement.CaptureInteractionReadView(SimulationTime.Zero), initial.Generation));
    }

    private static SystemPosition Position(long x) => GameSessionTestFixture.Position(x, 0);

    [Fact]
    public void RestoredStationaryIntentRetainsIdentityAndSuccessfulDeliverySuppression()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("deployable", ids);
        StationaryInteractionInterest interest = owner.RegisterStationary("pickup", new ShipId(1), Position(5), 5, new EntityId(2));
        owner.RecordDelivery(interest, movement.CaptureInteractionReadView(SimulationTime.Zero));
        InteractionInterestOwner restored = InteractionInterestOwner.RestoreCheckpoint(owner.CaptureCheckpoint(),
            IdSequence<InteractionInterestId>.RestoreCheckpoint(ids.CaptureCheckpoint()).Value!).Value!;
        using var coordinator = new SpatialInteractionCoordinator(movement, [restored]);

        Assert.Equal(interest, Assert.Single(restored.CaptureAllSnapshot()));
        Assert.Empty(coordinator.Reevaluate(new EventAgenda<InteractionForecast>(), SimulationTime.Zero, static value => value));
        Assert.Equal(new InteractionInterestId(2), restored.RegisterStationary("next", new ShipId(1), Position(5), 5).Id);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, false)]
    [InlineData(-5, 5, true)]
    public void StationaryInstantChecksUseInclusiveBounds(long target, ulong range, bool covered)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        var owner = new InteractionInterestOwner("deployable", new IdSequence<InteractionInterestId>());
        StationaryInteractionInterest interest = owner.RegisterStationary("pickup", new ShipId(1), Position(target), range);

        Assert.Equal(covered, InteractionForecast.Evaluate(interest,
            movement.CaptureInteractionReadView(SimulationTime.Zero), SimulationTime.Zero) is not null);
    }

    [Fact]
    public void InvalidStationaryRegistrationConsumesNoIdentity()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("deployable", ids);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.RegisterStationary("deploy", default, Position(0), 5));
        Assert.Equal<ulong?>(1, ids.CaptureCheckpoint().NextValue);
        Assert.Empty(owner.CaptureAllSnapshot());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    public void MixedInterestsRetainSerialEventOrderingAcrossBatches(int workers, int batchSize)
    {
        static InteractionCandidate[] Evaluate(int workers, int batchSize)
        {
            var movement = new SpatialMovement();
            var owner = new InteractionInterestOwner("deployable", new IdSequence<InteractionInterestId>());
            for (ulong ship = 1; ship <= 4; ship++)
            {
                var id = new ShipId(ship);
                movement.Add(id, Position(-10));
                _ = movement.CommitStartOrReplace(id,
                    new TravelLeg.Local(Position(-10), Position(10), new SimulationDuration(20)),
                    SimulationTime.Zero, static value => value);
                owner.RegisterStationary("deploy", id, Position(0), 5);
            }
            owner.Register("pair", new ShipId(1), new ShipId(2), 0);
            using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
            var agenda = new EventAgenda<InteractionForecast>();
            _ = coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value,
                new InteractionBatchExecution(workers, batchSize));
            return agenda.CaptureCheckpoint().Value!.PendingEvents
                .OrderBy(scheduled => scheduled.Key.CreationSequence).Select(scheduled => scheduled.Payload.Candidate).ToArray();
        }

        InteractionCandidate[] reference = Evaluate(1, 1);
        Assert.Equal(5, reference.Length);
        Assert.Equal(reference, Evaluate(workers, batchSize));
    }
}
