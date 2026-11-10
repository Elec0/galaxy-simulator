namespace GalaxyCommand.Simulation;

/// <summary>Opt-in pure evaluation and serial commit for contiguous physical envelopes from one domain.</summary>
public interface ISimulationPhysicalCompletionWaveRuntime<TEvent>
{
    /// <summary>Enables the optional wave path only when its owning domain is present.</summary>
    bool HasPhysicalCompletionWaves { get; }

    /// <summary>Classifies an envelope without mutation. Unselected envelopes remain exact dispatch and cancellation barriers.</summary>
    bool IsPhysicalCompletionWaveEvent(ScheduledEvent<TEvent> scheduled);

    /// <summary>
    /// Captures stable inputs for one contiguous group and commits deterministic owner effects.
    /// Returns dispositions in original envelope order; each envelope retains its own causal key.
    /// Same-phase work created during commit joins a later wave before the phase closes.
    /// </summary>
    IReadOnlyList<ScheduledEventDisposition> HandlePhysicalCompletionWave(
        IReadOnlyList<ScheduledEvent<TEvent>> events, SimulationTime now, EventAgenda<TEvent> agenda);
}
