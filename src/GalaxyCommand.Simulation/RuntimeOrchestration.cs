using System.Diagnostics;

namespace GalaxyCommand.Simulation;

public enum RuntimeMeasurementStage
{
    BatchPreparation,
    Evaluation,
    Commit,
}

public sealed record RuntimeMeasurement(
    string Domain,
    RuntimeMeasurementStage Stage,
    TimeSpan Elapsed,
    int BatchCount,
    int ProposalCount,
    int AcceptedEffectCount,
    int RejectedEffectCount);

public sealed record ProductionInputRead(
    MaterialId MaterialId,
    Quantity Required,
    Quantity Reserved,
    Quantity Available);

public sealed record ProductionFacilityRead(
    FacilityId FacilityId,
    InventoryId InventoryId,
    ProductionJobId JobId,
    EventGeneration Generation,
    IReadOnlyList<ProductionInputRead> Inputs);

public sealed class ProductionEvaluationBatch
{
    internal ProductionEvaluationBatch(IEnumerable<ProductionFacilityRead> facilities)
    {
        Facilities = Array.AsReadOnly(facilities.ToArray());
    }

    public IReadOnlyList<ProductionFacilityRead> Facilities { get; }
}

public sealed record ProductionReservationProposal(
    FacilityId FacilityId,
    InventoryId InventoryId,
    ProductionJobId JobId,
    EventGeneration Generation,
    MaterialId MaterialId,
    Quantity Quantity);

public sealed class ProductionEvaluationResult
{
    internal ProductionEvaluationResult(
        IEnumerable<ProductionFacilityRead> facilities,
        IEnumerable<ProductionReservationProposal> reservationProposals)
    {
        Facilities = Array.AsReadOnly(facilities.ToArray());
        ReservationProposals = Array.AsReadOnly(reservationProposals.ToArray());
    }

    public IReadOnlyList<ProductionFacilityRead> Facilities { get; }

    public IReadOnlyList<ProductionReservationProposal> ReservationProposals { get; }
}

public sealed record ProductionInputConsumption(
    FacilityId FacilityId,
    MaterialId MaterialId,
    Quantity Quantity);

public sealed record ProductionCompletionProposal(
    SimulationTime Timestamp,
    FacilityId FacilityId,
    ProductionJobId JobId,
    EventGeneration Generation);

public sealed class ProductionCommitResult
{
    internal ProductionCommitResult(
        IEnumerable<ProductionInputConsumption> consumedInputs,
        IEnumerable<ProductionCompletionProposal> completionProposals,
        int acceptedEffectCount,
        int rejectedEffectCount)
    {
        ConsumedInputs = Array.AsReadOnly(consumedInputs.ToArray());
        CompletionProposals = Array.AsReadOnly(completionProposals.ToArray());
        AcceptedEffectCount = acceptedEffectCount;
        RejectedEffectCount = rejectedEffectCount;
    }

    public IReadOnlyList<ProductionInputConsumption> ConsumedInputs { get; }

    public IReadOnlyList<ProductionCompletionProposal> CompletionProposals { get; }

    public int AcceptedEffectCount { get; }

    public int RejectedEffectCount { get; }
}

public sealed class ProductionReconciliationResult
{
    internal ProductionReconciliationResult(
        ProductionCommitResult commit,
        IEnumerable<RuntimeMeasurement> measurements)
    {
        Commit = commit;
        Measurements = Array.AsReadOnly(measurements.ToArray());
    }

    public ProductionCommitResult Commit { get; }

    public IReadOnlyList<RuntimeMeasurement> Measurements { get; }
}

/// <summary>
/// Reusable production readiness owner. Evaluation reads an immutable batch and
/// commit applies reservation effects in stable facility and material order.
/// </summary>
public sealed class ProductionSystem
{
    private const string DomainName = "production";
    private readonly IComparer<FacilityId> _facilityComparer =
        EntityIdComparer<FacilityId>.Instance;
    private readonly IComparer<InventoryId> _inventoryComparer =
        EntityIdComparer<InventoryId>.Instance;
    private readonly IComparer<MaterialId> _materialComparer =
        EntityIdComparer<MaterialId>.Instance;
    private readonly IComparer<ProductionJobId> _jobComparer =
        EntityIdComparer<ProductionJobId>.Instance;

