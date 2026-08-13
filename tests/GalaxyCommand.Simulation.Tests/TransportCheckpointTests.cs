using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class TransportCheckpointTests
{
    [Fact]
    public void RestorePreservesLoadingContinuationAndAllocatorPositions()
    {
        Fixture fixture = CreateFixture();
        TransportJobId jobId = PublishAssignAndStart(fixture);
        TransportJob job = Assert.IsType<TransportJob>(fixture.Board.GetJob(jobId));

        RestoredTransportOwner restored = Restore(fixture);
        TransportJob restoredJob = Assert.IsType<TransportJob>(restored.Board.GetJob(jobId));
        Freighter freighter = Assert.IsType<Freighter>(
            restored.Ships.GetFreighter(fixture.ShipId));
        ScheduledEventDisposition disposition = restored.Board.CommitEventCore(
            new TransportEvent.FinishLoading(jobId, restoredJob.Generation),
            freighter,
            fixture.Inventories,
            fixture.Navigation,
            job.TransitionAt!.Value).Disposition;

        Assert.Equal(ScheduledEventDisposition.Applied, disposition);
        Assert.Equal(
            new Quantity(4),
            fixture.Inventories.Get(fixture.CargoInventoryId)!.Stored(fixture.MaterialId));
        Assert.Equal(new SupplyOfferId(2), restored.Ids.AllocateOffer());
        Assert.Equal(new DemandRequestId(2), restored.Ids.AllocateDemand());
        Assert.Equal(new TransportJobId(2), restored.Ids.AllocateJob());
        Assert.Equal(new ReservationId(2), restored.ReservationIds.Allocate());
        Assert.Equal(
            new CapacityReservationId(1),
            restored.CapacityReservationIds.Allocate());
    }

    [Fact]
    public void RestorePreservesUnloadingContinuationAndCapacityReservation()
    {
        Fixture fixture = CreateFixture();
        TransportJobId jobId = PublishAssignAndStart(fixture);
        AdvanceToUnloading(fixture, jobId);
        TransportJob original = Assert.IsType<TransportJob>(fixture.Board.GetJob(jobId));

        RestoredTransportOwner restored = Restore(fixture);
        TransportJob job = Assert.IsType<TransportJob>(restored.Board.GetJob(jobId));
        Freighter freighter = Assert.IsType<Freighter>(
            restored.Ships.GetFreighter(fixture.ShipId));
        ScheduledEventDisposition disposition = restored.Board.CommitEventCore(
            new TransportEvent.FinishUnloading(jobId, job.Generation),
            freighter,
            fixture.Inventories,
            fixture.Navigation,
            original.TransitionAt!.Value).Disposition;

        Assert.Equal(ScheduledEventDisposition.Applied, disposition);
        Assert.Equal(TransportJobStatus.Completed, job.Status);
        Assert.Null(freighter.ActiveJobId);
        Assert.Equal(
            new Quantity(4),
            fixture.Inventories.Get(fixture.DestinationInventoryId)!
                .Stored(fixture.MaterialId));
    }

    [Fact]
    public void RestoreCanonicalizesUnorderedMarketAndFreighters()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);
        TransportOwnerCheckpoint captured = fixture.Checkpoint();
        TransportBoardCheckpoint board = captured.Board with
        {
            Supplies = captured.Board.Supplies.Reverse().ToArray(),
            Demands = captured.Board.Demands.Reverse().ToArray(),
            Jobs = captured.Board.Jobs.Reverse().ToArray(),
        };
        TransportOwnerCheckpoint reordered = captured with
        {
            Board = board,
            Freighters = captured.Freighters.Reverse().ToArray(),
        };

        RestoredTransportOwner restored = Assert.IsType<RestoredTransportOwner>(
            TransportCheckpointRestore.Restore(
                reordered,
                fixture.Inventories,
                fixture.LiveShips).Value);
        TransportOwnerCheckpoint recaptured = restored.CaptureCheckpoint();

        Assert.Equal(
            captured.Board.Supplies.Select(supply => supply!.Id),
            recaptured.Board.Supplies.Select(supply => supply!.Id));
        Assert.Equal(
            captured.Board.Demands.Select(demand => demand!.Id),
            recaptured.Board.Demands.Select(demand => demand!.Id));
        Assert.Equal(
            captured.Board.Jobs.Select(job => job!.Id),
            recaptured.Board.Jobs.Select(job => job!.Id));
        Assert.Equal(
            captured.Freighters.Select(freighter => freighter!.ShipId),
            recaptured.Freighters.Select(freighter => freighter!.ShipId));
    }

    [Fact]
    public void RestoreRejectsJobAtOrBeyondAllocatorPosition()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            fixture.Checkpoint() with
            {
                Ids = fixture.Checkpoint().Ids with
                {
                    JobIds = new IdSequenceCheckpoint(1),
                },
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.transport.board.jobs[0].id", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsJobWithMismatchedMarketReference()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        TransportJobCheckpoint job = checkpoint.Board.Jobs[0]! with
        {
            MaterialId = new MaterialId(2),
        };

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            checkpoint with
            {
                Board = checkpoint.Board with { Jobs = [job] },
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.transport.board.jobs[0]", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsLoadingJobWithoutTransitionTime()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        TransportJobCheckpoint job = checkpoint.Board.Jobs[0]! with
        {
            TransitionAt = null,
        };

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            checkpoint with
            {
                Board = checkpoint.Board with { Jobs = [job] },
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.transport.board.jobs[0].transitionAt",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsActiveFreighterJobMismatch()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        TransportFreighterCheckpoint freighter = checkpoint.Freighters[0]! with
        {
            ActiveJobId = null,
        };

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            checkpoint with { Freighters = [freighter] });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.transport.freighters[0].activeJobId", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsLoadingJobWithoutSourceReservation()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        Inventory source = fixture.Inventories.Get(fixture.SourceInventoryId)!;
        source.Release(checkpoint.Board.Jobs[0]!.SourceReservationId);

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(fixture, checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.transport.board.jobs[0].sourceReservationId",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsUnloadingJobWithoutCapacityReservation()
    {
        Fixture fixture = CreateFixture();
        TransportJobId jobId = PublishAssignAndStart(fixture);
        AdvanceToUnloading(fixture, jobId);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        CapacityReservationId reservationId =
            checkpoint.Board.Jobs[0]!.DestinationCapacityReservationId!.Value;
        fixture.Inventories.Get(fixture.DestinationInventoryId)!
            .ReleaseCapacity(reservationId);

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(fixture, checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.transport.board.jobs[0].destinationCapacityReservationId",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsCapacityReservationIdentityOutsideUnloading()
    {
        Fixture fixture = CreateFixture();
        PublishAssignAndStart(fixture);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        TransportJobCheckpoint job = checkpoint.Board.Jobs[0]! with
        {
            DestinationCapacityReservationId = new CapacityReservationId(1),
        };

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            checkpoint with
            {
                CapacityReservationIds = new IdSequenceCheckpoint(2),
                Board = checkpoint.Board with { Jobs = [job] },
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.economy.transport.board.jobs[0].destinationCapacityReservationId",
            result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsLoadedPhaseWithoutFreighterCargo()
    {
        Fixture fixture = CreateFixture();
        TransportJobId jobId = PublishAssignAndStart(fixture);
        AdvanceToUnloading(fixture, jobId);
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        fixture.Inventories.Get(fixture.CargoInventoryId)!
            .RemoveAvailable(fixture.MaterialId, new Quantity(4));

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(fixture, checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.transport.board.jobs[0].status", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsOrphanedTransportReservation()
    {
        Fixture fixture = CreateFixture();
        fixture.Inventories.Get(fixture.SourceInventoryId)!.Reserve(
            new ReservationId(1),
            fixture.MaterialId,
            new Quantity(1),
            new ReservationOwner.TransportJob(new TransportJobId(1)));

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            fixture.Checkpoint() with
            {
                ReservationIds = new IdSequenceCheckpoint(2),
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.transport.board.jobs", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsFreighterWhoseCargoDisagreesWithLiveShip()
    {
        Fixture fixture = CreateFixture();
        TransportOwnerCheckpoint checkpoint = fixture.Checkpoint();
        TransportFreighterCheckpoint freighter = checkpoint.Freighters[0]! with
        {
            CargoInventoryId = fixture.SourceInventoryId,
        };

        CheckpointResult<RestoredTransportOwner> result = RestoreCorrupt(
            fixture,
            checkpoint with { Freighters = [freighter] });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.economy.transport.freighters[0].cargoInventoryId", result.Failure!.Path);
    }

    [Fact]
    public void RestorePreservesCancelledJobWithoutRemovedFreighterCapability()
    {
        Fixture fixture = CreateFixture();
        TransportJobId jobId = PublishAssignAndStart(fixture);
        Assert.True(fixture.Board.CancelOrInterrupt(
            jobId,
            fixture.Freighter,
            fixture.Inventories));
        Assert.True(fixture.Ships.RemoveFreighter(fixture.ShipId));
        TransportOwnerCheckpoint checkpoint = TransportCheckpointCapture.Capture(
            fixture.Board,
            fixture.Ships,
            fixture.Ids,
            fixture.ReservationIds,
            fixture.CapacityReservationIds,
            fixture.Timing);

        RestoredTransportOwner restored = Assert.IsType<RestoredTransportOwner>(
            TransportCheckpointRestore.Restore(
                checkpoint,
                fixture.Inventories,
                new Dictionary<ShipId, InventoryId>()).Value);

        Assert.Equal(TransportJobStatus.Cancelled, restored.Board.GetJob(jobId)!.Status);
        Assert.Null(restored.Ships.GetFreighter(fixture.ShipId));
    }

    private static RestoredTransportOwner Restore(Fixture fixture) =>
        Assert.IsType<RestoredTransportOwner>(
            TransportCheckpointRestore.Restore(
                fixture.Checkpoint(),
                fixture.Inventories,
                fixture.LiveShips).Value);

    private static CheckpointResult<RestoredTransportOwner> RestoreCorrupt(
        Fixture fixture,
        TransportOwnerCheckpoint checkpoint) =>
        TransportCheckpointRestore.Restore(
            checkpoint,
            fixture.Inventories,
            fixture.LiveShips);

    private static TransportJobId PublishAssignAndStart(Fixture fixture)
    {
        fixture.Board.PublishSupply(
            fixture.Ids,
            fixture.SourceInventoryId,
            fixture.LocationId,
            fixture.MaterialId,
            new Quantity(4));
        fixture.Board.PublishDemand(
            fixture.Ids,
            fixture.DestinationInventoryId,
            fixture.LocationId,
            fixture.MaterialId,
            new Quantity(4),
            new DemandPriority(1),
            SimulationTime.Zero);
        TransportJobId jobId = Assert.IsType<TransportJobId>(fixture.Board.AssignBest(
            fixture.Ids,
            fixture.ReservationIds,
            fixture.Freighter,
            fixture.Inventories,
            fixture.Navigation,
            SimulationTime.Zero));
        Assert.True(fixture.Board.StartOrRetry(
            jobId,
            fixture.Freighter,
            fixture.Inventories,
            fixture.CapacityReservationIds,
            fixture.Navigation,
            fixture.Agenda,
            fixture.Timing,
            SimulationTime.Zero));
        return jobId;
    }

    private static void AdvanceToUnloading(Fixture fixture, TransportJobId jobId)
    {
        ScheduledEvent<TransportEvent> loading = Assert.IsType<ScheduledEvent<TransportEvent>>(
            fixture.Agenda.PopNextThrough(new SimulationTime(10_000)));
        Assert.Equal(
            ScheduledEventDisposition.Applied,
            fixture.Board.HandleEvent(
                loading.Payload,
                fixture.Freighter,
                fixture.Inventories,
                fixture.CapacityReservationIds,
                fixture.Navigation,
                fixture.Agenda,
                fixture.Timing,
                loading.Key.Timestamp));
        Assert.Equal(TransportJobStatus.Unloading, fixture.Board.GetJob(jobId)!.Status);
    }

    private static Fixture CreateFixture()
    {
        var inventoryIds = new IdSequence<InventoryId>();
        InventoryId sourceId = inventoryIds.Allocate();
        InventoryId destinationId = inventoryIds.Allocate();
        InventoryId cargoId = inventoryIds.Allocate();
        MaterialId materialId = new IdSequence<MaterialId>().Allocate();
        var source = new Inventory(sourceId, new Quantity(10));
        source.Add(materialId, new Quantity(10));
        var inventories = new InventoryRegistry();
        inventories.Add(source);
        inventories.Add(new Inventory(destinationId, new Quantity(10)));
        inventories.Add(new Inventory(cargoId, new Quantity(4)));
        LocationId locationId = new IdSequence<LocationId>().Allocate();
        var navigation = new RouteGraph();
        navigation.AddLocation(locationId);
        ShipId shipId = new IdSequence<ShipId>().Allocate();
        var ships = new ShipRegistry();
        ships.AddFreighter(shipId, locationId, cargoId);
        var timing = new TransportTiming(
            new SimulationDuration(10),
            new TransferRate(4),
            new TransferRate(4));
        return new Fixture(
            new TransportBoard(),
            new TransportIdSequences(),
            new IdSequence<ReservationId>(),
            new IdSequence<CapacityReservationId>(),
            inventories,
            ships,
            navigation,
            new EventAgenda<TransportEvent>(),
            timing,
            shipId,
            locationId,
            sourceId,
            destinationId,
            cargoId,
            materialId,
            new Dictionary<ShipId, InventoryId> { [shipId] = cargoId });
    }

    private sealed record Fixture(
        TransportBoard Board,
        TransportIdSequences Ids,
        IdSequence<ReservationId> ReservationIds,
        IdSequence<CapacityReservationId> CapacityReservationIds,
        InventoryRegistry Inventories,
        ShipRegistry Ships,
        RouteGraph Navigation,
        EventAgenda<TransportEvent> Agenda,
        TransportTiming Timing,
        ShipId ShipId,
        LocationId LocationId,
        InventoryId SourceInventoryId,
        InventoryId DestinationInventoryId,
        InventoryId CargoInventoryId,
        MaterialId MaterialId,
        IReadOnlyDictionary<ShipId, InventoryId> LiveShips)
    {
        internal Freighter Freighter => Ships.GetFreighter(ShipId)!;

        internal TransportOwnerCheckpoint Checkpoint() =>
            TransportCheckpointCapture.Capture(
                Board,
                Ships,
                Ids,
                ReservationIds,
                CapacityReservationIds,
                Timing);
    }
}
