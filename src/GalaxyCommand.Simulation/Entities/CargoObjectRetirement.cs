namespace GalaxyCommand.Simulation;

/// <summary>Distinguishes empty collection cleanup from permanent destruction at expiry.</summary>
internal enum CargoObjectRetirementKind { Collected, Expired }
internal sealed record PreparedCargoObjectRetirement(EntityLifecycleOwner Owner, CargoObjectSnapshot Object,
    Inventory Inventory, InventoryCheckpoint ExpectedContents, CargoObjectRetirementKind Kind);
internal sealed record PreparedCollectedCargoRetirement(EntityLifecycleOwner Owner, CargoObjectSnapshot Object,
    Inventory Inventory, InventoryCheckpoint ExpectedBeforeTransfer, PreparedCollectedCargoTransfer Transfer);

internal sealed partial class EntityLifecycleOwner
{
    /// <summary>
    /// Prepares retirement against the still-nonempty source and its exact planned
    /// complete transfer. This permits every owner to reject before collection mutates
    /// inventory, rather than preparing retirement only after an irreversible transfer.
    /// </summary>
    internal bool TryPrepareCollectedCargoRetirement(EntityId entityId, PreparedCollectedCargoTransfer transfer,
        out PreparedCollectedCargoRetirement? prepared)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        prepared = null;
        if (!_cargoObjects.TryGetValue(entityId, out CargoObjectSnapshot? cargo)
            || !ReferenceEquals(_inventories.Get(cargo.InventoryId), transfer.Source)) return false;
        var candidate = new PreparedCollectedCargoRetirement(this, cargo, transfer.Source,
            transfer.Source.CaptureCheckpoint(), transfer);
        if (!CanCommitCollectedCargoRetirement(candidate)) return false;
        prepared = candidate;
        return true;
    }

    /// <summary>Checks the pre-transfer state and proves that the prepared transfer will empty exactly this cargo object.</summary>
    internal bool CanCommitCollectedCargoRetirement(PreparedCollectedCargoRetirement prepared) =>
        HasExactCollectedCargoIdentity(prepared) && prepared.Transfer.CanApply()
        && ReferenceEquals(prepared.Transfer.Source, prepared.Inventory)
        && prepared.Inventory.UsedCapacity != Quantity.Zero
        && prepared.Inventory.UsedCapacity == prepared.Transfer.CapacityCost
        && Inventory.IsValidCargoObjectInventory(prepared.Inventory)
        && CargoRetirementContentsMatch(prepared.Inventory.CaptureCheckpoint(), prepared.ExpectedBeforeTransfer);

    /// <summary>
    /// Retires the expected empty post-transfer object using the previously prepared
    /// owner plan. Original item state has already moved to the recipient; this method
    /// destroys no contents and creates no replacement state.
    /// </summary>
    internal void ApplyPreparedCollectedCargoRetirement(PreparedCollectedCargoRetirement prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!HasExactCollectedCargoIdentity(prepared) || !prepared.Transfer.WasApplied
            || !ReferenceEquals(prepared.Transfer.Source, prepared.Inventory)
            || prepared.Inventory.UsedCapacity != Quantity.Zero || prepared.Inventory.HasCommitments
            || prepared.Inventory.Capacity != prepared.ExpectedBeforeTransfer.Capacity)
            throw new InvalidOperationException("Prepared collected cargo did not reach its expected empty post-transfer state.");
        if (!_cargoObjects.Remove(prepared.Object.EntityId) || !_entities.ApplyRemoveCargoObject(prepared.Object.EntityId)
            || !_inventories.ApplyRemove(prepared.Object.InventoryId))
            throw new InvalidOperationException("Prevalidated collected cargo retirement failed during commit.");
    }

    /// <summary>Checks only stable lifecycle links shared by the pre-transfer and post-transfer retirement boundaries.</summary>
    private bool HasExactCollectedCargoIdentity(PreparedCollectedCargoRetirement prepared) => ReferenceEquals(prepared.Owner, this)
        && _cargoObjects.TryGetValue(prepared.Object.EntityId, out CargoObjectSnapshot? current) && current == prepared.Object
        && _entities.GetKind(current.EntityId) == EntityKind.CargoObject
        && ReferenceEquals(_inventories.Get(current.InventoryId), prepared.Inventory)
        && prepared.Inventory.Custody == new InventoryCustody.Unclaimed(current.EntityId);

    /// <summary>
    /// Captures exact live cargo and inventory state without changing contents or
    /// identities. The causal owner must prepare field removal and pending-event
    /// cancellation alongside this lifecycle effect before committing any owner.
    /// </summary>
    internal bool TryPrepareCargoObjectRetirement(EntityId entityId, CargoObjectRetirementKind kind,
        out PreparedCargoObjectRetirement? prepared)
    {
        prepared = null;
        if (!Enum.IsDefined(kind) || !_cargoObjects.TryGetValue(entityId, out CargoObjectSnapshot? cargo)
            || _entities.GetKind(entityId) != EntityKind.CargoObject
            || _inventories.Get(cargo.InventoryId) is not { } inventory) return false;
        var candidate = new PreparedCargoObjectRetirement(this, cargo, inventory, inventory.CaptureCheckpoint(), kind);
        if (!CanCommitCargoObjectRetirement(candidate)) return false;
        prepared = candidate;
        return true;
    }

    /// <summary>
    /// Revalidates the exact object mapping, inventory instance and captured contents.
    /// A partial competing pickup invalidates expiry preparation instead of allowing
    /// stale destruction; a collected object may retire only after it becomes empty.
    /// </summary>
    internal bool CanCommitCargoObjectRetirement(PreparedCargoObjectRetirement prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!ReferenceEquals(prepared.Owner, this) || !Enum.IsDefined(prepared.Kind)
            || !_cargoObjects.TryGetValue(prepared.Object.EntityId, out CargoObjectSnapshot? current)
            || current != prepared.Object || _entities.GetKind(current.EntityId) != EntityKind.CargoObject
            || !ReferenceEquals(_inventories.Get(current.InventoryId), prepared.Inventory)
            || prepared.Inventory.Custody != new InventoryCustody.Unclaimed(current.EntityId)
            || prepared.Inventory.HasCommitments) return false;

        InventoryCheckpoint actual = prepared.Inventory.CaptureCheckpoint();
        if (!CargoRetirementContentsMatch(actual, prepared.ExpectedContents)
            || actual.StoredMaterials.Count != 0
            || prepared.Kind == CargoObjectRetirementKind.Collected && prepared.Inventory.UsedCapacity != Quantity.Zero)
            return false;
        return prepared.Inventory.UsedCapacity == Quantity.Zero || Inventory.IsValidCargoObjectInventory(prepared.Inventory);
    }

    /// <summary>
    /// Retires only a prevalidated cargo object. Expiry destroys remaining contents
    /// without replacement output; collection cleanup preserves the recipient's items.
    /// Any unexpected failure after mutation belongs to the enclosing session health guard.
    /// </summary>
    internal void ApplyPreparedCargoObjectRetirement(PreparedCargoObjectRetirement prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CanCommitCargoObjectRetirement(prepared))
            throw new InvalidOperationException("Prepared cargo retirement changed before commit.");
        if (prepared.Kind == CargoObjectRetirementKind.Expired) prepared.Inventory.ApplyDestroyAllContents();
        if (!_cargoObjects.Remove(prepared.Object.EntityId)
            || !_entities.ApplyRemoveCargoObject(prepared.Object.EntityId)
            || !_inventories.ApplyRemove(prepared.Object.InventoryId))
            throw new InvalidOperationException("Prevalidated cargo retirement failed during lifecycle commit.");
    }

    /// <summary>Compares canonical authoritative content snapshots without relying on container reference equality.</summary>
    private static bool CargoRetirementContentsMatch(InventoryCheckpoint actual, InventoryCheckpoint expected) =>
        actual.Id == expected.Id && actual.Custody == expected.Custody && actual.Capacity == expected.Capacity
        && actual.StoredMaterials.SequenceEqual(expected.StoredMaterials)
        && actual.FungibleHoldings.SequenceEqual(expected.FungibleHoldings)
        && actual.DiscreteItems.SequenceEqual(expected.DiscreteItems)
        && actual.Reservations.SequenceEqual(expected.Reservations)
        && actual.CapacityReservations.SequenceEqual(expected.CapacityReservations)
        && actual.PhysicalReservations.SequenceEqual(expected.PhysicalReservations);
}
