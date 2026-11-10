using System.Globalization;
using GalaxyCommand.Client;
using GalaxyCommand.Content;
using Godot;

namespace GalaxyCommand.GodotClient;

/// <summary>Shared presentation-only gettext lookup, English fallback and exact named-value formatting.</summary>
internal static class UiLocalization
{
    private static readonly HashSet<string> Diagnostics = [];

    /// <summary>Resolves a complete semantic-key message; missing English keys remain visible and are diagnosed once.</summary>
    internal static string Text(string key, params (string Name, object Value)[] arguments)
    {
        string translated = TranslationServer.Translate(key).ToString();
        string english = TranslationServer.FindTranslations("en", true).Select(translation => translation.GetMessage(key).ToString())
            .FirstOrDefault(message => !string.IsNullOrEmpty(message)) ?? key;
        if (string.IsNullOrEmpty(english)) english = key;
        if (translated == key && english == key)
        {
            Diagnose(key, "Missing application-owned English message.");
            return key;
        }
        if (translated == key) translated = english;
        var values = arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal);
        try { return PresentationMessageFormatter.Format(translated, values, Culture); }
        catch (FormatException)
        {
            Diagnose(key, "Translation placeholders differ from the declared argument contract.");
            return PresentationMessageFormatter.Format(english, values, Culture);
        }
    }

    /// <summary>Uses an authored invariant fallback only when the package has no translation for its own stable name key.</summary>
    internal static string ContentName(QualifiedContentKey key, IReadOnlyDictionary<QualifiedContentKey, string> fallbacks)
    {
        string resource = key + ".name";
        string translated = TranslationServer.Translate(resource).ToString();
        return translated != resource ? translated : fallbacks.GetValueOrDefault(key, key.ToString());
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
