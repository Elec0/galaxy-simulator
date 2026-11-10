using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class EconomicProviderLossTests
{
    private static readonly MaterialId Input = new(1);
    private static readonly PhysicalDefinition Definition = new(QualifiedContentKey.Create("test.provider-loss", "material", "input"),
        PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void FinalRemovalCancelsQueueAndDoesNotDoubleReleaseSourceLossClaim()
    {
        var state = WaitingProduction();
        ProductionJobId queued = state.Line.Enqueue(state.Ids, new([], new(2), new(1), new(1)), false);
        var removal = Assert.IsType<PreparedProductionProviderRemoval>(state.Line.PrepareProviderRemoval(state.Inventory));
        var release = Assert.IsType<InventoryCommitmentRelease.Material>(Assert.Single(removal.Releases));
        PreparedCargoSourceLoss loss = PrepareLoss(state.Inventory, release.Reservation);

        Assert.True(state.Line.CanApplyProviderRemoval(removal, loss));
        Assert.True(loss.Apply());
        state.Line.ApplyProviderRemoval(removal, loss);

        Assert.Equal(ProductionJobStatus.Cancelled, state.Line.GetJob(state.Active)!.Status);
        Assert.Equal(ProductionJobStatus.Cancelled, state.Line.GetJob(queued)!.Status);
        Assert.Null(state.Line.ActiveJob);
        Assert.Equal(0, state.Line.QueuedJobCount);
        Assert.Equal(new Quantity(1), state.Inventory.Stored(Input));
        Assert.Equal(Quantity.Zero, state.Inventory.Reserved(Input));
        Assert.False(state.Line.IsProviderAvailable);
    }

    [Fact]
    public void LostReservedInputPrunesOnlyItsProvenLinkAndReacquiresMissingStockNormally()
    {
        var state = WaitingProduction();
        Reservation reservation = Assert.Single(state.Inventory.CaptureCheckpoint().Reservations);
        var release = new InventoryCommitmentRelease.Material(state.Inventory, state.Inventory.GetReservation(reservation.Id)!);
        var invalidation = Assert.IsType<PreparedProductionInputLoss>(state.Line.PrepareInputLoss([release]));
        PreparedCargoSourceLoss loss = PrepareLoss(state.Inventory, release.Reservation);
        EventGeneration before = state.Line.ActiveJob!.Generation;
        Assert.True(state.Line.CanCommitInputLoss(invalidation, loss));
        Assert.True(loss.Apply());
        state.Line.ApplyInputLossAfterSourceLoss(invalidation, loss);

        Assert.Equal(ProductionJobStatus.WaitingForInputs, state.Line.ActiveJob.Status);
        Assert.Equal(before.Next(), state.Line.ActiveJob.Generation);
        Assert.Empty(state.Line.ActiveJob.AllReservationIds());
        Assert.Null(state.Line.PrepareActive(state.Reservations, state.Inventory, new(100)));
        Assert.Equal(new Quantity(1), state.Inventory.Reserved(Input));
    }

    [Fact]
    public void ConstructionFinalRemovalCancelsPausedWorkAndQueuedOrders()
    {
        var inventory = new Inventory(new(1), new(20));
        inventory.Add(Input, new(1));
        var process = new ConstructionProcess(new(1), inventory.Id, new(3));
        var ids = new ConstructionIdSequences();
        var design = new ShipDesign(new(1), "Provider Ship", new([new(Input, new(1))], new(10)),
            new(5), GameSessionTestFixture.ManeuverCapability, 0, 0);
        ConstructionOrderId first = process.Enqueue(ids, design);
        ConstructionOrderId second = process.Enqueue(ids, design);
        process.PrepareActive(new(), inventory, SimulationTime.Zero);
        process.ApplyProviderTransition(Assert.IsType<PreparedConstructionProviderTransition>(
            process.PrepareProviderTransition(false, new(3), new(501))));
        var removal = Assert.IsType<PreparedConstructionProviderRemoval>(process.PrepareProviderRemoval(inventory));

        Assert.True(process.CanApplyProviderRemoval(removal));
        process.ApplyProviderRemoval(removal);

        Assert.Equal(ConstructionOrderStatus.Cancelled, process.GetOrder(first)!.Status);
        Assert.Equal(ConstructionOrderStatus.Cancelled, process.GetOrder(second)!.Status);
        Assert.Null(process.ActiveOrder);
        Assert.Equal(0, process.QueuedOrderCount);
        Assert.Equal(Quantity.Zero, inventory.Stored(Input));
        Assert.False(process.CanApplyProviderRemoval(removal));
    }

    [Fact]
    public void ConstructionInputLossRetainsWaitingOrderAndProvesExactReleasedClaim()
    {
        var state = WaitingProduction();
        state.Line.CancelActive(state.Inventory);
        var process = new ConstructionProcess(new(2), state.Inventory.Id, new(3));
        var design = new ShipDesign(new(1), "Provider Ship", new([new(Input, new(3))], new(10)),
            new(5), GameSessionTestFixture.ManeuverCapability, 0, 0);
        process.Enqueue(new(), design);
        process.PrepareActive(state.Reservations, state.Inventory, SimulationTime.Zero);
        Reservation claim = Assert.Single(state.Inventory.CaptureCheckpoint().Reservations);
        var release = new InventoryCommitmentRelease.Material(state.Inventory, state.Inventory.GetReservation(claim.Id)!);
        var effect = Assert.IsType<PreparedConstructionInputLoss>(process.PrepareInputLoss([release]));
        PreparedCargoSourceLoss loss = PrepareLoss(state.Inventory, release.Reservation);

        Assert.True(process.CanCommitInputLoss(effect, loss));
        Assert.True(loss.Apply());
        process.ApplyInputLossAfterSourceLoss(effect, loss);

        Assert.Equal(ConstructionOrderStatus.WaitingForInputs, process.ActiveOrder!.Status);
        Assert.Empty(process.ActiveOrder.AllReservationIds());
        Assert.Null(process.PrepareActive(state.Reservations, state.Inventory, new(100)));
        Assert.Equal(new Quantity(1), state.Inventory.Reserved(Input));
    }

    private static (Inventory Inventory, ProductionLine Line, ProductionIdSequences Ids, ProductionJobId Active,
        IdSequence<ReservationId> Reservations) WaitingProduction()
    {
        var materials = new MaterialInventoryCompatibilityMap([new(Input, Definition)]);
        var inventory = new Inventory(new(1), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(1)), new(1)), new(10), materials);
        inventory.Add(Input, new(2));
        var line = new ProductionLine(new(1), inventory.Id, new(1));
        var ids = new ProductionIdSequences();
        ProductionJobId active = line.Enqueue(ids, new([new(Input, new(3))], new(2), new(1), new(1)), false);
        var reservations = new IdSequence<ReservationId>();
        line.PrepareActive(reservations, inventory, SimulationTime.Zero);
        return (inventory, line, ids, active, reservations);
    }

    private static PreparedCargoSourceLoss PrepareLoss(Inventory inventory, Reservation reservation) =>
        CargoSourceLoss.Prepare(inventory, [new PhysicalTransferSubject.Fungible(Definition.Key, new(1))],
            new PhysicalDefinitionCatalog([Definition]),
            [new CargoSourceReservationCleanup.Legacy(reservation.Id, reservation.Owner)]).Prepared!;
}
