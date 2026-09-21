using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Stable content-facing identity for an authored principal definition.
/// </summary>
public readonly record struct PrincipalContentId
{
    /// <summary>
    /// Creates an opaque case-sensitive content identity.
    /// </summary>
    public PrincipalContentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Authored identity and presentation metadata for one relationship principal.
/// </summary>
public sealed record PrincipalDefinition
{
    /// <summary>
    /// Creates one principal definition with stable runtime and content identities.
    /// </summary>
    public PrincipalDefinition(
        PrincipalId id,
        PrincipalContentId contentId,
        string name)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        ContentId = contentId;
        Name = name;
    }

    public PrincipalId Id { get; }

    public PrincipalContentId ContentId { get; }

    public string Name { get; }
}

/// <summary>
/// Qualitative treatment derived from authoritative directional standing.
/// </summary>
