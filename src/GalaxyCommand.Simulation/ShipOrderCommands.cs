namespace GalaxyCommand.Simulation;

public sealed record MoveShipCommand : GameplayCommand
{
    public const string CommandKind = "ship.move";

    public MoveShipCommand(ShipId shipId, NavigationDestination destination)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(destination);
        ShipId = shipId;
        Destination = destination;
    }

    public ShipId ShipId { get; }

    public NavigationDestination Destination { get; }
}

public sealed record CancelShipOrderCommand : GameplayCommand
{
    public const string CommandKind = "ship.cancel-order";

    public CancelShipOrderCommand(ShipId shipId)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ShipId = shipId;
    }

    public ShipId ShipId { get; }
}
