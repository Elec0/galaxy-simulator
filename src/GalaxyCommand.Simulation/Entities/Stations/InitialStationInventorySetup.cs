namespace GalaxyCommand.Simulation;

/// <summary>Setup-only empty storage owned by an explicit authored station origin.</summary>
public sealed record InitialStationInventorySetup
{
    public InitialStationInventorySetup(InventoryId id, EntityId stationEntityId,
        PrincipalId principalId, Quantity capacity)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(stationEntityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(principalId.Value);
        Id = id;
        StationEntityId = stationEntityId;
        PrincipalId = principalId;
        Capacity = capacity;
    }

    public InventoryId Id { get; }
    public EntityId StationEntityId { get; }
    public PrincipalId PrincipalId { get; }
    public Quantity Capacity { get; }
}
