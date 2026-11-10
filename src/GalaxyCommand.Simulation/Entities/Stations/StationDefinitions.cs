using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Explicit stationary construction range and work supply; no ship defaults are inferred.</summary>
public sealed record StationConstructionCapability
{
    /// <summary>Admits zero range and positive work supply in whole work units per second.</summary>
    public StationConstructionCapability(ulong rangeUnits, Throughput throughput)
    {
        ArgumentOutOfRangeException.ThrowIfZero(throughput.UnitsPerSecond);
        RangeUnits = rangeUnits;
        Throughput = throughput;
    }

    public ulong RangeUnits { get; }
    public Throughput Throughput { get; }
}

/// <summary>Immutable authored base contract. The content key identifies localized naming resources.</summary>
public sealed class StationDefinition
{
    /// <summary>Copies explicit base inputs and validates local recipe, storage and recovery invariants.</summary>
    public StationDefinition(QualifiedContentKey key, string invariantFallback, ulong moduleCapacity,
        Quantity stagingCapacity, Quantity baseCapacity, ConstructionRecipe baseRecipe, ulong sensorRadiusUnits,
        IEnumerable<QualifiedContentKey> allowedModules, Work dismantleWork,
        IEnumerable<KeyValuePair<MaterialId, Quantity>> dismantleRecovery, StationPlacementRules? placementRules = null)
    {
        StationDefinitionValidation.ValidateIdentity(key, "station", invariantFallback);
        if (baseCapacity.Units < stagingCapacity.Units)
            throw new ArgumentException("Completed base capacity cannot shrink site staging.", nameof(baseCapacity));
        StationDefinitionValidation.ValidateRecipe(baseRecipe);
        ArgumentOutOfRangeException.ThrowIfZero(dismantleWork.Units);
        Key = key;
        InvariantFallback = invariantFallback;
        ModuleCapacity = moduleCapacity;
        StagingCapacity = stagingCapacity;
        BaseCapacity = baseCapacity;
        BaseRecipe = baseRecipe;
        PlacementRules = placementRules ?? new StationPlacementRules([], [], []);
        SensorRadiusUnits = sensorRadiusUnits;
        AllowedModules = StationDefinitionValidation.CopyReferences(allowedModules, "station-module");
        DismantleWork = dismantleWork;
        DismantleRecovery = StationDefinitionValidation.CopyRecovery(dismantleRecovery);
    }

    public QualifiedContentKey Key { get; }
    public string InvariantFallback { get; }
    public string Name => InvariantFallback;
    public ulong ModuleCapacity { get; }
    public Quantity StagingCapacity { get; }
    public Quantity BaseCapacity { get; }
    public ConstructionRecipe BaseRecipe { get; }
    public StationPlacementRules PlacementRules { get; }
    public ulong SensorRadiusUnits { get; }
    public IReadOnlyList<QualifiedContentKey> AllowedModules { get; }
    public Work DismantleWork { get; }
    /// <summary>Explicit authored recovery for a completed structure; unfinished recovery needs consumed-input validation.</summary>
    public IReadOnlyDictionary<MaterialId, Quantity> DismantleRecovery { get; }
}

/// <summary>Explicit authored work and materials for one permitted module replacement.</summary>
public sealed record StationUpgradePath(QualifiedContentKey TargetKey, ConstructionRecipe Recipe);

