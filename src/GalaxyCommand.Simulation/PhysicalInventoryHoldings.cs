using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// One individually identified physical item and its immutable definition
/// reference.
/// </summary>
public sealed record DiscreteItemInstance
{
    /// <summary>Creates a discrete item with a non-zero session identity.</summary>
    public DiscreteItemInstance(
        ItemInstanceId id,
        QualifiedContentKey definitionKey)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentNullException.ThrowIfNull(definitionKey);
        Id = id;
        DefinitionKey = definitionKey;
    }

    public ItemInstanceId Id { get; }

    public QualifiedContentKey DefinitionKey { get; }
}

/// <summary>
/// Stable reasons that a physical holding could not be stored.
/// </summary>
public enum InventoryStorageRejectionReason
{
    MissingCustody,
    HoldingKindMismatch,
    InvalidQuantity,
    InvalidItemInstance,
    DuplicateItemInstance,
    InsufficientCapacity,
    CapacityOverflow,
}

/// <summary>
/// Typed result for one physical holding storage request.
/// </summary>
public sealed record InventoryStorageResult
{
    private InventoryStorageResult(
        bool isAccepted,
        InventoryStorageRejectionReason? rejectionReason)
    {
        IsAccepted = isAccepted;
        RejectionReason = rejectionReason;
    }

    public bool IsAccepted { get; }

    public InventoryStorageRejectionReason? RejectionReason { get; }

    internal static InventoryStorageResult Accepted() => new(true, null);

    internal static InventoryStorageResult Rejected(
        InventoryStorageRejectionReason reason) =>
        new(false, reason);
}

public sealed partial class Inventory
{
    private readonly Dictionary<QualifiedContentKey, Quantity> _fungibleStored = [];
    private readonly Dictionary<ItemInstanceId, DiscreteItemInstance> _discreteItems = [];
    private Quantity _physicalUsedCapacity;

    internal IEnumerable<ItemInstanceId> DiscreteItemIds => _discreteItems.Keys;

    /// <summary>
    /// Gets capacity consumed by both legacy materials and generalized physical
    /// holdings. Incoming reservations are excluded.
    /// </summary>
    public Quantity UsedCapacity => TotalStored.Add(_physicalUsedCapacity);

    /// <summary>Returns the aggregate fungible quantity for an exact definition.</summary>
    public Quantity FungibleStored(QualifiedContentKey definitionKey)
    {
        ArgumentNullException.ThrowIfNull(definitionKey);
        return _fungibleStored.GetValueOrDefault(definitionKey, Quantity.Zero);
    }

    /// <summary>Returns an exact discrete item instance when it is held here.</summary>
    public DiscreteItemInstance? GetDiscrete(ItemInstanceId instanceId) =>
        _discreteItems.GetValueOrDefault(instanceId);

    /// <summary>
    /// Stores a positive fungible quantity as one aggregate holding. Rejection
    /// leaves holdings and capacity unchanged.
    /// </summary>
    public InventoryStorageResult StoreFungible(
        PhysicalDefinition definition,
        Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(definition);
        InventoryStorageResult? rejection = ValidateStorage(
            definition,
            PhysicalHoldingKind.Fungible,
            quantity,
            out Quantity capacityCost);
        if (rejection is not null)
        {
            return rejection;
        }

        ApplyStoreFungible(definition.Key, quantity, capacityCost);
        return InventoryStorageResult.Accepted();
    }

    /// <summary>
    /// Stores one discrete instance without changing its session identity.
    /// Rejection leaves holdings and capacity unchanged.
    /// </summary>
    public InventoryStorageResult StoreDiscrete(
        PhysicalDefinition definition,
        ItemInstanceId instanceId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (instanceId.Value == 0)
        {
            return InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.InvalidItemInstance);
        }

        if (_discreteItems.ContainsKey(instanceId))
        {
            return InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.DuplicateItemInstance);
        }

        InventoryStorageResult? rejection = ValidateStorage(
            definition,
            PhysicalHoldingKind.Discrete,
            new Quantity(1),
            out Quantity capacityCost);
        if (rejection is not null)
        {
            return rejection;
        }

        ApplyStoreDiscrete(
            new DiscreteItemInstance(instanceId, definition.Key),
            capacityCost);
        return InventoryStorageResult.Accepted();
    }

    /// <summary>
    /// Applies a prevalidated fungible removal while keeping aggregate holding
    /// and capacity state synchronized.
    /// </summary>
    internal void ApplyRemoveFungible(
        QualifiedContentKey definitionKey,
        Quantity quantity,
        Quantity capacityCost)
    {
        Quantity remaining = FungibleStored(definitionKey).Subtract(quantity);
        if (remaining == Quantity.Zero)
        {
            _fungibleStored.Remove(definitionKey);
        }
        else
        {
            _fungibleStored[definitionKey] = remaining;
        }

        _physicalUsedCapacity = _physicalUsedCapacity.Subtract(capacityCost);
    }

    internal void ApplyRemoveDiscrete(
        ItemInstanceId instanceId,
        Quantity capacityCost)
    {
        _discreteItems.Remove(instanceId);
        _physicalUsedCapacity = _physicalUsedCapacity.Subtract(capacityCost);
    }

    internal void ApplyStoreFungible(
        QualifiedContentKey definitionKey,
        Quantity quantity,
        Quantity capacityCost)
    {
        _fungibleStored[definitionKey] = FungibleStored(definitionKey).Add(quantity);
        _physicalUsedCapacity = _physicalUsedCapacity.Add(capacityCost);
    }

    internal void ApplyStoreDiscrete(
        DiscreteItemInstance instance,
        Quantity capacityCost)
    {
        _discreteItems.Add(instance.Id, instance);
        _physicalUsedCapacity = _physicalUsedCapacity.Add(capacityCost);
    }

    /// <summary>
    /// Validates the shared custody, holding-kind, quantity, arithmetic, and
    /// capacity invariants before a storage method mutates any state.
    /// </summary>
    private InventoryStorageResult? ValidateStorage(
        PhysicalDefinition definition,
        PhysicalHoldingKind expectedKind,
        Quantity quantity,
        out Quantity capacityCost)
    {
        capacityCost = Quantity.Zero;
        if (Custody is null)
        {
            return InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.MissingCustody);
        }

        if (definition.HoldingKind != expectedKind)
        {
            return InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.HoldingKindMismatch);
        }

        if (quantity == Quantity.Zero)
        {
            return InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.InvalidQuantity);
        }

        try
        {
            capacityCost = new Quantity(checked(
                definition.CapacityCost.Units * quantity.Units));
        }
        catch (OverflowException)
        {
            return InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.CapacityOverflow);
        }

        return capacityCost > RemainingCapacity
            ? InventoryStorageResult.Rejected(
                InventoryStorageRejectionReason.InsufficientCapacity)
            : null;
    }
}
