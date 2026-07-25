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
        var design = new ShipDesign(
            new IdSequence<ConstructionDesignId>().Allocate(),
            "Test Freighter",
            new ConstructionRecipe(
                [new KeyValuePair<MaterialId, Quantity>(materialId, new Quantity(4))],
                new Work(4)),
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
        var constructionIds = new ConstructionIdSequences();
        ConstructionOrderId orderId = shipyard.Enqueue(constructionIds, design);
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
        Assert.Equal(design.Id, ship.DesignId);
        Assert.Equal(locationId, ship.LocationId);
        Assert.Null(ships.GetFreighter(shipId)?.ActiveJobId);
        Assert.Equal(
            new Quantity(7),
            inventories.Get(ship.CargoInventoryId)?.Capacity);
        ConstructionOrder completed = Assert.IsType<ConstructionOrder>(
            shipyard.GetCompletedOrder(orderId));
        Assert.Equal(ConstructionOrderStatus.Completed, completed.Status);
        Assert.Equal(shipId, shipyard.GetConstructedShipId(orderId));
    }

    [Fact]
    public void SameShipyardConstructsDifferentShipDesigns()
    {
        FacilityId facilityId = new IdSequence<FacilityId>().Allocate();
        OrganizationId organizationId = new IdSequence<OrganizationId>().Allocate();
        LocationId locationId = new IdSequence<LocationId>().Allocate();
        var inventoryIds = new IdSequence<InventoryId>();
        InventoryId inventoryId = inventoryIds.Allocate();
        var inventories = new InventoryRegistry();
        var constructionInventory = new Inventory(inventoryId, new Quantity(1));
        inventories.Add(constructionInventory);
        var shipyard = new Shipyard(
            facilityId,
            organizationId,
            locationId,
            inventoryId,
            new Throughput(1));
        var designIds = new IdSequence<ConstructionDesignId>();
        var constructionIds = new ConstructionIdSequences();
        var small = new ShipDesign(
            designIds.Allocate(),
            "Small Freighter",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(3));
        var large = new ShipDesign(
            designIds.Allocate(),
            "Large Freighter",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(9));
        ConstructionOrderId smallOrder = shipyard.Enqueue(constructionIds, small);
        ConstructionOrderId largeOrder = shipyard.Enqueue(constructionIds, large);
        var reservationIds = new IdSequence<ReservationId>();
        var shipIds = new IdSequence<ShipId>();
        var ships = new ShipRegistry();

        SimulationTime smallCompletesAt = Assert.IsType<SimulationTime>(
            shipyard.PrepareActive(
                reservationIds,
                constructionInventory,
                SimulationTime.Zero));
        ShipId smallShipId = Assert.IsType<ShipId>(shipyard.CompleteActive(
            shipIds,
            inventoryIds,
            inventories,
            ships,
            smallCompletesAt));
        SimulationTime largeCompletesAt = Assert.IsType<SimulationTime>(
            shipyard.PrepareActive(
                reservationIds,
                constructionInventory,
                smallCompletesAt));
        ShipId largeShipId = Assert.IsType<ShipId>(shipyard.CompleteActive(
            shipIds,
            inventoryIds,
            inventories,
            ships,
            largeCompletesAt));

        Ship smallShip = Assert.IsType<Ship>(ships.GetShip(smallShipId));
        Ship largeShip = Assert.IsType<Ship>(ships.GetShip(largeShipId));
        Assert.Equal(small.Id, smallShip.DesignId);
        Assert.Equal(large.Id, largeShip.DesignId);
        Assert.Equal(
            small.CargoCapacity,
            inventories.Get(smallShip.CargoInventoryId)?.Capacity);
        Assert.Equal(
            large.CargoCapacity,
            inventories.Get(largeShip.CargoInventoryId)?.Capacity);
        Assert.Equal(smallShipId, shipyard.GetConstructedShipId(smallOrder));
        Assert.Equal(largeShipId, shipyard.GetConstructedShipId(largeOrder));
    }
}
