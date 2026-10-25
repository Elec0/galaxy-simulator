namespace GalaxyCommand.Simulation;

/// <summary>Domain-owned directed range question with session identity and revision.</summary>
public abstract record SpatialInteractionInterest
{
    /// <summary>Validates the shared intent identity before a domain publishes it.</summary>
    private protected SpatialInteractionInterest(InteractionInterestId id, string domainKind,
        string purposeId, ShipId source, ulong rangeUnits, EventGeneration generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(purposeId);
        Id = id;
        DomainKind = domainKind;
        PurposeId = purposeId;
        Source = source;
        RangeUnits = rangeUnits;
        Generation = generation;
    }

    public InteractionInterestId Id { get; }
    public string DomainKind { get; }
    public string PurposeId { get; }
    public ShipId Source { get; }
    public ulong RangeUnits { get; }
    public EventGeneration Generation { get; }

    internal abstract IEnumerable<ShipId> Ships { get; }
    internal abstract SpatialRevision? TargetRevision(InteractionSpatialReadView view);
}

/// <summary>
/// Immutable fixed target supplied by its owning domain. A null entity names a
/// commanded position; entity-backed intent must be canceled or revised when that owner changes it.
/// </summary>
public sealed record StationaryInteractionInterest : SpatialInteractionInterest
{
    /// <summary>Captures an explicit local position without registering a fictitious spatial ship.</summary>
    public StationaryInteractionInterest(InteractionInterestId id, string domainKind, string purposeId,
        ShipId source, SystemPosition position, ulong rangeUnits, EventGeneration generation,
        EntityId? entityId = null, SpatialRevision targetRevision = default)
        : base(id, domainKind, purposeId, source, rangeUnits, generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        if (entityId is { Value: 0 }) throw new ArgumentOutOfRangeException(nameof(entityId));
        Position = position;
        EntityId = entityId;
        Revision = targetRevision;
    }

    public SystemPosition Position { get; }
    public EntityId? EntityId { get; }
    public SpatialRevision Revision { get; }

    internal override IEnumerable<ShipId> Ships => [Source];
    internal override SpatialRevision? TargetRevision(InteractionSpatialReadView view) => Revision;
}
