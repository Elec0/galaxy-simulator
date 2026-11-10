using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Explicit pre-existing operational module, optionally linked to one economic facility.</summary>
public sealed record InitialStationModuleSetup
{
    /// <summary>Copies a nonzero module identity and authored definition; facility identity is explicit when present.</summary>
    public InitialStationModuleSetup(StationModuleId id, QualifiedContentKey definitionKey, bool enabled, FacilityId? facilityId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentNullException.ThrowIfNull(definitionKey);
        if (facilityId is { Value: 0 }) throw new ArgumentOutOfRangeException(nameof(facilityId));
        Id = id;
        DefinitionKey = definitionKey;
        Enabled = enabled;
        FacilityId = facilityId;
    }

    public StationModuleId Id { get; }
    public QualifiedContentKey DefinitionKey { get; }
    public bool Enabled { get; }
    public FacilityId? FacilityId { get; }
}

/// <summary>Authored operational station origin. It supplies no fabricated construction work or consumed-input history.</summary>
public sealed class InitialStationSetup
{
    /// <summary>Copies explicit identity, ownership, position and pre-existing modules; aggregate admission validates references.</summary>
    public InitialStationSetup(EntityId entityId, StationId id, InventoryId inventoryId, PrincipalId principalId,
        QualifiedContentKey definitionKey, SystemPosition position, IEnumerable<InitialStationModuleSetup> modules,
        SimulationTime createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(inventoryId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(principalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        ArgumentNullException.ThrowIfNull(definitionKey);
        ArgumentNullException.ThrowIfNull(modules);
        InitialStationModuleSetup[] values = modules.ToArray();
        foreach (InitialStationModuleSetup module in values) ArgumentNullException.ThrowIfNull(module);
        if (values.Select(module => module.Id).Distinct().Count() != values.Length)
            throw new ArgumentException("Initial module identities must be unique.", nameof(modules));
        EntityId = entityId;
        Id = id;
        InventoryId = inventoryId;
        PrincipalId = principalId;
        DefinitionKey = definitionKey;
        Position = position;
        Modules = Array.AsReadOnly(values.OrderBy(module => module.Id.Value).ToArray());
        CreatedAt = createdAt;
    }

    public EntityId EntityId { get; }
    public StationId Id { get; }
    public InventoryId InventoryId { get; }
    public PrincipalId PrincipalId { get; }
    public QualifiedContentKey DefinitionKey { get; }
    public SystemPosition Position { get; }
    public IReadOnlyList<InitialStationModuleSetup> Modules { get; }
    public SimulationTime CreatedAt { get; }
}
