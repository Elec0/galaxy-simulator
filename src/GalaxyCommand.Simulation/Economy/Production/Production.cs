namespace GalaxyCommand.Simulation;

/// <summary>
/// Integer work required to complete production.
/// </summary>
public readonly record struct Work(ulong Units);

/// <summary>
/// Non-zero production work completed per simulated second.
/// </summary>
public readonly record struct Throughput
{
    public Throughput(ulong unitsPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfZero(unitsPerSecond);
        UnitsPerSecond = unitsPerSecond;
    }

    public ulong UnitsPerSecond { get; }

    public SimulationDuration DurationFor(Work work)
    {
        UInt128 numerator = (UInt128)work.Units * 1_000;
        UInt128 divisor = UnitsPerSecond;
        UInt128 milliseconds = (numerator + divisor - 1) / divisor;
        if (milliseconds > ulong.MaxValue)
        {
            throw new OverflowException("Production duration exceeds the simulation timeline.");
        }

        return new SimulationDuration((ulong)milliseconds);
    }
}

/// <summary>
/// One batch transformation performed by a production line.
/// </summary>
public sealed class Recipe
{
    private readonly SortedDictionary<MaterialId, Quantity> _inputs;

    public Recipe(
        IEnumerable<KeyValuePair<MaterialId, Quantity>> inputs,
        MaterialId outputMaterial,
        Quantity outputQuantity,
        Work requiredWork)
    {
        _inputs = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        foreach ((MaterialId materialId, Quantity quantity) in inputs)
        {
            _inputs.Add(materialId, quantity);
        }

        OutputMaterial = outputMaterial;
        OutputQuantity = outputQuantity;
        RequiredWork = requiredWork;
    }

    public IReadOnlyDictionary<MaterialId, Quantity> Inputs => _inputs;

    public MaterialId OutputMaterial { get; }

    public Quantity OutputQuantity { get; }

    public Work RequiredWork { get; }
}

public enum ProductionJobStatus
{
    WaitingForInputs,
    Running,
    CompletedAwaitingStorage,
    Completed,
    Cancelled,
}

/// <summary>
/// One finite or repeating production request.
/// </summary>
public sealed class ProductionJob
{
    private readonly SortedDictionary<MaterialId, List<ReservationId>> _reservationIds =
        new(EntityIdComparer<MaterialId>.Instance);

    internal ProductionJob(ProductionJobId id, Recipe recipe, bool isRepeating)
    {
        Id = id;
        Recipe = recipe;
        IsRepeating = isRepeating;
    }

    public ProductionJobId Id { get; }

    public Recipe Recipe { get; }

    public bool IsRepeating { get; }

    public ProductionJobStatus Status { get; internal set; } = ProductionJobStatus.WaitingForInputs;

    public SimulationTime? CompletesAt { get; internal set; }

    public EventGeneration Generation { get; internal set; } = new(0);

    public Quantity ReservedInput(Inventory inventory, MaterialId materialId)
    {
        if (!_reservationIds.TryGetValue(materialId, out List<ReservationId>? reservationIds))
        {
            return Quantity.Zero;
        }

        ulong units = 0;
        foreach (ReservationId reservationId in reservationIds)
        {
            if (inventory.GetReservation(reservationId) is { } reservation)
            {
                units = checked(units + reservation.Quantity.Units);
            }
        }

        return new Quantity(units);
    }

    internal void AddReservation(MaterialId materialId, ReservationId reservationId)
    {
        if (!_reservationIds.TryGetValue(materialId, out List<ReservationId>? reservations))
        {
            reservations = [];
            _reservationIds.Add(materialId, reservations);
        }

        reservations.Add(reservationId);
    }

    internal IReadOnlyList<ReservationId> AllReservationIds() =>
        _reservationIds.Values.SelectMany(ids => ids).ToArray();

