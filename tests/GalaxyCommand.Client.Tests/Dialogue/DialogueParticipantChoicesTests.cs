using GalaxyCommand.Client;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class DialogueParticipantChoicesTests
{
    [Fact]
    public void KnownStationsIncludeRetainedStaleEvidenceWithoutOfferingConfirmedMissingParticipants()
    {
        ObservedStationSnapshot missing = Station(3, SensorObservationState.ConfirmedMissing);
        ObservedStationSnapshot stale = Station(2, SensorObservationState.Stale);
        ObservedStationSnapshot live = Station(1, SensorObservationState.Live);

        IReadOnlyList<ObservedStationSnapshot> choices = DialogueParticipantChoices.KnownStations([missing, stale, live]);

        Assert.Equal(new[] { new StationId(1), new StationId(2) }, choices.Select(choice => choice.View.Id));
        Assert.Same(live, choices[0]);
        Assert.Same(stale, choices[1]);
        Assert.Equal(SensorObservationState.Stale, choices[1].State);
        Assert.Equal(new SimulationTime(10), choices[1].View.ObservedAt);
    }

    private static ObservedStationSnapshot Station(ulong id, SensorObservationState state) => new(
        new StationExteriorViewSnapshot(new(id), new(id), QualifiedContentKey.Parse("test/station/trading-post"),
            "Trading post", new(1), new(new SystemId(1), default), StationPhase.Operational,
            new(10), [], []), state);
}
