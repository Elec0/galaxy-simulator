using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public static partial class StaticNewGameLoader
{
    /// <summary>
    /// Resolves only the scenario's explicit policy selection. Generic content
    /// structural bounds have already been enforced before domain materialization.
    /// </summary>
    private static CargoDispositionPolicy? ComposeCargoPolicy(ResolvedContentCatalog catalog,
        StaticScenarioSource scenario, List<ContentDiagnostic> diagnostics)
    {
        if (!scenario.Values.Properties.ContainsKey("cargoPolicy")) return null;
        string scenarioSource = $"{scenario.PackageId}/scenario/{scenario.Id}";
        if (!TryDefinitionReference(scenario.Values, "cargoPolicy", CargoPolicyKind, scenario.References,
            scenarioSource, "$scenario.values.cargoPolicy", diagnostics, out QualifiedContentKey? key)) return null;
        ContentDefinitionSource definition = catalog.Definitions[key!];
        string source = key!.ToString();
        ContentObjectValue values = definition.Values;
        if (!TryObjectProperties(values,
            ["generationBehaviorVersion", "mergeBehaviorVersion", "orderingBehaviorVersion", "durationBehaviorVersion",
                "recoveryByCause", "fungibleObjectCount", "scatterRadiusMeters", "lifetimeMilliseconds",
                "minimumCollectionDurationMilliseconds", "maximumCollectionDurationMilliseconds", "collectionStorageCap"],
            source, "$definition.values", diagnostics)) return null;

        if (!TryCargoInt32(values, "generationBehaviorVersion", source, diagnostics, out int generation)
            || !TryCargoInt32(values, "mergeBehaviorVersion", source, diagnostics, out int merge)
            || !TryCargoInt32(values, "orderingBehaviorVersion", source, diagnostics, out int ordering)
            || !TryCargoInt32(values, "durationBehaviorVersion", source, diagnostics, out int duration)
            || !TryCargoInt32(values, "fungibleObjectCount", source, diagnostics, out int count)
            || !TryUInt64(values, "scatterRadiusMeters", source, "$definition.values.scatterRadiusMeters", diagnostics, out ulong radius)
            || !TryUInt64(values, "lifetimeMilliseconds", source, "$definition.values.lifetimeMilliseconds", diagnostics, out ulong lifetime)
            || !TryUInt64(values, "minimumCollectionDurationMilliseconds", source, "$definition.values.minimumCollectionDurationMilliseconds", diagnostics, out ulong minimum)
            || !TryUInt64(values, "maximumCollectionDurationMilliseconds", source, "$definition.values.maximumCollectionDurationMilliseconds", diagnostics, out ulong maximum)
            || !TryUInt64(values, "collectionStorageCap", source, "$definition.values.collectionStorageCap", diagnostics, out ulong cap)) return null;

        List<KeyValuePair<CargoLossCause, CargoRecoveryFraction>>? causes = ComposeRecoveryCauses(values, source, diagnostics);
        if (causes is null) return null;
        try
        {
            return new CargoDispositionPolicy(key, generation, merge, ordering, duration, causes, count, radius,
                new(lifetime), new(minimum), new(maximum), new(cap));
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Diagnostic(source, "$definition.values", "The cargo policy has unsupported versions, causes, or invalid parameters."));
            return null;
        }
    }

    /// <summary>Checks bounded integral counts and versions before narrowing authored numeric values.</summary>
    private static bool TryCargoInt32(ContentObjectValue owner, string name, string source,
        List<ContentDiagnostic> diagnostics, out int result)
    {
        result = 0;
        if (!TryUInt64(owner, name, source, $"$definition.values.{name}", diagnostics, out ulong value)) return false;
        if (value <= int.MaxValue)
        {
            result = (int)value;
            return true;
        }
        diagnostics.Add(Diagnostic(source, $"$definition.values.{name}", "The cargo count or version exceeds its supported integer bound."));
        return false;
    }

    /// <summary>Admits only the fixed supported cause vocabulary and exact fractions, without fallback entries.</summary>
    private static List<KeyValuePair<CargoLossCause, CargoRecoveryFraction>>? ComposeRecoveryCauses(
        ContentObjectValue owner, string source, List<ContentDiagnostic> diagnostics)
    {
        const string path = "$definition.values.recoveryByCause";
        if (owner.Properties["recoveryByCause"] is not ContentArrayValue array || array.Items.Count != 3)
        {
            diagnostics.Add(Diagnostic(source, path, "Cargo recovery requires exactly one explicit entry for every supported cause."));
            return null;
        }

        var causes = new List<KeyValuePair<CargoLossCause, CargoRecoveryFraction>>();
        for (int index = 0; index < array.Items.Count; index++)
        {
            string itemPath = $"{path}[{index}]";
            if (array.Items[index] is not ContentObjectValue values
                || !TryObjectProperties(values, ["cause", "numerator", "denominator"], source, itemPath, diagnostics)
                || !TryString(values, "cause", source, $"{itemPath}.cause", diagnostics, out string? name)
                || !TryUInt64(values, "numerator", source, $"{itemPath}.numerator", diagnostics, out ulong numerator)
                || !TryUInt64(values, "denominator", source, $"{itemPath}.denominator", diagnostics, out ulong denominator))
            {
                diagnostics.Add(Diagnostic(source, itemPath, "A cargo recovery entry is malformed."));
                return null;
            }

            CargoLossCause? cause = name switch
            {
                "entity-destruction" => CargoLossCause.EntityDestruction,
                "storage-module-loss" => CargoLossCause.StorageModuleLoss,
                "functional-ejection" => CargoLossCause.FunctionalEjection,
                _ => null,
            };
            if (cause is null || denominator == 0 || numerator > denominator)
            {
                diagnostics.Add(Diagnostic(source, itemPath, "The recovery cause or exact fraction is unsupported."));
                return null;
            }
            causes.Add(new(cause.Value, new CargoRecoveryFraction(numerator, denominator)));
        }
        return causes;
    }
}
