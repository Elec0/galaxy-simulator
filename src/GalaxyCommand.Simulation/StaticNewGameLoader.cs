using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Presentation-only authored position for one static scenario system. These
/// coordinates never enter simulation authority, checkpoints, or saves.
/// </summary>
public sealed record StaticGalaxyLayoutEntry(SystemId SystemId, decimal X, decimal Y);

/// <summary>
/// Returns either one fully composed clean-session setup or stable content
/// diagnostics. A rejected load never exposes a partial setup.
/// </summary>
public sealed class StaticNewGameLoadResult
{
    private StaticNewGameLoadResult(
        GameSessionSetup? setup,
        ResolvedContentSet? content,
        IEnumerable<StaticGalaxyLayoutEntry>? galaxyLayout,
        IEnumerable<ContentDiagnostic> diagnostics)
    {
        Setup = setup;
        Content = content;
        GalaxyLayout = galaxyLayout is null
            ? null
            : Array.AsReadOnly(galaxyLayout.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Gets whether both content admission and setup composition succeeded.</summary>
    public bool IsSuccess => Setup is not null;

    /// <summary>Gets the complete setup, or <see langword="null"/> after rejection.</summary>
    public GameSessionSetup? Setup { get; }

    /// <summary>Gets the resolved content retained by the new session boundary.</summary>
    public ResolvedContentSet? Content { get; }

    /// <summary>
    /// Gets the complete presentation-only galaxy layout, or
    /// <see langword="null"/> after rejection.
    /// </summary>
    public ReadOnlyCollection<StaticGalaxyLayoutEntry>? GalaxyLayout { get; }

    /// <summary>Gets deterministic diagnostics when no setup was published.</summary>
    public ReadOnlyCollection<ContentDiagnostic> Diagnostics { get; }

    internal static StaticNewGameLoadResult Accepted(
        GameSessionSetup setup,
        ResolvedContentSet content,
        IEnumerable<StaticGalaxyLayoutEntry> galaxyLayout) =>
        new(setup, content, galaxyLayout, []);

    internal static StaticNewGameLoadResult Rejected(
        IEnumerable<ContentDiagnostic> diagnostics) =>
        new(null, null, null, diagnostics);
}

/// <summary>Selects the approved built-in package and minimal static scenario.</summary>
public static class BuiltInNewGame
{
    private const string CorePackageDirectoryName = "galaxy-command.core";

    /// <summary>
    /// Loads the shipped core package beneath an explicit built-in content
    /// directory. The caller still owns runtime-only seed and retention policy.
    /// </summary>
    public static StaticNewGameLoadResult Load(
        string builtInContentDirectory,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity,
        int maximumDegreeOfParallelism)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(builtInContentDirectory);
        return StaticNewGameLoader.Load(
            [Path.Combine(builtInContentDirectory, CorePackageDirectoryName)],
            PackageId.Create("galaxy-command.core"),
            LocalContentId.Create("minimal"),
            randomRootSeed,
            factRetentionCapacity,
            maximumDegreeOfParallelism);
    }
}

/// <summary>
/// Admits selected disk packages through the production content pipeline and
/// composes one approved static scenario into clean simulation setup state.
/// </summary>
public static class StaticNewGameLoader
{
    private const string PrincipalKind = "principal";
    private const string ShipDesignKind = "ship-design";
    private const string StandingPolicyKind = "standing-policy";

    private static readonly ContentKindRegistry SupportedKinds = new(
        [
            ContentKind.Create(PrincipalKind),
            ContentKind.Create(ShipDesignKind),
            ContentKind.Create(StandingPolicyKind),
        ]);

