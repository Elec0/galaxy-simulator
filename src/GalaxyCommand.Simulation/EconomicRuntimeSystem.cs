namespace GalaxyCommand.Simulation;

/// <summary>
/// Closed event vocabulary handled by the reusable economic runtime system.
/// Outer runtimes may wrap these events alongside their own event types.
/// </summary>
public abstract record EconomicEvent
{
    private EconomicEvent()
    {
    }

    public sealed record Transport(TransportEvent Event) : EconomicEvent;

    public sealed record ProductionComplete(
        FacilityId FacilityId,
        ProductionJobId JobId) : EconomicEvent;

    public sealed record ConstructionComplete(
        FacilityId FacilityId,
        ConstructionOrderId OrderId) : EconomicEvent;
}

/// <summary>
/// Typed result of committing one economic event.
/// </summary>
public abstract record EconomicEventCommitResult
{
    private protected EconomicEventCommitResult(
        ScheduledEventDisposition disposition)
    {
        Disposition = disposition;
    }

    public ScheduledEventDisposition Disposition { get; }

    public sealed record Transport(
        TransportEventReconciliationResult Result)
        : EconomicEventCommitResult(Result.Disposition);

    public sealed record Production(
        ProductionCompletionCommitResult Result)
        : EconomicEventCommitResult(Result.Disposition);

    public sealed record Construction(
        ConstructionCompletionCommitResult Result)
        : EconomicEventCommitResult(Result.Disposition);
}

/// <summary>
/// Fixed reusable composition for economic reconciliation and scheduled-event
/// dispatch. Acceptance and persistent runtimes provide only outer event
/// wrapping, timing, and product-lifecycle adapters.
/// </summary>
public sealed class EconomicRuntimeSystem
{
    private readonly EconomicRuntimeCoordinator _coordinator;

    public EconomicRuntimeSystem(EconomicRuntimeCoordinator coordinator)
    {
        _coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public EconomicReconciliationResult Reconcile(
        SimulationTime now,
        TransportTiming transportTiming) =>
        _coordinator.Reconcile(now, transportTiming);

    public EconomicEventCommitResult CommitEvent(
        EconomicEvent economicEvent,
        EventGeneration scheduledGeneration,
        TransportTiming transportTiming,
        SimulationTime now)
    {
        ArgumentNullException.ThrowIfNull(economicEvent);
        return economicEvent switch
        {
            EconomicEvent.Transport transport =>
                CommitTransport(
                    transport.Event,
                    scheduledGeneration,
                    transportTiming,
                    now),
            EconomicEvent.ProductionComplete production =>
                new EconomicEventCommitResult.Production(
                    _coordinator.CommitProductionCompletion(
                        production.FacilityId,
                        production.JobId,
                        scheduledGeneration,
                        now)),
            EconomicEvent.ConstructionComplete construction =>
                new EconomicEventCommitResult.Construction(
                    _coordinator.CommitConstructionCompletion(
                        construction.FacilityId,
                        construction.OrderId,
                        scheduledGeneration,
                        now)),
            _ => throw new ArgumentOutOfRangeException(nameof(economicEvent)),
        };
    }

    private EconomicEventCommitResult.Transport CommitTransport(
        TransportEvent transportEvent,
        EventGeneration scheduledGeneration,
        TransportTiming transportTiming,
        SimulationTime now)
    {
        if (scheduledGeneration != transportEvent.Generation)
        {
            return new EconomicEventCommitResult.Transport(
                new TransportEventReconciliationResult(
                    ScheduledEventDisposition.IgnoredStateMismatch,
                    new TransportAdvanceCommitResult([], 0)));
        }

        return new EconomicEventCommitResult.Transport(
            _coordinator.HandleTransportEvent(
                transportEvent,
                transportTiming,
                now));
    }
}