    internal IEnumerable<ProductionReservationLinkCheckpoint> ReservationLinks =>
        _reservationIds.SelectMany(pair => pair.Value.Select(reservationId =>
            new ProductionReservationLinkCheckpoint(pair.Key, reservationId)));

    internal void ClearReservations() => _reservationIds.Clear();
}

public sealed class ProductionIdSequences
{
    private readonly IdSequence<ProductionJobId> _jobs;

    public ProductionIdSequences()
        : this(new IdSequence<ProductionJobId>())
    {
    }

    private ProductionIdSequences(IdSequence<ProductionJobId> jobs) =>
        _jobs = jobs;

    internal ProductionJobId AllocateJob() => _jobs.Allocate();

    internal IdSequenceCheckpoint CaptureCheckpoint() => _jobs.CaptureCheckpoint();

    internal static CheckpointResult<ProductionIdSequences> RestoreCheckpoint(
        IdSequenceCheckpoint checkpoint)
    {
        CheckpointResult<IdSequence<ProductionJobId>> restored =
            IdSequence<ProductionJobId>.RestoreCheckpoint(checkpoint);
        return restored.IsSuccess
            ? CheckpointResult<ProductionIdSequences>.Success(
                new ProductionIdSequences(restored.Value!))
            : CheckpointResult<ProductionIdSequences>.Rejected(restored.Failure!);
    }
}

/// <summary>
/// One production capability with a shared inventory and FIFO job queue.
/// </summary>
public sealed class ProductionLine
{
    private readonly Throughput _throughput;
    private readonly Queue<ProductionJob> _queued = new();
    private readonly SortedDictionary<ProductionJobId, ProductionJob> _jobs =
        new(EntityIdComparer<ProductionJobId>.Instance);

    public ProductionLine(
        FacilityId facilityId,
        InventoryId inventoryId,
        Throughput throughput)
    {
        FacilityId = facilityId;
        InventoryId = inventoryId;
        _throughput = throughput;
    }

    public FacilityId FacilityId { get; }

    public InventoryId InventoryId { get; }

    internal Throughput Throughput => _throughput;

    public ProductionJob? ActiveJob { get; private set; }

    public int QueuedJobCount => _queued.Count;

    public ProductionJob? GetJob(ProductionJobId jobId) =>
        _jobs.GetValueOrDefault(jobId);

    /// <summary>
    /// Captures the exact job registry, active identity, FIFO queue, recipes,
    /// reservations, generations, and completion state in stable job order.
    /// </summary>
    internal ProductionLineCheckpoint CaptureCheckpoint() =>
        new(
            FacilityId,
            InventoryId,
            _throughput,
            ActiveJob?.Id,
            _queued.Select(job => job.Id).ToArray(),
            _jobs.Values.Select(job => new ProductionJobCheckpoint(
                job.Id,
                new ProductionRecipeCheckpoint(
                    job.Recipe.Inputs.Select(input =>
                        new ConstructionInputPolicyCheckpoint(input.Key, input.Value)).ToArray(),
                    job.Recipe.OutputMaterial,
                    job.Recipe.OutputQuantity,
                    job.Recipe.RequiredWork),
                job.IsRepeating,
                job.Status,
                job.CompletesAt,
                job.Generation,
                job.ReservationLinks
                    .OrderBy(link => link.MaterialId.Value)
                    .ThenBy(link => link.ReservationId.Value)
                    .ToArray()))
                .ToArray());

    /// <summary>
    /// Directly assembles already validated job state without enqueueing,
    /// reserving inventory, consuming inputs, or scheduling completion.
    /// </summary>
    internal static ProductionLine RestoreDirect(
        FacilityId facilityId,
        InventoryId inventoryId,
        Throughput throughput,
        IReadOnlyDictionary<ProductionJobId, ProductionJob> jobs,
        ProductionJobId? activeJobId,
        IEnumerable<ProductionJobId> queuedJobIds)
    {
        var line = new ProductionLine(facilityId, inventoryId, throughput);
        foreach ((ProductionJobId jobId, ProductionJob job) in jobs)
        {
            line._jobs.Add(jobId, job);
        }

        line.ActiveJob = activeJobId is { } active ? jobs[active] : null;
        foreach (ProductionJobId queuedJobId in queuedJobIds)
        {
            line._queued.Enqueue(jobs[queuedJobId]);
        }

        return line;
    }

