using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationTransitCheckpointTests
{
    [Fact]
    public void TransitCheckpointRetainsBuilderAssignmentAndResumesOnlyAfterReturn()
    {
        using GameSession live = Create();
        var key = QualifiedContentKey.Create("test.stations", "station", "transit-base");
        AssertAccepted(live.SubmitCommand(GameSessionTestFixture.Player, new CreateStationSiteCommand(
            GameSessionTestFixture.Principal, new(1), key, GameSessionTestFixture.Position(0, 0), new(0))));
        StationSnapshot station = Assert.Single(live.CaptureSnapshot().Stations);
        AssertAccepted(live.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            station.PrincipalId, station.Id, Assert.Single(station.Projects).Id, station.Revision, new(1), new(0))));
        live.AdvanceTo(new(250));
        Move(live, new(2));
        Assert.NotNull(Assert.Single(live.CaptureSnapshot().Ships).Transit);
        live.AdvanceTo(new(1_000));
        CheckpointResult<GameSessionCheckpoint> captured = live.CaptureCheckpoint();
        Assert.True(captured.IsSuccess, captured.Failure?.Message);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(captured.Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        Assert.Equal(new[] { new ShipId(1) }, Project(restored).AssignedShips);
        Assert.Equal(new Work(0), Project(restored).CompletedWork);
        Assert.Equal(250, Assert.Single(Assert.Single(captured.Value!.Stations!.Stations).Projects).WorkNumerator);

        live.AdvanceTo(new(5_250));
        restored.AdvanceTo(new(5_250));
        Assert.Equal(new Work(0), Project(restored).CompletedWork);
        Move(live, new(1));
        Move(restored, new(1));
        live.AdvanceTo(new(11_000));
        restored.AdvanceTo(new(11_000));

        Assert.Equal(new Work(1), Project(restored).CompletedWork);
        Assert.Equal(Project(live).CompletedWork, Project(restored).CompletedWork);
        Assert.Equal(Project(live).AssignedShips, Project(restored).AssignedShips);
        Assert.True(restored.CaptureCheckpoint().IsSuccess);
    }

    private static StationProjectSnapshot Project(GameSession session) => Assert.Single(Assert.Single(session.CaptureSnapshot().Stations).Projects);
    private static void Move(GameSession session, SystemId target) => AssertAccepted(session.SubmitCommand(
        GameSessionTestFixture.Player, new MoveShipCommand(new(1), new NavigationDestination.System(target), OrderPlacement.ReplaceAll)));
    private static void AssertAccepted(GameplayCommandRecord command) => Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);

    private static GameSession Create()
    {
        SystemPosition origin = GameSessionTestFixture.Position(0, 0);
        var destination = new SystemPosition(new(2), new(new(0), new(0)));
        var topology = new ConnectorTopology([new(new(1), origin), new(new(2), destination)],
            [new(new(1), new(1), new(2), new(5_000)), new(new(2), new(2), new(1), new(5_000))]);
        var station = new StationDefinition(QualifiedContentKey.Create("test.stations", "station", "transit-base"),
            "Transit Base", 0, new(0), new(0), new([], new(100)), 123, [], new(1), []);
        var ship = new ShipDesign(new(1), "Builder", new([], new(1)), new(10), GameSessionTestFixture.ManeuverCapability,
            1_000, 1_000, new StationConstructionCapability(100, new(1)));
        var setup = new GameSessionSetup([new(new(1), "Origin"), new(new(2), "Destination")],
            [new InitialShipSetup(new(1), new(1), new(1), GameSessionTestFixture.Principal, ship, origin,
                GameSessionTestFixture.PlayerController)], topology, [], null, GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player,
            stations: new(new([station], []), new MaterialInventoryCompatibilityMap([]), StationTerritoryPolicy.Absent));
        return new(setup, new HierarchicalNavigationPlanner(topology, new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
