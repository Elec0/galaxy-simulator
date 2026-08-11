using System.Diagnostics;

namespace GalaxyCommand.Simulation;

public sealed record LogisticsDemandPublicationRead(
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Required,
    Quantity Pending,
    DemandPriority Priority);

public sealed record LogisticsSupplyPublicationRead(
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Available,
    Quantity Offered);

public sealed class LogisticsPublicationBatch
{
    internal LogisticsPublicationBatch(
        IEnumerable<LogisticsDemandPublicationRead> demands,
        IEnumerable<LogisticsSupplyPublicationRead> supplies)
    {
        Demands = Array.AsReadOnly(demands.ToArray());
        Supplies = Array.AsReadOnly(supplies.ToArray());
    }

    public IReadOnlyList<LogisticsDemandPublicationRead> Demands { get; }

    public IReadOnlyList<LogisticsSupplyPublicationRead> Supplies { get; }
}

public sealed record LogisticsDemandPublicationProposal(
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Quantity,
    DemandPriority Priority,
    SimulationTime CreatedAt);

public sealed record LogisticsSupplyPublicationProposal(
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Quantity);

public sealed class LogisticsPublicationEvaluation
{
    internal LogisticsPublicationEvaluation(
        IEnumerable<LogisticsDemandPublicationProposal> demands,
        IEnumerable<LogisticsSupplyPublicationProposal> supplies)
    {
        Demands = Array.AsReadOnly(demands.ToArray());
        Supplies = Array.AsReadOnly(supplies.ToArray());
    }

    public IReadOnlyList<LogisticsDemandPublicationProposal> Demands { get; }

    public IReadOnlyList<LogisticsSupplyPublicationProposal> Supplies { get; }
}

public sealed record LogisticsPublicationCommitResult(
    int PublishedDemandCount,
    int PublishedSupplyCount);

public sealed class LogisticsPublicationReconciliationResult
{
    internal LogisticsPublicationReconciliationResult(
        LogisticsPublicationCommitResult commit,
        IEnumerable<RuntimeMeasurement> measurements)
    {
        Commit = commit;
        Measurements = Array.AsReadOnly(measurements.ToArray());
    }

    public LogisticsPublicationCommitResult Commit { get; }

    public IReadOnlyList<RuntimeMeasurement> Measurements { get; }
}

public sealed record LogisticsMarketSupplyRead(
    SupplyOfferId OfferId,
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Remaining,
    Quantity SourceAvailable);

public sealed record LogisticsMarketDemandRead(
    DemandRequestId DemandId,
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Remaining,
    DemandPriority Priority,
    SimulationTime CreatedAt);

public sealed record LogisticsFreighterRead(
    ShipId ShipId,
    LocationId LocationId,
    InventoryId CargoInventoryId,
    Quantity CargoCapacity,
    TransportJobId? ActiveJobId);

public sealed class LogisticsAssignmentBatch
{
    internal LogisticsAssignmentBatch(
        IEnumerable<LogisticsFreighterRead> freighters,
        IEnumerable<LogisticsMarketSupplyRead> supplies,
        IEnumerable<LogisticsMarketDemandRead> demands)
    {
        Freighters = Array.AsReadOnly(freighters.ToArray());
        Supplies = Array.AsReadOnly(supplies.ToArray());
        Demands = Array.AsReadOnly(demands.ToArray());
    }

    public IReadOnlyList<LogisticsFreighterRead> Freighters { get; }

    public IReadOnlyList<LogisticsMarketSupplyRead> Supplies { get; }

    public IReadOnlyList<LogisticsMarketDemandRead> Demands { get; }
}

public sealed class LogisticsAssignmentEvaluation
{
    internal LogisticsAssignmentEvaluation(
        IEnumerable<TransportAssignmentCandidate> candidates)
    {
        Candidates = Array.AsReadOnly(candidates.ToArray());
    }

    public IReadOnlyList<TransportAssignmentCandidate> Candidates { get; }
}

public sealed record LogisticsAssignmentCommit(
    ShipId ShipId,
    TransportJobId JobId);

public sealed class LogisticsAssignmentCommitResult
{
    internal LogisticsAssignmentCommitResult(
        IEnumerable<LogisticsAssignmentCommit> assignments,
        int rejectedCandidateCount)
    {
        Assignments = Array.AsReadOnly(assignments.ToArray());
        RejectedCandidateCount = rejectedCandidateCount;
    }

    public IReadOnlyList<LogisticsAssignmentCommit> Assignments { get; }

