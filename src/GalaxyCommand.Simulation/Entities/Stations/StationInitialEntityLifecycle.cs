namespace GalaxyCommand.Simulation;

public sealed partial class EntityRegistry
{
    internal bool CanRegisterInitialStation(EntityId entity, StationId station) =>
        GetKind(entity) is null && !_stationsByEntity.ContainsValue(station);
}

internal sealed partial class EntityLifecycleOwner
{
    /// <summary>Preflights an authored initial entity against the existing shared inventory without allocating any identity.</summary>
    internal bool CanRegisterInitialStation(InitialStationSetup station) =>
        _entities.CanRegisterInitialStation(station.EntityId, station.Id)
        && _inventories.Get(station.InventoryId)?.Custody is InventoryCustody.PrincipalControlled custody
        && custody.PhysicalOwner == new InventoryOwnerReference.SessionEntity(station.EntityId)
        && custody.ControllingPrincipalId == station.PrincipalId;

    /// <summary>Publishes only a previously validated initial mapping and advances exact shared high-water marks.</summary>
    internal void ApplyRegisterInitialStation(InitialStationSetup station)
    {
        if (!CanRegisterInitialStation(station))
            throw new InvalidOperationException("Initial station identity changed after setup preflight.");
        _entityIds.AdvancePast(station.EntityId);
        _inventoryIds.AdvancePast(station.InventoryId);
        _entities.ApplyAddStation(station.EntityId, station.Id);
    }
}
