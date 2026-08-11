using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable initial contents for one economy-owned inventory.
/// </summary>
public sealed class InitialInventorySetup
{
    private readonly ReadOnlyDictionary<MaterialId, Quantity> _storedMaterials;

    /// <summary>
    /// Creates one capacity-limited inventory seed with its initially stored materials.
    /// </summary>
    public InitialInventorySetup(
        InventoryId inventoryId,
        Quantity capacity,
        IEnumerable<KeyValuePair<MaterialId, Quantity>> storedMaterials)
    {
        ArgumentOutOfRangeException.ThrowIfZero(inventoryId.Value);
        ArgumentNullException.ThrowIfNull(storedMaterials);

        var values = new SortedDictionary<MaterialId, Quantity>(
            EntityIdComparer<MaterialId>.Instance);
        Quantity total = Quantity.Zero;
        foreach ((MaterialId materialId, Quantity quantity) in storedMaterials)
        {
            if (quantity == Quantity.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storedMaterials),
                    "Initial material quantities must be positive.");
            }

            if (!values.TryAdd(materialId, quantity))
            {
                throw new ArgumentException(
                    $"Inventory {inventoryId} has duplicate material {materialId}.",
                    nameof(storedMaterials));
            }

            total = total.Add(quantity);
        }

        if (total > capacity)
        {
            throw new ArgumentException(
                $"Inventory {inventoryId} stores {total.Units} units beyond its {capacity.Units} unit capacity.",
                nameof(storedMaterials));
        }

        InventoryId = inventoryId;
        Capacity = capacity;
        _storedMaterials = new ReadOnlyDictionary<MaterialId, Quantity>(values);
    }

    public InventoryId InventoryId { get; }

    public Quantity Capacity { get; }

    public IReadOnlyDictionary<MaterialId, Quantity> StoredMaterials => _storedMaterials;
}

/// <summary>
/// Stable anchor that lets the economy refer to a facility without exposing a
/// route graph or making the facility a runtime entity.
/// </summary>
public sealed record EconomyFacilitySetup(
    FacilityId FacilityId,
    InventoryId InventoryId,
    LocationId LogisticsLocationId,
    SystemPosition Position);

/// <summary>
/// Initial recurring production behavior for one facility.
/// </summary>
public sealed record ProductionFacilitySetup(
    FacilityId FacilityId,
    Recipe Recipe,
    Throughput Throughput,
    bool Repeat);

/// <summary>
/// Initial ship-construction capability for one facility.
/// </summary>
public sealed record ConstructionFacilitySetup(
    FacilityId FacilityId,
    Throughput Throughput);

/// <summary>
/// One initial construction request. Runtime order identities are allocated by
/// the session-owned construction owner during setup.
/// </summary>
public sealed record InitialConstructionOrderSetup(
    FacilityId FacilityId,
    ConstructionDesignId DesignId);

/// <summary>
/// Initial transport capability for a session ship. It establishes only the
/// logistics anchor; it does not assign a job or create a movement order.
/// </summary>
public sealed record InitialFreighterSetup(
    ShipId ShipId,
    LocationId LogisticsLocationId);

/// <summary>
/// Generic, immutable seed for economy, construction, and transport owners
/// that belong to a new game session.
/// </summary>
public sealed class GameSessionEconomySetup
{
    private readonly ReadOnlyCollection<InitialInventorySetup> _inventories;
    private readonly ReadOnlyCollection<EconomyFacilitySetup> _facilities;
    private readonly ReadOnlyCollection<ProductionFacilitySetup> _productionFacilities;
    private readonly ReadOnlyCollection<ConstructionFacilitySetup> _constructionFacilities;
    private readonly ReadOnlyCollection<InitialConstructionOrderSetup> _constructionOrders;
    private readonly ReadOnlyCollection<InitialFreighterSetup> _freighters;
    private readonly ReadOnlyDictionary<ConstructionDesignId, ShipDesign> _shipDesigns;

