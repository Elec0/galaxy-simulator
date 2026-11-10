using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record EconomyFacilityCheckpoint(
    FacilityId FacilityId,
    InventoryId InventoryId,
    LocationId LocationId,
    SystemPosition Position,
    MaterialId? ProductionOutput,
    StationFacilityBinding? StationBinding = null, QualifiedContentKey? StationModuleDefinitionKey = null);

internal sealed record SessionEconomyCheckpoint(
    IReadOnlyList<EconomyFacilityCheckpoint?> Facilities,
    ProductionOwnerCheckpoint Production,
    ConstructionOwnerCheckpoint Construction,
    TransportOwnerCheckpoint Transport,
    IReadOnlyList<RetiredEconomyFacilityCheckpoint?>? RetiredFacilities = null,
    IdSequenceCheckpoint? FacilityIds = null,
    IdSequenceCheckpoint? LocationIds = null)
{
    internal EventKey? PendingWake { get; init; }
}

internal sealed record RetiredEconomyFacilityCheckpoint(
    EconomyFacilityCheckpoint Facility,
    ProductionLineCheckpoint? Production,
    ConstructionProcessCheckpoint? Construction);

