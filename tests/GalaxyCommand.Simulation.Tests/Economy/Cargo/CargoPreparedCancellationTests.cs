using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoPreparedCancellationTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void PreparationLeavesAttemptCapacityAndAgendaUntouchedUntilApply()
    {
        Fixture fixture = Create();
        CargoCollectionAttempt attempt = fixture.Attempts.Get(new(1))!;
        ulong sequence = fixture.Agenda.NextCreationSequence;
        Assert.True(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey>(), out var prepared));
        Assert.Equal(attempt, fixture.Attempts.Get(new(1)));
        Assert.Equal(new Quantity(5), fixture.Collector.ReservedCapacity);
        Assert.Equal(2, fixture.Agenda.Count);
        Assert.True(fixture.Attempts.CanCommitCancellation(prepared!));
        Assert.True(fixture.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.Null(fixture.Attempts.Get(new(1)));
        Assert.Equal(Quantity.Zero, fixture.Collector.ReservedCapacity);
        Assert.Equal(1, fixture.Agenda.Count);
        Assert.Equal(sequence, fixture.Agenda.NextCreationSequence);
        Assert.False(fixture.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.True(fixture.Attempts.IsHealthy);
    }

    [Fact]
    public void RestartedAttemptRejectsStaleCancellationWithoutReleasingNewCapacity()
    {
        Fixture fixture = Create();
        Assert.True(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey>(), out var prepared));
        Assert.True(fixture.Attempts.Cancel(new(1), new HashSet<EventKey>()));
        CargoCollectionAttempt current = fixture.Attempts.Start(fixture.Request, fixture.Movement.CaptureInteractionReadView(new(0))).Attempt!;
        Assert.False(fixture.Attempts.CanCommitCancellation(prepared!));
        Assert.False(fixture.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.Equal(current, fixture.Attempts.Get(new(1)));
        Assert.Equal(new Quantity(5), fixture.Collector.ReservedCapacity);
        Assert.NotNull(fixture.Collector.GetPhysicalReservation(current.CapacityReservationId));
        Assert.Equal(2, fixture.Agenda.Count);
    }

    [Fact]
    public void MissingFutureWorkRejectsPreparationWithoutPoisoningOrReleasingCapacity()
    {
        Fixture fixture = Create();
        CargoCollectionAttempt attempt = fixture.Attempts.Get(new(1))!;
        Assert.True(fixture.Agenda.TryCancelExact(attempt.CompletionKey, attempt.Generation,
            new GameEvent.CargoCollectionCompletion(attempt.ShipId, attempt.OrderId, attempt.Generation)));
        Assert.False(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey> { attempt.CompletionKey }, out _));
        Assert.True(fixture.Attempts.IsHealthy);
        Assert.Equal(attempt, fixture.Attempts.Get(new(1)));
        Assert.Equal(new Quantity(5), fixture.Collector.ReservedCapacity);
    }

    [Fact]
    public void MissingReservationRejectsBeforeCancellingEitherPendingEvent()
    {
        Fixture fixture = Create(moving: true);
        CargoCollectionAttempt attempt = fixture.Attempts.Get(new(1))!;
        Assert.True(fixture.Collector.ReleasePhysicalReservation(attempt.CapacityReservationId,
            new ReservationOwner.CargoCollection(attempt.ShipId, attempt.OrderId, attempt.Generation)).IsAccepted);
        int count = fixture.Agenda.Count;
        Assert.False(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey>(), out _));
        Assert.Equal(count, fixture.Agenda.Count);
        Assert.True(fixture.Attempts.IsHealthy);
        Assert.Equal(attempt, fixture.Attempts.Get(new(1)));
    }

    [Fact]
    public void CapturedDueKeysAreCopiedAndExactPoppedExitNeedsNoSecondCancellation()
    {
        Fixture fixture = Create(moving: true);
        CargoCollectionAttempt attempt = fixture.Attempts.Get(new(1))!;
        ScheduledEvent<GameEvent> exit = fixture.Agenda.PopNextThrough(attempt.ExitKey!.Value.Timestamp)!;
        var due = new HashSet<EventKey> { exit.Key };
        ShipSpatialState motion = fixture.Movement.GetState(new(1))!;
        Assert.True(fixture.Attempts.TryPrepareCancellation(new(1), due, out var prepared));
        due.Clear();
        Assert.True(fixture.Attempts.CanCommitCancellation(prepared!));
        Assert.True(fixture.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.Equal(Quantity.Zero, fixture.Collector.ReservedCapacity);
        Assert.Equal(1, fixture.Agenda.Count);
        Assert.Same(motion, fixture.Movement.GetState(new(1)));
    }

    [Fact]
    public void WorkChangedAfterPreparationRejectsBeforeReleasingReservationOrOtherWork()
    {
        Fixture fixture = Create(moving: true);
        CargoCollectionAttempt attempt = fixture.Attempts.Get(new(1))!;
        Assert.True(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey>(), out var prepared));
        Assert.True(fixture.Agenda.TryCancelExact(attempt.CompletionKey, attempt.Generation,
            new GameEvent.CargoCollectionCompletion(attempt.ShipId, attempt.OrderId, attempt.Generation)));
        Assert.False(fixture.Attempts.CanCommitCancellation(prepared!));
        Assert.False(fixture.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.Equal(new Quantity(5), fixture.Collector.ReservedCapacity);
        Assert.Equal(AgendaCancellationCheck.Matches, fixture.Agenda.CheckCancellation(attempt.ExitKey!.Value,
            attempt.Generation, new GameEvent.CargoCollectionRangeExit(attempt.ShipId, attempt.OrderId, attempt.Generation)));
        Assert.True(fixture.Attempts.IsHealthy);
    }

    [Fact]
    public void CancellationCannotApplyToAnotherOwnerAndUnknownActorHasNoPlan()
    {
        Fixture fixture = Create();
        Fixture other = Create();
        Assert.True(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey>(), out var prepared));
        Assert.False(other.Attempts.CanCommitCancellation(prepared!));
        Assert.False(other.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.False(fixture.Attempts.TryPrepareCancellation(new(999), new HashSet<EventKey>(), out var missing));
        Assert.Null(missing);
        Assert.Equal(new Quantity(5), other.Collector.ReservedCapacity);
    }

    [Fact]
    public void CancellationProofCoversOnlyItsExactReservationAndLeavesOtherWorkflowsUntouched()
    {
        Fixture fixture = Create();
        Assert.True(fixture.Attempts.TryPrepareCancellation(new(1), new HashSet<EventKey>(), out var prepared));
        var otherOwner = new ReservationOwner.TransportJob(new(44));
        Assert.True(fixture.Collector.ReservePhysical(new(100), new PhysicalReservationSubject.IncomingCapacity(new(2)), otherOwner).IsAccepted);
        Assert.True(fixture.Attempts.CanCommitCancellation(prepared!));
        Assert.True(fixture.Attempts.ApplyPreparedCancellation(prepared!));
        Assert.Equal(new Quantity(2), fixture.Collector.ReservedCapacity);
        Assert.Equal(otherOwner, fixture.Collector.GetPhysicalReservation(new(100))!.Owner);
        Assert.Null(fixture.Attempts.Get(new(1)));
    }

    private static Fixture Create(bool moving = false)
    {
        var movement = new SpatialMovement();
        var design = new ShipDesign(new(1), "Collector", new ConstructionRecipe([], new Work(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, 10000, 5000);
        var lifecycle = new EntityLifecycleOwner(movement, new ActorControlRegistry(), new ShipOrderCoordinator(), [], shipDefinitions: [design]);
        var origin = new SystemPosition(new(1), new(new(0), new(0)));
        lifecycle.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), design, origin, GameSessionTestFixture.PlayerController)]);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "cancellation"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            1, 0, new(10000), new(1000), new(15000), new(100));
        var agenda = new EventAgenda<GameEvent>();
        Inventory collector = lifecycle.GetRequiredCargo(new(1));
        Assert.True(collector.StoreFungible(Ore, new(5)).IsAccepted);
        var cargo = new CargoDispositionOwner(lifecycle, new([Ore]), policy, new(GameSessionTestFixture.RootSeed), agenda, new(100));
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), origin,
            CargoLossCause.FunctionalEjection, policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(5))], []);
        Assert.True(cargo.Commit(cargo.Prepare(request).Prepared!).WasApplied);
        if (moving) movement.CommitStartOrReplace(new(1), new TravelLeg.Local(origin,
            new(new(1), new(new(10000), new(0))), new(1000)), new(0), static value => value);
        var attempts = new CargoCollectionAttemptOwner(lifecycle, cargo, policy, new(lifecycle.Inventories), movement, agenda);
        var start = new CargoCollectionStartRequest(new(1), new(1), new(2), cargo.CaptureObjects()[0].EntityId);
        Assert.NotNull(attempts.Start(start, movement.CaptureInteractionReadView(new(0))).Attempt);
        return new(attempts, collector, agenda, movement, start);
    }

    private sealed record Fixture(CargoCollectionAttemptOwner Attempts, Inventory Collector, EventAgenda<GameEvent> Agenda,
        SpatialMovement Movement, CargoCollectionStartRequest Request);
}
