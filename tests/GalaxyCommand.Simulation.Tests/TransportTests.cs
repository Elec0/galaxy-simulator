using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class TransportTests
{
    [Fact]
    public void AssignmentCommitsSmallestAvailableQuantityAtomically()
    {
        TransportFixture fixture = CreateFixture();
        SupplyOfferId offerId = fixture.Board.PublishSupply(
            fixture.TransportIds,
            fixture.SourceInventoryId,
            fixture.SourceLocation,
            fixture.Material,
            new Quantity(10));
        DemandRequestId demandId = fixture.Board.PublishDemand(
            fixture.TransportIds,
            fixture.DestinationInventoryId,
            fixture.DestinationLocation,
            fixture.Material,
            new Quantity(8),
            new DemandPriority(1),
            SimulationTime.Zero);

        TransportJobId jobId = Assert.IsType<TransportJobId>(fixture.Board.AssignBest(
            fixture.TransportIds,
            fixture.ReservationIds,
            fixture.Freighter,
            fixture.Inventories,
            fixture.Graph,
            SimulationTime.Zero));

        Assert.Equal(new Quantity(4), fixture.Board.GetJob(jobId)?.Quantity);
        Assert.Equal(jobId, fixture.Freighter.ActiveJobId);
        Assert.Equal<ulong>(6, Assert.IsType<SupplyOffer>(fixture.Board.GetSupply(offerId)).Remaining.Units);
        Assert.Equal<ulong>(4, Assert.IsType<DemandRequest>(fixture.Board.GetDemand(demandId)).Remaining.Units);
        Assert.Equal(
            new Quantity(4),
            fixture.Inventories.Get(fixture.SourceInventoryId)?.Reserved(fixture.Material));
    }

    [Fact]
    public void AssignedJobMovesMaterialThroughScheduledEvents()
    {
        TransportFixture fixture = CreateFixture();
        TransportJobId jobId = PublishAndAssign(fixture);
        TransportTiming timing = CreateTiming();
        var capacityReservationIds = new IdSequence<CapacityReservationId>();
        var agenda = new EventAgenda<TransportEvent>();
        fixture.Board.StartOrRetry(
            jobId,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            SimulationTime.Zero);

        int processed = 0;
        var target = new SimulationTime(10_000);
        while (agenda.PopNextThrough(target) is { } scheduled)
        {
            fixture.Board.HandleEvent(
                scheduled.Payload,
                fixture.Freighter,
                fixture.Inventories,
                capacityReservationIds,
                fixture.Graph,
                agenda,
                timing,
                scheduled.Key.Timestamp);
            processed++;
        }

        Assert.Equal(4, processed);
        Assert.Null(fixture.Freighter.ActiveJobId);
        Assert.Equal(TransportJobStatus.Completed, fixture.Board.GetJob(jobId)?.Status);
        Assert.Equal(
            new Quantity(4),
            fixture.Inventories.Get(fixture.DestinationInventoryId)?.Stored(fixture.Material));
        Assert.Equal(
            Quantity.Zero,
            fixture.Inventories.Get(fixture.Freighter.CargoInventoryId)?.Stored(fixture.Material));
    }

    [Fact]
    public void UnreachableMatchIsExcluded()
    {
        TransportFixture fixture = CreateFixture();
        LocationId disconnected = fixture.LocationIds.Allocate();
        fixture.Graph.AddLocation(disconnected);
        fixture.Board.PublishSupply(
            fixture.TransportIds,
            fixture.SourceInventoryId,
            disconnected,
            fixture.Material,
            new Quantity(5));
        fixture.Board.PublishDemand(
            fixture.TransportIds,
            fixture.DestinationInventoryId,
            fixture.DestinationLocation,
            fixture.Material,
            new Quantity(5),
            new DemandPriority(1),
            SimulationTime.Zero);

        TransportJobId? result = fixture.Board.AssignBest(
            fixture.TransportIds,
            fixture.ReservationIds,
            fixture.Freighter,
            fixture.Inventories,
            fixture.Graph,
            SimulationTime.Zero);

        Assert.Null(result);
    }

    [Fact]
    public void HigherPriorityDemandWinsBeforeDistance()
    {
        TransportFixture fixture = CreateFixture();
        InventoryId secondDestination = fixture.InventoryIds.Allocate();
        fixture.Inventories.Add(new Inventory(secondDestination, new Quantity(20)));
        fixture.Board.PublishSupply(
            fixture.TransportIds,
            fixture.SourceInventoryId,
            fixture.SourceLocation,
            fixture.Material,
            new Quantity(10));
        fixture.Board.PublishDemand(
            fixture.TransportIds,
            fixture.DestinationInventoryId,
            fixture.DestinationLocation,
            fixture.Material,
            new Quantity(4),
            new DemandPriority(1),
            SimulationTime.Zero);
        DemandRequestId highPriority = fixture.Board.PublishDemand(
            fixture.TransportIds,
            secondDestination,
            fixture.DestinationLocation,
            fixture.Material,
            new Quantity(4),
            new DemandPriority(2),
            SimulationTime.Zero);

        TransportJobId jobId = Assert.IsType<TransportJobId>(fixture.Board.AssignBest(
            fixture.TransportIds,
            fixture.ReservationIds,
            fixture.Freighter,
            fixture.Inventories,
            fixture.Graph,
            SimulationTime.Zero));

        Assert.Equal(highPriority, fixture.Board.GetJob(jobId)?.DemandRequestId);
    }

    [Fact]
    public void DisabledRouteDoesNotInterruptAnActiveLeg()
    {
        TransportFixture fixture = CreateFixture();
        TransportJobId jobId = PublishAndAssign(fixture);
        var capacityReservationIds = new IdSequence<CapacityReservationId>();
        var agenda = new EventAgenda<TransportEvent>();
        TransportTiming timing = CreateTiming();
        fixture.Board.StartOrRetry(
            jobId,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            SimulationTime.Zero);
        ScheduledEvent<TransportEvent> arrival = Assert.IsType<ScheduledEvent<TransportEvent>>(
            agenda.PopNextThrough(new SimulationTime(10)));
        RouteId routeId = Assert.IsType<TransportEvent.Arrive>(arrival.Payload).RouteId;
        fixture.Graph.SetRouteEnabled(routeId, false);

        bool applied = fixture.Board.HandleEvent(
            arrival.Payload,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            arrival.Key.Timestamp);

        Assert.True(applied);
        Assert.Equal(fixture.SourceLocation, fixture.Freighter.LocationId);
        Assert.Equal(TransportJobStatus.Loading, fixture.Board.GetJob(jobId)?.Status);
    }

    [Fact]
    public void UnloadingWaitsForThenReservesDestinationCapacity()
    {
        TransportFixture fixture = CreateFixture();
        TransportJobId jobId = PublishAndAssign(fixture);
        Inventory destination = Assert.IsType<Inventory>(
            fixture.Inventories.Get(fixture.DestinationInventoryId));
        destination.Add(fixture.Material, new Quantity(20));
        var capacityReservationIds = new IdSequence<CapacityReservationId>();
        var agenda = new EventAgenda<TransportEvent>();
        TransportTiming timing = CreateTiming();
        fixture.Board.StartOrRetry(
            jobId,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            SimulationTime.Zero);

        for (int processed = 0; processed < 3; processed++)
        {
            ScheduledEvent<TransportEvent> scheduled = Assert.IsType<ScheduledEvent<TransportEvent>>(
                agenda.PopNextThrough(new SimulationTime(10_000)));
            fixture.Board.HandleEvent(
                scheduled.Payload,
                fixture.Freighter,
                fixture.Inventories,
                capacityReservationIds,
                fixture.Graph,
                agenda,
                timing,
                scheduled.Key.Timestamp);
        }

        Assert.Equal(
            TransportJobStatus.WaitingForDestinationCapacity,
            fixture.Board.GetJob(jobId)?.Status);
        destination.RemoveAvailable(fixture.Material, new Quantity(20));
        Assert.True(fixture.Board.StartOrRetry(
            jobId,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            new SimulationTime(2_130)));
        Assert.Equal(new Quantity(4), destination.ReservedCapacity);

        ScheduledEvent<TransportEvent> unloading = Assert.IsType<ScheduledEvent<TransportEvent>>(
            agenda.PopNextThrough(new SimulationTime(10_000)));
        fixture.Board.HandleEvent(
            unloading.Payload,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            unloading.Key.Timestamp);

        Assert.Equal(TransportJobStatus.Completed, fixture.Board.GetJob(jobId)?.Status);
        Assert.Equal(new Quantity(4), destination.Stored(fixture.Material));
    }

    [Fact]
    public void FailureBeforeLoadingRestoresBoardCommitments()
    {
        TransportFixture fixture = CreateFixture();
        SupplyOfferId offerId = fixture.Board.PublishSupply(
            fixture.TransportIds,
            fixture.SourceInventoryId,
            fixture.SourceLocation,
            fixture.Material,
            new Quantity(10));
        DemandRequestId demandId = fixture.Board.PublishDemand(
            fixture.TransportIds,
            fixture.DestinationInventoryId,
            fixture.DestinationLocation,
            fixture.Material,
            new Quantity(4),
            new DemandPriority(1),
            SimulationTime.Zero);
        TransportJobId jobId = Assert.IsType<TransportJobId>(fixture.Board.AssignBest(
            fixture.TransportIds,
            fixture.ReservationIds,
            fixture.Freighter,
            fixture.Inventories,
            fixture.Graph,
            SimulationTime.Zero));
        var capacityReservationIds = new IdSequence<CapacityReservationId>();
        var agenda = new EventAgenda<TransportEvent>();
        TransportTiming timing = CreateTiming();
        fixture.Board.StartOrRetry(
            jobId,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            SimulationTime.Zero);
        ScheduledEvent<TransportEvent> arrival = Assert.IsType<ScheduledEvent<TransportEvent>>(
            agenda.PopNextThrough(new SimulationTime(10)));
        fixture.Board.HandleEvent(
            arrival.Payload,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            arrival.Key.Timestamp);
        TransportJob job = Assert.IsType<TransportJob>(fixture.Board.GetJob(jobId));
        fixture.Inventories.Get(fixture.SourceInventoryId)?.Release(job.SourceReservationId);

        ScheduledEvent<TransportEvent> loading = Assert.IsType<ScheduledEvent<TransportEvent>>(
            agenda.PopNextThrough(new SimulationTime(10_000)));
        fixture.Board.HandleEvent(
            loading.Payload,
            fixture.Freighter,
            fixture.Inventories,
            capacityReservationIds,
            fixture.Graph,
            agenda,
            timing,
            loading.Key.Timestamp);

        Assert.Equal(TransportJobStatus.FailedBeforeLoading, job.Status);
        Assert.Null(fixture.Freighter.ActiveJobId);
        Assert.Equal<ulong>(10, Assert.IsType<SupplyOffer>(fixture.Board.GetSupply(offerId)).Remaining.Units);
        Assert.Equal<ulong>(4, Assert.IsType<DemandRequest>(fixture.Board.GetDemand(demandId)).Remaining.Units);
    }

    private static TransportJobId PublishAndAssign(TransportFixture fixture)
    {
        fixture.Board.PublishSupply(
            fixture.TransportIds,
            fixture.SourceInventoryId,
            fixture.SourceLocation,
            fixture.Material,
            new Quantity(10));
        fixture.Board.PublishDemand(
            fixture.TransportIds,
            fixture.DestinationInventoryId,
            fixture.DestinationLocation,
            fixture.Material,
            new Quantity(4),
            new DemandPriority(1),
            SimulationTime.Zero);
        return Assert.IsType<TransportJobId>(fixture.Board.AssignBest(
            fixture.TransportIds,
            fixture.ReservationIds,
            fixture.Freighter,
            fixture.Inventories,
            fixture.Graph,
            SimulationTime.Zero));
    }

    private static TransportTiming CreateTiming() =>
        new(new SimulationDuration(100), new TransferRate(2), new TransferRate(2));

    private static TransportFixture CreateFixture()
    {
        var locationIds = new IdSequence<LocationId>();
        LocationId shipLocation = locationIds.Allocate();
        LocationId source = locationIds.Allocate();
        LocationId destination = locationIds.Allocate();
        var graph = new RouteGraph();
        graph.AddLocation(shipLocation);
        graph.AddLocation(source);
        graph.AddLocation(destination);
        graph.AddRoute(shipLocation, source, new SimulationDuration(10));
        graph.AddRoute(source, destination, new SimulationDuration(20));

        var inventoryIds = new IdSequence<InventoryId>();
        InventoryId sourceInventoryId = inventoryIds.Allocate();
        InventoryId destinationInventoryId = inventoryIds.Allocate();
        InventoryId cargoInventoryId = inventoryIds.Allocate();
        MaterialId material = new IdSequence<MaterialId>().Allocate();
        var sourceInventory = new Inventory(sourceInventoryId, new Quantity(20));
        sourceInventory.Add(material, new Quantity(10));
        var inventories = new InventoryRegistry();
        inventories.Add(sourceInventory);
        inventories.Add(new Inventory(destinationInventoryId, new Quantity(20)));
        inventories.Add(new Inventory(cargoInventoryId, new Quantity(4)));
        ShipId shipId = new IdSequence<ShipId>().Allocate();

        return new TransportFixture(
            new TransportBoard(),
            new TransportIdSequences(),
            new IdSequence<ReservationId>(),
            inventories,
            graph,
            new Freighter(shipId, shipLocation, cargoInventoryId),
            inventoryIds,
            locationIds,
            sourceInventoryId,
            destinationInventoryId,
            source,
            destination,
            material);
    }

    private sealed record TransportFixture(
        TransportBoard Board,
        TransportIdSequences TransportIds,
        IdSequence<ReservationId> ReservationIds,
        InventoryRegistry Inventories,
        RouteGraph Graph,
        Freighter Freighter,
        IdSequence<InventoryId> InventoryIds,
        IdSequence<LocationId> LocationIds,
        InventoryId SourceInventoryId,
        InventoryId DestinationInventoryId,
        LocationId SourceLocation,
        LocationId DestinationLocation,
        MaterialId Material);
}
