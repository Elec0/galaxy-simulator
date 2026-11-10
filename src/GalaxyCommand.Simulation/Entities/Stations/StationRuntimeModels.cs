using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Explicit absence of territory policy for scenarios that do not implement that domain.</summary>
public enum StationTerritoryPolicy { Absent }

/// <summary>Explicit local command attribution for one principal; no ownership is inferred from source spelling.</summary>
public sealed record StationCommandAuthority(CommandSource Source, PrincipalId Principal);

/// <summary>Resolved station inputs; authored definitions and material identity remain immutable.</summary>
public sealed class StationRuntimeSetup
{
    /// <summary>Requires explicit territory absence; no territorial permission is inferred from geometry.</summary>
    public StationRuntimeSetup(StationDefinitionCatalog definitions, MaterialInventoryCompatibilityMap materials,
        StationTerritoryPolicy territoryPolicy, IEnumerable<StationCommandAuthority>? commandAuthorities = null,
        IEnumerable<InitialStationSetup>? initialStations = null, StationStorageLossPolicy? storageLossPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(materials);
        if (!Enum.IsDefined(territoryPolicy)) throw new ArgumentOutOfRangeException(nameof(territoryPolicy));
        Definitions = definitions;
        Materials = materials;
        TerritoryPolicy = territoryPolicy;
        StorageLossPolicy = storageLossPolicy;
        StationCommandAuthority[] authorities = (commandAuthorities ?? []).ToArray();
        foreach (StationCommandAuthority authority in authorities)
        {
            ArgumentNullException.ThrowIfNull(authority);
            ArgumentNullException.ThrowIfNull(authority.Source);
            ArgumentOutOfRangeException.ThrowIfZero(authority.Principal.Value);
        }
        if (authorities.Select(authority => authority.Source).Distinct().Count() != authorities.Length)
            throw new ArgumentException("Station command sources require one explicit principal binding.", nameof(commandAuthorities));
        CommandAuthorities = Array.AsReadOnly(authorities.OrderBy(authority => authority.Source.Kind)
            .ThenBy(authority => authority.Source.Id.Value, StringComparer.Ordinal).ToArray());
        InitialStations = Array.AsReadOnly((initialStations ?? []).OrderBy(station => station.Id.Value).ToArray());
    }

    public StationDefinitionCatalog Definitions { get; }
    public MaterialInventoryCompatibilityMap Materials { get; }
    public StationTerritoryPolicy TerritoryPolicy { get; }
    public IReadOnlyList<StationCommandAuthority> CommandAuthorities { get; }
    public IReadOnlyList<InitialStationSetup> InitialStations { get; }
    public StationStorageLossPolicy? StorageLossPolicy { get; }

    /// <summary>Retains the session's explicit player attribution, rejecting a conflicting authored binding.</summary>
    internal StationRuntimeSetup BindPlayer(CommandSource source, PrincipalId principal)
    {
        if (CommandAuthorities.FirstOrDefault(authority => authority.Source == source) is { } existing)
        {
            if (existing.Principal != principal) throw new ArgumentException("Station player authority conflicts with session setup.");
            return this;
        }
        return new(Definitions, Materials, TerritoryPolicy, CommandAuthorities.Append(new(source, principal)), InitialStations, StorageLossPolicy);
    }
}

public enum StationPhase { Site, Operational, Dismantling }
public enum StationProjectKind { BaseConstruction, ModuleConstruction, Upgrade, Dismantling }
public enum StationProjectStatus { Waiting, Working, Paused, Cancelled, Completed }

/// <summary>Authoritative diagnostic project view. Progress and inputs are private in player presentation.</summary>
public sealed record StationProjectSnapshot(StationProjectId Id, StationProjectKind Kind,
    StationProjectStatus Status, Work CompletedWork, Work RequiredWork,
    IReadOnlyDictionary<MaterialId, Quantity> ConsumedInputs, IReadOnlyList<ShipId> AssignedShips,
    StationModuleId? ModuleId = null, QualifiedContentKey? UpgradeTarget = null,
    IReadOnlyList<StationModuleId>? AssignedModules = null, QualifiedContentKey? ModuleDefinitionKey = null, FacilityId? ProviderFacilityId = null);

/// <summary>Authoritative diagnostic base view; observer-safe projection is a separate boundary.</summary>
public sealed record StationSnapshot(StationId Id, EntityId EntityId, InventoryId InventoryId,
    PrincipalId PrincipalId, QualifiedContentKey DefinitionKey, SystemPosition Position,
    StationPhase Phase, ulong Revision, ulong SensorRadiusUnits, IReadOnlyList<StationProjectSnapshot> Projects)
{
    public IReadOnlyList<StationModuleSnapshot> Modules { get; init; } = [];
}

/// <summary>Requests an empty site at an explicit local position using an eligible stationary builder.</summary>
public sealed record CreateStationSiteCommand(PrincipalId RequestingPrincipal, ShipId BuilderShip,
    QualifiedContentKey DefinitionKey, SystemPosition Position, ActorControlRevision ExpectedControlRevision)
    : GameplayCommand("create-station-site");

/// <summary>Assigns one explicitly controlled ship to one named station project.</summary>
public sealed record AssignStationBuilderCommand(PrincipalId RequestingPrincipal, StationId Station,
    StationProjectId Project, ulong ExpectedStationRevision, ShipId BuilderShip,
    ActorControlRevision ExpectedControlRevision) : GameplayCommand("assign-station-builder");

public enum StationProjectAction { Pause, Resume, Cancel, RestartBaseConstruction }

/// <summary>Controls one revision-qualified project without implicit refunds or builder reassignment.</summary>
public sealed record ControlStationProjectCommand(PrincipalId RequestingPrincipal, StationId Station,
    StationProjectId Project, ulong ExpectedStationRevision, StationProjectAction Action)
    : GameplayCommand("control-station-project");

/// <summary>Releases an explicitly controlled construction ship from its named project.</summary>
public sealed record ReleaseStationBuilderCommand(PrincipalId RequestingPrincipal, StationId Station,
    StationProjectId Project, ulong ExpectedStationRevision, ShipId BuilderShip,
    ActorControlRevision ExpectedControlRevision) : GameplayCommand("release-station-builder");

/// <summary>Owner-private project transition; exterior projections do not disclose its reason or progress.</summary>
public sealed record StationProjectChangedFact(StationId StationId, StationProjectId ProjectId,
    StationProjectStatus Status) : GameFact;

/// <summary>Starts final base dismantling only after attached modules, cargo and external commitments are cleared.</summary>
public sealed record DismantleStationBaseCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, InventoryId RecoveryInventory) : GameplayCommand("dismantle-station-base");

public enum StationRemovalReason { Dismantled, Destroyed }

/// <summary>Retains authoritative pre-removal identity for safe exterior disappearance projection.</summary>
public sealed record StationRemovedFact(StationSnapshot Station, StationRemovalReason Reason) : GameFact;

/// <summary>Creation fact carries exterior identity without private inventory quantities.</summary>
public sealed record StationCreatedFact(StationId StationId, EntityId EntityId, PrincipalId PrincipalId,
    QualifiedContentKey DefinitionKey, SystemPosition Position) : GameFact;

/// <summary>Completion preserves the station and inventory identities allocated for the site.</summary>
public sealed record StationProjectCompletedFact(StationId StationId, StationProjectId ProjectId) : GameFact;
