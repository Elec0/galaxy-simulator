using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoAdmissionHistoryCheckpointTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "history"), PhysicalHoldingKind.Discrete, new(1));
    private static readonly PhysicalDefinitionCatalog Definitions = new([Item]);

    [Fact]
    public void AllocatedHolesCannotInventHistoricalSourcesAfterBoundedFactsAreEvicted()
    {
        using GameSession original = Create(itemCount: 1, sparse: true);
        original.AdvanceTo(new(20000));
        GameSessionCheckpoint saved = Capture(original);
        Assert.Empty(saved.Cargo!.Objects);
        Assert.Single(saved.Facts.RetainedFacts);
        Assert.IsType<CargoObjectDisappearedFact>(saved.Facts.RetainedFacts.Single()!.Fact);
        CheckpointResult<GameSession> unchanged = GameSession.RestoreCheckpoint(saved, Definitions);
        Assert.True(unchanged.IsSuccess, unchanged.Failure?.Message);
        using GameSession valid = unchanged.Value!;

        CargoDispositionReceipt receipt = saved.Cargo.Receipts.Single();
        var forged = ChangeReceipt(saved, receipt with { Request = receipt.Request with
        {
            Key = receipt.Request.Key with { SourceEntityId = new(99) },
            SourceInventoryId = new(99),
        } });
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(forged, Definitions);
        using GameSession? unexpectedlyRestored = result.Value;
        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.cargo", result.Failure!.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiptSourceInventoryMustMatchLiveOrRemovedSourceEntity(bool removeSource)
    {
        using GameSession original = Create(removeSource);
        GameSessionCheckpoint saved = Capture(original);
        CargoDispositionReceipt receipt = saved.Cargo!.Receipts.Single();
        GameSessionCheckpoint forged = ChangeReceipt(saved, receipt with { Request = receipt.Request with { SourceInventoryId = new(2) } });
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(forged, Definitions);
        using GameSession? unexpectedlyRestored = result.Value;
        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.cargo", result.Failure!.Path);
    }

    [Fact]
    public void DestroyedDiscreteIdentityCannotReappearInAnyLiveInventory()
    {
        using GameSession original = Create();
        GameSessionCheckpoint saved = Capture(original);
        CargoDispositionReceipt receipt = saved.Cargo!.Receipts.Single();
        ItemInstanceId destroyed = receipt.Request.Selected.OfType<CargoDispositionSelection.Discrete>()
            .Select(selection => selection.InstanceId).Except(receipt.Outcomes.Single().SurvivingInstances).Single();
        InventoryCheckpoint destination = saved.Lifecycle.Inventories.Inventories.Single(inventory => inventory.Id == new InventoryId(2));
        var forgedInventory = new InventoryCheckpoint(destination.Id, destination.Custody, destination.Capacity,
            destination.StoredMaterials, destination.Reservations, destination.CapacityReservations, destination.FungibleHoldings,
            [new(destroyed, Item.Key)], destination.PhysicalReservations,
            destination.EnabledIntakeCapacity, destination.ProtectedIntakeCapacity,
            destination.NextIncomingAdmissionSequence, destination.IncomingAdmissions,
            destination.NextOutgoingAdmissionSequence, destination.OutgoingAdmissions);
        EntityLifecycleCheckpoint life = saved.Lifecycle;
        var forged = saved with { Lifecycle = new(life.EntityIds, life.ShipIds, life.InventoryIds,
            new(life.Inventories.Inventories.Select(inventory => inventory.Id == destination.Id ? forgedInventory : inventory)),
            life.LiveShips, life.MaterializationReceipts, life.RemovalReceipts, life.SensorDeployables, life.CargoObjects) };
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(forged, Definitions);
        using GameSession? unexpectedlyRestored = result.Value;
        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.cargo", result.Failure!.Path);
    }

    [Fact]
    public void ZeroSurvivorReceiptStillRequiresKnownSourceSystem()
    {
        using GameSession original = Create(itemCount: 1);
        GameSessionCheckpoint saved = Capture(original);
        CargoDispositionReceipt receipt = saved.Cargo!.Receipts.Single();
        Assert.Empty(receipt.ObjectIds);
        var forged = ChangeReceipt(saved, receipt with { Request = receipt.Request with
            { SourcePosition = new(new(999), receipt.Request.SourcePosition.Position) } });
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(forged, Definitions);
        using GameSession? unexpectedlyRestored = result.Value;
        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.cargo", result.Failure!.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedLiveOrRemovedSourceCheckpointContinuesWithExactExpiry(bool removeSource)
    {
        using GameSession original = Create(removeSource);
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(Capture(original), Definitions);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        original.AdvanceTo(new(20000));
        restored.AdvanceTo(new(20000));
        Assert.Equal(Capture(original).Engine.Agenda.PendingEvents, Capture(restored).Engine.Agenda.PendingEvents);
        Assert.Equal(original.CaptureCargoAuthority(), restored.CaptureCargoAuthority());
        Assert.Equal(original.ReadFactsAfter(null, 256).Facts, restored.ReadFactsAfter(null, 256).Facts);
    }

    private static GameSessionCheckpoint ChangeReceipt(GameSessionCheckpoint saved, CargoDispositionReceipt receipt) =>
        saved with { Cargo = saved.Cargo! with { Receipts = [receipt] } };
    private static GameSessionCheckpoint Capture(GameSession session) => Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);

    private static GameSession Create(bool removeSource = false, int itemCount = 2, bool sparse = false)
    {
        ShipDesign Design(ulong id) => new(new(id), "History fixture", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, 10000, 5000);
        var ships = new InitialShipSetup[]
        {
            new(new(sparse ? 100UL : 1UL), new(1), new(sparse ? 100UL : 1UL), new(1), Design(1), GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController,
                cargo: Enumerable.Repeat(Item, itemCount)),
            new(new(2), new(2), new(2), new(1), Design(2), GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
        };
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "history"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 0, new(20000), new(1000), new(15000), new(100));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")], ships, new ConnectorTopology([], []), [], null,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, sparse ? 1 : 256, GameSessionTestFixture.Player, cargoPolicy: policy);
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        var loss = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(sparse ? 100UL : 1UL), 0), new(sparse ? 100UL : 1UL),
            GameSessionTestFixture.Position(0, 0), sparse ? CargoLossCause.FunctionalEjection : CargoLossCause.StorageModuleLoss, policy,
            Enumerable.Range(1, itemCount).Select(index => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Item.Key, new((ulong)index))).ToArray(), []);
        Assert.True(session.CommitCargoDisposition(loss).WasApplied);
        session.AdvanceTo(new(0));
        if (removeSource) Assert.IsType<EntityRemovalResult.Removed>(session.RemoveEntity(new(new(1), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo)));
        return session;
    }
}