    public int RejectedCandidateCount { get; }
}

public sealed class LogisticsAssignmentReconciliationResult
{
    internal LogisticsAssignmentReconciliationResult(
        LogisticsAssignmentCommitResult commit,
        IEnumerable<RuntimeMeasurement> measurements)
    {
        Commit = commit;
        Measurements = Array.AsReadOnly(measurements.ToArray());
    }

    public LogisticsAssignmentCommitResult Commit { get; }

    public IReadOnlyList<RuntimeMeasurement> Measurements { get; }
}

/// <summary>
/// Reusable market publication and deterministic freighter assignment owner.
/// Candidate evaluation is stable and mutation-free; commit reduces complete
/// candidate sets in ShipId order.
/// </summary>
public sealed class LogisticsSystem
{
    private const string AssignmentDomainName = "logistics-assignment";
    private const string PublicationDomainName = "logistics-publication";
    private readonly IComparer<DemandRequestId> _demandComparer =
        EntityIdComparer<DemandRequestId>.Instance;
    private readonly IComparer<InventoryId> _inventoryComparer =
        EntityIdComparer<InventoryId>.Instance;
    private readonly IComparer<LocationId> _locationComparer =
        EntityIdComparer<LocationId>.Instance;
    private readonly IComparer<MaterialId> _materialComparer =
        EntityIdComparer<MaterialId>.Instance;
    private readonly IComparer<ShipId> _shipComparer =
        EntityIdComparer<ShipId>.Instance;
    private readonly IComparer<SupplyOfferId> _supplyComparer =
        EntityIdComparer<SupplyOfferId>.Instance;

    public LogisticsPublicationBatch CreatePublicationBatch(
        IEnumerable<LogisticsDemandPublicationRead> demands,
        IEnumerable<LogisticsSupplyPublicationRead> supplies)
    {
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(supplies);
        return new LogisticsPublicationBatch(
            demands
                .OrderBy(demand => demand.InventoryId, _inventoryComparer)
                .ThenBy(demand => demand.LocationId, _locationComparer)
                .ThenBy(demand => demand.MaterialId, _materialComparer),
            supplies
                .OrderBy(supply => supply.InventoryId, _inventoryComparer)
                .ThenBy(supply => supply.LocationId, _locationComparer)
                .ThenBy(supply => supply.MaterialId, _materialComparer));
    }

    public static LogisticsPublicationEvaluation EvaluatePublication(
        LogisticsPublicationBatch batch,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var demands = new List<LogisticsDemandPublicationProposal>();
        foreach (LogisticsDemandPublicationRead demand in batch.Demands)
        {
            if (demand.Required <= demand.Pending)
            {
                continue;
            }

            demands.Add(new LogisticsDemandPublicationProposal(
                demand.InventoryId,
                demand.LocationId,
                demand.MaterialId,
                demand.Required.Subtract(demand.Pending),
                demand.Priority,
                now));
        }

        var supplies = new List<LogisticsSupplyPublicationProposal>();
        foreach (LogisticsSupplyPublicationRead supply in batch.Supplies)
        {
            if (supply.Available <= supply.Offered)
            {
                continue;
            }

            supplies.Add(new LogisticsSupplyPublicationProposal(
                supply.InventoryId,
                supply.LocationId,
                supply.MaterialId,
                supply.Available.Subtract(supply.Offered)));
        }

        return new LogisticsPublicationEvaluation(demands, supplies);
    }

