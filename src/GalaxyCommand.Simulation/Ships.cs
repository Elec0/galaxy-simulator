namespace GalaxyCommand.Simulation;

public readonly record struct ShipBlueprint(ShipBlueprintId Id, Quantity CargoCapacity);

public sealed record Ship(
    ShipId Id,
    OrganizationId OrganizationId,
    ShipBlueprintId BlueprintId,
    LocationId LocationId,
    InventoryId CargoInventoryId);

/// <summary>
/// Deterministic ownership of persistent ships and their freighter state.
/// </summary>
public sealed class ShipRegistry
{
    private readonly SortedDictionary<ShipId, Ship> _ships =
        new(EntityIdComparer<ShipId>.Instance);
    private readonly SortedDictionary<ShipId, Freighter> _freighters =
        new(EntityIdComparer<ShipId>.Instance);

    public int Count => _ships.Count;

    public IEnumerable<ShipId> FreighterIds => _freighters.Keys;

    public void AddFreighter(Ship ship)
    {
        if (_ships.ContainsKey(ship.Id))
        {
            throw new InvalidOperationException($"Duplicate ship {ship.Id}.");
        }

        _freighters.Add(
            ship.Id,
            new Freighter(ship.Id, ship.LocationId, ship.CargoInventoryId));
        _ships.Add(ship.Id, ship);
    }

    public Ship? GetShip(ShipId shipId) => _ships.GetValueOrDefault(shipId);

    public Freighter? GetFreighter(ShipId shipId) => _freighters.GetValueOrDefault(shipId);
}
