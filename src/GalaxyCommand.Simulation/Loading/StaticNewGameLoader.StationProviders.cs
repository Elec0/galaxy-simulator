using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public static partial class StaticNewGameLoader
{
    /// <summary>Omission means no station work supply; present capabilities require exact explicit geometry and positive rate.</summary>
    private static StationConstructionCapability? ComposeStationConstructionCapability(ContentObjectValue owner,
        string name, string source, List<ContentDiagnostic> diagnostics)
    {
        if (!owner.Properties.TryGetValue(name, out ContentValue? raw)) return null;
        string path = $"$definition.values.{name}";
        if (raw is not ContentObjectValue values)
        {
            diagnostics.Add(Diagnostic(source, path, "A construction capability must be an explicit object."));
            return null;
        }
        if (!TryObjectProperties(values, ["rangeMeters", "throughputUnitsPerSecond"], source, path, diagnostics)
            || !TryUInt64(values, "rangeMeters", source, $"{path}.rangeMeters", diagnostics, out ulong range)
            || !TryUInt64(values, "throughputUnitsPerSecond", source, $"{path}.throughputUnitsPerSecond", diagnostics, out ulong rate)) return null;
        if (rate == 0)
        {
            diagnostics.Add(Diagnostic(source, $"{path}.throughputUnitsPerSecond", "Construction throughput must be positive."));
            return null;
        }
        return new(range, new(rate));
    }

    /// <summary>Composes one closed authored recipe without allocating a facility or synthesizing economic parameters.</summary>
    private static StationProductionProvider? ComposeStationProductionProvider(ContentDefinitionSource definition,
        MaterialInventoryCompatibilityMap materials, List<ContentDiagnostic> diagnostics)
    {
        const string path = "$definition.values.productionProvider";
        if (!definition.Values.Properties.TryGetValue("productionProvider", out ContentValue? raw)) return null;
        string source = definition.Key.ToString();
        if (raw is not ContentObjectValue values)
        {
            diagnostics.Add(Diagnostic(source, path, "A production provider must be an explicit object."));
            return null;
        }
        if (!TryObjectProperties(values, ["inputs", "outputMaterial", "outputQuantity", "requiredWork", "throughputUnitsPerSecond", "repeat"], source, path, diagnostics)
            || !TryComposeStationMaterialQuantities(values, "inputs", definition, materials, $"{path}.inputs", diagnostics, out var inputs)
            || !TryStationMaterial(values, "outputMaterial", definition, materials, $"{path}.outputMaterial", diagnostics, out MaterialId output)
            || !TryUInt64(values, "outputQuantity", source, $"{path}.outputQuantity", diagnostics, out ulong quantity)
            || !TryUInt64(values, "requiredWork", source, $"{path}.requiredWork", diagnostics, out ulong work)
            || !TryUInt64(values, "throughputUnitsPerSecond", source, $"{path}.throughputUnitsPerSecond", diagnostics, out ulong rate)) return null;
        if (values.Properties["repeat"] is not ContentBooleanValue repeat)
        {
            diagnostics.Add(Diagnostic(source, $"{path}.repeat", "Production repetition must be an explicit boolean."));
            return null;
        }
        try { return new(new(inputs, output, new(quantity), new(work)), new(rate), repeat.Value); }
        catch (ArgumentException) { diagnostics.Add(Diagnostic(source, path, "Production work, output and throughput must be positive.")); }
        catch (OverflowException) { diagnostics.Add(Diagnostic(source, path, "Production input quantities exceed their supported bound.")); }
        return null;
    }

    /// <summary>Retains only declared allowed ship definitions and explicit persistent materialization choices.</summary>
    private static StationShipConstructionProvider? ComposeStationShipProvider(ContentDefinitionSource definition,
        IReadOnlyDictionary<QualifiedContentKey, ShipDesign> designs, List<ContentDiagnostic> diagnostics)
    {
        const string path = "$definition.values.shipConstructionProvider";
        if (!definition.Values.Properties.TryGetValue("shipConstructionProvider", out ContentValue? raw)) return null;
        string source = definition.Key.ToString();
        if (raw is not ContentObjectValue values)
        {
            diagnostics.Add(Diagnostic(source, path, "A ship construction provider must be an explicit object."));
            return null;
        }
        if (!TryObjectProperties(values, ["allowedDesigns", "throughputUnitsPerSecond", "materializedShipController", "initialOrderPolicy"], source, path, diagnostics)
            || !TryArray(values, "allowedDesigns", source, $"{path}.allowedDesigns", diagnostics, out ContentArrayValue? array)
            || !TryUInt64(values, "throughputUnitsPerSecond", source, $"{path}.throughputUnitsPerSecond", diagnostics, out ulong rate)
            || !TryString(values, "initialOrderPolicy", source, $"{path}.initialOrderPolicy", diagnostics, out string? orderPolicy)) return null;
        if (values.Properties["materializedShipController"] is not ContentObjectValue controller
            || !TryObjectProperties(controller, ["kind", "source"], source, $"{path}.materializedShipController", diagnostics)
            || !TryString(controller, "kind", source, $"{path}.materializedShipController.kind", diagnostics, out string? kind)
            || !TryString(controller, "source", source, $"{path}.materializedShipController.source", diagnostics, out string? commandSource))
        {
            diagnostics.Add(Diagnostic(source, $"{path}.materializedShipController", "A ship provider requires its explicit persistent controller."));
            return null;
        }
        ActorControllerKind? controllerKind = kind switch { "player" => ActorControllerKind.Player, "autonomous" => ActorControllerKind.Autonomous, _ => null };
        if (controllerKind is null || orderPolicy != "no-initial-order")
        {
            diagnostics.Add(Diagnostic(source, path, "The ship controller or initial order policy is unsupported."));
            return null;
        }
        var allowed = new List<ShipDesign>();
        for (int index = 0; index < array!.Items.Count; index++)
        {
            string itemPath = $"{path}.allowedDesigns[{index}]";
            QualifiedContentKey? key = null;
            if (array.Items[index] is ContentStringValue reference)
                try { key = QualifiedContentKey.Parse(reference.Value); }
                catch (ArgumentException) { /* The diagnostic below rejects malformed authored keys. */ }
            if (key is null || key.ContentKind.Value != ShipDesignKind || !definition.References.Contains(key)
                || !designs.TryGetValue(key, out ShipDesign? design))
            {
                diagnostics.Add(Diagnostic(source, itemPath, "An allowed ship design must resolve through this definition's declared references."));
                return null;
            }
            allowed.Add(design);
        }
        try { return new(allowed, new(rate), new(controllerKind.Value, new(commandSource!)), InitialShipOrderPolicy.NoInitialOrder); }
        catch (ArgumentException) { diagnostics.Add(Diagnostic(source, path, "Ship designs must be nonempty and unique, and throughput must be positive.")); }
        return null;
    }
}
