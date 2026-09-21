using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record EntityLifecycleShipCheckpoint(
    EntityId EntityId,
    ShipId ShipId,
    PrincipalId PrincipalId,
    ConstructionDesignId DesignId,
    InventoryId CargoInventoryId,
    ShipManeuverCapabilityRevision ManeuverCapabilityRevision)
{
    internal EntityLifecycleShipCheckpoint(
        EntityId entityId,
        ShipId shipId,
        PrincipalId principalId,
        ConstructionDesignId designId,
        InventoryId cargoInventoryId)
        : this(
            entityId,
            shipId,
            principalId,
            designId,
            cargoInventoryId,
            ShipManeuverCapabilityRevision.Initial)
    {
    }
}

internal sealed record EntityMaterializationReceiptCheckpoint(
    ConstructionMaterializationEffect? Effect,
    EntityId EntityId,
    ShipId ShipId,
    InventoryId CargoInventoryId);

internal sealed record EntityRemovalReceiptCheckpoint(
    EntityRemovalRequest? Request,
    ShipId ShipId,
    InventoryId CargoInventoryId);

internal sealed class EntityLifecycleCheckpoint
{
    internal EntityLifecycleCheckpoint(
        IdSequenceCheckpoint entityIds,
        IdSequenceCheckpoint shipIds,
        IdSequenceCheckpoint inventoryIds,
        InventoryRegistryCheckpoint inventories,
        IEnumerable<EntityLifecycleShipCheckpoint?> liveShips,
        IEnumerable<EntityMaterializationReceiptCheckpoint?> materializationReceipts,
        IEnumerable<EntityRemovalReceiptCheckpoint?> removalReceipts)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        ArgumentNullException.ThrowIfNull(shipIds);
        ArgumentNullException.ThrowIfNull(inventoryIds);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(liveShips);
        ArgumentNullException.ThrowIfNull(materializationReceipts);
        ArgumentNullException.ThrowIfNull(removalReceipts);
        EntityIds = entityIds;
        ShipIds = shipIds;
        InventoryIds = inventoryIds;
        Inventories = inventories;
        LiveShips = new ReadOnlyCollection<EntityLifecycleShipCheckpoint?>(
            liveShips.ToArray());
        MaterializationReceipts =
            new ReadOnlyCollection<EntityMaterializationReceiptCheckpoint?>(
                materializationReceipts.ToArray());
        RemovalReceipts =
            new ReadOnlyCollection<EntityRemovalReceiptCheckpoint?>(
                removalReceipts.ToArray());
    }

    internal IdSequenceCheckpoint EntityIds { get; }

    internal IdSequenceCheckpoint ShipIds { get; }

    internal IdSequenceCheckpoint InventoryIds { get; }

    internal InventoryRegistryCheckpoint Inventories { get; }

    internal ReadOnlyCollection<EntityLifecycleShipCheckpoint?> LiveShips { get; }

    internal ReadOnlyCollection<EntityMaterializationReceiptCheckpoint?>
        MaterializationReceipts
    { get; }

    internal ReadOnlyCollection<EntityRemovalReceiptCheckpoint?> RemovalReceipts { get; }
}

