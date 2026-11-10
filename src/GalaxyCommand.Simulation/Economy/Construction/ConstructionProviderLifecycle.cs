namespace GalaxyCommand.Simulation;

internal sealed record ConstructionProviderJobState(ConstructionOrder Order, ConstructionOrderStatus Status,
    SimulationTime? Completion, EventGeneration Generation, EconomicJobWorkState? Work);

internal sealed record PreparedConstructionProviderTransition(ConstructionProcess Owner, ulong Revision, bool Available,
    Throughput Throughput, SimulationTime At, ConstructionProviderJobState? Before, ConstructionProviderJobState? After)
{
    internal bool ReplacesCompletion => Before?.Status == ConstructionOrderStatus.Running && Before != After;
}

public sealed partial class ConstructionProcess
{
    private ulong _providerRevision;

    public bool IsProviderAvailable { get; private set; } = true;

    /// <summary>Prepares exact pause/resume/rate replacement without consuming inputs again or changing queue order.</summary>
    internal PreparedConstructionProviderTransition? PrepareProviderTransition(bool available, Throughput throughput,
        SimulationTime now)
    {
        if (throughput.UnitsPerSecond == 0 || _providerRevision == ulong.MaxValue) return null;
        ConstructionProviderJobState? before = CaptureProviderJob();
        ConstructionProviderJobState? after = before;
        if (before is { Status: ConstructionOrderStatus.Running or ConstructionOrderStatus.Paused }
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
            after = new(before.Order, available ? ConstructionOrderStatus.Running : ConstructionOrderStatus.Paused,
                completion, before.Generation.Next(), work);
        }
        return new(this, _providerRevision, available, throughput, now, before, after);
    }

    /// <summary>Rejects stale order, rate or availability preparation before any authoritative mutation.</summary>
    internal bool CanApplyProviderTransition(PreparedConstructionProviderTransition transition) =>
        ReferenceEquals(transition.Owner, this) && transition.Revision == _providerRevision
        && transition.Before == CaptureProviderJob();

    /// <summary>Applies one revalidated transition and returns only its replacement completion for shared agenda admission.</summary>
    internal ConstructionCompletionProposal? ApplyProviderTransition(PreparedConstructionProviderTransition transition)
    {
        if (!CanApplyProviderTransition(transition)) throw new InvalidOperationException("Construction provider transition changed after preparation.");
        _providerRevision++;
        IsProviderAvailable = transition.Available;
        _throughput = transition.Throughput;
        if (transition.After is not { } after || transition.Before == after) return null;
        after.Order.Status = after.Status;
        after.Order.CompletesAt = after.Completion;
        after.Order.Generation = after.Generation;
        after.Order.WorkState = after.Work;
        return after.Status == ConstructionOrderStatus.Running && after.Completion is { } completion
            ? new(completion, FacilityId, after.Order.Id, after.Generation) : null;
    }

    private ConstructionProviderJobState? CaptureProviderJob() => ActiveOrder is { } order
        ? new(order, order.Status, order.CompletesAt, order.Generation, order.WorkState) : null;
}
