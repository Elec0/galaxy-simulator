namespace GalaxyCommand.Simulation;

/// <summary>
/// Ship-specific construction site. The contained construction process owns
/// the reusable queue, reservation, work, and completion lifecycle.
/// </summary>
public sealed class Shipyard
{
    private readonly ConstructionProcess _construction;
    private readonly SortedDictionary<ConstructionOrderId, ShipId> _constructedShips =
        new(EntityIdComparer<ConstructionOrderId>.Instance);

    public Shipyard(
        FacilityId facilityId,
        OrganizationId organizationId,
        LocationId locationId,
        InventoryId inventoryId,
        Throughput throughput)
    {
        OrganizationId = organizationId;
        LocationId = locationId;
        _construction = new ConstructionProcess(facilityId, inventoryId, throughput);
    }

    public FacilityId FacilityId => _construction.FacilityId;

    public OrganizationId OrganizationId { get; }

    public LocationId LocationId { get; }

    public InventoryId InventoryId => _construction.InventoryId;

    public ConstructionOrder? ActiveOrder => _construction.ActiveOrder;

    public int QueuedOrderCount => _construction.QueuedOrderCount;

    public ConstructionOrder? GetCompletedOrder(ConstructionOrderId orderId) =>
        _construction.GetCompletedOrder(orderId);

    public ShipId? GetConstructedShipId(ConstructionOrderId orderId) =>
        _constructedShips.GetValueOrDefault(orderId);

    public IReadOnlyDictionary<MaterialId, Quantity> UnmetInputs(Inventory inventory) =>
        _construction.UnmetInputs(inventory);

    public ConstructionOrderId Enqueue(
        ConstructionIdSequences ids,
        ShipDesign design) =>
        _construction.Enqueue(ids, design);

    public SimulationTime? PrepareActive(
        IdSequence<ReservationId> reservationIds,
        Inventory inventory,
        SimulationTime now) =>
        _construction.PrepareActive(reservationIds, inventory, now);

    public ShipId? CompleteActive(
        IdSequence<ShipId> shipIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        ShipRegistry ships,
        SimulationTime now)
    {
        ShipId? constructedShipId = null;
        ConstructionOrder? completed = _construction.CompleteActive(
            now,
            order =>
            {
                if (order.Design is not ShipDesign design)
                {
                    throw new InvalidOperationException(
                        $"Shipyard cannot materialize construction design {order.Design.Id}.");
                }

                ShipId shipId = shipIds.Allocate();
                InventoryId cargoInventoryId = inventoryIds.Allocate();
                inventories.Add(new Inventory(cargoInventoryId, design.CargoCapacity));
                ships.AddFreighter(new Ship(
                    shipId,
                    OrganizationId,
                    design.Id,
                    LocationId,
                    cargoInventoryId));
                _constructedShips.Add(order.Id, shipId);
                constructedShipId = shipId;
            });

        return completed is null
            ? null
            : constructedShipId
                ?? throw new InvalidOperationException(
                    $"Construction order {completed.Id} completed without creating a ship.");
    }
}
