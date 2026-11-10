using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public enum StationServiceType { Storage, Sensors, Production, Construction, Trade, Repair, Defense, Refitting }

/// <summary>Exterior service category and current provider-backed availability, without workload or capacity disclosure.</summary>
public sealed record StationServiceDescriptor(StationServiceType Type, bool IsAvailable);

/// <summary>Opaque visible module target and exterior condition, excluding its projects and unfinished upgrade details.</summary>
public sealed record StationModuleExteriorSnapshot(StationModuleId Handle, QualifiedContentKey DefinitionKey,
    StationModulePhase Phase, bool Enabled);

/// <summary>Approved station exterior captured at one committed boundary; no cargo, progress or throughput is present.</summary>
public sealed record StationExteriorViewSnapshot(StationId Id, EntityId EntityId, QualifiedContentKey DefinitionKey,
    string Name, PrincipalId PrincipalId, SystemPosition Position, StationPhase Phase, SimulationTime ObservedAt,
    IReadOnlyList<StationModuleExteriorSnapshot> Modules, IReadOnlyList<StationServiceDescriptor> Services);

/// <summary>Committed station source using the greatest active base or module radius.</summary>
public sealed record StationSensorSourceSnapshot(StationId Id, EntityId EntityId, PrincipalId PrincipalId,
    SystemPosition Position, ulong RadiusUnits);

/// <summary>Immutable station handoff for the observation owner, without writable station authority.</summary>
public sealed record StationObservationSnapshot(SimulationTime Timestamp,
    IReadOnlyList<StationExteriorViewSnapshot> Stations, IReadOnlyList<StationSensorSourceSnapshot> Sources);

/// <summary>Last observed exterior evidence; missing records retain their observation time without inferring a removal cause.</summary>
public sealed record ObservedStationSnapshot(StationExteriorViewSnapshot View, SensorObservationState State);

public enum ObservedStationChangeKind { Appeared, Changed, Disappeared }

/// <summary>Covered exterior event without project identity, inventory, cause or removal reason.</summary>
public sealed record ObservedStationChangeFact(StationId Id, EntityId EntityId, QualifiedContentKey DefinitionKey,
    PrincipalId PrincipalId, SystemPosition Position, ObservedStationChangeKind Kind) : GameFact;
