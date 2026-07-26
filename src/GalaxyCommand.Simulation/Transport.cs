namespace GalaxyCommand.Simulation;

public readonly record struct TransferRate
{
    public TransferRate(ulong unitsPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfZero(unitsPerSecond);
        UnitsPerSecond = unitsPerSecond;
    }

    public ulong UnitsPerSecond { get; }

    internal SimulationDuration DurationFor(Quantity quantity)
    {
        UInt128 numerator = (UInt128)quantity.Units * 1_000;
        UInt128 divisor = UnitsPerSecond;
        UInt128 milliseconds = (numerator + divisor - 1) / divisor;
        if (milliseconds > ulong.MaxValue)
        {
            throw new OverflowException("Material transfer duration exceeds the simulation timeline.");
        }

        return new SimulationDuration((ulong)milliseconds);
    }
}

public readonly record struct TransportTiming(
    SimulationDuration DockingOverhead,
    TransferRate LoadingRate,
    TransferRate UnloadingRate)
{
    internal SimulationDuration LoadingDuration(Quantity quantity) =>
        DockingOverhead.Add(LoadingRate.DurationFor(quantity));

    internal SimulationDuration UnloadingDuration(Quantity quantity) =>
        DockingOverhead.Add(UnloadingRate.DurationFor(quantity));
}

public readonly record struct DemandPriority(uint Value) : IComparable<DemandPriority>
{
    public int CompareTo(DemandPriority other) => Value.CompareTo(other.Value);

    public static bool operator <(DemandPriority left, DemandPriority right) => left.Value < right.Value;
    public static bool operator <=(DemandPriority left, DemandPriority right) => left.Value <= right.Value;
    public static bool operator >(DemandPriority left, DemandPriority right) => left.Value > right.Value;
    public static bool operator >=(DemandPriority left, DemandPriority right) => left.Value >= right.Value;
}

public sealed class SupplyOffer
{
    internal SupplyOffer(
        SupplyOfferId id,
        InventoryId inventoryId,
        LocationId locationId,
        MaterialId materialId,
        Quantity remaining)
    {
        Id = id;
        InventoryId = inventoryId;
        LocationId = locationId;
        MaterialId = materialId;
        Remaining = remaining;
    }

    public SupplyOfferId Id { get; }
    public InventoryId InventoryId { get; }
    public LocationId LocationId { get; }
    public MaterialId MaterialId { get; }
    public Quantity Remaining { get; internal set; }
}

public sealed class DemandRequest
{
    internal DemandRequest(
        DemandRequestId id,
        InventoryId inventoryId,
        LocationId locationId,
        MaterialId materialId,
        Quantity remaining,
        DemandPriority priority,
        SimulationTime createdAt)
    {
        Id = id;
        InventoryId = inventoryId;
        LocationId = locationId;
        MaterialId = materialId;
        Remaining = remaining;
        Priority = priority;
        CreatedAt = createdAt;
    }

    public DemandRequestId Id { get; }
    public InventoryId InventoryId { get; }
    public LocationId LocationId { get; }
    public MaterialId MaterialId { get; }
    public Quantity Remaining { get; internal set; }
    public DemandPriority Priority { get; }
    public SimulationTime CreatedAt { get; }
}

public sealed class Freighter
{
    public Freighter(ShipId shipId, LocationId locationId, InventoryId cargoInventoryId)
    {
        ShipId = shipId;
        LocationId = locationId;
        CargoInventoryId = cargoInventoryId;
    }

    public ShipId ShipId { get; }
    public LocationId LocationId { get; internal set; }
    public InventoryId CargoInventoryId { get; }
    public TransportJobId? ActiveJobId { get; internal set; }
}

public enum TransportJobStatus
{
    Assigned,
    WaitingForRouteToSource,
    TravelingToSource,
    Loading,
    WaitingForRouteToDestination,
    TravelingToDestination,
    WaitingForDestinationCapacity,
    Unloading,
    Completed,
    FailedBeforeLoading,
    Cancelled,
}

public enum TravelTarget
{
    Source,
    Destination,
}

