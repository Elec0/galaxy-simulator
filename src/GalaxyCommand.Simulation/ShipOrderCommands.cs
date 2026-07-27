namespace GalaxyCommand.Simulation;

public sealed record MoveShipCommand : GameplayCommand
{
    public const string CommandKind = "ship.move";

    public MoveShipCommand(
        ShipId shipId,
        NavigationDestination destination,
        OrderPlacement placement)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(placement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placement),
                placement,
                "Unknown order placement.");
        }

        ShipId = shipId;
        Destination = destination;
        Placement = placement;
    }

    public ShipId ShipId { get; }

    public NavigationDestination Destination { get; }

    public OrderPlacement Placement { get; }
}

public sealed record CancelShipOrderCommand : GameplayCommand
{
    public const string CommandKind = "ship.cancel-order";

    public CancelShipOrderCommand(ShipId shipId, ShipOrderId orderId)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        ShipId = shipId;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public ShipOrderId OrderId { get; }
}

public sealed record BeginScriptedOverrideCommand : GameplayCommand
{
    public const string CommandKind = "actor.begin-scripted-override";

    public BeginScriptedOverrideCommand(
        ShipId shipId,
        ActorOverrideReasonId reason,
        ActorControlRevision expectedRevision)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason.Value);
        ShipId = shipId;
        Reason = reason;
        ExpectedRevision = expectedRevision;
    }

    public ShipId ShipId { get; }

    public ActorOverrideReasonId Reason { get; }

    public ActorControlRevision ExpectedRevision { get; }
}

public sealed record EndScriptedOverrideCommand : GameplayCommand
{
    public const string CommandKind = "actor.end-scripted-override";

    public EndScriptedOverrideCommand(
        ShipId shipId,
        ScriptedOverrideReleasePolicy releasePolicy,
        ActorControlRevision expectedRevision)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        if (!Enum.IsDefined(releasePolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(releasePolicy),
                releasePolicy,
                "Unknown scripted override release policy.");
        }

        ShipId = shipId;
        ReleasePolicy = releasePolicy;
        ExpectedRevision = expectedRevision;
    }

    public ShipId ShipId { get; }

    public ScriptedOverrideReleasePolicy ReleasePolicy { get; }

    public ActorControlRevision ExpectedRevision { get; }
}
