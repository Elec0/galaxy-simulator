namespace GalaxyCommand.Simulation;

/// <summary>Local inspection for one owned, in-range pickup interaction; this does not create a map contact.</summary>
public sealed record CargoCollectionProgressView(ShipId ShipId, ShipOrderId OrderId, CargoObjectView Target,
    Quantity ReservedQuantity, SimulationTime StartedAt, SimulationTime DueAt);
