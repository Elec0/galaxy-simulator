using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionEconomySetupTests
{
    [Fact]
    public void MappedEconomyCreatesCustodyAwareGeneralizedFacilityInventory()
    {
        MaterialId materialId = new(1);
        PhysicalDefinition definition = FungibleDefinition("ore", 1);
        var mapping = new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(materialId, definition),
        ]);
        GameSessionEconomySetup economy = CreateEconomySeed(
            GameSessionTestFixture.Principal,
            mapping);
        var lifecycle = new EntityLifecycleOwner(
            new SpatialMovement(),
            new ActorControlRegistry(),
            new ShipOrderCoordinator(),
            policies: []);
        lifecycle.RegisterSetup([CreateInitialShip()]);

        _ = new SessionEconomyOwner(economy, lifecycle);

        Inventory inventory = lifecycle.Inventories.Get(new InventoryId(2))!;
        Assert.Equal(
            new InventoryCustody(
                new InventoryOwnerReference.Facility(new FacilityId(1)),
                GameSessionTestFixture.Principal),
            inventory.Custody);
        Assert.Equal<ulong>(5, inventory.Stored(materialId).Units);
        Assert.Equal<ulong>(5, inventory.FungibleStored(definition.Key).Units);
        InventoryCheckpoint checkpoint = inventory.CaptureCheckpoint();
        Assert.Empty(checkpoint.StoredMaterials);
        Assert.Single(checkpoint.FungibleHoldings);
    }

    [Fact]
    public void MappedEconomyRequiresEveryUsedMaterialAndFacilityPrincipal()
    {
        MaterialId materialId = new(1);
        var emptyMapping = new MaterialInventoryCompatibilityMap([]);

        Assert.Throws<ArgumentException>(() =>
            CreateEconomySeed(GameSessionTestFixture.Principal, emptyMapping));
        Assert.Throws<ArgumentException>(() =>
            CreateEconomySeed(null, new MaterialInventoryCompatibilityMap([
                new KeyValuePair<MaterialId, PhysicalDefinition>(
                    materialId,
                    FungibleDefinition("ore", 1)),
            ])));
    }

    [Fact]
    public void MaterialCompatibilityMappingRequiresUniqueUnitCostFungibleDefinitions()
    {
        MaterialId first = new(1);
        MaterialId second = new(2);
        PhysicalDefinition ore = FungibleDefinition("ore", 1);

        Assert.Throws<ArgumentException>(() => new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(first, ore),
            new KeyValuePair<MaterialId, PhysicalDefinition>(second, ore),
        ]));
        Assert.Throws<ArgumentException>(() => new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(
                first,
                FungibleDefinition("dense-ore", 2)),
        ]));
        Assert.Throws<ArgumentException>(() => new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(
                first,
                new PhysicalDefinition(
                    QualifiedContentKey.Create("core", "cargo", "sensor"),
                    PhysicalHoldingKind.Discrete,
                    new Quantity(1))),
        ]));
    }

    [Fact]
    public void SessionRejectsMappedFacilityWithUnknownControllingPrincipal()
    {
        MaterialId materialId = new(1);
        var mapping = new MaterialInventoryCompatibilityMap([
            new KeyValuePair<MaterialId, PhysicalDefinition>(
                materialId,
                FungibleDefinition("ore", 1)),
        ]);
        GameSessionEconomySetup economy = CreateEconomySeed(
            new PrincipalId(99),
            mapping);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [CreateInitialShip()],
                new ConnectorTopology([], []),
                [CreateMaterializationPolicy()],
                economy,
                GameSessionTestFixture.Relationships,
                GameSessionTestFixture.RootSeed,
                factRetentionCapacity: 16));

        Assert.Contains("controlling principal", error.Message);
    }

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
            GameSessionTestFixture.RootSeed,
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
            GameSessionTestFixture.RootSeed,
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
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 16);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator()));

        session.AdvanceTo(new SimulationTime(1_000));

        GameEventRecord completion = Assert.Single(session.EventRecords);
        Assert.Equal(ScheduledEventDisposition.Applied, completion.Disposition);
        var economic = Assert.IsType<GameEventKind.Economic>(completion.Kind);
        Assert.IsType<EconomicEvent.ConstructionComplete>(economic.Event);
        GameShipSnapshot materialized = Assert.Single(
            session.CaptureSnapshot().Ships,
            ship => ship.Id == new ShipId(2));
        Assert.Equal(new EntityId(2), materialized.EntityId);
        Assert.Equal(new InventoryId(3), materialized.CargoInventoryId);
        Assert.Equal(GameSessionTestFixture.Principal, materialized.PrincipalId);
        Assert.Equal(GameSessionTestFixture.Design.Id, materialized.DesignId);
        Assert.Equal(GameSessionTestFixture.Design.CargoCapacity, materialized.CargoCapacity);
        Assert.Equal(GameSessionTestFixture.Position(0, 0), materialized.Position);
        Assert.Equal(GameSessionTestFixture.PlayerController, materialized.Control.BaseController);
        Assert.Null(materialized.Control.TemporaryOverride);
        Assert.Null(materialized.CurrentOrder);
        Assert.Empty(materialized.QueuedOrders);
        Assert.Empty(materialized.SuspendedOrders);
        Assert.Equal(materialized.Id, session.ResolveShip(materialized.EntityId));
        Assert.Equal(materialized.EntityId, session.ResolveEntity(materialized.Id));

        GameFactEnvelope envelope = Assert.Single(session.ReadFactsAfter(null, 16).Facts);
        Assert.Equal(
            new EventKey(
                completion.Timestamp,
                completion.Phase,
                completion.CreationSequence),
            Assert.IsType<ScheduledEventFactCause>(envelope.Cause).Key);
        var fact = Assert.IsType<EntityMaterializedFact>(envelope.Fact);
        Assert.Equal(materialized.EntityId, fact.EntityId);
        Assert.Equal(materialized.Id, fact.ShipId);
        Assert.Equal(EntityMaterializationSourceKind.Construction, fact.SourceKind);
        Assert.Equal(materialized.PrincipalId, fact.PrincipalId);
        Assert.Equal(materialized.DesignId, fact.DesignId);
        Assert.Equal(materialized.Position, fact.InitialPosition);

        session.AdvanceTo(new SimulationTime(2_000));

        Assert.Equal(2, session.CaptureSnapshot().Ships.Count);
        Assert.Single(session.EventRecords);
        Assert.Single(session.ReadFactsAfter(null, 16).Facts);
    }

    [Fact]
    public void SessionOwnedConstructionUsesStableFacilityOrder()
    {
        var lowerFacility = new FacilityId(1);
        var higherFacility = new FacilityId(2);
        var lowerInventory = new InventoryId(2);
        var higherInventory = new InventoryId(3);
        GameSessionEconomySetup economy = new(
            [
                new InitialInventorySetup(lowerInventory, new Quantity(20), []),
                new InitialInventorySetup(higherInventory, new Quantity(20), []),
            ],
            [
                new EconomyFacilitySetup(
                    higherFacility,
                    higherInventory,
                    new LocationId(2),
                    GameSessionTestFixture.Position(20, 0)),
                new EconomyFacilitySetup(
                    lowerFacility,
                    lowerInventory,
                    new LocationId(1),
                    GameSessionTestFixture.Position(10, 0)),
            ],
            [],
            [
                new ConstructionFacilitySetup(higherFacility, new Throughput(1)),
                new ConstructionFacilitySetup(lowerFacility, new Throughput(1)),
            ],
            [GameSessionTestFixture.Design],
            [
                new InitialConstructionOrderSetup(
                    higherFacility,
                    GameSessionTestFixture.Design.Id),
                new InitialConstructionOrderSetup(
                    lowerFacility,
                    GameSessionTestFixture.Design.Id),
            ],
            [],
            new UnreachableLogisticsNavigation(),
            new TransportTiming(
                SimulationDuration.Zero,
                new TransferRate(1),
                new TransferRate(1)));
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [CreateInitialShip()],
            new ConnectorTopology([], []),
            [
                CreateMaterializationPolicy(lowerFacility, 10),
                CreateMaterializationPolicy(higherFacility, 20),
            ],
            economy,
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 16);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator()));

        session.AdvanceTo(new SimulationTime(1_000));

        EntityMaterializedFact[] facts = session.ReadFactsAfter(null, 16).Facts
            .Select(envelope => Assert.IsType<EntityMaterializedFact>(envelope.Fact))
            .ToArray();
        Assert.Equal(
            [new EntityId(2), new EntityId(3)],
            facts.Select(fact => fact.EntityId));
        Assert.Equal(
            [GameSessionTestFixture.Position(10, 0), GameSessionTestFixture.Position(20, 0)],
            facts.Select(fact => fact.InitialPosition));
        Assert.Equal(
            [new ShipId(2), new ShipId(3)],
            facts.Select(fact => fact.ShipId));
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
            GameSessionTestFixture.RootSeed,
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

    private static ShipMaterializationPolicy CreateMaterializationPolicy() =>
        CreateMaterializationPolicy(new FacilityId(1), 0);

    private static ShipMaterializationPolicy CreateMaterializationPolicy(
        FacilityId facilityId,
        long x) => new(
        facilityId,
        GameSessionTestFixture.Principal,
        GameSessionTestFixture.Position(x, 0),
        GameSessionTestFixture.PlayerController,
        InitialShipOrderPolicy.NoInitialOrder,
        [GameSessionTestFixture.Design]);

    private static GameSessionEconomySetup CreateEconomySeed(
        PrincipalId? controllingPrincipalId = null,
        MaterialInventoryCompatibilityMap? materialCompatibility = null) => new(
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
                GameSessionTestFixture.Position(0, 0),
                controllingPrincipalId),
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
            new TransferRate(1)),
        materialCompatibility);

    private static PhysicalDefinition FungibleDefinition(
        string localId,
        ulong capacityCost) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            PhysicalHoldingKind.Fungible,
            new Quantity(capacityCost));

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
