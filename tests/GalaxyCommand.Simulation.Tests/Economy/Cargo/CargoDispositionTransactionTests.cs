using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDispositionTransactionTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinition Sensor = new(QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(1));

    [Fact]
    public void PrepareLeavesAllOwnersUntouchedThenCommitPublishesOneConservedDestruction()
    {
        var (owner, lifecycle, source, agenda, facts, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(7)).IsAccepted);
        CargoDispositionRequest request = Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(7))]);
        var preparation = owner.Prepare(request);
        Assert.Null(preparation.Failure);
        Assert.NotNull(preparation.Prepared);
        Assert.Equal(new Quantity(7), source.FungibleStored(Ore.Key));
        Assert.Equal(1, lifecycle.Entities.Count);
        Assert.Equal(0, agenda.Count);
        Assert.Empty(owner.CaptureReceipts());
        Assert.True(owner.CanCommit(preparation.Prepared!));
        CargoDispositionCommitResult result = owner.Commit(preparation.Prepared!);
        Assert.True(result.WasApplied);
        Assert.Equal(Quantity.Zero, source.FungibleStored(Ore.Key));
        CargoDispositionTypeOutcome outcome = Assert.Single(result.Receipt!.Outcomes);
        Assert.Equal(new Quantity(3), outcome.Recoverable);
        Assert.Equal(new Quantity(4), outcome.Destroyed);
        Assert.Equal(3UL, lifecycle.CaptureCargoObjects().Aggregate(0UL,
            (sum, cargo) => sum + lifecycle.Inventories.Get(cargo.InventoryId)!.FungibleStored(Ore.Key).Units));
        Assert.Equal(3, owner.CaptureObjects().Count);
        Assert.Single(owner.Fields.CaptureFields());
        Assert.Equal(3, agenda.Count);
        Assert.All(owner.CaptureObjects(), cargo => Assert.Equal(new SimulationTime(63_072_000_000), cargo.ExpiresAt));
        Assert.Single(facts.ReadAfter(null, 100).Facts, envelope => envelope.Fact is CargoDispositionCommittedFact);
    }

    [Fact]
    public void ExactReplayIgnoresEnumerationOrderAndDoesNotNeedRemainingSourceCargo()
    {
        var (owner, lifecycle, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(5)).IsAccepted);
        CargoDispositionRequest request = Request(policy,
            [new CargoDispositionSelection.Fungible(Ore.Key, new(2)), new CargoDispositionSelection.Fungible(Ore.Key, new(3))]);
        PreparedCargoDisposition plan = owner.Prepare(request).Prepared!;
        var committed = owner.Commit(plan);
        var replay = owner.Prepare(request with { Selected = [new CargoDispositionSelection.Fungible(Ore.Key, new(5))] });
        Assert.Same(committed.Receipt, replay.Receipt);
        Assert.Null(replay.Prepared);
        Assert.Equal(2, owner.CaptureObjects().Count);
        Assert.Equal(2, agenda.Count);
        Assert.Equal(3, lifecycle.Entities.Count);
        Assert.False(owner.Commit(plan).WasApplied);
    }

    [Fact]
    public void ConflictingReplayRejectsChangedPositionCauseOrSelection()
    {
        var (owner, _, source, _, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(5)).IsAccepted);
        CargoDispositionRequest request = Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(5))]);
        owner.Commit(owner.Prepare(request).Prepared!);
        CargoDispositionRequest[] conflicts =
        [
            request with { SourcePosition = GameSessionTestFixture.Position(1, 0) },
            request with { Cause = CargoLossCause.FunctionalEjection },
            request with { Selected = [new CargoDispositionSelection.Fungible(Ore.Key, new(4))] },
        ];
        Assert.All(conflicts, changed => Assert.Equal(CargoDispositionFailure.ConflictingReplay, owner.Prepare(changed).Failure));
        Assert.Single(owner.CaptureReceipts());
    }

    [Fact]
    public void FunctionalEjectionPreservesEveryInstanceAndCreatesNoField()
    {
        var (owner, lifecycle, source, _, _, policy) = Create();
        Assert.True(source.StoreDiscrete(Sensor, new(9)).IsAccepted);
        DiscreteItemInstance original = source.GetDiscrete(new(9))!;
        CargoDispositionRequest request = Request(policy, [new CargoDispositionSelection.Discrete(Sensor.Key, original.Id)])
            with { Cause = CargoLossCause.FunctionalEjection };
        var result = owner.Commit(owner.Prepare(request).Prepared!);
        Assert.Null(result.Receipt!.FieldHandle);
        Assert.Empty(owner.Fields.CaptureFields());
        CargoObjectSnapshot cargo = Assert.Single(lifecycle.CaptureCargoObjects());
        Assert.Same(original, lifecycle.Inventories.Get(cargo.InventoryId)!.GetDiscrete(original.Id));
        Assert.Null(source.GetDiscrete(original.Id));
    }

    [Fact]
    public void DestructionRoundsEachTypeIndependentlyAndZeroSurvivorsStillHaveReceipt()
    {
        var (owner, lifecycle, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(1)).IsAccepted);
        Assert.True(source.StoreDiscrete(Sensor, new(9)).IsAccepted);
        var result = owner.Commit(owner.Prepare(Request(policy,
            [new CargoDispositionSelection.Fungible(Ore.Key, new(1)), new CargoDispositionSelection.Discrete(Sensor.Key, new(9))])).Prepared!);
        Assert.True(result.WasApplied);
        Assert.All(result.Receipt!.Outcomes, outcome => Assert.Equal(Quantity.Zero, outcome.Recoverable));
        Assert.Empty(owner.CaptureObjects());
        Assert.Empty(owner.Fields.CaptureFields());
        Assert.Empty(lifecycle.CaptureCargoObjects());
        Assert.Equal(0, agenda.Count);
        Assert.Single(owner.CaptureReceipts());
        Assert.Equal(Quantity.Zero, source.UsedCapacity);
    }

    [Fact]
    public void StaleSourceOrAgendaRejectsBeforeAnyOwnerMutation()
    {
        var (owner, lifecycle, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(7)).IsAccepted);
        CargoDispositionRequest request = Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(5))]);
        PreparedCargoDisposition stale = owner.Prepare(request).Prepared!;
        Assert.True(source.StoreFungible(Ore, new(1)).IsAccepted);
        Assert.False(owner.CanCommit(stale));
        Assert.Equal(CargoDispositionFailure.StalePreparation, owner.Commit(stale).Failure);
        PreparedCargoDisposition agendaStale = owner.Prepare(request).Prepared!;
        agenda.Schedule(new(500), EventPhase.Decision, default, new GameEvent.SensorActionReevaluation([]));
        Assert.False(owner.CanCommit(agendaStale));
        Assert.False(owner.Commit(agendaStale).WasApplied);
        Assert.Empty(lifecycle.CaptureCargoObjects());
        Assert.Equal(new Quantity(8), source.FungibleStored(Ore.Key));
        Assert.True(owner.IsHealthy);
    }

    [Fact]
    public void ReservedSourceRequiresExplicitOwnerCleanup()
    {
        var (owner, _, source, _, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(7)).IsAccepted);
        var reservationOwner = new ReservationOwner.TransportJob(new(1));
        Assert.True(source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(7)), reservationOwner).IsAccepted);
        CargoDispositionRequest request = Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(7))]);
        Assert.Equal(CargoDispositionFailure.SourceLossRejected, owner.Prepare(request).Failure);
        Assert.NotNull(source.GetPhysicalReservation(new(1)));
        var result = owner.Commit(owner.Prepare(request with
        {
            Cleanup = [new CargoSourceReservationCleanup.Physical(new(1), reservationOwner)],
        }).Prepared!);
        Assert.True(result.WasApplied);
        Assert.Null(source.GetPhysicalReservation(new(1)));
    }

    [Fact]
    public void ExpiryRemovesRemainingContentsFieldsAndPhysicalIdentitiesWithoutOutputs()
    {
        var (owner, lifecycle, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(7)).IsAccepted);
        owner.Commit(owner.Prepare(Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(7))])).Prepared!);
        CargoLiveObject[] created = owner.CaptureObjects().ToArray();
        while (agenda.PopNextThrough(new SimulationTime(63_072_000_000)) is { } due)
            Assert.Equal(ScheduledEventDisposition.Applied, owner.Expire(due));
        Assert.Empty(owner.CaptureObjects());
        Assert.Empty(lifecycle.CaptureCargoObjects());
        Assert.Empty(owner.Fields.CaptureFields());
        Assert.Equal(1, lifecycle.Entities.Count);
        Assert.Equal(Quantity.Zero, source.UsedCapacity);
        Assert.Single(owner.CaptureReceipts());
        Assert.All(created, cargo => Assert.Null(owner.Fields.Resolve(cargo.OriginalFieldId!.Value)));
        Assert.Equal<ulong?>(5, lifecycle.CaptureCheckpoint().EntityIds.NextValue);
    }

    [Fact]
    public void StaleExpiryCannotDestroyAReplacementOrReleaseAnyOtherObject()
    {
        var (owner, lifecycle, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(5)).IsAccepted);
        owner.Commit(owner.Prepare(Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(5))])).Prepared!);
        ScheduledEvent<GameEvent> first = agenda.PopNextThrough(new SimulationTime(63_072_000_000))!;
        Assert.Equal(ScheduledEventDisposition.IgnoredStaleGeneration,
            owner.Expire(new(first.Key, first.Generation.Next(), first.Payload)));
        Assert.Equal(2, lifecycle.CaptureCargoObjects().Count);
        Assert.Equal(ScheduledEventDisposition.Applied, owner.Expire(first));
        Assert.Equal(ScheduledEventDisposition.IgnoredMissingReference, owner.Expire(first));
        Assert.Single(lifecycle.CaptureCargoObjects());
    }

    [Fact]
    public void ScheduledCausalIdentityMustMatchTheActiveAllocatedEvent()
    {
        var (owner, _, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(5)).IsAccepted);
        CargoDispositionRequest request = Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(5))]);
        Assert.Equal(CargoDispositionFailure.InvalidInput, owner.Prepare(request with
        {
            Key = request.Key with { Trigger = new CargoDispositionTrigger.Scheduled(new(new(99), EventPhase.StateUpdate, 999)) },
        }).Failure);
        EventKey causalKey = agenda.Schedule(new(1), EventPhase.StateUpdate, default, new GameEvent.SensorActionReevaluation([]));
        agenda.PopNextThrough(new(1));
        var scheduled = owner.Prepare(request with { Key = request.Key with { Trigger = new CargoDispositionTrigger.Scheduled(causalKey) } });
        Assert.NotNull(scheduled.Prepared);
        Assert.True(owner.Commit(scheduled.Prepared!).WasApplied);
    }

    [Fact]
    public void ExpiryCannotRunWhileItsEnvelopeIsStillPendingOrItsPhaseIsClosed()
    {
        var (owner, _, source, agenda, _, policy) = Create();
        Assert.True(source.StoreFungible(Ore, new(2)).IsAccepted);
        owner.Commit(owner.Prepare(Request(policy, [new CargoDispositionSelection.Fungible(Ore.Key, new(2))])).Prepared!);
        ScheduledEvent<GameEvent> pending = Assert.Single(agenda.CaptureCheckpoint().Value!.PendingEvents);
        agenda.AdvanceTo(pending.Key.Timestamp);
        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch, owner.Expire(pending));
        Assert.Single(owner.CaptureObjects());
        agenda.EnterPhase(EventPhase.StateUpdate);
        Assert.Equal(ScheduledEventDisposition.IgnoredStateMismatch, owner.Expire(pending));
        Assert.Equal(ScheduledEventDisposition.Applied, owner.Expire(agenda.PopNextInCurrentPhase()!));
    }

    private static CargoDispositionRequest Request(CargoDispositionPolicy policy, IReadOnlyList<CargoDispositionSelection> selected) =>
        new(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), GameSessionTestFixture.Position(0, 0),
            CargoLossCause.StorageModuleLoss, policy, selected, []);

    private static (CargoDispositionOwner Owner, EntityLifecycleOwner Lifecycle, Inventory Source,
        EventAgenda<GameEvent> Agenda, GameFactStore Facts, CargoDispositionPolicy Policy) Create()
    {
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        var design = new ShipDesign(new(1), "Source", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 10000, pickupRangeUnits: 5000);
        lifecycle.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 10000, new(63_072_000_000), new(1000), new(15000), new(100));
        var agenda = new EventAgenda<GameEvent>();
        var facts = new GameFactStore(100);
        return (new CargoDispositionOwner(lifecycle, new([Ore, Sensor]), policy,
                new DeterministicRandomOwner(GameSessionTestFixture.RootSeed), agenda, facts),
            lifecycle, lifecycle.Inventories.Get(new(1))!, agenda, facts, policy);
    }
}
