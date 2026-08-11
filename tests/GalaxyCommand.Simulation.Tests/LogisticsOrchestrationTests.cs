using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class LogisticsOrchestrationTests
{
    [Fact]
    public void PublicationEvaluationDoesNotAllocateMarketIdsOrMutateBoard()
    {
        LogisticsFixture fixture = CreateFixture();
        var system = new LogisticsSystem();
        LogisticsPublicationBatch batch = system.CreatePublicationBatch(
            [
                new LogisticsDemandPublicationRead(
                    fixture.DestinationInventoryIds[0],
                    fixture.Location,
                    fixture.Material,
                    new Quantity(6),
                    new Quantity(2),
                    new DemandPriority(1)),
            ],
            [
                new LogisticsSupplyPublicationRead(
                    fixture.SourceInventoryIds[0],
                    fixture.Location,
                    fixture.Material,
                    new Quantity(8),
                    new Quantity(3)),
            ]);

        LogisticsPublicationEvaluation evaluation =
            LogisticsSystem.EvaluatePublication(batch, SimulationTime.Zero);

        Assert.Single(evaluation.Demands);
        Assert.Single(evaluation.Supplies);
        Assert.Equal(new Quantity(4), evaluation.Demands[0].Quantity);
        Assert.Equal(new Quantity(5), evaluation.Supplies[0].Quantity);
        Assert.Null(fixture.Board.GetDemand(new DemandRequestId(1)));
        Assert.Null(fixture.Board.GetSupply(new SupplyOfferId(1)));

        LogisticsPublicationReconciliationResult reconciliation =
            system.ReconcilePublication(
            batch.Demands,
            batch.Supplies,
            fixture.Board,
            fixture.TransportIds,
            SimulationTime.Zero);

        Assert.Equal(1, reconciliation.Commit.PublishedDemandCount);
        Assert.Equal(1, reconciliation.Commit.PublishedSupplyCount);
        Assert.Equal(
            new Quantity(4),
            fixture.Board.GetDemand(new DemandRequestId(1))?.Remaining);
        Assert.Equal(
            new Quantity(5),
            fixture.Board.GetSupply(new SupplyOfferId(1))?.Remaining);
        Assert.Equal(3, reconciliation.Measurements.Count);
    }

    [Fact]
    public void ShipOrderedReducerUsesNextCandidateAfterPreferredMarketIsConsumed()
    {
        static IReadOnlyList<(ShipId ShipId, DemandRequestId DemandId)> Run(bool reverse)
        {
            LogisticsFixture fixture = CreateFixture();
            SupplyOfferId firstSupply = fixture.Board.PublishSupply(
                fixture.TransportIds,
                fixture.SourceInventoryIds[0],
                fixture.Location,
                fixture.Material,
                new Quantity(4));
            fixture.Board.PublishSupply(
                fixture.TransportIds,
                fixture.SourceInventoryIds[1],
                fixture.Location,
                fixture.Material,
                new Quantity(4));
            DemandRequestId preferredDemand = fixture.Board.PublishDemand(
                fixture.TransportIds,
                fixture.DestinationInventoryIds[0],
                fixture.Location,
                fixture.Material,
                new Quantity(4),
                new DemandPriority(2),
                SimulationTime.Zero);
            DemandRequestId fallbackDemand = fixture.Board.PublishDemand(
                fixture.TransportIds,
                fixture.DestinationInventoryIds[1],
                fixture.Location,
                fixture.Material,
                new Quantity(4),
                new DemandPriority(1),
                SimulationTime.Zero);
            Freighter[] freighters = fixture.Ships.FreighterIds
                .Select(shipId => Assert.IsType<Freighter>(
                    fixture.Ships.GetFreighter(shipId)))
                .ToArray();
            if (reverse)
            {
                Array.Reverse(freighters);
            }

            var system = new LogisticsSystem();
            LogisticsAssignmentBatch batch = system.CreateAssignmentBatch(
                fixture.Board,
                freighters,
                fixture.Inventories);
            LogisticsAssignmentEvaluation evaluation =
                LogisticsSystem.EvaluateAssignments(
                    batch,
                    fixture.Navigation,
                    SimulationTime.Zero);

            Assert.Equal(8, evaluation.Candidates.Count);
            Assert.All(freighters, freighter =>
                Assert.Null(freighter.ActiveJobId));
            Assert.Equal(
                Quantity.Zero,
                fixture.Inventories.Get(fixture.SourceInventoryIds[0])?
                    .Reserved(fixture.Material));

            LogisticsAssignmentCommitResult commit = system.CommitAssignments(
                evaluation,
                fixture.Board,
                fixture.TransportIds,
                fixture.ReservationIds,
                fixture.Ships,
                fixture.Inventories,
                SimulationTime.Zero);

            Assert.Equal(2, commit.Assignments.Count);
            LogisticsAssignmentCommit first = commit.Assignments[0];
            LogisticsAssignmentCommit second = commit.Assignments[1];
            TransportJob firstJob = Assert.IsType<TransportJob>(
                fixture.Board.GetJob(first.JobId));
            TransportJob secondJob = Assert.IsType<TransportJob>(
                fixture.Board.GetJob(second.JobId));
            Assert.True(first.ShipId.Value < second.ShipId.Value);
            Assert.Equal(preferredDemand, firstJob.DemandRequestId);
            Assert.Equal(firstSupply, firstJob.SupplyOfferId);
            Assert.Equal(fallbackDemand, secondJob.DemandRequestId);
            Assert.NotEqual(firstJob.SupplyOfferId, secondJob.SupplyOfferId);
            return commit.Assignments
                .Select(assignment =>
                {
                    TransportJob job = Assert.IsType<TransportJob>(
                        fixture.Board.GetJob(assignment.JobId));
                    return (assignment.ShipId, job.DemandRequestId);
                })
                .ToArray();
        }

        Assert.Equal(Run(reverse: false), Run(reverse: true));
    }

    private static LogisticsFixture CreateFixture()
    {
        LocationId location = new IdSequence<LocationId>().Allocate();
        var navigation = new RouteGraph();
        navigation.AddLocation(location);

        var inventoryIds = new IdSequence<InventoryId>();
        InventoryId[] sourceInventoryIds =
            [inventoryIds.Allocate(), inventoryIds.Allocate()];
        InventoryId[] destinationInventoryIds =
            [inventoryIds.Allocate(), inventoryIds.Allocate()];
        InventoryId[] cargoInventoryIds =
            [inventoryIds.Allocate(), inventoryIds.Allocate()];
        MaterialId material = new IdSequence<MaterialId>().Allocate();
        var inventories = new InventoryRegistry();
        foreach (InventoryId sourceId in sourceInventoryIds)
        {
            var source = new Inventory(sourceId, new Quantity(8));
            source.Add(material, new Quantity(4));
            inventories.Add(source);
        }

        foreach (InventoryId destinationId in destinationInventoryIds)
        {
            inventories.Add(new Inventory(destinationId, new Quantity(8)));
        }

        foreach (InventoryId cargoId in cargoInventoryIds)
        {
            inventories.Add(new Inventory(cargoId, new Quantity(4)));
        }

        var organizationIds = new IdSequence<OrganizationId>();
        OrganizationId organizationId = organizationIds.Allocate();
        var designIds = new IdSequence<ConstructionDesignId>();
        ConstructionDesignId designId = designIds.Allocate();
        var shipIds = new IdSequence<ShipId>();
        var ships = new ShipRegistry();
        foreach (InventoryId cargoId in cargoInventoryIds)
        {
            ShipId shipId = shipIds.Allocate();
            ships.AddFreighter(new Ship(
                shipId,
                organizationId,
                designId,
                location,
                cargoId));
        }

        return new LogisticsFixture(
            new TransportBoard(),
            new TransportIdSequences(),
            new IdSequence<ReservationId>(),
            inventories,
            navigation,
            ships,
            location,
            material,
            sourceInventoryIds,
            destinationInventoryIds);
    }

    private sealed record LogisticsFixture(
        TransportBoard Board,
        TransportIdSequences TransportIds,
        IdSequence<ReservationId> ReservationIds,
        InventoryRegistry Inventories,
        RouteGraph Navigation,
        ShipRegistry Ships,
        LocationId Location,
        MaterialId Material,
        IReadOnlyList<InventoryId> SourceInventoryIds,
        IReadOnlyList<InventoryId> DestinationInventoryIds);
}
