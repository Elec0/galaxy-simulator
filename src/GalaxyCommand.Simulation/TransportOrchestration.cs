using System.Diagnostics;

namespace GalaxyCommand.Simulation;

public sealed record TransportAdvanceRead(
    ShipId ShipId,
    TransportJobId JobId,
    EventGeneration Generation,
    TransportJobStatus Status,
    LocationId CurrentLocationId,
    LocationId SourceLocationId,
    LocationId DestinationLocationId,
    InventoryId DestinationInventoryId,
    Quantity Quantity,
    Quantity DestinationRemainingCapacity);

public sealed class TransportAdvanceBatch
{
    internal TransportAdvanceBatch(IEnumerable<TransportAdvanceRead> transports)
    {
        Transports = Array.AsReadOnly(transports.ToArray());
    }

    public IReadOnlyList<TransportAdvanceRead> Transports { get; }
}

public abstract record TransportAdvanceEffect(
    ShipId ShipId,
    TransportJobId JobId,
    EventGeneration Generation,
    TransportJobStatus ExpectedStatus,
    LocationId ExpectedLocationId,
    TravelTarget Target)
{
    public sealed record WaitForRoute(
        ShipId ShipId,
        TransportJobId JobId,
        EventGeneration Generation,
        TransportJobStatus ExpectedStatus,
        LocationId ExpectedLocationId,
        TravelTarget Target)
        : TransportAdvanceEffect(
            ShipId,
            JobId,
            Generation,
            ExpectedStatus,
            ExpectedLocationId,
            Target);

    public sealed record Travel(
        ShipId ShipId,
        TransportJobId JobId,
        EventGeneration Generation,
        TransportJobStatus ExpectedStatus,
        LocationId ExpectedLocationId,
        TravelTarget Target,
        RouteId RouteId,
        SimulationTime ArrivesAt)
        : TransportAdvanceEffect(
            ShipId,
            JobId,
            Generation,
            ExpectedStatus,
            ExpectedLocationId,
            Target);

    public sealed record BeginLoading(
        ShipId ShipId,
        TransportJobId JobId,
        EventGeneration Generation,
        TransportJobStatus ExpectedStatus,
        LocationId ExpectedLocationId,
        SimulationTime CompletesAt)
        : TransportAdvanceEffect(
            ShipId,
            JobId,
            Generation,
            ExpectedStatus,
            ExpectedLocationId,
            TravelTarget.Source);

    public sealed record WaitForDestinationCapacity(
        ShipId ShipId,
        TransportJobId JobId,
        EventGeneration Generation,
        TransportJobStatus ExpectedStatus,
        LocationId ExpectedLocationId)
        : TransportAdvanceEffect(
            ShipId,
            JobId,
            Generation,
            ExpectedStatus,
            ExpectedLocationId,
            TravelTarget.Destination);

    public sealed record BeginUnloading(
        ShipId ShipId,
        TransportJobId JobId,
        EventGeneration Generation,
        TransportJobStatus ExpectedStatus,
        LocationId ExpectedLocationId,
        SimulationTime CompletesAt)
        : TransportAdvanceEffect(
            ShipId,
            JobId,
            Generation,
            ExpectedStatus,
            ExpectedLocationId,
            TravelTarget.Destination);
}

public sealed class TransportAdvanceEvaluation
{
    internal TransportAdvanceEvaluation(IEnumerable<TransportAdvanceEffect> effects)
    {
        Effects = Array.AsReadOnly(effects.ToArray());
    }

    public IReadOnlyList<TransportAdvanceEffect> Effects { get; }
}

public sealed record TransportEventProposal(
    ShipId ShipId,
    TransportJobId JobId,
    SimulationTime Timestamp,
    EventGeneration Generation,
    TransportEvent Event);

public sealed record TransportEventCoreCommit(
    ScheduledEventDisposition Disposition,
    ShipId? ShipId,
    TransportJobId? JobId,
    TravelTarget? ContinuationTarget);

public sealed record TransportAdvanceCommit(
    ShipId ShipId,
    TransportJobId JobId,
    TransportJobStatus Before,
    TransportJobStatus After,
    TransportEventProposal? EventProposal);

public sealed class TransportAdvanceCommitResult
{
    internal TransportAdvanceCommitResult(
        IEnumerable<TransportAdvanceCommit> commits,
        int rejectedEffectCount)
    {
        Commits = Array.AsReadOnly(commits.ToArray());
        RejectedEffectCount = rejectedEffectCount;
    }

    public IReadOnlyList<TransportAdvanceCommit> Commits { get; }

