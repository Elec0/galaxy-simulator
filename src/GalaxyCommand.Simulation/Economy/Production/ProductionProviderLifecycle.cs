namespace GalaxyCommand.Simulation;

internal sealed record ProductionProviderJobState(ProductionJob Job, ProductionJobStatus Status,
    SimulationTime? Completion, EventGeneration Generation, EconomicJobWorkState? Work);

internal sealed record PreparedProductionProviderTransition(ProductionLine Owner, ulong Revision, bool Available,
    Throughput Throughput, SimulationTime At, ProductionProviderJobState? Before, ProductionProviderJobState? After)
{
    internal bool ReplacesCompletion => Before?.Status == ProductionJobStatus.Running && Before != After;
}

public sealed partial class ProductionLine
{
    private ulong _providerRevision;

    public bool IsProviderAvailable { get; private set; } = true;

    /// <summary>Prepares exact pause/resume/rate replacement without touching inventory, queues or the shared agenda.</summary>
    internal PreparedProductionProviderTransition? PrepareProviderTransition(bool available, Throughput throughput,
        SimulationTime now)
    {
        if (throughput.UnitsPerSecond == 0 || _providerRevision == ulong.MaxValue) return null;
        ProductionProviderJobState? before = CaptureProviderJob();
        ProductionProviderJobState? after = before;
        if (before is { Status: ProductionJobStatus.Running or ProductionJobStatus.Paused }
            && (available != IsProviderAvailable || throughput != _throughput))
        {
            if (before.Work is null || before.Generation.Value == ulong.MaxValue) return null;
            UInt128 remaining;
            try { remaining = before.Work.RemainingAt(_throughput, now); }
            catch (ArgumentOutOfRangeException) { return null; }
            var work = new EconomicJobWorkState(remaining, available ? now : null);
            SimulationTime? completion;
            try { completion = available ? work.CompletionAt(throughput, now) : null; }
            catch (OverflowException) { return null; }
            after = new(before.Job, available ? ProductionJobStatus.Running : ProductionJobStatus.Paused,
                completion, before.Generation.Next(), work);
        }
        return new(this, _providerRevision, available, throughput, now, before, after);
    }

    /// <summary>Rejects stale job, rate or availability preparation before any authoritative mutation.</summary>
    internal bool CanApplyProviderTransition(PreparedProductionProviderTransition transition) =>
        ReferenceEquals(transition.Owner, this) && transition.Revision == _providerRevision
        && transition.Before == CaptureProviderJob();

    /// <summary>Applies one revalidated transition and returns only its replacement completion for ordered agenda admission.</summary>
    internal ProductionCompletionProposal? ApplyProviderTransition(PreparedProductionProviderTransition transition)
    {
        if (!CanApplyProviderTransition(transition)) throw new InvalidOperationException("Production provider transition changed after preparation.");
        _providerRevision++;
        IsProviderAvailable = transition.Available;
        _throughput = transition.Throughput;
        if (transition.After is not { } after || transition.Before == after) return null;
        after.Job.Status = after.Status;
        after.Job.CompletesAt = after.Completion;
        after.Job.Generation = after.Generation;
        after.Job.WorkState = after.Work;
        return after.Status == ProductionJobStatus.Running && after.Completion is { } completion
            ? new(completion, FacilityId, after.Job.Id, after.Generation) : null;
    }

    private ProductionProviderJobState? CaptureProviderJob() => ActiveJob is { } job
        ? new(job, job.Status, job.CompletesAt, job.Generation, job.WorkState) : null;
}
