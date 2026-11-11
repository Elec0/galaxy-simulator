using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationRemovalDisclosureCheckpointTests
{
    [Fact]
    public void RestoreAcceptsIndependentlyDecodedOwnedStationRemovalDisclosure()
    {
        using GameSession session = CreateRemovedStation();
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        StationRemovedFact source = Removal(checkpoint);
        StationRemovedFact copy = source with { Station = Copy(source.Station) };
        Assert.NotSame(source.Station.Projects, copy.Station.Projects);
        Assert.NotSame(source.Station.Modules, copy.Station.Modules);
        Assert.NotEqual(source, copy);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(WithDisclosure(checkpoint, copy));

        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Empty(restored.CaptureSnapshot().Stations);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("revision")]
    [InlineData("project-work")]
    [InlineData("project-inputs")]
    [InlineData("project-ships")]
    [InlineData("project-modules")]
    [InlineData("modules")]
    public void RestoreRejectsAnyChangedOwnedRemovalDisclosurePayload(string changed)
    {
        using GameSession session = CreateRemovedStation();
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        StationRemovedFact source = Removal(checkpoint);
        StationSnapshot station = Copy(source.Station);
        StationProjectSnapshot[] projects = station.Projects.ToArray();
        StationProjectSnapshot project = projects[0];
        switch (changed)
        {
            case "revision": station = station with { Revision = station.Revision + 1 }; break;
            case "project-work": projects[0] = project with { RequiredWork = new(project.RequiredWork.Units + 1) }; break;
            case "project-inputs": projects[0] = project with { ConsumedInputs = new Dictionary<MaterialId, Quantity> { [new(999)] = new(1) } }; break;
            case "project-ships": projects[0] = project with { AssignedShips = [new(999)] }; break;
            case "project-modules": projects[0] = project with { AssignedModules = [new(999)] }; break;
            case "modules": station = station with { Modules = [] }; break;
        }
        StationRemovedFact changedFact = source with
        {
            Station = station with { Projects = projects },
            Reason = changed == "reason" ? StationRemovalReason.Dismantled : source.Reason,
        };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(WithDisclosure(checkpoint, changedFact));

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.playerObservation", result.Failure!.Path);
    }

    private static StationSnapshot Copy(StationSnapshot station) => station with
    {
        Modules = station.Modules.ToArray(),
        Projects = station.Projects.Select(project => project with
        {
            ConsumedInputs = project.ConsumedInputs.ToDictionary(),
            AssignedShips = project.AssignedShips.ToArray(),
            AssignedModules = project.AssignedModules?.ToArray(),
        }).ToArray(),
    };

    private static StationRemovedFact Removal(GameSessionCheckpoint checkpoint) =>
        Assert.IsType<StationRemovedFact>(Assert.Single(checkpoint.Facts.RetainedFacts,
            envelope => envelope!.Fact is StationRemovedFact)!.Fact);

    private static GameSessionCheckpoint WithDisclosure(GameSessionCheckpoint checkpoint, StationRemovedFact fact) => checkpoint with
    {
        Facts = new(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences,
            checkpoint.Facts.RetainedFacts.Select(envelope => envelope!.Fact is StationRemovedFact
                ? envelope with { PlayerDisclosure = envelope.PlayerDisclosure! with { Fact = fact } } : envelope)),
    };

    private static GameSession CreateRemovedStation()
    {
        GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        Assert.NotEmpty(station.Projects);
        Assert.NotEmpty(station.Modules);
        Assert.True(session.CommitStationDestruction(StationDestructionSessionTests.Request(session, station, destroyBase: true)).WasApplied);
        return session;
    }
}
