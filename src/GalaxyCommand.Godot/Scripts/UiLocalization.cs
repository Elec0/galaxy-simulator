using System.Globalization;
using GalaxyCommand.Client;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

/// <summary>Shared presentation-only gettext lookup, English fallback and exact named-value formatting.</summary>
internal static class UiLocalization
{
    private static readonly HashSet<string> Diagnostics = [];

    /// <summary>Resolves authored dialogue resources without translating literal participant names or reparsing them as markup.</summary>
    internal static DialogueFormattedText Dialogue(DialogueText text)
    {
        TranslationDomain domain = TranslationServer.GetOrAddDomain("");
        // Read raw catalog resources below so engine pseudolocalization cannot
        // transform a missing resource key or literal dynamic arguments first.
        return DialogueTextFormatter.Format(text, Culture, LookupTemplate,
            pseudolocalize: domain.PseudolocalizationEnabled,
            pseudoRightToLeft: domain.PseudolocalizationEnabled && domain.PseudolocalizationFakeBidiEnabled);
    }

    /// <summary>Delegates plural choice to the locale catalog and rejects counts outside the engine's supported range.</summary>
    private static string? LookupTemplate(string key, CultureInfo culture, decimal? selector)
    {
        if (selector > int.MaxValue)
        {
            Diagnose(key, "Plural selector exceeds the presentation engine's supported range; using invariant fallback.");
            return null;
        }
        string locale = culture.Name.Replace('-', '_');
        foreach (Translation translation in TranslationServer.FindTranslations(locale, false)
            .OrderByDescending(translation => TranslationServer.CompareLocales(locale, translation.Locale)))
        {
            string value = selector is null ? translation.GetMessage(key).ToString()
                : translation.GetPluralMessage(key, key, (int)selector.Value).ToString();
            if (!string.IsNullOrWhiteSpace(value) && value != key) return value;
        }
        return null;
    }

    /// <summary>Resolves a complete semantic-key message; missing English keys remain visible and are diagnosed once.</summary>
    internal static string Text(string key, params (string Name, object Value)[] arguments)
    {
        CultureInfo culture = Culture;
        string english = LookupTemplate(key, CultureInfo.GetCultureInfo("en"), null) ?? key;
        string? translated = LookupTemplate(key, culture, null);
        if (translated is null && english == key)
        {
            Diagnose(key, "Missing application-owned English message.");
            return key;
        }
        var values = arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal);
        try { return FormatApplicationTemplate(translated ?? english, values, culture); }
        catch (FormatException)
        {
            Diagnose(key, "Translation placeholders differ from the declared argument contract.");
            return FormatApplicationTemplate(english, values, culture);
        }
    }

    /// <summary>Applies the shared client pseudo transform to raw templates before named semantic values are inserted.</summary>
    private static string FormatApplicationTemplate(string template, IReadOnlyDictionary<string, object> values, CultureInfo culture)
    {
        TranslationDomain domain = TranslationServer.GetOrAddDomain("");
        return PresentationMessagePseudolocalizer.Format(template, values, culture, domain.PseudolocalizationEnabled,
            domain.PseudolocalizationEnabled && domain.PseudolocalizationFakeBidiEnabled);
    }

    /// <summary>Uses an authored invariant fallback only when the package has no translation for its own stable name key.</summary>
    internal static string ContentName(QualifiedContentKey key, IReadOnlyDictionary<QualifiedContentKey, string> fallbacks)
    {
        string resource = key + ".name";
        return LookupTemplate(resource, Culture, null) ?? fallbacks.GetValueOrDefault(key, key.ToString());
    }

    /// <summary>Maps Godot locale spelling to .NET numeric formatting without introducing locale into session authority.</summary>
    internal static CultureInfo Culture
    {
        get
        {
            string locale = TranslationServer.GetLocale();
            // An unsupported OS locale falls back to the installed English
            // presentation contract for both wording and numeric formatting.
            if (!TranslationServer.HasTranslationForLocale(locale, false)) locale = "en";
            try { return CultureInfo.GetCultureInfo(locale.Replace('_', '-')); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }

    /// <summary>Deduplicates presentation defects per locale and key rather than repeating them every simulation frame.</summary>
    private static void Diagnose(string key, string explanation)
    {
        if (Diagnostics.Add(TranslationServer.GetLocale() + ":" + key)) GD.PushWarning($"{key}: {explanation}");
    }
}
