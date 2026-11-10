using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record PhaseOneFixtureState(
    SimulationWorld World,
    Shipyard Shipyard,
    ILogisticsNavigation LogisticsNavigation,
    MaterialId[] KnownMaterials,
    StationRuntimeOwner StationOwner,
    EntityLifecycleOwner StationLifecycle,
    IReadOnlyList<EconomyFacilitySetup> StationFacilities);

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
        PrincipalId principal = new(1);
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition>
        {
            [ore] = Material("ore"), [alloy] = Material("alloy"), [components] = Material("components"),
        });
        Inventory mineInventory = setup.AddInventory(config.FacilityStorageCapacity, Custody(1, principal), materials);
        Inventory refineryInventory = setup.AddInventory(config.FacilityStorageCapacity, Custody(2, principal), materials);
        // Both attached services use this same physical owner, with the two prior storage budgets retained in aggregate.
        Inventory shipyardInventory = setup.AddInventory(config.FacilityStorageCapacity.Add(config.FacilityStorageCapacity), Custody(3, principal), materials);

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
            shipyardInventory.Id,
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
            config.FreighterCargoCapacity,
            new ShipManeuverCapability(
                baseMassKilograms: 10_000,
                ManeuverAcceleration.ParseMetersPerSecondSquared("10"),
                customPassiveDeceleration: null,
                ManeuverSpeed.ParseMetersPerSecond("300"),
                ManeuverSpeed.ParseMetersPerSecond("1000"),
                ManeuverTurnRate.ParseDegreesPerSecond("45"),
                new SimulationDuration(10_000)), sensorRadiusUnits: 10_000, pickupRangeUnits: 5_000);
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

        SimulationWorld world = setup.Complete();
        (StationRuntimeOwner stationOwner, EntityLifecycleOwner stationLifecycle, IReadOnlyList<EconomyFacilitySetup> bindings) =
            CreateStationAuthority(principal, materials, world, shipDesign,
                [(mine, mineInventory, mineLocation), (refinery, refineryInventory, refineryLocation),
                    (componentFactory, shipyardInventory, shipyardLocation), (shipyardFacility, shipyardInventory, shipyardLocation)]);
        return new PhaseOneFixtureState(
            world,
            shipyard,
            logisticsNavigation,
            [ore, alloy, components], stationOwner, stationLifecycle, bindings);
    }

    private static PhysicalDefinition Material(string name) => new(
        QualifiedContentKey.Create("test.phase-one", "material", name), PhysicalHoldingKind.Fungible, new(1));

    private static InventoryCustody.PrincipalControlled Custody(ulong stationEntity, PrincipalId principal) =>
        new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(new(stationEntity)), principal);

    /// <summary>
    /// Registers actual economic inventories with station authority and explicit module/facility links.
    /// The canonical scenario continues to run the same economic workloads; these authored origins do not replay construction.
    /// </summary>
    private static (StationRuntimeOwner Owner, EntityLifecycleOwner Lifecycle, IReadOnlyList<EconomyFacilitySetup> Bindings)
        CreateStationAuthority(PrincipalId principal, MaterialInventoryCompatibilityMap materials, SimulationWorld world, ShipDesign shipDesign,
            IReadOnlyList<(FacilityId Facility, Inventory Inventory, LocationId Location)> facilities)
    {
        StationModuleDefinition[] modules = facilities.Select(facility => new StationModuleDefinition(
            QualifiedContentKey.Create("test.phase-one", "station-module", $"facility-{facility.Facility.Value}"),
            $"Facility {facility.Facility.Value}", 1, Quantity.Zero, 0, null,
            new([], new(1)), new(1), [], [], productionProvider:
                world.ProductionLines.TryGetValue(facility.Facility, out ProductionLine? line)
                    ? new StationProductionProvider(line.ActiveJob!.Recipe, line.Throughput, line.ActiveJob.IsRepeating) : null,
                shipConstructionProvider: world.Shipyards.TryGetValue(facility.Facility, out Shipyard? shipyard)
                    ? new StationShipConstructionProvider([shipDesign], shipyard.Process.Throughput,
                        new(ActorControllerKind.Autonomous, new("phase-one-organization")), InitialShipOrderPolicy.NoInitialOrder) : null)).ToArray();
        var definitions = new List<StationDefinition>();
        var initial = new List<InitialStationSetup>();
        var bindings = new List<EconomyFacilitySetup>();
        var lifecycle = new EntityLifecycleOwner(new(), new(), new(), [], materials);
        foreach (IGrouping<LocationId, (FacilityId Facility, Inventory Inventory, LocationId Location)> group in facilities.GroupBy(facility => facility.Location))
        {
            Inventory inventory = group.First().Inventory;
            if (group.Any(facility => !ReferenceEquals(facility.Inventory, inventory)))
                throw new InvalidOperationException("One station's facilities must share the actual economic inventory instance.");
            ulong id = group.Key.Value;
            var key = QualifiedContentKey.Create("test.phase-one", "station", $"location-{id}");
            InitialStationModuleSetup[] attached = group.Select(facility => new InitialStationModuleSetup(
                new(facility.Facility.Value), modules.Single(module => module.Key.LocalId.Value == $"facility-{facility.Facility.Value}").Key,
                true, facility.Facility)).ToArray();
            definitions.Add(new(key, $"Phase 1 Station {id}", (ulong)attached.Length, Quantity.Zero,
                inventory.Capacity, new([], new(1)), 0, attached.Select(module => module.DefinitionKey), new(1), []));
            initial.Add(new(new(id), new(id), inventory.Id, principal, key, Position(id), attached, SimulationTime.Zero));
            lifecycle.RegisterEconomyInventory(inventory);
            foreach (var facility in group)
                bindings.Add(new(facility.Facility, inventory.Id, facility.Location, Position(id), principal,
                    new(new(id), new(id), new(facility.Facility.Value)),
                    modules.Single(module => module.Key.LocalId.Value == $"facility-{facility.Facility.Value}").Key));
        }
        var catalog = new StationDefinitionCatalog(definitions, modules);
        catalog.ValidateForSession(materials, [new(1), new(2), new(3)]);
        var setup = new StationRuntimeSetup(catalog, materials, StationTerritoryPolicy.Absent, initialStations: initial);
        return (new StationRuntimeOwner(setup, lifecycle, new(), (_, _) => null), lifecycle,
            Array.AsReadOnly(bindings.OrderBy(binding => binding.FacilityId.Value).ToArray()));
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
