namespace GalaxyCommand.Simulation.Acceptance;

/// <summary>
/// Bounded acceptance harness for the integrated three-location Phase 1 fixture.
/// </summary>
public sealed class PhaseOneScenario
{
    private readonly PhaseOneRuntime _runtime;

    public PhaseOneScenario(PhaseOneConfig? config = null)
    {
        _runtime = new PhaseOneRuntime(config);
    }

    public SimulationWorld World => _runtime.World;

    public IReadOnlyList<ScenarioEventRecord> EventRecords => _runtime.EventRecords;

    public IReadOnlyList<DecisionRecord> DecisionRecords => _runtime.DecisionRecords;

    public PhaseOneSnapshot CaptureSnapshot() => _runtime.CaptureSnapshot();

    public void ScheduleApprovedRouteDisruption() =>
        _runtime.ScheduleApprovedRouteDisruption();

    public PhaseOneReport RunUntilFirstShip(SimulationTime target) =>
        _runtime.RunUntilFirstShip(target);
}
