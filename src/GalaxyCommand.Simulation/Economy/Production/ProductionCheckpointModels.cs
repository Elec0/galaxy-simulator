using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record ProductionRecipeCheckpoint(
    IReadOnlyList<ConstructionInputPolicyCheckpoint?> Inputs,
    MaterialId OutputMaterial,
    Quantity OutputQuantity,
    Work RequiredWork);

internal sealed record ProductionReservationLinkCheckpoint(
    MaterialId MaterialId,
    ReservationId ReservationId);

internal sealed record ProductionJobCheckpoint(
    ProductionJobId Id,
    ProductionRecipeCheckpoint? Recipe,
    bool IsRepeating,
    ProductionJobStatus Status,
    SimulationTime? CompletesAt,
    EventGeneration Generation,
    IReadOnlyList<ProductionReservationLinkCheckpoint?> Reservations);

internal sealed record ProductionLineCheckpoint(
    FacilityId FacilityId,
    InventoryId InventoryId,
    Throughput Throughput,
    ProductionJobId? ActiveJobId,
    IReadOnlyList<ProductionJobId> QueuedJobIds,
    IReadOnlyList<ProductionJobCheckpoint?> Jobs);

internal sealed record ProductionOwnerCheckpoint(
    IdSequenceCheckpoint JobIds,
    IReadOnlyList<ProductionLineCheckpoint?> Lines);

