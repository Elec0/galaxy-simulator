using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public static partial class StaticNewGameLoader
{
    /// <summary>Allocates the selected material key table once, independently of document order or recipe traversal.</summary>
    private static MaterialInventoryCompatibilityMap ComposeStationMaterials(ResolvedContentCatalog catalog,
        StaticScenarioSource scenario, List<ContentDiagnostic> diagnostics)
    {
        var ids = new IdSequence<MaterialId>();
        var mappings = new List<KeyValuePair<MaterialId, PhysicalDefinition>>();
        foreach (var definition in StationSources(catalog, scenario, "material"))
        {
            string source = definition.Key.ToString();
            if (!TryObjectProperties(definition.Values, ["holdingKind", "capacityCost"], source, "$definition.values", diagnostics)
                || !TryString(definition.Values, "holdingKind", source, "$definition.values.holdingKind", diagnostics, out string? kind)
                || !TryUInt64(definition.Values, "capacityCost", source, "$definition.values.capacityCost", diagnostics, out ulong capacity)) continue;
            if (kind != "fungible" || capacity != 1)
            {
                diagnostics.Add(Diagnostic(source, "$definition.values", "Materials require explicitly authored fungible, one-unit compatibility storage."));
                continue;
            }
            mappings.Add(new(ids.Allocate(), new PhysicalDefinition(definition.Key, PhysicalHoldingKind.Fungible, new(capacity))));
        }
        return new(mappings);
    }

    /// <summary>Composes closed structural definitions, then applies the existing cross-definition and session admission validators.</summary>
    private static StationDefinitionCatalog? ComposeStationDefinitions(ResolvedContentCatalog catalog,
        StaticScenarioSource scenario, MaterialInventoryCompatibilityMap materials, IReadOnlyDictionary<string, StarSystem> systems,
        IReadOnlyDictionary<QualifiedContentKey, ShipDesign> designs, List<ContentDiagnostic> diagnostics)
    {
        var modules = new List<StationModuleDefinition>();
        var stations = new List<StationDefinition>();
        foreach (var definition in StationSources(catalog, scenario, "station-module"))
        {
            var module = ComposeStationModule(definition, materials, designs, diagnostics);
            if (module is not null) modules.Add(module);
        }
        foreach (var definition in StationSources(catalog, scenario, "station"))
        {
            var station = ComposeStationBase(definition, materials, systems, diagnostics);
            if (station is not null) stations.Add(station);
        }
        if (diagnostics.Count != 0) return null;
        try
        {
            var result = new StationDefinitionCatalog(stations, modules);
            result.ValidateForSession(materials, systems.Values.Select(system => system.Id));
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            diagnostics.Add(Diagnostic($"{scenario.PackageId}/scenario/{scenario.Id}", "$scenario.values.stationRuntime", "Station definitions violate composition, material, placement or staging invariants."));
            return null;
        }
    }

    /// <summary>Requires all base capability and recovery values explicitly, including empty placement restrictions.</summary>
    private static StationDefinition? ComposeStationBase(ContentDefinitionSource definition,
        MaterialInventoryCompatibilityMap materials, IReadOnlyDictionary<string, StarSystem> systems, List<ContentDiagnostic> diagnostics)
    {
        string source = definition.Key.ToString();
        const string path = "$definition.values";
        var values = definition.Values;
        if (!TryObjectProperties(values, ["moduleCapacity", "stagingCapacity", "baseCapacity", "baseRecipe", "sensorRadiusMeters",
                "allowedModules", "dismantleWork", "dismantleRecovery", "placementRules"], source, path, diagnostics)
            || !TryUInt64(values, "moduleCapacity", source, $"{path}.moduleCapacity", diagnostics, out ulong modules)
            || !TryUInt64(values, "stagingCapacity", source, $"{path}.stagingCapacity", diagnostics, out ulong staging)
            || !TryUInt64(values, "baseCapacity", source, $"{path}.baseCapacity", diagnostics, out ulong capacity)
            || !TryUInt64(values, "sensorRadiusMeters", source, $"{path}.sensorRadiusMeters", diagnostics, out ulong sensor)
            || !TryUInt64(values, "dismantleWork", source, $"{path}.dismantleWork", diagnostics, out ulong dismantle)
            || !TryComposeStationRecipe(values.Properties.GetValueOrDefault("baseRecipe") as ContentObjectValue, definition, materials, $"{path}.baseRecipe", diagnostics, out var recipe)
            || !TryComposeStationMaterialQuantities(values, "dismantleRecovery", definition, materials, $"{path}.dismantleRecovery", diagnostics, out var recovery)
            || !TryStationReferences(values, "allowedModules", "station-module", definition, $"{path}.allowedModules", diagnostics, out var allowed)
            || !TryStationPlacement(values.Properties.GetValueOrDefault("placementRules") as ContentObjectValue, systems, source, diagnostics, out var placement)) return null;
        try { return new(definition.Key, definition.InvariantFallback, modules, new(staging), new(capacity), recipe!, sensor, allowed, new(dismantle), recovery, placement); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            diagnostics.Add(Diagnostic(source, path, "The station base has invalid work, recovery, or physical capacity."));
            return null;
        }
    }

    /// <summary>Preserves explicit upgrade costs and delegates optional economic capabilities to their owning parsers.</summary>
    private static StationModuleDefinition? ComposeStationModule(ContentDefinitionSource definition,
        MaterialInventoryCompatibilityMap materials, IReadOnlyDictionary<QualifiedContentKey, ShipDesign> designs, List<ContentDiagnostic> diagnostics)
    {
        string source = definition.Key.ToString();
        const string path = "$definition.values";
        var values = definition.Values;
        string[] fields = ["capacityCost", "storageCapacity", "sensorRadiusMeters", "constructionRecipe", "dismantleWork", "dismantleRecovery", "upgradePaths"];
        foreach (string optional in new[] { "constructionCapability", "productionProvider", "shipConstructionProvider" })
            if (values.Properties.ContainsKey(optional)) fields = [.. fields, optional];
        if (!TryObjectProperties(values, fields, source, path, diagnostics)
            || !TryUInt64(values, "capacityCost", source, $"{path}.capacityCost", diagnostics, out ulong cost)
            || !TryUInt64(values, "storageCapacity", source, $"{path}.storageCapacity", diagnostics, out ulong storage)
            || !TryUInt64(values, "sensorRadiusMeters", source, $"{path}.sensorRadiusMeters", diagnostics, out ulong sensor)
            || !TryUInt64(values, "dismantleWork", source, $"{path}.dismantleWork", diagnostics, out ulong dismantle)
            || !TryComposeStationRecipe(values.Properties.GetValueOrDefault("constructionRecipe") as ContentObjectValue, definition, materials, $"{path}.constructionRecipe", diagnostics, out var recipe)
            || !TryComposeStationMaterialQuantities(values, "dismantleRecovery", definition, materials, $"{path}.dismantleRecovery", diagnostics, out var recovery)
            || !TryArray(values, "upgradePaths", source, $"{path}.upgradePaths", diagnostics, out var upgrades)) return null;
        var paths = new List<StationUpgradePath>();
        for (int index = 0; index < upgrades!.Items.Count; index++)
        {
            string upgradePath = $"{path}.upgradePaths[{index}]";
            var upgrade = upgrades.Items[index] as ContentObjectValue;
            if (!TryObjectProperties(upgrade, ["target", "recipe"], source, upgradePath, diagnostics)
                || !TryDefinitionReference(upgrade!, "target", "station-module", definition.References, source, $"{upgradePath}.target", diagnostics, out var target)
                || !TryComposeStationRecipe(upgrade!.Properties.GetValueOrDefault("recipe") as ContentObjectValue,
                    definition, materials, $"{upgradePath}.recipe", diagnostics, out var upgradeRecipe)) return null;
            paths.Add(new(target!, upgradeRecipe!));
        }
        var construction = ComposeStationConstructionCapability(values, "constructionCapability", source, diagnostics);
        var production = ComposeStationProductionProvider(definition, materials, diagnostics);
        var ships = ComposeStationShipProvider(definition, designs, diagnostics);
        if (diagnostics.Count != 0) return null;
        try { return new(definition.Key, definition.InvariantFallback, cost, new(storage), sensor, construction,
            recipe!, new(dismantle), recovery, [], paths, production, ships); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            diagnostics.Add(Diagnostic(source, path, "The module has invalid work, recovery, upgrade paths or capabilities."));
            return null;
        }
    }

    /// <summary>Resolves the explicitly selected retained loss policy without inventing a cutoff or behavior version.</summary>
    private static StationStorageLossPolicy? ComposeStationLossPolicy(ResolvedContentCatalog catalog,
        StaticScenarioSource scenario, ContentObjectValue stationRuntime, List<ContentDiagnostic> diagnostics)
    {
        if (!TryDefinitionReference(stationRuntime, "storageLossPolicy", "station-storage-loss-policy", scenario.References,
            $"{scenario.PackageId}/scenario/{scenario.Id}", "$scenario.values.stationRuntime.storageLossPolicy", diagnostics, out var key)
            || !catalog.Definitions.TryGetValue(key!, out var definition)) return null;
        string source = key!.ToString();
        var values = definition.Values;
        if (!TryObjectProperties(values, ["behaviorVersion", "cutoffNumerator", "cutoffDenominator"], source, "$definition.values", diagnostics)
            || !TryUInt64(values, "behaviorVersion", source, "$definition.values.behaviorVersion", diagnostics, out ulong version)
            || !TryUInt64(values, "cutoffNumerator", source, "$definition.values.cutoffNumerator", diagnostics, out ulong numerator)
            || !TryUInt64(values, "cutoffDenominator", source, "$definition.values.cutoffDenominator", diagnostics, out ulong denominator)) return null;
        if (version != 1 || numerator == 0 || denominator == 0 || numerator > denominator)
        {
            diagnostics.Add(Diagnostic(source, "$definition.values", "The loss behavior or exact cutoff fraction is unsupported."));
            return null;
        }
        return new(key, (int)version, numerator, denominator);
    }

    /// <summary>Admits only a complete positive-work recipe, with checked totals and declared material references.</summary>
    private static bool TryComposeStationRecipe(ContentObjectValue? value, ContentDefinitionSource definition,
        MaterialInventoryCompatibilityMap materials, string path, List<ContentDiagnostic> diagnostics, out ConstructionRecipe? recipe)
    {
        recipe = null;
        string source = definition.Key.ToString();
        if (!TryObjectProperties(value, ["inputs", "requiredWork"], source, path, diagnostics)
            || !TryUInt64(value!, "requiredWork", source, $"{path}.requiredWork", diagnostics, out ulong work)
            || !TryComposeStationMaterialQuantities(value!, "inputs", definition, materials, $"{path}.inputs", diagnostics, out var inputs)) return false;
        if (work == 0)
        {
            diagnostics.Add(Diagnostic(source, $"{path}.requiredWork", "Required work must be positive."));
            return false;
        }
        recipe = new(inputs, new(work));
        return true;
    }

    /// <summary>Rejects duplicate, zero and overflowing material quantities before any consumer receives numeric identities.</summary>
    private static bool TryComposeStationMaterialQuantities(ContentObjectValue owner, string name, ContentDefinitionSource definition,
        MaterialInventoryCompatibilityMap materials, string path, List<ContentDiagnostic> diagnostics, out KeyValuePair<MaterialId, Quantity>[] quantities)
    {
        quantities = [];
        string source = definition.Key.ToString();
        if (!TryArray(owner, name, source, path, diagnostics, out var entries)) return false;
        var parsed = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        UInt128 total = 0;
        for (int index = 0; index < entries!.Items.Count; index++)
        {
            string entryPath = $"{path}[{index}]";
            var entry = entries.Items[index] as ContentObjectValue;
            if (!TryObjectProperties(entry, ["material", "quantity"], source, entryPath, diagnostics)
                || !TryStationMaterial(entry!, "material", definition, materials, $"{entryPath}.material", diagnostics, out var material)
                || !TryUInt64(entry!, "quantity", source, $"{entryPath}.quantity", diagnostics, out ulong amount)) return false;
            total += amount;
            if (amount == 0 || total > ulong.MaxValue || !parsed.TryAdd(material, new(amount)))
            {
                diagnostics.Add(Diagnostic(source, entryPath, "Material quantities must be positive, unique, and have a representable total."));
                return false;
            }
        }
        quantities = parsed.ToArray();
        return true;
    }

    /// <summary>Joins declared qualified material keys to the single selected compatibility map.</summary>
    private static bool TryStationMaterial(ContentObjectValue owner, string name, ContentDefinitionSource definition,
        MaterialInventoryCompatibilityMap materials, string path, List<ContentDiagnostic> diagnostics, out MaterialId material)
    {
        material = default;
        if (!TryDefinitionReference(owner, name, "material", definition.References, definition.Key.ToString(), path, diagnostics, out var key)) return false;
        if (materials.GetMaterialId(key!) is { } id) { material = id; return true; }
        diagnostics.Add(Diagnostic(definition.Key.ToString(), path, "The declared material is not in the selected compatibility map."));
        return false;
    }

    /// <summary>Retains reference order only after every entry is declared, categorized and unique.</summary>
    private static bool TryStationReferences(ContentObjectValue owner, string name, string kind, ContentDefinitionSource definition,
        string path, List<ContentDiagnostic> diagnostics, out QualifiedContentKey[] references)
    {
        references = [];
        if (!TryArray(owner, name, definition.Key.ToString(), path, diagnostics, out var array)) return false;
        var result = new HashSet<QualifiedContentKey>();
        foreach (var item in array!.Items)
        {
            QualifiedContentKey? key = null;
            if (item is ContentStringValue text)
                try { key = QualifiedContentKey.Parse(text.Value); }
                catch (ArgumentException) { /* The shared diagnostic below covers malformed and undeclared references. */ }
            if (key is null || key.ContentKind.Value != kind || !definition.References.Contains(key) || !result.Add(key))
            {
                diagnostics.Add(Diagnostic(definition.Key.ToString(), path, "References must be unique, declared qualified keys of the required kind."));
                return false;
            }
        }
        references = result.OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
        return true;
    }

    /// <summary>Resolves authored system strings and exact circles without inferring territory permissions.</summary>
    private static bool TryStationPlacement(ContentObjectValue? value, IReadOnlyDictionary<string, StarSystem> systems,
        string source, List<ContentDiagnostic> diagnostics, out StationPlacementRules? placement)
    {
        placement = null;
        const string path = "$definition.values.placementRules";
        if (!TryObjectProperties(value, ["allowedSystems", "allowedRegions", "exclusionRegions"], source, path, diagnostics)
            || !TryArray(value!, "allowedSystems", source, $"{path}.allowedSystems", diagnostics, out var allowed)
            || !TryStationRegions(value!, "allowedRegions", systems, source, diagnostics, out var regions)
            || !TryStationRegions(value!, "exclusionRegions", systems, source, diagnostics, out var excluded)) return false;
        var ids = new HashSet<SystemId>();
        foreach (var item in allowed!.Items)
        {
            if (item is not ContentStringValue text || !systems.TryGetValue(text.Value, out var system) || !ids.Add(system.Id))
            {
                diagnostics.Add(Diagnostic(source, $"{path}.allowedSystems", "Allowed systems must be unique selected scenario system strings."));
                return false;
            }
        }
        placement = new(ids, regions, excluded);
        return true;
    }

    /// <summary>Admits system-local signed integral centers and unsigned radii, including exact zero-radius points.</summary>
    private static bool TryStationRegions(ContentObjectValue owner, string name, IReadOnlyDictionary<string, StarSystem> systems,
        string source, List<ContentDiagnostic> diagnostics, out StationPlacementRegion[] regions)
    {
        regions = [];
        string path = $"$definition.values.placementRules.{name}";
        if (!TryArray(owner, name, source, path, diagnostics, out var entries)) return false;
        var result = new List<StationPlacementRegion>();
        for (int index = 0; index < entries!.Items.Count; index++)
        {
            string entryPath = $"{path}[{index}]";
            var entry = entries.Items[index] as ContentObjectValue;
            if (!TryObjectProperties(entry, ["system", "x", "y", "radiusMeters"], source, entryPath, diagnostics)
                || !TryString(entry!, "system", source, $"{entryPath}.system", diagnostics, out string? systemKey)
                || !TryInt64(entry!, "x", source, $"{entryPath}.x", diagnostics, out long x)
                || !TryInt64(entry!, "y", source, $"{entryPath}.y", diagnostics, out long y)
                || !TryUInt64(entry!, "radiusMeters", source, $"{entryPath}.radiusMeters", diagnostics, out ulong radius)) return false;
            if (!systems.TryGetValue(systemKey!, out var system))
            {
                diagnostics.Add(Diagnostic(source, $"{entryPath}.system", "The region references an unknown scenario system."));
                return false;
            }
            result.Add(new(new(system.Id, new(new(x), new(y))), radius));
        }
        regions = result.ToArray();
        return true;
    }

    /// <summary>Enumerates the already-expanded selected reference closure in stable qualified-key order.</summary>
    private static IEnumerable<ContentDefinitionSource> StationSources(ResolvedContentCatalog catalog, StaticScenarioSource scenario, string kind) =>
        scenario.References.Where(key => key.ContentKind.Value == kind).OrderBy(key => key.ToString(), StringComparer.Ordinal)
            .Select(key => catalog.Definitions[key]);
}
