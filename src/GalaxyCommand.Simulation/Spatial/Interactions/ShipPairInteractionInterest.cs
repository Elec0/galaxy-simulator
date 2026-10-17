namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable range-interest input from its authoritative domain. Source and
/// target retain their directed meaning independent of canonical discovery order.
/// </summary>
public sealed record ShipPairInteractionInterest
{
    /// <summary>
    /// Captures a domain's explicit range question for two distinct ship identities.
    /// Zero range is valid; missing identities or purpose are rejected. The caller
    /// owns liveness and eligibility validation against authoritative state.
    /// </summary>
    public ShipPairInteractionInterest(
        InteractionInterestId id, string domainKind, string purposeId,
        ShipId source, ShipId target, ulong rangeUnits, EventGeneration generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(purposeId);
        _ = new InteractionShipPair(source, target);

        Id = id;
        DomainKind = domainKind;
        PurposeId = purposeId;
        Source = source;
        Target = target;
        RangeUnits = rangeUnits;
        Generation = generation;
    }

    public InteractionInterestId Id { get; }
    public string DomainKind { get; }
    public string PurposeId { get; }
    public ShipId Source { get; }
    public ShipId Target { get; }
    public ulong RangeUnits { get; }
    public EventGeneration Generation { get; }
}
