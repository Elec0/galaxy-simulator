using System.Collections.ObjectModel;

namespace GalaxyCommand.Content;

/// <summary>
/// Identifies a stable content failure category independently from prose.
/// </summary>
public enum ContentDiagnosticKind
{
    DocumentTooLarge,
    InvalidUtf8,
    InvalidJson,
    DuplicateProperty,
    UnknownProperty,
    MissingProperty,
    InvalidValue,
    LimitExceeded,
    WrongFormat,
    UnsupportedSchemaVersion,
    MissingDependency,
    DependencyCycle,
    IdentityCollision,
    UnresolvedReference,
    UnsupportedContentKind,
    StorageAccess,
}

/// <summary>
/// Reports one bounded failure at a stable source and neutral-model path.
/// </summary>
public sealed record ContentDiagnostic(
    ContentDiagnosticKind Kind,
    string Source,
    string Path,
    string Message);

/// <summary>
/// Returns either one completely decoded value or stable diagnostics, never a
/// partially accepted value.
/// </summary>
public sealed class ContentReadResult<T>
    where T : class
{
    private ContentReadResult(T? value, IEnumerable<ContentDiagnostic> diagnostics)
    {
        Value = value;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Gets whether decoding completed without diagnostics.</summary>
    public bool IsSuccess => Value is not null;

    /// <summary>Gets the complete decoded value, or <see langword="null"/> on failure.</summary>
    public T? Value { get; }

    /// <summary>Gets stable diagnostics in deterministic order.</summary>
    public ReadOnlyCollection<ContentDiagnostic> Diagnostics { get; }

    internal static ContentReadResult<T> Accepted(T value) => new(value, []);

    internal static ContentReadResult<T> Rejected(params ContentDiagnostic[] diagnostics) =>
        new(null, diagnostics);
}

/// <summary>
/// Provides resource limits for untrusted package documents and aggregate
/// package shape.
/// </summary>
public sealed class ContentJsonLimits
{
    /// <summary>
    /// Creates explicit positive limits. Readers reject input before publishing
    /// any neutral model when a limit is exceeded.
    /// </summary>
    public ContentJsonLimits(
        int maximumDocumentBytes,
        int maximumDepth,
        int maximumStringLength,
        int maximumContainerEntries,
        int maximumDocumentsPerPackage,
        int maximumDependenciesPerPackage,
        int maximumDiagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDocumentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStringLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumContainerEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDocumentsPerPackage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDependenciesPerPackage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDiagnostics);

        MaximumDocumentBytes = maximumDocumentBytes;
        MaximumDepth = maximumDepth;
        MaximumStringLength = maximumStringLength;
        MaximumContainerEntries = maximumContainerEntries;
        MaximumDocumentsPerPackage = maximumDocumentsPerPackage;
        MaximumDependenciesPerPackage = maximumDependenciesPerPackage;
        MaximumDiagnostics = maximumDiagnostics;
    }

    /// <summary>Gets the approved production defaults.</summary>
    public static ContentJsonLimits ProductionDefaults { get; } = new(
        maximumDocumentBytes: 1_048_576,
        maximumDepth: 32,
        maximumStringLength: 4_096,
        maximumContainerEntries: 4_096,
        maximumDocumentsPerPackage: 256,
        maximumDependenciesPerPackage: 128,
        maximumDiagnostics: 1_024);

    public int MaximumDocumentBytes { get; }

    public int MaximumDepth { get; }

    public int MaximumStringLength { get; }

    public int MaximumContainerEntries { get; }

    public int MaximumDocumentsPerPackage { get; }

    public int MaximumDependenciesPerPackage { get; }

    public int MaximumDiagnostics { get; }
}
