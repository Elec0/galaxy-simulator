using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionSnapshotTests
{
    [Fact]
    public void InitialSnapshotDescribesExplicitSetup()
    {
        GameSession session = GameSessionTestFixture.Create();

        GameSnapshot snapshot = session.CaptureSnapshot();

        Assert.Equal(SimulationTime.Zero, snapshot.Time);
        GameSystemSnapshot system = Assert.Single(snapshot.Systems);
        Assert.Equal(GameSessionTestFixture.System, system.Id);
        Assert.Equal("Test System", system.Name);
        GameShipSnapshot ship = Assert.Single(snapshot.Ships);
        Assert.Equal(GameSessionTestFixture.Ship, ship.Id);
        Assert.Equal(GameSessionTestFixture.Position(0, 0), ship.Position);
        Assert.Null(ship.Motion);
        Assert.Null(ship.CurrentOrder);
    }

    [Fact]
    public void SnapshotCollectionsCannotBeModified()
    {
        GameSnapshot snapshot = GameSessionTestFixture.Create().CaptureSnapshot();
        var systems = Assert.IsAssignableFrom<IList<GameSystemSnapshot>>(snapshot.Systems);
        var ships = Assert.IsAssignableFrom<IList<GameShipSnapshot>>(snapshot.Ships);

        Assert.Throws<NotSupportedException>(() =>
            systems.Add(new GameSystemSnapshot(new SystemId(99), "Injected")));
        Assert.Throws<NotSupportedException>(() =>
            ships.Add(new GameShipSnapshot(
                new ShipId(99),
                GameSessionTestFixture.Position(0, 0),
                null,
                null)));
    }

    [Fact]
    public void SetupRejectsShipsInUnknownSystems()
    {
        var ship = new InitialShipSetup(
            GameSessionTestFixture.Ship,
            new SystemPosition(
                new SystemId(2),
                new SpatialPosition(
                    new SpatialCoordinate(0),
                    new SpatialCoordinate(0))));

        Assert.Throws<ArgumentException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [ship]));
    }
}
