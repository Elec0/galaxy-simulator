using System.Globalization;
using GalaxyCommand.Client;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class DialogueTextFormatterTests
{
    [Fact]
    public void LocaleSwitchChangesOnlyPresentationAndUsesExactTypedArguments()
    {
        var text = new DialogueText("dialogue.arrival", "{name}: {number}, {count}, {time}", DialogueMarkup.PlainText,
            [new DialogueTextArgument.Text("name", "Literal {number}"),
                new DialogueTextArgument.Number("number", 12.5m),
                new DialogueTextArgument.Count("count", 1200),
                new DialogueTextArgument.Duration("time", new SimulationDuration(1250))]);

        DialogueFormattedText english = DialogueTextFormatter.Format(text, CultureInfo.GetCultureInfo("en-US"),
            (_, _, _) => "{name}: {number}, {count}, {time}");
        DialogueFormattedText french = DialogueTextFormatter.Format(text, CultureInfo.GetCultureInfo("fr-FR"),
            (_, _, _) => "{time} / {number} / {count} / {name}");

        Assert.Equal("Literal {number}: 12.5, 1,200, 1.25", english.Text);
        Assert.StartsWith("1,25 / 12,5 / ", french.Text, StringComparison.Ordinal);
        Assert.EndsWith(" / Literal {number}", french.Text, StringComparison.Ordinal);
        Assert.False(english.UsedFallback);
        Assert.False(french.UsedFallback);
        Assert.Equal("{name}: {number}, {count}, {time}", text.InvariantFallback);
        Assert.Equal(12.5m, Assert.IsType<DialogueTextArgument.Number>(text.Arguments.Single(argument => argument.Name == "number")).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dialogue.speaker")]
    [InlineData("Missing {unknown}")]
    [InlineData("Malformed {name")]
    public void MissingOrInvalidTranslationUsesInvariantFallbackForNamedSpeaker(string? translation)
    {
        var speaker = new DialogueText("dialogue.speaker", "Captain {name}", DialogueMarkup.PlainText,
            [new DialogueTextArgument.Text("name", "Anika")]);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(speaker, CultureInfo.GetCultureInfo("en-US"),
            (_, _, _) => translation);

        Assert.Equal("Captain Anika", formatted.Text);
        Assert.True(formatted.UsedFallback);
    }

    [Fact]
    public void PluralLookupReceivesExactSelectorAndLocaleWithoutEnglishCountAssumptions()
    {
        var text = new DialogueText("dialogue.ships", "{count} ships", DialogueMarkup.PlainText,
            [new DialogueTextArgument.Count("count", 2)], pluralSelector: 2);
        CultureInfo culture = CultureInfo.GetCultureInfo("ar");

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, culture, (key, locale, selector) =>
        {
            Assert.Equal("dialogue.ships", key);
            Assert.Same(culture, locale);
            Assert.Equal(2m, selector);
            return "سفينتان ({count})";
        });

        Assert.StartsWith("سفينتان (", formatted.Text, StringComparison.Ordinal);
        Assert.True(formatted.IsRightToLeft);
        Assert.False(formatted.UsedFallback);
        Assert.Equal(2m, text.PluralSelector);
    }

    [Fact]
    public void PseudolocalizationExpandsAuthoredTemplateAndPreservesLiteralDynamicValues()
    {
        var text = new DialogueText("dialogue.greeting", "Welcome aboard, {name}.", DialogueMarkup.PlainText,
            [new DialogueTextArgument.Text("name", "Literal {name} <b>[i]Name[/i]</b>")]);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture,
            (_, _, _) => null, pseudolocalize: true);

        Assert.Contains("Literal {name} <b>[i]Name[/i]</b>", formatted.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Welcome aboard", formatted.Text, StringComparison.Ordinal);
        Assert.True(formatted.Text.Length >= Math.Ceiling(("Welcome aboard, .".Length * 1.3m))
            + "Literal {name} <b>[i]Name[/i]</b>".Length);
        Assert.Equal("Welcome aboard, {name}.", text.InvariantFallback);
    }

    [Fact]
    public void FakeRightToLeftPresentationDoesNotRewriteAuthoritativeText()
    {
        var text = new DialogueText("dialogue.message", "Message", DialogueMarkup.PlainText);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.GetCultureInfo("en-US"),
            (_, _, _) => null, pseudolocalize: true, pseudoRightToLeft: true);

        Assert.True(formatted.IsRightToLeft);
        Assert.StartsWith("\u2067", formatted.Text, StringComparison.Ordinal);
        Assert.EndsWith("\u2069", formatted.Text, StringComparison.Ordinal);
        Assert.Equal("Message", text.InvariantFallback);
    }

    [Fact]
    public void PseudolocalizationExpandsCompleteMessageEvenWhenLiteralArgumentsDominateItsLength()
    {
        string name = new('x', 100);
        var text = new DialogueText("dialogue.name", "Hello {name}", DialogueMarkup.PlainText,
            [new DialogueTextArgument.Text("name", name)]);
        DialogueFormattedText ordinary = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture, (_, _, _) => null);

        DialogueFormattedText pseudo = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture, (_, _, _) => null,
            pseudolocalize: true);

        Assert.Contains(name, pseudo.Text, StringComparison.Ordinal);
        Assert.True(pseudo.Text.Length >= Math.Ceiling(ordinary.Text.Length * 1.3m));
    }

    [Fact]
    public void RestrictedMarkupPreservesApprovedTagsAndEscapesDynamicValuesInOnePass()
    {
        var text = new DialogueText("dialogue.warning", "[b]{name}[/b] [i]{number}[/i] [u]Ready[/u]", DialogueMarkup.RestrictedRichText,
            [new DialogueTextArgument.Text("name", "[url]Name[/url][lb]{number}"), new DialogueTextArgument.Number("number", 12.5m)]);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.GetCultureInfo("en-US"), (_, _, _) => null);

        Assert.Equal("[b][lb]url[rb]Name[lb]/url[rb][lb]lb[rb]{number}[/b] [i]12.5[/i] [u]Ready[/u]", formatted.Text);
        Assert.True(formatted.UsedFallback);
    }

    [Theory]
    [InlineData("[url]Translated[/url]")]
    [InlineData("[color=red]Translated[/color]")]
    [InlineData("[b]Translated")]
    [InlineData("[b][i]Translated[/b][/i]")]
    [InlineData("[lb]b[rb]Translated")]
    public void UnsupportedOrMalformedTranslatedMarkupFallsBack(string translation)
    {
        var text = new DialogueText("dialogue.warning", "[b]Fallback[/b]", DialogueMarkup.RestrictedRichText);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture, (_, _, _) => translation);

        Assert.Equal("[b]Fallback[/b]", formatted.Text);
        Assert.True(formatted.UsedFallback);
    }

    [Fact]
    public void PlainTextDoesNotInterpretDynamicOrAuthoredMarkup()
    {
        var text = new DialogueText("dialogue.literal", "[b]{name}[/b]", DialogueMarkup.PlainText,
            [new DialogueTextArgument.Text("name", "[i]Literal[/i]")]);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture, (_, _, _) => null);

        Assert.Equal("[b][i]Literal[/i][/b]", formatted.Text);
    }

    [Fact]
    public void RichTextPseudolocalizationPreservesApprovedTagsAndEscapesLiteralBrackets()
    {
        var text = new DialogueText("dialogue.pseudo", "[b]Welcome {name}[/b]", DialogueMarkup.RestrictedRichText,
            [new DialogueTextArgument.Text("name", "[i]Literal[/i]")]);

        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture, (_, _, _) => null,
            pseudolocalize: true);

        Assert.Contains("[b]", formatted.Text, StringComparison.Ordinal);
        Assert.Contains("[/b]", formatted.Text, StringComparison.Ordinal);
        Assert.Contains("[lb]i[rb]Literal[lb]/i[rb]", formatted.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Welcome", formatted.Text, StringComparison.Ordinal);
        Assert.StartsWith("[lb]", formatted.Text, StringComparison.Ordinal);
        Assert.EndsWith("[rb]", formatted.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainNativeLabelsRemoveAuthoredTagsAndDecodeDynamicEscapesOnlyOnce()
    {
        var text = new DialogueText("dialogue.label", "[b]Hello {name}[/b]", DialogueMarkup.RestrictedRichText,
            [new DialogueTextArgument.Text("name", "[i]Literal[/i]")]);
        DialogueFormattedText formatted = DialogueTextFormatter.Format(text, CultureInfo.InvariantCulture, (_, _, _) => null);

        Assert.Equal("Hello [i]Literal[/i]", DialogueTextFormatter.ToPlainText(formatted, text.Markup));
        Assert.Equal(formatted.Text, DialogueTextFormatter.ToPlainText(formatted, DialogueMarkup.PlainText));
    }
}