public abstract record TransportEvent(TransportJobId JobId, EventGeneration Generation)
{
    public sealed record Arrive(
        TransportJobId JobId,
        EventGeneration Generation,
        RouteId RouteId,
        TravelTarget Target) : TransportEvent(JobId, Generation);

    public sealed record FinishLoading(
        TransportJobId JobId,
        EventGeneration Generation) : TransportEvent(JobId, Generation);

    public sealed record FinishUnloading(
        TransportJobId JobId,
        EventGeneration Generation) : TransportEvent(JobId, Generation);
}

public sealed class TransportJob
{
    internal TransportJob(
        TransportJobId id,
        ShipId shipId,
        SupplyOfferId supplyOfferId,
        DemandRequestId demandRequestId,
        InventoryId sourceInventoryId,
        LocationId sourceLocationId,
        InventoryId destinationInventoryId,
        LocationId destinationLocationId,
        MaterialId materialId,
        Quantity quantity,
        ReservationId sourceReservationId,
        SimulationTime assignedAt)
    {
        Id = id;
        ShipId = shipId;
        SupplyOfferId = supplyOfferId;
        DemandRequestId = demandRequestId;
        SourceInventoryId = sourceInventoryId;
        SourceLocationId = sourceLocationId;
        DestinationInventoryId = destinationInventoryId;
        DestinationLocationId = destinationLocationId;
        MaterialId = materialId;
        Quantity = quantity;
        SourceReservationId = sourceReservationId;
        AssignedAt = assignedAt;
    }

    public TransportJobId Id { get; }
    public ShipId ShipId { get; }
    public SupplyOfferId SupplyOfferId { get; }
    public DemandRequestId DemandRequestId { get; }
    public InventoryId SourceInventoryId { get; }
    public LocationId SourceLocationId { get; }
    public InventoryId DestinationInventoryId { get; }
    public LocationId DestinationLocationId { get; }
    public MaterialId MaterialId { get; }
    public Quantity Quantity { get; }
    public ReservationId SourceReservationId { get; }
    public CapacityReservationId? DestinationCapacityReservationId { get; internal set; }
    public SimulationTime AssignedAt { get; }
    public EventGeneration Generation { get; internal set; } = new(0);
    public TransportJobStatus Status { get; internal set; } = TransportJobStatus.Assigned;
    public RouteId? CurrentRouteId { get; internal set; }
    public SimulationTime? TransitionAt { get; internal set; }
}

public sealed class TransportIdSequences
{
    private readonly IdSequence<SupplyOfferId> _offers = new();
    private readonly IdSequence<DemandRequestId> _demands = new();
    private readonly IdSequence<TransportJobId> _jobs = new();

    internal SupplyOfferId AllocateOffer() => _offers.Allocate();
    internal DemandRequestId AllocateDemand() => _demands.Allocate();
    internal TransportJobId AllocateJob() => _jobs.Allocate();
}

/// <summary>
/// Central Phase 1 exchange for supply, demand, and assigned transport jobs.
/// </summary>
public sealed class TransportBoard
{
    private readonly SortedDictionary<SupplyOfferId, SupplyOffer> _supplies =
        new(EntityIdComparer<SupplyOfferId>.Instance);
    private readonly SortedDictionary<DemandRequestId, DemandRequest> _demands =
        new(EntityIdComparer<DemandRequestId>.Instance);
    private readonly SortedDictionary<TransportJobId, TransportJob> _jobs =
        new(EntityIdComparer<TransportJobId>.Instance);

    public SupplyOfferId PublishSupply(
        TransportIdSequences ids,
        InventoryId inventoryId,
        LocationId locationId,
        MaterialId materialId,
        Quantity quantity)
    {
        RequirePositive(quantity);
        SupplyOfferId id = ids.AllocateOffer();
        _supplies.Add(id, new SupplyOffer(id, inventoryId, locationId, materialId, quantity));
        return id;
    }

    public DemandRequestId PublishDemand(
        TransportIdSequences ids,
        InventoryId inventoryId,
        LocationId locationId,
        MaterialId materialId,
        Quantity quantity,
        DemandPriority priority,
        SimulationTime createdAt)
    {
        RequirePositive(quantity);
        DemandRequestId id = ids.AllocateDemand();
        _demands.Add(
            id,
            new DemandRequest(id, inventoryId, locationId, materialId, quantity, priority, createdAt));
        return id;
    }

