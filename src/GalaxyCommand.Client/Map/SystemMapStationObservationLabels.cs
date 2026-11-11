using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>A system-map label built only from an approved, retained station exterior.</summary>
public sealed record SystemMapStationObservationLabel(SystemPosition Position, string Text);

/// <summary>Resolves a semantic message key and its named typed values through the application's locale formatter.</summary>
public delegate string SystemMapObservationText(string key, params (string Name, object Value)[] arguments);

/// <summary>Projects station discoveries without consulting current station authority or inferring a removal cause.</summary>
public static class SystemMapStationObservationLabels
{
    /// <summary>
    /// Captures localized labels in stable entity order for one system. Stale and
    /// missing records retain their observed exterior and timestamp rather than using the current simulation time.
    /// </summary>
    public static IReadOnlyList<SystemMapStationObservationLabel> Capture(
        IReadOnlyList<ObservedStationSnapshot> observations, SystemId system, SystemMapObservationText text)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(text);
        return Array.AsReadOnly(observations.Where(item => item.View.Position.SystemId == system)
            .OrderBy(item => item.View.EntityId.Value).Select(observation => Label(observation, text)).ToArray());
    }

    /// <summary>Names absence without implying destruction, operational status, or an inferred removal time.</summary>
    private static SystemMapStationObservationLabel Label(ObservedStationSnapshot observation, SystemMapObservationText text)
    {
        string stateKey = observation.State switch
        {
            SensorObservationState.Live => "ui.observation.current",
            SensorObservationState.Stale => "ui.observation.stale",
            SensorObservationState.ConfirmedMissing => "ui.observation.confirmed_missing",
            _ => throw new ArgumentOutOfRangeException(nameof(observation), "Unknown station observation state."),
        };
        StationExteriorViewSnapshot view = observation.View;
        string label = text("ui.map.station_marker", ("station", view.EntityId.Value), ("name", view.Name),
            ("state", text(stateKey)), ("observed", new SimulationDuration(view.ObservedAt.Milliseconds)));
        return new(view.Position, label);
    }
}
