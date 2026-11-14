using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GalaxyCommand.Client;

/// <summary>Shared development presentation transform that preserves named arguments and literal dynamic values.</summary>
internal static partial class PresentationMessagePseudolocalizer
{
    /// <summary>
    /// Formats a raw catalog template, optionally accenting and expanding its
    /// literal spans before inserting semantic values. The caller validates any
    /// restricted markup; this transform preserves approved b/i/u template tags.
    /// </summary>
    internal static string Format(string template, IReadOnlyDictionary<string, object> arguments, CultureInfo culture,
        bool pseudolocalize, bool pseudoRightToLeft, bool restrictedRichText = false)
    {
        string formatted = PresentationMessageFormatter.Format(template, arguments, culture);
        if (pseudolocalize)
            formatted = PresentationMessageFormatter.Format(Transform(template, restrictedRichText, formatted.Length), arguments, culture);
        return pseudoRightToLeft ? "\u2067" + formatted + "\u2069" : formatted;
    }

    /// <summary>Pads the complete displayed message by at least thirty percent without accenting placeholders or dynamic names.</summary>
    private static string Transform(string template, bool restrictedRichText, int formattedLength)
    {
        var builder = new StringBuilder(restrictedRichText ? "[lb]" : "[");
        int copied = 0;
        Regex protectedTokens = restrictedRichText ? RichTextToken() : Placeholder();
        foreach (Match match in protectedTokens.Matches(template))
        {
            AppendAccented(template.AsSpan(copied, match.Index - copied), builder);
            builder.Append(match.Value);
            copied = match.Index + match.Length;
        }
        AppendAccented(template.AsSpan(copied), builder);
        builder.Append('~', Math.Max(0, (int)Math.Ceiling(formattedLength * 0.3m) - 2));
        return builder.Append(restrictedRichText ? "[rb]" : "]").ToString();
    }

    /// <summary>Substitutes development accents without rewriting numbers or structural punctuation.</summary>
    private static void AppendAccented(ReadOnlySpan<char> value, StringBuilder builder)
    {
        const string original = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string accented = "áƀçďéƒğħíĵķľḿñóþɋŕšţúṽẃẋýžÁƁÇĎÉƑĞĦÍĴĶĽḾÑÓÞɊŔŠŢÚṼẂẊÝŽ";
        foreach (char character in value)
        {
            int index = original.IndexOf(character, StringComparison.Ordinal);
            builder.Append(index < 0 ? character : accented[index]);
        }
    }

    [GeneratedRegex("\\{([a-z][a-z0-9_]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
    [GeneratedRegex("\\{([a-z][a-z0-9_]*)\\}|\\[/?[biu]\\]", RegexOptions.CultureInvariant)]
    private static partial Regex RichTextToken();
}
