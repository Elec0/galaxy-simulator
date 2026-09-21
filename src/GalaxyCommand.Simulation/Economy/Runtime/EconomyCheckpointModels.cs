using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record EconomyFacilityCheckpoint(
    FacilityId FacilityId,
    InventoryId InventoryId,
    LocationId LocationId,
    SystemPosition Position,
    MaterialId? ProductionOutput);

internal sealed record SessionEconomyCheckpoint(
    IReadOnlyList<EconomyFacilityCheckpoint?> Facilities,
    ProductionOwnerCheckpoint Production,
    ConstructionOwnerCheckpoint Construction,
    TransportOwnerCheckpoint Transport);

