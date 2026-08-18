using System.Collections.ObjectModel;

namespace GalaxyCommand.Content;

/// <summary>Lists trusted content kinds understood by the current application.</summary>
public sealed class ContentKindRegistry
{
    private readonly HashSet<ContentKind> _kinds;

    /// <summary>Creates an immutable registry and rejects duplicate kind tokens.</summary>
    public ContentKindRegistry(IEnumerable<ContentKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = new HashSet<ContentKind>();
        foreach (ContentKind kind in kinds)
        {
            ArgumentNullException.ThrowIfNull(kind);
            if (!_kinds.Add(kind))
            {
                throw new ArgumentException("Content kinds must be unique.", nameof(kinds));
            }
        }
    }

    /// <summary>Returns whether a definition kind is registered by trusted code.</summary>
    public bool IsSupported(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return _kinds.Contains(kind);
    }
}

/// <summary>Configures one production content-validation run.</summary>
public sealed class ContentValidationOptions
{
    /// <summary>Creates validation options with an explicit worker bound.</summary>
    public ContentValidationOptions(
        ContentJsonLimits limits,
        ContentKindRegistry contentKinds,
        int maximumDegreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(contentKinds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDegreeOfParallelism);
        Limits = limits;
        ContentKinds = contentKinds;
        MaximumDegreeOfParallelism = maximumDegreeOfParallelism;
    }

    public ContentJsonLimits Limits { get; }

    public ContentKindRegistry ContentKinds { get; }

    public int MaximumDegreeOfParallelism { get; }
}

/// <summary>Provides immutable definitions indexed by stable qualified key.</summary>
public sealed class ResolvedContentCatalog : IEquatable<ResolvedContentCatalog>
{
    /// <summary>Creates a catalog in canonical qualified-key order.</summary>
    public ResolvedContentCatalog(IEnumerable<ContentDefinitionSource> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        SortedDictionary<QualifiedContentKey, ContentDefinitionSource> sorted =
            new(QualifiedContentKeyComparer.Instance);
        foreach (ContentDefinitionSource definition in definitions)
        {
            if (!sorted.TryAdd(definition.Key, definition))
            {
                throw new ArgumentException("Resolved definition keys must be unique.", nameof(definitions));
            }
        }

        Definitions = new ReadOnlyDictionary<QualifiedContentKey, ContentDefinitionSource>(sorted);
    }

    public ReadOnlyDictionary<QualifiedContentKey, ContentDefinitionSource> Definitions { get; }

    /// <inheritdoc/>
    public bool Equals(ResolvedContentCatalog? other) =>
        other is not null && Definitions.SequenceEqual(other.Definitions);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ResolvedContentCatalog);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach ((QualifiedContentKey key, ContentDefinitionSource definition) in Definitions)
        {
            hash.Add(key);
            hash.Add(definition);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Contains one completely resolved and fingerprinted content set.</summary>
public sealed class ResolvedContentSet
{
    internal ResolvedContentSet(
        IEnumerable<PackageId> packageOrder,
        ResolvedContentCatalog catalog,
        IEnumerable<StaticScenarioSource> scenarios,
        string catalogFingerprint,
        IReadOnlyDictionary<PackageId, string> packageFingerprints)
    {
        PackageOrder = Array.AsReadOnly(packageOrder.ToArray());
        Catalog = catalog;
        Scenarios = Array.AsReadOnly(scenarios.ToArray());
        CatalogFingerprint = catalogFingerprint;
        SortedDictionary<PackageId, string> sortedFingerprints = new(PackageIdComparer.Instance);
        foreach ((PackageId packageId, string fingerprint) in packageFingerprints)
        {
            sortedFingerprints.Add(packageId, fingerprint);
        }

        PackageFingerprints = new ReadOnlyDictionary<PackageId, string>(sortedFingerprints);
    }

    public ReadOnlyCollection<PackageId> PackageOrder { get; }

    public ResolvedContentCatalog Catalog { get; }

    public ReadOnlyCollection<StaticScenarioSource> Scenarios { get; }

    public string CatalogFingerprint { get; }

    public ReadOnlyDictionary<PackageId, string> PackageFingerprints { get; }
}

/// <summary>Returns either one complete resolved set or bounded diagnostics.</summary>
public sealed class ContentValidationResult
{
    internal ContentValidationResult(ResolvedContentSet? content, IEnumerable<ContentDiagnostic> diagnostics)
    {
        Content = content;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public bool IsSuccess => Content is not null;

    public ResolvedContentSet? Content { get; }

    public ReadOnlyCollection<ContentDiagnostic> Diagnostics { get; }
}

internal sealed class QualifiedContentKeyComparer : IComparer<QualifiedContentKey>
{
    internal static QualifiedContentKeyComparer Instance { get; } = new();

    public int Compare(QualifiedContentKey? x, QualifiedContentKey? y) =>
        StringComparer.Ordinal.Compare(x?.ToString(), y?.ToString());
}

internal sealed class PackageIdComparer : IComparer<PackageId>
{
    internal static PackageIdComparer Instance { get; } = new();

    public int Compare(PackageId? x, PackageId? y) =>
        StringComparer.Ordinal.Compare(x?.Value, y?.Value);
}
