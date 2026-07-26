using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionSnapshotTests
{
    [Fact]
    public void InitialSnapshotDescribesScenarioTopologyAndShips()
    {
        var session = new GameSession();
        session.AdvanceTo(SimulationTime.Zero);

        PhaseOneSnapshot snapshot = session.CaptureSnapshot();

        Assert.Equal(SimulationTime.Zero, snapshot.Time);
        Assert.Equal(["Mine", "Refinery", "Shipyard"],
            snapshot.Locations.Select(location => location.Name));
        Assert.Equal(4, snapshot.Routes.Count);
        Assert.Equal(2, snapshot.Ships.Count);
        Assert.Equal([1UL, 2UL],
            snapshot.Ships.Select(ship => ship.Location.Value));
        ConstructionSnapshot construction = Assert.Single(snapshot.Constructions);
        Assert.Equal("Phase 1 Freighter", construction.DesignName);
        Assert.Equal(ConstructionOrderStatus.WaitingForInputs, construction.Status);
        Assert.Equal(
            new Quantity(4),
            Assert.Single(construction.UnmetInputs).Value);
        Assert.All(snapshot.Ships, ship =>
            Assert.Equal(construction.DesignId, ship.DesignId));
    }

    [Fact]
    public void TravelingShipSnapshotIncludesInterpolationTimes()
    {
        var session = new GameSession();
        session.AdvanceTo(new SimulationTime(50_000));

        PhaseOneSnapshot snapshot = session.CaptureSnapshot();
        ShipSnapshot traveling = Assert.Single(
            snapshot.Ships,
            ship => ship.TransportStatus is TransportJobStatus.TravelingToSource
                or TransportJobStatus.TravelingToDestination);

        Assert.NotNull(traveling.CurrentRoute);
        Assert.True(traveling.DepartedAt < snapshot.Time);
        Assert.True(traveling.ArrivesAt > snapshot.Time);
    }

    [Fact]
    public void CompletedSnapshotContainsConstructedShipAtShipyard()
    {
        var session = new GameSession();
        session.AdvanceTo(new SimulationTime(1_000_000));

        PhaseOneSnapshot snapshot = session.CaptureSnapshot();

        ShipSnapshot constructed = Assert.Single(
            snapshot.Ships,
            ship => ship.Id == new ShipId(3));
        LocationSnapshot shipyard = Assert.Single(
            snapshot.Locations,
            location => location.Name == "Shipyard");
        Assert.Equal(shipyard.Id, constructed.Location);
        Assert.Null(constructed.ActiveTransportJob);
        Assert.Empty(snapshot.Constructions);
    }

    [Fact]
    public void SnapshotCollectionsCannotBeModified()
    {
        var session = new GameSession();
        PhaseOneSnapshot snapshot = session.CaptureSnapshot();
        var locations = Assert.IsAssignableFrom<IList<LocationSnapshot>>(snapshot.Locations);

        Assert.Throws<NotSupportedException>(() =>
            locations.Add(new LocationSnapshot(new LocationId(99), "Injected")));
    }
}
