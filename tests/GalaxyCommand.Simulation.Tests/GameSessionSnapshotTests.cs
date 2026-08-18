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
        Assert.Empty(snapshot.ConnectorEndpoints);
        Assert.Empty(snapshot.TransitConnections);
        GameShipSnapshot ship = Assert.Single(snapshot.Ships);
        Assert.Equal(GameSessionTestFixture.Entity, ship.EntityId);
        Assert.Equal(GameSessionTestFixture.Ship, ship.Id);
        Assert.Equal(GameSessionTestFixture.Principal, ship.PrincipalId);
        Assert.Equal(GameSessionTestFixture.Design.Id, ship.DesignId);
        Assert.Equal(GameSessionTestFixture.CargoInventory, ship.CargoInventoryId);
        Assert.Equal(GameSessionTestFixture.Design.CargoCapacity, ship.CargoCapacity);
        Assert.Equal(GameSessionTestFixture.Position(0, 0), ship.Position);
        Assert.Null(ship.Motion);
        Assert.Null(ship.CurrentOrder);
    }

    [Fact]
    public void SnapshotCollectionsCannotBeModified()
    {
        GameSnapshot snapshot = GameSessionTestFixture.Create().CaptureSnapshot();
        var systems = Assert.IsAssignableFrom<IList<GameSystemSnapshot>>(snapshot.Systems);
        var endpoints = Assert.IsAssignableFrom<IList<ConnectorEndpointSnapshot>>(
            snapshot.ConnectorEndpoints);
        var connections = Assert.IsAssignableFrom<IList<TransitConnectionSnapshot>>(
            snapshot.TransitConnections);
        var ships = Assert.IsAssignableFrom<IList<GameShipSnapshot>>(snapshot.Ships);

        Assert.Throws<NotSupportedException>(() =>
            systems.Add(new GameSystemSnapshot(new SystemId(99), "Injected")));
        Assert.Throws<NotSupportedException>(() =>
            endpoints.Add(new ConnectorEndpointSnapshot(
                new ConnectorEndpointId(99),
                GameSessionTestFixture.Position(0, 0))));
        Assert.Throws<NotSupportedException>(() =>
            connections.Add(new TransitConnectionSnapshot(
                new TransitConnectionId(99),
                new ConnectorEndpointId(1),
                new ConnectorEndpointId(2),
                new SimulationDuration(1))));
        Assert.Throws<NotSupportedException>(() =>
            ships.Add(new GameShipSnapshot(
                new EntityId(99),
                new ShipId(99),
                new PrincipalId(99),
                new ConstructionDesignId(99),
                new InventoryId(99),
                new Quantity(99),
                new ShipSpatialSnapshotState.AtPosition(
                    GameSessionTestFixture.Position(0, 0)),
                new ActorControlSnapshot(
                    GameSessionTestFixture.PlayerController,
                    GameSessionTestFixture.PlayerController,
                    null,
                    null,
                    default),
                null,
                Array.Empty<ShipOrderSnapshot>(),
                Array.Empty<ShipOrderSnapshot>())));
    }

    [Fact]
    public void SetupRejectsShipsInUnknownSystems()
    {
        var ship = new InitialShipSetup(
            GameSessionTestFixture.Entity,
            GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory,
            GameSessionTestFixture.Principal,
            GameSessionTestFixture.Design,
            new SystemPosition(
                new SystemId(2),
                new SpatialPosition(
                    new SpatialCoordinate(0),
                    new SpatialCoordinate(0))),
            GameSessionTestFixture.PlayerController);

        Assert.Throws<ArgumentException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [ship],
                GameSessionTestFixture.Relationships,
                GameSessionTestFixture.RootSeed,
                factRetentionCapacity: 256));
    }

    [Fact]
    public void SetupRejectsConnectorEndpointsInUnknownSystems()
    {
        var topology = new ConnectorTopology(
            [
                new ConnectorEndpoint(
                    new ConnectorEndpointId(1),
                    new SystemPosition(
                        new SystemId(2),
                        new SpatialPosition())),
            ],
            Array.Empty<TransitConnection>());

        Assert.Throws<ArgumentException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [
                    new InitialShipSetup(
                        GameSessionTestFixture.Entity,
                        GameSessionTestFixture.Ship,
                        GameSessionTestFixture.CargoInventory,
                        GameSessionTestFixture.Principal,
                        GameSessionTestFixture.Design,
                        GameSessionTestFixture.Position(0, 0),
                        GameSessionTestFixture.PlayerController),
                ],
                topology,
                GameSessionTestFixture.Relationships,
                GameSessionTestFixture.RootSeed,
                factRetentionCapacity: 256));
    }

    [Fact]
    public void SetupRequiresExplicitPositiveFactRetentionCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [
                    new InitialShipSetup(
                        GameSessionTestFixture.Entity,
                        GameSessionTestFixture.Ship,
                        GameSessionTestFixture.CargoInventory,
                        GameSessionTestFixture.Principal,
                        GameSessionTestFixture.Design,
                        GameSessionTestFixture.Position(0, 0),
                        GameSessionTestFixture.PlayerController),
                ],
                GameSessionTestFixture.Relationships,
                GameSessionTestFixture.RootSeed,
                factRetentionCapacity: 0));
    }
}
