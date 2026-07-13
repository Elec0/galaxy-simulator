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

    public EventGeneration Generation { get; } = new(0);

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

    internal void ClearReservations() => _reservationIds.Clear();
}

public sealed class ProductionIdSequences
{
    private readonly IdSequence<ProductionJobId> _jobs = new();

    internal ProductionJobId AllocateJob() => _jobs.Allocate();
}

/// <summary>
/// One production capability with a shared inventory and FIFO job queue.
/// </summary>
public sealed class ProductionLine
{
    private readonly Throughput _throughput;
    private readonly Queue<ProductionJob> _queued = new();

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

    public ProductionJob? ActiveJob { get; private set; }

    public int QueuedJobCount => _queued.Count;

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
            _queued.Enqueue(AllocateJob(ids, job.Recipe, repeat: true));
        }

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
