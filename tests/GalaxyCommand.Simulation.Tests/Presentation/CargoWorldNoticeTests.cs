using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoWorldNoticeTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "item"), PhysicalHoldingKind.Discrete, new(1));
    private static readonly CommandSource NpcSource = new(CommandSourceKind.Autonomous, new("npc-source"));

    [Fact]
    public void CoveredPublicationProducesOneApprovedAppearancePerVisibleObject()
    {
        using GameSession session = Create();
        CargoDispositionReceipt receipt = Dispose(session);
        CargoObjectView[] visible = Present(session).World.CargoObjects.ToArray();
        Assert.Equal(2, visible.Length);
        ObservedCargoAppearanceFact[] notices = Present(session).Facts.Facts.Select(envelope => envelope.Fact)
            .OfType<ObservedCargoAppearanceFact>().ToArray();
        Assert.Equal(visible, notices.Select(notice => notice.Cargo));
        Assert.All(notices, notice => Assert.Equal(receipt.FieldHandle, notice.Cargo.OriginalFieldId));
        Assert.DoesNotContain(Present(session).Facts.Facts, envelope => envelope.Fact is CargoObjectAppearedFact or CargoDispositionCommittedFact);
    }

    [Fact]
    public void AppearanceBatchSequenceExhaustionRejectsBeforeAnySourceOrPublicationMutation()
    {
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        lifecycle.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        Inventory source = lifecycle.GetRequiredCargo(new(1));
        for (ulong id = 1; id <= 5; id++) Assert.True(source.StoreDiscrete(Item, new(id)).IsAccepted);
        var existing = new GameFactEnvelope(new(ulong.MaxValue - 1), SimulationTime.Zero, new CommandFactCause(new(1)),
            new CommandAcceptedFact(new(1), GameSessionTestFixture.Player, "fixture.command"));
        GameFactStore facts = GameFactStore.RestoreCheckpoint(new(8, new(ulong.MaxValue), [existing])).Value!;
        Assert.True(facts.CanCommit(1));
        var agenda = new EventAgenda<GameEvent>();
        var owner = new CargoDispositionOwner(lifecycle, new([Item]), Policy(), new(GameSessionTestFixture.RootSeed), agenda, facts);
        IdSequenceCheckpoint entityIds = lifecycle.CaptureCheckpoint().EntityIds;
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), CargoLossCause.StorageModuleLoss, Policy(),
            Enumerable.Range(1, 5).Select(id => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Item.Key, new((ulong)id))).ToArray(), []);
        Assert.Equal(CargoDispositionFailure.FactCapacityExhausted, owner.Prepare(request).Failure);
        Assert.Equal(5, source.CaptureCheckpoint().DiscreteItems.Count);
        Assert.Empty(lifecycle.CaptureCargoObjects());
        Assert.Empty(owner.CaptureReceipts());
        Assert.Empty(owner.Fields.CaptureFields());
        Assert.Equal(entityIds, lifecycle.CaptureCheckpoint().EntityIds);
        Assert.Equal(0, agenda.Count);
        Assert.True(owner.IsHealthy);
    }

    [Fact]
    public void HiddenPublicationDoesNotAcquireHistoricalNoticeWhenCoverageArrives()
    {
        using GameSession session = Create(sourceX: 100);
        Dispose(session, sourceX: 100);
        Assert.Empty(Present(session).World.CargoObjects);
        Assert.DoesNotContain(Present(session).Facts.Facts, envelope => envelope.Fact is ObservedCargoAppearanceFact);
        MovePlayer(session, 100);
        Assert.Equal(2, Present(session).World.CargoObjects.Count);
        Assert.DoesNotContain(Present(session).Facts.Facts, envelope => envelope.Fact is ObservedCargoAppearanceFact);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([Item]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Equal(2, Present(restored).World.CargoObjects.Count);
        Assert.DoesNotContain(Present(restored).Facts.Facts, envelope => envelope.Fact is ObservedCargoAppearanceFact);
    }

    [Fact]
    public void VisibleAppearanceRemainsFrozenAfterCoverageLossAndRestore()
    {
        using GameSession session = Create();
        Dispose(session);
        GameFact[] appeared = Present(session).Facts.Facts.Select(envelope => envelope.Fact).OfType<ObservedCargoAppearanceFact>().ToArray();
        Assert.Equal(2, appeared.Length);
        MovePlayer(session, 100);
        Assert.Empty(Present(session).World.CargoObjects);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var result = GameSession.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([Item]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Empty(Present(restored).World.CargoObjects);
        Assert.Equal(appeared, Present(restored).Facts.Facts.Select(envelope => envelope.Fact).OfType<ObservedCargoAppearanceFact>());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(100, 0)]
    public void ExpiryDisappearanceIsCoverageGatedAndCarriesNoPrivateCause(long sourceX, int expected)
    {
        using GameSession session = Create(sourceX, lifetime: 2000);
        CargoDispositionReceipt receipt = Dispose(session, sourceX, lifetime: 2000);
        session.AdvanceTo(new(2000));
        Assert.Empty(Present(session).World.CargoObjects);
        ObservedCargoDisappearanceFact[] notices = Present(session).Facts.Facts.Select(envelope => envelope.Fact)
            .OfType<ObservedCargoDisappearanceFact>().ToArray();
        Assert.Equal(expected, notices.Length);
        Assert.All(notices, notice =>
        {
            Assert.Contains(notice.EntityId, receipt.ObjectIds);
            Assert.Equal(GameSessionTestFixture.Position(sourceX, 0), notice.Position);
        });
        Assert.DoesNotContain(Present(session).Facts.Facts, envelope => envelope.Fact is CargoObjectExpiredFact or CargoObjectDisappearedFact);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var result = GameSession.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([Item]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Equal(notices, Present(restored).Facts.Facts.Select(envelope => envelope.Fact).OfType<ObservedCargoDisappearanceFact>());
    }

    [Fact]
    public void CoveredCollectionByAnotherPrincipalProducesOnlyBoundedWorldDisappearance()
    {
        using GameSession session = Create();
        EntityId target = Dispose(session).ObjectIds[0];
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(NpcSource,
            new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(1000));
        ObservedCargoDisappearanceFact notice = Assert.IsType<ObservedCargoDisappearanceFact>(Assert.Single(Present(session).Facts.Facts,
            envelope => envelope.Fact is ObservedCargoDisappearanceFact).Fact);
        Assert.Equal(target, notice.EntityId);
        Assert.Equal(GameSessionTestFixture.Position(0, 0), notice.Position);
        Assert.DoesNotContain(Present(session).Facts.Facts, envelope => envelope.Fact is PlayerCargoCollectedFact or CargoCollectedFact);
    }

    [Fact]
    public void OwnedHiddenPickupResultDoesNotGrantAHiddenWorldDisappearance()
    {
        using GameSession session = CreateHiddenPickup();
        EntityId target = Present(session).World.CargoObjects[0].EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll));
        session.AdvanceTo(SimulationTime.Zero);
        session.RemoveEntity(new(new(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Empty(Present(session).World.CargoObjects);
        session.AdvanceTo(new(1000));
        Assert.Contains(Present(session).Facts.Facts, envelope => envelope.Fact is PlayerCargoCollectedFact);
        Assert.DoesNotContain(Present(session).Facts.Facts, envelope => envelope.Fact is ObservedCargoDisappearanceFact);
    }

    [Theory]
    [InlineData("appearance-position")]
    [InlineData("appearance-principal")]
    [InlineData("appearance-raw")]
    [InlineData("disappearance-position")]
    public void RestoreRejectsNoticePayloadsThatDisagreeWithTheirCommitFacts(string change)
    {
        using GameSession session = Create(lifetime: 2000);
        Dispose(session, lifetime: 2000);
        session.AdvanceTo(new(2000));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        bool appearance = change.StartsWith("appearance", StringComparison.Ordinal);
        GameFactEnvelope envelope = checkpoint.Facts.RetainedFacts.First(envelope => appearance
            ? envelope!.Fact is CargoObjectAppearedFact : envelope!.Fact is CargoObjectDisappearedFact)!;
        PlayerFactDisclosure saved = envelope.PlayerDisclosure!;
        PlayerFactDisclosure changed;
        if (saved.Fact is ObservedCargoAppearanceFact notice)
        {
            CargoObjectView view = notice.Cargo;
            var shifted = new CargoObjectView(view.EntityId, GameSessionTestFixture.Position(1, 0), view.DefinitionKey,
                view.HoldingKind, view.Quantity, view.UnitCapacityCost, view.OccupiedStorage, view.DiscreteItem, view.OriginalFieldId);
            changed = change switch
            {
                "appearance-position" => saved with { Fact = notice with { Cargo = shifted } },
                "appearance-principal" => saved with { SubjectPrincipalId = new(1) },
                _ => saved with { Fact = envelope.Fact },
            };
        }
        else changed = saved with { Fact = ((ObservedCargoDisappearanceFact)saved.Fact!) with { Position = GameSessionTestFixture.Position(1, 0) } };
        GameSessionCheckpoint altered = checkpoint with
        {
            Facts = new(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences,
                checkpoint.Facts.RetainedFacts.Select(item => item == envelope ? envelope with { PlayerDisclosure = changed } : item)),
        };
        Assert.False(GameSession.RestoreCheckpoint(altered, new PhysicalDefinitionCatalog([Item])).IsSuccess);
    }

    private static CargoDispositionReceipt Dispose(GameSession session, long sourceX = 0, ulong lifetime = 100000)
    {
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(2), 0), new(2),
            GameSessionTestFixture.Position(sourceX, 0), CargoLossCause.StorageModuleLoss, Policy(lifetime),
            Enumerable.Range(1, 5).Select(id => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Item.Key, new((ulong)id))).ToArray(), []);
        CargoDispositionCommitResult result = session.CommitCargoDisposition(request);
        Assert.True(result.WasApplied, result.Failure?.ToString());
        return result.Receipt!;
    }

    private static void MovePlayer(GameSession session, long x)
    {
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll));
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
    }

    private static GamePresentationSnapshot Present(GameSession session) => session.CapturePresentation(new(new PrincipalId(1), [], null, null, 512));

    private static GameSession Create(long sourceX = 0, ulong lifetime = 100000)
    {
        var design = new ShipDesign(new(1), "Fixture", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, 10, 5000);
        var relationships = new RelationshipSetup(GameSessionTestFixture.Relationships.Principals.Append(
            new PrincipalDefinition(new(2), new PrincipalContentId("npc"), "NPC")), new(1), GameSessionTestFixture.StandingPolicy, []);
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), new(1), design, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new(2), new(2), new(2), new(2), design, GameSessionTestFixture.Position(sourceX, 0),
                 new ActorController(ActorControllerKind.Autonomous, NpcSource.Id), cargo: [Item, Item, Item, Item, Item])],
            new ConnectorTopology([], []), [], null, relationships, GameSessionTestFixture.RootSeed, 512,
            GameSessionTestFixture.Player, cargoPolicy: Policy(lifetime));
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        return session;
    }

    private static GameSession CreateHiddenPickup()
    {
        ShipDesign Design(ulong id, ulong radius) => new(new(id), "Fixture", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, radius, 5000);
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), new(1), Design(1, 10), GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, cargo: [Item]),
             new InitialShipSetup(new(2), new(2), new(2), new(1), Design(2, 0), GameSessionTestFixture.Position(100, 0),
                GameSessionTestFixture.PlayerController)], new ConnectorTopology([], []), [], null,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 512, GameSessionTestFixture.Player, cargoPolicy: Policy());
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), CargoLossCause.FunctionalEjection, Policy(), [new CargoDispositionSelection.Discrete(Item.Key, new(1))], []);
        Assert.True(session.CommitCargoDisposition(request).WasApplied);
        return session;
    }

    private static CargoDispositionPolicy Policy(ulong lifetime = 100000) => new(QualifiedContentKey.Create("test.cargo", "cargo-policy", "notice"),
        1, 1, 1, 1, [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
            new(CargoLossCause.FunctionalEjection, new(1, 1))], 4, 0, new(lifetime), new(1000), new(15000), new(100));
}