    /// <summary>
    /// Loads all selected packages, resolves the named scenario, and maps
    /// scenario-local string identities to typed runtime IDs in canonical
    /// ordinal order. Caller-owned runtime policy supplies the random root and
    /// fact-retention capacity rather than authored content.
    /// </summary>
    public static StaticNewGameLoadResult Load(
        IEnumerable<string> packageDirectories,
        PackageId scenarioPackageId,
        LocalContentId scenarioId,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity,
        int maximumDegreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(packageDirectories);
        ArgumentNullException.ThrowIfNull(scenarioPackageId);
        ArgumentNullException.ThrowIfNull(scenarioId);
        ArgumentNullException.ThrowIfNull(randomRootSeed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factRetentionCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDegreeOfParallelism);

        ContentValidationResult validation = ContentPipeline.Validate(
            packageDirectories,
            new ContentValidationOptions(
                ContentJsonLimits.ProductionDefaults,
                SupportedKinds,
                maximumDegreeOfParallelism));
        if (!validation.IsSuccess)
        {
            return StaticNewGameLoadResult.Rejected(validation.Diagnostics);
        }

        ResolvedContentSet content = validation.Content!;
        StaticScenarioSource? scenario = content.Scenarios.SingleOrDefault(
            candidate => candidate.PackageId == scenarioPackageId && candidate.Id == scenarioId);
        if (scenario is null)
        {
            return StaticNewGameLoadResult.Rejected(
                [Diagnostic(scenarioPackageId.Value, $"scenario/{scenarioId}", "The selected static scenario does not exist.")]);
        }

        List<ContentDiagnostic> diagnostics = [];
        GameSessionSetup? setup = Compose(
            content.Catalog,
            scenario,
            randomRootSeed,
            factRetentionCapacity,
            diagnostics,
            out ReadOnlyCollection<StaticGalaxyLayoutEntry> galaxyLayout);
        return setup is null
            ? StaticNewGameLoadResult.Rejected(diagnostics)
            : StaticNewGameLoadResult.Accepted(setup, content, galaxyLayout);
    }

    /// <summary>
    /// Maps only the approved initial static-scenario vocabulary and preserves
    /// the all-or-nothing boundary when any authored value is malformed.
    /// </summary>
    private static GameSessionSetup? Compose(
        ResolvedContentCatalog catalog,
        StaticScenarioSource scenario,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity,
        List<ContentDiagnostic> diagnostics,
        out ReadOnlyCollection<StaticGalaxyLayoutEntry> galaxyLayout)
    {
        galaxyLayout = Array.AsReadOnly(Array.Empty<StaticGalaxyLayoutEntry>());
        string source = $"{scenario.PackageId}/scenario/{scenario.Id}";
        if (!TryObjectProperties(
                scenario.Values,
                ["galaxyLayout", "playerPrincipal", "ships", "standingPolicy", "systems"],
                source,
                "$scenario.values",
                diagnostics))
        {
            return null;
        }

        Dictionary<QualifiedContentKey, PrincipalDefinition> principals =
            ComposePrincipals(catalog, scenario, source, diagnostics);
        Dictionary<QualifiedContentKey, ShipDesign> designs =
            ComposeShipDesigns(catalog, scenario, source, diagnostics);
        if (!TryDefinitionReference(
                scenario.Values,
                "playerPrincipal",
                PrincipalKind,
                principals.Keys,
                source,
                "$scenario.values.playerPrincipal",
                diagnostics,
                out QualifiedContentKey? playerKey)
            || !TryDefinitionReference(
                scenario.Values,
                "standingPolicy",
                StandingPolicyKind,
                scenario.References,
                source,
                "$scenario.values.standingPolicy",
                diagnostics,
                out QualifiedContentKey? policyKey)
            || diagnostics.Count > 0)
        {
            return null;
        }

        StandingPolicy? standingPolicy = ComposeStandingPolicy(
            catalog,
            policyKey!,
            diagnostics);
        Dictionary<string, StarSystem> systems = ComposeSystems(
            scenario.Values,
            source,
            diagnostics);
        galaxyLayout = ComposeGalaxyLayout(
            scenario.Values,
            source,
            systems,
            diagnostics);
        List<InitialShipSetup> ships = ComposeShips(
            scenario.Values,
            source,
            systems,
            principals,
            designs,
            diagnostics);
        if (standingPolicy is null || diagnostics.Count > 0)
        {
            return null;
        }

        try
        {
            return new GameSessionSetup(
                systems.Values,
                ships,
                new RelationshipSetup(
                    principals.Values,
                    principals[playerKey!].Id,
                    standingPolicy,
                    []),
                randomRootSeed,
                factRetentionCapacity);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            diagnostics.Add(Diagnostic(source, "$scenario.values", "The static scenario violates clean-session invariants."));
            return null;
        }
    }