    /// <summary>
    /// Validates the complete new-game seed for session-owned economic work.
    /// </summary>
    public GameSessionEconomySetup(
        IEnumerable<InitialInventorySetup> inventories,
        IEnumerable<EconomyFacilitySetup> facilities,
        IEnumerable<ProductionFacilitySetup> productionFacilities,
        IEnumerable<ConstructionFacilitySetup> constructionFacilities,
        IEnumerable<ShipDesign> shipDesigns,
        IEnumerable<InitialConstructionOrderSetup> constructionOrders,
        IEnumerable<InitialFreighterSetup> freighters,
        ILogisticsNavigation navigation,
        TransportTiming transportTiming)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(facilities);
        ArgumentNullException.ThrowIfNull(productionFacilities);
        ArgumentNullException.ThrowIfNull(constructionFacilities);
        ArgumentNullException.ThrowIfNull(shipDesigns);
        ArgumentNullException.ThrowIfNull(constructionOrders);
        ArgumentNullException.ThrowIfNull(freighters);
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        TransportTiming = transportTiming;

        InitialInventorySetup[] inventoryValues = inventories.ToArray();
        EconomyFacilitySetup[] facilityValues = facilities.ToArray();
        ProductionFacilitySetup[] productionValues = productionFacilities.ToArray();
        ConstructionFacilitySetup[] constructionFacilityValues = constructionFacilities.ToArray();
        InitialConstructionOrderSetup[] constructionValues = constructionOrders.ToArray();
        InitialFreighterSetup[] freighterValues = freighters.ToArray();

        ValidateInventories(inventoryValues);
        ValidateFacilities(facilityValues, inventoryValues);
        ValidateProduction(productionValues, facilityValues);
        ValidateConstructionFacilities(constructionFacilityValues, facilityValues);
        _shipDesigns = ValidateShipDesigns(shipDesigns);
        ValidateConstruction(
            constructionValues,
            constructionFacilityValues,
            _shipDesigns);
        ValidateFreighters(freighterValues, facilityValues);

