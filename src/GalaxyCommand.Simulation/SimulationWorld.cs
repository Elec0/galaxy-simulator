namespace GalaxyCommand.Simulation;

/// <summary>
/// Durable mutable state for one simulation. Scenario fixtures populate a
/// world; runtimes operate on it; the engine only advances events.
/// </summary>
internal sealed class SimulationWorld
{
    private readonly IdSequence<LocationId> _locationIds = new();
    private readonly IdSequence<MaterialId> _materialIds = new();
    private readonly IdSequence<OrganizationId> _organizationIds = new();
    private readonly IdSequence<FacilityId> _facilityIds = new();
    private readonly List<MaterialId> _materials = [];

    internal SortedDictionary<FacilityId, ProductionLine> ProductionLines { get; } =
        new(EntityIdComparer<FacilityId>.Instance);

    internal SortedDictionary<FacilityId, MaterialId> ProductionOutputs { get; } =
        new(EntityIdComparer<FacilityId>.Instance);

    internal SortedDictionary<FacilityId, LocationId> FacilityLocations { get; } =
        new(EntityIdComparer<FacilityId>.Instance);

    internal SortedDictionary<FacilityId, Shipyard> Shipyards { get; } =
        new(EntityIdComparer<FacilityId>.Instance);

    internal SortedDictionary<LocationId, string> LocationNames { get; } =
        new(EntityIdComparer<LocationId>.Instance);

    private SimulationWorld()
    {
    }

    internal RouteGraph Navigation { get; } = new();

    internal InventoryRegistry Inventories { get; } = new();

    internal ShipRegistry Ships { get; } = new();

    internal TransportBoard TransportBoard { get; } = new();

    internal ConstructionDesignCatalog ConstructionDesigns { get; } = new();

    internal IReadOnlyList<MaterialId> Materials => _materials;

    internal IEnumerable<ProductionLine> ProductionFacilities => ProductionLines.Values;

    internal IEnumerable<Shipyard> ShipyardFacilities => Shipyards.Values;

    internal ProductionIdSequences ProductionIds { get; } = new();

    internal TransportIdSequences TransportIds { get; } = new();

    internal ConstructionIdSequences ConstructionIds { get; } = new();

    internal IdSequence<ReservationId> ReservationIds { get; } = new();

    internal IdSequence<CapacityReservationId> CapacityReservationIds { get; } = new();

    internal IdSequence<ShipId> ShipIds { get; } = new();

    internal IdSequence<InventoryId> InventoryIds { get; } = new();

    internal IdSequence<ConstructionDesignId> ConstructionDesignIds { get; } = new();

    internal static Setup BeginSetup() => new(new SimulationWorld());

    private LocationId AddLocation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LocationId locationId = _locationIds.Allocate();
        Navigation.AddLocation(locationId);
        LocationNames.Add(locationId, name);
        return locationId;
    }

    private MaterialId AddMaterial()
    {
        MaterialId materialId = _materialIds.Allocate();
        _materials.Add(materialId);
        return materialId;
    }

    private OrganizationId AddOrganization() => _organizationIds.Allocate();

    private FacilityId AddFacility() => _facilityIds.Allocate();

    private Inventory AddInventory(Quantity capacity)
    {
        var inventory = new Inventory(InventoryIds.Allocate(), capacity);
        Inventories.Add(inventory);
        return inventory;
    }

    private ProductionLine AddProductionLine(
        FacilityId facilityId,
        InventoryId inventoryId,
        LocationId locationId,
        Recipe recipe,
        Throughput throughput,
        bool repeat = true)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var line = new ProductionLine(facilityId, inventoryId, throughput);
        line.Enqueue(ProductionIds, recipe, repeat);
        ProductionLines.Add(line.FacilityId, line);
        ProductionOutputs.Add(line.FacilityId, recipe.OutputMaterial);
        FacilityLocations.Add(line.FacilityId, locationId);
        return line;
    }

    private void AddShipyard(Shipyard shipyard)
    {
        ArgumentNullException.ThrowIfNull(shipyard);
        Shipyards.Add(shipyard.FacilityId, shipyard);
        FacilityLocations.Add(shipyard.FacilityId, shipyard.LocationId);
    }

    private ShipDesign AddShipDesign(
        string name,
        ConstructionRecipe recipe,
        Quantity cargoCapacity)
    {
        var design = new ShipDesign(
            ConstructionDesignIds.Allocate(),
            name,
            recipe,
            cargoCapacity);
        ConstructionDesigns.Add(design);
        return design;
    }

    private ConstructionOrderId EnqueueConstruction(
        Shipyard shipyard,
        ShipDesign design)
    {
        ArgumentNullException.ThrowIfNull(shipyard);
        ArgumentNullException.ThrowIfNull(design);
        return shipyard.Enqueue(ConstructionIds, design);
    }

    private Ship AddFreighter(
        OrganizationId organizationId,
        ShipDesign design,
        LocationId locationId)
    {
        ArgumentNullException.ThrowIfNull(design);
        Inventory cargoInventory = AddInventory(design.CargoCapacity);
        var ship = new Ship(
            ShipIds.Allocate(),
            organizationId,
            design.Id,
            locationId,
            cargoInventory.Id);
        Ships.AddFreighter(ship);
        return ship;
    }

    /// <summary>
    /// One-use capability for fixture and save-load construction. Completing
    /// setup consumes the capability and hands the world to its runtime.
    /// </summary>
    internal sealed class Setup
    {
        private SimulationWorld? _world;

        internal Setup(SimulationWorld world)
        {
            _world = world;
        }

        internal LocationId AddLocation(string name) =>
            World.AddLocation(name);

        internal (RouteId Forward, RouteId Reverse) AddBidirectionalRoutes(
            LocationId first,
            LocationId second,
            SimulationDuration duration) =>
            World.Navigation.AddBidirectionalRoutes(first, second, duration);

        internal MaterialId AddMaterial() =>
            World.AddMaterial();

        internal OrganizationId AddOrganization() =>
            World.AddOrganization();

        internal FacilityId AddFacility() =>
            World.AddFacility();

        internal Inventory AddInventory(Quantity capacity) =>
            World.AddInventory(capacity);

        internal ProductionLine AddProductionLine(
            FacilityId facilityId,
            InventoryId inventoryId,
            LocationId locationId,
            Recipe recipe,
            Throughput throughput,
            bool repeat = true) =>
            World.AddProductionLine(
                facilityId,
                inventoryId,
                locationId,
                recipe,
                throughput,
                repeat);

        internal void AddShipyard(Shipyard shipyard) =>
            World.AddShipyard(shipyard);

        internal ShipDesign AddShipDesign(
            string name,
            ConstructionRecipe recipe,
            Quantity cargoCapacity) =>
            World.AddShipDesign(name, recipe, cargoCapacity);

        internal ConstructionOrderId EnqueueConstruction(
            Shipyard shipyard,
            ShipDesign design) =>
            World.EnqueueConstruction(shipyard, design);

        internal Ship AddFreighter(
            OrganizationId organizationId,
            ShipDesign design,
            LocationId locationId) =>
            World.AddFreighter(organizationId, design, locationId);

        internal SimulationWorld Complete()
        {
            SimulationWorld world = World;
            _world = null;
            return world;
        }

        private SimulationWorld World =>
            _world
            ?? throw new InvalidOperationException(
                "Simulation world setup has already completed.");
    }
}