    public SupplyOffer? GetSupply(SupplyOfferId offerId) => _supplies.GetValueOrDefault(offerId);
    public DemandRequest? GetDemand(DemandRequestId demandId) => _demands.GetValueOrDefault(demandId);
    public TransportJob? GetJob(TransportJobId jobId) => _jobs.GetValueOrDefault(jobId);
    public IEnumerable<TransportJob> Jobs => _jobs.Values;

    public Quantity OfferedQuantity(InventoryId inventoryId, MaterialId materialId) =>
        SumQuantities(_supplies.Values
            .Where(offer => offer.InventoryId == inventoryId && offer.MaterialId == materialId)
            .Select(offer => offer.Remaining));

    public Quantity PendingDeliveryQuantity(InventoryId inventoryId, MaterialId materialId)
    {
        Quantity requested = SumQuantities(_demands.Values
            .Where(demand => demand.InventoryId == inventoryId && demand.MaterialId == materialId)
            .Select(demand => demand.Remaining));
        return SumQuantities([requested, CommittedDeliveryQuantity(inventoryId, materialId)]);
    }

    public Quantity CommittedDeliveryQuantity(InventoryId inventoryId, MaterialId materialId) =>
        SumQuantities(_jobs.Values
            .Where(job => job.DestinationInventoryId == inventoryId
                && job.MaterialId == materialId
                && job.Status is not TransportJobStatus.Completed
                and not TransportJobStatus.FailedBeforeLoading)
            .Select(job => job.Quantity));

    public TransportJobId? AssignBest(
        TransportIdSequences ids,
        IdSequence<ReservationId> reservationIds,
        Freighter freighter,
        InventoryRegistry inventories,
        INavigation navigation,
        SimulationTime now)
    {
        if (freighter.ActiveJobId is { } activeJobId)
        {
            throw new InvalidOperationException(
                $"Freighter {freighter.ShipId} is already assigned to job {activeJobId}.");
        }

        Inventory cargo = inventories.Get(freighter.CargoInventoryId)
            ?? throw new KeyNotFoundException($"Unknown inventory {freighter.CargoInventoryId}.");
        Quantity cargoCapacity = cargo.RemainingCapacity;
        if (cargoCapacity == Quantity.Zero)
        {
            return null;
        }

        Candidate? candidate = BestCandidate(freighter, inventories, navigation, cargoCapacity);
        return candidate is null
            ? null
            : CommitAssignment(candidate, ids, reservationIds, freighter, inventories, now);
    }

    public bool StartOrRetry(
        TransportJobId jobId,
        Freighter freighter,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        INavigation navigation,
        EventAgenda<TransportEvent> agenda,
        TransportTiming timing,
        SimulationTime now) =>
        StartOrRetry(
            jobId,
            freighter,
            inventories,
            capacityReservationIds,
            navigation,
            agenda,
            static transportEvent => transportEvent,
            timing,
            now);

    public bool StartOrRetry<TEvent>(
        TransportJobId jobId,
        Freighter freighter,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        INavigation navigation,
        EventAgenda<TEvent> agenda,
        Func<TransportEvent, TEvent> wrapEvent,
        TransportTiming timing,
        SimulationTime now)
    {
        TransportJob job = RequireJobForFreighter(jobId, freighter);
        return job.Status switch
        {
            TransportJobStatus.Assigned or TransportJobStatus.WaitingForRouteToSource =>
                AdvanceToward(jobId, TravelTarget.Source, freighter, inventories,
                    capacityReservationIds, navigation, agenda, wrapEvent, timing, now),
            TransportJobStatus.WaitingForRouteToDestination =>
                AdvanceToward(jobId, TravelTarget.Destination, freighter, inventories,
                    capacityReservationIds, navigation, agenda, wrapEvent, timing, now),
            TransportJobStatus.WaitingForDestinationCapacity =>
                BeginUnloading(jobId, inventories, capacityReservationIds, agenda, wrapEvent, timing, now),
            _ => false,
        };
    }

