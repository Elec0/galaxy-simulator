using System.Diagnostics;

namespace GalaxyCommand.Simulation;

public sealed record ConstructionInputRead(
    MaterialId MaterialId,
    Quantity Required,
    Quantity Reserved,
    Quantity Available);

public sealed record ConstructionFacilityRead(
    FacilityId FacilityId,
    InventoryId InventoryId,
    ConstructionOrderId OrderId,
    ConstructionDesignId DesignId,
    EventGeneration Generation,
    IReadOnlyList<ConstructionInputRead> Inputs);

public sealed class ConstructionEvaluationBatch
{
    internal ConstructionEvaluationBatch(IEnumerable<ConstructionFacilityRead> facilities)
    {
        Facilities = Array.AsReadOnly(facilities.ToArray());
    }

    public IReadOnlyList<ConstructionFacilityRead> Facilities { get; }
}

public sealed record ConstructionReservationProposal(
    FacilityId FacilityId,
    InventoryId InventoryId,
    ConstructionOrderId OrderId,
    EventGeneration Generation,
    MaterialId MaterialId,
    Quantity Quantity);

public sealed class ConstructionEvaluationResult
{
    internal ConstructionEvaluationResult(
        IEnumerable<ConstructionFacilityRead> facilities,
        IEnumerable<ConstructionReservationProposal> reservationProposals)
    {
        Facilities = Array.AsReadOnly(facilities.ToArray());
        ReservationProposals = Array.AsReadOnly(reservationProposals.ToArray());
    }

    public IReadOnlyList<ConstructionFacilityRead> Facilities { get; }

    public IReadOnlyList<ConstructionReservationProposal> ReservationProposals { get; }
}

public sealed record ConstructionInputConsumption(
    FacilityId FacilityId,
    MaterialId MaterialId,
    Quantity Quantity);

public sealed record ConstructionCompletionProposal(
    SimulationTime Timestamp,
    FacilityId FacilityId,
    ConstructionOrderId OrderId,
    EventGeneration Generation);

public sealed class ConstructionCommitResult
{
    internal ConstructionCommitResult(
        IEnumerable<ConstructionInputConsumption> consumedInputs,
        IEnumerable<ConstructionCompletionProposal> completionProposals,
        int acceptedEffectCount,
        int rejectedEffectCount)
    {
        ConsumedInputs = Array.AsReadOnly(consumedInputs.ToArray());
        CompletionProposals = Array.AsReadOnly(completionProposals.ToArray());
        AcceptedEffectCount = acceptedEffectCount;
        RejectedEffectCount = rejectedEffectCount;
    }

    public IReadOnlyList<ConstructionInputConsumption> ConsumedInputs { get; }

    public IReadOnlyList<ConstructionCompletionProposal> CompletionProposals { get; }

    public int AcceptedEffectCount { get; }

    public int RejectedEffectCount { get; }
}

public sealed class ConstructionReconciliationResult
{
    internal ConstructionReconciliationResult(
        ConstructionCommitResult commit,
        IEnumerable<RuntimeMeasurement> measurements)
    {
        Commit = commit;
        Measurements = Array.AsReadOnly(measurements.ToArray());
    }

    public ConstructionCommitResult Commit { get; }

    public IReadOnlyList<RuntimeMeasurement> Measurements { get; }
}

public sealed record ConstructionCompletionCommitResult(
    ScheduledEventDisposition Disposition,
    ConstructionMaterializationEffect? Materialization);

/// <summary>
/// Product-neutral construction owner. Evaluation reads immutable facility
/// batches; commit owns construction reservations and lifecycle transitions.
/// </summary>
public sealed class ConstructionSystem
{
    private const string DomainName = "construction";
    private readonly IComparer<ConstructionOrderId> _orderComparer =
        EntityIdComparer<ConstructionOrderId>.Instance;
    private readonly IComparer<FacilityId> _facilityComparer =
        EntityIdComparer<FacilityId>.Instance;
    private readonly IComparer<InventoryId> _inventoryComparer =
        EntityIdComparer<InventoryId>.Instance;
    private readonly IComparer<MaterialId> _materialComparer =
        EntityIdComparer<MaterialId>.Instance;

