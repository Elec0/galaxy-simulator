namespace GalaxyCommand.Simulation;

internal sealed record PhaseOneFixtureState(
    SimulationWorld World,
    Shipyard Shipyard,
    ILogisticsNavigation LogisticsNavigation,
    MaterialId[] KnownMaterials);

/// <summary>
/// Builds the concrete Phase 1 acceptance world without coupling the
/// production simulation engine to that scenario.
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

        setup.AddBidirectionalRoutes(
            mineLocation,
            refineryLocation,
            config.RouteDuration);
        setup.AddBidirectionalRoutes(
            refineryLocation,
            shipyardLocation,
            config.RouteDuration);
        ILogisticsNavigation logisticsNavigation = CreateLogisticsNavigation(
            mineLocation,
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
            logisticsNavigation,
            [ore, alloy, components]);
    }

    private static HierarchicalLogisticsNavigation CreateLogisticsNavigation(
        LocationId mineLocation,
        LocationId refineryLocation,
        LocationId shipyardLocation,
        SimulationDuration connectorDuration)
    {
        SystemPosition mine = Position(1);
        SystemPosition refinery = Position(2);
        SystemPosition shipyard = Position(3);
        var topology = new ConnectorTopology(
            [
                new ConnectorEndpoint(new ConnectorEndpointId(1), mine),
                new ConnectorEndpoint(new ConnectorEndpointId(2), refinery),
                new ConnectorEndpoint(new ConnectorEndpointId(3), refinery),
                new ConnectorEndpoint(new ConnectorEndpointId(4), shipyard),
            ],
            [
                new TransitConnection(
                    new TransitConnectionId(1),
                    new ConnectorEndpointId(1),
                    new ConnectorEndpointId(2),
                    connectorDuration),
                new TransitConnection(
                    new TransitConnectionId(2),
                    new ConnectorEndpointId(2),
                    new ConnectorEndpointId(1),
                    connectorDuration),
                new TransitConnection(
                    new TransitConnectionId(3),
                    new ConnectorEndpointId(3),
                    new ConnectorEndpointId(4),
                    connectorDuration),
                new TransitConnection(
                    new TransitConnectionId(4),
                    new ConnectorEndpointId(4),
                    new ConnectorEndpointId(3),
                    connectorDuration),
            ]);
        return new HierarchicalLogisticsNavigation(
            new Dictionary<LocationId, SystemPosition>
            {
                [mineLocation] = mine,
                [refineryLocation] = refinery,
                [shipyardLocation] = shipyard,
            },
            new HierarchicalNavigationPlanner(
                topology,
                new ZeroLocalTravelTimeEstimator()));
    }

    private static SystemPosition Position(ulong systemId) =>
        new(new SystemId(systemId), new SpatialPosition());

    private sealed class ZeroLocalTravelTimeEstimator : ILocalTravelTimeEstimator
    {
        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination) => SimulationDuration.Zero;
    }
}
