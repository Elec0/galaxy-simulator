using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>Derives interim participant choices exclusively from the player's disclosed presentation evidence.</summary>
internal static class DialogueParticipantChoices
{
    /// <summary>
    /// Includes current and last-observed stations in stable identity order.
    /// Stale evidence remains explicitly stale; authored conditions and ordinary
    /// session admission decide whether the selected participant is admissible.
    /// Confirmed-missing records are unavailable as participant choices.
    /// </summary>
    internal static IReadOnlyList<ObservedStationSnapshot> KnownStations(IEnumerable<ObservedStationSnapshot> stations)
    {
        ArgumentNullException.ThrowIfNull(stations);
        return Array.AsReadOnly(stations.Where(station => station.State is SensorObservationState.Live or SensorObservationState.Stale)
            .OrderBy(station => station.View.Id.Value).ToArray());
    }
}