    public IReadOnlyList<TransportEventProposal> EventProposals =>
        Array.AsReadOnly(
            Commits
                .Where(commit => commit.EventProposal is not null)
                .Select(commit => commit.EventProposal!)
                .ToArray());

    public int RejectedEffectCount { get; }
}

public sealed class TransportAdvanceReconciliationResult
{
    internal TransportAdvanceReconciliationResult(
        TransportAdvanceCommitResult commit,
        IEnumerable<RuntimeMeasurement> measurements)
    {
        Commit = commit;
        Measurements = Array.AsReadOnly(measurements.ToArray());
    }

    public TransportAdvanceCommitResult Commit { get; }

    public IReadOnlyList<RuntimeMeasurement> Measurements { get; }
}

public sealed record TransportEventReconciliationResult(
    ScheduledEventDisposition Disposition,
    TransportAdvanceCommitResult Continuation);

/// <summary>
/// Stable evaluation and deterministic commit for assigned transport retries.
/// </summary>
public sealed class TransportSystem
{
    private const string DomainName = "transport-advance";
    private readonly IComparer<ShipId> _shipComparer =
        EntityIdComparer<ShipId>.Instance;
    private readonly IComparer<TransportJobId> _jobComparer =
        EntityIdComparer<TransportJobId>.Instance;

    public TransportAdvanceBatch CreateBatch(
        TransportBoard board,
        ShipRegistry ships,
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(inventories);

        var reads = new List<TransportAdvanceRead>();
        foreach (ShipId shipId in ships.FreighterIds.OrderBy(
            shipId => shipId,
            _shipComparer))
        {
            Freighter freighter = ships.GetFreighter(shipId)
                ?? throw new KeyNotFoundException($"Missing freighter {shipId}.");
            if (freighter.ActiveJobId is not { } jobId)
            {
                continue;
            }

            TransportJob job = board.GetJob(jobId)
                ?? throw new KeyNotFoundException($"Unknown transport job {jobId}.");
            if (job.Status is not (
                TransportJobStatus.Assigned
                or TransportJobStatus.WaitingForRouteToSource
                or TransportJobStatus.WaitingForRouteToDestination
                or TransportJobStatus.WaitingForDestinationCapacity))
            {
                continue;
            }

            Inventory destination = inventories.Get(job.DestinationInventoryId)
                ?? throw new KeyNotFoundException(
                    $"Unknown inventory {job.DestinationInventoryId}.");
            reads.Add(new TransportAdvanceRead(
                shipId,
                job.Id,
                job.Generation,
                job.Status,
                freighter.LocationId,
                job.SourceLocationId,
                job.DestinationLocationId,
                job.DestinationInventoryId,
                job.Quantity,
                destination.RemainingCapacity));
        }

        return new TransportAdvanceBatch(reads);
    }

    public static TransportAdvanceEvaluation Evaluate(
        TransportAdvanceBatch batch,
        INavigation navigation,
        TransportTiming timing,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(navigation);

        var effects = new List<TransportAdvanceEffect>();
        foreach (TransportAdvanceRead transport in batch.Transports)
        {
            TravelTarget target = transport.Status is
                TransportJobStatus.Assigned
                or TransportJobStatus.WaitingForRouteToSource
                ? TravelTarget.Source
                : TravelTarget.Destination;
            effects.Add(EvaluateTransport(
                transport,
                target,
                navigation,
                timing,
                now));
        }

        return new TransportAdvanceEvaluation(effects);
    }

    public static TransportAdvanceEvaluation EvaluateContinuation(
        TransportBoard board,
        Freighter freighter,
        InventoryRegistry inventories,
        TravelTarget target,
        INavigation navigation,
        TransportTiming timing,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(freighter);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(navigation);
        TransportJobId jobId = freighter.ActiveJobId
            ?? throw new InvalidOperationException(
                $"Freighter {freighter.ShipId} has no active transport job.");
        TransportJob job = board.GetJob(jobId)
            ?? throw new KeyNotFoundException($"Unknown transport job {jobId}.");
        Inventory destination = inventories.Get(job.DestinationInventoryId)
            ?? throw new KeyNotFoundException(
                $"Unknown inventory {job.DestinationInventoryId}.");
        var read = new TransportAdvanceRead(
            freighter.ShipId,
            job.Id,
            job.Generation,
            job.Status,
            freighter.LocationId,
            job.SourceLocationId,
            job.DestinationLocationId,
            job.DestinationInventoryId,
            job.Quantity,
            destination.RemainingCapacity);
        return new TransportAdvanceEvaluation(
            [
                EvaluateTransport(
                    read,
                    target,
                    navigation,
                    timing,
                    now),
            ]);
    }

