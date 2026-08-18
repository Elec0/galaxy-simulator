using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GalaxyCommand.Content;

/// <summary>
/// Loads untrusted package directories through the production adapters and
/// publishes either one complete immutable content set or no content.
/// </summary>
public static class ContentPipeline
{
    /// <summary>
    /// Validates explicitly selected package directories. Read-only package
    /// loading may run concurrently, while reduction and publication use stable
    /// package and content-key order.
    /// </summary>
    public static ContentValidationResult Validate(
        IEnumerable<string> packageDirectories,
        ContentValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(packageDirectories);
        ArgumentNullException.ThrowIfNull(options);
        string[] directories = packageDirectories.ToArray();
        if (directories.Length > options.Limits.MaximumContainerEntries)
        {
            return Rejected(
                options,
                [Diagnostic(ContentDiagnosticKind.LimitExceeded, "packages", "$", "The selected package count exceeds the production limit.")]);
        }

        ConcurrentBag<LoadedPackage> loaded = [];
        ConcurrentBag<ContentDiagnostic> diagnostics = [];
        Parallel.ForEach(
            directories,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaximumDegreeOfParallelism },
            directory =>
            {
                PackageLoadResult result = LoadPackage(directory, options.Limits);
                foreach (ContentDiagnostic diagnostic in result.Diagnostics)
                {
                    diagnostics.Add(diagnostic);
                }

                if (result.Package is not null)
                {
                    loaded.Add(result.Package);
                }
            });

        List<LoadedPackage> packages = loaded.OrderBy(item => item.Source, StringComparer.Ordinal).ToList();
        List<ContentDiagnostic> failures = diagnostics.ToList();
        List<ContentDiagnostic> graphFailures = [];
        foreach (IGrouping<PackageId, LoadedPackage> collision in packages.GroupBy(item => item.Manifest.PackageId).Where(group => group.Count() > 1))
        {
            graphFailures.Add(Diagnostic(ContentDiagnosticKind.IdentityCollision, collision.First().Source, "$.packageId", "More than one selected package has the same identity."));
        }

        Dictionary<PackageId, LoadedPackage> uniquePackages = packages
            .GroupBy(item => item.Manifest.PackageId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (LoadedPackage package in uniquePackages.Values)
        {
            foreach (PackageId dependency in package.Manifest.Dependencies)
            {
                if (!uniquePackages.ContainsKey(dependency))
                {
                    graphFailures.Add(Diagnostic(ContentDiagnosticKind.MissingDependency, package.Source, "$.dependencies", "A declared package dependency is not selected."));
                }
            }
        }

        List<PackageId> packageOrder = ResolvePackageOrder(uniquePackages, graphFailures);
        failures.AddRange(graphFailures);
        if (graphFailures.Count > 0)
        {
            return Rejected(options, failures);
        }

        List<ContentDefinitionSource> definitions = packages.SelectMany(package => package.Definitions).ToList();
        foreach (ContentDefinitionSource definition in definitions)
        {
            if (!options.ContentKinds.IsSupported(definition.Key.ContentKind))
            {
                failures.Add(Diagnostic(ContentDiagnosticKind.UnsupportedContentKind, definition.Key.PackageId.Value, definition.Key.ToString(), "The definition kind is not registered by trusted code."));
            }
        }

        foreach (IGrouping<QualifiedContentKey, ContentDefinitionSource> collision in definitions.GroupBy(item => item.Key).Where(group => group.Count() > 1))
        {
            failures.Add(Diagnostic(ContentDiagnosticKind.IdentityCollision, collision.Key.PackageId.Value, collision.Key.ToString(), "More than one definition has the same qualified key."));
        }

        HashSet<QualifiedContentKey> keys = definitions.Select(definition => definition.Key).ToHashSet();
        foreach (ContentDefinitionSource definition in definitions)
        {
            ValidateReferences(definition.References, keys, definition.Key.PackageId.Value, definition.Key.ToString(), failures);
        }

        List<StaticScenarioSource> scenarios = packages.SelectMany(package => package.Scenarios).ToList();
        foreach (IGrouping<(PackageId PackageId, LocalContentId Id), StaticScenarioSource> collision in
                 scenarios.GroupBy(item => (item.PackageId, item.Id)).Where(group => group.Count() > 1))
        {
            failures.Add(Diagnostic(
                ContentDiagnosticKind.IdentityCollision,
                collision.Key.PackageId.Value,
                $"scenario/{collision.Key.Id.Value}",
                "More than one static scenario has the same package-local identity."));
        }

        foreach (StaticScenarioSource scenario in scenarios)
        {
            ValidateReferences(scenario.References, keys, scenario.PackageId.Value, $"scenario/{scenario.Id}", failures);
        }

        if (failures.Count > 0)
        {
            return Rejected(options, failures);
        }

        ResolvedContentCatalog catalog = new(definitions);
        SortedDictionary<PackageId, string> packageFingerprints = FingerprintPackages(packages, options.Limits);
        string catalogFingerprint = FingerprintCatalog(packageFingerprints);
        ResolvedContentSet content = new(
            packageOrder,
            catalog,
            scenarios.OrderBy(scenario => scenario.PackageId.Value, StringComparer.Ordinal).ThenBy(scenario => scenario.Id.Value, StringComparer.Ordinal),
            catalogFingerprint,
            packageFingerprints);
        return new ContentValidationResult(content, []);
    }