    public ScheduledEventDisposition HandleEvent(
        TransportEvent transportEvent,
        Freighter? freighter,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        INavigation navigation,
        EventAgenda<TransportEvent> agenda,
        TransportTiming timing,
        SimulationTime now) =>
        HandleEvent(
            transportEvent,
            freighter,
            inventories,
            capacityReservationIds,
            navigation,
            agenda,
            static eventToWrap => eventToWrap,
            timing,
            now);

    public ScheduledEventDisposition HandleEvent<TEvent>(
        TransportEvent transportEvent,
        Freighter? freighter,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        INavigation navigation,
        EventAgenda<TEvent> agenda,
        Func<TransportEvent, TEvent> wrapEvent,
        TransportTiming timing,
        SimulationTime now)
    {
        if (GetJob(transportEvent.JobId) is not { } job)
        {
            return ScheduledEventDisposition.IgnoredMissingReference;
        }

        if (job.Generation != transportEvent.Generation)
        {
            return ScheduledEventDisposition.IgnoredStaleGeneration;
        }

        if (freighter is null)
        {
            return ScheduledEventDisposition.IgnoredMissingReference;
        }

        if (job.ShipId != freighter.ShipId || freighter.ActiveJobId != job.Id)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        switch (transportEvent)
        {
            case TransportEvent.Arrive arrive:
                if (!TravelStateMatches(job, arrive.RouteId, arrive.Target, now))
                {
                    return ScheduledEventDisposition.IgnoredStateMismatch;
                }

                DirectedRoute route = navigation.GetRoute(arrive.RouteId)
                    ?? throw new KeyNotFoundException($"Unknown route {arrive.RouteId}.");
                freighter.LocationId = route.Destination;
                AdvanceToward(job.Id, arrive.Target, freighter, inventories,
                    capacityReservationIds, navigation, agenda, wrapEvent, timing, now);
                return ScheduledEventDisposition.Applied;

            case TransportEvent.FinishLoading:
                if (job.Status != TransportJobStatus.Loading || job.TransitionAt != now)
                {
                    return ScheduledEventDisposition.IgnoredStateMismatch;
                }

                var owner = new ReservationOwner.TransportJob(job.Id);
                try
                {
                    inventories.TransferReserved(
                        job.SourceInventoryId,
                        freighter.CargoInventoryId,
                        job.SourceReservationId,
                        owner);
                }
                catch (Exception error) when (error is InvalidOperationException
                    or KeyNotFoundException or OverflowException)
                {
                    FailBeforeLoading(job, freighter, inventories);
                    return ScheduledEventDisposition.Applied;
                }

                AdvanceToward(job.Id, TravelTarget.Destination, freighter, inventories,
                    capacityReservationIds, navigation, agenda, wrapEvent, timing, now);
                return ScheduledEventDisposition.Applied;

            case TransportEvent.FinishUnloading:
                if (job.Status != TransportJobStatus.Unloading || job.TransitionAt != now)
                {
                    return ScheduledEventDisposition.IgnoredStateMismatch;
                }

                CapacityReservationId capacityReservationId =
                    job.DestinationCapacityReservationId
                    ?? throw new InvalidOperationException(
                        $"Transport job {job.Id} has no destination capacity reservation.");
                inventories.TransferIntoReservedCapacity(
                    freighter.CargoInventoryId,
                    job.DestinationInventoryId,
                    job.MaterialId,
                    job.Quantity,
                    capacityReservationId,
                    new ReservationOwner.TransportJob(job.Id));
                SetJobState(job, TransportJobStatus.Completed);
                freighter.ActiveJobId = null;
                return ScheduledEventDisposition.Applied;

            default:
                throw new ArgumentOutOfRangeException(nameof(transportEvent));
        }
    }