    /// <summary>
    /// Assigns principal IDs from qualified-key order so input document order
    /// and worker completion order cannot affect runtime identity.
    /// </summary>
    private static Dictionary<QualifiedContentKey, PrincipalDefinition> ComposePrincipals(
        ResolvedContentCatalog catalog,
        StaticScenarioSource scenario,
        string source,
        List<ContentDiagnostic> diagnostics)
    {
        QualifiedContentKey[] keys = scenario.References
            .Where(key => key.ContentKind.Value == PrincipalKind)
            .OrderBy(key => key.ToString(), StringComparer.Ordinal)
            .ToArray();
        var principals = new Dictionary<QualifiedContentKey, PrincipalDefinition>();
        for (int index = 0; index < keys.Length; index++)
        {
            QualifiedContentKey key = keys[index];
            if (!catalog.Definitions.TryGetValue(key, out ContentDefinitionSource? definition))
            {
                diagnostics.Add(Diagnostic(source, key.ToString(), "The principal definition is not present in the resolved catalog."));
                continue;
            }

            if (!TryObjectProperties(definition.Values, [], key.ToString(), "$definition.values", diagnostics))
            {
                continue;
            }

            principals.Add(
                key,
                new PrincipalDefinition(
                    new PrincipalId(checked((uint)index + 1)),
                    new PrincipalContentId(key.LocalId.Value),
                    definition.InvariantFallback));
        }

        if (principals.Count == 0)
        {
            diagnostics.Add(Diagnostic(source, "$scenario.references", "The static scenario must select at least one principal definition."));
        }

        return principals;
    }

    /// <summary>
    /// Assigns construction design IDs from qualified-key order and admits only
    /// the initial material-free starter design vocabulary.
    /// </summary>
    private static Dictionary<QualifiedContentKey, ShipDesign> ComposeShipDesigns(
        ResolvedContentCatalog catalog,
        StaticScenarioSource scenario,
        string source,
        List<ContentDiagnostic> diagnostics)
    {
        QualifiedContentKey[] keys = scenario.References
            .Where(key => key.ContentKind.Value == ShipDesignKind)
            .OrderBy(key => key.ToString(), StringComparer.Ordinal)
            .ToArray();
        var designs = new Dictionary<QualifiedContentKey, ShipDesign>();
        for (int index = 0; index < keys.Length; index++)
        {
            QualifiedContentKey key = keys[index];
            if (!catalog.Definitions.TryGetValue(key, out ContentDefinitionSource? definition)
                || !TryObjectProperties(
                    definition?.Values,
                    ["cargoCapacity", "requiredWork"],
                    key.ToString(),
                    "$definition.values",
                    diagnostics)
                || !TryUInt64(definition!.Values, "cargoCapacity", key.ToString(), "$definition.values.cargoCapacity", diagnostics, out ulong capacity)
                || !TryUInt64(definition.Values, "requiredWork", key.ToString(), "$definition.values.requiredWork", diagnostics, out ulong work)
                || capacity == 0
                || work == 0)
            {
                continue;
            }

            designs.Add(
                key,
                new ShipDesign(
                    new ConstructionDesignId(checked((uint)index + 1)),
                    definition.InvariantFallback,
                    new ConstructionRecipe([], new Work(work)),
                    new Quantity(capacity)));
        }

        return designs;
    }

