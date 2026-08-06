namespace GalaxyCommand.Simulation;

/// <summary>
/// Acceptance-only bridge until TASK-011 supplies production entity lifecycle.
/// </summary>
internal static class PhaseOneShipMaterializer
{
    public static ShipId Materialize(
        ConstructionMaterializationEffect materialization,
        Shipyard shipyard,
        IdSequence<ShipId> shipIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        ShipRegistry ships)
    {
        if (materialization.FacilityId != shipyard.FacilityId)
        {
            throw new InvalidOperationException(
                $"Shipyard {shipyard.FacilityId} cannot materialize an effect for facility {materialization.FacilityId}.");
        }

        if (shipyard.GetConstructedShipId(materialization.OrderId) is { } existing)
        {
            return existing;
        }

        ConstructionOrder order = shipyard.Process.GetOrder(materialization.OrderId)
            ?? throw new InvalidOperationException(
                $"Construction order {materialization.OrderId} does not exist.");
        if (shipyard.GetPendingMaterialization(materialization.OrderId) != materialization)
        {
            throw new InvalidOperationException(
                $"Construction order {materialization.OrderId} has no matching pending materialization.");
        }
        if (order.DesignId != materialization.DesignId
            || order.Design is not ShipDesign design)
        {
            throw new InvalidOperationException(
                $"Phase 1 cannot materialize construction design {materialization.DesignId}.");
        }

        ShipId shipId = shipIds.Allocate();
        InventoryId cargoInventoryId = inventoryIds.Allocate();
        inventories.Add(new Inventory(cargoInventoryId, design.CargoCapacity));
        ships.AddFreighter(new Ship(
            shipId,
            shipyard.OrganizationId,
            design.Id,
            shipyard.LocationId,
            cargoInventoryId));
        shipyard.RecordConstructedShip(order.Id, shipId);
        return shipId;
    }
}
