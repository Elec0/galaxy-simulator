using System.Collections.ObjectModel;

namespace GalaxyCommand.Content;

/// <summary>
/// Represents a format-neutral authored value that does not expose tokens from
/// any physical adapter.
/// </summary>
public abstract class ContentValue;

/// <summary>Represents an authored null value.</summary>
public sealed class ContentNullValue : ContentValue
{
    private ContentNullValue()
    {
    }

    /// <summary>Gets the single immutable null value.</summary>
    public static ContentNullValue Instance { get; } = new();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ContentNullValue;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;
}

/// <summary>Represents an authored Boolean value.</summary>
public sealed class ContentBooleanValue(bool value) : ContentValue
{
    public bool Value { get; } = value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ContentBooleanValue other && Value == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>Represents an authored decimal value exactly representable by .NET decimal.</summary>
public sealed class ContentNumberValue(decimal value) : ContentValue
{
    public decimal Value { get; } = value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ContentNumberValue other && Value == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>Represents an authored string value.</summary>
public sealed class ContentStringValue : ContentValue
{
    /// <summary>Creates a non-null authored string.</summary>
    public ContentStringValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ContentStringValue other && Value == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}

/// <summary>Represents an immutable ordered authored array.</summary>
public sealed class ContentArrayValue : ContentValue, IEquatable<ContentArrayValue>
{
    /// <summary>Creates an array by defensively copying its items.</summary>
    public ContentArrayValue(IEnumerable<ContentValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Array.AsReadOnly(items.ToArray());
    }

    /// <summary>Gets authored items in their significant array order.</summary>
    public ReadOnlyCollection<ContentValue> Items { get; }

    /// <inheritdoc/>
    public bool Equals(ContentArrayValue? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentArrayValue);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (ContentValue item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents immutable authored properties canonicalized by ordinal name.
/// </summary>
public sealed class ContentObjectValue : ContentValue, IEquatable<ContentObjectValue>
{
    /// <summary>
    /// Creates an object whose property order is independent from adapter input
    /// order and filesystem or worker behavior.
    /// </summary>
    public ContentObjectValue(IEnumerable<KeyValuePair<string, ContentValue>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        SortedDictionary<string, ContentValue> sorted = new(StringComparer.Ordinal);
        foreach ((string name, ContentValue value) in properties)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(value);
            if (!sorted.TryAdd(name, value))
            {
                throw new ArgumentException("Content object property names must be unique.", nameof(properties));
            }
        }

        Properties = new ReadOnlyDictionary<string, ContentValue>(sorted);
    }

    /// <summary>Gets properties in canonical ordinal-name order.</summary>
    public ReadOnlyDictionary<string, ContentValue> Properties { get; }

    /// <inheritdoc/>
    public bool Equals(ContentObjectValue? other) =>
        other is not null && Properties.SequenceEqual(other.Properties);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentObjectValue);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach ((string name, ContentValue value) in Properties)
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
