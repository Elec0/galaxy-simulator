using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionOrderCheckpointTests
{
    private static readonly SystemPosition Anchor = GameSessionTestFixture.Position(40, -10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CargoOrderRestorePreservesExactIntentAndActiveCollectionWithoutTravel(bool field)
    {
        ShipActionIntent.Cargo action = Action(field);
        ShipOrderCheckpoint order = Order(action) with { Reason = ShipOrderReason.CollectingCargo };
        CheckpointResult<ShipOrderCoordinator> result = Restore(order);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        ShipOrderSnapshot current = result.Value!.CaptureCurrent(new(1))!;
        Assert.Equal(action, current.Action);
        Assert.Equal(ShipOrderReason.CollectingCargo, current.Reason);
        Assert.Equal(action.Kind, current.Kind);
    }

    [Fact]
    public void CargoIntentRestoreRequiresItsAcceptedObservedPositionDestinationAndNoHeading()
    {
        ShipOrderCheckpoint valid = Order(Action(false));
        Assert.True(Restore(valid).IsSuccess);
        Assert.False(Restore(valid with { Destination = new NavigationDestination.Entity(new(9)) }).IsSuccess);
        Assert.False(Restore(valid with { Destination = new NavigationDestination.Position(GameSessionTestFixture.Position(41, -10)) }).IsSuccess);
        Assert.False(Restore(valid with { RequestedHeading = new ShipHeading(0) }).IsSuccess);
    }

    [Fact]
    public void CollectingReasonCannotBeAssignedToSensorOrMoveOrders()
    {
        ShipOrderCheckpoint move = Order(null) with { Reason = ShipOrderReason.CollectingCargo };
        Assert.False(Restore(move).IsSuccess);
        var sensor = new ShipActionIntent.DeploySensor(new(3), new(2),
            QualifiedContentKey.Create("test.commands", "deployable", "sensor"), new(8), Anchor);
        Assert.False(Restore(Order(sensor) with { Reason = ShipOrderReason.CollectingCargo }).IsSuccess);
    }

    private static ShipActionIntent.Cargo Action(bool field) => field
        ? new ShipActionIntent.CollectField(new(3), new(2), new(7), new(9), Anchor, [])
        : new ShipActionIntent.CollectObject(new(3), new(2), new(9), Anchor);

    private static ShipOrderCheckpoint Order(ShipActionIntent? action) => new(new(1), GameSessionTestFixture.Player,
        new NavigationDestination.Position(Anchor), ShipOrderStatus.Active, ShipOrderReason.AwaitingActionExecution,
        null, 0, null, null, Action: action, Kind: action?.Kind ?? ShipOrderKind.Move);

    private static CheckpointResult<ShipOrderCoordinator> Restore(ShipOrderCheckpoint order) =>
        ShipOrderCoordinator.RestoreCheckpoint(new(new(2), [new(new(1), new(order, [], null), null)]));
}
