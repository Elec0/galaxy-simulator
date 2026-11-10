using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public enum StationModulePhase { UnderConstruction, Operational, Unfinished, Dismantling }

/// <summary>Diagnostic module view; project progress and cargo remain in the owning station's private view.</summary>
public sealed record StationModuleSnapshot(StationModuleId Id, QualifiedContentKey DefinitionKey,
    StationModulePhase Phase, bool Enabled, ulong OccupiedCapacity, StationProjectId? ActiveProject,
    QualifiedContentKey? UnfinishedUpgrade, FacilityId? FacilityId = null);

/// <summary>Claims expansion room for an explicit authored module without activating capabilities.</summary>
public sealed record ConstructStationModuleCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, QualifiedContentKey DefinitionKey) : GameplayCommand("construct-station-module");

/// <summary>Starts an explicit authored upgrade while retaining the original module capability.</summary>
public sealed record UpgradeStationModuleCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationModuleId Module, QualifiedContentKey UpgradeTarget)
    : GameplayCommand("upgrade-station-module");

/// <summary>Reserves recovery room before timed removal of a module or its cancelled upgrade addition.</summary>
public sealed record DismantleStationModuleCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationModuleId Module, InventoryId RecoveryInventory,
    bool UnfinishedUpgradeAddition = false) : GameplayCommand("dismantle-station-module");

/// <summary>Controls operational module services independently of retained physical storage.</summary>
public sealed record SetStationModuleEnabledCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationModuleId Module, bool Enabled) : GameplayCommand("set-station-module-enabled");

/// <summary>Assigns one eligible construction module to one project on its own station.</summary>
public sealed record AssignStationModuleBuilderCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationProjectId Project, StationModuleId BuilderModule)
    : GameplayCommand("assign-station-module-builder");

/// <summary>Releases one explicit module assignment without selecting replacement work.</summary>
public sealed record ReleaseStationModuleBuilderCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationProjectId Project, StationModuleId BuilderModule)
    : GameplayCommand("release-station-module-builder");

/// <summary>Replaces a dismantling recovery destination with a newly reserved live inventory.</summary>
public sealed record SetStationRecoveryDestinationCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationProjectId Project, InventoryId RecoveryInventory)
    : GameplayCommand("set-station-recovery-destination");

/// <summary>Exterior module changes reveal neither material quantities nor project progress.</summary>
public sealed record StationModuleChangedFact(StationId StationId, StationModuleId ModuleId,
    QualifiedContentKey DefinitionKey, StationModulePhase Phase, bool Enabled) : GameFact;

/// <summary>Records removal of an attached module without exposing recovery quantities.</summary>
public sealed record StationModuleRemovedFact(StationId StationId, StationModuleId ModuleId) : GameFact;
