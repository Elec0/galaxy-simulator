using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoHiddenMergeInspectorTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "hidden-bridge"),
        PhysicalHoldingKind.Discrete, new(1));

    [Fact]
    public void HiddenBridgeMergesPrivateFieldsWithoutRegroupingCoveredInspectors()
    {
        const ulong radius = 10000;
        SystemPosition left = GameSessionTestFixture.Position(-20000, 0);
        SystemPosition right = GameSessionTestFixture.Position(20000, 0);
        SystemPosition center = GameSessionTestFixture.Position(0, 0);
        SystemPosition GeneratedPosition(ulong source, ulong command, ulong firstItem, SystemPosition position)
        {
            var generator = new CargoDispositionGeneration(new DeterministicRandomOwner(GameSessionTestFixture.RootSeed),
                new EntityId(source), new CommandSequence(command), 0, 1, Item.Key);
            ItemInstanceId survivor = Assert.Single(generator.SelectSurvivors([new(firstItem), new(firstItem + 1)], 1));
            return generator.Scatter(position, (long)radius, survivor.Value);
        }
        SystemPosition leftObject = GeneratedPosition(1, 1, 1, left);
        SystemPosition rightObject = GeneratedPosition(2, 2, 3, right);
        SystemPosition bridgeObject = GeneratedPosition(3, 3, 5, center);
        Assert.NotEqual(leftObject, bridgeObject);
        Assert.NotEqual(rightObject, bridgeObject);
        var design = new ShipDesign(new(1), "Fixture", new ConstructionRecipe([], new Work(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 0, pickupRangeUnits: 5000);
        var npc = new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("other"));
        var relationships = new RelationshipSetup(GameSessionTestFixture.Relationships.Principals.Append(
            new PrincipalDefinition(new(2), new PrincipalContentId("other"), "Other")), new(1), GameSessionTestFixture.StandingPolicy, []);
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "hidden-bridge"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
                new(CargoLossCause.FunctionalEjection, new(1, 1))], 4, radius, new(100000), new(1000), new(15000), new(100));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), new(2), design, left, npc, cargo: [Item, Item]),
             new InitialShipSetup(new(2), new(2), new(2), new(2), design, right, npc, cargo: [Item, Item]),
             new InitialShipSetup(new(3), new(3), new(3), new(2), design, center, npc, cargo: [Item, Item]),
             new InitialShipSetup(new(4), new(4), new(4), new(1), design, leftObject, GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new(5), new(5), new(5), new(1), design, rightObject, GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []), [], null, relationships, GameSessionTestFixture.RootSeed, 256,
            GameSessionTestFixture.Player, cargoPolicy: policy);
        using var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        CargoDispositionReceipt Drop(ulong source, ulong firstItem, SystemPosition position)
        {
            GameplayCommandRecord cause = session.SubmitCommand(GameSessionTestFixture.Player,
                new MoveShipCommand(new(4), new NavigationDestination.Position(leftObject), OrderPlacement.ReplaceAll));
            var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(cause.Envelope.Sequence), new(source), 0),
                new(source), position, CargoLossCause.StorageModuleLoss, policy,
                [new CargoDispositionSelection.Discrete(Item.Key, new(firstItem)), new CargoDispositionSelection.Discrete(Item.Key, new(firstItem + 1))], []);
            CargoDispositionCommitResult result = session.CommitCargoDisposition(request);
            Assert.True(result.WasApplied, result.Failure?.ToString());
            return result.Receipt!;
        }
        CargoDispositionReceipt first = Drop(1, 1, left);
        CargoDispositionReceipt second = Drop(2, 3, right);
        IReadOnlyList<CargoObjectView> firstBefore = session.CaptureVisibleCargoField(first.FieldHandle!.Value);
        IReadOnlyList<CargoObjectView> secondBefore = session.CaptureVisibleCargoField(second.FieldHandle!.Value);
        Assert.Equal(leftObject, Assert.Single(firstBefore).Position);
        Assert.Equal(rightObject, Assert.Single(secondBefore).Position);
        Assert.Equal(2, session.CaptureCheckpoint().Value!.Cargo!.Fields.Fields.Count);
        CargoObjectView[] visibleBefore = session.CaptureVisibleCargo().ToArray();

        CargoDispositionReceipt bridge = Drop(3, 5, center);
        Assert.DoesNotContain(session.CaptureVisibleCargo(), cargo => bridge.ObjectIds.Contains(cargo.EntityId));
        Assert.Equal(visibleBefore, session.CaptureVisibleCargo());
        Assert.Single(session.CaptureCheckpoint().Value!.Cargo!.Fields.Fields);
        Assert.Equal(firstBefore, session.CaptureVisibleCargoField(first.FieldHandle.Value));
        Assert.Equal(secondBefore, session.CaptureVisibleCargoField(second.FieldHandle.Value));
        Assert.Empty(session.CaptureVisibleCargoField(bridge.FieldHandle!.Value));
    }
}