    public TransportAdvanceCommitResult Commit(
        TransportAdvanceEvaluation evaluation,
        TransportBoard board,
        ShipRegistry ships,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(capacityReservationIds);

        var commits = new List<TransportAdvanceCommit>();
        int rejected = 0;
        foreach (TransportAdvanceEffect effect in evaluation.Effects
            .OrderBy(effect => effect.ShipId, _shipComparer)
            .ThenBy(effect => effect.JobId, _jobComparer))
        {
            TransportAdvanceCommit? commit = board.CommitAdvance(
                effect,
                ships.GetFreighter(effect.ShipId),
                inventories,
                capacityReservationIds);
            if (commit is null)
            {
                rejected++;
                continue;
            }

            commits.Add(commit);
        }

        return new TransportAdvanceCommitResult(commits, rejected);
    }

    public TransportAdvanceReconciliationResult Reconcile(
        TransportBoard board,
        ShipRegistry ships,
        InventoryRegistry inventories,
        IdSequence<CapacityReservationId> capacityReservationIds,
        INavigation navigation,
        TransportTiming timing,
        SimulationTime now)
    {
        var measurements = new List<RuntimeMeasurement>();

        Stopwatch stopwatch = Stopwatch.StartNew();
        TransportAdvanceBatch batch = CreateBatch(board, ships, inventories);
        stopwatch.Stop();
        int batchCount = batch.Transports.Count == 0 ? 0 : 1;
        measurements.Add(new RuntimeMeasurement(
            DomainName,
            RuntimeMeasurementStage.BatchPreparation,
            stopwatch.Elapsed,
            batchCount,
            0,
            0,
            0));

        stopwatch.Restart();
        TransportAdvanceEvaluation evaluation =
            Evaluate(batch, navigation, timing, now);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            DomainName,
            RuntimeMeasurementStage.Evaluation,
            stopwatch.Elapsed,
            batchCount,
            evaluation.Effects.Count,
            0,
            0));

        stopwatch.Restart();
        TransportAdvanceCommitResult commit = Commit(
            evaluation,
            board,
            ships,
            inventories,
            capacityReservationIds);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            DomainName,
            RuntimeMeasurementStage.Commit,
            stopwatch.Elapsed,
            batchCount,
            evaluation.Effects.Count,
            commit.Commits.Count,
            commit.RejectedEffectCount));

        return new TransportAdvanceReconciliationResult(commit, measurements);
    }

    private static TransportAdvanceEffect EvaluateTransport(
        TransportAdvanceRead transport,
        TravelTarget target,
        INavigation navigation,
        TransportTiming timing,
        SimulationTime now)
    {
        LocationId destination = target == TravelTarget.Source
            ? transport.SourceLocationId
            : transport.DestinationLocationId;
        RoutePlan? plan = navigation.FindRoute(
            transport.CurrentLocationId,
            destination);
        if (plan is null)
        {
            return new TransportAdvanceEffect.WaitForRoute(
                transport.ShipId,
                transport.JobId,
                transport.Generation,
                transport.Status,
                transport.CurrentLocationId,
                target);
        }

        if (plan.RouteIds.Count > 0)
        {
            RouteId routeId = plan.RouteIds[0];
            DirectedRoute route = navigation.GetRoute(routeId)
                ?? throw new KeyNotFoundException($"Unknown route {routeId}.");
            return new TransportAdvanceEffect.Travel(
                transport.ShipId,
                transport.JobId,
                transport.Generation,
                transport.Status,
                transport.CurrentLocationId,
                target,
                routeId,
                now.Add(route.BaseDuration));
        }

        if (target == TravelTarget.Source)
        {
            return new TransportAdvanceEffect.BeginLoading(
                transport.ShipId,
                transport.JobId,
                transport.Generation,
                transport.Status,
                transport.CurrentLocationId,
                now.Add(timing.LoadingDuration(transport.Quantity)));
        }

        return transport.Quantity > transport.DestinationRemainingCapacity
            ? new TransportAdvanceEffect.WaitForDestinationCapacity(
                transport.ShipId,
                transport.JobId,
                transport.Generation,
                transport.Status,
                transport.CurrentLocationId)
            : new TransportAdvanceEffect.BeginUnloading(
                transport.ShipId,
                transport.JobId,
                transport.Generation,
                transport.Status,
                transport.CurrentLocationId,
                now.Add(timing.UnloadingDuration(transport.Quantity)));
    }
}
