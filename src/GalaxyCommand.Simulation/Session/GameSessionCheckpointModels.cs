using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record GameSessionRuntimeCheckpoint(
    SimulationEngineCheckpoint<GameEvent> Engine,
    RuntimePolicyManifestCheckpoint RuntimePolicies,
    WorldTopologyCheckpoint WorldTopology,
    SpatialMovementCheckpoint Movement,
    ActorControlRegistryCheckpoint Control,
    ShipOrderCoordinatorCheckpoint Orders,
    EntityLifecycleCheckpoint Lifecycle,
    RelationshipCheckpoint Relationships,
    SessionEconomyCheckpoint? Economy,
    InventoryCommitOwnerCheckpoint? InventoryCommit = null);

internal sealed record GameSessionCheckpoint(
    SimulationEngineCheckpoint<GameEvent> Engine,
    RuntimePolicyManifestCheckpoint RuntimePolicies,
    WorldTopologyCheckpoint WorldTopology,
    SpatialMovementCheckpoint Movement,
    ActorControlRegistryCheckpoint Control,
    ShipOrderCoordinatorCheckpoint Orders,
    EntityLifecycleCheckpoint Lifecycle,
    RelationshipCheckpoint Relationships,
    SessionEconomyCheckpoint? Economy,
    DeterministicRandomCheckpoint? Random,
    GameFactStoreCheckpoint Facts,
    CommandAdmissionCheckpoint CommandAdmission,
    InventoryCommitOwnerCheckpoint? InventoryCommit = null);