/// <summary>Immutable attached-module composition, capability and structural-work contract.</summary>
public sealed class StationModuleDefinition
{
    /// <summary>Copies explicit capabilities, recovery and upgrade references without activating any service provider.</summary>
    public StationModuleDefinition(QualifiedContentKey key, string invariantFallback, ulong capacityCost,
        Quantity storageCapacity, ulong sensorRadiusUnits, StationConstructionCapability? constructionCapability,
        ConstructionRecipe constructionRecipe, Work dismantleWork,
        IEnumerable<KeyValuePair<MaterialId, Quantity>> dismantleRecovery,
        IEnumerable<QualifiedContentKey> allowedUpgrades,
        IEnumerable<StationUpgradePath>? upgradePaths = null,
        StationProductionProvider? productionProvider = null,
        StationShipConstructionProvider? shipConstructionProvider = null)
    {
        StationDefinitionValidation.ValidateIdentity(key, "station-module", invariantFallback);
        StationDefinitionValidation.ValidateRecipe(constructionRecipe);
        ArgumentOutOfRangeException.ThrowIfZero(dismantleWork.Units);
        Key = key;
        InvariantFallback = invariantFallback;
        CapacityCost = capacityCost;
        StorageCapacity = storageCapacity;
        SensorRadiusUnits = sensorRadiusUnits;
        ConstructionCapability = constructionCapability;
        ConstructionRecipe = constructionRecipe;
        DismantleWork = dismantleWork;
        DismantleRecovery = StationDefinitionValidation.CopyRecovery(dismantleRecovery);
        StationUpgradePath[] paths = (upgradePaths ?? []).ToArray();
        ProductionProvider = productionProvider;
        ShipConstructionProvider = shipConstructionProvider;
        foreach (StationUpgradePath path in paths)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (path.TargetKey is null || path.TargetKey.ContentKind.Value != "station-module")
                throw new ArgumentException("Upgrade paths require an explicit module target.", nameof(upgradePaths));
            StationDefinitionValidation.ValidateRecipe(path.Recipe);
        }
        if (paths.Select(path => path.TargetKey).Distinct().Count() != paths.Length)
            throw new ArgumentException("Upgrade targets must be unique.", nameof(upgradePaths));
        UpgradePaths = Array.AsReadOnly(paths.OrderBy(path => path.TargetKey.ToString(), StringComparer.Ordinal).ToArray());
        QualifiedContentKey[] references = allowedUpgrades.ToArray();
        AllowedUpgrades = StationDefinitionValidation.CopyReferences(references.Length == 0
            ? paths.Select(path => path.TargetKey) : references, "station-module");
        if (AllowedUpgrades.Contains(key))
            throw new ArgumentException("A module cannot upgrade to its own definition.", nameof(allowedUpgrades));
    }

    public QualifiedContentKey Key { get; }
    public string InvariantFallback { get; }
    public string Name => InvariantFallback;
    public ulong CapacityCost { get; }
    public Quantity StorageCapacity { get; }
    public ulong SensorRadiusUnits { get; }
    public StationConstructionCapability? ConstructionCapability { get; }
    public ConstructionRecipe ConstructionRecipe { get; }
    public Work DismantleWork { get; }
    public IReadOnlyDictionary<MaterialId, Quantity> DismantleRecovery { get; }
    public IReadOnlyList<QualifiedContentKey> AllowedUpgrades { get; }
    public IReadOnlyList<StationUpgradePath> UpgradePaths { get; }
    public StationProductionProvider? ProductionProvider { get; }
    public StationShipConstructionProvider? ShipConstructionProvider { get; }
}

