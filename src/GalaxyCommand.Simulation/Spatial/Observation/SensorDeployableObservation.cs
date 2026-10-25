using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Live committed deployable input for the observation owner, without inventory or command state.</summary>
public sealed record SensorDeployableViewSnapshot(EntityId EntityId, QualifiedContentKey DefinitionKey,
    PrincipalId PrincipalId, SystemPosition Position, SimulationTime ObservedAt);

/// <summary>Committed stationary coverage source, withdrawn immediately when its entity is removed.</summary>
public sealed record DeployedSensorSourceSnapshot(EntityId EntityId, PrincipalId PrincipalId,
    SystemPosition Position, ulong RadiusUnits);

/// <summary>Immutable observation handoff captured from one authoritative boundary.</summary>
public sealed record SensorDeployableObservationSnapshot(SimulationTime Timestamp,
    IReadOnlyList<SensorDeployableViewSnapshot> Deployables, IReadOnlyList<DeployedSensorSourceSnapshot> Sources);
