using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ConstructionCheckpointTests
{
    [Fact]
    public void RestorePreservesQueueReservationsAndAllocatorPosition()
    {
        Fixture fixture = CreateFixture();
        ConstructionOrderId first = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionOrderId second = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(2));
        Assert.Null(fixture.Process.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));

        RestoredConstructionOwner restored = Restore(fixture);
        ConstructionProcess process = Assert.Single(restored.Processes.Values);

        Assert.Equal(first, process.ActiveOrder!.Id);
        Assert.Equal(1, process.QueuedOrderCount);
        Assert.Equal(second, process.GetOrder(second)!.Id);
        Assert.Equal(new Quantity(2), process.ActiveOrder.ReservedInput(
            fixture.Inventory,
            new MaterialId(1)));
        Assert.Equal(new ConstructionOrderId(3), restored.Ids.AllocateOrder());
    }

    [Fact]
    public void RestorePreservesPendingMaterializationAndAcknowledgementIdentity()
    {
        Fixture fixture = CreateFixture();
        ConstructionOrderId pendingId = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionOrderId completedId = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        var eventKey = new EventKey(
            new SimulationTime(5_000),
            EventPhase.PhysicalCompletion,
            7);
        ConstructionMaterializationEffect pending = CompleteActive(fixture, eventKey);
        ConstructionMaterializationEffect completed = CompleteActive(fixture, eventKey with
        {
            CreationSequence = 8,
        });
        var identity = new ConstructionMaterializationIdentity.Ship(
            new EntityId(4),
            new ShipId(5),
            new InventoryId(6));
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.Applied,
            fixture.Process.AcknowledgeMaterialization(completed, identity));

        RestoredConstructionOwner restored = Restore(fixture);
        ConstructionProcess process = Assert.Single(restored.Processes.Values);

        Assert.Equal(pending, process.GetPendingMaterialization(pendingId));
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.AlreadyAcknowledged,
            process.AcknowledgeMaterialization(completed, identity));
        Assert.Equal(completedId, process.GetCompletedOrder(completedId)!.Id);
    }

    [Fact]
    public void RestorePreservesRunningCompletionIdentity()
    {
        Fixture fixture = CreateFixture();
        ConstructionOrderId orderId = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(4));
        SimulationTime completesAt = Assert.IsType<SimulationTime>(
            fixture.Process.PrepareActive(
                fixture.ReservationIds,
                fixture.Inventory,
                new SimulationTime(100)));

        ConstructionProcess process = Assert.Single(Restore(fixture).Processes.Values);
        ScheduledEventDisposition disposition = process.CompleteScheduled(
            orderId,
            new EventGeneration(0),
            completesAt,
            new EventKey(completesAt, EventPhase.PhysicalCompletion, 9),
            out ConstructionMaterializationEffect? materialization);

        Assert.Equal(ScheduledEventDisposition.Applied, disposition);
        Assert.Equal(orderId, materialization!.OrderId);
    }

    [Fact]
    public void RestoreCanonicalizesUnorderedProcessesAndOrders()
    {
        Fixture first = CreateFixture(2, 2, 2);
        Fixture second = CreateFixture(1, 1, 1);
        first.Process.Enqueue(first.Ids, first.Design);
        ConstructionOrderId secondFirst = second.Process.Enqueue(first.Ids, second.Design);
        ConstructionOrderId secondQueued = second.Process.Enqueue(first.Ids, second.Design);
        ConstructionProcessCheckpoint secondCheckpoint = second.Process.CaptureCheckpoint();
        var reorderedSecond = secondCheckpoint with
        {
            Orders = secondCheckpoint.Orders.Reverse().ToArray(),
        };
        var checkpoint = new ConstructionOwnerCheckpoint(
            first.Ids.CaptureCheckpoint(),
            [first.Process.CaptureCheckpoint(), reorderedSecond]);
        var inventories = new InventoryRegistry();
        inventories.Add(first.Inventory);
        inventories.Add(second.Inventory);
        var catalog = new ConstructionDesignCatalog();
        catalog.Add(first.Design);
        catalog.Add(second.Design);

        RestoredConstructionOwner restored = Assert.IsType<RestoredConstructionOwner>(
            ConstructionCheckpointRestore.Restore(checkpoint, inventories, catalog).Value);
        ConstructionOwnerCheckpoint captured = restored.CaptureCheckpoint();

        Assert.Equal([1UL, 2UL], captured.Processes.Select(process => process!.FacilityId.Value));
        Assert.Equal(
            [secondFirst.Value, secondQueued.Value],
            captured.Processes[0]!.Orders.Select(order => order!.Id.Value));
    }

    [Fact]
    public void RestoreRejectsUnknownDesignReference()
    {
        Fixture fixture = CreateFixture();
        fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionOwnerCheckpoint checkpoint = fixture.Checkpoint();
        ConstructionProcessCheckpoint process = checkpoint.Processes[0]!;
        ConstructionOrderCheckpoint order = process.Orders[0]! with
        {
            DesignId = new ConstructionDesignId(99),
        };

        CheckpointResult<RestoredConstructionOwner> result =
            ConstructionCheckpointRestore.Restore(
                checkpoint with
                {
                    Processes = [process with { Orders = [order] }],
                },
                fixture.Inventories,
                fixture.Catalog);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].orders[0].designId",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsRunningOrderWithoutCompletionTime()
    {
        Fixture fixture = CreateFixture();
        ConstructionOrderId id = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionProcessCheckpoint process = fixture.Process.CaptureCheckpoint();
        ConstructionOrderCheckpoint order = process.Orders[0]! with
        {
            Status = ConstructionOrderStatus.Running,
            CompletesAt = null,
        };

        CheckpointResult<RestoredConstructionOwner> result = RestoreCorrupt(
            fixture,
            process with { ActiveOrderId = id, Orders = [order] });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].orders[0].completesAt",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsReservationOwnedByDifferentOrder()
    {
        Fixture fixture = CreateFixture();
        fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(2));
        Assert.Null(fixture.Process.PrepareActive(
            fixture.ReservationIds,
            fixture.Inventory,
            SimulationTime.Zero));
        ConstructionProcessCheckpoint process = fixture.Process.CaptureCheckpoint();
        ConstructionOrderCheckpoint order = process.Orders[0]! with
        {
            Id = new ConstructionOrderId(2),
        };

        CheckpointResult<RestoredConstructionOwner> result =
            ConstructionCheckpointRestore.Restore(
                new ConstructionOwnerCheckpoint(
                    new IdSequenceCheckpoint(3),
                    [process with { ActiveOrderId = order.Id, Orders = [order] }]),
                fixture.Inventories,
                fixture.Catalog);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].orders[0].reservations[0]",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsAwaitingOrderWithoutPendingEffect()
    {
        Fixture fixture = CreateFixture();
        fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        CompleteActive(fixture, null);
        ConstructionProcessCheckpoint process = fixture.Process.CaptureCheckpoint();

        CheckpointResult<RestoredConstructionOwner> result = RestoreCorrupt(
            fixture,
            process with { PendingMaterializations = [] });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].orders",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsReceiptForNoncompletedOrder()
    {
        Fixture fixture = CreateFixture();
        fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionMaterializationEffect effect = CompleteActive(fixture, null);
        Assert.Equal(
            ConstructionMaterializationAcknowledgement.Applied,
            fixture.Process.AcknowledgeMaterialization(effect));
        ConstructionProcessCheckpoint process = fixture.Process.CaptureCheckpoint();
        ConstructionOrderCheckpoint order = process.Orders[0]! with
        {
            Status = ConstructionOrderStatus.AwaitingMaterialization,
        };

        CheckpointResult<RestoredConstructionOwner> result = RestoreCorrupt(
            fixture,
            process with { Orders = [order] });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].materializationReceipts[0]",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsQueueWithoutActiveOrder()
    {
        Fixture fixture = CreateFixture();
        ConstructionOrderId first = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionOrderId second = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        ConstructionProcessCheckpoint process = fixture.Process.CaptureCheckpoint();

        CheckpointResult<RestoredConstructionOwner> result = RestoreCorrupt(
            fixture,
            process with
            {
                ActiveOrderId = null,
                QueuedOrderIds = [first, second],
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].activeOrderId",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsConstructionReservationOmittedFromOrderLinks()
    {
        Fixture fixture = CreateFixture();
        ConstructionOrderId id = fixture.Process.Enqueue(fixture.Ids, fixture.Design);
        fixture.Inventory.Add(new MaterialId(1), new Quantity(2));
        fixture.Inventory.Reserve(
            new ReservationId(1),
            new MaterialId(1),
            new Quantity(2),
            new ReservationOwner.ConstructionOrder(id));

        CheckpointResult<RestoredConstructionOwner> result =
            ConstructionCheckpointRestore.Restore(
                fixture.Checkpoint(),
                fixture.Inventories,
                fixture.Catalog);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.construction.processes[0].orders",
            result.Failure!.Path);
    }

    private static ConstructionMaterializationEffect CompleteActive(
        Fixture fixture,
        EventKey? eventKey)
    {
        fixture.Inventory.Add(new MaterialId(1), new Quantity(4));
        SimulationTime completesAt = Assert.IsType<SimulationTime>(
            fixture.Process.PrepareActive(
                fixture.ReservationIds,
                fixture.Inventory,
                SimulationTime.Zero));
        return Assert.IsType<ConstructionMaterializationEffect>(
            fixture.Process.CompleteActive(completesAt, eventKey));
    }

    private static RestoredConstructionOwner Restore(Fixture fixture) =>
        Assert.IsType<RestoredConstructionOwner>(
            ConstructionCheckpointRestore.Restore(
                fixture.Checkpoint(),
                fixture.Inventories,
                fixture.Catalog).Value);

    private static CheckpointResult<RestoredConstructionOwner> RestoreCorrupt(
        Fixture fixture,
        ConstructionProcessCheckpoint process) =>
        ConstructionCheckpointRestore.Restore(
            new ConstructionOwnerCheckpoint(fixture.Ids.CaptureCheckpoint(), [process]),
            fixture.Inventories,
            fixture.Catalog);

    private static Fixture CreateFixture(
        ulong facilityId = 1,
        ulong inventoryId = 1,
        ulong designId = 1)
    {
        var inventory = new Inventory(new InventoryId(inventoryId), new Quantity(20));
        var inventories = new InventoryRegistry();
        inventories.Add(inventory);
        var design = new TestConstructionDesign(
            new ConstructionDesignId(designId),
            $"Design {designId}",
            new ConstructionRecipe(
                [new KeyValuePair<MaterialId, Quantity>(new MaterialId(1), new Quantity(4))],
                new Work(5)));
        var catalog = new ConstructionDesignCatalog();
        catalog.Add(design);
        return new Fixture(
            new ConstructionProcess(
                new FacilityId(facilityId),
                inventory.Id,
                new Throughput(4)),
            inventory,
            inventories,
            design,
            catalog,
            new ConstructionIdSequences(),
            new IdSequence<ReservationId>());
    }

    private sealed record Fixture(
        ConstructionProcess Process,
        Inventory Inventory,
        InventoryRegistry Inventories,
        ConstructionDesign Design,
        ConstructionDesignCatalog Catalog,
        ConstructionIdSequences Ids,
        IdSequence<ReservationId> ReservationIds)
    {
        internal ConstructionOwnerCheckpoint Checkpoint() =>
            new(Ids.CaptureCheckpoint(), [Process.CaptureCheckpoint()]);
    }

    private sealed class TestConstructionDesign : ConstructionDesign
    {
        internal TestConstructionDesign(
            ConstructionDesignId id,
            string name,
            ConstructionRecipe recipe)
            : base(id, name, recipe)
        {
        }
    }
}
