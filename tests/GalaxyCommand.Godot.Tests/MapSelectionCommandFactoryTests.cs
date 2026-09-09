using GalaxyCommand.GodotClient;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Godot.Tests;

public sealed class MapSelectionCommandFactoryTests
{
    [Fact]
    public void ReplaceMoveUsesTheCapturedMultiSelection()
    {
        GameplayCommand? command = MapSelectionCommandFactory.CreateMove(
            [new ShipId(2), new ShipId(1)],
            new ShipId(2),
            Destination(),
            OrderPlacement.ReplaceAll);

        var groupMove = Assert.IsType<MoveShipGroupCommand>(command);
        Assert.Equal([new ShipId(1), new ShipId(2)], groupMove.ShipIds);
    }

    [Fact]
    public void AppendMoveRemainsFocusedShipOnly()
    {
        GameplayCommand? command = MapSelectionCommandFactory.CreateMove(
            [new ShipId(2), new ShipId(1)],
            new ShipId(2),
            Destination(),
            OrderPlacement.Append);

        var move = Assert.IsType<MoveShipCommand>(command);
        Assert.Equal(new ShipId(2), move.ShipId);
        Assert.Equal(OrderPlacement.Append, move.Placement);
    }

    [Fact]
    public void CancelUsesTheCapturedMultiSelectionEvenWhenFocusedShipIsIdle()
    {
        GameplayCommand? command = MapSelectionCommandFactory.CreateCancel(
            [new ShipId(2), new ShipId(1)],
            new ShipId(2),
            currentFocusedOrder: null);

        var cancellation = Assert.IsType<CancelShipGroupCommand>(command);
        Assert.Equal([new ShipId(1), new ShipId(2)], cancellation.ShipIds);
    }

    private static SystemPosition Destination() =>
        new(
            new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(1_000), new SpatialCoordinate(2_000)));
}
