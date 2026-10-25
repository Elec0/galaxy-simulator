using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Requests deployment of one exact cargo item at a fixed local position.</summary>
public sealed record DeploySensorCommand : GameplayCommand
{
    public const string CommandKind = "sensor.deploy";

    /// <summary>Captures intent only. Admission and execution independently validate current cargo, authority and range.</summary>
    public DeploySensorCommand(ShipId shipId, ItemInstanceId itemId, SystemPosition position, OrderPlacement placement)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(itemId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        ShipId = shipId;
        ItemId = itemId;
        Position = position;
        Placement = placement;
    }

    public ShipId ShipId { get; }
    public ItemInstanceId ItemId { get; }
    public SystemPosition Position { get; }
    public OrderPlacement Placement { get; }
}

/// <summary>Requests recovery of one live, same-owner stationary sensor into the acting ship's cargo.</summary>
public sealed record PickupSensorCommand : GameplayCommand
{
    public const string CommandKind = "sensor.pickup";

    /// <summary>Captures explicit target and placement without reserving the entity or cargo capacity.</summary>
    public PickupSensorCommand(ShipId shipId, EntityId targetEntityId, OrderPlacement placement)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(targetEntityId.Value);
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        ShipId = shipId;
        TargetEntityId = targetEntityId;
        Placement = placement;
    }

    public ShipId ShipId { get; }
    public EntityId TargetEntityId { get; }
    public OrderPlacement Placement { get; }
}

public enum ShipOrderKind { Move, DeploySensor, PickupSensor }

/// <summary>Immutable action payload retained by the ordinary order lifecycle, including original command correlation.</summary>
public abstract record ShipActionIntent
{
    private protected ShipActionIntent(CommandSequence command, InventoryId inventory, QualifiedContentKey definition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(command.Value);
        ArgumentOutOfRangeException.ThrowIfZero(inventory.Value);
        ArgumentNullException.ThrowIfNull(definition);
        OriginCommandSequence = command;
        CargoInventoryId = inventory;
        DefinitionKey = definition;
    }

    public CommandSequence OriginCommandSequence { get; }
    public InventoryId CargoInventoryId { get; }
    public QualifiedContentKey DefinitionKey { get; }
    public abstract ShipOrderKind Kind { get; }

    public sealed record DeploySensor : ShipActionIntent
    {
        /// <summary>Retains the admitted exact item and fixed target independently of future ship motion.</summary>
        internal DeploySensor(CommandSequence command, InventoryId inventory, QualifiedContentKey definition,
            ItemInstanceId item, SystemPosition position) : base(command, inventory, definition)
        {
            ArgumentOutOfRangeException.ThrowIfZero(item.Value);
            ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
            ItemId = item;
            Position = position;
        }
        public ItemInstanceId ItemId { get; }
        public SystemPosition Position { get; }
        public override ShipOrderKind Kind => ShipOrderKind.DeploySensor;
    }

    public sealed record PickupSensor : ShipActionIntent
    {
        /// <summary>Retains the admitted stable entity without reserving it for this order.</summary>
        internal PickupSensor(CommandSequence command, InventoryId inventory, QualifiedContentKey definition,
            EntityId target) : base(command, inventory, definition)
        {
            ArgumentOutOfRangeException.ThrowIfZero(target.Value);
            TargetEntityId = target;
        }
        public EntityId TargetEntityId { get; }
        public override ShipOrderKind Kind => ShipOrderKind.PickupSensor;
    }
}
