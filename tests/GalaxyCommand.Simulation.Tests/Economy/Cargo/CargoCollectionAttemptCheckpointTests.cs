using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionAttemptCheckpointTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));

    [Fact]
    public void RestoreRetainsElapsedAttemptAndAllocatesNoReservationOrPendingWork()
    {
        Fixture f = Create();
        f.Agenda.AdvanceTo(new(500));
        CargoCollectionAttemptOwnerCheckpoint checkpoint = f.Attempts.CaptureCheckpoint().Value!;
        var beforeEvents = f.Agenda.CaptureCheckpoint().Value!.PendingEvents.ToArray();
        IdSequenceCheckpoint beforeIds = f.InventoryCommit.CaptureCheckpoint().ReservationIds;
        CheckpointResult<CargoCollectionAttemptOwner> result = Restore(f, checkpoint);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(f.Attempts.CaptureAttempts(), result.Value!.CaptureAttempts());
        Assert.Equal(beforeEvents, f.Agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(beforeIds, f.InventoryCommit.CaptureCheckpoint().ReservationIds);
        Assert.True(result.Value.Cancel(new(2), new HashSet<EventKey>()));
        CargoCollectionAttempt restarted = result.Value.Start(f.Request, f.Movement.CaptureInteractionReadView(new(500))).Attempt!;
        Assert.Equal(new EventGeneration(2), restarted.Generation);
        Assert.Equal(new SimulationTime(4187), restarted.DueAt);
    }

    [Fact]
    public void RestoreAllowsContestedRemainderSmallerThanReservedQuantityWithoutChangingStartSize()
    {
        Fixture f = Create();
        f.Target.ApplyRemoveFungible(Ore.Key, new(9), new(18));
        CheckpointResult<CargoCollectionAttemptOwner> restored = Restore(f, Snapshot(f));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(new Quantity(20), restored.Value!.Get(new(2))!.StartStorage);
        Assert.Equal(new Quantity(2), restored.Value.Get(new(2))!.ReservedQuantity);
    }

    [Fact]
    public void RestoreRecomputesExactMotionExitForecastWithoutReplacingItsIdentity()
    {
        Fixture f = Create(moving: true);
        CargoCollectionAttempt attempt = f.Attempts.Get(new(2))!;
        Assert.Equal(new SimulationTime(501), attempt.ExitKey!.Value.Timestamp);
        CheckpointResult<CargoCollectionAttemptOwner> restored = Restore(f, Snapshot(f));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(attempt.ExitKey, restored.Value!.Get(new(2))!.ExitKey);
        Assert.False(Restore(f, new(Snapshot(f).Generations, [attempt with { ExitKey = null }])).IsSuccess);
    }

    [Fact]
    public void CaptureRejectsOpenBoundaryAndUnhealthyAttemptAuthority()
    {
        Fixture open = Create();
        open.Agenda.EnterPhase(EventPhase.StateUpdate);
        Assert.False(open.Attempts.CaptureCheckpoint().IsSuccess);
        Assert.False(Restore(open, Snapshot(open)).IsSuccess);

        Fixture unhealthy = Create();
        CargoCollectionAttempt attempt = unhealthy.Attempts.Get(new(2))!;
        Assert.True(unhealthy.Agenda.TryCancelExact(attempt.CompletionKey, attempt.Generation,
            new GameEvent.CargoCollectionCompletion(attempt.ShipId, attempt.OrderId, attempt.Generation)));
        Assert.Throws<InvalidOperationException>(() => unhealthy.Attempts.Cancel(new(2), new HashSet<EventKey>()));
        Assert.False(unhealthy.Attempts.CaptureCheckpoint().IsSuccess);
    }

    [Fact]
    public void CancelledGenerationHistorySurvivesRemovalOfAnAllocatedShip()
    {
        Fixture f = Create();
        Assert.True(f.Attempts.Cancel(new(2), new HashSet<EventKey>()));
        var removal = Assert.IsType<EntityRemovalPreparation.Prepared>(f.Lifecycle.PrepareRemoval(
            new(new(2), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo)));
        Assert.IsType<EntityRemovalResult.Removed>(f.Lifecycle.ApplyRemoval(removal.Value, new(0)));
        CheckpointResult<CargoCollectionAttemptOwner> restored = Restore(f, new([new(new(2), new(1))], []));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Empty(restored.Value!.CaptureAttempts());
        Assert.Equal(new EventGeneration(1), Assert.Single(restored.Value.CaptureCheckpoint().Value!.Generations)!.Generation);
    }

    [Fact]
    public void CheckpointContainersCopyInputsAndRestoreCanonicalGenerationOrder()
    {
        Fixture f = Create();
        CargoCollectionGenerationCheckpoint?[] generations = [new(new(2), new(1)), new(new(1), new(3))];
        CargoCollectionAttempt?[] attempts = [f.Attempts.Get(new(2))!];
        var checkpoint = new CargoCollectionAttemptOwnerCheckpoint(generations, attempts);
        generations[0] = null;
        attempts[0] = null;
        CheckpointResult<CargoCollectionAttemptOwner> restored = Restore(f, checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(new[] { new ShipId(1), new ShipId(2) }, restored.Value!.CaptureCheckpoint().Value!.Generations.Select(row => row!.ShipId));
    }

    [Theory]
    [InlineData("zero-ship")]
    [InlineData("zero-order")]
    [InlineData("zero-command")]
    [InlineData("zero-generation")]
    [InlineData("generation-mismatch")]
    [InlineData("missing-target")]
    [InlineData("position")]
    [InlineData("definition")]
    [InlineData("unit-cost")]
    [InlineData("zero-quantity")]
    [InlineData("excess-quantity")]
    [InlineData("reservation-id")]
    [InlineData("future-start")]
    [InlineData("zero-start-size")]
    [InlineData("fractional-start-size")]
    [InlineData("undersized-start")]
    [InlineData("due-time")]
    [InlineData("completion-key")]
    [InlineData("motion-revision")]
    [InlineData("capability-revision")]
    [InlineData("pickup-range")]
    public void MalformedAttemptRejectsBeforePublishingOrChangingResources(string invalid)
    {
        Fixture f = Create();
        CargoCollectionAttempt attempt = f.Attempts.Get(new(2))!;
        CargoCollectionAttempt forged = invalid switch
        {
            "zero-ship" => attempt with { ShipId = default },
            "zero-order" => attempt with { OrderId = default },
            "zero-command" => attempt with { AcceptedCommand = default },
            "zero-generation" => attempt with { Generation = default },
            "generation-mismatch" => attempt with { Generation = new(2) },
            "missing-target" => attempt with { TargetId = new(99) },
            "position" => attempt with { TargetPosition = Position(1) },
            "definition" => attempt with { DefinitionKey = QualifiedContentKey.Create("test.cargo", "commodity", "ice") },
            "unit-cost" => attempt with { UnitCapacityCost = new(3) },
            "zero-quantity" => attempt with { ReservedQuantity = Quantity.Zero },
            "excess-quantity" => attempt with { ReservedQuantity = new(11) },
            "reservation-id" => attempt with { CapacityReservationId = new(99) },
            "future-start" => attempt with { StartedAt = new(1) },
            "zero-start-size" => attempt with { StartStorage = Quantity.Zero },
            "fractional-start-size" => attempt with { StartStorage = new(21) },
            "undersized-start" => attempt with { StartStorage = new(18) },
            "due-time" => attempt with { DueAt = new(4000) },
            "completion-key" => attempt with { CompletionKey = attempt.CompletionKey with { CreationSequence = 99 } },
            "motion-revision" => attempt with { MotionRevision = attempt.MotionRevision.Next() },
            "capability-revision" => attempt with { CapabilityRevision = attempt.CapabilityRevision.Next() },
            "pickup-range" => attempt with { PickupRange = 5001 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        CheckpointResult<CargoCollectionAttemptOwner> restored = Restore(f, new([new(new(2), new(1))], [forged]));
        Assert.False(restored.IsSuccess);
        Assert.Null(restored.Value);
        Assert.Equal(new Quantity(4), f.Collector.ReservedCapacity);
        Assert.Equal(new Quantity(10), f.Target.FungibleStored(Ore.Key));
        Assert.Equal(2, f.Agenda.Count);
    }

    [Fact]
    public void DuplicateMissingUnallocatedAndNullRowsReject()
    {
        Fixture f = Create();
        CargoCollectionAttempt attempt = f.Attempts.Get(new(2))!;
        CargoCollectionAttemptOwnerCheckpoint[] invalid =
        [
            new([], [attempt]),
            new([new(new(2), new(1)), new(new(2), new(1))], [attempt]),
            new([new(new(2), new(1)), new(new(99), new(1))], [attempt]),
            new([new(new(2), new(1)), new(default, new(1))], [attempt]),
            new([new(new(2), new(1)), new(new(1), new(0))], [attempt]),
            new([new(new(2), new(1)), null], [attempt]),
            new([new(new(2), new(1))], [attempt, attempt]),
            new([new(new(2), new(1))], [null]),
        ];
        foreach (CargoCollectionAttemptOwnerCheckpoint checkpoint in invalid)
            Assert.False(Restore(f, checkpoint).IsSuccess);
    }

    [Fact]
    public void RestoreRejectsMissingWorkAndOrphanReservationsOrAgendaEnvelopes()
    {
        Fixture missing = Create();
        CargoCollectionAttempt attempt = missing.Attempts.Get(new(2))!;
        Assert.True(missing.Agenda.TryCancelExact(attempt.CompletionKey, attempt.Generation,
            new GameEvent.CargoCollectionCompletion(attempt.ShipId, attempt.OrderId, attempt.Generation)));
        Assert.False(Restore(missing, Snapshot(missing)).IsSuccess);

        Fixture orphanReservation = Create();
        Assert.False(Restore(orphanReservation, new([new(new(2), new(1))], [])).IsSuccess);

        Fixture orphanEvent = Create();
        Assert.True(orphanEvent.Attempts.Cancel(new(2), new HashSet<EventKey>()));
        orphanEvent.Agenda.Schedule(new(5), EventPhase.StateUpdate, new(1), new GameEvent.CargoCollectionRangeExit(new(2), new(9), new(1)));
        Assert.False(Restore(orphanEvent, new([new(new(2), new(1))], [])).IsSuccess);
    }

    [Fact]
    public void RestoreRejectsCapacityReservationBeyondItsAllocatorBoundary()
    {
        Fixture f = Create();
        CargoCollectionAttempt attempt = f.Attempts.Get(new(2))!;
        var owner = new ReservationOwner.CargoCollection(attempt.ShipId, attempt.OrderId, attempt.Generation);
        Assert.True(f.Collector.ReleasePhysicalReservation(attempt.CapacityReservationId, owner).IsAccepted);
        Assert.True(f.Collector.ReservePhysical(new(2), new PhysicalReservationSubject.IncomingCapacity(new(4)), owner).IsAccepted);
        Assert.False(Restore(f, new([new(new(2), new(1))], [attempt with { CapacityReservationId = new(2) }])).IsSuccess);
    }

    private static CargoCollectionAttemptOwnerCheckpoint Snapshot(Fixture f) => new([new(new(2), new(1))], f.Attempts.CaptureAttempts());
    private static CheckpointResult<CargoCollectionAttemptOwner> Restore(Fixture f, CargoCollectionAttemptOwnerCheckpoint checkpoint) =>
        CargoCollectionAttemptOwner.Restore(checkpoint, f.Lifecycle, f.Cargo, f.Policy, f.InventoryCommit, f.Movement, f.Agenda);

    private static Fixture Create(bool moving = false)
    {
        var movement = new SpatialMovement();
        ShipDesign Design(ulong id, ulong capacity) => new(new(id), "Fixture", new ConstructionRecipe([], new Work(1)),
            new(capacity), GameSessionTestFixture.ManeuverCapability, 10000, 5000);
        ShipDesign sourceDesign = Design(1, 100);
        ShipDesign collectorDesign = Design(2, 5);
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
        source.StoreFungible(Ore, new(10));
        CargoDispositionRequest loss = new(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), Position(0),
            CargoLossCause.FunctionalEjection, policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(10))], []);
        Assert.True(cargo.Commit(cargo.Prepare(loss).Prepared!).WasApplied);
        CargoObjectSnapshot target = Assert.Single(lifecycle.CaptureCargoObjects());
        var inventoryCommit = new InventoryCommitOwner(lifecycle.Inventories);
        var attempts = new CargoCollectionAttemptOwner(lifecycle, cargo, policy, inventoryCommit, movement, agenda);
        var request = new CargoCollectionStartRequest(new(2), new(1), new(2), target.EntityId);
        if (moving) movement.CommitStartOrReplace(new(2), new TravelLeg.Local(Position(0), Position(10000), new(1000)), new(0), static value => value);
        Assert.NotNull(attempts.Start(request, movement.CaptureInteractionReadView(new(0))).Attempt);
        return new(attempts, lifecycle, cargo, policy, movement, agenda, inventoryCommit,
            lifecycle.Inventories.Get(new(2))!, lifecycle.Inventories.Get(target.InventoryId)!, request);
    }

    private static SystemPosition Position(long x) => new(new(1), new(new(x), new(0)));
    private sealed record Fixture(CargoCollectionAttemptOwner Attempts, EntityLifecycleOwner Lifecycle, CargoDispositionOwner Cargo,
        CargoDispositionPolicy Policy, SpatialMovement Movement, EventAgenda<GameEvent> Agenda, InventoryCommitOwner InventoryCommit,
        Inventory Collector, Inventory Target, CargoCollectionStartRequest Request);
}