    public ConstructionEvaluationBatch CreateBatch(
        IEnumerable<ConstructionProcess> processes,
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(inventories);

        var facilities = new List<ConstructionFacilityRead>();
        foreach (ConstructionProcess process in processes.OrderBy(
            process => process.FacilityId,
            _facilityComparer))
        {
            if (process.ActiveOrder is not
                {
                    Status: ConstructionOrderStatus.WaitingForInputs,
                } order)
            {
                continue;
            }

            Inventory inventory = inventories.Get(process.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {process.InventoryId}.");
            var inputs = new List<ConstructionInputRead>();
            foreach ((MaterialId materialId, Quantity required) in order.Design.Recipe.Inputs)
            {
                inputs.Add(new ConstructionInputRead(
                    materialId,
                    required,
                    order.ReservedInput(inventory, materialId),
                    inventory.Available(materialId)));
            }

            facilities.Add(new ConstructionFacilityRead(
                process.FacilityId,
                process.InventoryId,
                order.Id,
                order.DesignId,
                order.Generation,
                Array.AsReadOnly(inputs.ToArray())));
        }

        return new ConstructionEvaluationBatch(facilities);
    }

    public static ConstructionEvaluationResult Evaluate(ConstructionEvaluationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var proposals = new List<ConstructionReservationProposal>();
        foreach (ConstructionFacilityRead facility in batch.Facilities)
        {
            foreach (ConstructionInputRead input in facility.Inputs)
            {
                Quantity missing = input.Required.Subtract(input.Reserved);
                Quantity requested = missing.Min(input.Available);
                if (requested == Quantity.Zero)
                {
                    continue;
                }

                proposals.Add(new ConstructionReservationProposal(
                    facility.FacilityId,
                    facility.InventoryId,
                    facility.OrderId,
                    facility.Generation,
                    input.MaterialId,
                    requested));
            }
        }

        return new ConstructionEvaluationResult(batch.Facilities, proposals);
    }

    public ConstructionCommitResult Commit(
        ConstructionEvaluationResult evaluation,
        IReadOnlyDictionary<FacilityId, ConstructionProcess> processes,
        InventoryRegistry inventories,
        IdSequence<ReservationId> reservationIds,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(reservationIds);

        int accepted = 0;
        int rejected = 0;
        foreach (ConstructionReservationProposal proposal in evaluation.ReservationProposals
            .OrderBy(
                proposal => proposal.InventoryId,
                _inventoryComparer)
            .ThenBy(
                proposal => proposal.FacilityId,
                _facilityComparer)
            .ThenBy(
                proposal => proposal.OrderId,
                _orderComparer)
            .ThenBy(
                proposal => proposal.MaterialId,
                _materialComparer))
        {
            if (!processes.TryGetValue(
                    proposal.FacilityId,
                    out ConstructionProcess? process)
                || process.InventoryId != proposal.InventoryId
                || !process.MatchesActivePreparation(
                    proposal.OrderId,
                    proposal.Generation))
            {
                rejected++;
                continue;
            }

            Inventory inventory = inventories.Get(proposal.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {proposal.InventoryId}.");
            Quantity granted = proposal.Quantity
                .Min(process.MissingInput(inventory, proposal.MaterialId))
                .Min(inventory.Available(proposal.MaterialId));
            if (granted == Quantity.Zero)
            {
                rejected++;
                continue;
            }

            process.GrantInputReservation(
                reservationIds,
                inventory,
                proposal.MaterialId,
                granted);
            accepted++;
        }

        var consumed = new List<ConstructionInputConsumption>();
        var completions = new List<ConstructionCompletionProposal>();
        foreach (ConstructionFacilityRead facility in evaluation.Facilities.OrderBy(
            facility => facility.FacilityId,
            _facilityComparer))
        {
            if (!processes.TryGetValue(
                    facility.FacilityId,
                    out ConstructionProcess? process)
                || process.InventoryId != facility.InventoryId
                || !process.MatchesActivePreparation(
                    facility.OrderId,
                    facility.Generation))
            {
                continue;
            }

            Inventory inventory = inventories.Get(facility.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {facility.InventoryId}.");
            if (process.StartPrepared(inventory, now) is not { } completesAt)
            {
                continue;
            }

            ConstructionOrder started = process.ActiveOrder
                ?? throw new InvalidOperationException(
                    $"Construction facility {facility.FacilityId} started without an active order.");
            foreach ((MaterialId materialId, Quantity quantity) in started.Design.Recipe.Inputs)
            {
                consumed.Add(new ConstructionInputConsumption(
                    facility.FacilityId,
                    materialId,
                    quantity));
            }

            completions.Add(new ConstructionCompletionProposal(
                completesAt,
                facility.FacilityId,
                started.Id,
                started.Generation));
        }

        return new ConstructionCommitResult(consumed, completions, accepted, rejected);
    }

    public ConstructionReconciliationResult Reconcile(
        IReadOnlyDictionary<FacilityId, ConstructionProcess> processes,
        InventoryRegistry inventories,
        IdSequence<ReservationId> reservationIds,
        SimulationTime now)
    {
        var measurements = new List<RuntimeMeasurement>();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ConstructionEvaluationBatch batch = CreateBatch(processes.Values, inventories);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            DomainName,
            RuntimeMeasurementStage.BatchPreparation,
            stopwatch.Elapsed,
            batch.Facilities.Count == 0 ? 0 : 1,
            0,
            0,
            0));

        stopwatch.Restart();
        ConstructionEvaluationResult evaluation = Evaluate(batch);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            DomainName,
            RuntimeMeasurementStage.Evaluation,
            stopwatch.Elapsed,
            batch.Facilities.Count == 0 ? 0 : 1,
            evaluation.ReservationProposals.Count,
            0,
            0));

        stopwatch.Restart();
        ConstructionCommitResult commit = Commit(
            evaluation,
            processes,
            inventories,
            reservationIds,
            now);
        stopwatch.Stop();
        measurements.Add(new RuntimeMeasurement(
            DomainName,
            RuntimeMeasurementStage.Commit,
            stopwatch.Elapsed,
            batch.Facilities.Count == 0 ? 0 : 1,
            evaluation.ReservationProposals.Count,
            commit.AcceptedEffectCount,
            commit.RejectedEffectCount));

        return new ConstructionReconciliationResult(commit, measurements);
    }

    public static ConstructionCompletionCommitResult CommitCompletion(
        IReadOnlyDictionary<FacilityId, ConstructionProcess> processes,
        FacilityId facilityId,
        ConstructionOrderId orderId,
        EventGeneration generation,
        SimulationTime now,
        EventKey? completionEventKey = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        if (!processes.TryGetValue(facilityId, out ConstructionProcess? process))
        {
            return new ConstructionCompletionCommitResult(
                ScheduledEventDisposition.IgnoredMissingReference,
                null);
        }

        ScheduledEventDisposition disposition = process.CompleteScheduled(
            orderId,
            generation,
            now,
            completionEventKey,
            out ConstructionMaterializationEffect? materialization);
        return new ConstructionCompletionCommitResult(disposition, materialization);
    }
}