/// <summary>Resolved station content with canonical enumeration and checked cross-definition references.</summary>
public sealed class StationDefinitionCatalog
{
    /// <summary>Rejects duplicate definitions and unresolved allowed modules or upgrade targets before publication.</summary>
    public StationDefinitionCatalog(IEnumerable<StationDefinition> stations, IEnumerable<StationModuleDefinition> modules)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(modules);
        StationDefinition[] bases = stations.ToArray();
        StationModuleDefinition[] attached = modules.ToArray();
        foreach (StationDefinition station in bases) ArgumentNullException.ThrowIfNull(station);
        foreach (StationModuleDefinition module in attached) ArgumentNullException.ThrowIfNull(module);
        if (bases.Select(value => value.Key).Distinct().Count() != bases.Length
            || attached.Select(value => value.Key).Distinct().Count() != attached.Length)
            throw new ArgumentException("Station content keys must be unique.");
        var moduleKeys = attached.Select(value => value.Key).ToHashSet();
        if (bases.Any(value => value.AllowedModules.Any(key => !moduleKeys.Contains(key)))
            || attached.Any(value => value.AllowedUpgrades.Any(key => !moduleKeys.Contains(key))))
            throw new ArgumentException("Station module and upgrade references must resolve.");
        if (attached.Any(module => !module.AllowedUpgrades.OrderBy(key => key.ToString(), StringComparer.Ordinal)
                .SequenceEqual(module.UpgradePaths.Select(path => path.TargetKey))))
            throw new ArgumentException("Every permitted upgrade requires its own explicit authored recipe.");
        Stations = Array.AsReadOnly(bases.OrderBy(value => value.Key.ToString(), StringComparer.Ordinal).ToArray());
        Modules = Array.AsReadOnly(attached.OrderBy(value => value.Key.ToString(), StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Resolves all material references and verifies each site's staging increment against explicit session content.
    /// System references are checked before any station can be published.
    /// </summary>
    public void ValidateForSession(MaterialInventoryCompatibilityMap materials, IEnumerable<SystemId> systemIds)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(systemIds);
        var systems = systemIds.ToHashSet();
        foreach (SystemId system in systems) ArgumentOutOfRangeException.ThrowIfZero(system.Value);
        foreach (StationDefinition station in Stations)
        {
            station.PlacementRules.ValidateSystems(systems);
            StationConstructionAdmission.ValidateBaseRecipe(station.BaseRecipe, materials,
                station.StagingCapacity, station.BaseCapacity);
            ValidateMaterials(station.DismantleRecovery.Keys, materials);
        }
        foreach (StationModuleDefinition module in Modules)
        {
            ValidateMaterials(module.ConstructionRecipe.Inputs.Keys, materials);
            ValidateMaterials(module.DismantleRecovery.Keys, materials);
            if (module.ProductionProvider is { } production)
                ValidateMaterials(production.Recipe.Inputs.Keys.Append(production.Recipe.OutputMaterial), materials);
            foreach (ShipDesign design in module.ShipConstructionProvider?.AllowedDesigns ?? [])
                ValidateMaterials(design.Recipe.Inputs.Keys, materials);
            foreach (StationUpgradePath path in module.UpgradePaths)
                ValidateMaterials(path.Recipe.Inputs.Keys, materials);
        }
    }

    /// <summary>Rejects unresolved input or recovery material references rather than inventing a physical definition.</summary>
    private static void ValidateMaterials(IEnumerable<MaterialId> materialIds, MaterialInventoryCompatibilityMap materials)
    {
        foreach (MaterialId material in materialIds)
            if (materials.Get(material) is null)
                throw new ArgumentException($"Station material {material} is unresolved.", nameof(materials));
    }

    public IReadOnlyList<StationDefinition> Stations { get; }
    public IReadOnlyList<StationModuleDefinition> Modules { get; }
}

internal static class StationDefinitionValidation
{
    /// <summary>Checks category and required invariant naming without consulting presentation locale.</summary>
    internal static void ValidateIdentity(QualifiedContentKey key, string kind, string invariantFallback)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(invariantFallback);
        if (key.ContentKind.Value != kind) throw new ArgumentException("Station definition content kind is invalid.", nameof(key));
    }

    /// <summary>Checks positive per-material inputs, work, and checked aggregate recipe quantity.</summary>
    internal static void ValidateRecipe(ConstructionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfZero(recipe.RequiredWork.Units);
        ulong total = 0;
        foreach ((MaterialId material, Quantity amount) in recipe.Inputs)
        {
            ArgumentOutOfRangeException.ThrowIfZero(material.Value);
            ArgumentOutOfRangeException.ThrowIfZero(amount.Units);
            total = checked(total + amount.Units);
        }
    }

    /// <summary>Copies unique correctly categorized references in content-key order.</summary>
    internal static IReadOnlyList<QualifiedContentKey> CopyReferences(IEnumerable<QualifiedContentKey> references, string kind)
    {
        ArgumentNullException.ThrowIfNull(references);
        QualifiedContentKey[] values = references.ToArray();
        foreach (QualifiedContentKey value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.ContentKind.Value != kind) throw new ArgumentException("Station reference kind is invalid.", nameof(references));
        }
        if (values.Distinct().Count() != values.Length) throw new ArgumentException("Station references must be unique.", nameof(references));
        return Array.AsReadOnly(values.OrderBy(value => value.ToString(), StringComparer.Ordinal).ToArray());
    }

    /// <summary>Copies explicit completed-structure recovery with checked totals; unfinished recovery policy is separate.</summary>
    internal static IReadOnlyDictionary<MaterialId, Quantity> CopyRecovery(
        IEnumerable<KeyValuePair<MaterialId, Quantity>> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        var values = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        ulong total = 0;
        foreach ((MaterialId material, Quantity amount) in recovery)
        {
            if (material.Value == 0 || amount.Units == 0 || !values.TryAdd(material, amount))
                throw new ArgumentException("Recovery must contain unique positive quantities and valid material identities.", nameof(recovery));
            total = checked(total + amount.Units);
        }
        return new ReadOnlyDictionary<MaterialId, Quantity>(values);
    }
}


