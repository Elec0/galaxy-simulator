using System.Collections.ObjectModel;

namespace GalaxyCommand.Content;

/// <summary>Represents one immutable format-neutral declarative definition.</summary>
public sealed class ContentDefinitionSource : IEquatable<ContentDefinitionSource>
{
    /// <summary>Creates a definition and defensively copies its references.</summary>
    public ContentDefinitionSource(
        QualifiedContentKey key,
        string invariantFallback,
        IEnumerable<QualifiedContentKey> references,
        ContentObjectValue values)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(invariantFallback);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(values);
        Key = key;
        InvariantFallback = invariantFallback;
        References = Array.AsReadOnly(references.ToArray());
        Values = values;
    }

    public QualifiedContentKey Key { get; }

    public string InvariantFallback { get; }

    public ReadOnlyCollection<QualifiedContentKey> References { get; }

    public ContentObjectValue Values { get; }

    /// <inheritdoc/>
    public bool Equals(ContentDefinitionSource? other) =>
        other is not null && Key == other.Key && InvariantFallback == other.InvariantFallback &&
        References.SequenceEqual(other.References) && Values.Equals(other.Values);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentDefinitionSource);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Key, InvariantFallback, Values);
}

/// <summary>Represents one format-neutral definitions document.</summary>
public sealed class ContentDefinitionsSource : IEquatable<ContentDefinitionsSource>
{
    /// <summary>Creates a definitions document by defensively copying definitions.</summary>
    public ContentDefinitionsSource(IEnumerable<ContentDefinitionSource> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        Definitions = Array.AsReadOnly(definitions.ToArray());
    }

    public ReadOnlyCollection<ContentDefinitionSource> Definitions { get; }

    /// <inheritdoc/>
    public bool Equals(ContentDefinitionsSource? other) =>
        other is not null && Definitions.SequenceEqual(other.Definitions);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentDefinitionsSource);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (ContentDefinitionSource definition in Definitions)
        {
            hash.Add(definition);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Represents one format-neutral static new-game scenario.</summary>
public sealed class StaticScenarioSource : IEquatable<StaticScenarioSource>
{
    /// <summary>Creates a static scenario and defensively copies its references.</summary>
    public StaticScenarioSource(
        PackageId packageId,
        LocalContentId id,
        string invariantFallback,
        IEnumerable<QualifiedContentKey> references,
        ContentObjectValue values)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(invariantFallback);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(values);
        PackageId = packageId;
        Id = id;
        InvariantFallback = invariantFallback;
        References = Array.AsReadOnly(references.ToArray());
        Values = values;
    }

    public PackageId PackageId { get; }

    public LocalContentId Id { get; }

    public string InvariantFallback { get; }

    public ReadOnlyCollection<QualifiedContentKey> References { get; }

    public ContentObjectValue Values { get; }

    /// <inheritdoc/>
    public bool Equals(StaticScenarioSource? other) =>
        other is not null && PackageId == other.PackageId && Id == other.Id &&
        InvariantFallback == other.InvariantFallback && References.SequenceEqual(other.References) &&
        Values.Equals(other.Values);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as StaticScenarioSource);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(PackageId, Id, InvariantFallback, Values);
}
