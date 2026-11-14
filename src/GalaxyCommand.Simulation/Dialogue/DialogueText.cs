using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace GalaxyCommand.Simulation;

public enum DialogueMarkup { PlainText, RestrictedRichText }

/// <summary>Explicit semantic presentation values; rendered text never becomes a condition input.</summary>
public abstract record DialogueTextArgument(string Name)
{
    public sealed record Text(string Name, string Value) : DialogueTextArgument(Name);
    public sealed record Number(string Name, decimal Value) : DialogueTextArgument(Name);
    public sealed record Count(string Name, ulong Value) : DialogueTextArgument(Name);
    public sealed record Duration(string Name, SimulationDuration Value) : DialogueTextArgument(Name);
}

/// <summary>Immutable localization resource and exact fallback argument contract.</summary>
public sealed partial class DialogueText
{
    /// <summary>Copies semantic arguments and rejects malformed or mismatched named placeholders.</summary>
    public DialogueText(string resourceKey, string invariantFallback, DialogueMarkup markup,
        IEnumerable<DialogueTextArgument>? arguments = null, decimal? pluralSelector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(invariantFallback);
        if (!Enum.IsDefined(markup)) throw new ArgumentOutOfRangeException(nameof(markup));
        ValidateMarkup(invariantFallback, markup);
        if (pluralSelector is { } selector && (selector < 0 || decimal.Truncate(selector) != selector))
            throw new ArgumentOutOfRangeException(nameof(pluralSelector));
        DialogueTextArgument[] copied = (arguments ?? []).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (DialogueTextArgument argument in copied)
        {
            if (argument is not (DialogueTextArgument.Text or DialogueTextArgument.Number
                or DialogueTextArgument.Count or DialogueTextArgument.Duration))
                throw new ArgumentException("The dialogue argument semantic kind is unsupported.", nameof(arguments));
            if (argument is null || string.IsNullOrWhiteSpace(argument.Name)
                || !ArgumentName().IsMatch(argument.Name) || !names.Add(argument.Name))
                throw new ArgumentException("Dialogue text arguments require unique valid names.", nameof(arguments));
            if (argument is DialogueTextArgument.Text { Value: null })
                throw new ArgumentException("Text argument values cannot be null.", nameof(arguments));
        }
        string remainder = Placeholder().Replace(invariantFallback, "");
        if (remainder.Contains('{', StringComparison.Ordinal) || remainder.Contains('}', StringComparison.Ordinal)
            || !names.SetEquals(Placeholder().Matches(invariantFallback).Select(match => match.Groups[1].Value)))
            throw new ArgumentException("Fallback placeholders must match the exact declared argument names.", nameof(invariantFallback));
        ResourceKey = resourceKey;
        InvariantFallback = invariantFallback;
        Markup = markup;
        Arguments = Array.AsReadOnly(copied.OrderBy(argument => argument.Name, StringComparer.Ordinal).ToArray());
        PluralSelector = pluralSelector;
    }

    public string ResourceKey { get; }
    public string InvariantFallback { get; }
    public DialogueMarkup Markup { get; }
    public ReadOnlyCollection<DialogueTextArgument> Arguments { get; }
    public decimal? PluralSelector { get; }

    /// <summary>
    /// Validates authored or translated template markup. Plain text remains literal;
    /// rich text permits only balanced, correctly nested b/i/u BBCode tags.
    /// Dynamic values must be escaped separately by the presentation formatter.
    /// </summary>
    public static void ValidateMarkup(string template, DialogueMarkup markup)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!Enum.IsDefined(markup)) throw new ArgumentOutOfRangeException(nameof(markup));
        if (markup == DialogueMarkup.PlainText) return;
        var stack = new Stack<string>();
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] == ']') throw new ArgumentException("Restricted markup contains an unmatched bracket.", nameof(template));
            if (template[index] != '[') continue;
            int end = template.IndexOf(']', index + 1);
            if (end < 0) throw new ArgumentException("Restricted markup contains an incomplete tag.", nameof(template));
            string tag = template[(index + 1)..end];
            if (tag is "b" or "i" or "u") stack.Push(tag);
            else if (tag is "/b" or "/i" or "/u")
            {
                if (stack.Count == 0 || stack.Pop() != tag[1..])
                    throw new ArgumentException("Restricted markup tags must close in nesting order.", nameof(template));
            }
            else throw new ArgumentException("Restricted markup permits only b/i/u tags.", nameof(template));
            index = end;
        }
        if (stack.Count != 0) throw new ArgumentException("Restricted markup tags must be closed.", nameof(template));
    }

    [GeneratedRegex("\\{([a-z][a-z0-9_]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
    [GeneratedRegex("\\A[a-z][a-z0-9_]*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentName();
}
