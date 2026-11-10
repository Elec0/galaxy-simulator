using System.Globalization;
using GalaxyCommand.Client;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class PresentationMessageFormatterTests
{
    [Fact]
    public void CompleteTemplatesMayReorderAndRepeatNamedArgumentsWithoutReinterpretingLiteralValues()
    {
        Assert.Equal("3 x {quantity} / 3", PresentationMessageFormatter.Format("{quantity} x {name} / {quantity}",
            new Dictionary<string, object> { ["name"] = "{quantity}", ["quantity"] = new Quantity(3) }, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TemplateArgumentsMustMatchExactlyAndHaveSupportedTypedValues()
    {
        Assert.Throws<FormatException>(() => PresentationMessageFormatter.Format("{name}", new Dictionary<string, object>(), CultureInfo.InvariantCulture));
        Assert.Throws<FormatException>(() => PresentationMessageFormatter.Format("plain", new Dictionary<string, object> { ["name"] = "extra" }, CultureInfo.InvariantCulture));
        Assert.Throws<FormatException>(() => PresentationMessageFormatter.Format("{broken", new Dictionary<string, object>(), CultureInfo.InvariantCulture));
        Assert.Throws<ArgumentException>(() => PresentationMessageFormatter.Format("{value}",
            new Dictionary<string, object> { ["value"] = new object() }, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CountsAndSimulationDurationsAreFormattedAtThePresentationBoundary()
    {
        Assert.Equal("1,200 / 1.25", PresentationMessageFormatter.Format("{count} / {seconds}",
            new Dictionary<string, object> { ["count"] = new Quantity(1200), ["seconds"] = new SimulationDuration(1250) }, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("1,25", PresentationMessageFormatter.Format("{seconds}",
            new Dictionary<string, object> { ["seconds"] = new SimulationDuration(1250) }, CultureInfo.GetCultureInfo("fr-FR")));
    }
}
