using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SensorDeployableEconomyTests
{
    [Fact]
    public void EconomicCargoLoadInvalidatesAnAcceptedPickupAtItsOwningBoundary()
    {
        var sensor = new SensorDeployableDefinition(QualifiedContentKey.Create("test", "sensor-deployable", "satellite"),
            "Satellite", new Quantity(1), 2500, 30_000);
        var actorDesign = new ShipDesign(new ConstructionDesignId(1), "Actor", new ConstructionRecipe([], new Work(1)),
            new Quantity(4), GameSessionTestFixture.ManeuverCapability, 10_000, 5000);
        var material = new MaterialId(1);
        var factoryDesign = new ShipDesign(new ConstructionDesignId(2), "Factory Output",
            new ConstructionRecipe([new KeyValuePair<MaterialId, Quantity>(material, new Quantity(1))], new Work(1)),
            new Quantity(4), GameSessionTestFixture.ManeuverCapability, 10_000, 5000);
        var economy = new GameSessionEconomySetup(
            [new InitialInventorySetup(new InventoryId(3), new Quantity(20), []),
                new InitialInventorySetup(new InventoryId(4), new Quantity(20), [])],
            [new EconomyFacilitySetup(new FacilityId(1), new InventoryId(3), new LocationId(1), GameSessionTestFixture.Position(0, 0)),
                new EconomyFacilitySetup(new FacilityId(2), new InventoryId(4), new LocationId(2), GameSessionTestFixture.Position(0, 0))],
            [new ProductionFacilitySetup(new FacilityId(1), new Recipe([], material, new Quantity(1), new Work(1)), new Throughput(1), false)],
            [new ConstructionFacilitySetup(new FacilityId(2), new Throughput(1))], [factoryDesign],
            [new InitialConstructionOrderSetup(new FacilityId(2), factoryDesign.Id)],
            [new InitialFreighterSetup(new ShipId(2), new LocationId(2))], new FixedNavigation(),
            new TransportTiming(SimulationDuration.Zero, new TransferRate(1), new TransferRate(1)));
        var setup = new GameSessionSetup([new StarSystem(GameSessionTestFixture.System, "Test")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), GameSessionTestFixture.Principal, actorDesign,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController, cargo: [sensor.Physical]),
                new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), GameSessionTestFixture.Principal, actorDesign,
                    GameSessionTestFixture.Position(15_000, 0), GameSessionTestFixture.PlayerController,
                    cargo: [sensor.Physical, sensor.Physical, sensor.Physical])], new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(new FacilityId(2), GameSessionTestFixture.Principal, GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder, [factoryDesign])],
            economy, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 128, GameSessionTestFixture.Player, [sensor]);
        using var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        session.SubmitCommand(GameSessionTestFixture.Player, new DeploySensorCommand(new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(SimulationTime.Zero);
        EntityId target = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new PickupSensorCommand(new ShipId(2), target, OrderPlacement.ReplaceAll)).Result.Status);

        session.AdvanceTo(new SimulationTime(2200));

        ShipOrderSnapshot order = session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(2)).CurrentOrder!;
        Assert.Equal(ShipOrderReason.ActionCapacityUnavailable, order.Reason);
        Assert.Single(session.CaptureSnapshot().DeployedSensors);
        Assert.DoesNotContain(session.ReadFactsAfter(null, 128).Facts, fact => fact.Fact is SensorPickedUpFact);
    }

    private sealed class FixedNavigation : ILogisticsNavigation
    {
        public LogisticsTravelEstimate? Estimate(ShipId actorId, LocationId origin, LocationId destination,
            SimulationTime plannedAt) => new(origin == destination ? SimulationDuration.Zero : new SimulationDuration(100));
    }
}
