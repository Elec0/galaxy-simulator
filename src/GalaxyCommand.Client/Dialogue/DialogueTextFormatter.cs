using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>Disposable localized text and logical layout direction, never simulation state.</summary>
public sealed record DialogueFormattedText(string Text, bool IsRightToLeft, bool UsedFallback);

/// <summary>Resolves authored dialogue resources and formats their explicit semantic arguments.</summary>
public static partial class DialogueTextFormatter
{
    /// <summary>
    /// Projects validated rich output into literal text for native choice buttons
    /// and accessible names. Generated bracket escapes decode once, so a literal
    /// dynamic value cannot turn into a newly interpreted authored tag.
    /// </summary>
    public static string ToPlainText(DialogueFormattedText formatted, DialogueMarkup markup)
    {
        ArgumentNullException.ThrowIfNull(formatted);
        if (!Enum.IsDefined(markup)) throw new ArgumentOutOfRangeException(nameof(markup));
        return markup == DialogueMarkup.PlainText ? formatted.Text : RenderedMarkupToken().Replace(formatted.Text,
            match => match.Value switch { "[lb]" => "[", "[rb]" => "]", _ => "" });
    }

    /// <summary>
    /// Resolves a complete line, choice label, or speaker resource for the current
    /// culture. Missing resources and invalid translated placeholders use the
    /// authored fallback. Lookup owns locale-specific plural rules. Restricted
    /// rich text permits only authored b/i/u tags; generated lb/rb escapes keep
    /// dynamic values literal. Development pseudolocalization transforms templates
    /// before inserting literal values.
    /// </summary>
    public static DialogueFormattedText Format(DialogueText text, CultureInfo culture,
        Func<string, CultureInfo, decimal?, string?> lookup, bool pseudolocalize = false, bool pseudoRightToLeft = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(lookup);
        Dictionary<string, object> arguments = text.Arguments.ToDictionary(argument => argument.Name,
            argument => ArgumentValue(argument, culture, text.Markup), StringComparer.Ordinal);
        string? translated = lookup(text.ResourceKey, culture, text.PluralSelector);
        bool usedFallback = string.IsNullOrWhiteSpace(translated) || translated == text.ResourceKey;
        string template = usedFallback ? text.InvariantFallback : translated!;
        string formatted;
        try
        {
            DialogueText.ValidateMarkup(template, text.Markup);
            formatted = PresentationMessageFormatter.Format(template, arguments, culture);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            usedFallback = true;
            template = text.InvariantFallback;
            formatted = PresentationMessageFormatter.Format(template, arguments, culture);
        }

        if (pseudolocalize || pseudoRightToLeft)
            formatted = PresentationMessagePseudolocalizer.Format(template, arguments, culture, pseudolocalize, pseudoRightToLeft,
                restrictedRichText: text.Markup == DialogueMarkup.RestrictedRichText);
        return new DialogueFormattedText(formatted, culture.TextInfo.IsRightToLeft || pseudoRightToLeft, usedFallback);
    }

    /// <summary>Adapts only the closed approved argument kinds to the shared presentation formatter.</summary>
    private static object SemanticValue(DialogueTextArgument argument) => argument switch
    {
        DialogueTextArgument.Text value => value.Value,
        DialogueTextArgument.Number value => value.Value,
        DialogueTextArgument.Count value => value.Value,
        DialogueTextArgument.Duration value => value.Value,
        _ => throw new ArgumentException("Unsupported dialogue argument kind.", nameof(argument)),
    };

    /// <summary>
    /// Formats a rich-text argument before escaping it so generated markup is
    /// never parsed as user input. Plain-text controls receive literal values.
    /// </summary>
    private static object ArgumentValue(DialogueTextArgument argument, CultureInfo culture, DialogueMarkup markup)
    {
        object value = SemanticValue(argument);
        if (markup == DialogueMarkup.PlainText)
            return value;
        string formatted = PresentationMessageFormatter.Format("{value}",
            new Dictionary<string, object> { ["value"] = value }, culture);
        var escaped = new StringBuilder(formatted.Length);
        // Single-pass replacement cannot escape brackets introduced by an earlier replacement.
        foreach (char character in formatted)
        {
            escaped.Append(character switch { '[' => "[lb]", ']' => "[rb]", _ => character.ToString() });
        }
        return escaped.ToString();
    }

    [GeneratedRegex("\\[/?[biu]\\]|\\[lb\\]|\\[rb\\]", RegexOptions.CultureInvariant)]
    private static partial Regex RenderedMarkupToken();
}
