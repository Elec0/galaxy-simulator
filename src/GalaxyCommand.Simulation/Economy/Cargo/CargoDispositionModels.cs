using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal abstract record CargoDispositionTrigger
{
    internal sealed record Command(CommandSequence Sequence) : CargoDispositionTrigger;
    internal sealed record Scheduled(EventKey Key) : CargoDispositionTrigger;
}

internal sealed record CargoDispositionKey(CargoDispositionTrigger Trigger, EntityId SourceEntityId, ulong OccurrenceOrdinal);

internal abstract record CargoDispositionSelection(QualifiedContentKey DefinitionKey)
{
    internal sealed record Fungible(QualifiedContentKey Key, Quantity Quantity) : CargoDispositionSelection(Key);
    internal sealed record Discrete(QualifiedContentKey Key, ItemInstanceId InstanceId) : CargoDispositionSelection(Key);
}

internal sealed record CargoDispositionRequest(CargoDispositionKey Key, InventoryId SourceInventoryId,
    SystemPosition SourcePosition, CargoLossCause Cause, CargoDispositionPolicy Policy,
    IReadOnlyList<CargoDispositionSelection> Selected, IReadOnlyList<CargoSourceReservationCleanup> Cleanup);

internal sealed record CargoDispositionTypeOutcome(QualifiedContentKey DefinitionKey, PhysicalHoldingKind Kind,
    Quantity Removed, Quantity Destroyed, Quantity Recoverable, IReadOnlyList<ItemInstanceId> SurvivingInstances);

internal sealed record CargoLiveObject(EntityId EntityId, CargoDispositionKey Origin, SimulationTime CreatedAt,
    SimulationTime ExpiresAt, QualifiedContentKey PolicyKey, EventKey ExpiryEvent, EventGeneration Generation,
    DebrisFieldId? OriginalFieldId);

internal sealed record CargoDispositionReceipt(CargoDispositionRequest Request, SimulationTime CommittedAt,
    IReadOnlyList<CargoDispositionTypeOutcome> Outcomes, IReadOnlyList<EntityId> ObjectIds, DebrisFieldId? FieldHandle);

internal enum CargoDispositionFailure
{
    InvalidInput, PolicyMismatch, SourceUnavailable, SourceLossRejected, IdentifierExhausted,
    NumericExhaustion, ConflictingReplay, StalePreparation, FactCapacityExhausted,
}

internal sealed record CargoDispositionPreparation(PreparedCargoDisposition? Prepared = null,
    CargoDispositionReceipt? Receipt = null, CargoDispositionFailure? Failure = null);

internal sealed record CargoDispositionCommitResult(CargoDispositionReceipt? Receipt,
    CargoDispositionFailure? Failure, bool WasApplied);

internal sealed record PreparedCargoDisposition(CargoDispositionOwner Owner, CargoDispositionRequest Request,
    PreparedCargoSourceLoss SourceLoss, PreparedCargoObjectBatch Objects, PreparedCargoDebrisMerge? Field,
    IReadOnlyList<CargoDispositionTypeOutcome> Outcomes, SimulationTime PreparedAt, SimulationTime ExpiresAt,
    ulong AgendaSequence);

internal sealed record CargoDispositionCommittedFact(CargoDispositionKey Key) : GameFact;

/// <summary>Canonical checkpoint order only; actual causal commit ordering remains with the session coordinator.</summary>
internal sealed class CargoDispositionKeyComparer : IComparer<CargoDispositionKey>
{
    internal static CargoDispositionKeyComparer Instance { get; } = new();

    /// <summary>Keeps command and event identities in distinct namespaces before source and occurrence ordering.</summary>
    public int Compare(CargoDispositionKey? x, CargoDispositionKey? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int kind = (x.Trigger is CargoDispositionTrigger.Command ? 0 : 1).CompareTo(y.Trigger is CargoDispositionTrigger.Command ? 0 : 1);
        if (kind != 0) return kind;
        int trigger = (x.Trigger, y.Trigger) switch
        {
            (CargoDispositionTrigger.Command left, CargoDispositionTrigger.Command right) => left.Sequence.Value.CompareTo(right.Sequence.Value),
            (CargoDispositionTrigger.Scheduled left, CargoDispositionTrigger.Scheduled right) => left.Key.CompareTo(right.Key),
            _ => throw new InvalidOperationException("Cargo receipt cause is invalid."),
        };
        if (trigger != 0) return trigger;
        int source = x.SourceEntityId.Value.CompareTo(y.SourceEntityId.Value);
        return source != 0 ? source : x.OccurrenceOrdinal.CompareTo(y.OccurrenceOrdinal);
    }
}
