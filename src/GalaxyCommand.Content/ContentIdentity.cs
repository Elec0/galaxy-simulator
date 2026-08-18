namespace GalaxyCommand.Content;

/// <summary>
/// Identifies one content package independently from its filesystem location.
/// </summary>
public sealed record PackageId
{
    private PackageId(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the canonical lowercase ASCII package identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a dotted package identifier whose segments use lowercase ASCII
    /// letters, digits, and interior hyphens.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is outside the approved grammar.
    /// </exception>
    public static PackageId Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ContentIdentifierGrammar.IsPackageId(value))
        {
            throw new ArgumentException("Package identifiers must use the approved lowercase ASCII grammar.", nameof(value));
        }

        return new PackageId(value);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies a registered category of declarative content.
/// </summary>
public sealed record ContentKind
{
    private ContentKind(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the canonical lowercase ASCII kind token.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a hyphenated lowercase ASCII kind token.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is outside the approved grammar.
    /// </exception>
    public static ContentKind Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ContentIdentifierGrammar.IsHyphenatedId(value))
        {
            throw new ArgumentException("Content kinds must use the approved lowercase ASCII grammar.", nameof(value));
        }

        return new ContentKind(value);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies one definition within a package and content kind.
/// </summary>
public sealed record LocalContentId
{
    private LocalContentId(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the canonical lowercase ASCII local identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a hyphenated lowercase ASCII local identifier.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is outside the approved grammar.
    /// </exception>
    public static LocalContentId Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ContentIdentifierGrammar.IsHyphenatedId(value))
        {
            throw new ArgumentException("Local content identifiers must use the approved lowercase ASCII grammar.", nameof(value));
        }

        return new LocalContentId(value);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// Provides the stable package, kind, and local identity of one definition.
/// </summary>
public sealed record QualifiedContentKey
{
    private QualifiedContentKey(PackageId packageId, ContentKind contentKind, LocalContentId localId)
    {
        PackageId = packageId;
        ContentKind = contentKind;
        LocalId = localId;
    }

    /// <summary>Gets the owning package identifier.</summary>
    public PackageId PackageId { get; }

    /// <summary>Gets the registered content-kind token.</summary>
    public ContentKind ContentKind { get; }

    /// <summary>Gets the identifier local to the package and kind.</summary>
    public LocalContentId LocalId { get; }

    /// <summary>
    /// Validates and creates one qualified content key.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when any component is outside the approved grammar.
    /// </exception>
    public static QualifiedContentKey Create(string packageId, string contentKind, string localId) =>
        new(PackageId.Create(packageId), ContentKind.Create(contentKind), LocalContentId.Create(localId));

    /// <summary>
    /// Parses the canonical three-component external representation.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the value does not contain exactly three valid components.
    /// </exception>
    public static QualifiedContentKey Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] components = value.Split('/');
        if (components.Length != 3)
        {
            throw new ArgumentException("Qualified content keys must contain exactly three components.", nameof(value));
        }

        return Create(components[0], components[1], components[2]);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{PackageId}/{ContentKind}/{LocalId}";
}

internal static class ContentIdentifierGrammar
{
    /// <summary>
    /// Validates every dotted package segment independently so dots cannot be
    /// mistaken for part of a local identity component.
    /// </summary>
    internal static bool IsPackageId(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        string[] segments = value.Split('.');
        return segments.Length > 0 && segments.All(IsHyphenatedId);
    }

    /// <summary>
    /// Enforces the shared lowercase ASCII grammar without culture-sensitive
    /// character classes or normalization.
    /// </summary>
    internal static bool IsHyphenatedId(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        bool previousWasHyphen = false;
        foreach (char character in value)
        {
            bool isLowercaseAscii = character is >= 'a' and <= 'z';
            bool isDigit = character is >= '0' and <= '9';
            bool isHyphen = character == '-';
            if (!isLowercaseAscii && !isDigit && !isHyphen)
            {
                return false;
            }

            if (isHyphen && previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = isHyphen;
        }

        return true;
    }
}
