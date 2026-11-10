using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectedFactProjectionTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "item"),
        PhysicalHoldingKind.Discrete, new(1));

    [Theory]
    [InlineData(1UL, true)]
    [InlineData(2UL, false)]
    [InlineData(null, false)]
    public void OnlyOwnedCollectionResultsProjectAndPrivateTargetDetailsNeverParticipate(ulong? principal, bool visible)
    {
        var fact = new CargoCollectedFact(new(2), new(200), new(3), new(4), new(5), new(999),
            new(Item.Key, new(1), new ItemInstanceId(17), true));
        bool handled = PlayerFactProjector.TryProjectWithoutCoverage(fact, new(1), GameSessionTestFixture.Player,
            principal is { } value ? new(value) : null, out GameFact? result);
        Assert.True(handled);
        Assert.Equal(visible, result is not null);
        if (!visible) return;
        PlayerCargoCollectedFact notice = Assert.IsType<PlayerCargoCollectedFact>(result);
        Assert.Equal(new ShipId(2), notice.ShipId);
        Assert.Equal(new ShipOrderId(3), notice.OrderId);
        Assert.Equal(Item.Key, notice.DefinitionKey);
        Assert.Equal(new Quantity(1), notice.Quantity);
        Assert.Equal(new ItemInstanceId(17), notice.InstanceId);
        var changedPrivate = fact with { CollectorEntityId = new(300), TargetId = new(888), AttemptGeneration = new(99),
            Outcome = fact.Outcome with { Retired = false } };
        Assert.True(PlayerFactProjector.TryProjectWithoutCoverage(changedPrivate, new(1), GameSessionTestFixture.Player, new(1), out GameFact? changed));
        Assert.Equal(notice, changed);
        Assert.Equal(new ShipId(2), GamePresentationSnapshotFactory.GetShipId(fact));
        Assert.Equal(new ShipId(2), GamePresentationSnapshotFactory.GetShipId(notice));
    }

    [Fact]
    public void LiveHiddenPickupDisclosesOnlyOwnResultAndRetainsItThroughRestore()
    {
        using GameSession session = Create();
        EntityId target = Assert.Single(session.CaptureVisibleCargo()).EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll));
        session.AdvanceTo(SimulationTime.Zero);
        Assert.Equal(ShipOrderReason.CollectingCargo, session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(2)).CurrentOrder!.Reason);
        session.RemoveEntity(new(new(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Empty(session.CaptureVisibleCargo());
        CargoCollectionProgressView hiddenProgress = Assert.Single(session.CaptureCargoCollectionProgress());
        Assert.Equal(target, hiddenProgress.Target.EntityId);
        Assert.Equal(new ShipId(2), hiddenProgress.ShipId);
        Assert.Empty(session.CaptureVisibleCargo());
        session.AdvanceTo(new(1000));
        GamePresentationSnapshot presentation = Present(session);
        PlayerCargoCollectedFact notice = Assert.IsType<PlayerCargoCollectedFact>(Assert.Single(presentation.Facts.Facts,
            fact => fact.Fact is PlayerCargoCollectedFact).Fact);
        Assert.Equal(new ShipId(2), notice.ShipId);
        Assert.Equal(Item.Key, notice.DefinitionKey);
        Assert.Equal(new Quantity(1), notice.Quantity);
        Assert.Equal(new ItemInstanceId(1), notice.InstanceId);
        Assert.DoesNotContain(presentation.Facts.Facts, envelope => envelope.Fact is CargoCollectedFact);
        Assert.Contains(presentation.SelectedShipFacts, envelope => envelope.Fact == notice);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([Item]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Contains(Present(restored).Facts.Facts, envelope => envelope.Fact == notice);
        Assert.Empty(restored.CaptureVisibleCargo());
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("owner")]
    [InlineData("raw")]
    public void RestoreRejectsChangedOwnedCollectionPayloadOrRawTargetAuthority(string changed)
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(1000));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        GameFactEnvelope collected = Assert.Single(checkpoint.Facts.RetainedFacts,
            envelope => envelope!.Fact is CargoCollectedFact)!;
        PlayerFactDisclosure disclosure = collected.PlayerDisclosure!;
        PlayerCargoCollectedFact notice = Assert.IsType<PlayerCargoCollectedFact>(disclosure.Fact);
        PlayerFactDisclosure invalid = changed switch
        {
            "quantity" => disclosure with { Fact = notice with { Quantity = new(2) } },
            "owner" => disclosure with { SubjectPrincipalId = new(2) },
            _ => disclosure with { Fact = collected.Fact },
        };
        GameSessionCheckpoint altered = checkpoint with
        {
            Facts = new(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences,
                checkpoint.Facts.RetainedFacts.Select(envelope => envelope == collected ? collected with { PlayerDisclosure = invalid } : envelope)),
        };
        Assert.False(GameSession.RestoreCheckpoint(altered, new PhysicalDefinitionCatalog([Item])).IsSuccess);
    }

    private static GamePresentationSnapshot Present(GameSession session) => session.CapturePresentation(
        new GamePresentationRequest(new PrincipalId(1), [new ShipId(2)], new ShipId(2), null, 256));

    private static GameSession Create()
    {
        ShipDesign Design(ulong id, ulong radius) => new(new(id), "Fixture", new ConstructionRecipe([], new Work(1)),
            new(100), GameSessionTestFixture.ManeuverCapability, radius, 5000);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "facts"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
                new(CargoLossCause.FunctionalEjection, new(1, 1))], 4, 0, new(20000), new(1000), new(15000), new(100));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), new(1), Design(1, 10), GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, cargo: [Item]),
             new InitialShipSetup(new(2), new(2), new(2), new(1), Design(2, 0), GameSessionTestFixture.Position(100, 0),
                GameSessionTestFixture.PlayerController)], new ConnectorTopology([], []), [], null,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player, cargoPolicy: policy);
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        CargoDispositionRequest request = new(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), CargoLossCause.FunctionalEjection, policy,
            [new CargoDispositionSelection.Discrete(Item.Key, new(1))], []);
        Assert.True(session.CommitCargoDisposition(request).WasApplied);
        return session;
    }
}
