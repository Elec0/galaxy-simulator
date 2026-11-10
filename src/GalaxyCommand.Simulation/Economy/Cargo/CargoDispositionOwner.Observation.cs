namespace GalaxyCommand.Simulation;

internal sealed partial class CargoDispositionOwner
{
    /// <summary>Captures current inventory contents for one live object without asserting player visibility.</summary>
    internal CargoObjectView? CaptureObjectView(EntityId entityId)
    {
        ThrowIfUnhealthy();
        if (!_objects.TryGetValue(entityId, out CargoLiveObject? cargo)) return null;
        CargoObjectSnapshot physical = _lifecycle.GetCargoObject(entityId)
            ?? throw new InvalidOperationException("Live cargo metadata requires its physical object.");
        Inventory inventory = _lifecycle.Inventories.Get(physical.InventoryId)
            ?? throw new InvalidOperationException("Live cargo requires its authoritative inventory.");
        if (!Inventory.IsValidCargoObjectInventory(inventory))
            throw new InvalidOperationException("Live cargo contents must have positive single-definition shape.");
        InventoryCheckpoint contents = inventory.CaptureCheckpoint();
        DiscreteItemInstance? item = contents.DiscreteItems.Count == 1
            ? inventory.GetDiscrete(contents.DiscreteItems[0].Id) : null;
        var key = item?.DefinitionKey ?? contents.FungibleHoldings[0].DefinitionKey;
        PhysicalDefinition definition = _definitions.Get(key)
            ?? throw new InvalidOperationException("Live cargo requires its exact physical definition.");
        Quantity quantity = item is null ? contents.FungibleHoldings[0].Quantity : new(1);
        return new(entityId, physical.Position, definition.Key, definition.HoldingKind,
            quantity, definition.CapacityCost, inventory.UsedCapacity, item, cargo.OriginalFieldId);
    }

    /// <summary>Captures current authoritative contents in stable identity order; callers must apply coverage before publication.</summary>
    internal IReadOnlyList<CargoObjectView> CaptureObjectViews()
    {
        ThrowIfUnhealthy();
        return Array.AsReadOnly(_objects.Keys.Select(entityId => CaptureObjectView(entityId)!).ToArray());
    }
}
