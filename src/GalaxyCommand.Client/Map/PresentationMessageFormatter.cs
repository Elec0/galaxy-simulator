using System.Globalization;
using System.Text.RegularExpressions;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>Formats complete plain-text templates using exact named arguments and locale-neutral simulation values.</summary>
internal static partial class PresentationMessageFormatter
{
    /// <summary>Rejects mismatched placeholders or unsupported values instead of silently changing a message contract.</summary>
    internal static string Format(string template, IReadOnlyDictionary<string, object> arguments, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(culture);
        Regex pattern = PlaceholderPattern();
        var names = pattern.Matches(template).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        string remainder = pattern.Replace(template, "");
        if (remainder.Contains('{', StringComparison.Ordinal) || remainder.Contains('}', StringComparison.Ordinal)
            || !names.SetEquals(arguments.Keys))
            throw new FormatException("Localized template placeholders must match the declared argument set exactly.");
        Dictionary<string, string> formatted = arguments.ToDictionary(pair => pair.Key, pair => FormatValue(pair.Value, culture), StringComparer.Ordinal);
        // Replace in one pass: a player/content name containing braces stays literal.
        return pattern.Replace(template, match => formatted[match.Groups[1].Value]);
    }

    /// <summary>Formats approved scalar/count/time values without reading presentation text back into simulation state.</summary>
    private static string FormatValue(object value, CultureInfo culture) => value switch
    {
        string literal => literal,
        Quantity quantity => quantity.Units.ToString("N0", culture),
        SimulationDuration duration => (duration.Milliseconds / 1000m).ToString("0.###", culture),
        int count => count.ToString("N0", culture),
        long count => count.ToString("N0", culture),
        ulong count => count.ToString("N0", culture),
        decimal number => number.ToString("0.###", culture),
        _ => throw new ArgumentException("Localized arguments require an explicit supported semantic value.", nameof(value)),
    };

    [GeneratedRegex("\\{([a-z][a-z0-9_]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
