namespace GalaxyCommand.Simulation;

/// <summary>Successful atomic deployment, retaining full authoritative actor and source-command correlation.</summary>
public sealed record SensorDeployedFact(DeployedSensorSnapshot Sensor, ShipId ActingShipId,
    CommandSequence SourceCommand) : GameFact;

/// <summary>Successful atomic pickup, referring to the removed entity and its original definition.</summary>
public sealed record SensorPickedUpFact(DeployedSensorSnapshot Sensor, ShipId ActingShipId,
    CommandSequence SourceCommand) : GameFact;

/// <summary>Explicit lifecycle removal, distinct from a successful inventory pickup.</summary>
public sealed record SensorRemovedFact(DeployedSensorSnapshot Sensor, EntityRemovalReason Reason) : GameFact;
