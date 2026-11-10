using GalaxyCommand.Content;
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
    SystemPosition Position,
    PrincipalId? ControllingPrincipalId = null,
    StationFacilityBinding? StationBinding = null, QualifiedContentKey? StationModuleDefinitionKey = null);

/// <summary>Explicit facility link to an attached module and its shared station inventory owner.</summary>
public sealed record StationFacilityBinding
{
    /// <summary>Creates a nonzero station/entity/module link; session admission validates the live composition.</summary>
    public StationFacilityBinding(StationId stationId, EntityId stationEntityId, StationModuleId moduleId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(stationId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(stationEntityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(moduleId.Value);
        StationId = stationId;
        StationEntityId = stationEntityId;
        ModuleId = moduleId;
    }

    public StationId StationId { get; }
    public EntityId StationEntityId { get; }
    public StationModuleId ModuleId { get; }
}

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
        TransportTiming transportTiming,
        MaterialInventoryCompatibilityMap? materialCompatibility = null)
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
        ValidateStationBindings(facilityValues, materialCompatibility);
        ValidateProduction(productionValues, facilityValues);
        ValidateConstructionFacilities(constructionFacilityValues, facilityValues);
        _shipDesigns = ValidateShipDesigns(shipDesigns);
        ValidateMaterialCompatibility(
            materialCompatibility,
            inventoryValues,
            facilityValues,
            productionValues,
            _shipDesigns.Values);
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
        MaterialCompatibility = materialCompatibility;
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

    public MaterialInventoryCompatibilityMap? MaterialCompatibility { get; }

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
        var locations = new Dictionary<LocationId, EconomyFacilitySetup>();
        var inventoryIds = new HashSet<InventoryId>(inventories.Select(inventory => inventory.InventoryId));
        foreach (EconomyFacilitySetup facility in facilities)
        {
            ArgumentNullException.ThrowIfNull(facility);
            ArgumentOutOfRangeException.ThrowIfZero(facility.FacilityId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(facility.InventoryId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(facility.LogisticsLocationId.Value);
            ArgumentOutOfRangeException.ThrowIfZero(facility.Position.SystemId.Value);
            if (!facilityIds.Add(facility.FacilityId))
            {
                throw new ArgumentException(
                    $"Economy facility {facility.FacilityId} or logistics location {facility.LogisticsLocationId} is duplicated.",
                    nameof(facilities));
            }

            if (locations.TryGetValue(facility.LogisticsLocationId, out EconomyFacilitySetup? anchor)
                && (anchor.StationBinding is not { } binding || facility.StationBinding?.StationId != binding.StationId
                    || facility.StationBinding.StationEntityId != binding.StationEntityId
                    || facility.InventoryId != anchor.InventoryId || facility.Position != anchor.Position
                    || facility.ControllingPrincipalId != anchor.ControllingPrincipalId))
                throw new ArgumentException("Only explicitly bound modules of one station may share a logistics anchor.", nameof(facilities));
            locations.TryAdd(facility.LogisticsLocationId, facility);

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

    /// <summary>
    /// Requires explicit coherent composition before facilities may share station storage.
    /// Legacy unbound seeds retain their existing inventory semantics.
    /// </summary>
    private static void ValidateStationBindings(
        IReadOnlyCollection<EconomyFacilitySetup> facilities,
        MaterialInventoryCompatibilityMap? compatibility)
    {
        var stations = new Dictionary<StationId, EconomyFacilitySetup>();
        var entities = new Dictionary<EntityId, StationId>();
        var modules = new HashSet<(StationId, StationModuleId)>();
        foreach (EconomyFacilitySetup facility in facilities)
        {
            if (facility.StationBinding is not { } binding) continue;
            if (compatibility is null || facility.ControllingPrincipalId is not { Value: > 0 })
                throw new ArgumentException("Station facilities require mapped materials and an explicit controlling principal.", nameof(facilities));
            if (!modules.Add((binding.StationId, binding.ModuleId)))
                throw new ArgumentException("A station module cannot bind more than one economy facility.", nameof(facilities));
            if (entities.TryGetValue(binding.StationEntityId, out StationId station) && station != binding.StationId)
                throw new ArgumentException("A station entity cannot identify multiple stations.", nameof(facilities));
            entities[binding.StationEntityId] = binding.StationId;

            if (stations.TryGetValue(binding.StationId, out EconomyFacilitySetup? first)
                && (first.InventoryId != facility.InventoryId || first.Position != facility.Position
                    || first.ControllingPrincipalId != facility.ControllingPrincipalId
                    || first.StationBinding!.StationEntityId != binding.StationEntityId))
                throw new ArgumentException("Station facilities must share one inventory, owner, position and entity.", nameof(facilities));
            stations.TryAdd(binding.StationId, facility);
        }

        foreach (IGrouping<InventoryId, EconomyFacilitySetup> group in facilities.GroupBy(facility => facility.InventoryId))
        {
            EconomyFacilitySetup first = group.First();
            if (group.Count() == 1 || compatibility is null && group.All(facility => facility.StationBinding is null)) continue;
            if (first.StationBinding is not { } binding || group.Any(facility => facility.StationBinding?.StationId != binding.StationId))
                throw new ArgumentException("Shared mapped storage requires an explicit common station binding on every facility.", nameof(facilities));
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

    /// <summary>
    /// Requires complete explicit ownership and material coverage only when a
    /// setup opts into the generalized compatibility path.
    /// </summary>
    private static void ValidateMaterialCompatibility(
        MaterialInventoryCompatibilityMap? compatibility,
        IReadOnlyCollection<InitialInventorySetup> inventories,
        IReadOnlyCollection<EconomyFacilitySetup> facilities,
        IReadOnlyCollection<ProductionFacilitySetup> productionFacilities,
        IEnumerable<ShipDesign> shipDesigns)
    {
        if (compatibility is null)
        {
            return;
        }

        var facilityInventoryIds = new HashSet<InventoryId>();
        foreach (EconomyFacilitySetup facility in facilities)
        {
            if (facility.ControllingPrincipalId is not { Value: > 0 })
            {
                throw new ArgumentException(
                    "Mapped economy facilities require an explicit controlling principal.",
                    nameof(facilities));
            }
            facilityInventoryIds.Add(facility.InventoryId);
        }

        if (inventories.Any(inventory =>
                !facilityInventoryIds.Contains(inventory.InventoryId)))
        {
            throw new ArgumentException(
                "Every mapped economy inventory must belong to a facility or explicit station facility group.",
                nameof(inventories));
        }

        var usedMaterials = new HashSet<MaterialId>();
        foreach (InitialInventorySetup inventory in inventories)
        {
            usedMaterials.UnionWith(inventory.StoredMaterials.Keys);
        }

        foreach (ProductionFacilitySetup production in productionFacilities)
        {
            usedMaterials.UnionWith(production.Recipe.Inputs.Keys);
            usedMaterials.Add(production.Recipe.OutputMaterial);
        }

        foreach (ShipDesign design in shipDesigns)
        {
            usedMaterials.UnionWith(design.Recipe.Inputs.Keys);
        }

        if (usedMaterials.Any(materialId => compatibility.Get(materialId) is null))
        {
            throw new ArgumentException(
                "The material compatibility mapping does not cover every material used by economy setup.",
                nameof(compatibility));
        }
    }
}