    /// <summary>
    /// Loads a manifest and only its declared documents. No directory
    /// enumeration participates in authoritative content discovery.
    /// </summary>
    private static PackageLoadResult LoadPackage(string directory, ContentJsonLimits limits)
    {
        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PackageLoadResult.Failed(Diagnostic(ContentDiagnosticKind.StorageAccess, directory, "$", "The package directory path is invalid."));
        }

        string manifestPath = Path.Combine(fullDirectory, "package.json");
        ContentReadResult<ContentPackageSource> manifest = ReadFile(
            manifestPath,
            bytes => ContentJsonAdapter.ReadPackage(bytes, limits, manifestPath));
        if (!manifest.IsSuccess)
        {
            return PackageLoadResult.Failed(manifest.Diagnostics);
        }

        List<ContentDefinitionSource> definitions = [];
        List<StaticScenarioSource> scenarios = [];
        List<ContentDiagnostic> diagnostics = [];
        foreach (ContentDocumentDeclaration declaration in manifest.Value!.Documents)
        {
            if (!TryResolveDocumentPath(fullDirectory, declaration.Path, out string documentPath))
            {
                diagnostics.Add(Diagnostic(ContentDiagnosticKind.InvalidValue, manifestPath, "$.documents", "A declared document path escapes its package directory."));
                continue;
            }

            if (declaration.Kind == ContentDocumentKind.Definitions)
            {
                ContentReadResult<ContentDefinitionsSource> result = ReadFile(
                    documentPath,
                    bytes => ContentJsonAdapter.ReadDefinitions(bytes, manifest.Value.PackageId, limits, documentPath));
                diagnostics.AddRange(result.Diagnostics);
                if (result.IsSuccess)
                {
                    definitions.AddRange(result.Value!.Definitions);
                }
            }
            else
            {
                ContentReadResult<StaticScenarioSource> result = ReadFile(
                    documentPath,
                    bytes => ContentJsonAdapter.ReadScenario(bytes, manifest.Value.PackageId, limits, documentPath));
                diagnostics.AddRange(result.Diagnostics);
                if (result.IsSuccess)
                {
                    scenarios.Add(result.Value!);
                }
            }
        }