    public bool CancelOrInterrupt(
        TransportJobId jobId,
        Freighter freighter,
        InventoryRegistry inventories)
    {
        TransportJob job = GetRequiredJob(jobId);
        if (job.ShipId != freighter.ShipId)
        {
            throw new InvalidOperationException(
                $"Transport job {jobId} belongs to ship {job.ShipId}, not {freighter.ShipId}.");
        }

        if (job.Status is TransportJobStatus.Completed
            or TransportJobStatus.FailedBeforeLoading
            or TransportJobStatus.Cancelled)
        {
            return false;
        }

        if (freighter.ActiveJobId != jobId)
        {
            throw new InvalidOperationException(
                $"Freighter {freighter.ShipId} is not assigned to job {jobId}.");
        }

        bool cargoLoaded = job.Status is TransportJobStatus.WaitingForRouteToDestination
            or TransportJobStatus.TravelingToDestination
            or TransportJobStatus.WaitingForDestinationCapacity
            or TransportJobStatus.Unloading;
        EventGeneration nextGeneration = job.Generation.Next();
        Inventory source = inventories.Get(job.SourceInventoryId)
            ?? throw new KeyNotFoundException($"Unknown inventory {job.SourceInventoryId}.");
        Inventory? destination = null;

        DemandRequest demand = _demands[job.DemandRequestId];
        Quantity restoredDemand = demand.Remaining.Add(job.Quantity);
        SupplyOffer? supply = null;
        Quantity? restoredSupply = null;
        if (!cargoLoaded)
        {
            supply = _supplies[job.SupplyOfferId];
            restoredSupply = supply.Remaining.Add(job.Quantity);
        }

        if (job.DestinationCapacityReservationId is not null)
        {
            destination = inventories.Get(job.DestinationInventoryId)
                ?? throw new KeyNotFoundException(
                    $"Unknown inventory {job.DestinationInventoryId}.");
        }

        if (source.GetReservation(job.SourceReservationId) is not null)
        {
            source.Release(job.SourceReservationId);
        }

        if (job.DestinationCapacityReservationId is { } capacityReservationId
            && destination?.GetCapacityReservation(capacityReservationId) is not null)
        {
            destination.ReleaseCapacity(capacityReservationId);
        }

        job.DestinationCapacityReservationId = null;
        demand.Remaining = restoredDemand;
        if (supply is not null && restoredSupply is { } supplyQuantity)
        {
            supply.Remaining = supplyQuantity;
        }

        job.Generation = nextGeneration;
        SetJobState(job, TransportJobStatus.Cancelled);
        freighter.ActiveJobId = null;
        return true;
    }