    public ProductionEvaluationBatch CreateBatch(
        IEnumerable<ProductionLine> productionLines,
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(productionLines);
        ArgumentNullException.ThrowIfNull(inventories);

        var facilities = new List<ProductionFacilityRead>();
        foreach (ProductionLine line in productionLines.OrderBy(
            line => line.FacilityId,
            _facilityComparer))
        {
            if (line.ActiveJob is not
                {
                    Status: ProductionJobStatus.WaitingForInputs,
                } job)
            {
                continue;
            }

            Inventory inventory = inventories.Get(line.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {line.InventoryId}.");
            var inputs = new List<ProductionInputRead>();
            foreach ((MaterialId materialId, Quantity required) in job.Recipe.Inputs)
            {
                inputs.Add(new ProductionInputRead(
                    materialId,
                    required,
                    job.ReservedInput(inventory, materialId),
                    inventory.Available(materialId)));
            }

            facilities.Add(new ProductionFacilityRead(
                line.FacilityId,
                line.InventoryId,
                job.Id,
                job.Generation,
                Array.AsReadOnly(inputs.ToArray())));
        }

        return new ProductionEvaluationBatch(facilities);
    }

    public static ProductionEvaluationResult Evaluate(ProductionEvaluationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var proposals = new List<ProductionReservationProposal>();
        foreach (ProductionFacilityRead facility in batch.Facilities)
        {
            foreach (ProductionInputRead input in facility.Inputs)
            {
                Quantity missing = input.Required.Subtract(input.Reserved);
                Quantity requested = missing.Min(input.Available);
                if (requested == Quantity.Zero)
                {
                    continue;
                }

                proposals.Add(new ProductionReservationProposal(
                    facility.FacilityId,
                    facility.InventoryId,
                    facility.JobId,
                    facility.Generation,
                    input.MaterialId,
                    requested));
            }
        }

        return new ProductionEvaluationResult(batch.Facilities, proposals);
    }

    public ProductionCommitResult Commit(
        ProductionEvaluationResult evaluation,
        IReadOnlyDictionary<FacilityId, ProductionLine> productionLines,
        InventoryRegistry inventories,
        IdSequence<ReservationId> reservationIds,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(productionLines);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(reservationIds);

        int accepted = 0;
        int rejected = 0;
        foreach (ProductionReservationProposal proposal in evaluation.ReservationProposals
            .OrderBy(
                proposal => proposal.InventoryId,
                _inventoryComparer)
            .ThenBy(
                proposal => proposal.FacilityId,
                _facilityComparer)
            .ThenBy(
                proposal => proposal.JobId,
                _jobComparer)
            .ThenBy(
                proposal => proposal.MaterialId,
                _materialComparer))
        {
            if (!productionLines.TryGetValue(proposal.FacilityId, out ProductionLine? line)
                || line.InventoryId != proposal.InventoryId
                || !line.MatchesActivePreparation(proposal.JobId, proposal.Generation))
            {
                rejected++;
                continue;
            }

            Inventory inventory = inventories.Get(proposal.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {proposal.InventoryId}.");
            Quantity granted = proposal.Quantity
                .Min(line.MissingInput(inventory, proposal.MaterialId))
                .Min(inventory.Available(proposal.MaterialId));
            if (granted == Quantity.Zero)
            {
                rejected++;
                continue;
            }

            line.GrantInputReservation(
                reservationIds,
                inventory,
                proposal.MaterialId,
                granted);
            accepted++;
        }

        var consumed = new List<ProductionInputConsumption>();
        var completions = new List<ProductionCompletionProposal>();
        foreach (ProductionFacilityRead facility in evaluation.Facilities.OrderBy(
            facility => facility.FacilityId,
            _facilityComparer))
        {
            if (!productionLines.TryGetValue(facility.FacilityId, out ProductionLine? line)
                || line.InventoryId != facility.InventoryId
                || !line.MatchesActivePreparation(facility.JobId, facility.Generation))
            {
                continue;
            }

            Inventory inventory = inventories.Get(facility.InventoryId)
                ?? throw new KeyNotFoundException($"Unknown inventory {facility.InventoryId}.");
            if (line.StartPrepared(inventory, now) is not { } completesAt)
            {
                continue;
            }

            ProductionJob started = line.ActiveJob
                ?? throw new InvalidOperationException(
                    $"Production facility {facility.FacilityId} started without an active job.");
            foreach ((MaterialId materialId, Quantity quantity) in started.Recipe.Inputs)
            {
                consumed.Add(new ProductionInputConsumption(
                    facility.FacilityId,
                    materialId,
                    quantity));
            }

            completions.Add(new ProductionCompletionProposal(
                completesAt,
                facility.FacilityId,
                started.Id,
                started.Generation));
        }

        return new ProductionCommitResult(consumed, completions, accepted, rejected);
    }

    public ProductionReconciliationResult Reconcile(
        IReadOnlyDictionary<FacilityId, ProductionLine> productionLines,
        InventoryRegistry inventories,
        IdSequence<ReservationId> reservationIds,
        SimulationTime now)
    {
        var measurements = new List<RuntimeMeasurement>();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ProductionEvaluationBatch batch = CreateBatch(productionLines.Values, inventories);
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
        ProductionEvaluationResult evaluation = Evaluate(batch);
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
        ProductionCommitResult commit = Commit(
            evaluation,
            productionLines,
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

        return new ProductionReconciliationResult(commit, measurements);
    }
}
