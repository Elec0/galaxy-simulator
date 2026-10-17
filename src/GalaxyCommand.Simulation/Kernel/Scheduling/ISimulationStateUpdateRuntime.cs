namespace GalaxyCommand.Simulation;

/// <summary>Opt-in stable evaluation waves after all same-time physical completions drain.</summary>
public interface ISimulationStateUpdateRuntime<TEvent>
{
    /// <summary>Prepares derived StateUpdate work at the post-completion boundary.</summary>
    void PrepareStateUpdate(SimulationTime now, EventAgenda<TEvent> agenda);

    /// <summary>
    /// Evaluates a complete immutable wave and commits its buffered effects.
    /// Returns one disposition per input envelope in input order. New same-phase
    /// work belongs to a subsequent wave before Decision begins.
    /// </summary>
    IReadOnlyList<ScheduledEventDisposition> HandleStateUpdateWave(
        IReadOnlyList<ScheduledEvent<TEvent>> events, SimulationTime now, EventAgenda<TEvent> agenda);
}
