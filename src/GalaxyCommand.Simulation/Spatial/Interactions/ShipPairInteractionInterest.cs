namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable range-interest input from its authoritative domain. Source and
/// target retain their directed meaning independent of canonical discovery order.
/// </summary>
public sealed record ShipPairInteractionInterest : SpatialInteractionInterest
{
    /// <summary>
    /// Captures a domain's explicit range question for two distinct ship identities.
    /// Zero range is valid; missing identities or purpose are rejected. The caller
    /// owns liveness and eligibility validation against authoritative state.
    /// </summary>
    public ShipPairInteractionInterest(
        InteractionInterestId id, string domainKind, string purposeId,
        ShipId source, ShipId target, ulong rangeUnits, EventGeneration generation)
        : base(id, domainKind, purposeId, source, rangeUnits, generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(purposeId);
        _ = new InteractionShipPair(source, target);

        Target = target;
    }

    public ShipId Target { get; }
    internal override IEnumerable<ShipId> Ships => [Source, Target];
    internal override SpatialRevision? TargetRevision(InteractionSpatialReadView view) => view.Get(Target)?.Revision;
}
