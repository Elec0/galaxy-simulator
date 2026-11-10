using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDispositionPreparedPlanTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(1));

    [Theory]
    [InlineData("missing-share")]
    [InlineData("edited-share")]
    [InlineData("position")]
    [InlineData("outcome")]
    [InlineData("expiry")]
    [InlineData("missing-field")]
    [InlineData("field-circle")]
    [InlineData("field-members")]
    [InlineData("field-participants")]
    [InlineData("cause")]
    [InlineData("selection")]
    [InlineData("cleanup")]
    [InlineData("source-loss")]
    [InlineData("source-cleanup")]
    public void CorruptPlanRejectsWithoutMutationAndOriginalPlanStillCommits(string corruption)
    {
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        var design = new ShipDesign(new(1), "Source", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 10000, pickupRangeUnits: 5000);
        lifecycle.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 10000, new(1000), new(1000), new(15000), new(100));
        var agenda = new EventAgenda<GameEvent>();
        var facts = new GameFactStore(100);
        var definitions = new PhysicalDefinitionCatalog([Ore]);
        var owner = new CargoDispositionOwner(lifecycle, definitions, policy,
            new DeterministicRandomOwner(GameSessionTestFixture.RootSeed), agenda, facts);
        Inventory source = lifecycle.Inventories.Get(new(1))!;
        Assert.True(source.StoreFungible(Ore, new(20)).IsAccepted);
        var reservationOwner = new ReservationOwner.TransportJob(new(1));
        Assert.True(source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(20)), reservationOwner).IsAccepted);
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), CargoLossCause.StorageModuleLoss, policy,
            [new CargoDispositionSelection.Fungible(Ore.Key, new(20))],
            [new CargoSourceReservationCleanup.Physical(new(1), reservationOwner)]);
        PreparedCargoDisposition plan = owner.Prepare(request).Prepared!;
        Assert.NotNull(plan);
        PreparedCargoObject first = plan.Objects.Objects[0];
        PreparedCargoDisposition changed = corruption switch
        {
            "missing-share" => plan with { Objects = plan.Objects with { Objects = plan.Objects.Objects.Take(3).ToArray() } },
            "edited-share" => plan with { Objects = plan.Objects with { Objects = plan.Objects.Objects.Select((entry, index) => index == 0
                ? entry with { Inventory = Inventory.CreateCargoObjectInventory(entry.Object.InventoryId, entry.Object.EntityId,
                    Ore, new PhysicalTransferSubject.Fungible(Ore.Key, first.Inventory.FungibleStored(Ore.Key).Add(new(1)))) } : entry).ToArray() } },
            "position" => plan with { Objects = plan.Objects with { Objects = plan.Objects.Objects.Select((entry, index) => index == 0
                ? entry with { Object = entry.Object with { Position = GameSessionTestFixture.Position(20_001, 0) } } : entry).ToArray() } },
            "outcome" => plan with { Outcomes = [plan.Outcomes[0] with { Recoverable = new(9), Destroyed = new(11) }] },
            "expiry" => plan with { ExpiresAt = new(1001) },
            "missing-field" => plan with { Field = null },
            "field-circle" => plan with { Field = plan.Field! with { Circle = plan.Field!.Circle with { RadiusMeters = 1 } } },
            "field-members" => plan with { Field = plan.Field! with { Members = [first.Object.EntityId] } },
            "field-participants" => plan with { Field = plan.Field! with { ParticipatingFields = [new(999)] } },
            "cause" => plan with { Request = plan.Request with { Cause = CargoLossCause.FunctionalEjection } },
            "selection" => plan with { Request = plan.Request with { Selected = [new CargoDispositionSelection.Fungible(Ore.Key, new(19))] } },
            "cleanup" => plan with { Request = plan.Request with { Cleanup = [] } },
            "source-loss" => plan with { SourceLoss = CargoSourceLoss.Prepare(source,
                [new PhysicalTransferSubject.Fungible(Ore.Key, new(19))], definitions, request.Cleanup).Prepared! },
            "source-cleanup" => plan with { SourceLoss = new PreparedCargoSourceLoss(source, source.CaptureCheckpoint(),
                plan.SourceLoss.Groups, plan.SourceLoss.RemovedCapacity, [], []) },
            _ => throw new InvalidOperationException("Unknown corruption."),
        };

        Assert.False(owner.CanCommit(changed));
        Assert.Equal(CargoDispositionFailure.StalePreparation, owner.Commit(changed).Failure);
        Assert.Equal(new Quantity(20), source.FungibleStored(Ore.Key));
        Assert.NotNull(source.GetPhysicalReservation(new(1)));
        Assert.Empty(lifecycle.CaptureCargoObjects());
        Assert.Empty(owner.CaptureObjects());
        Assert.Empty(owner.CaptureReceipts());
        Assert.Empty(owner.Fields.CaptureFields());
        Assert.Equal(0, agenda.Count);
        Assert.Empty(facts.ReadAfter(null, 100).Facts);
        Assert.True(owner.IsHealthy);
        Assert.True(owner.Commit(plan).WasApplied);
        Assert.Equal(Quantity.Zero, source.FungibleStored(Ore.Key));
        Assert.Equal(10UL, owner.CaptureObjects().Aggregate(0UL, (sum, cargo) =>
            sum + lifecycle.Inventories.Get(lifecycle.GetCargoObject(cargo.EntityId)!.InventoryId)!.FungibleStored(Ore.Key).Units));
    }

    [Fact]
    public void RefreshedAgendaSequenceCannotBypassExpiryCapacityPreflight()
    {
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        var design = new ShipDesign(new(1), "Source", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 10000, pickupRangeUnits: 5000);
        lifecycle.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 10000, new(1000), new(1000), new(15000), new(100));
        EventAgenda<GameEvent> agenda = EventAgenda<GameEvent>.RestoreCheckpoint(
            new EventAgendaCheckpoint<GameEvent>(SimulationTime.Zero, ulong.MaxValue - 4, [])).Value!;
        var facts = new GameFactStore(100);
        var owner = new CargoDispositionOwner(lifecycle, new([Ore]), policy,
            new DeterministicRandomOwner(GameSessionTestFixture.RootSeed), agenda, facts);
        Inventory source = lifecycle.Inventories.Get(new(1))!;
        Assert.True(source.StoreFungible(Ore, new(20)).IsAccepted);
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), CargoLossCause.StorageModuleLoss, policy,
            [new CargoDispositionSelection.Fungible(Ore.Key, new(20))], []);
        PreparedCargoDisposition original = owner.Prepare(request).Prepared!;
        Assert.Equal(4, original.Objects.Objects.Count);
        EventKey unrelated = agenda.Schedule(new(2000), EventPhase.Decision, default, new GameEvent.SensorActionReevaluation([]));
        PreparedCargoDisposition refreshed = original with { AgendaSequence = agenda.NextCreationSequence };

        Assert.False(owner.CanCommit(refreshed));
        Assert.Equal(CargoDispositionFailure.StalePreparation, owner.Commit(refreshed).Failure);
        Assert.False(owner.CanCommit(original));
        Assert.Equal(new Quantity(20), source.FungibleStored(Ore.Key));
        Assert.Empty(lifecycle.CaptureCargoObjects());
        Assert.Empty(owner.CaptureObjects());
        Assert.Empty(owner.CaptureReceipts());
        Assert.Empty(owner.Fields.CaptureFields());
        Assert.Equal(1, agenda.Count);
        Assert.Equal(ulong.MaxValue - 3, agenda.NextCreationSequence);
        Assert.Equal(unrelated, Assert.Single(agenda.CaptureCheckpoint().Value!.PendingEvents).Key);
        Assert.Empty(facts.ReadAfter(null, 100).Facts);
        Assert.True(owner.IsHealthy);
    }

}
