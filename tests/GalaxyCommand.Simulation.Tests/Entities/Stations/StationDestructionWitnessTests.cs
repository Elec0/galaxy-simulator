using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationDestructionWitnessTests
{
    [Theory]
    [InlineData("unallocated-module")]
    [InlineData("foreign-origin-module")]
    [InlineData("fabricated-project")]
    [InlineData("fabricated-consumption")]
    [InlineData("station-identity")]
    [InlineData("module-key")]
    public void RestoreRejectsUnexplainedDestructionBeforeImageWithoutChangingItsCargoDelta(string mutation)
    {
        using GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        Assert.True(session.CommitStationDestruction(StationDestructionSessionTests.Request(session, station, false)).WasApplied);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        StationDestructionReceipt receipt = Assert.Single(checkpoint.Stations!.Destructions);
        StationSnapshot before = receipt.Before;
        switch (mutation)
        {
            case "unallocated-module":
            case "foreign-origin-module":
                var fake = new StationModuleSnapshot(mutation == "unallocated-module" ? new(checkpoint.Stations.ModuleIds.NextValue!.Value) : new(999),
                    QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage"), StationModulePhase.Operational, true, 1, null, null);
                before = before with { Modules = [.. before.Modules, fake] };
                if (mutation == "foreign-origin-module") checkpoint = checkpoint with { Stations = checkpoint.Stations with { ModuleIds = new(1_001) } };
                break;
            case "fabricated-project":
                before = before with { Projects = [.. before.Projects, before.Projects[0] with { Id = new(checkpoint.Stations.ProjectIds.NextValue!.Value) }] };
                break;
            case "fabricated-consumption":
                before = before with { Projects = before.Projects.Select(project => project.Id == before.Projects[0].Id
                    ? project with { ConsumedInputs = new Dictionary<MaterialId, Quantity> { [new(1)] = new(1) } } : project).ToArray() };
                break;
            case "station-identity": before = before with { Position = GameSessionTestFixture.Position(1, 0) }; break;
            case "module-key": before = before with { Modules = before.Modules.Select(module => module with
                { DefinitionKey = QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage") }).ToArray() }; break;
        }
        checkpoint = checkpoint with { Stations = checkpoint.Stations with { Destructions = [receipt with { Before = before }] } };
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);
        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }
    [Fact]
    public void BeforeImageCannotImportALaterOwnModuleAndProjectEvenWhenCargoIsUnchanged()
    {
        using GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        Assert.True(session.CommitStationDestruction(StationDestructionSessionTests.Request(session, station, false)).WasApplied);
        BuildZeroStorage(session);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var valid = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(valid.IsSuccess, valid.Failure?.Message);
        valid.Value?.Dispose();
        StationDestructionReceipt receipt = Assert.Single(checkpoint.Stations!.Destructions);
        StationSnapshot current = Assert.Single(session.CaptureSnapshot().Stations);
        var forged = receipt.Before with
        {
            Modules = [.. receipt.Before.Modules, Assert.Single(current.Modules)],
            Projects = current.Projects,
        };
        var result = GameSession.RestoreCheckpoint(checkpoint with { Stations = checkpoint.Stations with
            { Destructions = [receipt with { Before = forged }] } });
        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void LaterBeforeImageCannotResurrectAPreviouslyDestroyedZeroStorageModule()
    {
        using GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        BuildZeroStorage(session);
        StationModuleSnapshot zero = Assert.Single(session.CaptureSnapshot().Stations).Modules.Single(module => module.DefinitionKey.LocalId.Value == "zero-storage");
        station = Assert.Single(session.CaptureSnapshot().Stations);
        var first = StationDestructionSessionTests.Request(session, station, false) with { Modules = [zero.Id] };
        Assert.True(session.CommitStationDestruction(first).WasApplied);
        station = Assert.Single(session.CaptureSnapshot().Stations);
        var second = StationDestructionSessionTests.Request(session, station, false) with { Occurrence = 2 };
        Assert.True(session.CommitStationDestruction(second).WasApplied);
        var checkpoint = session.CaptureCheckpoint().Value!;
        var valid = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(valid.IsSuccess, valid.Failure?.Message);
        valid.Value?.Dispose();
        var receipt = checkpoint.Stations!.Destructions[1];
        var result = GameSession.RestoreCheckpoint(checkpoint with { Stations = checkpoint.Stations with
            { Destructions = [checkpoint.Stations.Destructions[0], receipt with
                { Before = receipt.Before with { Modules = [.. receipt.Before.Modules, zero] } }] } });
        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancelledOrDestroyedProjectBeforeImageCannotClaimLessWorkThanItsRetainedTerminalState(bool cancelBeforeLoss)
    {
        using GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        var construct = session.SubmitCommand(GameSessionTestFixture.Player, new ConstructStationModuleCommand(
            GameSessionTestFixture.Principal, station.Id, station.Revision,
            QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage")));
        Assert.Equal(CommandResultStatus.Accepted, construct.Result.Status);
        station = Assert.Single(session.CaptureSnapshot().Stations);
        var project = station.Projects[^1];
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, project.Id, station.Revision, new(1), new(0))).Result.Status);
        session.AdvanceTo(session.CurrentTime.Add(new(1)));
        station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(new Work(1), station.Projects[^1].CompletedWork);
        if (cancelBeforeLoss)
            Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
                new ControlStationProjectCommand(GameSessionTestFixture.Principal, station.Id, project.Id, station.Revision, StationProjectAction.Cancel)).Result.Status);
        station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.True(session.CommitStationDestruction(StationDestructionSessionTests.Request(session, station, false)).WasApplied);
        var checkpoint = session.CaptureCheckpoint().Value!;
        var receipt = Assert.Single(checkpoint.Stations!.Destructions);
        var before = receipt.Before with { Projects = receipt.Before.Projects.Select(value => value.Id == project.Id
            ? value with { CompletedWork = new(0) } : value).ToArray() };
        var result = GameSession.RestoreCheckpoint(checkpoint with { Stations = checkpoint.Stations with
            { Destructions = [receipt with { Before = before }] } });
        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void DistinctAdmissionsMayReuseTheSameImmutableCreateCommandObject()
    {
        using GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        Assert.True(session.CommitStationDestruction(StationDestructionSessionTests.Request(session, station, false)).WasApplied);
        var command = session.CaptureCheckpoint().Value!.Stations!.Receipts.Select(value => value.Request.Command)
            .OfType<CreateStationSiteCommand>().Single();
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player, command).Result.Status);
        var result = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        result.Value?.Dispose();
    }

    private static void BuildZeroStorage(GameSession session)
    {
        var station = Assert.Single(session.CaptureSnapshot().Stations);
        void Accepted(GameplayCommand command) => Assert.Equal(CommandResultStatus.Accepted,
            session.SubmitCommand(GameSessionTestFixture.Player, command).Result.Status);
        Accepted(new ConstructStationModuleCommand(GameSessionTestFixture.Principal, station.Id, station.Revision,
            QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage")));
        station = Assert.Single(session.CaptureSnapshot().Stations);
        var project = station.Projects.Last(value => value.Kind == StationProjectKind.ModuleConstruction);
        Accepted(new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, project.Id, station.Revision, new(1), new(0)));
        session.AdvanceTo(session.CurrentTime.Add(new(2)));
    }

}
