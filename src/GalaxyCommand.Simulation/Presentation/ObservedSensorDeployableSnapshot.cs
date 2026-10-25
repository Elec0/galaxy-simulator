namespace GalaxyCommand.Simulation;

/// <summary>Current coverage, retained evidence outside coverage, or absence verified at the last known position.</summary>
public enum SensorObservationState { Live, Stale, ConfirmedMissing }

/// <summary>
/// An approved deployable view and its observation state. Missing records retain
/// the last observation time; they expose no inferred removal time or cause.
/// </summary>
public sealed record ObservedSensorDeployableSnapshot(SensorDeployableViewSnapshot View, SensorObservationState State);
