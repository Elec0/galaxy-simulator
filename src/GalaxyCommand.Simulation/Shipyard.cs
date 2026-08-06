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

    internal ConstructionProcess Process => _construction;

    public ConstructionOrder? ActiveOrder => _construction.ActiveOrder;

    public int QueuedOrderCount => _construction.QueuedOrderCount;

    public ConstructionOrder? GetCompletedOrder(ConstructionOrderId orderId) =>
        _construction.GetCompletedOrder(orderId);

    public ConstructionMaterializationEffect? GetPendingMaterialization(
        ConstructionOrderId orderId) =>
        _construction.GetPendingMaterialization(orderId);

    public ShipId? GetConstructedShipId(ConstructionOrderId orderId) =>
        _constructedShips.TryGetValue(orderId, out ShipId shipId)
            ? shipId
            : null;

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

    public bool CancelActive(Inventory inventory) =>
        _construction.CancelActive(inventory);

    public ShipId? CompleteActive(
        IdSequence<ShipId> shipIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        ShipRegistry ships,
        SimulationTime now)
    {
        ConstructionMaterializationEffect? materialization =
            _construction.CompleteActive(now);
        return materialization is null
            ? null
            : MaterializeShip(
                materialization,
                shipIds,
                inventoryIds,
                inventories,
                ships);
    }

    public ScheduledEventDisposition CompleteScheduled(
        ConstructionOrderId orderId,
        EventGeneration generation,
        IdSequence<ShipId> shipIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        ShipRegistry ships,
        SimulationTime now,
        out ShipId? constructedShipId)
    {
        ScheduledEventDisposition disposition = _construction.CompleteScheduled(
            orderId,
            generation,
            now,
            null,
            out ConstructionMaterializationEffect? materialization);
        constructedShipId = materialization is null
            ? null
            : MaterializeShip(
                materialization,
                shipIds,
                inventoryIds,
                inventories,
                ships);
        return disposition;
    }

    internal void RecordConstructedShip(
        ConstructionOrderId orderId,
        ShipId shipId)
    {
        if (_constructedShips.TryGetValue(orderId, out ShipId existing))
        {
            if (existing != shipId)
            {
                throw new InvalidOperationException(
                    $"Construction order {orderId} already materialized ship {existing}, not {shipId}.");
            }

            return;
        }

        ConstructionMaterializationEffect materialization =
            GetPendingMaterialization(orderId)
            ?? throw new InvalidOperationException(
                $"Construction order {orderId} has no pending materialization.");
        _constructedShips.Add(orderId, shipId);
        ConstructionMaterializationAcknowledgement acknowledgement =
            _construction.AcknowledgeMaterialization(materialization);
        if (acknowledgement != ConstructionMaterializationAcknowledgement.Applied)
        {
            throw new InvalidOperationException(
                $"Construction order {orderId} acknowledgement returned {acknowledgement}.");
        }
    }

    private ShipId MaterializeShip(
        ConstructionMaterializationEffect materialization,
        IdSequence<ShipId> shipIds,
        IdSequence<InventoryId> inventoryIds,
        InventoryRegistry inventories,
        ShipRegistry ships)
    {
        if (materialization.FacilityId != FacilityId)
        {
            throw new InvalidOperationException(
                $"Shipyard {FacilityId} cannot materialize an effect for facility {materialization.FacilityId}.");
        }

        if (GetConstructedShipId(materialization.OrderId) is { } existing)
        {
            return existing;
        }

        ConstructionOrder order = _construction.GetOrder(materialization.OrderId)
            ?? throw new InvalidOperationException(
                $"Construction order {materialization.OrderId} does not exist.");
        if (GetPendingMaterialization(materialization.OrderId) != materialization)
        {
            throw new InvalidOperationException(
                $"Construction order {materialization.OrderId} has no matching pending materialization.");
        }
        if (order.DesignId != materialization.DesignId
            || order.Design is not ShipDesign design)
        {
            throw new InvalidOperationException(
                $"Shipyard cannot materialize construction design {materialization.DesignId}.");
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
        RecordConstructedShip(order.Id, shipId);
        return shipId;
    }
}
