using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Requests timed collection of one exact visible cargo object by one ship.</summary>
public sealed record CollectCargoObjectCommand : GameplayCommand
{
    public const string CommandKind = "cargo.collect-object";
    /// <summary>Captures intent without reserving target contents or admitting current coverage and capacity.</summary>
    public CollectCargoObjectCommand(ShipId shipId, EntityId targetEntityId, OrderPlacement placement) : base(CommandKind)
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

/// <summary>Requests field collection with priorities captured independently from later client editing.</summary>
public sealed record CollectCargoFieldCommand : GameplayCommand
{
    public const string CommandKind = "cargo.collect-field";
    /// <summary>Captures a stable field handle and unique priorities; content and coverage admission remain authoritative.</summary>
    public CollectCargoFieldCommand(ShipId shipId, DebrisFieldId targetFieldId, IEnumerable<QualifiedContentKey> priorities, OrderPlacement placement) : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(targetFieldId.Value);
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        ShipId = shipId;
        TargetFieldId = targetFieldId;
        Priorities = CargoCollectionPriorities.Capture(priorities);
        Placement = placement;
    }
    public ShipId ShipId { get; }
    public DebrisFieldId TargetFieldId { get; }
    public IReadOnlyList<QualifiedContentKey> Priorities { get; }
    public OrderPlacement Placement { get; }
}

public abstract partial record ShipActionIntent
{
    public abstract record Cargo : ShipActionIntent
    {
        /// <summary>Retains the accepted observed anchor without authorizing hidden target updates during approach.</summary>
        private protected Cargo(CommandSequence command, InventoryId inventory, EntityId observedObject, SystemPosition anchor) : base(command, inventory)
        {
            ArgumentOutOfRangeException.ThrowIfZero(observedObject.Value);
            ArgumentOutOfRangeException.ThrowIfZero(anchor.SystemId.Value);
            if (anchor.Position.X.Units is < -(1L << 50) or > (1L << 50)
                || anchor.Position.Y.Units is < -(1L << 50) or > (1L << 50))
                throw new ArgumentOutOfRangeException(nameof(anchor));
            LastObservedObjectEntityId = observedObject;
            LastObservedPosition = anchor;
        }
        public EntityId LastObservedObjectEntityId { get; }
        public SystemPosition LastObservedPosition { get; }
    }

    public sealed record CollectObject : Cargo
    {
        /// <summary>Retains one exact accepted object identity and its last observed position.</summary>
        internal CollectObject(CommandSequence command, InventoryId inventory, EntityId target, SystemPosition anchor) : base(command, inventory, target, anchor) { }
        public EntityId TargetEntityId => LastObservedObjectEntityId;
        public override ShipOrderKind Kind => ShipOrderKind.CollectCargoObject;
    }

    public sealed record CollectField : Cargo
    {
        /// <summary>Retains the original field handle, observed member anchor and immutable priority order.</summary>
        internal CollectField(CommandSequence command, InventoryId inventory, DebrisFieldId targetField,
            EntityId observedObject, SystemPosition anchor, IEnumerable<QualifiedContentKey> priorities) : base(command, inventory, observedObject, anchor)
        {
            ArgumentOutOfRangeException.ThrowIfZero(targetField.Value);
            TargetFieldId = targetField;
            Priorities = CargoCollectionPriorities.Capture(priorities);
        }
        public DebrisFieldId TargetFieldId { get; }
        public IReadOnlyList<QualifiedContentKey> Priorities { get; }
        public override ShipOrderKind Kind => ShipOrderKind.CollectCargoField;
    }
}

internal static class CargoCollectionPriorities
{
    /// <summary>Copies exact unique keys without inferring content meaning or sharing a mutable client array.</summary>
    internal static IReadOnlyList<QualifiedContentKey> Capture(IEnumerable<QualifiedContentKey> priorities)
    {
        ArgumentNullException.ThrowIfNull(priorities);
        QualifiedContentKey[] captured = priorities.ToArray();
        if (captured.Any(key => key is null) || captured.Distinct().Count() != captured.Length)
            throw new ArgumentException("Cargo priorities must be unique nonnull qualified keys.", nameof(priorities));
        return Array.AsReadOnly(captured);
    }
}
