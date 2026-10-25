using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SensorDeployableTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRemovalReceiptSubjectsAreRejectedWithoutThrowing(bool missingRequest)
    {
        var fixture = Create();
        SensorActionResult deployed = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);
        fixture.Owner.RemoveSensor(new EntityRemovalRequest(deployed.Deployed!.EntityId, EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo), SimulationTime.Zero);
        EntityLifecycleCheckpoint saved = fixture.Owner.CaptureCheckpoint();
        SensorRemovalReceipt receipt = saved.SensorDeployables.Removals![0]!;
        receipt = receipt with { Result = missingRequest ? receipt.Result with { Request = null! } : receipt.Result with { Sensor = null! } };
        var checkpoint = new EntityLifecycleCheckpoint(saved.EntityIds, saved.ShipIds, saved.InventoryIds, saved.Inventories,
            saved.LiveShips, saved.MaterializationReceipts, saved.RemovalReceipts, saved.SensorDeployables with { Removals = [receipt] });

        CheckpointResult<EntityLifecycleOwner> result = EntityLifecycleOwner.RestoreCheckpoint(checkpoint,
            fixture.Movement, fixture.Control, fixture.Orders, [], new PhysicalDefinitionCatalog([Definition().Physical]),
            null, [Definition()], [GameSessionTestFixture.Design]);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void RestoredSensorCannotContradictItsLiveActingShipsOwnership()
    {
        var fixture = Create();
        fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);
        EntityLifecycleCheckpoint saved = fixture.Owner.CaptureCheckpoint();
        DeployedSensorSnapshot changed = saved.SensorDeployables.Live[0]! with { PrincipalId = new PrincipalId(2) };
        SensorActionReceipt receipt = saved.SensorDeployables.Receipts[0]!;
        var checkpoint = new EntityLifecycleCheckpoint(saved.EntityIds, saved.ShipIds, saved.InventoryIds, saved.Inventories,
            saved.LiveShips, saved.MaterializationReceipts, saved.RemovalReceipts, saved.SensorDeployables with
            { Live = [changed], Receipts = [receipt with { Result = receipt.Result with { Deployed = changed } }] });

        CheckpointResult<EntityLifecycleOwner> restored = EntityLifecycleOwner.RestoreCheckpoint(checkpoint,
            fixture.Movement, fixture.Control, fixture.Orders, [], new PhysicalDefinitionCatalog([Definition().Physical]),
            null, [Definition()], [GameSessionTestFixture.Design]);

        Assert.False(restored.IsSuccess);
    }

    [Fact]
    public void ExhaustedEntityAllocatorRejectsDeploymentWithoutConsumingTheItem()
    {
        var fixture = Create();
        EntityLifecycleCheckpoint saved = fixture.Owner.CaptureCheckpoint();
        var checkpoint = new EntityLifecycleCheckpoint(new IdSequenceCheckpoint(null), saved.ShipIds, saved.InventoryIds,
            saved.Inventories, saved.LiveShips, saved.MaterializationReceipts, saved.RemovalReceipts, saved.SensorDeployables);
        EntityLifecycleOwner restored = EntityLifecycleOwner.RestoreCheckpoint(checkpoint, fixture.Movement, fixture.Control,
            fixture.Orders, [], new PhysicalDefinitionCatalog([Definition().Physical]), null, [Definition()], [GameSessionTestFixture.Design]).Value!;
        var inventory = new InventoryCommitOwner(restored.Inventories);

        SensorActionResult rejected = restored.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), inventory, SimulationTime.Zero);

        Assert.Equal(SensorActionFailure.IdentifierExhausted, rejected.Failure);
        Assert.NotNull(restored.GetRequiredCargo(new ShipId(1)).GetDiscrete(new ItemInstanceId(1)));
        Assert.Empty(restored.CaptureDeployedSensors());
        Assert.Null(restored.CaptureCheckpoint().EntityIds.NextValue);
    }

    [Fact]
    public void ExhaustedItemAllocatorRejectsPickupWithoutRemovingTheEntity()
    {
        var fixture = Create();
        SensorActionResult deployed = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);
        InventoryCommitOwnerCheckpoint saved = fixture.Commit.CaptureCheckpoint();
        InventoryCommitOwner inventory = InventoryCommitOwner.RestoreCheckpoint(new InventoryCommitOwnerCheckpoint(
            new IdSequenceCheckpoint(null), saved.ReservationIds, saved.Receipts), fixture.Owner.Inventories,
            new PhysicalDefinitionCatalog([Definition().Physical])).Value!;
        EntityLifecycleCheckpoint before = fixture.Owner.CaptureCheckpoint();

        SensorActionResult rejected = fixture.Owner.PickupSensor(new CommandSequence(2), new ShipId(1), deployed.Deployed!.EntityId,
            inventory, SimulationTime.Zero);

        Assert.Equal(SensorActionFailure.IdentifierExhausted, rejected.Failure);
        Assert.Single(fixture.Owner.CaptureDeployedSensors());
        Assert.Equal(before.EntityIds, fixture.Owner.CaptureCheckpoint().EntityIds);
        Assert.Empty(fixture.Owner.GetRequiredCargo(new ShipId(1)).CaptureCheckpoint().DiscreteItems);
    }

    [Fact]
    public void RestoredSensorCannotReuseAHistoricalShipEntityIdentity()
    {
        var fixture = Create();
        fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);
        EntityRemovalPreparation.Prepared removal = Assert.IsType<EntityRemovalPreparation.Prepared>(fixture.Owner.PrepareRemoval(
            new EntityRemovalRequest(new EntityId(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo), false));
        fixture.Owner.ApplyRemoval(removal.Value, SimulationTime.Zero);
        EntityLifecycleCheckpoint checkpoint = fixture.Owner.CaptureCheckpoint();
        DeployedSensorSnapshot aliased = checkpoint.SensorDeployables.Live[0]! with { EntityId = new EntityId(1) };
        SensorActionReceipt receipt = checkpoint.SensorDeployables.Receipts[0]!;
        checkpoint = new EntityLifecycleCheckpoint(checkpoint.EntityIds, checkpoint.ShipIds, checkpoint.InventoryIds,
            checkpoint.Inventories, checkpoint.LiveShips, checkpoint.MaterializationReceipts, checkpoint.RemovalReceipts,
            checkpoint.SensorDeployables with { Live = [aliased], Receipts = [receipt with { Result = receipt.Result with { Deployed = aliased } }] });

        CheckpointResult<EntityLifecycleOwner> restored = EntityLifecycleOwner.RestoreCheckpoint(checkpoint,
            fixture.Movement, fixture.Control, fixture.Orders, [], new PhysicalDefinitionCatalog([Definition().Physical]),
            null, [Definition()], [GameSessionTestFixture.Design]);

        Assert.False(restored.IsSuccess);
    }

    [Fact]
    public void BriefSweptContactDoesNotAuthorizeExecutionAfterTheShipHasExitedRange()
    {
        var fixture = Create();
        fixture.Movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.Position(20_000, 0), new SimulationDuration(1)), SimulationTime.Zero, static value => value);
        SystemPosition target = GameSessionTestFixture.Position(10_000, 0);
        var interests = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        StationaryInteractionInterest interest = interests.RegisterStationary("deploy", new ShipId(1), target, 2500);
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(interest,
            fixture.Movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(1)));
        Assert.Equal(new SimulationTime(1), forecast.Candidate.Key.Timestamp);

        SensorActionResult result = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            target, fixture.Commit, forecast.Candidate.Key.Timestamp);

        Assert.Equal(SensorActionFailure.OutOfRange, result.Failure);
        Assert.Empty(fixture.Owner.CaptureDeployedSensors());
        Assert.NotNull(fixture.Owner.GetRequiredCargo(new ShipId(1)).GetDiscrete(new ItemInstanceId(1)));
        Assert.Equal<ulong?>(2, fixture.Owner.CaptureCheckpoint().EntityIds.NextValue);
    }

    [Fact]
    public void ConnectorTransitHasNoLocalOriginAndCannotDeploy()
    {
        var fixture = Create();
        fixture.Movement.CommitStartConnector(new ShipId(1), new TravelLeg.Connector(new TransitConnectionId(1),
            GameSessionTestFixture.Position(0, 0), new SystemPosition(new SystemId(2), GameSessionTestFixture.Position(0, 0).Position),
            new SimulationDuration(100)), SimulationTime.Zero, static value => value);

        SensorActionResult result = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, new SimulationTime(1));

        Assert.False(result.IsSuccess);
        Assert.Empty(fixture.Owner.CaptureDeployedSensors());
        Assert.NotNull(fixture.Owner.GetRequiredCargo(new ShipId(1)).GetDiscrete(new ItemInstanceId(1)));
    }

    [Theory]
    [InlineData(2499, 0, 4999, 0, true, true)]
    [InlineData(2500, 0, 5000, 0, true, true)]
    [InlineData(2501, 0, 5001, 0, false, false)]
    [InlineData(1500, 2000, 3000, 4000, true, true)]
    [InlineData(1500, 2001, 3000, 4001, false, false)]
    [InlineData(-2500, 0, -5000, 0, true, true)]
    public void IndependentInclusiveRangesUseExactGeometryAtLargeCoordinates(long deployX, long deployY,
        long pickupX, long pickupY, bool deployAllowed, bool pickupAllowed)
    {
        const long offset = 1_000_000_000;
        SensorDeployableDefinition definition = Definition();
        var movement = new SpatialMovement();
        var owner = new EntityLifecycleOwner(movement, new ActorControlRegistry(), new ShipOrderCoordinator(), [],
            sensorDefinitions: [definition], shipDefinitions: [GameSessionTestFixture.Design]);
        owner.RegisterSetup([
            new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(offset + deployX, offset + deployY),
                GameSessionTestFixture.PlayerController, cargo: [definition.Physical]),
            new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(offset, offset),
                GameSessionTestFixture.PlayerController, cargo: [definition.Physical]),
            new InitialShipSetup(new EntityId(3), new ShipId(3), new InventoryId(3), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(offset + pickupX, offset + pickupY),
                GameSessionTestFixture.PlayerController)]);
        var commit = new InventoryCommitOwner(owner.Inventories);
        SystemPosition target = GameSessionTestFixture.Position(offset, offset);

        SensorActionResult deployment = owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            target, commit, SimulationTime.Zero);
        SensorActionResult fixtureDeployment = owner.DeploySensor(new CommandSequence(2), new ShipId(2), new ItemInstanceId(2),
            target, commit, SimulationTime.Zero);
        SensorActionResult pickup = owner.PickupSensor(new CommandSequence(3), new ShipId(3), fixtureDeployment.Deployed!.EntityId,
            commit, SimulationTime.Zero);

        Assert.Equal(deployAllowed, deployment.IsSuccess);
        Assert.Equal(pickupAllowed, pickup.IsSuccess);
        if (!deployAllowed) Assert.Equal(SensorActionFailure.OutOfRange, deployment.Failure);
        if (!pickupAllowed) Assert.Equal(SensorActionFailure.OutOfRange, pickup.Failure);
    }

    [Fact]
    public void DeploymentAndPickupPreserveCustodyWhileAllocatingFreshIdentities()
    {
        SensorDeployableDefinition definition = Definition();
        var movement = new SpatialMovement();
        var owner = new EntityLifecycleOwner(movement, new ActorControlRegistry(), new ShipOrderCoordinator(), [],
            sensorDefinitions: [definition], shipDefinitions: [GameSessionTestFixture.Design]);
        owner.RegisterSetup([new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1),
            GameSessionTestFixture.Principal, GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.PlayerController, cargo: [definition.Physical])]);
        var inventoryCommit = new InventoryCommitOwner(owner.Inventories);

        SensorActionResult deployed = owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(2500, 0), inventoryCommit, SimulationTime.Zero);

        Assert.True(deployed.IsSuccess);
        Assert.True(deployed.WasApplied);
        Assert.Equal(new EntityId(2), deployed.Deployed!.EntityId);
        Assert.Equal(EntityKind.SensorDeployable, owner.Entities.GetKind(deployed.Deployed.EntityId));
        Assert.Null(owner.Inventories.Get(new InventoryId(1))!.GetDiscrete(new ItemInstanceId(1)));
        SensorActionResult pickedUp = owner.PickupSensor(new CommandSequence(2), new ShipId(1), deployed.Deployed.EntityId,
            inventoryCommit, SimulationTime.Zero);
        Assert.True(pickedUp.IsSuccess);
        Assert.Equal(new ItemInstanceId(2), pickedUp.Item!.Id);
        Assert.Equal(definition.Key, pickedUp.Item.DefinitionKey);
        Assert.Empty(owner.CaptureDeployedSensors());
        Assert.Null(owner.Entities.GetKind(deployed.Deployed.EntityId));
        SensorActionResult replay = owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(2500, 0), inventoryCommit, SimulationTime.Zero);
        Assert.True(replay.IsSuccess);
        Assert.False(replay.WasApplied);
        Assert.Empty(owner.CaptureDeployedSensors());
        SensorActionResult redeployed = owner.DeploySensor(new CommandSequence(3), new ShipId(1), pickedUp.Item.Id,
            GameSessionTestFixture.Position(2500, 0), inventoryCommit, SimulationTime.Zero);
        Assert.True(redeployed.IsSuccess);
        Assert.Equal(new EntityId(3), redeployed.Deployed!.EntityId);
        Assert.Equal(definition.Key, redeployed.Deployed.DefinitionKey);
        Assert.Null(owner.GetRequiredCargo(new ShipId(1)).GetDiscrete(pickedUp.Item.Id));
    }

    private static SensorDeployableDefinition Definition() => new(
        QualifiedContentKey.Create("test", "sensor-deployable", "probe"), "Fixture Probe",
        new Quantity(1), 2500, 30_000);

    [Fact]
    public void ReservedItemCannotPartiallyDeployOrConsumeAnEntityIdentity()
    {
        var fixture = Create();
        fixture.Commit.CommitBatch([new ReservePhysicalInventoryProposal(
            new InventoryOperationKey(InventoryOperationSourceKind.Explicit, 1), new InventoryId(1),
            new PhysicalReservationSubject.Discrete(new ItemInstanceId(1)),
            new ReservationOwner.TransportJob(new TransportJobId(1)))]);
        IdSequenceCheckpoint before = fixture.Owner.CaptureCheckpoint().EntityIds;

        SensorActionResult rejected = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);

        Assert.Equal(SensorActionFailure.ItemReserved, rejected.Failure);
        Assert.Equal(before, fixture.Owner.CaptureCheckpoint().EntityIds);
        Assert.NotNull(fixture.Owner.Inventories.Get(new InventoryId(1))!.GetDiscrete(new ItemInstanceId(1)));
        Assert.Empty(fixture.Owner.CaptureDeployedSensors());
    }

    [Fact]
    public void IncomingCapacityCommitmentPreventsPickupWithoutRemovingTheSensor()
    {
        var fixture = Create();
        SensorActionResult deployed = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);
        fixture.Commit.CommitBatch([new ReservePhysicalInventoryProposal(
            new InventoryOperationKey(InventoryOperationSourceKind.Explicit, 1), new InventoryId(1),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(10)),
            new ReservationOwner.TransportJob(new TransportJobId(1)))]);
        IdSequenceCheckpoint before = fixture.Commit.CaptureCheckpoint().ItemInstanceIds;

        SensorActionResult rejected = fixture.Owner.PickupSensor(new CommandSequence(2), new ShipId(1), deployed.Deployed!.EntityId,
            fixture.Commit, SimulationTime.Zero);

        Assert.Equal(SensorActionFailure.InsufficientCapacity, rejected.Failure);
        Assert.Equal(before, fixture.Commit.CaptureCheckpoint().ItemInstanceIds);
        Assert.Single(fixture.Owner.CaptureDeployedSensors());
        Assert.Null(fixture.Owner.Inventories.Get(new InventoryId(1))!.GetDiscrete(new ItemInstanceId(2)));
    }

    [Fact]
    public void RestoredDeploymentContinuesPickupExactlyOnce()
    {
        var fixture = Create();
        SensorActionResult deployed = fixture.Owner.DeploySensor(new CommandSequence(1), new ShipId(1), new ItemInstanceId(1),
            GameSessionTestFixture.Position(0, 0), fixture.Commit, SimulationTime.Zero);
        EntityLifecycleCheckpoint checkpoint = fixture.Owner.CaptureCheckpoint();
        SpatialMovement movement = SpatialMovement.RestoreCheckpoint(
            fixture.Movement.CaptureCheckpoint(SimulationTime.Zero).Value!, SimulationTime.Zero).Value!;
        ActorControlRegistry control = ActorControlRegistry.RestoreCheckpoint(fixture.Control.CaptureCheckpoint()).Value!;
        ShipOrderCoordinator orders = ShipOrderCoordinator.RestoreCheckpoint(fixture.Orders.CaptureCheckpoint()).Value!;
        var catalog = new PhysicalDefinitionCatalog([Definition().Physical]);
        CheckpointResult<EntityLifecycleOwner> restored = EntityLifecycleOwner.RestoreCheckpoint(checkpoint,
            movement, control, orders, [], catalog, null, [Definition()], [GameSessionTestFixture.Design]);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        InventoryCommitOwner inventory = InventoryCommitOwner.RestoreCheckpoint(fixture.Commit.CaptureCheckpoint(),
            restored.Value!.Inventories, catalog).Value!;

        SensorActionResult pickedUp = restored.Value.PickupSensor(new CommandSequence(2), new ShipId(1), deployed.Deployed!.EntityId,
            inventory, SimulationTime.Zero);
        SensorActionResult replay = restored.Value.PickupSensor(new CommandSequence(2), new ShipId(1), deployed.Deployed.EntityId,
            inventory, SimulationTime.Zero);

        Assert.True(pickedUp.IsSuccess);
        Assert.True(pickedUp.WasApplied);
        Assert.Equal(new ItemInstanceId(2), pickedUp.Item!.Id);
        Assert.True(replay.IsSuccess);
        Assert.False(replay.WasApplied);
        Assert.Equal(pickedUp.Item, replay.Item);
        Assert.Single(fixture.Owner.CaptureDeployedSensors());
        Assert.Empty(restored.Value.CaptureDeployedSensors());
        Assert.Equal<ulong?>(3, inventory.CaptureCheckpoint().ItemInstanceIds.NextValue);
    }

    private static (EntityLifecycleOwner Owner, InventoryCommitOwner Commit, SpatialMovement Movement,
        ActorControlRegistry Control, ShipOrderCoordinator Orders) Create()
    {
        var movement = new SpatialMovement();
        var control = new ActorControlRegistry();
        var orders = new ShipOrderCoordinator();
        var owner = new EntityLifecycleOwner(movement, control, orders, [],
            sensorDefinitions: [Definition()], shipDefinitions: [GameSessionTestFixture.Design]);
        owner.RegisterSetup([new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1),
            GameSessionTestFixture.Principal, GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.PlayerController, cargo: [Definition().Physical])]);
        return (owner, new InventoryCommitOwner(owner.Inventories), movement, control, orders);
    }
}
