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
        SimulationWorld.Setup setup = SimulationWorld.BeginSetup();

        LocationId mineLocation = setup.AddLocation("Mine");
        LocationId refineryLocation = setup.AddLocation("Refinery");
        LocationId shipyardLocation = setup.AddLocation("Shipyard");

        (RouteId mineToRefineryRoute, _) = setup.AddBidirectionalRoutes(
            mineLocation,
            refineryLocation,
            config.RouteDuration);
        setup.AddBidirectionalRoutes(
            refineryLocation,
            shipyardLocation,
            config.RouteDuration);

        MaterialId ore = setup.AddMaterial();
        MaterialId alloy = setup.AddMaterial();
        MaterialId components = setup.AddMaterial();
        OrganizationId organization = setup.AddOrganization();
        FacilityId mine = setup.AddFacility();
        FacilityId refinery = setup.AddFacility();
        FacilityId componentFactory = setup.AddFacility();
        FacilityId shipyardFacility = setup.AddFacility();
        Inventory mineInventory = setup.AddInventory(config.FacilityStorageCapacity);
        Inventory refineryInventory = setup.AddInventory(config.FacilityStorageCapacity);
        Inventory componentInventory = setup.AddInventory(config.FacilityStorageCapacity);
        Inventory shipyardInventory = setup.AddInventory(config.FacilityStorageCapacity);

        var throughput = new Throughput(1);
        setup.AddProductionLine(
            mine,
            mineInventory.Id,
            mineLocation,
            new Recipe([], ore, config.OreBatch, config.MineWork),
            throughput);
        setup.AddProductionLine(
            refinery,
            refineryInventory.Id,
            refineryLocation,
            new Recipe(
                [new KeyValuePair<MaterialId, Quantity>(ore, config.RefineryOreInput)],
                alloy,
                config.RefineryAlloyOutput,
                config.RefineryWork),
            throughput);
        setup.AddProductionLine(
            componentFactory,
            componentInventory.Id,
            shipyardLocation,
            new Recipe(
                [new KeyValuePair<MaterialId, Quantity>(alloy, config.ComponentAlloyInput)],
                components,
                config.ComponentOutput,
                config.ComponentWork),
            throughput);

        ShipDesign shipDesign = setup.AddShipDesign(
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
        setup.AddShipyard(shipyard);
        setup.EnqueueConstruction(shipyard, shipDesign);

        foreach (LocationId location in new[] { mineLocation, refineryLocation })
        {
            setup.AddFreighter(organization, shipDesign, location);
        }

        return new PhaseOneFixtureState(
            setup.Complete(),
            shipyard,
            mineToRefineryRoute,
            [ore, alloy, components]);
    }
}
