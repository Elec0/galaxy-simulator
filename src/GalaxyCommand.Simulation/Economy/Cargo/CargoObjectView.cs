using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>A live cargo object's inspectable contents, excluding hidden source, extent and expiry authority.</summary>
public sealed record CargoObjectView
{
    /// <summary>
    /// Creates a positive single-definition view with exact occupied storage and original
    /// discrete identity. Invalid shape, coordinates or overflowing capacity reject construction.
    /// </summary>
    public CargoObjectView(EntityId entityId, SystemPosition position, QualifiedContentKey definitionKey,
        PhysicalHoldingKind holdingKind, Quantity quantity, Quantity unitCapacityCost, Quantity occupiedStorage,
        DiscreteItemInstance? discreteItem, DebrisFieldId? originalFieldId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ValidatePosition(position);
        ArgumentNullException.ThrowIfNull(definitionKey);
        if (!Enum.IsDefined(holdingKind)) throw new ArgumentOutOfRangeException(nameof(holdingKind));
        ArgumentOutOfRangeException.ThrowIfZero(quantity.Units, nameof(quantity));
        ArgumentOutOfRangeException.ThrowIfZero(unitCapacityCost.Units, nameof(unitCapacityCost));
        if (occupiedStorage.Units != checked(quantity.Units * unitCapacityCost.Units))
            throw new ArgumentException("Occupied storage must match the exact definition cost and quantity.", nameof(occupiedStorage));
        if (holdingKind == PhysicalHoldingKind.Discrete
            ? quantity.Units != 1 || discreteItem is null || discreteItem.DefinitionKey != definitionKey
            : discreteItem is not null)
            throw new ArgumentException("Discrete views require one exact original instance; fungible views have no instance.", nameof(discreteItem));
        if (originalFieldId is { Value: 0 }) throw new ArgumentOutOfRangeException(nameof(originalFieldId));
        EntityId = entityId;
        Position = position;
        DefinitionKey = definitionKey;
        HoldingKind = holdingKind;
        Quantity = quantity;
        UnitCapacityCost = unitCapacityCost;
        OccupiedStorage = occupiedStorage;
        DiscreteItem = discreteItem;
        OriginalFieldId = originalFieldId;
    }

    public EntityId EntityId { get; }
    public SystemPosition Position { get; }
    public QualifiedContentKey DefinitionKey { get; }
    public PhysicalHoldingKind HoldingKind { get; }
    public Quantity Quantity { get; }
    public Quantity UnitCapacityCost { get; }
    public Quantity OccupiedStorage { get; }
    public DiscreteItemInstance? DiscreteItem { get; }
    public DebrisFieldId? OriginalFieldId { get; }

    /// <summary>Enforces the accepted coordinate envelope so exact squared-distance comparisons fit wide arithmetic.</summary>
    internal static void ValidatePosition(SystemPosition position)
    {
        if (position.SystemId.Value == 0 || position.Position.X.Units is < -(1L << 50) or > (1L << 50)
            || position.Position.Y.Units is < -(1L << 50) or > (1L << 50))
            throw new ArgumentOutOfRangeException(nameof(position));
    }
}

internal static class CargoObjectObservation
{
    /// <summary>Copies only explicitly covered objects in stable identity order, retaining no stale view cache.</summary>
    internal static IReadOnlyList<CargoObjectView> ProjectCovered(IReadOnlyList<CargoObjectView> objects,
        IReadOnlySet<EntityId> coveredIds)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(coveredIds);
        return Array.AsReadOnly(objects.Where(view => coveredIds.Contains(view.EntityId)).OrderBy(view => view.EntityId.Value).ToArray());
    }
}

internal static class CargoFieldCandidateSelector
{
    /// <summary>Ranks covered members by priority, exact distance and identity, excluding objects with no fitting unit.</summary>
    internal static CargoObjectView? Select(IReadOnlyList<CargoObjectView> coveredMembers, SystemPosition shipPosition,
        Quantity availableIncomingCapacity, IReadOnlyList<QualifiedContentKey> priorities)
    {
        ArgumentNullException.ThrowIfNull(coveredMembers);
        ArgumentNullException.ThrowIfNull(priorities);
        CargoObjectView.ValidatePosition(shipPosition);
        var ranks = new Dictionary<QualifiedContentKey, int>();
        for (int index = 0; index < priorities.Count; index++)
            if (priorities[index] is not { } key || !ranks.TryAdd(key, index))
                throw new ArgumentException("Cargo priorities require unique physical definition keys.", nameof(priorities));

        // Only the caller's covered member subset participates. Private field aliases
        // and hidden objects cannot influence choice or make an otherwise empty order wait.
        return coveredMembers.Where(view => view.Position.SystemId == shipPosition.SystemId
                && view.UnitCapacityCost <= availableIncomingCapacity)
            .OrderBy(view => ranks.GetValueOrDefault(view.DefinitionKey, int.MaxValue))
            .ThenBy(view => SquaredDistance(shipPosition, view.Position))
            .ThenBy(view => view.EntityId.Value).FirstOrDefault();
    }

    /// <summary>Compares exact committed coordinates without square roots or floating-point rounding.</summary>
    private static Int128 SquaredDistance(SystemPosition left, SystemPosition right)
    {
        Int128 x = (Int128)left.Position.X.Units - right.Position.X.Units;
        Int128 y = (Int128)left.Position.Y.Units - right.Position.Y.Units;
        return checked(x * x + y * y);
    }
}
