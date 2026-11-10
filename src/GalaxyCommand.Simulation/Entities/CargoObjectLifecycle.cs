using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record CargoObjectSnapshot(EntityId EntityId, InventoryId InventoryId, SystemPosition Position);
internal sealed record CargoObjectSeed(SystemPosition Position, PhysicalDefinition Definition, PhysicalTransferSubject Subject,
    DiscreteItemInstance? OriginalInstance = null);
internal sealed record PreparedCargoObject(CargoObjectSnapshot Object, Inventory Inventory);
internal sealed record PreparedCargoObjectBatch(IReadOnlyList<PreparedCargoObject> Objects,
    IdSequenceCheckpoint EntityIds, IdSequenceCheckpoint InventoryIds);

internal sealed partial class EntityLifecycleOwner
{
    private readonly SortedDictionary<EntityId, CargoObjectSnapshot> _cargoObjects = new(EntityIdComparer<EntityId>.Instance);

    /// <summary>
    /// Previews one causal publication batch without consuming identifiers or changing
    /// source holdings. The disposition owner supplies canonical seed order and prepares
    /// source removal, fields, expiry and receipts before any owner commits.
    /// </summary>
    internal bool TryPrepareCargoObjects(IReadOnlyList<CargoObjectSeed> seeds, out PreparedCargoObjectBatch? batch)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        batch = null;
        if (!_entityIds.CanAllocate((ulong)seeds.Count) || !_inventoryIds.CanAllocate((ulong)seeds.Count)) return false;
        var prepared = new List<PreparedCargoObject>(seeds.Count);
        var items = new HashSet<ItemInstanceId>();
        ulong entityStart = _entityIds.CaptureCheckpoint().NextValue ?? 0;
        ulong inventoryStart = _inventoryIds.CaptureCheckpoint().NextValue ?? 0;
        for (int index = 0; index < seeds.Count; index++)
        {
            CargoObjectSeed? seed = seeds[index];
            if (seed is null || !IsValidCargoPosition(seed.Position) || seed.Definition is null || seed.Subject is null)
                return false;
            if (seed.Subject is PhysicalTransferSubject.Discrete discrete && !items.Add(discrete.InstanceId)) return false;
            var entityId = new EntityId(checked(entityStart + (ulong)index));
            var inventoryId = new InventoryId(checked(inventoryStart + (ulong)index));
            Inventory inventory;
            try { inventory = Inventory.CreateCargoObjectInventory(inventoryId, entityId, seed.Definition, seed.Subject, seed.OriginalInstance); }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
            prepared.Add(new PreparedCargoObject(new CargoObjectSnapshot(entityId, inventoryId, seed.Position), inventory));
        }
        batch = new PreparedCargoObjectBatch(Array.AsReadOnly(prepared.ToArray()),
            _entityIds.CaptureCheckpoint(), _inventoryIds.CaptureCheckpoint());
        return true;
    }

    /// <summary>
    /// Revalidates allocator positions and all unpublished objects before mutation.
    /// Source item removals must already be part of the enclosing prepared commit;
    /// no surviving instance may remain in another inventory at publication.
    /// </summary>
    internal bool CanCommitCargoObjects(PreparedCargoObjectBatch batch) => CanCommitCargoObjectsCore(batch, null);

    /// <summary>
    /// Preflights publication while original instances are still in a source whose
    /// complete loss delta is prepared. Commit uses the ordinary no-duplicates check
    /// again after source removal, so this exemption cannot survive publication.
    /// </summary>
    internal bool CanCommitCargoObjects(PreparedCargoObjectBatch batch, PreparedCargoSourceLoss sourceLoss) =>
        sourceLoss.CanApply() && ReferenceEquals(_inventories.Get(sourceLoss.Source.Id), sourceLoss.Source)
        && CanCommitCargoObjectsCore(batch, sourceLoss) && CargoObjectsAreContainedInSourceLoss(batch, sourceLoss);

    /// <summary>
    /// Proves publication is a subset of the selected source delta without choosing
    /// its survival policy. Discrete contents retain exact original references; all
    /// contents retain their selected definition's capacity meaning.
    /// </summary>
    private static bool CargoObjectsAreContainedInSourceLoss(PreparedCargoObjectBatch batch, PreparedCargoSourceLoss sourceLoss)
    {
        var groups = sourceLoss.Groups.ToDictionary(group => group.Definition.Key);
        var originals = sourceLoss.Groups.SelectMany(group => group.DiscreteItems).ToDictionary(item => item.Id);
        var emittedQuantities = new Dictionary<QualifiedContentKey, ulong>();
        foreach (PreparedCargoObject entry in batch.Objects)
        {
            InventoryCheckpoint contents = entry.Inventory.CaptureCheckpoint();
            if (contents.FungibleHoldings.Count == 1)
            {
                InventoryFungibleCheckpoint holding = contents.FungibleHoldings[0];
                if (!groups.TryGetValue(holding.DefinitionKey, out CargoSourceLossGroup? group)
                    || group.Definition.HoldingKind != PhysicalHoldingKind.Fungible)
                    return false;
                ulong emitted = emittedQuantities.GetValueOrDefault(holding.DefinitionKey);
                // Bound before adding so hostile aggregate shares cannot overflow
                // or consume units outside this exact selected definition group.
                if (holding.Quantity.Units > group.Quantity.Units - emitted
                    || checked((UInt128)holding.Quantity.Units * group.Definition.CapacityCost.Units) != contents.Capacity.Units)
                    return false;
                emittedQuantities[holding.DefinitionKey] = checked(emitted + holding.Quantity.Units);
            }
            else
            {
                InventoryDiscreteItemCheckpoint item = contents.DiscreteItems[0];
                if (!groups.TryGetValue(item.DefinitionKey, out CargoSourceLossGroup? group)
                    || group.Definition.HoldingKind != PhysicalHoldingKind.Discrete
                    || contents.Capacity != group.Definition.CapacityCost)
                    return false;
                if (!originals.TryGetValue(item.Id, out DiscreteItemInstance? original)
                    || original.DefinitionKey != group.Definition.Key
                    || !ReferenceEquals(original, entry.Inventory.GetDiscrete(item.Id)))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Checks current identity capacity and item locations, excluding only the exact prepared source removals.</summary>
    private bool CanCommitCargoObjectsCore(PreparedCargoObjectBatch batch, PreparedCargoSourceLoss? sourceLoss)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.EntityIds != _entityIds.CaptureCheckpoint() || batch.InventoryIds != _inventoryIds.CaptureCheckpoint()
            || !_entityIds.CanAllocate((ulong)batch.Objects.Count) || !_inventoryIds.CanAllocate((ulong)batch.Objects.Count)) return false;
        HashSet<ItemInstanceId> removed = sourceLoss?.Groups.SelectMany(group => group.DiscreteItems).Select(item => item.Id).ToHashSet() ?? [];
        var itemIds = _inventories.PhysicalItemLocations.Where(location => sourceLoss is null
            || location.InventoryId != sourceLoss.Source.Id || !removed.Contains(location.ItemId)).Select(location => location.ItemId).ToHashSet();
        for (int index = 0; index < batch.Objects.Count; index++)
        {
            PreparedCargoObject entry = batch.Objects[index];
            CargoObjectSnapshot cargo = entry.Object;
            if (cargo.EntityId.Value != checked(batch.EntityIds.NextValue!.Value + (ulong)index)
                || cargo.InventoryId.Value != checked(batch.InventoryIds.NextValue!.Value + (ulong)index)
                || !_entities.CanAddCargoObject(cargo.EntityId) || _inventories.Contains(cargo.InventoryId)
                || !IsValidCargoPosition(cargo.Position) || entry.Inventory.Id != cargo.InventoryId
                || entry.Inventory.Custody != new InventoryCustody.Unclaimed(cargo.EntityId)
                || entry.Inventory.Capacity != entry.Inventory.UsedCapacity
                || !Inventory.IsValidCargoObjectInventory(entry.Inventory)
                || entry.Inventory.DiscreteItemIds.Any(id => !itemIds.Add(id))) return false;
        }
        return true;
    }

    /// <summary>
    /// Publishes a prevalidated batch using the shared lifecycle sequences. Any failure
    /// after mutation is an invariant failure for the enclosing session health guard.
    /// </summary>
    internal void ApplyPreparedCargoObjects(PreparedCargoObjectBatch batch)
    {
        if (!CanCommitCargoObjects(batch)) throw new InvalidOperationException("Prepared cargo publication changed before commit.");
        foreach (PreparedCargoObject entry in batch.Objects)
        {
            if (_entityIds.Allocate() != entry.Object.EntityId || _inventoryIds.Allocate() != entry.Object.InventoryId)
                throw new InvalidOperationException("Prepared cargo allocator identity changed during commit.");
            _inventories.Add(entry.Inventory);
            _entities.ApplyAddCargoObject(entry.Object.EntityId);
            _cargoObjects.Add(entry.Object.EntityId, entry.Object);
        }
    }

    internal IReadOnlyList<CargoObjectSnapshot> CaptureCargoObjects() => Array.AsReadOnly(_cargoObjects.Values.ToArray());

    internal CargoObjectSnapshot? GetCargoObject(EntityId entityId) => _cargoObjects.GetValueOrDefault(entityId);

    /// <summary>Enforces the approved stationary position envelope before publication or restoration.</summary>
    private static bool IsValidCargoPosition(SystemPosition position) => position.SystemId.Value != 0
        && position.Position.X.Units is >= -(1L << 50) and <= (1L << 50)
        && position.Position.Y.Units is >= -(1L << 50) and <= (1L << 50);

    /// <summary>
    /// Restores cargo mappings only when every unclaimed inventory has exactly one
    /// live cargo owner, with no recycled historical identity or duplicated custody.
    /// </summary>
    private CheckpointValidationFailure? RestoreCargoObjects(EntityLifecycleCheckpoint checkpoint)
    {
        const string path = "$.checkpoint.lifecycle.cargoObjects";
        HashSet<EntityId> historicalEntities = checkpoint.MaterializationReceipts.Select(receipt => receipt!.EntityId)
            .Concat(checkpoint.RemovalReceipts.Select(receipt => receipt!.Request!.EntityId))
            .Concat(checkpoint.SensorDeployables.Receipts.Select(receipt => receipt!.Result.Deployed!.EntityId)).ToHashSet();
        HashSet<InventoryId> historicalInventories = checkpoint.MaterializationReceipts.Select(receipt => receipt!.CargoInventoryId)
            .Concat(checkpoint.RemovalReceipts.Select(receipt => receipt!.CargoInventoryId)).ToHashSet();
        var inventoryIds = new HashSet<InventoryId>();
        foreach (CargoObjectSnapshot? cargo in checkpoint.CargoObjects)
        {
            if (cargo is null || !WasAllocated(cargo.EntityId.Value, checkpoint.EntityIds)
                || !WasAllocated(cargo.InventoryId.Value, checkpoint.InventoryIds) || !IsValidCargoPosition(cargo.Position)
                || !_entities.CanAddCargoObject(cargo.EntityId) || historicalEntities.Contains(cargo.EntityId)
                || historicalInventories.Contains(cargo.InventoryId) || !inventoryIds.Add(cargo.InventoryId)
                || _inventories.Get(cargo.InventoryId) is not { } inventory
                || inventory.Custody != new InventoryCustody.Unclaimed(cargo.EntityId)
                || !Inventory.IsValidCargoObjectInventory(inventory))
                return new CheckpointValidationFailure(path, "Cargo identity, position, inventory contents or unclaimed custody is invalid.");
            _cargoObjects.Add(cargo.EntityId, cargo);
            _entities.ApplyAddCargoObject(cargo.EntityId);
        }
        foreach (InventoryCheckpoint inventory in checkpoint.Inventories.Inventories)
            if (inventory.Custody is InventoryCustody.Unclaimed && !inventoryIds.Contains(inventory.Id))
                return new CheckpointValidationFailure(path, "An unclaimed inventory has no registered cargo-object owner.");
        return null;
    }
}
