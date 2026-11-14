using System.Globalization;
using GalaxyCommand.Client;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class PresentationMessagePseudolocalizerTests
{
    [Fact]
    public void SharedApplicationPseudoPreservesNamedPlaceholdersAndLiteralTypedValues()
    {
        var values = new Dictionary<string, object>
        {
            ["name"] = "Literal {count}",
            ["count"] = new Quantity(1200),
            ["time"] = new SimulationDuration(1250),
        };

        string formatted = PresentationMessagePseudolocalizer.Format("Ship {name}: {count} in {time}", values,
            CultureInfo.GetCultureInfo("en-US"), pseudolocalize: true, pseudoRightToLeft: true);

        Assert.Contains("Literal {count}", formatted, StringComparison.Ordinal);
        Assert.Contains("1,200", formatted, StringComparison.Ordinal);
        Assert.Contains("1.25", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("Ship ", formatted, StringComparison.Ordinal);
        Assert.StartsWith("\u2067", formatted, StringComparison.Ordinal);
        Assert.EndsWith("\u2069", formatted, StringComparison.Ordinal);
    }
}
