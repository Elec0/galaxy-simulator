using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoFieldHandleAdmissionTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "field-handle-item"),
        PhysicalHoldingKind.Discrete, new(1));

    [Fact]
    public void NewFieldCommandRequiresAnOriginalHandleOfferedByCurrentlyCoveredCargo()
    {
        using Fixture fixture = CreateWithRetiredSecondOrigin();
        GameSession session = fixture.Session;
        Assert.Empty(session.CaptureVisibleCargoField(fixture.Second.FieldHandle!.Value));
        Assert.Single(session.CaptureVisibleCargoField(fixture.First.FieldHandle!.Value));
        GameplayCommandRecord rejected = session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(4), fixture.Second.FieldHandle.Value, [], OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidIntent, rejected.Result.RejectionCode);
        Assert.Null(Ship(session, 4).CurrentOrder);
        Assert.Single(session.CaptureVisibleCargo());
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(4), fixture.First.FieldHandle.Value, [], OrderPlacement.ReplaceAll)).Result.Status);
    }

    [Fact]
    public void PreviouslyAcceptedFieldOrderRetainsItsAliasAndCollectsCoveredMembersAfterOriginalObjectsRetire()
    {
        using Fixture fixture = CreateWithRetiredSecondOrigin();
        GameSession session = fixture.Session;
        ShipOrderSnapshot queued = Assert.Single(Ship(session, 2).QueuedOrders);
        Assert.Equal(fixture.QueuedOrder, queued.Id);
        Assert.Equal(fixture.Second.FieldHandle, Assert.IsType<ShipActionIntent.CollectField>(queued.Action).TargetFieldId);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CancelShipOrderCommand(new(2), fixture.MoveOrder)).Result.Status);
        session.AdvanceTo(new(60000));
        GameShipSnapshot collector = Ship(session, 2);
        Assert.Equal(fixture.QueuedOrder, collector.CurrentOrder!.Id);
        Assert.Equal(ShipOrderStatus.Completed, collector.CurrentOrder.Status);
        Assert.Equal(fixture.Second.FieldHandle, Assert.IsType<ShipActionIntent.CollectField>(collector.CurrentOrder.Action).TargetFieldId);
        InventoryCheckpoint inventory = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(entry => entry.Id == new InventoryId(2));
        Assert.Single(inventory.DiscreteItems);
        Assert.Empty(session.CaptureVisibleCargo());
    }

    private static Fixture CreateWithRetiredSecondOrigin()
    {
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "field-handles"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
                new(CargoLossCause.FunctionalEjection, new(1, 1))], 4, 0, new(100000), new(1000), new(15000), new(100));
        var ships = Enumerable.Range(1, 4).Select(index => new InitialShipSetup(new((ulong)index), new((ulong)index),
            new((ulong)index), new(1), GameSessionTestFixture.Design, GameSessionTestFixture.Position(index == 2 ? 6000 : 0, 0),
            GameSessionTestFixture.PlayerController, cargo: index == 1 ? [Item, Item, Item, Item] : [])).ToArray();
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")], ships, new ConnectorTopology([], []), [], null,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 512, GameSessionTestFixture.Player, cargoPolicy: policy);
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        CargoDispositionReceipt Drop(ulong firstItem)
        {
            GameplayCommandRecord cause = session.SubmitCommand(GameSessionTestFixture.Player,
                new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
            var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(cause.Envelope.Sequence), new(1), 0), new(1),
                GameSessionTestFixture.Position(0, 0), CargoLossCause.StorageModuleLoss, policy,
                [new CargoDispositionSelection.Discrete(Item.Key, new(firstItem)), new CargoDispositionSelection.Discrete(Item.Key, new(firstItem + 1))], []);
            CargoDispositionCommitResult result = session.CommitCargoDisposition(request);
            Assert.True(result.WasApplied, result.Failure?.ToString());
            return result.Receipt!;
        }
        CargoDispositionReceipt first = Drop(1);
        CargoDispositionReceipt second = Drop(3);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(2), GameSessionTestFixture.Destination(100000, 0), OrderPlacement.ReplaceAll)).Result.Status);
        ShipOrderId move = Ship(session, 2).CurrentOrder!.Id;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(2), second.FieldHandle!.Value, [], OrderPlacement.Append)).Result.Status);
        ShipOrderId queued = Assert.Single(Ship(session, 2).QueuedOrders).Id;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(3), Assert.Single(second.ObjectIds), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(1000));
        Assert.Equal(first.ObjectIds[0], Assert.Single(session.CaptureVisibleCargo()).EntityId);
        return new(session, first, second, move, queued);
    }

    private static GameShipSnapshot Ship(GameSession session, ulong id) => session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(id));

    private sealed record Fixture(GameSession Session, CargoDispositionReceipt First, CargoDispositionReceipt Second,
        ShipOrderId MoveOrder, ShipOrderId QueuedOrder) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }
}
