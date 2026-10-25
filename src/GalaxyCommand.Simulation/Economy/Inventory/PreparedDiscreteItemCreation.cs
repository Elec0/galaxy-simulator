namespace GalaxyCommand.Simulation;

public sealed partial class Inventory
{
    /// <summary>Preflights a cross-owner item creation without changing holdings, capacity or commitments.</summary>
    internal bool CanCreateDiscrete(PhysicalDefinition definition, ItemInstanceId id) =>
        id.Value != 0 && !_discreteItems.ContainsKey(id)
        && ValidateStorage(definition, PhysicalHoldingKind.Discrete, new Quantity(1), out _) is null;
}

public sealed partial class InventoryCommitOwner
{
    internal bool TryPeekItemIdentity(out ItemInstanceId id) => _itemInstanceIds.TryPeek(out id);

    /// <summary>
    /// Publishes a prevalidated item using the shared allocator. A mismatch is an invariant
    /// failure, not an ordinary rejection; the enclosing session commit must poison on failure.
    /// </summary>
    internal DiscreteItemInstance ApplyPreparedDiscreteCreation(Inventory inventory, PhysicalDefinition definition,
        ItemInstanceId expectedId)
    {
        if (!_itemInstanceIds.TryPeek(out ItemInstanceId next) || next != expectedId
            || !inventory.CanCreateDiscrete(definition, expectedId))
            throw new InvalidOperationException("Prepared item creation changed before cross-owner commit.");
        if (!inventory.StoreDiscrete(definition, expectedId).IsAccepted || _itemInstanceIds.Allocate() != expectedId)
            throw new InvalidOperationException("Prepared item creation failed during cross-owner commit.");
        return inventory.GetDiscrete(expectedId)!;
    }
}
