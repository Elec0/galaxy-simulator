using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record ConstructionInputPolicyCheckpoint(
    MaterialId MaterialId,
    Quantity Quantity);

internal sealed record ShipManeuverCapabilityCheckpoint(
    int BehaviorVersion,
    ulong BaseMassKilograms,
    ManeuverAcceleration BaseAcceleration,
    ManeuverAcceleration? CustomPassiveDeceleration,
    ManeuverSpeed MaximumSubCruiseSpeed,
    ManeuverSpeed CruiseSpeed,
    ManeuverTurnRate TurnRate,
    SimulationDuration MovingSpoolDuration);

internal sealed record ShipDesignPolicyCheckpoint(
    ConstructionDesignId Id,
    string? Name,
    IReadOnlyList<ConstructionInputPolicyCheckpoint?> Inputs,
    Work RequiredWork,
    Quantity CargoCapacity,
    ShipManeuverCapabilityCheckpoint? ManeuverCapability,
    ulong? SensorRadiusUnits,
    ulong? PickupRangeUnits = null);

internal sealed record MaterializationPolicyCheckpoint(
    FacilityId FacilityId,
    PrincipalId PrincipalId,
    SystemId SystemId,
    SpatialCoordinate X,
    SpatialCoordinate Y,
    ActorControllerKind BaseControllerKind,
    string? BaseControllerId,
    InitialShipOrderPolicy InitialOrderPolicy,
    IReadOnlyList<ConstructionDesignId> AllowedDesignIds);

internal sealed record SensorDeployableDefinitionCheckpoint(
    QualifiedContentKey? Key, string? Name, Quantity CapacityCost,
    ulong DeploymentRangeUnits, ulong SensorRadiusUnits);

internal sealed record RuntimePolicyManifestCheckpoint(
    NavigationPolicyCheckpoint? Navigation,
    TravelTimePolicyCheckpoint? TravelTime,
    IReadOnlyList<MaterializationPolicyCheckpoint?> MaterializationPolicies,
    int FactRetentionCapacity,
    IReadOnlyList<ShipDesignPolicyCheckpoint?>? ShipDefinitions = null,
    IReadOnlyList<SensorDeployableDefinitionCheckpoint?>? SensorDeployableDefinitions = null);

