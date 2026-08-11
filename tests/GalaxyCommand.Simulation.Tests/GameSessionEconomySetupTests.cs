using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionEconomySetupTests
{
    [Fact]
    public void SessionSetupRetainsValidatedGenericEconomySeed()
    {
        GameSessionEconomySetup economy = CreateEconomySeed();
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [CreateInitialShip()],
            new ConnectorTopology([], []),
            [CreateMaterializationPolicy()],
            economy,
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 16);

        Assert.Same(economy, setup.Economy);
        EconomyFacilitySetup facility = Assert.Single(economy.Facilities);
        Assert.Equal(new FacilityId(1), facility.FacilityId);
        Assert.Equal(new InventoryId(2), facility.InventoryId);
        Assert.Equal(new LocationId(1), facility.LogisticsLocationId);
        Assert.Equal(GameSessionTestFixture.Design, economy.ShipDesigns[GameSessionTestFixture.Design.Id]);
        Assert.Equal(GameSessionTestFixture.Ship, Assert.Single(economy.Freighters).ShipId);
    }

    [Fact]
    public void SessionSetupRejectsConstructionWithoutMaterializationPolicy()
    {
        var error = Assert.Throws<ArgumentException>(() => new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [CreateInitialShip()],
            new ConnectorTopology([], []),
            [],
            CreateEconomySeed(),
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 16));

        Assert.Contains("materialization policy", error.Message);
    }

    [Fact]
    public void SessionOwnedConstructionMaterializesWithoutExternalProcess()
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [CreateInitialShip()],
            new ConnectorTopology([], []),
            [CreateMaterializationPolicy()],
            CreateEconomySeed(),
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 16);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator()));

        session.AdvanceTo(new SimulationTime(1_000));

        GameEventRecord completion = Assert.Single(session.EventRecords);
        Assert.Equal(ScheduledEventDisposition.Applied, completion.Disposition);
        var economic = Assert.IsType<GameEventKind.Economic>(completion.Kind);
        Assert.IsType<EconomicEvent.ConstructionComplete>(economic.Event);
        Assert.Equal(2, session.CaptureSnapshot().Ships.Count);
    }

    [Fact]
    public void RemovingInFlightFreighterReleasesTransportAndLeavesStaleEventHarmless()
    {
        MaterialId material = new(1);
        var design = new ShipDesign(
            new ConstructionDesignId(2),
            "Transport Test Ship",
            new ConstructionRecipe(
                [new KeyValuePair<MaterialId, Quantity>(material, new Quantity(1))],
                new Work(1)),
            new Quantity(10));
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [CreateInitialShip()],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(
                new FacilityId(2),
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder,
                [design])],
            CreateTransportEconomySeed(material, design),
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 16);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator()));

        session.AdvanceTo(new SimulationTime(1_000));

        var removed = Assert.IsType<EntityRemovalResult.Removed>(session.RemoveEntity(
            new EntityRemovalRequest(
                GameSessionTestFixture.Entity,
                EntityRemovalReason.Destroyed,
                EntityCargoDisposition.DiscardCargo)));
        Assert.Equal(GameSessionTestFixture.Ship, removed.ShipId);
        Assert.Empty(session.CaptureSnapshot().Ships);

        session.AdvanceTo(new SimulationTime(1_100));

        Assert.Contains(
            session.EventRecords,
            record => record.Disposition == ScheduledEventDisposition.IgnoredStaleGeneration);
    }

    private static InitialShipSetup CreateInitialShip() => new(
        GameSessionTestFixture.Entity,
        GameSessionTestFixture.Ship,
        GameSessionTestFixture.CargoInventory,
        GameSessionTestFixture.Principal,
        GameSessionTestFixture.Design,
        GameSessionTestFixture.Position(0, 0),
        GameSessionTestFixture.PlayerController);

    private static ShipMaterializationPolicy CreateMaterializationPolicy() => new(
        new FacilityId(1),
        GameSessionTestFixture.Principal,
        GameSessionTestFixture.Position(0, 0),
        GameSessionTestFixture.PlayerController,
        InitialShipOrderPolicy.NoInitialOrder,
        [GameSessionTestFixture.Design]);

    private static GameSessionEconomySetup CreateEconomySeed() => new(
        [
            new InitialInventorySetup(
                new InventoryId(2),
                new Quantity(20),
                [new KeyValuePair<MaterialId, Quantity>(new MaterialId(1), new Quantity(5))]),
        ],
        [
            new EconomyFacilitySetup(
                new FacilityId(1),
                new InventoryId(2),
                new LocationId(1),
                GameSessionTestFixture.Position(0, 0)),
        ],
        [],
        [new ConstructionFacilitySetup(new FacilityId(1), new Throughput(1))],
        [GameSessionTestFixture.Design],
        [new InitialConstructionOrderSetup(new FacilityId(1), GameSessionTestFixture.Design.Id)],
        [new InitialFreighterSetup(GameSessionTestFixture.Ship, new LocationId(1))],
        new UnreachableLogisticsNavigation(),
        new TransportTiming(
            SimulationDuration.Zero,
            new TransferRate(1),
            new TransferRate(1)));

    private static GameSessionEconomySetup CreateTransportEconomySeed(
        MaterialId material,
        ShipDesign design) => new(
        [
            new InitialInventorySetup(new InventoryId(2), new Quantity(20), []),
            new InitialInventorySetup(new InventoryId(3), new Quantity(20), []),
        ],
        [
            new EconomyFacilitySetup(
                new FacilityId(1),
                new InventoryId(2),
                new LocationId(1),
                GameSessionTestFixture.Position(0, 0)),
            new EconomyFacilitySetup(
                new FacilityId(2),
                new InventoryId(3),
                new LocationId(2),
                GameSessionTestFixture.Position(0, 0)),
        ],
        [
            new ProductionFacilitySetup(
                new FacilityId(1),
                new Recipe([], material, new Quantity(1), new Work(1)),
                new Throughput(1),
                Repeat: false),
        ],
        [new ConstructionFacilitySetup(new FacilityId(2), new Throughput(1))],
        [design],
        [new InitialConstructionOrderSetup(new FacilityId(2), design.Id)],
        [new InitialFreighterSetup(GameSessionTestFixture.Ship, new LocationId(2))],
        new FixedLogisticsNavigation(new SimulationDuration(100)),
        new TransportTiming(
            SimulationDuration.Zero,
            new TransferRate(1),
            new TransferRate(1)));

    private sealed class UnreachableLogisticsNavigation : ILogisticsNavigation
    {
        /// <inheritdoc />
        public LogisticsTravelEstimate? Estimate(
            ShipId actorId,
            LocationId origin,
            LocationId destination,
            SimulationTime plannedAt) => null;
    }

    private sealed class FixedLogisticsNavigation : ILogisticsNavigation
    {
        private readonly SimulationDuration _duration;

        public FixedLogisticsNavigation(SimulationDuration duration)
        {
            _duration = duration;
        }

        /// <inheritdoc />
        public LogisticsTravelEstimate? Estimate(
            ShipId actorId,
            LocationId origin,
            LocationId destination,
            SimulationTime plannedAt) => new(_duration);
    }
}