    /// <summary>Builds the single selected standing policy from validated exact integers.</summary>
    private static StandingPolicy? ComposeStandingPolicy(
        ResolvedContentCatalog catalog,
        QualifiedContentKey key,
        List<ContentDiagnostic> diagnostics)
    {
        if (!catalog.Definitions.TryGetValue(key, out ContentDefinitionSource? definition)
            || !TryObjectProperties(
                definition?.Values,
                ["adversarialThreshold", "alliedThreshold", "favorableThreshold", "initial", "maximum", "minimum", "neutralThreshold"],
                key.ToString(),
                "$definition.values",
                diagnostics))
        {
            return null;
        }

        string[] names = ["minimum", "maximum", "initial", "adversarialThreshold", "neutralThreshold", "favorableThreshold", "alliedThreshold"];
        long[] values = new long[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            if (!TryInt64(definition!.Values, names[index], key.ToString(), $"$definition.values.{names[index]}", diagnostics, out values[index]))
            {
                return null;
            }
        }

        try
        {
            return new StandingPolicy(
                new StandingPolicyId(key.LocalId.Value),
                new StandingValue(values[0]),
                new StandingValue(values[1]),
                new StandingValue(values[2]),
                new StandingValue(values[3]),
                new StandingValue(values[4]),
                new StandingValue(values[5]),
                new StandingValue(values[6]));
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Diagnostic(key.ToString(), "$definition.values", "The standing policy bounds and thresholds are invalid."));
            return null;
        }
    }

