using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record ConstructionReservationLinkCheckpoint(
    MaterialId MaterialId,
    ReservationId ReservationId);

internal sealed record ConstructionOrderCheckpoint(
    ConstructionOrderId Id,
    ConstructionDesignId DesignId,
    ConstructionOrderStatus Status,
    SimulationTime? CompletesAt,
    EventGeneration Generation,
    IReadOnlyList<ConstructionReservationLinkCheckpoint?> Reservations);

internal sealed record ConstructionShipIdentityCheckpoint(
    EntityId EntityId,
    ShipId ShipId,
    InventoryId CargoInventoryId);

internal sealed record ConstructionMaterializationReceiptCheckpoint(
    ConstructionMaterializationEffect? Effect,
    ConstructionShipIdentityCheckpoint? ShipIdentity);

internal sealed record ConstructionProcessCheckpoint(
    FacilityId FacilityId,
    InventoryId InventoryId,
    Throughput Throughput,
    ConstructionOrderId? ActiveOrderId,
    IReadOnlyList<ConstructionOrderId> QueuedOrderIds,
    IReadOnlyList<ConstructionOrderCheckpoint?> Orders,
    IReadOnlyList<ConstructionMaterializationEffect?> PendingMaterializations,
    IReadOnlyList<ConstructionMaterializationReceiptCheckpoint?> MaterializationReceipts);

internal sealed record ConstructionOwnerCheckpoint(
    IdSequenceCheckpoint OrderIds,
    IReadOnlyList<ConstructionProcessCheckpoint?> Processes);

