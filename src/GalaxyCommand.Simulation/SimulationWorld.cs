namespace GalaxyCommand.Simulation;

/// <summary>
/// Durable mutable state for one simulation. Scenario fixtures populate a
/// world; runtimes operate on it; the engine only advances events.
/// </summary>
public sealed class SimulationWorld
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

    public RouteGraph Navigation { get; } = new();

    public InventoryRegistry Inventories { get; } = new();

    public ShipRegistry Ships { get; } = new();

    public TransportBoard TransportBoard { get; } = new();

    public ConstructionDesignCatalog ConstructionDesigns { get; } = new();

    public IReadOnlyList<MaterialId> Materials => _materials;

    public IEnumerable<ProductionLine> ProductionFacilities => ProductionLines.Values;

    public IEnumerable<Shipyard> ShipyardFacilities => Shipyards.Values;

    internal ProductionIdSequences ProductionIds { get; } = new();

    internal TransportIdSequences TransportIds { get; } = new();

    internal ConstructionIdSequences ConstructionIds { get; } = new();

    internal IdSequence<ReservationId> ReservationIds { get; } = new();

    internal IdSequence<CapacityReservationId> CapacityReservationIds { get; } = new();

    internal IdSequence<ShipId> ShipIds { get; } = new();

    internal IdSequence<InventoryId> InventoryIds { get; } = new();

    internal IdSequence<ConstructionDesignId> ConstructionDesignIds { get; } = new();

    public LocationId AddLocation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LocationId locationId = _locationIds.Allocate();
        Navigation.AddLocation(locationId);
        LocationNames.Add(locationId, name);
        return locationId;
    }

    public MaterialId AddMaterial()
    {
        MaterialId materialId = _materialIds.Allocate();
        _materials.Add(materialId);
        return materialId;
    }

    public OrganizationId AddOrganization() => _organizationIds.Allocate();

    public FacilityId AddFacility() => _facilityIds.Allocate();

    public Inventory AddInventory(Quantity capacity)
    {
        var inventory = new Inventory(InventoryIds.Allocate(), capacity);
        Inventories.Add(inventory);
        return inventory;
    }

    public ProductionLine AddProductionLine(
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

    public void AddShipyard(Shipyard shipyard)
    {
        ArgumentNullException.ThrowIfNull(shipyard);
        Shipyards.Add(shipyard.FacilityId, shipyard);
        FacilityLocations.Add(shipyard.FacilityId, shipyard.LocationId);
    }

    public ShipDesign AddShipDesign(
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

    public ConstructionOrderId EnqueueConstruction(
        Shipyard shipyard,
        ShipDesign design)
    {
        ArgumentNullException.ThrowIfNull(shipyard);
        ArgumentNullException.ThrowIfNull(design);
        return shipyard.Enqueue(ConstructionIds, design);
    }

    public Ship AddFreighter(
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
}
