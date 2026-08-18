using System.Collections.ObjectModel;

namespace GalaxyCommand.Content;

/// <summary>
/// Distinguishes the physical documents declared by a package manifest.
/// </summary>
public enum ContentDocumentKind
{
    Definitions,
    Scenario,
}

/// <summary>
/// Declares one explicitly loaded package-relative content document.
/// </summary>
public sealed record ContentDocumentDeclaration
{
    /// <summary>
    /// Creates a document declaration without accessing the filesystem.
    /// </summary>
    public ContentDocumentDeclaration(string path, ContentDocumentKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Kind = kind;
    }

    /// <summary>Gets the package-relative document path.</summary>
    public string Path { get; }

    /// <summary>Gets the declared document category.</summary>
    public ContentDocumentKind Kind { get; }
}

/// <summary>
/// Represents one format-neutral package manifest before dependency resolution.
/// </summary>
public sealed class ContentPackageSource : IEquatable<ContentPackageSource>
{
    /// <summary>
    /// Creates an immutable package source and defensively copies caller-owned
    /// dependency and document collections.
    /// </summary>
    public ContentPackageSource(
        PackageId packageId,
        IEnumerable<PackageId> dependencies,
        IEnumerable<ContentDocumentDeclaration> documents)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(documents);

        PackageId = packageId;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        Documents = Array.AsReadOnly(documents.ToArray());
    }

    /// <summary>Gets the stable package identity.</summary>
    public PackageId PackageId { get; }

    /// <summary>Gets package identities that must resolve before this package.</summary>
    public ReadOnlyCollection<PackageId> Dependencies { get; }

    /// <summary>Gets the authoritative documents explicitly declared by the manifest.</summary>
    public ReadOnlyCollection<ContentDocumentDeclaration> Documents { get; }

    /// <inheritdoc/>
    public bool Equals(ContentPackageSource? other) =>
        other is not null &&
        PackageId == other.PackageId &&
        Dependencies.SequenceEqual(other.Dependencies) &&
        Documents.SequenceEqual(other.Documents);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentPackageSource);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(PackageId);
        foreach (PackageId dependency in Dependencies)
        {
            hash.Add(dependency);
        }

        foreach (ContentDocumentDeclaration document in Documents)
        {
            hash.Add(document);
        }

        return hash.ToHashCode();
    }
}
