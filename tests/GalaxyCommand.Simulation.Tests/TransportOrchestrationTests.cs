using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class TransportOrchestrationTests
{
    [Fact]
    public void TransportEvaluationDoesNotMutateAssignedJob()
    {
        TransportSystemFixture fixture = CreateFixture(shipCount: 1);
        TransportJobId jobId = AssignJobs(fixture).Single();
        var system = new TransportSystem();

        TransportAdvanceBatch batch = system.CreateBatch(
            fixture.Board,
            fixture.Ships,
            fixture.Inventories);
        TransportAdvanceEvaluation evaluation = TransportSystem.Evaluate(
            batch,
            fixture.Navigation,
            fixture.Timing,
            SimulationTime.Zero);

        Assert.IsType<TransportAdvanceEffect.BeginLoading>(
            Assert.Single(evaluation.Effects));
        Assert.Equal(
            TransportJobStatus.Assigned,
            fixture.Board.GetJob(jobId)?.Status);
        Assert.Null(fixture.Board.GetJob(jobId)?.TransitionAt);
        Assert.Equal(
            Quantity.Zero,
            fixture.Inventories.Get(fixture.DestinationInventoryId)?
                .ReservedCapacity);

        TransportAdvanceCommitResult commit = system.Commit(
            evaluation,
            fixture.Board,
            fixture.Ships,
            fixture.Inventories,
            fixture.CapacityReservationIds);

        Assert.Equal(
            TransportJobStatus.Loading,
            fixture.Board.GetJob(jobId)?.Status);
        Assert.IsType<TransportEvent.FinishLoading>(
            Assert.Single(commit.EventProposals).Event);
    }

    [Fact]
    public void ShipOrderedCommitRevalidatesSharedDestinationCapacity()
    {
        TransportSystemFixture fixture = CreateFixture(shipCount: 2);
        TransportJobId[] jobIds = AssignJobs(fixture);
        var system = new TransportSystem();
        TransportAdvanceReconciliationResult loading = system.Reconcile(
            fixture.Board,
            fixture.Ships,
            fixture.Inventories,
            fixture.CapacityReservationIds,
            fixture.Navigation,
            fixture.Timing,
            SimulationTime.Zero);
        Assert.Equal(2, loading.Commit.EventProposals.Count);

        var unusedAgenda = new EventAgenda<TransportEvent>();
        foreach (TransportEventProposal proposal in loading.Commit.EventProposals)
        {
            Freighter freighter = Assert.IsType<Freighter>(
                fixture.Ships.GetFreighter(proposal.ShipId));
            Assert.Equal(
                ScheduledEventDisposition.Applied,
                fixture.Board.HandleEvent(
                    proposal.Event,
                    freighter,
                    fixture.Inventories,
                    fixture.CapacityReservationIds,
                    fixture.Navigation,
                    unusedAgenda,
                    fixture.Timing,
                    proposal.Timestamp));
        }

        Inventory destination = Assert.IsType<Inventory>(
            fixture.Inventories.Get(fixture.DestinationInventoryId));
        Assert.All(jobIds, jobId =>
            Assert.Equal(
                TransportJobStatus.WaitingForDestinationCapacity,
                fixture.Board.GetJob(jobId)?.Status));
        destination.RemoveAvailable(
            fixture.BlockingMaterial,
            new Quantity(4));

        TransportAdvanceBatch retryBatch = system.CreateBatch(
            fixture.Board,
            fixture.Ships,
            fixture.Inventories);
        TransportAdvanceEvaluation retryEvaluation = TransportSystem.Evaluate(
            retryBatch,
            fixture.Navigation,
            fixture.Timing,
            loading.Commit.EventProposals[0].Timestamp);
        Assert.All(retryEvaluation.Effects, effect =>
            Assert.IsType<TransportAdvanceEffect.BeginUnloading>(effect));

        TransportAdvanceCommitResult retry = system.Commit(
            retryEvaluation,
            fixture.Board,
            fixture.Ships,
            fixture.Inventories,
            fixture.CapacityReservationIds);

        Assert.Single(retry.EventProposals);
        TransportJob first = Assert.IsType<TransportJob>(
            fixture.Board.GetJob(jobIds[0]));
        TransportJob second = Assert.IsType<TransportJob>(
            fixture.Board.GetJob(jobIds[1]));
        Assert.True(first.ShipId.Value < second.ShipId.Value);
        Assert.Equal(TransportJobStatus.Unloading, first.Status);
        Assert.Equal(
            TransportJobStatus.WaitingForDestinationCapacity,
            second.Status);
        Assert.Equal(new Quantity(4), destination.ReservedCapacity);
    }

    private static TransportJobId[] AssignJobs(TransportSystemFixture fixture)
    {
        fixture.Board.PublishSupply(
            fixture.TransportIds,
            fixture.SourceInventoryId,
            fixture.Location,
            fixture.Material,
            new Quantity(8));
        fixture.Board.PublishDemand(
            fixture.TransportIds,
            fixture.DestinationInventoryId,
            fixture.Location,
            fixture.Material,
            new Quantity(8),
            new DemandPriority(1),
            SimulationTime.Zero);

        var jobs = new List<TransportJobId>();
        foreach (ShipId shipId in fixture.Ships.FreighterIds)
        {
            Freighter freighter = Assert.IsType<Freighter>(
                fixture.Ships.GetFreighter(shipId));
            jobs.Add(Assert.IsType<TransportJobId>(
                fixture.Board.AssignBest(
                    fixture.TransportIds,
                    fixture.ReservationIds,
                    freighter,
                    fixture.Inventories,
                    fixture.Navigation,
                    SimulationTime.Zero)));
        }

        return jobs.ToArray();
    }

    private static TransportSystemFixture CreateFixture(int shipCount)
    {
        LocationId location = new IdSequence<LocationId>().Allocate();
        var navigation = new RouteGraph();
        navigation.AddLocation(location);
        var inventoryIds = new IdSequence<InventoryId>();
        InventoryId sourceInventoryId = inventoryIds.Allocate();
        InventoryId destinationInventoryId = inventoryIds.Allocate();
        var materialIds = new IdSequence<MaterialId>();
        MaterialId material = materialIds.Allocate();
        MaterialId blockingMaterial = materialIds.Allocate();
        var source = new Inventory(sourceInventoryId, new Quantity(8));
        source.Add(material, new Quantity(8));
        var destination = new Inventory(
            destinationInventoryId,
            new Quantity(4));
        destination.Add(blockingMaterial, new Quantity(4));
        var inventories = new InventoryRegistry();
        inventories.Add(source);
        inventories.Add(destination);

        var ships = new ShipRegistry();
        var shipIds = new IdSequence<ShipId>();
        OrganizationId organizationId =
            new IdSequence<OrganizationId>().Allocate();
        ConstructionDesignId designId =
            new IdSequence<ConstructionDesignId>().Allocate();
        for (int index = 0; index < shipCount; index++)
        {
            InventoryId cargoInventoryId = inventoryIds.Allocate();
            inventories.Add(new Inventory(
                cargoInventoryId,
                new Quantity(4)));
            ShipId shipId = shipIds.Allocate();
            ships.AddFreighter(new Ship(
                shipId,
                organizationId,
                designId,
                location,
                cargoInventoryId));
        }

        return new TransportSystemFixture(
            new TransportBoard(),
            new TransportIdSequences(),
            new IdSequence<ReservationId>(),
            new IdSequence<CapacityReservationId>(),
            inventories,
            navigation,
            ships,
            new TransportTiming(
                SimulationDuration.Zero,
                new TransferRate(4),
                new TransferRate(4)),
            location,
            sourceInventoryId,
            destinationInventoryId,
            material,
            blockingMaterial);
    }

    private sealed record TransportSystemFixture(
        TransportBoard Board,
        TransportIdSequences TransportIds,
        IdSequence<ReservationId> ReservationIds,
        IdSequence<CapacityReservationId> CapacityReservationIds,
        InventoryRegistry Inventories,
        RouteGraph Navigation,
        ShipRegistry Ships,
        TransportTiming Timing,
        LocationId Location,
        InventoryId SourceInventoryId,
        InventoryId DestinationInventoryId,
        MaterialId Material,
        MaterialId BlockingMaterial);
}
