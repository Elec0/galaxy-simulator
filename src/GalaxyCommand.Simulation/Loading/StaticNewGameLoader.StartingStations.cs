using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public static partial class StaticNewGameLoader
{
    /// <summary>
    /// Composes explicit empty operational origins without constructing stations
    /// through runtime commands or introducing economic facilities. All authored
    /// identities and references validate before any setup escapes the loader.
    /// </summary>
    private static bool TryComposeStartingStations(StaticScenarioSource scenario, List<InitialShipSetup> ships,
        IReadOnlyDictionary<QualifiedContentKey, PrincipalDefinition> principals, Dictionary<string, StarSystem> systems,
        StationRuntimeSetup? stationRuntime, List<ContentDiagnostic> diagnostics,
        out StationRuntimeSetup? stations, out InitialStationInventorySetup[] inventories)
    {
        stations = stationRuntime;
        inventories = [];
        if (!scenario.Values.Properties.ContainsKey("startingStations")) return true;
        string source = $"{scenario.PackageId}/scenario/{scenario.Id}";
        const string path = "$scenario.values.startingStations";
        if (!TryArray(scenario.Values, "startingStations", source, path, diagnostics, out ContentArrayValue? array)) return false;
        if (stationRuntime is null)
        {
            diagnostics.Add(Diagnostic(source, path, "Starting stations require explicit station runtime definitions and policies."));
            return false;
        }
        var definitions = stationRuntime.Definitions.Stations.ToDictionary(definition => definition.Key);
        var entries = new SortedDictionary<string, StartingStationEntry>(StringComparer.Ordinal);
        var inventoryNames = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < array!.Items.Count; index++)
        {
            string itemPath = $"{path}[{index}]";
            if (array.Items[index] is not ContentObjectValue item
                || !TryObjectProperties(item, ["id", "principal", "definition", "system", "x", "y", "inventory", "modules"], source, itemPath, diagnostics)
                || !TryString(item, "id", source, $"{itemPath}.id", diagnostics, out string? localId)
                || !TryString(item, "system", source, $"{itemPath}.system", diagnostics, out string? systemName)
                || !TryInt64(item, "x", source, $"{itemPath}.x", diagnostics, out long x)
                || !TryInt64(item, "y", source, $"{itemPath}.y", diagnostics, out long y)
                || !TryReference(item, "principal", PrincipalKind, principals, source, $"{itemPath}.principal", diagnostics, out PrincipalDefinition? principal)
                || !TryReference(item, "definition", "station", definitions, source, $"{itemPath}.definition", diagnostics, out StationDefinition? definition))
            {
                if (array.Items[index] is not ContentObjectValue)
                    diagnostics.Add(Diagnostic(source, itemPath, "A starting station must be an object."));
                continue;
            }
            if (!systems.TryGetValue(systemName!, out StarSystem? system))
            {
                diagnostics.Add(Diagnostic(source, $"{itemPath}.system", "The starting station references an unknown scenario system."));
                continue;
            }
            if (item.Properties["inventory"] is not ContentObjectValue inventory
                || !TryObjectProperties(inventory, ["id", "capacity", "contents"], source, $"{itemPath}.inventory", diagnostics)
                || !TryString(inventory, "id", source, $"{itemPath}.inventory.id", diagnostics, out string? inventoryName)
                || !TryUInt64(inventory, "capacity", source, $"{itemPath}.inventory.capacity", diagnostics, out ulong capacity)
                || !TryArray(inventory, "contents", source, $"{itemPath}.inventory.contents", diagnostics, out ContentArrayValue? contents)
                || !TryArray(item, "modules", source, $"{itemPath}.modules", diagnostics, out ContentArrayValue? modules))
            {
                if (item.Properties["inventory"] is not ContentObjectValue)
                    diagnostics.Add(Diagnostic(source, $"{itemPath}.inventory", "A starting station requires an explicit inventory object."));
                continue;
            }
            if (contents!.Items.Count != 0 || modules!.Items.Count != 0)
            {
                diagnostics.Add(Diagnostic(source, itemPath, "The initial starting-station vocabulary requires empty inventory contents and no modules."));
                continue;
            }
            if (capacity != definition!.BaseCapacity.Units)
            {
                diagnostics.Add(Diagnostic(source, $"{itemPath}.inventory.capacity", "The empty station inventory capacity must equal the selected base definition's capacity."));
                continue;
            }
            var entry = new StartingStationEntry(inventoryName!, principal!.Id, definition.Key,
                new(system.Id, new(new(x), new(y))), new(capacity));
            if (!entries.TryAdd(localId!, entry))
                diagnostics.Add(Diagnostic(source, $"{itemPath}.id", "Starting station identities must be unique."));
            if (!inventoryNames.Add(inventoryName!))
                diagnostics.Add(Diagnostic(source, $"{itemPath}.inventory.id", "Starting station inventory identities must be unique."));
        }
        if (diagnostics.Count != 0) return false;

        // Entity and inventory identities share their allocators with ships.
        // Station identities belong to a separate canonical domain sequence.
        ulong nextEntity = ships.Count == 0 ? 0 : ships.Max(ship => ship.EntityId.Value);
        ulong nextInventory = ships.Count == 0 ? 0 : ships.Max(ship => ship.CargoInventoryId.Value);
        var inventoryIds = new Dictionary<string, InventoryId>(StringComparer.Ordinal);
        foreach (string inventoryName in inventoryNames.Order(StringComparer.Ordinal))
            inventoryIds.Add(inventoryName, new(checked(++nextInventory)));
        var stationSeeds = new List<InitialStationSetup>();
        var inventorySeeds = new List<InitialStationInventorySetup>();
        ulong stationId = 0;
        foreach (StartingStationEntry entry in entries.Values)
        {
            var entity = new EntityId(checked(++nextEntity));
            InventoryId inventory = inventoryIds[entry.InventoryName];
            stationSeeds.Add(new(entity, new(checked(++stationId)), inventory, entry.Principal,
                entry.Definition, entry.Position, [], SimulationTime.Zero));
            inventorySeeds.Add(new(inventory, entity, entry.Principal, entry.Capacity));
        }
        stations = new(stationRuntime.Definitions, stationRuntime.Materials, stationRuntime.TerritoryPolicy,
            stationRuntime.CommandAuthorities, stationSeeds, stationRuntime.StorageLossPolicy);
        inventories = inventorySeeds.OrderBy(inventory => inventory.Id.Value).ToArray();
        return true;
    }

    private sealed record StartingStationEntry(string InventoryName, PrincipalId Principal, QualifiedContentKey Definition,
        SystemPosition Position, Quantity Capacity);
}
