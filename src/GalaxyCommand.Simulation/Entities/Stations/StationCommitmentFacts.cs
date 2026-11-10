namespace GalaxyCommand.Simulation;

/// <summary>Identifies the reservation namespace without conflating legacy material, capacity and physical claims.</summary>
public enum StationCommitmentKind { Material, Capacity, Physical }

/// <summary>Private causal evidence that station loss invalidated one exact workflow commitment.</summary>
public sealed record StationCommitmentInvalidatedFact(StationId StationId, PrincipalId PrincipalId,
    InventoryId InventoryId, StationCommitmentKind Kind, ulong ReservationValue, ReservationOwner Owner) : GameFact;
