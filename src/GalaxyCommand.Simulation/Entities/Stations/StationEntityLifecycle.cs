namespace GalaxyCommand.Simulation;

public sealed partial class EntityRegistry
{
    private readonly SortedDictionary<EntityId, StationId> _stationsByEntity = new(EntityIdComparer<EntityId>.Instance);

    public StationId? GetStationId(EntityId entity) => _stationsByEntity.TryGetValue(entity, out StationId station) ? station : null;

    /// <summary>Registers a preflighted station identity in the shared entity namespace.</summary>
    internal void ApplyAddStation(EntityId entity, StationId station)
    {
        if (GetKind(entity) is not null || _stationsByEntity.ContainsValue(station))
            throw new InvalidOperationException("Station identity conflicts with a live entity.");
        _stationsByEntity.Add(entity, station);
    }

    /// <summary>Withdraws an exact station mapping after all dependent owners have prepared removal.</summary>
    internal bool ApplyRemoveStation(EntityId entity, StationId station) =>
        _stationsByEntity.TryGetValue(entity, out StationId expected) && expected == station && _stationsByEntity.Remove(entity);
}

internal sealed record PreparedStationIdentity(EntityId Entity, Inventory Inventory);

internal sealed partial class EntityLifecycleOwner
{
    /// <summary>Prepares empty, principal-controlled storage without advancing either shared allocator.</summary>
    internal PreparedStationIdentity? PrepareStationIdentity(PrincipalId principal, Quantity capacity,
        MaterialInventoryCompatibilityMap materials)
    {
        if (!_entityIds.TryPeek(out EntityId entity) || !_inventoryIds.TryPeek(out InventoryId inventory)
            || _entities.GetKind(entity) is not null || _inventories.Contains(inventory)) return null;
        return new(entity, new Inventory(inventory,
            new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(entity), principal), capacity, materials));
    }

    /// <summary>Allocates and publishes only the exact empty inventory and identity already prepared.</summary>
    internal void ApplyStationIdentity(PreparedStationIdentity prepared, StationId station)
    {
        if (!_entityIds.TryPeek(out EntityId entity) || entity != prepared.Entity
            || !_inventoryIds.TryPeek(out InventoryId inventory) || inventory != prepared.Inventory.Id
            || _entities.GetKind(entity) is not null || _inventories.Contains(inventory))
            throw new InvalidOperationException("Prepared station identity changed before commit.");
        _entityIds.Allocate();
        _inventoryIds.Allocate();
        _inventories.Add(prepared.Inventory);
        _entities.ApplyAddStation(entity, station);
    }

    /// <summary>Validates final base removal against its one empty inventory and shared entity registration.</summary>
    internal bool CanRemoveEmptyStation(StationId station, EntityId entity, Inventory inventory) =>
        _entities.GetStationId(entity) == station && ReferenceEquals(_inventories.Get(inventory.Id), inventory)
        && inventory.UsedCapacity == Quantity.Zero && !inventory.HasCommitments;

    /// <summary>Withdraws prevalidated empty station ownership without resetting allocator high-water marks.</summary>
    internal void ApplyRemoveEmptyStation(StationId station, EntityId entity, Inventory inventory)
    {
        if (!CanRemoveEmptyStation(station, entity, inventory)
            || !_inventories.ApplyRemove(inventory.Id) || !_entities.ApplyRemoveStation(entity, station))
            throw new InvalidOperationException("Prepared final station removal changed during commit.");
    }
}

internal sealed partial class EntityLifecycleOwner
{
    /// <summary>Validates restored station custody and existing allocation evidence without modifying the live registry.</summary>
    internal bool CanRestoreStationIdentity(EntityId entity, StationId station, InventoryId inventory, PrincipalId principal) =>
        entity.Value != 0 && station.Value != 0 && inventory.Value != 0 && principal.Value != 0
        && WasAllocated(entity.Value, _entityIds.CaptureCheckpoint())
        && WasAllocated(inventory.Value, _inventoryIds.CaptureCheckpoint())
        && _entities.GetKind(entity) is null
        && !_removalReceipts.ContainsKey(entity)
        && !_sensorActionReceipts.Values.Any(receipt => receipt.Result.Deployed?.EntityId == entity)
        && _inventories.Get(inventory)?.Custody is InventoryCustody.PrincipalControlled custody
        && custody.PhysicalOwner == new InventoryOwnerReference.SessionEntity(entity)
        && custody.ControllingPrincipalId == principal;

    /// <summary>Checks a retired station's shared allocator evidence while forbidding any surviving entity or inventory.</summary>
    internal bool IsRetiredStationIdentity(EntityId entity, InventoryId inventory) =>
        WasAllocated(entity.Value, _entityIds.CaptureCheckpoint()) && IsAllocatedStationInventory(inventory)
        && _entities.GetKind(entity) is null && !_inventories.Contains(inventory)
        && !_removalReceipts.ContainsKey(entity)
        && !_sensorActionReceipts.Values.Any(receipt => receipt.Result.Deployed?.EntityId == entity);

    /// <summary>Checks allocation evidence for a live or historically lost recovery destination.</summary>
    internal bool IsAllocatedStationInventory(InventoryId inventory) => WasAllocated(inventory.Value, _inventoryIds.CaptureCheckpoint());

    /// <summary>Publishes a fully validated station reference without allocating or replaying its creation.</summary>
    internal void ApplyRestoreStationIdentity(EntityId entity, StationId station) => _entities.ApplyAddStation(entity, station);
}
