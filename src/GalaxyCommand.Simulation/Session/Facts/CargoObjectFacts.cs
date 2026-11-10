namespace GalaxyCommand.Simulation;

/// <summary>Immutable initial contents and position captured by the cargo publication commit.</summary>
internal sealed record CargoObjectAppearedFact(CargoObjectView Object) : GameFact;

/// <summary>Committed physical disappearance, independent of private removal cause or collector attribution.</summary>
internal sealed record CargoObjectDisappearedFact(EntityId EntityId, SystemPosition Position) : GameFact;
