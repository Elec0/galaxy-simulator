namespace GalaxyCommand.Simulation;

public sealed partial class TransportBoard
{
    /// <summary>Withdraws only unassigned station demand; committed delivery jobs keep their existing claims.</summary>
    internal void TrimStationUnassignedDemand(InventoryId inventory, MaterialId material, Quantity retained)
    {
        ulong remaining = retained.Units;
        foreach (DemandRequest demand in _demands.Values.Where(value => value.InventoryId == inventory && value.MaterialId == material))
        {
            ulong keep = Math.Min(remaining, demand.Remaining.Units);
            demand.Remaining = new(keep);
            remaining -= keep;
        }
    }

    /// <summary>Offers represent current physical cargo independently of the producer that originally authored its output.</summary>
    internal void TrimStationUnassignedSupply(InventoryId inventory, MaterialId material, Quantity available)
    {
        ulong remaining = available.Units;
        foreach (SupplyOffer supply in _supplies.Values.Where(value => value.InventoryId == inventory && value.MaterialId == material))
        {
            ulong keep = Math.Min(remaining, supply.Remaining.Units);
            supply.Remaining = new(keep);
            remaining -= keep;
        }
    }
}
