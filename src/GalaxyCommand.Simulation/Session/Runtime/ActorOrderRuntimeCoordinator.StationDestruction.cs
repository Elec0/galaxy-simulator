namespace GalaxyCommand.Simulation;

/// <summary>Explicit absence of economic workflow authority, distinct from an economic preparation rejection.</summary>
internal sealed class EmptyStationWorkflowLoss : IPreparedStationWorkflowLoss
{
    public IReadOnlyList<InventoryCommitmentRelease> Releases => [];
    public bool CanCommit(PreparedCargoSourceLoss loss) => loss.CanApply();
    public void ApplyAfterSourceLoss(PreparedCargoSourceLoss loss)
    {
        if (!loss.WasApplied) throw new InvalidOperationException("Station source loss has not committed.");
    }
}

/// <summary>Joins exact economic completion cancellation to the same prepared station source-loss transaction.</summary>
internal sealed class PreparedStationWorkflowAgendaLoss(EventAgenda<GameEvent> agenda,
    PreparedStationEconomyLoss effect, IReadOnlyList<StationProviderCancellation> cancellations) : IPreparedStationWorkflowLoss
{
    public IReadOnlyList<InventoryCommitmentRelease> Releases => effect.Releases;

    public bool CanCommit(PreparedCargoSourceLoss loss) => effect.CanCommit(loss) && CanCancel();

    /// <summary>Withdraws only the envelopes proven before the cargo owner applied its canonical inventory loss.</summary>
    public void ApplyAfterSourceLoss(PreparedCargoSourceLoss loss)
    {
        if (!loss.WasApplied || !CanCancel()) throw new InvalidOperationException("Prepared station economic agenda changed during commit.");
        foreach (StationProviderCancellation cancellation in cancellations)
            if (!agenda.TryCancelExact(cancellation.Key, cancellation.Event.Generation, new GameEvent.Economic(cancellation.Event.Event)))
                throw new InvalidOperationException("Prepared economic cancellation disappeared during station loss.");
        effect.ApplyAfterSourceLoss(loss);
    }

    private bool CanCancel() => cancellations.All(cancellation => agenda.CheckCancellation(cancellation.Key,
        cancellation.Event.Generation, new GameEvent.Economic(cancellation.Event.Event)) == AgendaCancellationCheck.Matches);
}

internal sealed partial class ActorOrderRuntimeCoordinator
{
    /// <summary>Commits one causal station loss and reevaluates surviving work only after all participating owners finish.</summary>
    internal StationDestructionResult CommitStationDestruction(StationDestructionRequest request)
    {
        ThrowIfUnhealthy();
        if (_stations is null) return new(false, StationDestructionFailure.PolicyUnavailable);
        try
        {
            StationDestructionResult result = _stations.CommitDestruction(request);
            if (result.WasApplied) _stations.Reconcile(CurrentTime);
            return result;
        }
        catch { _isPoisoned = true; throw; }
    }
}