        _inventories = new ReadOnlyCollection<InitialInventorySetup>(inventoryValues);
        _facilities = new ReadOnlyCollection<EconomyFacilitySetup>(facilityValues);
        _productionFacilities = new ReadOnlyCollection<ProductionFacilitySetup>(productionValues);
        _constructionFacilities = new ReadOnlyCollection<ConstructionFacilitySetup>(constructionFacilityValues);
        _constructionOrders = new ReadOnlyCollection<InitialConstructionOrderSetup>(constructionValues);
        _freighters = new ReadOnlyCollection<InitialFreighterSetup>(freighterValues);
    }

    public IReadOnlyList<InitialInventorySetup> Inventories => _inventories;

    public IReadOnlyList<EconomyFacilitySetup> Facilities => _facilities;

    public IReadOnlyList<ProductionFacilitySetup> ProductionFacilities => _productionFacilities;

    public IReadOnlyList<ConstructionFacilitySetup> ConstructionFacilities => _constructionFacilities;

    public IReadOnlyDictionary<ConstructionDesignId, ShipDesign> ShipDesigns => _shipDesigns;

    public IReadOnlyList<InitialConstructionOrderSetup> ConstructionOrders => _constructionOrders;

    public IReadOnlyList<InitialFreighterSetup> Freighters => _freighters;

    public ILogisticsNavigation Navigation { get; }

    public TransportTiming TransportTiming { get; }

    private static void ValidateInventories(IEnumerable<InitialInventorySetup> inventories)
    {
        var ids = new HashSet<InventoryId>();
        foreach (InitialInventorySetup inventory in inventories)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            if (!ids.Add(inventory.InventoryId))
            {
                throw new ArgumentException(
                    $"Duplicate economy inventory {inventory.InventoryId}.",
                    nameof(inventories));
            }
        }
    }

    private static void ValidateFacilities(
        IEnumerable<EconomyFacilitySetup> facilities,
        IEnumerable<InitialInventorySetup> inventories)
    {
        var facilityIds = new HashSet<FacilityId>();
        var locationIds = new HashSet<LocationId>();
        var inventoryIds = new HashSet<InventoryId>(inventories.Select(inventory => inventory.InventoryId));
        foreach (EconomyFacilitySetup facility in facilities)
        {
            ArgumentNullException.ThrowIfNull(facility);
            ArgumentOutOfRangeException.ThrowIfZero(facility.FacilityId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(facility.InventoryId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(facility.LogisticsLocationId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(facility.Position.SystemId.Value);
            if (!facilityIds.Add(facility.FacilityId)
                || !locationIds.Add(facility.LogisticsLocationId))
            {
                throw new ArgumentException(
                    $"Economy facility {facility.FacilityId} or logistics location {facility.LogisticsLocationId} is duplicated.",
                    nameof(facilities));
            }

            if (!inventoryIds.Contains(facility.InventoryId))
            {
                throw new ArgumentException(
                    $"Economy facility {facility.FacilityId} references unknown inventory {facility.InventoryId}.",
                    nameof(facilities));
            }
        }
    }

    private static void ValidateProduction(
        IEnumerable<ProductionFacilitySetup> productionFacilities,
        IEnumerable<EconomyFacilitySetup> facilities)
    {
        var facilityIds = new HashSet<FacilityId>(facilities.Select(facility => facility.FacilityId));
        var configured = new HashSet<FacilityId>();
        foreach (ProductionFacilitySetup production in productionFacilities)
        {
            ArgumentNullException.ThrowIfNull(production);
            ArgumentNullException.ThrowIfNull(production.Recipe);
            if (!facilityIds.Contains(production.FacilityId) || !configured.Add(production.FacilityId))
            {
                throw new ArgumentException(
                    $"Production configuration for facility {production.FacilityId} is missing or duplicated.",
                    nameof(productionFacilities));
            }
        }
    }

    private static ReadOnlyDictionary<ConstructionDesignId, ShipDesign> ValidateShipDesigns(
        IEnumerable<ShipDesign> shipDesigns)
    {
        var designs = new SortedDictionary<ConstructionDesignId, ShipDesign>(
            EntityIdComparer<ConstructionDesignId>.Instance);
        foreach (ShipDesign design in shipDesigns)
        {
            ArgumentNullException.ThrowIfNull(design);
            if (!designs.TryAdd(design.Id, design))
            {
                throw new ArgumentException(
                    $"Duplicate ship design {design.Id}.",
                    nameof(shipDesigns));
            }
        }

        return new ReadOnlyDictionary<ConstructionDesignId, ShipDesign>(designs);
    }

    private static void ValidateConstructionFacilities(
        IEnumerable<ConstructionFacilitySetup> constructionFacilities,
        IEnumerable<EconomyFacilitySetup> facilities)
    {
        var facilityIds = new HashSet<FacilityId>(facilities.Select(facility => facility.FacilityId));
        var configured = new HashSet<FacilityId>();
        foreach (ConstructionFacilitySetup construction in constructionFacilities)
        {
            ArgumentNullException.ThrowIfNull(construction);
            if (!facilityIds.Contains(construction.FacilityId)
                || !configured.Add(construction.FacilityId))
            {
                throw new ArgumentException(
                    $"Construction configuration for facility {construction.FacilityId} is missing or duplicated.",
                    nameof(constructionFacilities));
            }
        }
    }

    private static void ValidateConstruction(
        IEnumerable<InitialConstructionOrderSetup> constructionOrders,
        IEnumerable<ConstructionFacilitySetup> constructionFacilities,
        ReadOnlyDictionary<ConstructionDesignId, ShipDesign> shipDesigns)
    {
        var facilityIds = new HashSet<FacilityId>(constructionFacilities.Select(facility => facility.FacilityId));
        foreach (InitialConstructionOrderSetup order in constructionOrders)
        {
            ArgumentNullException.ThrowIfNull(order);
            if (!facilityIds.Contains(order.FacilityId)
                || !shipDesigns.ContainsKey(order.DesignId))
            {
                throw new ArgumentException(
                    $"Construction order for facility {order.FacilityId} references an unknown facility or ship design {order.DesignId}.",
                    nameof(constructionOrders));
            }
        }
    }

    private static void ValidateFreighters(
        IEnumerable<InitialFreighterSetup> freighters,
        IEnumerable<EconomyFacilitySetup> facilities)
    {
        var locationIds = new HashSet<LocationId>(facilities.Select(facility => facility.LogisticsLocationId));
        var shipIds = new HashSet<ShipId>();
        foreach (InitialFreighterSetup freighter in freighters)
        {
            ArgumentNullException.ThrowIfNull(freighter);
            ArgumentOutOfRangeException.ThrowIfZero(freighter.ShipId.Value);
            if (!shipIds.Add(freighter.ShipId)
                || !locationIds.Contains(freighter.LogisticsLocationId))
            {
                throw new ArgumentException(
                    $"Freighter {freighter.ShipId} is duplicated or references unknown logistics location {freighter.LogisticsLocationId}.",
                    nameof(freighters));
            }
        }
    }
}