    public IReadOnlyDictionary<MaterialId, Quantity> UnmetInputs(Inventory inventory)
    {
        var unmet = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        if (ActiveJob is not { Status: ProductionJobStatus.WaitingForInputs } job)
        {
            return unmet;
        }

        foreach ((MaterialId materialId, Quantity required) in job.Recipe.Inputs)
        {
            Quantity missing = required.Subtract(job.ReservedInput(inventory, materialId));
            if (missing > Quantity.Zero)
            {
                unmet.Add(materialId, missing);
            }
        }

        return unmet;
    }

    public ProductionJobId Enqueue(
        ProductionIdSequences ids,
        Recipe recipe,
        bool repeat)
    {
        ProductionJob job = AllocateJob(ids, recipe, repeat);
        _jobs.Add(job.Id, job);
        if (ActiveJob is null)
        {
            ActiveJob = job;
        }
        else
        {
            _queued.Enqueue(job);
        }

        return job.Id;
    }

    public SimulationTime? PrepareActive(
        IdSequence<ReservationId> reservationIds,
        Inventory inventory,
        SimulationTime now)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveJob is not { Status: ProductionJobStatus.WaitingForInputs } job)
        {
            return null;
        }

        foreach ((MaterialId materialId, Quantity required) in job.Recipe.Inputs)
        {
            Quantity reserved = job.ReservedInput(inventory, materialId);
            Quantity missing = required.Subtract(reserved);
            Quantity toReserve = missing.Min(inventory.Available(materialId));
            if (toReserve == Quantity.Zero)
            {
                continue;
            }

            ReservationId reservationId = reservationIds.Allocate();
            inventory.Reserve(
                reservationId,
                materialId,
                toReserve,
                new ReservationOwner.ProductionJob(job.Id));
            job.AddReservation(materialId, reservationId);
        }

        return StartPrepared(inventory, now);
    }

    internal bool MatchesActivePreparation(
        ProductionJobId jobId,
        EventGeneration generation) =>
        ActiveJob is
        {
            Status: ProductionJobStatus.WaitingForInputs,
        } job
        && job.Id == jobId
        && job.Generation == generation;

    internal Quantity MissingInput(Inventory inventory, MaterialId materialId)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveJob is not
            {
                Status: ProductionJobStatus.WaitingForInputs,
            } job
            || !job.Recipe.Inputs.TryGetValue(materialId, out Quantity required))
        {
            return Quantity.Zero;
        }

        return required.Subtract(job.ReservedInput(inventory, materialId));
    }

    internal void GrantInputReservation(
        IdSequence<ReservationId> reservationIds,
        Inventory inventory,
        MaterialId materialId,
        Quantity quantity)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveJob is not
            {
                Status: ProductionJobStatus.WaitingForInputs,
            } job)
        {
            throw new InvalidOperationException(
                $"Production facility {FacilityId} has no job waiting for inputs.");
        }

        Quantity missing = MissingInput(inventory, materialId);
        if (quantity == Quantity.Zero || quantity > missing)
        {
            throw new InvalidOperationException(
                $"Production job {job.Id} cannot reserve {quantity.Units} units of material {materialId}; {missing.Units} units are missing.");
        }

        ReservationId reservationId = reservationIds.Allocate();
        inventory.Reserve(
            reservationId,
            materialId,
            quantity,
            new ReservationOwner.ProductionJob(job.Id));
        job.AddReservation(materialId, reservationId);
    }

    internal SimulationTime? StartPrepared(
        Inventory inventory,
        SimulationTime now)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveJob is not
            {
                Status: ProductionJobStatus.WaitingForInputs,
            } job)
        {
            return null;
        }

        bool allReserved = job.Recipe.Inputs.All(input =>
            job.ReservedInput(inventory, input.Key) == input.Value);
        if (!allReserved)
        {
            return null;
        }

        inventory.ConsumeReservations(
            job.AllReservationIds(),
            new ReservationOwner.ProductionJob(job.Id));
        job.ClearReservations();

        SimulationTime completesAt = now.Add(_throughput.DurationFor(job.Recipe.RequiredWork));
        job.Status = ProductionJobStatus.Running;
        job.CompletesAt = completesAt;
        return completesAt;
    }

    public bool CompleteActive(
        ProductionIdSequences ids,
        Inventory inventory,
        SimulationTime now)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveJob is not { } job)
        {
            return false;
        }

        if (job.Status == ProductionJobStatus.Running
            && job.CompletesAt is { } completesAt
            && now >= completesAt)
        {
            job.Status = ProductionJobStatus.CompletedAwaitingStorage;
        }
        else if (job.Status != ProductionJobStatus.CompletedAwaitingStorage)
        {
            return false;
        }

        if (!inventory.TryAdd(job.Recipe.OutputMaterial, job.Recipe.OutputQuantity))
        {
            return false;
        }

        job.Status = ProductionJobStatus.Completed;
        ActiveJob = null;
        if (job.IsRepeating)
        {
            ProductionJob repeated = AllocateJob(ids, job.Recipe, repeat: true);
            _jobs.Add(repeated.Id, repeated);
            _queued.Enqueue(repeated);
        }

        PromoteQueuedJob();
        return true;
    }

    public ScheduledEventDisposition CompleteScheduled(
        ProductionJobId jobId,
        EventGeneration generation,
        ProductionIdSequences ids,
        Inventory inventory,
        SimulationTime now,
        out bool outputStored)
    {
        outputStored = false;
        if (GetJob(jobId) is not { } job)
        {
            return ScheduledEventDisposition.IgnoredMissingReference;
        }

        if (job.Generation != generation)
        {
            return ScheduledEventDisposition.IgnoredStaleGeneration;
        }

        if (!ReferenceEquals(ActiveJob, job)
            || job.Status != ProductionJobStatus.Running
            || job.CompletesAt != now)
        {
            return ScheduledEventDisposition.IgnoredStateMismatch;
        }

        outputStored = CompleteActive(ids, inventory, now);
        return ScheduledEventDisposition.Applied;
    }

    public bool CancelActive(Inventory inventory)
    {
        RequireConfiguredInventory(inventory);
        if (ActiveJob is not { } job)
        {
            return false;
        }

        EventGeneration nextGeneration = job.Generation.Next();
        foreach (ReservationId reservationId in job.AllReservationIds())
        {
            if (inventory.GetReservation(reservationId) is not null)
            {
                inventory.Release(reservationId);
            }
        }

        job.ClearReservations();
        job.Generation = nextGeneration;
        job.Status = ProductionJobStatus.Cancelled;
        job.CompletesAt = null;
        ActiveJob = null;
        PromoteQueuedJob();
        return true;
    }

    private static ProductionJob AllocateJob(
        ProductionIdSequences ids,
        Recipe recipe,
        bool repeat) =>
        new(ids.AllocateJob(), recipe, repeat);

    private void PromoteQueuedJob()
    {
        if (ActiveJob is null && _queued.TryDequeue(out ProductionJob? next))
        {
            ActiveJob = next;
        }
    }

    private void RequireConfiguredInventory(Inventory inventory)
    {
        if (inventory.Id != InventoryId)
        {
            throw new ArgumentException(
                $"Production line expected inventory {InventoryId}, but received {inventory.Id}.",
                nameof(inventory));
        }
    }
}