    public LogisticsPublicationCommitResult CommitPublication(
        LogisticsPublicationEvaluation evaluation,
        TransportBoard board,
        TransportIdSequences ids)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ids);

        foreach (LogisticsDemandPublicationProposal demand in evaluation.Demands
            .OrderBy(demand => demand.InventoryId, _inventoryComparer)
            .ThenBy(demand => demand.LocationId, _locationComparer)
            .ThenBy(demand => demand.MaterialId, _materialComparer))
        {
            board.PublishDemand(
                ids,
                demand.InventoryId,
                demand.LocationId,
                demand.MaterialId,
                demand.Quantity,
                demand.Priority,
                demand.CreatedAt);
        }

        foreach (LogisticsSupplyPublicationProposal supply in evaluation.Supplies
            .OrderBy(supply => supply.InventoryId, _inventoryComparer)
            .ThenBy(supply => supply.LocationId, _locationComparer)
            .ThenBy(supply => supply.MaterialId, _materialComparer))
        {
            board.PublishSupply(
                ids,
                supply.InventoryId,
                supply.LocationId,
                supply.MaterialId,
                supply.Quantity);
        }

        return new LogisticsPublicationCommitResult(
            evaluation.Demands.Count,
            evaluation.Supplies.Count);
    }

    public LogisticsPublicationReconciliationResult ReconcilePublication(
        IEnumerable<LogisticsDemandPublicationRead> demands,
        IEnumerable<LogisticsSupplyPublicationRead> supplies,
        TransportBoard board,
        TransportIdSequences ids,
        SimulationTime now)
    {
        var measurements = new List<RuntimeMeasurement>();

        Stopwatch stopwatch = Stopwatch.StartNew();
        LogisticsPublicationBatch batch =
            CreatePublicationBatch(demands, supplies);
        stopwatch.Stop();
        int batchCount = batch.Demands.Count == 0 && batch.Supplies.Count == 0
            ? 0
            : 1;
        measurements.Add(new RuntimeMeasurement(
            PublicationDomainName,
            RuntimeMeasurementStage.BatchPreparation,
            stopwatch.Elapsed,
            batchCount,
            0,
            0,
            0));

        stopwatch.Restart();
        LogisticsPublicationEvaluation evaluation =
            EvaluatePublication(batch, now);
        stopwatch.Stop();
        int proposalCount = evaluation.Demands.Count + evaluation.Supplies.Count;
        measurements.Add(new RuntimeMeasurement(
            PublicationDomainName,
            RuntimeMeasurementStage.Evaluation,
            stopwatch.Elapsed,
            batchCount,
            proposalCount,
            0,
            0));

        stopwatch.Restart();
        LogisticsPublicationCommitResult commit =
            CommitPublication(evaluation, board, ids);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            PublicationDomainName,
            RuntimeMeasurementStage.Commit,
            stopwatch.Elapsed,
            batchCount,
            proposalCount,
            commit.PublishedDemandCount + commit.PublishedSupplyCount,
            0));

        return new LogisticsPublicationReconciliationResult(commit, measurements);
    }

    public LogisticsAssignmentBatch CreateAssignmentBatch(
        TransportBoard board,
        IEnumerable<Freighter> freighters,
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(freighters);
        ArgumentNullException.ThrowIfNull(inventories);

        var freighterReads = new List<LogisticsFreighterRead>();
        foreach (Freighter freighter in freighters.OrderBy(
            freighter => freighter.ShipId,
            _shipComparer))
        {
            Inventory cargo = inventories.Get(freighter.CargoInventoryId)
                ?? throw new KeyNotFoundException(
                    $"Unknown inventory {freighter.CargoInventoryId}.");
            freighterReads.Add(new LogisticsFreighterRead(
                freighter.ShipId,
                freighter.LocationId,
                freighter.CargoInventoryId,
                cargo.RemainingCapacity,
                freighter.ActiveJobId));
        }

        var supplies = new List<LogisticsMarketSupplyRead>();
        foreach (SupplyOffer supply in board.Supplies)
        {
            Inventory source = inventories.Get(supply.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {supply.InventoryId}.");
            supplies.Add(new LogisticsMarketSupplyRead(
                supply.Id,
                supply.InventoryId,
                supply.LocationId,
                supply.MaterialId,
                supply.Remaining,
                source.Available(supply.MaterialId)));
        }

        var demands = board.Demands.Select(demand =>
            new LogisticsMarketDemandRead(
                demand.Id,
                demand.InventoryId,
                demand.LocationId,
                demand.MaterialId,
                demand.Remaining,
                demand.Priority,
                demand.CreatedAt));
        return new LogisticsAssignmentBatch(freighterReads, supplies, demands);
    }

    public static LogisticsAssignmentEvaluation EvaluateAssignments(
        LogisticsAssignmentBatch batch,
        ILogisticsNavigation navigation,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(navigation);

        var candidates = new List<TransportAssignmentCandidate>();
        foreach (LogisticsFreighterRead freighter in batch.Freighters)
        {
            if (freighter.ActiveJobId is not null
                || freighter.CargoCapacity == Quantity.Zero)
            {
                continue;
            }

            foreach (LogisticsMarketDemandRead demand in batch.Demands)
            {
                if (demand.Remaining == Quantity.Zero)
                {
                    continue;
                }

                foreach (LogisticsMarketSupplyRead supply in batch.Supplies)
                {
                    if (supply.Remaining == Quantity.Zero
                        || supply.MaterialId != demand.MaterialId)
                    {
                        continue;
                    }

                    Quantity quantity = demand.Remaining
                        .Min(supply.Remaining)
                        .Min(supply.SourceAvailable)
                        .Min(freighter.CargoCapacity);
                    if (quantity == Quantity.Zero)
                    {
                        continue;
                    }

                    LogisticsTravelEstimate? toSource = navigation.Estimate(
                        freighter.ShipId,
                        freighter.LocationId,
                        supply.LocationId,
                        now);
                    LogisticsTravelEstimate? toDestination = navigation.Estimate(
                        freighter.ShipId,
                        supply.LocationId,
                        demand.LocationId,
                        now);
                    if (toSource is null || toDestination is null)
                    {
                        continue;
                    }

                    candidates.Add(new TransportAssignmentCandidate(
                        freighter.ShipId,
                        supply.OfferId,
                        demand.DemandId,
                        demand.Priority,
                        demand.CreatedAt,
                        toSource.Duration.Add(toDestination.Duration),
                        quantity));
                }
            }
        }

        candidates.Sort((left, right) =>
        {
            int ship = left.ShipId.Value.CompareTo(right.ShipId.Value);
            return ship != 0
                ? ship
                : TransportAssignmentCandidateComparer.Instance.Compare(left, right);
        });
        return new LogisticsAssignmentEvaluation(candidates);
    }

    public LogisticsAssignmentCommitResult CommitAssignments(
        LogisticsAssignmentEvaluation evaluation,
        TransportBoard board,
        TransportIdSequences ids,
        IdSequence<ReservationId> reservationIds,
        ShipRegistry ships,
        InventoryRegistry inventories,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(reservationIds);
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(inventories);

        var assignments = new List<LogisticsAssignmentCommit>();
        int rejected = 0;
        foreach (IGrouping<ShipId, TransportAssignmentCandidate> candidates
            in evaluation.Candidates
                .GroupBy(candidate => candidate.ShipId)
                .OrderBy(group => group.Key, _shipComparer))
        {
            Freighter freighter = ships.GetFreighter(candidates.Key)
                ?? throw new KeyNotFoundException($"Missing freighter {candidates.Key}.");
            foreach (TransportAssignmentCandidate candidate in candidates.OrderBy(
                candidate => candidate,
                TransportAssignmentCandidateComparer.Instance))
            {
                TransportJobId? jobId = board.TryCommitAssignment(
                    candidate,
                    ids,
                    reservationIds,
                    freighter,
                    inventories,
                    now);
                if (jobId is not { } committedJobId)
                {
                    rejected++;
                    continue;
                }

                assignments.Add(new LogisticsAssignmentCommit(
                    freighter.ShipId,
                    committedJobId));
                break;
            }
        }

        return new LogisticsAssignmentCommitResult(assignments, rejected);
    }

    public LogisticsAssignmentReconciliationResult ReconcileAssignments(
        TransportBoard board,
        TransportIdSequences ids,
        IdSequence<ReservationId> reservationIds,
        ShipRegistry ships,
        InventoryRegistry inventories,
        ILogisticsNavigation navigation,
        SimulationTime now)
    {
        var measurements = new List<RuntimeMeasurement>();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Freighter[] freighters = ships.FreighterIds
            .Select(shipId => ships.GetFreighter(shipId)
                ?? throw new KeyNotFoundException($"Missing freighter {shipId}."))
            .ToArray();
        LogisticsAssignmentBatch batch =
            CreateAssignmentBatch(board, freighters, inventories);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            AssignmentDomainName,
            RuntimeMeasurementStage.BatchPreparation,
            stopwatch.Elapsed,
            batch.Freighters.Count == 0 ? 0 : 1,
            0,
            0,
            0));

        stopwatch.Restart();
        LogisticsAssignmentEvaluation evaluation =
            EvaluateAssignments(batch, navigation, now);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            AssignmentDomainName,
            RuntimeMeasurementStage.Evaluation,
            stopwatch.Elapsed,
            batch.Freighters.Count == 0 ? 0 : 1,
            evaluation.Candidates.Count,
            0,
            0));

        stopwatch.Restart();
        LogisticsAssignmentCommitResult commit = CommitAssignments(
            evaluation,
            board,
            ids,
            reservationIds,
            ships,
            inventories,
            now);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            AssignmentDomainName,
            RuntimeMeasurementStage.Commit,
            stopwatch.Elapsed,
            batch.Freighters.Count == 0 ? 0 : 1,
            evaluation.Candidates.Count,
            commit.Assignments.Count,
            commit.RejectedCandidateCount));

        return new LogisticsAssignmentReconciliationResult(commit, measurements);
    }
}
