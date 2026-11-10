using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class TransportRemovalPreparationTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("missing-id")]
    [InlineData("owner")]
    [InlineData("quantity")]
    [InlineData("inventory")]
    public void RemovalPreparationRejectsUnprovenDestinationCapacityRelease(string mismatch)
    {
        Fixture fixture = CreateFixture();
        AdvanceToUnloading(fixture);
        CapacityReservationId reservationId = fixture.Job.DestinationCapacityReservationId!.Value;
        fixture.Destination.ReleaseCapacity(reservationId);
        if (mismatch == "missing-id") fixture.Job.DestinationCapacityReservationId = null;
        if (mismatch is "owner" or "quantity")
        {
            fixture.Destination.ReserveCapacity(reservationId,
                mismatch == "quantity" ? new(1) : fixture.Job.Quantity,
                new ReservationOwner.TransportJob(mismatch == "owner" ? new(99) : fixture.Job.Id));
        }
        if (mismatch == "inventory") fixture.Inventories.ApplyRemove(fixture.Destination.Id);
        InventoryCheckpoint before = fixture.Destination.CaptureCheckpoint();

        Assert.False(fixture.Board.CanCancelForEntityRemoval(fixture.Job, fixture.Freighter, fixture.Inventories));

        Assert.Equal(before.CapacityReservations, fixture.Destination.CaptureCheckpoint().CapacityReservations);
        Assert.Equal(fixture.Job.Id, fixture.Freighter.ActiveJobId);
        Assert.Equal(TransportJobStatus.Unloading, fixture.Job.Status);
    }

    [Fact]
    public void RemovalPreparationRejectsGenerationExhaustionWithoutReleasingReservations()
    {
        Fixture fixture = CreateFixture();
        fixture.Job.Generation = new(ulong.MaxValue);
        Reservation reservation = fixture.Source.GetReservation(fixture.Job.SourceReservationId)!;

        Assert.False(fixture.Board.CanCancelForEntityRemoval(fixture.Job, fixture.Freighter, fixture.Inventories));

        Assert.Same(reservation, fixture.Source.GetReservation(reservation.Id));
        Assert.Equal(new EventGeneration(ulong.MaxValue), fixture.Job.Generation);
        Assert.Equal(TransportJobStatus.Assigned, fixture.Job.Status);
    }

    [Theory]
    [InlineData("demand")]
    [InlineData("supply")]
    public void RemovalPreparationRejectsRestoredQuantityOverflowWithoutChangingCommitments(string owner)
    {
        Fixture fixture = CreateFixture();
        SupplyOffer supply = fixture.Board.GetSupply(fixture.Job.SupplyOfferId)!;
        DemandRequest demand = fixture.Board.GetDemand(fixture.Job.DemandRequestId)!;
        if (owner == "demand") demand.Remaining = new(ulong.MaxValue);
        else supply.Remaining = new(ulong.MaxValue);
        Quantity beforeDemand = demand.Remaining;
        Quantity beforeSupply = supply.Remaining;
        Reservation reservation = fixture.Source.GetReservation(fixture.Job.SourceReservationId)!;

        Assert.False(fixture.Board.CanCancelForEntityRemoval(fixture.Job, fixture.Freighter, fixture.Inventories));

        Assert.Equal(beforeDemand, demand.Remaining);
        Assert.Equal(beforeSupply, supply.Remaining);
        Assert.Same(reservation, fixture.Source.GetReservation(reservation.Id));
        Assert.Equal(fixture.Job.Id, fixture.Freighter.ActiveJobId);
    }

    [Fact]
    public void LoadedRemovalPreparationAcceptsConsumedSourceReservationAndPreservesCapacityUntilApply()
    {
        Fixture fixture = CreateFixture();
        AdvanceToUnloading(fixture);
        Assert.Null(fixture.Source.GetReservation(fixture.Job.SourceReservationId));
        CapacityReservation reservation = fixture.Destination.GetCapacityReservation(
            fixture.Job.DestinationCapacityReservationId!.Value)!;

        Assert.True(fixture.Board.CanCancelForEntityRemoval(fixture.Job, fixture.Freighter, fixture.Inventories));

        Assert.Same(reservation, fixture.Destination.GetCapacityReservation(reservation.Id));
        Assert.Equal(TransportJobStatus.Unloading, fixture.Job.Status);
        Assert.True(fixture.Board.CancelOrInterrupt(fixture.Job.Id, fixture.Freighter, fixture.Inventories));
        Assert.Null(fixture.Destination.GetCapacityReservation(reservation.Id));
        Assert.Equal(TransportJobStatus.Cancelled, fixture.Job.Status);
    }

    [Theory]
    [InlineData("reservation")]
    [InlineData("demand")]
    [InlineData("supply")]
    [InlineData("generation")]
    [InlineData("assignment")]
    public void PreparedCancellationRejectsChangedParticipantsBeforeRelease(string change)
    {
        Fixture fixture = CreateFixture();
        Assert.True(fixture.Board.TryPrepareCancellationForEntityRemoval(fixture.Job, fixture.Freighter,
            fixture.Inventories, out var prepared));
        Assert.True(fixture.Board.CanCommitRemovalCancellation(prepared!, fixture.Inventories));
        Assert.False(new TransportBoard().CanCommitRemovalCancellation(prepared!, fixture.Inventories));
        switch (change)
        {
            case "reservation":
                Reservation old = fixture.Source.Release(fixture.Job.SourceReservationId);
                fixture.Source.Reserve(old.Id, old.MaterialId, old.Quantity, old.Owner);
                break;
            case "demand": fixture.Board.GetDemand(fixture.Job.DemandRequestId)!.Remaining = new(1); break;
            case "supply": fixture.Board.GetSupply(fixture.Job.SupplyOfferId)!.Remaining = new(1); break;
            case "generation": fixture.Job.Generation = fixture.Job.Generation.Next(); break;
            case "assignment": fixture.Freighter.ActiveJobId = null; break;
        }
        Reservation current = fixture.Source.GetReservation(fixture.Job.SourceReservationId)!;
        Assert.False(fixture.Board.CanCommitRemovalCancellation(prepared!, fixture.Inventories));
        Assert.Same(current, fixture.Source.GetReservation(current.Id));
        Assert.Equal(TransportJobStatus.Assigned, fixture.Job.Status);
    }

    /// <summary>Runs real loading and travel commits so cancellation sees consumed source cargo and live unloading capacity.</summary>
    private static void AdvanceToUnloading(Fixture fixture)
    {
        var agenda = new EventAgenda<TransportEvent>();
        var capacityIds = new IdSequence<CapacityReservationId>();
        var timing = new TransportTiming(new(100), new(2), new(2));
        fixture.Board.StartOrRetry(fixture.Job.Id, fixture.Freighter, fixture.Inventories,
            capacityIds, fixture.Graph, agenda, timing, SimulationTime.Zero);
        while (fixture.Job.Status != TransportJobStatus.Unloading)
        {
            ScheduledEvent<TransportEvent> scheduled = agenda.PopNextThrough(new(10_000))!;
            fixture.Board.HandleEvent(scheduled.Payload, fixture.Freighter, fixture.Inventories,
                capacityIds, fixture.Graph, agenda, timing, scheduled.Key.Timestamp);
        }
    }

    private static Fixture CreateFixture()
    {
        var graph = new RouteGraph();
        LocationId initial = new(1), sourceLocation = new(2), destinationLocation = new(3);
        graph.AddLocation(initial);
        graph.AddLocation(sourceLocation);
        graph.AddLocation(destinationLocation);
        graph.AddRoute(initial, sourceLocation, new(10));
        graph.AddRoute(sourceLocation, destinationLocation, new(20));
        var source = new Inventory(new(1), new(20));
        var destination = new Inventory(new(2), new(20));
        var cargo = new Inventory(new(3), new(4));
        MaterialId material = new(1);
        source.Add(material, new(10));
        var inventories = new InventoryRegistry();
        inventories.Add(source);
        inventories.Add(destination);
        inventories.Add(cargo);
        var board = new TransportBoard();
        var ids = new TransportIdSequences();
        var freighter = new Freighter(new(1), initial, cargo.Id);
        board.PublishSupply(ids, source.Id, sourceLocation, material, new(10));
        board.PublishDemand(ids, destination.Id, destinationLocation, material, new(4), new(1), SimulationTime.Zero);
        TransportJobId jobId = board.AssignBest(ids, new(), freighter, inventories, graph, SimulationTime.Zero)!.Value;
        return new(board, board.GetJob(jobId)!, inventories, graph, freighter, source, destination);
    }

    private sealed record Fixture(TransportBoard Board, TransportJob Job, InventoryRegistry Inventories,
        RouteGraph Graph, Freighter Freighter, Inventory Source, Inventory Destination);
}