    /// <summary>Assigns system IDs from scenario-local string identity order.</summary>
    private static Dictionary<string, StarSystem> ComposeSystems(
        ContentObjectValue scenario,
        string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!TryArray(scenario, "systems", source, "$scenario.values.systems", diagnostics, out ContentArrayValue? array))
        {
            return [];
        }

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < array!.Items.Count; index++)
        {
            string path = $"$scenario.values.systems[{index}]";
            if (array.Items[index] is not ContentObjectValue item
                || !TryObjectProperties(item, ["fallback", "id"], source, path, diagnostics)
                || !TryString(item, "id", source, $"{path}.id", diagnostics, out string? id)
                || !TryString(item, "fallback", source, $"{path}.fallback", diagnostics, out string? fallback))
            {
                continue;
            }

            if (!entries.TryAdd(id!, fallback!))
            {
                diagnostics.Add(Diagnostic(source, $"{path}.id", "System identities must be unique."));
            }
        }

        var systems = new Dictionary<string, StarSystem>(StringComparer.Ordinal);
        int runtimeId = 1;
        foreach ((string id, string fallback) in entries)
        {
            systems.Add(id, new StarSystem(new SystemId(checked((uint)runtimeId)), fallback));
            runtimeId++;
        }

        if (systems.Count == 0)
        {
            diagnostics.Add(Diagnostic(source, "$scenario.values.systems", "The static scenario must contain at least one system."));
        }

        return systems;
    }

    /// <summary>
    /// Validates the required one-to-one system layout while keeping authored
    /// presentation coordinates separate from clean-session authority.
    /// </summary>
    private static ReadOnlyCollection<StaticGalaxyLayoutEntry> ComposeGalaxyLayout(
        ContentObjectValue scenario,
        string source,
        Dictionary<string, StarSystem> systems,
        List<ContentDiagnostic> diagnostics)
    {
        if (!TryArray(
                scenario,
                "galaxyLayout",
                source,
                "$scenario.values.galaxyLayout",
                diagnostics,
                out ContentArrayValue? array))
        {
            return Array.AsReadOnly(Array.Empty<StaticGalaxyLayoutEntry>());
        }

        var entries = new Dictionary<string, StaticGalaxyLayoutEntry>(StringComparer.Ordinal);
        for (int index = 0; index < array!.Items.Count; index++)
        {
            string path = $"$scenario.values.galaxyLayout[{index}]";
            if (array.Items[index] is not ContentObjectValue item
                || !TryObjectProperties(item, ["system", "x", "y"], source, path, diagnostics)
                || !TryString(item, "system", source, $"{path}.system", diagnostics, out string? systemId)
                || !TryDecimal(item, "x", source, $"{path}.x", diagnostics, out decimal x)
                || !TryDecimal(item, "y", source, $"{path}.y", diagnostics, out decimal y))
            {
                continue;
            }

            if (!systems.TryGetValue(systemId!, out StarSystem? system))
            {
                diagnostics.Add(Diagnostic(source, $"{path}.system", "The layout references an unknown scenario system."));
                continue;
            }

            if (!entries.TryAdd(systemId!, new StaticGalaxyLayoutEntry(system.Id, x, y)))
            {
                diagnostics.Add(Diagnostic(source, $"{path}.system", "Each scenario system must have exactly one galaxy layout entry."));
            }
        }

        foreach (string systemId in systems.Keys.Order(StringComparer.Ordinal))
        {
            if (!entries.ContainsKey(systemId))
            {
                diagnostics.Add(Diagnostic(source, "$scenario.values.galaxyLayout", "Every scenario system must have one galaxy layout entry."));
            }
        }

        return Array.AsReadOnly(
            entries.Values.OrderBy(entry => entry.SystemId.Value).ToArray());
    }

    /// <summary>Assigns ship, entity, and cargo IDs from scenario-local string identity order.</summary>
    private static List<InitialShipSetup> ComposeShips(
        ContentObjectValue scenario,
        string source,
        Dictionary<string, StarSystem> systems,
        IReadOnlyDictionary<QualifiedContentKey, PrincipalDefinition> principals,
        IReadOnlyDictionary<QualifiedContentKey, ShipDesign> designs,
        List<ContentDiagnostic> diagnostics)
    {
        if (!TryArray(scenario, "ships", source, "$scenario.values.ships", diagnostics, out ContentArrayValue? array))
        {
            return [];
        }

        var entries = new SortedDictionary<string, (ContentObjectValue Value, string Path)>(StringComparer.Ordinal);
        for (int index = 0; index < array!.Items.Count; index++)
        {
            string path = $"$scenario.values.ships[{index}]";
            if (array.Items[index] is not ContentObjectValue item
                || !TryObjectProperties(item, ["controllerSource", "design", "id", "principal", "system", "x", "y"], source, path, diagnostics)
                || !TryString(item, "id", source, $"{path}.id", diagnostics, out string? id))
            {
                continue;
            }

            if (!entries.TryAdd(id!, (item, path)))
            {
                diagnostics.Add(Diagnostic(source, $"{path}.id", "Ship identities must be unique."));
            }
        }

        List<InitialShipSetup> ships = [];
        int runtimeId = 1;
        foreach ((string _, (ContentObjectValue item, string path)) in entries)
        {
            if (!TryString(item, "system", source, $"{path}.system", diagnostics, out string? systemId)
                || !systems.TryGetValue(systemId!, out StarSystem? system)
                || !TryString(item, "controllerSource", source, $"{path}.controllerSource", diagnostics, out string? controllerSource)
                || !TryInt64(item, "x", source, $"{path}.x", diagnostics, out long x)
                || !TryInt64(item, "y", source, $"{path}.y", diagnostics, out long y)
                || !TryReference(item, "principal", PrincipalKind, principals, source, $"{path}.principal", diagnostics, out PrincipalDefinition? principal)
                || !TryReference(item, "design", ShipDesignKind, designs, source, $"{path}.design", diagnostics, out ShipDesign? design))
            {
                if (systemId is not null && !systems.ContainsKey(systemId))
                {
                    diagnostics.Add(Diagnostic(source, $"{path}.system", "The ship references an unknown scenario system."));
                }

                runtimeId++;
                continue;
            }

            uint id = checked((uint)runtimeId);
            ships.Add(
                new InitialShipSetup(
                    new EntityId(id),
                    new ShipId(id),
                    new InventoryId(id),
                    principal!.Id,
                    design!,
                    new SystemPosition(
                        system!.Id,
                        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y))),
                    new ActorController(ActorControllerKind.Player, new CommandSourceId(controllerSource!))));
            runtimeId++;
        }

        if (ships.Count == 0)
        {
            diagnostics.Add(Diagnostic(source, "$scenario.values.ships", "The approved minimal scenario must contain at least one player-controlled ship."));
        }

        return ships;
    }

    /// <summary>Checks that an object has exactly the approved property set.</summary>
    private static bool TryObjectProperties(
        ContentObjectValue? value,
        IEnumerable<string> expected,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics)
    {
        if (value is null)
        {
            diagnostics.Add(Diagnostic(source, path, "The value must be an object."));
            return false;
        }

        string[] expectedNames = expected.Order(StringComparer.Ordinal).ToArray();
        string[] actualNames = value.Properties.Keys.Order(StringComparer.Ordinal).ToArray();
        if (actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The object does not contain exactly the approved properties."));
        return false;
    }

    /// <summary>Reads one required authored array without accepting a scalar substitute.</summary>
    private static bool TryArray(
        ContentObjectValue owner,
        string name,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out ContentArrayValue? value)
    {
        if (owner.Properties.TryGetValue(name, out ContentValue? raw) && raw is ContentArrayValue array)
        {
            value = array;
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The property must be an array."));
        value = null;
        return false;
    }

    /// <summary>Reads one required non-empty authored string.</summary>
    private static bool TryString(
        ContentObjectValue owner,
        string name,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out string? value)
    {
        if (owner.Properties.TryGetValue(name, out ContentValue? raw)
            && raw is ContentStringValue text
            && !string.IsNullOrWhiteSpace(text.Value))
        {
            value = text.Value;
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The property must be a non-empty string."));
        value = null;
        return false;
    }

    /// <summary>Reads one exact integral Int64 authored number.</summary>
    private static bool TryInt64(
        ContentObjectValue owner,
        string name,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out long value)
    {
        if (owner.Properties.TryGetValue(name, out ContentValue? raw)
            && raw is ContentNumberValue number
            && decimal.Truncate(number.Value) == number.Value
            && number.Value >= long.MinValue
            && number.Value <= long.MaxValue)
        {
            value = decimal.ToInt64(number.Value);
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The property must be an exact signed 64-bit integer."));
        value = 0;
        return false;
    }

    /// <summary>Reads one authored decimal presentation coordinate.</summary>
    private static bool TryDecimal(
        ContentObjectValue owner,
        string name,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out decimal value)
    {
        if (owner.Properties.TryGetValue(name, out ContentValue? raw)
            && raw is ContentNumberValue number)
        {
            value = number.Value;
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The property must be a finite decimal coordinate."));
        value = 0;
        return false;
    }

    /// <summary>Reads one exact integral UInt64 authored number.</summary>
    private static bool TryUInt64(
        ContentObjectValue owner,
        string name,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out ulong value)
    {
        if (owner.Properties.TryGetValue(name, out ContentValue? raw)
            && raw is ContentNumberValue number
            && decimal.Truncate(number.Value) == number.Value
            && number.Value >= 0
            && number.Value <= ulong.MaxValue)
        {
            value = decimal.ToUInt64(number.Value);
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The property must be an exact unsigned 64-bit integer."));
        value = 0;
        return false;
    }

    /// <summary>Resolves one scenario property to a selected definition key.</summary>
    private static bool TryDefinitionReference(
        ContentObjectValue owner,
        string name,
        string expectedKind,
        IEnumerable<QualifiedContentKey> allowed,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out QualifiedContentKey? key)
    {
        if (!TryString(owner, name, source, path, diagnostics, out string? raw))
        {
            key = null;
            return false;
        }

        try
        {
            key = QualifiedContentKey.Parse(raw!);
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Diagnostic(source, path, "The property must be a qualified content key."));
            key = null;
            return false;
        }

        if (key.ContentKind.Value == expectedKind && allowed.Contains(key))
        {
            return true;
        }

        diagnostics.Add(Diagnostic(source, path, "The definition reference has the wrong kind or is not selected by the scenario."));
        key = null;
        return false;
    }

    /// <summary>Resolves one object property through a typed composed-definition map.</summary>
    private static bool TryReference<T>(
        ContentObjectValue owner,
        string name,
        string expectedKind,
        IReadOnlyDictionary<QualifiedContentKey, T> values,
        string source,
        string path,
        List<ContentDiagnostic> diagnostics,
        out T? value)
        where T : class
    {
        if (TryDefinitionReference(owner, name, expectedKind, values.Keys, source, path, diagnostics, out QualifiedContentKey? key)
            && values.TryGetValue(key!, out T? resolved))
        {
            value = resolved;
            return true;
        }

        value = null;
        return false;
    }

    private static ContentDiagnostic Diagnostic(string source, string path, string message) =>
        new(ContentDiagnosticKind.InvalidValue, source, path, message);
}
