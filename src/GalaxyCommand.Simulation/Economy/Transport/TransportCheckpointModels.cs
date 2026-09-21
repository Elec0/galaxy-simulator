using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record TransportIdSequencesCheckpoint(
    IdSequenceCheckpoint OfferIds,
    IdSequenceCheckpoint DemandIds,
    IdSequenceCheckpoint JobIds);

internal sealed record TransportTimingCheckpoint(
    SimulationDuration DockingOverhead,
    ulong LoadingUnitsPerSecond,
    ulong UnloadingUnitsPerSecond);

internal sealed record TransportSupplyCheckpoint(
    SupplyOfferId Id,
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Remaining);

internal sealed record TransportDemandCheckpoint(
    DemandRequestId Id,
    InventoryId InventoryId,
    LocationId LocationId,
    MaterialId MaterialId,
    Quantity Remaining,
    DemandPriority Priority,
    SimulationTime CreatedAt);

internal sealed record TransportJobCheckpoint(
    TransportJobId Id,
    ShipId ShipId,
    SupplyOfferId SupplyOfferId,
    DemandRequestId DemandRequestId,
    InventoryId SourceInventoryId,
    LocationId SourceLocationId,
    InventoryId DestinationInventoryId,
    LocationId DestinationLocationId,
    MaterialId MaterialId,
    Quantity Quantity,
    ReservationId SourceReservationId,
    CapacityReservationId? DestinationCapacityReservationId,
    SimulationTime AssignedAt,
    EventGeneration Generation,
    TransportJobStatus Status,
    SimulationTime? TransitionAt);

internal sealed record TransportBoardCheckpoint(
    IReadOnlyList<TransportSupplyCheckpoint?> Supplies,
    IReadOnlyList<TransportDemandCheckpoint?> Demands,
    IReadOnlyList<TransportJobCheckpoint?> Jobs);

internal sealed record TransportFreighterCheckpoint(
    ShipId ShipId,
    LocationId LocationId,
    InventoryId CargoInventoryId,
    TransportJobId? ActiveJobId);

internal sealed record TransportOwnerCheckpoint(
    TransportIdSequencesCheckpoint Ids,
    IdSequenceCheckpoint ReservationIds,
    IdSequenceCheckpoint CapacityReservationIds,
    TransportTimingCheckpoint Timing,
    TransportBoardCheckpoint Board,
    IReadOnlyList<TransportFreighterCheckpoint?> Freighters);

