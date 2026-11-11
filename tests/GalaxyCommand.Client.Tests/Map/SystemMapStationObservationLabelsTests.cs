using System.Globalization;
using GalaxyCommand.Client;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class SystemMapStationObservationLabelsTests
{
    [Theory]
    [InlineData(SensorObservationState.Live, "current")]
    [InlineData(SensorObservationState.Stale, "stale")]
    [InlineData(SensorObservationState.ConfirmedMissing, "confirmed missing")]
    public void LabelsDescribeOnlyRetainedExteriorAndItsObservationTime(SensorObservationState state, string description)
    {
        ObservedStationSnapshot observation = Observation(7, 1, state);

        SystemMapStationObservationLabel label = Assert.Single(SystemMapStationObservationLabels.Capture([observation], new(1), English));

        Assert.Equal(observation.View.Position, label.Position);
        Assert.Equal($"Station 7 Observed Station | {description} | observed 1.25s", label.Text);
    }

    [Fact]
    public void LabelsIncludeOnlyTheRequestedSystemAndRetainStableEntityOrder()
    {
        ObservedStationSnapshot[] observations = [
            Observation(9, 1, SensorObservationState.Stale),
            Observation(8, 2, SensorObservationState.Live),
            Observation(7, 1, SensorObservationState.ConfirmedMissing),
        ];

        IReadOnlyList<SystemMapStationObservationLabel> labels = SystemMapStationObservationLabels.Capture(observations, new(1), English);

        Assert.Collection(labels,
            label => Assert.StartsWith("Station 7 ", label.Text),
            label => Assert.StartsWith("Station 9 ", label.Text));
        Assert.All(labels, label => Assert.Equal(new SystemId(1), label.Position.SystemId));
    }

    [Fact]
    public void LabelTemplatesAndStateTextUseTheSharedLocaleFormatter()
    {
        var templates = new Dictionary<string, string>
        {
            ["ui.map.station_marker"] = "{state} : {name} ({station}) ; vu {observed}s",
            ["ui.observation.stale"] = "ancienne observation",
        };
        string Localized(string key, params (string Name, object Value)[] arguments) =>
            PresentationMessageFormatter.Format(templates[key], arguments.ToDictionary(item => item.Name, item => item.Value),
                CultureInfo.GetCultureInfo("fr-FR"));

        SystemMapStationObservationLabel label = Assert.Single(SystemMapStationObservationLabels.Capture(
            [Observation(7, 1, SensorObservationState.Stale)], new(1), Localized));

        Assert.Equal("ancienne observation : Observed Station (7) ; vu 1,25s", label.Text);
    }

    private static string English(string key, params (string Name, object Value)[] arguments)
    {
        string template = key switch
        {
            "ui.map.station_marker" => "Station {station} {name} | {state} | observed {observed}s",
            "ui.observation.current" => "current",
            "ui.observation.stale" => "stale",
            "ui.observation.confirmed_missing" => "confirmed missing",
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        return PresentationMessageFormatter.Format(template, arguments.ToDictionary(item => item.Name, item => item.Value),
            CultureInfo.InvariantCulture);
    }

    private static ObservedStationSnapshot Observation(ulong entity, ulong system, SensorObservationState state) => new(
        new(new(entity), new(entity), QualifiedContentKey.Create("test", "station", "observed"), "Observed Station",
            new(2), new(new(system), new(new(10), new(20))), StationPhase.Operational, new(1250), [], []), state);
}
