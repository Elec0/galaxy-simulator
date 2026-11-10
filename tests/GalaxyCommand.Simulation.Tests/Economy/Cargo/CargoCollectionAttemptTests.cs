using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionAttemptTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly HashSet<EventKey> NoDue = [];

    [Fact]
    public void StartReservesOnlyIncomingCapacityAndUsesFullObjectStorageForDuration()
    {
        Fixture f = Create();
        CargoCollectionStartResult result = f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0)));
        Assert.Null(result.Failure);
        CargoCollectionAttempt attempt = Assert.IsType<CargoCollectionAttempt>(result.Attempt);
        Assert.Equal(new Quantity(2), attempt.ReservedQuantity);
        Assert.Equal(new Quantity(20), attempt.StartStorage);
        Assert.Equal(new SimulationTime(3687), attempt.DueAt);
        Assert.Equal(new Quantity(4), f.Collector.ReservedCapacity);
        Assert.Equal(new Quantity(1), f.Collector.RemainingCapacity);
        Assert.False(f.Target.HasCommitments);
        Assert.Equal(2, f.Agenda.Count);
        Assert.Null(attempt.ExitKey);
    }

    [Fact]
    public void NoFittingUnitRejectsWithoutConsumingReservationOrAttemptIdentity()
    {
        Fixture f = Create(collectorCapacity: 1);
        IdSequenceCheckpoint before = f.InventoryCommit.CaptureCheckpoint().ReservationIds;
        Assert.Equal(CargoCollectionStartFailure.InsufficientCapacity,
            f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0))).Failure);
        Assert.Equal(before, f.InventoryCommit.CaptureCheckpoint().ReservationIds);
        Assert.Null(f.Attempts.Get(new(2)));
        Assert.Equal(1, f.Agenda.Count);
        Assert.Equal(Quantity.Zero, f.Collector.ReservedCapacity);
    }

    [Fact]
    public void CancellationReleasesCapacityAndExactPendingWorkAndRestartUsesFullDuration()
    {
        Fixture f = Create();
        CargoCollectionAttempt first = f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0))).Attempt!;
        ulong sequence = f.Agenda.NextCreationSequence;
        Assert.True(f.Attempts.Cancel(new(2), NoDue));
        Assert.False(f.Attempts.Cancel(new(2), NoDue));
        Assert.Equal(sequence, f.Agenda.NextCreationSequence);
        Assert.Equal(1, f.Agenda.Count);
        Assert.Equal(Quantity.Zero, f.Collector.ReservedCapacity);
        CargoCollectionAttempt second = f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0))).Attempt!;
        Assert.Equal(first.Generation.Next(), second.Generation);
        Assert.NotEqual(first.CapacityReservationId, second.CapacityReservationId);
        Assert.Equal(first.DueAt, second.DueAt);
    }

    [Fact]
    public void StrictRangeExitForecastRetainsCommittedMotionAndDiscardsElapsedAttemptTime()
    {
        Fixture f = Create();
        CargoCollectionAttempt initial = f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0))).Attempt!;
        f.Movement.CommitStartOrReplace(new(2), new TravelLeg.Local(Position(0), Position(10000), new(1000)), new(0), static value => value);
        ShipSpatialState moving = f.Movement.GetState(new(2))!;
        f.Attempts.RefreshForecast(new(2), f.Movement.CaptureInteractionReadView(new(0)), NoDue);
        CargoCollectionAttempt refreshed = f.Attempts.Get(new(2))!;
        Assert.Equal(initial.StartedAt, refreshed.StartedAt);
        Assert.Equal(initial.DueAt, refreshed.DueAt);
        Assert.Equal(new SimulationTime(501), refreshed.ExitKey!.Value.Timestamp);
        ScheduledEvent<GameEvent> exit = f.Agenda.PopNextThrough(new(501))!;
        Assert.Equal(refreshed, f.Attempts.MatchExit(exit));
        Assert.True(f.Attempts.Cancel(new(2), new HashSet<EventKey> { exit.Key }));
        Assert.Same(moving, f.Movement.GetState(new(2)));
        Assert.Equal(Quantity.Zero, f.Collector.ReservedCapacity);
        Assert.Equal(CargoCollectionStartFailure.OutOfRange,
            f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(501))).Failure);
    }

    [Fact]
    public void OnlyExactPoppedCompletionCanMatchCurrentAttempt()
    {
        Fixture f = Create();
        CargoCollectionAttempt attempt = f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0))).Attempt!;
        ScheduledEvent<GameEvent> pending = Assert.Single(f.Agenda.CaptureCheckpoint().Value!.PendingEvents,
            item => item.Key == attempt.CompletionKey);
        Assert.Null(f.Attempts.MatchCompletion(pending));
        ScheduledEvent<GameEvent> due = f.Agenda.PopNextThrough(attempt.DueAt)!;
        Assert.Equal(attempt, f.Attempts.MatchCompletion(due));
        Assert.Null(f.Attempts.MatchCompletion(due with { Generation = due.Generation.Next() }));
        Assert.Equal(new Quantity(4), f.Collector.ReservedCapacity);
    }

    [Fact]
    public void MissingPendingWorkPoisonsAttemptAuthorityWithoutReleasingCapacity()
    {
        Fixture f = Create();
        CargoCollectionAttempt attempt = f.Attempts.Start(f.Request, f.Movement.CaptureInteractionReadView(new(0))).Attempt!;
        ScheduledEvent<GameEvent> completion = Assert.Single(f.Agenda.CaptureCheckpoint().Value!.PendingEvents,
            item => item.Key == attempt.CompletionKey);
        Assert.True(f.Agenda.TryCancelExact(completion.Key, completion.Generation, completion.Payload));
        Assert.Throws<InvalidOperationException>(() => f.Attempts.Cancel(new(2), NoDue));
        Assert.False(f.Attempts.IsHealthy);
        Assert.Equal(new Quantity(4), f.Collector.ReservedCapacity);
    }

    private static Fixture Create(ulong collectorCapacity = 5)
    {
        var movement = new SpatialMovement();
        ShipDesign Design(ulong id, ulong capacity) => new(new(id), "Fixture", new ConstructionRecipe([], new Work(1)),
            new(capacity), GameSessionTestFixture.ManeuverCapability, 10000, 5000);
        ShipDesign sourceDesign = Design(1, 100);
        ShipDesign collectorDesign = Design(2, collectorCapacity);
        var lifecycle = new EntityLifecycleOwner(movement, new ActorControlRegistry(), new ShipOrderCoordinator(), [],
            shipDefinitions: [sourceDesign, collectorDesign]);
        lifecycle.RegisterSetup([
            new InitialShipSetup(new(1), new(1), new(1), new(1), sourceDesign, Position(0), GameSessionTestFixture.PlayerController),
            new InitialShipSetup(new(2), new(2), new(2), new(1), collectorDesign, Position(0), GameSessionTestFixture.PlayerController)]);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "attempt"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            1, 0, new(63072000000), new(1000), new(15000), new(100));
        var agenda = new EventAgenda<GameEvent>();
        var cargo = new CargoDispositionOwner(lifecycle, new([Ore]), policy,
            new(GameSessionTestFixture.RootSeed), agenda, new GameFactStore(100));
        Inventory source = lifecycle.Inventories.Get(new(1))!;
        Assert.True(source.StoreFungible(Ore, new(10)).IsAccepted);
        CargoDispositionRequest loss = new(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), Position(0),
            CargoLossCause.FunctionalEjection, policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(10))], []);
        Assert.True(cargo.Commit(cargo.Prepare(loss).Prepared!).WasApplied);
        CargoObjectSnapshot target = Assert.Single(lifecycle.CaptureCargoObjects());
        var inventoryCommit = new InventoryCommitOwner(lifecycle.Inventories);
        return new(new(lifecycle, cargo, policy, inventoryCommit, movement, agenda), lifecycle.Inventories.Get(new(2))!,
            lifecycle.Inventories.Get(target.InventoryId)!, agenda, movement, inventoryCommit, new(new(2), new(1), new(2), target.EntityId));
    }

    private static SystemPosition Position(long x) => new(new(1), new(new(x), new(0)));
    private sealed record Fixture(CargoCollectionAttemptOwner Attempts, Inventory Collector, Inventory Target,
        EventAgenda<GameEvent> Agenda, SpatialMovement Movement, InventoryCommitOwner InventoryCommit, CargoCollectionStartRequest Request);
}
