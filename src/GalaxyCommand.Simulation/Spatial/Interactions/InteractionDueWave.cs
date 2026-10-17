namespace GalaxyCommand.Simulation;

/// <summary>Buffered correlation disposition for one due agenda envelope.</summary>
public sealed record InteractionDueResult(ScheduledEvent<InteractionForecast> Event, ScheduledEventDisposition Disposition);

/// <summary>Immutable shared spatial input and canonically ordered results awaiting owner consumption.</summary>
public sealed class InteractionDueWave
{
    internal InteractionDueWave(SpatialInteractionCoordinator owner, InteractionSpatialReadView view,
        IReadOnlyList<InteractionDueResult> results) { Owner = owner; View = view; Results = results; }
    internal SpatialInteractionCoordinator Owner { get; }
    public InteractionSpatialReadView View { get; }
    public IReadOnlyList<InteractionDueResult> Results { get; }
}
