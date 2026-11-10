namespace GalaxyCommand.Simulation;

public sealed partial class Inventory
{
    /// <summary>
    /// Builds an unpublished single-content cargo inventory for a prepared disposition.
    /// The enclosing transaction owns source removal and lifecycle publication.
    /// Surviving discrete IDs are supplied, never allocated here.
    /// </summary>
    internal static Inventory CreateCargoObjectInventory(InventoryId inventoryId, EntityId objectId,
        PhysicalDefinition definition, PhysicalTransferSubject subject, DiscreteItemInstance? originalInstance = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(inventoryId.Value);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(subject);
        if (originalInstance is not null && (subject is not PhysicalTransferSubject.Discrete discrete
            || discrete.InstanceId != originalInstance.Id || definition.Key != originalInstance.DefinitionKey))
            throw new ArgumentException("Original item does not match the selected cargo instance.", nameof(originalInstance));
        ulong units = subject is PhysicalTransferSubject.Fungible fungible ? fungible.Quantity.Units : 1;
        var capacity = new Quantity(checked(units * definition.CapacityCost.Units));
        var inventory = new Inventory(inventoryId, new InventoryCustody.Unclaimed(objectId), capacity);
        InventoryStorageResult result = inventory.InitializeCargoObjectContents(definition, subject);
        if (!result.IsAccepted)
            throw new ArgumentException($"Invalid cargo contents: {result.RejectionReason}.", nameof(subject));
        if (originalInstance is not null) inventory._discreteItems[originalInstance.Id] = originalInstance;
        return inventory;
    }

    /// <summary>
    /// Initializes only unpublished empty cargo state, including private restoration.
    /// Ordinary stores never call this path, so partial collection cannot enable refill.
    /// </summary>
    private InventoryStorageResult InitializeCargoObjectContents(PhysicalDefinition definition, PhysicalTransferSubject subject)
    {
        if (Custody is not InventoryCustody.Unclaimed || UsedCapacity != Quantity.Zero || HasCommitments)
            throw new InvalidOperationException("Cargo initialization requires empty unclaimed inventory state.");
        bool compatible = (definition.HoldingKind, subject) switch
        {
            (PhysicalHoldingKind.Fungible, PhysicalTransferSubject.Fungible fungible) => definition.Key == fungible.DefinitionKey,
            (PhysicalHoldingKind.Discrete, PhysicalTransferSubject.Discrete) => true,
            _ => false,
        };
        if (!compatible) return InventoryStorageResult.Rejected(InventoryStorageRejectionReason.HoldingKindMismatch);

        Quantity quantity = subject is PhysicalTransferSubject.Fungible value ? value.Quantity : new Quantity(1);
        Quantity cost;
        try { cost = new Quantity(checked(quantity.Units * definition.CapacityCost.Units)); }
        catch (OverflowException) { return InventoryStorageResult.Rejected(InventoryStorageRejectionReason.CapacityOverflow); }
        if (cost > RemainingCapacity) return InventoryStorageResult.Rejected(InventoryStorageRejectionReason.InsufficientCapacity);

        if (subject is PhysicalTransferSubject.Fungible holding)
            ApplyStoreFungible(definition.Key, holding.Quantity, cost);
        else
            ApplyStoreDiscrete(new DiscreteItemInstance(((PhysicalTransferSubject.Discrete)subject).InstanceId, definition.Key), cost);
        return InventoryStorageResult.Accepted();
    }

    /// <summary>Rejects cargo shapes that would admit refilling, mixed contents, or gameplay commitments.</summary>
    private static bool HasValidCargoObjectShape(InventoryCheckpoint checkpoint) =>
        checkpoint.StoredMaterials.Count == 0 && checkpoint.Reservations.Count == 0
        && checkpoint.CapacityReservations.Count == 0 && checkpoint.PhysicalReservations.Count == 0
        && checkpoint.FungibleHoldings.Count + checkpoint.DiscreteItems.Count == 1;

    /// <summary>Checks live cargo shape using its sole authoritative inventory contents.</summary>
    internal static bool IsValidCargoObjectInventory(Inventory inventory) => inventory.Custody is InventoryCustody.Unclaimed
        && inventory.UsedCapacity > Quantity.Zero && HasValidCargoObjectShape(inventory.CaptureCheckpoint());
}

public sealed partial class InventoryRegistry
{
    /// <summary>Retains physical location while evaluating source-aware instance continuity without changing authority.</summary>
    internal IEnumerable<(InventoryId InventoryId, ItemInstanceId ItemId)> PhysicalItemLocations =>
        _inventories.Values.SelectMany(inventory => inventory.DiscreteItemIds.Select(id => (inventory.Id, id)));
}