        return PackageLoadResult.Accepted(
            new LoadedPackage(manifestPath, manifest.Value, definitions, scenarios),
            diagnostics);
    }

    /// <summary>
    /// Reads one bounded file and maps storage failures to stable diagnostics
    /// without exposing exception text.
    /// </summary>
    private static ContentReadResult<T> ReadFile<T>(
        string path,
        Func<ReadOnlyMemory<byte>, ContentReadResult<T>> decode)
        where T : class
    {
        try
        {
            return decode(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ContentReadResult<T>.Rejected(Diagnostic(ContentDiagnosticKind.StorageAccess, path, "$", "The declared content file could not be read."));
        }
    }

    /// <summary>
    /// Resolves a declared path and keeps the full path below the package root;
    /// rooted paths and alternate separators cannot bypass this check.
    /// </summary>
    private static bool TryResolveDocumentPath(string directory, string declaration, out string path)
    {
        path = string.Empty;
        if (Path.IsPathRooted(declaration) || declaration.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(directory, declaration));
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    /// <summary>
    /// Produces a stable dependency-first order and reports a cycle without
    /// publishing a partial ordering.
    /// </summary>
    private static List<PackageId> ResolvePackageOrder(
        IReadOnlyDictionary<PackageId, LoadedPackage> packages,
        List<ContentDiagnostic> failures)
    {
        List<PackageId> order = [];
        HashSet<PackageId> visited = [];
        HashSet<PackageId> visiting = [];
        foreach (PackageId packageId in packages.Keys.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            if (!Visit(packageId, packages, visited, visiting, order))
            {
                failures.Add(Diagnostic(ContentDiagnosticKind.DependencyCycle, packageId.Value, "$.dependencies", "The selected package dependency graph contains a cycle."));
                return [];
            }
        }

        return order;
    }

    /// <summary>Visits one package while maintaining the acyclic DFS stack invariant.</summary>
    private static bool Visit(
        PackageId packageId,
        IReadOnlyDictionary<PackageId, LoadedPackage> packages,
        ISet<PackageId> visited,
        ISet<PackageId> visiting,
        ICollection<PackageId> order)
    {
        if (visited.Contains(packageId))
        {
            return true;
        }

        if (!visiting.Add(packageId))
        {
            return false;
        }

        foreach (PackageId dependency in packages[packageId].Manifest.Dependencies.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            if (packages.ContainsKey(dependency) && !Visit(dependency, packages, visited, visiting, order))
            {
                return false;
            }
        }

        visiting.Remove(packageId);
        visited.Add(packageId);
        order.Add(packageId);
        return true;
    }

    private static void ValidateReferences(
        IEnumerable<QualifiedContentKey> references,
        HashSet<QualifiedContentKey> keys,
        string source,
        string path,
        List<ContentDiagnostic> failures)
    {
        foreach (QualifiedContentKey reference in references)
        {
            if (!keys.Contains(reference))
            {
                failures.Add(Diagnostic(ContentDiagnosticKind.UnresolvedReference, source, path, "A qualified content reference does not resolve."));
            }
        }
    }

    /// <summary>
    /// Fingerprints canonical package content rather than file bytes, document
    /// order, or input enumeration order.
    /// </summary>
    private static SortedDictionary<PackageId, string> FingerprintPackages(
        IEnumerable<LoadedPackage> packages,
        ContentJsonLimits limits)
    {
        SortedDictionary<PackageId, string> fingerprints = new(PackageIdComparer.Instance);
        foreach (LoadedPackage package in packages.OrderBy(item => item.Manifest.PackageId.Value, StringComparer.Ordinal))
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(hash, package.Manifest.PackageId.Value);
            foreach (PackageId dependency in package.Manifest.Dependencies.OrderBy(id => id.Value, StringComparer.Ordinal))
            {
                Append(hash, dependency.Value);
            }

            ContentDefinitionsSource definitions = new(
                package.Definitions.OrderBy(item => item.Key, QualifiedContentKeyComparer.Instance));
            hash.AppendData(ContentJsonAdapter.WriteDefinitions(definitions, limits));
            foreach (StaticScenarioSource scenario in package.Scenarios.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                hash.AppendData(ContentJsonAdapter.WriteScenario(scenario, limits));
            }

            fingerprints.Add(package.Manifest.PackageId, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        return fingerprints;
    }

    private static string FingerprintCatalog(IReadOnlyDictionary<PackageId, string> packageFingerprints)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((PackageId packageId, string fingerprint) in packageFingerprints.OrderBy(item => item.Key.Value, StringComparer.Ordinal))
        {
            Append(hash, packageId.Value);
            Append(hash, fingerprint);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static ContentValidationResult Rejected(
        ContentValidationOptions options,
        IEnumerable<ContentDiagnostic> diagnostics)
    {
        ContentDiagnostic[] stable = diagnostics
            .OrderBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Kind)
            .Take(options.Limits.MaximumDiagnostics)
            .ToArray();
        return new ContentValidationResult(null, stable);
    }

    private static ContentDiagnostic Diagnostic(
        ContentDiagnosticKind kind,
        string source,
        string path,
        string message) => new(kind, source, path, message);

    private sealed record LoadedPackage(
        string Source,
        ContentPackageSource Manifest,
        IReadOnlyList<ContentDefinitionSource> Definitions,
        IReadOnlyList<StaticScenarioSource> Scenarios);

    private sealed record PackageLoadResult(LoadedPackage? Package, IReadOnlyList<ContentDiagnostic> Diagnostics)
    {
        internal static PackageLoadResult Accepted(
            LoadedPackage package,
            IEnumerable<ContentDiagnostic> diagnostics) => new(package, diagnostics.ToArray());

        internal static PackageLoadResult Failed(params ContentDiagnostic[] diagnostics) => new(null, diagnostics);

        internal static PackageLoadResult Failed(IEnumerable<ContentDiagnostic> diagnostics) => new(null, diagnostics.ToArray());
    }
}
