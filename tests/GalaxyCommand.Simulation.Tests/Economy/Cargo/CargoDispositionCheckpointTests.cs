using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDispositionCheckpointTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinition Sensor = new(QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(1));

    [Fact]
    public void RoundtripRetainsReplayObjectsFieldsAndPendingExpiryWithoutAllocating()
    {
        Fixture fixture = Create();
        CargoDispositionRequest request = CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        var result = Restore(state, fixture);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        var restored = result.Value!;
        Assert.Equal(fixture.Owner.CaptureObjects(), restored.CaptureObjects());
        Assert.Equal(fixture.Owner.Fields.CaptureCheckpoint().FieldIds, restored.Fields.CaptureCheckpoint().FieldIds);
        Assert.NotNull(restored.Prepare(request).Receipt);
        Assert.Null(restored.Prepare(request).Prepared);
        Assert.Equal(state.Receipts[0].ObjectIds, restored.CaptureReceipts()[0].ObjectIds);
        Assert.Empty(fixture.Random.CaptureCheckpoint().Streams);
    }

    [Fact]
    public void CaptureAndRestoreRejectAnOpenTimestamp()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        fixture.Agenda.EnterPhase(EventPhase.StateUpdate);
        Assert.False(fixture.Owner.CaptureCheckpoint().IsSuccess);
        Assert.False(CargoDispositionOwner.Restore(state, fixture.Lifecycle, fixture.Definitions, fixture.Policy,
            fixture.Random, fixture.Agenda, fixture.Facts).IsSuccess);
    }

    [Fact]
    public void FailedExpiryInvariantPoisonsOwnerAndPreventsCompletedBoundaryCapture()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        foreach (EntityId member in fixture.Owner.Fields.CaptureFields()[0].Members)
            fixture.Owner.Fields.Apply(fixture.Owner.Fields.PrepareRemoval(member)!);
        ScheduledEvent<GameEvent> due = fixture.Agenda.PopNextThrough(fixture.Owner.CaptureObjects()[0].ExpiresAt)!;
        Assert.Throws<InvalidOperationException>(() => fixture.Owner.Expire(due));
        Assert.False(fixture.Owner.IsHealthy);
        fixture.Agenda.EnterPhase(EventPhase.Decision);
        fixture.Agenda.CompleteTimestamp();
        Assert.False(fixture.Owner.CaptureCheckpoint().IsSuccess);
    }

    [Fact]
    public void WrongPolicyAndMalformedGenerationOutcomesRejectRestore()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.False(Restore(state with { PolicyKey = QualifiedContentKey.Create("test.cargo", "cargo-policy", "other") }, fixture).IsSuccess);
        CargoDispositionReceipt receipt = state.Receipts[0];
        CargoDispositionTypeOutcome outcome = receipt.Outcomes[0];
        Assert.False(Restore(state with { Receipts = [receipt with { Outcomes = [outcome with { Destroyed = new(99) }] }] }, fixture).IsSuccess);
        Assert.False(Restore(state with { Receipts = [receipt, receipt] }, fixture).IsSuccess);
        Assert.False(Restore(state with { Receipts = [receipt with { ObjectIds = [new(1), .. receipt.ObjectIds.Skip(1)] }] }, fixture).IsSuccess);
    }

    [Fact]
    public void ChangedMetadataPositionExpiryAndMissingLifecycleObjectRejectRestore()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        CargoLiveObject first = state.Objects[0];
        Assert.False(Restore(state with { Objects = [first with { ExpiresAt = new(1) }, .. state.Objects.Skip(1)] }, fixture).IsSuccess);
        Assert.False(Restore(state with { Objects = [first with { Generation = new(1) }, .. state.Objects.Skip(1)] }, fixture).IsSuccess);
        Assert.False(Restore(state with { Objects = state.Objects.Skip(1).ToArray() }, fixture).IsSuccess);
        Assert.False(Restore(state with { Objects = [first with { CreatedAt = new(1) }, .. state.Objects.Skip(1)] }, fixture).IsSuccess);
    }

    [Fact]
    public void MissingAndExtraExpiryAgendaEntriesRejectRestore()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        CargoLiveObject first = state.Objects[0];
        Assert.True(fixture.Agenda.TryCancelExact(first.ExpiryEvent, first.Generation, new GameEvent.CargoExpiry(first.EntityId)));
        Assert.False(Restore(state, fixture).IsSuccess);

        fixture = Create();
        CommitOre(fixture);
        state = fixture.Owner.CaptureCheckpoint().Value!;
        fixture.Agenda.Schedule(new(99), EventPhase.StateUpdate, default, new GameEvent.CargoExpiry(new(999)));
        Assert.False(Restore(state, fixture).IsSuccess);
    }

    [Fact]
    public void PartialFungibleRemainderRestoresWithoutRepeatingGenerationOrRounding()
    {
        Fixture fixture = Create();
        CommitOre(fixture, quantity: 10);
        CargoObjectSnapshot cargo = fixture.Lifecycle.CaptureCargoObjects().First(entry => fixture.Lifecycle.Inventories.Get(entry.InventoryId)!.FungibleStored(Ore.Key).Units > 1);
        Inventory destination = new(new(100), new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.Facility(new(1)), new(1)), new(100));
        fixture.Lifecycle.RegisterEconomyInventory(destination);
        Assert.True(fixture.Lifecycle.Inventories.TransferPhysical(new(cargo.InventoryId, destination.Id, Ore,
            new PhysicalTransferSubject.Fungible(Ore.Key, new(1)))).IsAccepted);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.True(Restore(state, fixture).IsSuccess);
        Assert.Equal(new Quantity(1), destination.FungibleStored(Ore.Key));
    }

    [Fact]
    public void FieldCirclesAndMembershipMustJoinTheirOriginalReceipts()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        CargoDebrisFieldSnapshot field = state.Fields.Fields[0];
        Assert.False(Restore(state with { Fields = state.Fields with { Fields = [field with
        {
            Circles = [field.Circles[0] with { Center = GameSessionTestFixture.Position(5, 0) }],
        }] } }, fixture).IsSuccess);
        Assert.False(Restore(state with { Fields = state.Fields with { Fields = [field with { Members = field.Members.Skip(1).ToArray() }] } }, fixture).IsSuccess);
    }

    [Fact]
    public void FunctionalEjectionRestoresOriginalDiscreteIdentityWithoutField()
    {
        Fixture fixture = Create();
        Assert.True(fixture.Source.StoreDiscrete(Sensor, new(9)).IsAccepted);
        CargoDispositionRequest request = Request(fixture, [new CargoDispositionSelection.Discrete(Sensor.Key, new(9))])
            with { Cause = CargoLossCause.FunctionalEjection };
        fixture.Owner.Commit(fixture.Owner.Prepare(request).Prepared!);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        var result = Restore(state, fixture);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Null(Assert.Single(result.Value!.CaptureObjects()).OriginalFieldId);
        Assert.Empty(result.Value.Fields.CaptureFields());
    }

    [Fact]
    public void ZeroSurvivorReceiptRoundtripsWithoutLiveObjectsOrFields()
    {
        Fixture fixture = Create();
        CommitOre(fixture, 1);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.True(Restore(state, fixture).IsSuccess);
        Assert.Empty(state.Objects);
        Assert.Single(state.Receipts);
    }

    [Fact]
    public void ScheduledCauseRetainsItsFullTimestampPhaseAndSequence()
    {
        Fixture fixture = Create();
        Assert.True(fixture.Source.StoreFungible(Ore, new(7)).IsAccepted);
        EventKey cause = fixture.Agenda.Schedule(SimulationTime.Zero, EventPhase.StateUpdate, default,
            new GameEvent.SensorActionReevaluation([]));
        fixture.Agenda.PopNextThrough(SimulationTime.Zero);
        CargoDispositionRequest request = Request(fixture, [new CargoDispositionSelection.Fungible(Ore.Key, new(7))]) with
        {
            Key = new(new CargoDispositionTrigger.Scheduled(cause), new(1), 0),
        };
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(request).Prepared!).WasApplied);
        fixture.Agenda.EnterPhase(EventPhase.Decision);
        fixture.Agenda.CompleteTimestamp();
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.True(Restore(state, fixture).IsSuccess);

        CargoDispositionKey changedKey = request.Key with { Trigger = new CargoDispositionTrigger.Scheduled(cause with { Phase = EventPhase.Decision }) };
        CargoDispositionReceipt changedReceipt = state.Receipts[0] with { Request = request with { Key = changedKey } };
        Assert.False(Restore(state with
        {
            Receipts = [changedReceipt],
            Objects = state.Objects.Select(cargo => cargo with { Origin = changedKey }).ToArray(),
        }, fixture).IsSuccess);
    }

    [Fact]
    public void ExpiredMergedFieldsRestoreTheirAliasesTombstonesAndReplayReceipts()
    {
        Fixture fixture = Create();
        CargoDispositionRequest first = CommitOre(fixture);
        Assert.True(fixture.Source.StoreFungible(Ore, new(7)).IsAccepted);
        CargoDispositionRequest second = first with { Key = first.Key with { Trigger = new CargoDispositionTrigger.Command(new(2)) } };
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(second).Prepared!).WasApplied);
        SimulationTime expiry = fixture.Owner.CaptureObjects()[0].ExpiresAt;
        while (fixture.Agenda.Count > 0)
            Assert.Equal(ScheduledEventDisposition.Applied, fixture.Owner.Expire(fixture.Agenda.PopNextThrough(expiry)!));
        fixture.Agenda.EnterPhase(EventPhase.Decision);
        fixture.Agenda.CompleteTimestamp();
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        var result = Restore(state, fixture);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Empty(result.Value!.CaptureObjects());
        Assert.Single(state.Fields.Aliases);
        Assert.Single(state.Fields.Tombstones);
        Assert.NotNull(result.Value.Prepare(first).Receipt);
        Assert.NotNull(result.Value.Prepare(second).Receipt);

        GameFactStoreCheckpoint facts = fixture.Facts.CaptureCheckpoint();
        GameFactEnvelope[] corrupted = facts.RetainedFacts.Select(envelope =>
        {
            GameFactEnvelope original = envelope!;
            return original.Fact is CargoObjectExpiredFact && original.Cause is ScheduledEventFactCause cause
                ? new GameFactEnvelope(original.Sequence, original.Timestamp,
                    new ScheduledEventFactCause(cause.Key with { Phase = EventPhase.Decision }), original.Fact)
                : original;
        }).ToArray();
        GameFactStore changedFacts = GameFactStore.RestoreCheckpoint(new(facts.Capacity, facts.Sequences, corrupted)).Value!;
        Assert.NotNull(changedFacts);
        Assert.False(Restore(state, fixture with { Facts = changedFacts }).IsSuccess);
    }

    [Fact]
    public void RestorationCopiesCallerOwnedContainers()
    {
        Fixture fixture = Create();
        CommitOre(fixture);
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        List<CargoLiveObject> objects = state.Objects.ToList();
        List<CargoDispositionReceipt> receipts = state.Receipts.ToList();
        CargoDispositionOwner restored = Restore(state with { Objects = objects, Receipts = receipts }, fixture).Value!;
        objects.Clear();
        receipts.Clear();
        Assert.Equal(state.Objects.Count, restored.CaptureObjects().Count);
        Assert.Single(restored.CaptureReceipts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreRejectsAliasThatResurrectsAFieldAfterEveryPossibleMemberExpired(bool laterFieldStillLive)
    {
        Fixture fixture = Create(lifetime: 1000);
        CargoDispositionRequest first = CommitOre(fixture, 2);
        DebrisFieldId firstField = fixture.Owner.CaptureObjects()[0].OriginalFieldId!.Value;
        ExpireThrough(fixture, new(1000));
        fixture.Agenda.AdvanceTo(new(2000));
        Assert.True(fixture.Source.StoreFungible(Ore, new(2)).IsAccepted);
        CargoDispositionRequest second = first with { Key = first.Key with { Trigger = new CargoDispositionTrigger.Command(new(2)) } };
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(second).Prepared!).WasApplied);
        DebrisFieldId secondField = fixture.Owner.CaptureObjects()[0].OriginalFieldId!.Value;
        if (laterFieldStillLive) fixture.Agenda.AdvanceTo(new(2500));
        else ExpireThrough(fixture, new(3000));
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.True(Restore(state, fixture).IsSuccess);
        CargoDebrisFieldSnapshot[] live = laterFieldStillLive
            ? [state.Fields.Fields.Single() with
                { Id = firstField, Circles = [new(firstField, first.SourcePosition, fixture.Policy.ScatterRadiusMeters), .. state.Fields.Fields.Single().Circles] }]
            : [];
        CargoDispositionOwnerCheckpoint forged = state with { Fields = state.Fields with
            { Fields = live, Aliases = [new(secondField, firstField)], Tombstones = laterFieldStillLive ? [] : [firstField] } };
        Assert.False(Restore(forged, fixture).IsSuccess);
    }

    [Theory]
    [InlineData(900UL)]
    [InlineData(1000UL)]
    public void RestoreAllowsAConnectedFieldToOutliveItsFirstOccurrencesOwnObjects(ulong secondAt)
    {
        Fixture fixture = Create(lifetime: 1000);
        CargoDispositionRequest first = CommitOre(fixture, 2);
        fixture.Agenda.AdvanceTo(new(secondAt));
        Assert.True(fixture.Source.StoreFungible(Ore, new(2)).IsAccepted);
        CargoDispositionRequest second = first with { Key = first.Key with { Trigger = new CargoDispositionTrigger.Command(new(2)) } };
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(second).Prepared!).WasApplied);
        ExpireThrough(fixture, new(1000));
        fixture.Agenda.AdvanceTo(new(1800));
        Assert.True(fixture.Source.StoreFungible(Ore, new(2)).IsAccepted);
        CargoDispositionRequest third = first with { Key = first.Key with { Trigger = new CargoDispositionTrigger.Command(new(3)) } };
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(third).Prepared!).WasApplied);
        ExpireThrough(fixture, new(2800));
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.Single(state.Fields.Tombstones);
        Assert.Equal(2, state.Fields.Aliases.Count);
        CheckpointResult<CargoDispositionOwner> restored = Restore(state, fixture);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
    }

    [Fact]
    public void RestoreRejectsALiveItemThatContradictsItsRetainedExpiryOutcome()
    {
        Fixture fixture = Create(lifetime: 1000);
        var item = new ItemInstanceId(10);
        Assert.True(fixture.Source.StoreDiscrete(Sensor, item).IsAccepted);
        CargoDispositionRequest request = Request(fixture, [new CargoDispositionSelection.Discrete(Sensor.Key, item)])
            with { Cause = CargoLossCause.FunctionalEjection };
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(request).Prepared!).WasApplied);
        ExpireThrough(fixture, new(1000));
        CargoDispositionOwnerCheckpoint state = fixture.Owner.CaptureCheckpoint().Value!;
        Assert.True(Restore(state, fixture).IsSuccess);
        Assert.True(fixture.Source.StoreDiscrete(Sensor, item).IsAccepted);
        Assert.False(Restore(state, fixture).IsSuccess);
    }

    private static void ExpireThrough(Fixture fixture, SimulationTime through)
    {
        while (fixture.Agenda.NextEventKey is { } next && next.Timestamp <= through)
            Assert.Equal(ScheduledEventDisposition.Applied, fixture.Owner.Expire(fixture.Agenda.PopNextThrough(through)!));
        fixture.Agenda.EnterPhase(EventPhase.Decision);
        fixture.Agenda.CompleteTimestamp();
        fixture.Agenda.AdvanceTo(through);
    }

    private static CargoDispositionRequest CommitOre(Fixture fixture, ulong quantity = 7)
    {
        Assert.True(fixture.Source.StoreFungible(Ore, new(quantity)).IsAccepted);
        CargoDispositionRequest request = Request(fixture, [new CargoDispositionSelection.Fungible(Ore.Key, new(quantity))]);
        Assert.True(fixture.Owner.Commit(fixture.Owner.Prepare(request).Prepared!).WasApplied);
        return request;
    }

    private static CargoDispositionRequest Request(Fixture fixture, IReadOnlyList<CargoDispositionSelection> selected) =>
        new(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), GameSessionTestFixture.Position(0, 0),
            CargoLossCause.StorageModuleLoss, fixture.Policy, selected, []);

    private static CheckpointResult<CargoDispositionOwner> Restore(CargoDispositionOwnerCheckpoint state, Fixture fixture)
    {
        EntityLifecycleOwner lifecycle = EntityLifecycleOwner.RestoreCheckpoint(fixture.Lifecycle.CaptureCheckpoint(),
            new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), [], fixture.Definitions).Value!;
        EventAgenda<GameEvent> agenda = EventAgenda<GameEvent>.RestoreCheckpoint(fixture.Agenda.CaptureCheckpoint().Value!).Value!;
        return CargoDispositionOwner.Restore(state, lifecycle, fixture.Definitions, fixture.Policy,
            fixture.Random, agenda, fixture.Facts);
    }

    private static Fixture Create(ulong lifetime = 63_072_000_000)
    {
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        lifecycle.RegisterSetup([new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal, GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        var definitions = new PhysicalDefinitionCatalog([Ore, Sensor]);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 10000, new(lifetime), new(1000), new(15000), new(100));
        var random = new DeterministicRandomOwner(GameSessionTestFixture.RootSeed);
        var agenda = new EventAgenda<GameEvent>();
        var facts = new GameFactStore(100);
        return new(new(lifecycle, definitions, policy, random, agenda, facts), lifecycle,
            lifecycle.Inventories.Get(new(1))!, definitions, policy, random, agenda, facts);
    }

    private sealed record Fixture(CargoDispositionOwner Owner, EntityLifecycleOwner Lifecycle, Inventory Source,
        PhysicalDefinitionCatalog Definitions, CargoDispositionPolicy Policy, DeterministicRandomOwner Random,
        EventAgenda<GameEvent> Agenda, GameFactStore Facts);
}