    private Candidate? BestCandidate(
        Freighter freighter,
        InventoryRegistry inventories,
        INavigation navigation,
        Quantity cargoCapacity)
    {
        Candidate? best = null;
        foreach (DemandRequest demand in _demands.Values.Where(d => d.Remaining > Quantity.Zero))
        {
            foreach (SupplyOffer supply in _supplies.Values.Where(s =>
                s.Remaining > Quantity.Zero && s.MaterialId == demand.MaterialId))
            {
                Inventory source = inventories.Get(supply.InventoryId)
                    ?? throw new KeyNotFoundException($"Unknown inventory {supply.InventoryId}.");
                Quantity quantity = demand.Remaining
                    .Min(supply.Remaining)
                    .Min(source.Available(supply.MaterialId))
                    .Min(cargoCapacity);
                if (quantity == Quantity.Zero)
                {
                    continue;
                }

                RoutePlan? toSource = navigation.FindRoute(freighter.LocationId, supply.LocationId);
                RoutePlan? toDestination = navigation.FindRoute(supply.LocationId, demand.LocationId);
                if (toSource is null || toDestination is null)
                {
                    continue;
                }

                var candidate = new Candidate(
                    supply.Id,
                    demand.Id,
                    demand.Priority,
                    demand.CreatedAt,
                    toSource.TotalDuration.Add(toDestination.TotalDuration),
                    quantity);
                if (best is null || CandidateComparer.Instance.Compare(candidate, best) < 0)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private TransportJobId CommitAssignment(
        Candidate candidate,
        TransportIdSequences ids,
        IdSequence<ReservationId> reservationIds,
        Freighter freighter,
        InventoryRegistry inventories,
        SimulationTime now)
    {
        TransportJobId jobId = ids.AllocateJob();
        ReservationId reservationId = reservationIds.Allocate();
        SupplyOffer supply = _supplies[candidate.OfferId];
        DemandRequest demand = _demands[candidate.DemandId];
        Inventory source = inventories.Get(supply.InventoryId)
            ?? throw new KeyNotFoundException($"Unknown inventory {supply.InventoryId}.");
        source.Reserve(
            reservationId,
            supply.MaterialId,
            candidate.Quantity,
            new ReservationOwner.TransportJob(jobId));

        supply.Remaining = supply.Remaining.Subtract(candidate.Quantity);
        demand.Remaining = demand.Remaining.Subtract(candidate.Quantity);
        var job = new TransportJob(
            jobId,
            freighter.ShipId,
            supply.Id,
            demand.Id,
            supply.InventoryId,
            supply.LocationId,
            demand.InventoryId,
            demand.LocationId,
            supply.MaterialId,
            candidate.Quantity,
            reservationId,
            now);
        _jobs.Add(jobId, job);
        freighter.ActiveJobId = jobId;
        return jobId;
    }

    private bool AdvanceToward<TEvent>(
        TransportJobId jobId,
        TravelTarget target,
        Freighter freighter,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        INavigation navigation,
        EventAgenda<TEvent> agenda,
        Func<TransportEvent, TEvent> wrapEvent,
        TransportTiming timing,
        SimulationTime now)
    {
        TransportJob job = GetRequiredJob(jobId);
        LocationId destination = target == TravelTarget.Source
            ? job.SourceLocationId
            : job.DestinationLocationId;
        RoutePlan? plan = navigation.FindRoute(freighter.LocationId, destination);
        if (plan is null)
        {
            SetJobState(job, target == TravelTarget.Source
                ? TransportJobStatus.WaitingForRouteToSource
                : TransportJobStatus.WaitingForRouteToDestination);
            return false;
        }

        if (plan.RouteIds.Count == 0)
        {
            return target == TravelTarget.Source
                ? BeginLoading(job, agenda, wrapEvent, timing, now)
                : BeginUnloading(jobId, inventories, capacityReservationIds,
                    agenda, wrapEvent, timing, now);
        }

        RouteId routeId = plan.RouteIds[0];
        DirectedRoute route = navigation.GetRoute(routeId)
            ?? throw new KeyNotFoundException($"Unknown route {routeId}.");
        SimulationTime arrivesAt = now.Add(route.BaseDuration);
        agenda.Schedule(
            arrivesAt,
            EventPhase.PhysicalCompletion,
            job.Generation,
            wrapEvent(new TransportEvent.Arrive(job.Id, job.Generation, routeId, target)));
        SetJobState(
            job,
            target == TravelTarget.Source
                ? TransportJobStatus.TravelingToSource
                : TransportJobStatus.TravelingToDestination,
            routeId,
            arrivesAt);
        return true;
    }

    private static bool BeginLoading<TEvent>(
        TransportJob job,
        EventAgenda<TEvent> agenda,
        Func<TransportEvent, TEvent> wrapEvent,
        TransportTiming timing,
        SimulationTime now)
    {
        SimulationTime completesAt = now.Add(timing.LoadingDuration(job.Quantity));
        agenda.Schedule(
            completesAt,
            EventPhase.PhysicalCompletion,
            job.Generation,
            wrapEvent(new TransportEvent.FinishLoading(job.Id, job.Generation)));
        SetJobState(job, TransportJobStatus.Loading, transitionAt: completesAt);
        return true;
    }

    private bool BeginUnloading<TEvent>(
        TransportJobId jobId,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        EventAgenda<TEvent> agenda,
        Func<TransportEvent, TEvent> wrapEvent,
        TransportTiming timing,
        SimulationTime now)
    {
        TransportJob job = GetRequiredJob(jobId);
        Inventory destination = inventories.Get(job.DestinationInventoryId)
            ?? throw new KeyNotFoundException($"Unknown inventory {job.DestinationInventoryId}.");
        if (job.Quantity > destination.RemainingCapacity)
        {
            SetJobState(job, TransportJobStatus.WaitingForDestinationCapacity);
            return false;
        }

        CapacityReservationId reservationId = capacityReservationIds.Allocate();
        destination.ReserveCapacity(
            reservationId,
            job.Quantity,
            new ReservationOwner.TransportJob(job.Id));
        SimulationTime completesAt = now.Add(timing.UnloadingDuration(job.Quantity));
        try
        {
            agenda.Schedule(
                completesAt,
                EventPhase.PhysicalCompletion,
                job.Generation,
                wrapEvent(new TransportEvent.FinishUnloading(job.Id, job.Generation)));
        }
        catch
        {
            destination.ReleaseCapacity(reservationId);
            throw;
        }

        job.DestinationCapacityReservationId = reservationId;
        SetJobState(job, TransportJobStatus.Unloading, transitionAt: completesAt);
        return true;
    }

    private void FailBeforeLoading(
        TransportJob job,
        Freighter freighter,
        InventoryRegistry inventories)
    {
        Inventory source = inventories.Get(job.SourceInventoryId)
            ?? throw new KeyNotFoundException($"Unknown inventory {job.SourceInventoryId}.");
        if (source.GetReservation(job.SourceReservationId) is not null)
        {
            source.Release(job.SourceReservationId);
        }

        SupplyOffer supply = _supplies[job.SupplyOfferId];
        DemandRequest demand = _demands[job.DemandRequestId];
        supply.Remaining = supply.Remaining.Add(job.Quantity);
        demand.Remaining = demand.Remaining.Add(job.Quantity);
        SetJobState(job, TransportJobStatus.FailedBeforeLoading);
        freighter.ActiveJobId = null;
    }

    private TransportJob RequireJobForFreighter(TransportJobId jobId, Freighter freighter)
    {
        TransportJob job = GetRequiredJob(jobId);
        if (job.ShipId != freighter.ShipId || freighter.ActiveJobId != jobId)
        {
            throw new InvalidOperationException(
                $"Transport job {jobId} belongs to ship {job.ShipId}, not {freighter.ShipId}.");
        }

        return job;
    }

    private TransportJob GetRequiredJob(TransportJobId jobId) =>
        GetJob(jobId) ?? throw new KeyNotFoundException($"Unknown transport job {jobId}.");

    private static bool TravelStateMatches(
        TransportJob job,
        RouteId routeId,
        TravelTarget target,
        SimulationTime now) =>
        job.CurrentRouteId == routeId
        && job.TransitionAt == now
        && job.Status == (target == TravelTarget.Source
            ? TransportJobStatus.TravelingToSource
            : TransportJobStatus.TravelingToDestination);

    private static void SetJobState(
        TransportJob job,
        TransportJobStatus status,
        RouteId? routeId = null,
        SimulationTime? transitionAt = null)
    {
        job.Status = status;
        job.CurrentRouteId = routeId;
        job.TransitionAt = transitionAt;
    }

    private static Quantity SumQuantities(IEnumerable<Quantity> quantities)
    {
        ulong total = 0;
        foreach (Quantity quantity in quantities)
        {
            total = ulong.MaxValue - total < quantity.Units
                ? ulong.MaxValue
                : total + quantity.Units;
        }

        return new Quantity(total);
    }

    private static void RequirePositive(Quantity quantity)
    {
        if (quantity == Quantity.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }
    }

    private sealed record Candidate(
        SupplyOfferId OfferId,
        DemandRequestId DemandId,
        DemandPriority Priority,
        SimulationTime DemandCreatedAt,
        SimulationDuration JourneyDuration,
        Quantity Quantity);

    private sealed class CandidateComparer : IComparer<Candidate>
    {
        public static readonly CandidateComparer Instance = new();

        public int Compare(Candidate? left, Candidate? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            int result = right.Priority.CompareTo(left.Priority);
            if (result != 0) return result;
            result = left.DemandCreatedAt.CompareTo(right.DemandCreatedAt);
            if (result != 0) return result;
            result = left.JourneyDuration.CompareTo(right.JourneyDuration);
            if (result != 0) return result;
            result = right.Quantity.CompareTo(left.Quantity);
            if (result != 0) return result;
            result = left.DemandId.Value.CompareTo(right.DemandId.Value);
            return result != 0 ? result : left.OfferId.Value.CompareTo(right.OfferId.Value);
        }
    }
}
