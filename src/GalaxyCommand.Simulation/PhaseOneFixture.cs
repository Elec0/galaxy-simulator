namespace GalaxyCommand.Simulation;

internal sealed record PhaseOneFixtureState(
    SimulationWorld World,
    Shipyard Shipyard,
    RouteId MineToRefineryRoute,
    MaterialId[] KnownMaterials);

/// <summary>
/// Builds the concrete Phase 1 proof-of-concept world without coupling the
/// simulation engine to that scenario.
/// </summary>
internal static class PhaseOneFixture
{
    public static PhaseOneFixtureState Create(PhaseOneConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var world = new SimulationWorld();

        LocationId mineLocation = world.AddLocation("Mine");
        LocationId refineryLocation = world.AddLocation("Refinery");
        LocationId shipyardLocation = world.AddLocation("Shipyard");

        (RouteId mineToRefineryRoute, _) = world.Navigation.AddBidirectionalRoutes(
            mineLocation,
            refineryLocation,
            config.RouteDuration);
        world.Navigation.AddBidirectionalRoutes(
            refineryLocation,
            shipyardLocation,
            config.RouteDuration);

        MaterialId ore = world.AddMaterial();
        MaterialId alloy = world.AddMaterial();
        MaterialId components = world.AddMaterial();
        OrganizationId organization = world.AddOrganization();
        FacilityId mine = world.AddFacility();
        FacilityId refinery = world.AddFacility();
        FacilityId componentFactory = world.AddFacility();
        FacilityId shipyardFacility = world.AddFacility();
        Inventory mineInventory = world.AddInventory(config.FacilityStorageCapacity);
        Inventory refineryInventory = world.AddInventory(config.FacilityStorageCapacity);
        Inventory componentInventory = world.AddInventory(config.FacilityStorageCapacity);
        Inventory shipyardInventory = world.AddInventory(config.FacilityStorageCapacity);

        var throughput = new Throughput(1);
        world.AddProductionLine(
            mine,
            mineInventory.Id,
            mineLocation,
            new Recipe([], ore, config.OreBatch, config.MineWork),
            throughput);
        world.AddProductionLine(
            refinery,
            refineryInventory.Id,
            refineryLocation,
            new Recipe(
                [new KeyValuePair<MaterialId, Quantity>(ore, config.RefineryOreInput)],
                alloy,
                config.RefineryAlloyOutput,
                config.RefineryWork),
            throughput);
        world.AddProductionLine(
            componentFactory,
            componentInventory.Id,
            shipyardLocation,
            new Recipe(
                [new KeyValuePair<MaterialId, Quantity>(alloy, config.ComponentAlloyInput)],
                components,
                config.ComponentOutput,
                config.ComponentWork),
            throughput);

        ShipDesign shipDesign = world.AddShipDesign(
            "Phase 1 Freighter",
            new ConstructionRecipe(
                [new KeyValuePair<MaterialId, Quantity>(
                    components,
                    config.ShipyardComponentInput)],
                config.ShipyardWork),
            config.FreighterCargoCapacity);
        var shipyard = new Shipyard(
            shipyardFacility,
            organization,
            shipyardLocation,
            shipyardInventory.Id,
            throughput);
        world.AddShipyard(shipyard);
        world.EnqueueConstruction(shipyard, shipDesign);

        foreach (LocationId location in new[] { mineLocation, refineryLocation })
        {
            world.AddFreighter(organization, shipDesign, location);
        }

        return new PhaseOneFixtureState(
            world,
            shipyard,
            mineToRefineryRoute,
            [ore, alloy, components]);
    }
}
