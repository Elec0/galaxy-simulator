using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionSettlementTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinition Sensor = new(QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(1));

    [Fact]
    public void FullCollectionPreparesWhileObjectIsNonemptyThenAtomicallyRetiresEveryResource()
    {
        Fixture fixture = Create();
        CargoCollectionSettlementRequest request = Attempt(fixture, new(5));
        CargoCollectionSettlementPreparation preparation = fixture.Owner.PrepareCollection(request);
        Assert.Null(preparation.Failure);
        Assert.True(fixture.Owner.CanCommitCollection(preparation.Prepared!));
        Assert.Equal(new Quantity(5), fixture.SourceCargo.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.ReservedCapacity);
        Assert.Single(fixture.Owner.CaptureObjects());

        CargoCollectionSettlementResult result = fixture.Owner.CommitCollection(preparation.Prepared!);
        Assert.True(result.WasApplied);
        Assert.True(result.Outcome!.Retired);
        Assert.Equal(new Quantity(5), result.Outcome.Quantity);
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.FungibleStored(Ore.Key));
        Assert.Null(fixture.CollectorCargo.GetPhysicalReservation(new(1)));
        Assert.Empty(fixture.Owner.CaptureObjects());
        Assert.Empty(fixture.Lifecycle.CaptureCargoObjects());
        Assert.Empty(fixture.Owner.Fields.CaptureFields());
        Assert.Single(fixture.Owner.Fields.CaptureCheckpoint().Tombstones);
        Assert.Null(fixture.Lifecycle.Inventories.Get(fixture.Target.InventoryId));
        Assert.Equal(0, fixture.Agenda.Count);
        Assert.Single(fixture.Facts.ReadAfter(null, 100).Facts, fact => fact.Fact is CargoCollectedFact);
        Assert.False(fixture.Owner.CommitCollection(preparation.Prepared!).WasApplied);
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.FungibleStored(Ore.Key));
    }

    [Fact]
    public void PartialPickupLeavesOriginalObjectFieldAndExpiryAndCompletesItsReservation()
    {
        Fixture fixture = Create();
        CargoLiveObject metadata = Assert.Single(fixture.Owner.CaptureObjects());
        var preparation = fixture.Owner.PrepareCollection(Attempt(fixture, new(2)));
        CargoCollectionSettlementResult result = fixture.Owner.CommitCollection(preparation.Prepared!);
        Assert.True(result.WasApplied);
        Assert.False(result.Outcome!.Retired);
        Assert.Equal(new Quantity(3), fixture.SourceCargo.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(2), fixture.CollectorCargo.FungibleStored(Ore.Key));
        Assert.Equal(metadata, Assert.Single(fixture.Owner.CaptureObjects()));
        Assert.Single(fixture.Owner.Fields.CaptureFields());
        Assert.Equal(AgendaCancellationCheck.Matches, fixture.Agenda.CheckCancellation(metadata.ExpiryEvent,
            metadata.Generation, new GameEvent.CargoExpiry(metadata.EntityId)));
        Assert.Equal(Quantity.Zero, fixture.CollectorCargo.ReservedCapacity);
    }

    [Fact]
    public void ContestedSmallerRemainderReleasesUnusedIncomingCapacityInTheSameSettlement()
    {
        Fixture fixture = Create();
        CargoCollectionSettlementRequest request = Attempt(fixture, new(5));
        var otherInventory = new Inventory(new(100), new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.Facility(new(1)), new(1)), new(100));
        fixture.Lifecycle.RegisterEconomyInventory(otherInventory);
        Assert.True(fixture.Lifecycle.Inventories.TransferPhysical(new(fixture.Target.InventoryId, otherInventory.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(2)))).IsAccepted);
        var preparation = fixture.Owner.PrepareCollection(request);
        var result = fixture.Owner.CommitCollection(preparation.Prepared!);
        Assert.True(result.WasApplied);
        Assert.Equal(new Quantity(3), result.Outcome!.Quantity);
        Assert.Equal(new Quantity(3), fixture.CollectorCargo.UsedCapacity);
        Assert.Equal(Quantity.Zero, fixture.CollectorCargo.ReservedCapacity);
        Assert.Equal(new Quantity(2), otherInventory.FungibleStored(Ore.Key));
        Assert.Empty(fixture.Owner.CaptureObjects());
    }

    [Fact]
    public void ExpiryPoppedInSameWaveDoesNotPreventEligibleCollectionOrRequireSecondCancellation()
    {
        Fixture fixture = Create();
        CargoLiveObject target = Assert.Single(fixture.Owner.CaptureObjects());
        CargoCollectionSettlementRequest request = Attempt(fixture, new(5), target.ExpiresAt);
        ScheduledEvent<GameEvent> expiry = fixture.PoppedExpiry!;
        Assert.Equal(CargoCollectionSettlementFailure.AgendaMismatch, fixture.Owner.PrepareCollection(request).Failure);
        var preparation = fixture.Owner.PrepareCollection(request with { DueExpiryKeys = new HashSet<EventKey> { expiry.Key } });
        Assert.Null(preparation.Failure);
        Assert.True(fixture.Owner.CommitCollection(preparation.Prepared!).WasApplied);
        Assert.Equal(ScheduledEventDisposition.IgnoredMissingReference, fixture.Owner.Expire(expiry));
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.FungibleStored(Ore.Key));
    }

    [Fact]
    public void MissingOrChangedExpiryRejectsBeforeInventoryAndReservationMutation()
    {
        Fixture fixture = Create();
        CargoCollectionSettlementRequest request = Attempt(fixture, new(5));
        var preparation = fixture.Owner.PrepareCollection(request);
        CargoLiveObject target = Assert.Single(fixture.Owner.CaptureObjects());
        Assert.True(fixture.Agenda.TryCancelExact(target.ExpiryEvent, target.Generation, new GameEvent.CargoExpiry(target.EntityId)));
        Assert.Equal(CargoCollectionSettlementFailure.AgendaMismatch,
            fixture.Owner.PrepareCollection(request with { DueExpiryKeys = new HashSet<EventKey> { target.ExpiryEvent } }).Failure);
        Assert.False(fixture.Owner.CanCommitCollection(preparation.Prepared!));
        Assert.False(fixture.Owner.CommitCollection(preparation.Prepared!).WasApplied);
        Assert.Equal(new Quantity(5), fixture.SourceCargo.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.ReservedCapacity);
        Assert.Single(fixture.Owner.CaptureObjects());
        Assert.True(fixture.Owner.IsHealthy);
    }

    [Fact]
    public void ChangedReservationAndMissingTargetLeaveCapacityForAttemptOwnerCleanup()
    {
        Fixture fixture = Create();
        CargoCollectionSettlementRequest request = Attempt(fixture, new(5));
        Assert.Equal(CargoCollectionSettlementFailure.ReservationMismatch,
            fixture.Owner.PrepareCollection(request with { AttemptGeneration = new(99) }).Failure);
        Assert.Equal(CargoCollectionSettlementFailure.ReservationMismatch,
            fixture.Owner.PrepareCollection(request with { OriginallyReservedQuantity = new(2) }).Failure);
        Assert.Equal(CargoCollectionSettlementFailure.TargetUnavailable,
            fixture.Owner.PrepareCollection(request with { TargetId = new(999) }).Failure);
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.ReservedCapacity);
        Assert.Equal(new Quantity(5), fixture.SourceCargo.FungibleStored(Ore.Key));
    }

    [Fact]
    public void StaleSourceOrCancelledCapacityCannotPartiallySettle()
    {
        Fixture fixture = Create();
        CargoCollectionSettlementRequest request = Attempt(fixture, new(5));
        var preparation = fixture.Owner.PrepareCollection(request);
        Assert.True(fixture.CollectorCargo.ReleasePhysicalReservation(new(1),
            new ReservationOwner.CargoCollection(new(1), new(1), new(1))).IsAccepted);
        Assert.False(fixture.Owner.CanCommitCollection(preparation.Prepared!));
        Assert.False(fixture.Owner.CommitCollection(preparation.Prepared!).WasApplied);
        Assert.Equal(new Quantity(5), fixture.SourceCargo.FungibleStored(Ore.Key));
        Assert.Equal(Quantity.Zero, fixture.CollectorCargo.UsedCapacity);
        Assert.Single(fixture.Owner.CaptureObjects());

        fixture = Create();
        request = Attempt(fixture, new(5));
        preparation = fixture.Owner.PrepareCollection(request);
        Assert.True(fixture.Lifecycle.Inventories.TransferPhysical(new(fixture.Target.InventoryId,
            fixture.CollectorCargo.Id, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(1)))).IsAccepted);
        Assert.False(fixture.Owner.CommitCollection(preparation.Prepared!).WasApplied);
        Assert.Equal(new Quantity(4), fixture.SourceCargo.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(1), fixture.CollectorCargo.FungibleStored(Ore.Key));
        Assert.Equal(new Quantity(5), fixture.CollectorCargo.ReservedCapacity);
        Assert.True(fixture.Owner.IsHealthy);
    }

    [Fact]
    public void DiscreteCollectionTransfersTheOriginalInstanceReference()
    {
        Fixture fixture = Create(discrete: true);
        DiscreteItemInstance original = fixture.SourceCargo.GetDiscrete(new(9))!;
        var preparation = fixture.Owner.PrepareCollection(Attempt(fixture, new(1)));
        Assert.True(fixture.Owner.CommitCollection(preparation.Prepared!).WasApplied);
        Assert.Same(original, fixture.CollectorCargo.GetDiscrete(new(9)));
        Assert.Empty(fixture.Owner.CaptureObjects());
        Assert.Empty(fixture.Owner.Fields.CaptureFields());
    }

    [Fact]
    public void MissingCollectorInventoryRejectsBeforeTouchingTargetResources()
    {
        Fixture fixture = Create();
        Assert.True(fixture.Lifecycle.Inventories.RemovePhysicalInventory(new(fixture.CollectorCargo.Id,
            new InventoryRemovalDisposition.DestroyContents())).IsAccepted);
        EventKey completion = fixture.Agenda.Schedule(new(100), EventPhase.StateUpdate, new(1), new GameEvent.SensorActionReevaluation([]));
        fixture.Agenda.PopNextThrough(new(100));
        var request = new CargoCollectionSettlementRequest(fixture.Collector, new(1), new(1), new(2), new(1),
            fixture.Target.EntityId, new(5), new(1), completion, new HashSet<EventKey>());
        Assert.Equal(CargoCollectionSettlementFailure.InvalidCollector, fixture.Owner.PrepareCollection(request).Failure);
        Assert.Equal(new Quantity(5), fixture.SourceCargo.FungibleStored(Ore.Key));
        Assert.Single(fixture.Owner.CaptureObjects());
    }

    private static CargoCollectionSettlementRequest Attempt(Fixture fixture, Quantity quantity, SimulationTime? due = null)
    {
        var owner = new ReservationOwner.CargoCollection(fixture.Collector.Id, new(1), new(1));
        Assert.True(fixture.CollectorCargo.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(quantity), owner).IsAccepted);
        SimulationTime at = due ?? new SimulationTime(100);
        EventKey completion = fixture.Agenda.Schedule(at, EventPhase.StateUpdate, new(1), new GameEvent.SensorActionReevaluation([]));
        while (fixture.Agenda.NextEventKey is { } next && next.Timestamp <= at)
        {
            ScheduledEvent<GameEvent> entry = fixture.Agenda.PopNextThrough(at)!;
            if (entry.Payload is GameEvent.CargoExpiry) fixture.PoppedExpiry = entry;
        }
        return new(fixture.Collector, new(1), new(1), new(2), new(1), fixture.Target.EntityId, quantity, new(1), completion, new HashSet<EventKey>());
    }

    private static Fixture Create(bool discrete = false)
    {
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        lifecycle.RegisterSetup([new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal, GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        Inventory source = lifecycle.Inventories.Get(new(1))!;
        if (discrete) Assert.True(source.StoreDiscrete(Sensor, new(9)).IsAccepted);
        else Assert.True(source.StoreFungible(Ore, new(10)).IsAccepted);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            1, 0, new(1000), new(1), new(2), new(2));
        var agenda = new EventAgenda<GameEvent>();
        var facts = new GameFactStore(100);
        var owner = new CargoDispositionOwner(lifecycle, new([Ore, Sensor]), policy,
            new DeterministicRandomOwner(GameSessionTestFixture.RootSeed), agenda, facts);
        CargoDispositionSelection selection = discrete ? new CargoDispositionSelection.Discrete(Sensor.Key, new(9))
            : new CargoDispositionSelection.Fungible(Ore.Key, new(10));
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), discrete ? CargoLossCause.FunctionalEjection : CargoLossCause.EntityDestruction,
            policy, [selection], []);
        Assert.True(owner.Commit(owner.Prepare(request).Prepared!).WasApplied);
        CargoObjectSnapshot target = Assert.Single(lifecycle.CaptureCargoObjects());
        return new(owner, lifecycle, lifecycle.GetRequiredShip(new(1)), source,
            lifecycle.Inventories.Get(target.InventoryId)!, target, agenda, facts);
    }

    private sealed record Fixture(CargoDispositionOwner Owner, EntityLifecycleOwner Lifecycle, GameSessionShip Collector,
        Inventory CollectorCargo, Inventory SourceCargo, CargoObjectSnapshot Target, EventAgenda<GameEvent> Agenda, GameFactStore Facts)
    {
        internal ScheduledEvent<GameEvent>? PoppedExpiry { get; set; }
    }
}