/// <summary>Explicit circular region in the shared system-local coordinate space.</summary>
public sealed record StationPlacementRegion
{
    /// <summary>Creates an authored region; zero radius denotes its center point only.</summary>
    public StationPlacementRegion(SystemPosition center, ulong radiusUnits)
    {
        ArgumentOutOfRangeException.ThrowIfZero(center.SystemId.Value);
        Center = center;
        RadiusUnits = radiusUnits;
    }

    public SystemPosition Center { get; }
    public ulong RadiusUnits { get; }

    /// <summary>Uses shared exact proximity geometry, including the circle boundary and system identity.</summary>
    public bool Contains(SystemPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        var center = new PreparedInteractionPoint(Center.SystemId, Center.Position.X.Units, Center.Position.Y.Units, 1);
        var point = new PreparedInteractionPoint(position.SystemId, position.Position.X.Units, position.Position.Y.Units, 1);
        return center.InRange(point, RadiusUnits);
    }
}

/// <summary>Immutable authored spatial restrictions, independent of territory permissions and collision policy.</summary>
public sealed class StationPlacementRules
{
    /// <summary>Copies explicit restrictions; an empty allowed category imposes no restriction of that kind.</summary>
    public StationPlacementRules(IEnumerable<SystemId> allowedSystems,
        IEnumerable<StationPlacementRegion> allowedRegions, IEnumerable<StationPlacementRegion> exclusionRegions)
    {
        ArgumentNullException.ThrowIfNull(allowedSystems);
        ArgumentNullException.ThrowIfNull(allowedRegions);
        ArgumentNullException.ThrowIfNull(exclusionRegions);
        SystemId[] systems = allowedSystems.ToArray();
        foreach (SystemId system in systems) ArgumentOutOfRangeException.ThrowIfZero(system.Value);
        if (systems.Distinct().Count() != systems.Length) throw new ArgumentException("Allowed systems must be unique.", nameof(allowedSystems));
        StationPlacementRegion[] allowed = allowedRegions.ToArray();
        StationPlacementRegion[] excluded = exclusionRegions.ToArray();
        foreach (StationPlacementRegion region in allowed.Concat(excluded)) ArgumentNullException.ThrowIfNull(region);
        AllowedSystems = Array.AsReadOnly(systems.OrderBy(system => system.Value).ToArray());
        AllowedRegions = Array.AsReadOnly(allowed);
        ExclusionRegions = Array.AsReadOnly(excluded);
    }

    public IReadOnlyList<SystemId> AllowedSystems { get; }
    public IReadOnlyList<StationPlacementRegion> AllowedRegions { get; }
    public IReadOnlyList<StationPlacementRegion> ExclusionRegions { get; }

    /// <summary>Checks authored spatial restrictions only; territory admission is a separate provider responsibility.</summary>
    public bool Allows(SystemPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        return (AllowedSystems.Count == 0 || AllowedSystems.Contains(position.SystemId))
            && (AllowedRegions.Count == 0 || AllowedRegions.Any(region => region.Contains(position)))
            && !ExclusionRegions.Any(region => region.Contains(position));
    }

    /// <summary>Rejects unresolved system references before session authority publishes any station.</summary>
    internal void ValidateSystems(IReadOnlySet<SystemId> systems)
    {
        if (AllowedSystems.Concat(AllowedRegions.Select(region => region.Center.SystemId))
            .Concat(ExclusionRegions.Select(region => region.Center.SystemId)).Any(system => !systems.Contains(system)))
            throw new ArgumentException("Station placement references an unresolved system.", nameof(systems));
    }
}
