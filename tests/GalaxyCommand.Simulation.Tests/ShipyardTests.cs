using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShipyardTests
{
    [Fact]
    public void CompletedOrderCreatesPersistentIdleFreighter()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        OrganizationId organizationId = new IdSequence<OrganizationId>().Allocate();
        LocationId locationId = new IdSequence<LocationId>().Allocate();
        var inventoryIds = new IdSequence<InventoryId>();
        InventoryId inventoryId = inventoryIds.Allocate();
        MaterialId materialId = new IdSequence<MaterialId>().Allocate();
        var blueprint = new ShipBlueprint(
            new IdSequence<ShipBlueprintId>().Allocate(),
            new Quantity(7));
        var constructionInventory = new Inventory(inventoryId, new Quantity(10));
        constructionInventory.Add(materialId, new Quantity(4));
        var inventories = new InventoryRegistry();
        inventories.Add(constructionInventory);
        var shipyard = new Shipyard(
            facilityId,
            organizationId,
            locationId,
            inventoryId,
            new Throughput(2));
        var shipyardIds = new ShipyardIdSequences();
        ShipConstructionOrderId orderId = shipyard.Enqueue(
            shipyardIds,
            blueprint,
            [new KeyValuePair<MaterialId, Quantity>(materialId, new Quantity(4))],
            new Work(4));
        SimulationTime completesAt = Assert.IsType<SimulationTime>(shipyard.PrepareActive(
            new IdSequence<ReservationId>(),
            constructionInventory,
            SimulationTime.Zero));
        var ships = new ShipRegistry();

        ShipId shipId = Assert.IsType<ShipId>(shipyard.CompleteActive(
            new IdSequence<ShipId>(),
            inventoryIds,
            inventories,
            ships,
            completesAt));

        Ship ship = Assert.IsType<Ship>(ships.GetShip(shipId));
        Assert.Equal(organizationId, ship.OrganizationId);
        Assert.Equal(blueprint.Id, ship.BlueprintId);
        Assert.Equal(locationId, ship.LocationId);
        Assert.Null(ships.GetFreighter(shipId)?.ActiveJobId);
        Assert.Equal(
            new Quantity(7),
            inventories.Get(ship.CargoInventoryId)?.Capacity);
        ShipConstructionOrder completed = Assert.IsType<ShipConstructionOrder>(
            shipyard.GetCompletedOrder(orderId));
        Assert.Equal(ShipConstructionOrderStatus.Completed, completed.Status);
        Assert.Equal(shipId, completed.ConstructedShipId);
    }
}
