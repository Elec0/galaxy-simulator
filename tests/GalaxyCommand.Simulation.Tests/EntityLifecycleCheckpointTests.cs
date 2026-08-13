using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class EntityLifecycleCheckpointTests
{
    [Fact]
    public void RestorePreservesLiveIdentityInventoriesAndAllocatorPositions()
    {
        EntityLifecycleOwner original = CreateOwner(
            out _,
            out _,
            out _);
        original.RegisterSetup([InitialShip()]);
        original.RegisterEconomyInventory(
            new Inventory(new InventoryId(5), new Quantity(20)));

        EntityLifecycleCheckpoint checkpoint = original.CaptureCheckpoint();
        CheckpointResult<EntityLifecycleOwner> result = Restore(checkpoint);

        Assert.True(result.IsSuccess);
        EntityLifecycleOwner restored =
            Assert.IsType<EntityLifecycleOwner>(result.Value);
        Assert.Equal(
            GameSessionTestFixture.Ship,
            restored.Entities.GetShipId(GameSessionTestFixture.Entity));
        Assert.Equal(
            GameSessionTestFixture.Entity,
            restored.Entities.GetEntityId(GameSessionTestFixture.Ship));
        Assert.Equal(
            GameSessionTestFixture.Design.Id,
            restored.GetRequiredShip(GameSessionTestFixture.Ship).DesignId);
        Assert.NotNull(restored.Inventories.Get(GameSessionTestFixture.CargoInventory));
        Assert.NotNull(restored.Inventories.Get(new InventoryId(5)));

        EntityLifecycleCheckpoint recaptured = restored.CaptureCheckpoint();
        Assert.Equal<ulong?>(2, recaptured.EntityIds.NextValue);
        Assert.Equal<ulong?>(2, recaptured.ShipIds.NextValue);
        Assert.Equal<ulong?>(6, recaptured.InventoryIds.NextValue);
    }

    [Fact]
    public void RestorePreservesMaterializationReceiptIdempotency()
    {
        EntityLifecycleOwner original = CreateOwner(
            out _,
            out _,
            out _);
        original.RegisterSetup([InitialShip()]);
        EntityLifecycleCheckpoint captured = original.CaptureCheckpoint();
        ConstructionMaterializationEffect effect = MaterializationEffect();
        var checkpoint = new EntityLifecycleCheckpoint(
            captured.EntityIds,
            captured.ShipIds,
            captured.InventoryIds,
            captured.Inventories,
            captured.LiveShips,
            [new EntityMaterializationReceiptCheckpoint(
                effect,
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory)],
            captured.RemovalReceipts);
        EntityLifecycleOwner restored = Assert.IsType<EntityLifecycleOwner>(
            Restore(checkpoint).Value);
        var source = new ConstructionProcess(
            effect.FacilityId,
            GameSessionTestFixture.CargoInventory,
            new Throughput(1));

        ConstructionMaterializationCommit repeated =
            restored.MaterializeConstruction(
                source,
                effect,
                effect.CompletedAt);

        Assert.False(repeated.WasApplied);
        var materialized =
            Assert.IsType<ConstructionEntityMaterializationResult.Materialized>(
                repeated.Result);
        Assert.Equal(GameSessionTestFixture.Entity, materialized.EntityId);
        Assert.Equal(GameSessionTestFixture.Ship, materialized.ShipId);
        Assert.Single(restored.CaptureCheckpoint().MaterializationReceipts);
    }

    [Fact]
    public void RestorePreservesRemovalReceiptIdempotency()
    {
        EntityLifecycleOwner original = CreateOwner(
            out _,
            out _,
            out _);
        original.RegisterSetup([InitialShip()]);
        var request = new EntityRemovalRequest(
            GameSessionTestFixture.Entity,
            EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo);
        var prepared = Assert.IsType<EntityRemovalPreparation.Prepared>(
            original.PrepareRemoval(
                request,
                permitOwnerReleasedCommitments: false));
        EntityRemovalResult.Removed removed = Assert.IsType<EntityRemovalResult.Removed>(
            original.ApplyRemoval(prepared.Value, SimulationTime.Zero));

        EntityLifecycleOwner restored = Assert.IsType<EntityLifecycleOwner>(
            Restore(original.CaptureCheckpoint()).Value);

        var repeated = Assert.IsType<EntityRemovalPreparation.Resolved>(
            restored.PrepareRemoval(
                request,
                permitOwnerReleasedCommitments: false));
        Assert.Equal(removed, repeated.Value);
        Assert.Null(restored.Entities.GetShipId(request.EntityId));
        Assert.Single(restored.CaptureCheckpoint().RemovalReceipts);
    }

    [Fact]
    public void RestoreAcceptsUnorderedLiveShipsAndCanonicalizesCapture()
    {
        EntityLifecycleOwner original = CreateOwner(
            out _,
            out _,
            out _);
        original.RegisterSetup(
            [
                InitialShip(),
                InitialShip(
                    new EntityId(9),
                    new ShipId(9),
                    new InventoryId(9)),
            ]);
        EntityLifecycleCheckpoint captured = original.CaptureCheckpoint();
        var reordered = new EntityLifecycleCheckpoint(
            captured.EntityIds,
            captured.ShipIds,
            captured.InventoryIds,
            captured.Inventories,
            captured.LiveShips.Reverse(),
            captured.MaterializationReceipts,
            captured.RemovalReceipts);

        EntityLifecycleOwner restored = Assert.IsType<EntityLifecycleOwner>(
            Restore(reordered).Value);

        Assert.Equal(
            [1UL, 9UL],
            restored.CaptureCheckpoint().LiveShips
                .Select(ship => ship!.EntityId.Value));
    }

    [Fact]
    public void RestoreRejectsDuplicateLiveIdentity()
    {
        EntityLifecycleOwner original = CreateOwner(
            out _,
            out _,
            out _);
        original.RegisterSetup([InitialShip()]);
        EntityLifecycleCheckpoint captured = original.CaptureCheckpoint();
        EntityLifecycleShipCheckpoint ship = Assert.Single(captured.LiveShips)!;
        var checkpoint = new EntityLifecycleCheckpoint(
            captured.EntityIds,
            captured.ShipIds,
            captured.InventoryIds,
            captured.Inventories,
            [ship, ship],
            captured.MaterializationReceipts,
            captured.RemovalReceipts);

        CheckpointResult<EntityLifecycleOwner> result = Restore(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.lifecycle.liveShips[1].entityId",
            result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsLiveShipWithoutCargoInventory()
    {
        var checkpoint = new EntityLifecycleCheckpoint(
            new IdSequenceCheckpoint(2),
            new IdSequenceCheckpoint(2),
            new IdSequenceCheckpoint(2),
            new InventoryRegistryCheckpoint([]),
            [LiveShip()],
            [],
            []);

        CheckpointResult<EntityLifecycleOwner> result = Restore(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.lifecycle.liveShips[0].cargoInventoryId",
            result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsInventoryBeyondAllocatorPosition()
    {
        var inventory = new Inventory(new InventoryId(2), new Quantity(10));
        var checkpoint = new EntityLifecycleCheckpoint(
            new IdSequenceCheckpoint(1),
            new IdSequenceCheckpoint(1),
            new IdSequenceCheckpoint(2),
            new InventoryRegistryCheckpoint([inventory.CaptureCheckpoint()]),
            [],
            [],
            []);

        CheckpointResult<EntityLifecycleOwner> result = Restore(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.lifecycle.inventories[0].id",
            result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsOrphanedMaterializationReceipt()
    {
        var checkpoint = new EntityLifecycleCheckpoint(
            new IdSequenceCheckpoint(2),
            new IdSequenceCheckpoint(2),
            new IdSequenceCheckpoint(2),
            new InventoryRegistryCheckpoint([]),
            [],
            [new EntityMaterializationReceiptCheckpoint(
                MaterializationEffect(),
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory)],
            []);

        CheckpointResult<EntityLifecycleOwner> result = Restore(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.lifecycle.materializationReceipts[0]",
            result.Failure?.Path);
    }

    private static CheckpointResult<EntityLifecycleOwner> Restore(
        EntityLifecycleCheckpoint checkpoint)
    {
        _ = CreateOwner(
            out SpatialMovement movement,
            out ActorControlRegistry control,
            out ShipOrderCoordinator orders);
        return EntityLifecycleOwner.RestoreCheckpoint(
            checkpoint,
            movement,
            control,
            orders,
            policies: []);
    }

    private static EntityLifecycleOwner CreateOwner(
        out SpatialMovement movement,
        out ActorControlRegistry control,
        out ShipOrderCoordinator orders)
    {
        movement = new SpatialMovement();
        control = new ActorControlRegistry();
        orders = new ShipOrderCoordinator();
        return new EntityLifecycleOwner(movement, control, orders, policies: []);
    }

    /// <summary>
    /// Creates a valid setup ship while allowing multi-identity checkpoint
    /// scenarios to replace only the identifiers under test.
    /// </summary>
    private static InitialShipSetup InitialShip(
        EntityId? entityId = null,
        ShipId? shipId = null,
        InventoryId? inventoryId = null) =>
        new(
            entityId ?? GameSessionTestFixture.Entity,
            shipId ?? GameSessionTestFixture.Ship,
            inventoryId ?? GameSessionTestFixture.CargoInventory,
            GameSessionTestFixture.Principal,
            GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.PlayerController);

    private static EntityLifecycleShipCheckpoint LiveShip() =>
        new(
            GameSessionTestFixture.Entity,
            GameSessionTestFixture.Ship,
            GameSessionTestFixture.Principal,
            GameSessionTestFixture.Design.Id,
            GameSessionTestFixture.CargoInventory);

    private static ConstructionMaterializationEffect MaterializationEffect() =>
        new(
            new FacilityId(1),
            new ConstructionOrderId(1),
            GameSessionTestFixture.Design.Id,
            new SimulationTime(10),
            new EventGeneration(0),
            new EventKey(
                new SimulationTime(10),
                EventPhase.PhysicalCompletion,
                CreationSequence: 7));
}
