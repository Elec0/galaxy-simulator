namespace GalaxyCommand.Simulation;

/// <summary>
/// Constructible ship definition. Ship-specific capabilities belong here rather
/// than in the shared construction pipeline.
/// </summary>
public sealed class ShipDesign : ConstructionDesign
{
    public ShipDesign(
        ConstructionDesignId id,
        string name,
        ConstructionRecipe recipe,
        Quantity cargoCapacity)
        : base(id, name, recipe)
    {
        CargoCapacity = cargoCapacity;
    }

    public Quantity CargoCapacity { get; }
}

public sealed record Ship(
    ShipId Id,
    OrganizationId OrganizationId,
    ConstructionDesignId DesignId,
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
        ArgumentNullException.ThrowIfNull(ship);
        AddFreighter(ship.Id, ship.LocationId, ship.CargoInventoryId);
        _ships.Add(ship.Id, ship);
    }

    /// <summary>
    /// Registers logistics capability for a session-owned ship without
    /// importing the acceptance-only organization ship record.
    /// </summary>
    public void AddFreighter(
        ShipId shipId,
        LocationId locationId,
        InventoryId cargoInventoryId)
    {
        if (_freighters.ContainsKey(shipId))
        {
            throw new InvalidOperationException($"Duplicate freighter {shipId}.");
        }

        _freighters.Add(
            shipId,
            new Freighter(shipId, locationId, cargoInventoryId));
    }

    public Ship? GetShip(ShipId shipId) => _ships.GetValueOrDefault(shipId);

    public Freighter? GetFreighter(ShipId shipId) => _freighters.GetValueOrDefault(shipId);

    internal bool RemoveFreighter(ShipId shipId)
    {
        bool removed = _freighters.Remove(shipId);
        _ships.Remove(shipId);
        return removed;
    }
}
