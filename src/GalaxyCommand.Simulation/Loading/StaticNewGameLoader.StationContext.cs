using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public static partial class StaticNewGameLoader
{
    /// <summary>Admits exactly the scenario's transitive definition closure before allocating any runtime content identities.</summary>
    private static StaticScenarioSource ExpandScenarioReferences(ResolvedContentCatalog catalog, StaticScenarioSource scenario)
    {
        var selected = new HashSet<QualifiedContentKey>();
        var pending = new Stack<QualifiedContentKey>(scenario.References);
        while (pending.TryPop(out QualifiedContentKey? key))
            if (selected.Add(key))
                foreach (QualifiedContentKey reference in catalog.Definitions[key].References) pending.Push(reference);
        return new(scenario.PackageId, scenario.Id, scenario.InvariantFallback,
            selected.OrderBy(key => key.ToString(), StringComparer.Ordinal), scenario.Values);
    }

    /// <summary>Requires authored station policies and provider context; explicit starting origins are composed separately.</summary>
    private static bool TryComposeStationRuntime(ResolvedContentCatalog catalog, StaticScenarioSource scenario,
        MaterialInventoryCompatibilityMap materials, IReadOnlyDictionary<string, StarSystem> systems,
        IReadOnlyDictionary<QualifiedContentKey, ShipDesign> designs, PrincipalId player,
        CommandSource playerSource, ISpatialNavigationPlanner? navigation, List<ContentDiagnostic> diagnostics,
        out StationRuntimeSetup? stations, out GameSessionEconomySetup? economy)
    {
        stations = null;
        economy = null;
        string source = $"{scenario.PackageId}/scenario/{scenario.Id}";
        const string path = "$scenario.values.stationRuntime";
        bool selected = scenario.References.Any(key => key.ContentKind.Value is "station" or "station-module");
        if (!scenario.Values.Properties.TryGetValue("stationRuntime", out ContentValue? raw))
        {
            if (!selected) return true;
            diagnostics.Add(Diagnostic(source, path, "Selected station definitions require explicit station runtime policies."));
            return false;
        }
        ContentObjectValue? runtime = raw as ContentObjectValue;
        string[] fields = runtime?.Properties.ContainsKey("economy") == true
            ? ["territoryPolicy", "storageLossPolicy", "economy"] : ["territoryPolicy", "storageLossPolicy"];
        if (!TryObjectProperties(runtime, fields, source, path, diagnostics)
            || !TryString(runtime!, "territoryPolicy", source, $"{path}.territoryPolicy", diagnostics, out string? territory)) return false;
        if (territory != "absent")
        {
            diagnostics.Add(Diagnostic(source, $"{path}.territoryPolicy", "Only explicitly absent territory policy is supported."));
            return false;
        }
        StationStorageLossPolicy? policy = ComposeStationLossPolicy(catalog, scenario, runtime!, diagnostics);
        StationDefinitionCatalog? definitions = ComposeStationDefinitions(catalog, scenario, materials, systems, designs, diagnostics);
        if (policy is null || definitions is null || diagnostics.Count > 0) return false;
        definitions.ValidateForSession(materials, systems.Values.Select(system => system.Id));
        bool hasProviders = definitions.Modules.Any(module => module.ProductionProvider is not null || module.ShipConstructionProvider is not null);
        if (runtime!.Properties.TryGetValue("economy", out ContentValue? context))
        {
            if (!TryComposeStationEconomy(context, source, navigation, materials, designs.Values, diagnostics, out economy)) return false;
        }
        else if (hasProviders)
        {
            diagnostics.Add(Diagnostic(source, $"{path}.economy", "Selected economic providers require explicit transport timing and host navigation."));
            return false;
        }
        stations = new(definitions, materials, StationTerritoryPolicy.Absent, [new(playerSource, player)], storageLossPolicy: policy);
        return true;
    }

    /// <summary>Builds an empty economic owner only from explicit timing and the host's shared navigation instance.</summary>
    private static bool TryComposeStationEconomy(ContentValue context, string source, ISpatialNavigationPlanner? navigation,
        MaterialInventoryCompatibilityMap materials, IEnumerable<ShipDesign> designs, List<ContentDiagnostic> diagnostics,
        out GameSessionEconomySetup? economy)
    {
        economy = null;
        const string path = "$scenario.values.stationRuntime.economy";
        ContentObjectValue? values = context as ContentObjectValue;
        if (!TryObjectProperties(values, ["transportTiming"], source, path, diagnostics)) return false;
        ContentObjectValue? timing = values!.Properties["transportTiming"] as ContentObjectValue;
        if (!TryObjectProperties(timing, ["dockingOverheadMilliseconds", "loadingUnitsPerSecond", "unloadingUnitsPerSecond"],
                source, $"{path}.transportTiming", diagnostics)
            || !TryUInt64(timing!, "dockingOverheadMilliseconds", source, $"{path}.transportTiming.dockingOverheadMilliseconds", diagnostics, out ulong overhead)
            || !TryPositiveUInt64(timing!, "loadingUnitsPerSecond", source, $"{path}.transportTiming.loadingUnitsPerSecond", diagnostics, out ulong loading)
            || !TryPositiveUInt64(timing!, "unloadingUnitsPerSecond", source, $"{path}.transportTiming.unloadingUnitsPerSecond", diagnostics, out ulong unloading)) return false;
        if (navigation is null)
        {
            diagnostics.Add(Diagnostic(source, path, "Economic context requires an explicitly supplied shared host navigation planner."));
            return false;
        }
        economy = new([], [], [], [], designs, [], [], new HierarchicalLogisticsNavigation(
            new Dictionary<LocationId, SystemPosition>(), navigation), new(new(overhead), new(loading), new(unloading)), materials);
        return true;
    }
}
