using System.Collections.ObjectModel;
using System.Globalization;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministic total order of semantic facts within one game session.
/// </summary>
public readonly record struct GameFactSequence
{
    public GameFactSequence(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public override string ToString() =>
        Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Immediate authoritative trigger for one committed fact batch.
/// </summary>
public abstract record GameFactCause
{
    private protected GameFactCause()
    {
    }
}

public sealed record CommandFactCause : GameFactCause
{
    public CommandFactCause(CommandSequence sequence)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sequence.Value);
        Sequence = sequence;
    }

    public CommandSequence Sequence { get; }
}

public sealed record ScheduledEventFactCause : GameFactCause
{
    public ScheduledEventFactCause(EventKey key)
    {
        Key = key;
    }

    public EventKey Key { get; }
}

public sealed record EntityRemovalFactCause : GameFactCause
{
    public EntityRemovalFactCause(EntityRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public EntityRemovalRequest Request { get; }
}

/// <summary>
/// Causal construction identity used when completion was not dispatched from
/// a scheduled event carrying an <see cref="EventKey"/>.
/// </summary>
public sealed record ConstructionMaterializationFactCause : GameFactCause
{
    public ConstructionMaterializationFactCause(
        FacilityId facilityId,
        ConstructionOrderId orderId,
        EventGeneration generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(facilityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        FacilityId = facilityId;
        OrderId = orderId;
        Generation = generation;
    }

    public FacilityId FacilityId { get; }

    public ConstructionOrderId OrderId { get; }

    public EventGeneration Generation { get; }
}

/// <summary>
/// Typed gameplay meaning committed by the authoritative simulation.
/// </summary>
public abstract record GameFact
{
    private protected GameFact()
    {
    }
}

/// <summary>
/// Authoritative source category that requested an entity materialization.
/// </summary>
public enum EntityMaterializationSourceKind
{
    Construction,
}

/// <summary>
/// Semantic record of a fully committed entity becoming publicly live.
/// </summary>
public sealed record EntityMaterializedFact : GameFact
{
    /// <summary>
    /// Creates the semantic record for one fully committed ship materialization.
    /// </summary>
    public EntityMaterializedFact(
        EntityId entityId,
        EntityKind kind,
        ShipId shipId,
        EntityMaterializationSourceKind sourceKind,
        PrincipalId principalId,
        ConstructionDesignId designId,
        SystemPosition initialPosition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(principalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(designId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(initialPosition.SystemId.Value);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind.");
        }

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown materialization source kind.");
        }

        EntityId = entityId;
        Kind = kind;
        ShipId = shipId;
        SourceKind = sourceKind;
        PrincipalId = principalId;
        DesignId = designId;
        InitialPosition = initialPosition;
    }

    public EntityId EntityId { get; }

    public EntityKind Kind { get; }

    public ShipId ShipId { get; }

    public EntityMaterializationSourceKind SourceKind { get; }

    public PrincipalId PrincipalId { get; }

    public ConstructionDesignId DesignId { get; }

    public SystemPosition InitialPosition { get; }
}

public sealed record EntityRemovedFact : GameFact
{
    public EntityRemovedFact(
        EntityId entityId,
        EntityKind kind,
        ShipId shipId,
        EntityRemovalReason reason,
        EntityCargoDisposition cargoDisposition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown removal reason.");
        }

        if (!Enum.IsDefined(cargoDisposition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cargoDisposition),
                cargoDisposition,
                "Unknown cargo disposition.");
        }

        EntityId = entityId;
        Kind = kind;
        ShipId = shipId;
        Reason = reason;
        CargoDisposition = cargoDisposition;
    }

    public EntityId EntityId { get; }

    public EntityKind Kind { get; }

    public ShipId ShipId { get; }

    public EntityRemovalReason Reason { get; }

    public EntityCargoDisposition CargoDisposition { get; }
}

public sealed record CommandAcceptedFact : GameFact
{
    public CommandAcceptedFact(
        CommandSequence commandSequence,
        CommandSource source,
        string commandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(commandSequence.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandKind);
        CommandSequence = commandSequence;
        Source = source;
        CommandKind = commandKind;
    }

    public CommandSequence CommandSequence { get; }

    public CommandSource Source { get; }

    public string CommandKind { get; }
}

public sealed record CommandRejectedFact : GameFact
{
    public CommandRejectedFact(
        CommandSequence commandSequence,
        CommandSource source,
        string commandKind,
        CommandRejectionCode rejectionCode)
    {
        ArgumentOutOfRangeException.ThrowIfZero(commandSequence.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionCode.Value);
        CommandSequence = commandSequence;
        Source = source;
        CommandKind = commandKind;
        RejectionCode = rejectionCode;
    }

    public CommandSequence CommandSequence { get; }

    public CommandSource Source { get; }

    public string CommandKind { get; }

    public CommandRejectionCode RejectionCode { get; }
}

public sealed record ShipOrderTransitionFact : GameFact
{
    public ShipOrderTransitionFact(
        ShipId shipId,
        ShipOrderId orderId,
        CommandSource source,
        NavigationDestination destination,
        ShipOrderStatus? previousStatus,
        ShipOrderStatus nextStatus,
        ShipOrderReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (previousStatus is { } previous && !Enum.IsDefined(previous))
        {
            throw new ArgumentOutOfRangeException(
                nameof(previousStatus),
                previousStatus,
                "Unknown previous ship-order status.");
        }

        if (!Enum.IsDefined(nextStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextStatus),
                nextStatus,
                "Unknown next ship-order status.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown ship-order reason.");
        }

        ShipId = shipId;
        OrderId = orderId;
        Source = source;
        Destination = destination;
        PreviousStatus = previousStatus;
        NextStatus = nextStatus;
        Reason = reason;
    }

    public ShipId ShipId { get; }

    public ShipOrderId OrderId { get; }

    public CommandSource Source { get; }

    public NavigationDestination Destination { get; }

    public ShipOrderStatus? PreviousStatus { get; }

    public ShipOrderStatus NextStatus { get; }

    public ShipOrderReason Reason { get; }
}

public enum LocalMotionEndReason
{
    Arrived,
    CancelledByCommand,
    ReplacedByCommand,
    SuspendedByScriptedOverride,
    ScriptedOverrideEnded,
    TargetRemoved,
}

public sealed record ShipLocalMotionStartedFact : GameFact
{
    public ShipLocalMotionStartedFact(
        ShipId shipId,
        LocalMotionSnapshot motion,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(motion);
        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Motion = motion;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public LocalMotionSnapshot Motion { get; }

    public ShipOrderId? OrderId { get; }
}

public sealed record ShipLocalMotionEndedFact : GameFact
{
    public ShipLocalMotionEndedFact(
        ShipId shipId,
        LocalMotionSnapshot motion,
        SystemPosition finalPosition,
        SimulationTime endedAt,
        LocalMotionEndReason reason,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentOutOfRangeException.ThrowIfZero(finalPosition.SystemId.Value);
        if (endedAt < motion.DepartedAt || endedAt > motion.ArrivesAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAt),
                endedAt,
                "Motion end time must fall within the scheduled segment.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown local-motion end reason.");
        }

        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Motion = motion;
        FinalPosition = finalPosition;
        EndedAt = endedAt;
        Reason = reason;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public LocalMotionSnapshot Motion { get; }

    public SystemPosition FinalPosition { get; }

    public SimulationTime EndedAt { get; }

    public LocalMotionEndReason Reason { get; }

    public ShipOrderId? OrderId { get; }
}

public sealed record ShipConnectorTransitStartedFact : GameFact
{
    public ShipConnectorTransitStartedFact(
        ShipId shipId,
        ConnectorTransitSnapshot transit,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(transit);
        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Transit = transit;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public ConnectorTransitSnapshot Transit { get; }

    public ShipOrderId? OrderId { get; }
}

public sealed record ShipConnectorTransitCompletedFact : GameFact
{
    public ShipConnectorTransitCompletedFact(
        ShipId shipId,
        ConnectorTransitSnapshot transit,
        SimulationTime completedAt,
        ShipOrderId? orderId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(transit);
        if (completedAt != transit.ArrivesAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt),
                completedAt,
                "Connector transit must complete at its scheduled arrival.");
        }

        if (orderId is { } order)
        {
            ArgumentOutOfRangeException.ThrowIfZero(order.Value);
        }

        ShipId = shipId;
        Transit = transit;
        CompletedAt = completedAt;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public ConnectorTransitSnapshot Transit { get; }

    public SimulationTime CompletedAt { get; }

    public ShipOrderId? OrderId { get; }
}

/// <summary>
/// One immutable semantic fact with authoritative order and cause.
/// </summary>
public sealed record GameFactEnvelope
{
    public GameFactEnvelope(
        GameFactSequence sequence,
        SimulationTime timestamp,
        GameFactCause cause,
        GameFact fact)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sequence.Value);
        ArgumentNullException.ThrowIfNull(cause);
        ArgumentNullException.ThrowIfNull(fact);
        Sequence = sequence;
        Timestamp = timestamp;
        Cause = cause;
        Fact = fact;
    }

    public GameFactSequence Sequence { get; }

    public SimulationTime Timestamp { get; }

    public GameFactCause Cause { get; }

    public GameFact Fact { get; }
}

/// <summary>
/// Result of reading the bounded retained fact window after a consumer cursor.
/// </summary>
public sealed record GameFactReadResult
{
    public GameFactReadResult(
        IReadOnlyList<GameFactEnvelope> facts,
        GameFactSequence? oldestRetainedSequence,
        GameFactSequence? newestCommittedSequence,
        bool cursorGap)
    {
        ArgumentNullException.ThrowIfNull(facts);
        Facts = facts;
        OldestRetainedSequence = oldestRetainedSequence;
        NewestCommittedSequence = newestCommittedSequence;
        CursorGap = cursorGap;
    }

    public IReadOnlyList<GameFactEnvelope> Facts { get; }

    public GameFactSequence? OldestRetainedSequence { get; }

    public GameFactSequence? NewestCommittedSequence { get; }

    public bool CursorGap { get; }
}

internal enum GameFactCommitCategory
{
    CommandOutcome,
    PhysicalWorkEnded,
    OrderTransition,
    PhysicalWorkStarted,
    EntityLifecycle,
}

internal readonly record struct GameFactProposalKey(
    GameFactCommitCategory Category,
    ulong PrimaryIdentity,
    ulong SecondaryIdentity,
    int TransitionOrdinal) : IComparable<GameFactProposalKey>
{
    public int CompareTo(GameFactProposalKey other)
    {
        int category = Category.CompareTo(other.Category);
        if (category != 0)
        {
            return category;
        }

        int primary = PrimaryIdentity.CompareTo(other.PrimaryIdentity);
        if (primary != 0)
        {
            return primary;
        }

        int ordinal = TransitionOrdinal.CompareTo(other.TransitionOrdinal);
        return ordinal != 0
            ? ordinal
            : SecondaryIdentity.CompareTo(other.SecondaryIdentity);
    }
}

internal sealed record GameFactProposal(
    GameFactProposalKey Key,
    GameFact Fact);

internal sealed class GameFactStore
{
    private readonly GameFactEnvelope?[] _retained;
    private int _start;
    private int _count;
    private ulong? _nextSequence = 1;

    internal GameFactStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _retained = new GameFactEnvelope[capacity];
    }

    internal void Commit(
        SimulationTime timestamp,
        GameFactCause cause,
        IEnumerable<GameFactProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(cause);
        ArgumentNullException.ThrowIfNull(proposals);
        GameFactProposal[] ordered = proposals
            .OrderBy(proposal => proposal.Key)
            .ToArray();
        if (ordered.Length == 0)
        {
            return;
        }

        for (int index = 0; index < ordered.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(ordered[index]);
            ArgumentNullException.ThrowIfNull(ordered[index].Fact);
            if (index > 0 && ordered[index - 1].Key == ordered[index].Key)
            {
                throw new InvalidOperationException(
                    $"Duplicate fact proposal key {ordered[index].Key}.");
            }
        }

        ulong firstValue = _nextSequence
            ?? throw new InvalidOperationException("Game fact sequence exhausted.");
        ulong finalOffset = checked((ulong)ordered.Length - 1);
        ulong finalValue = checked(firstValue + finalOffset);
        var committed = new GameFactEnvelope[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            committed[index] = new GameFactEnvelope(
                new GameFactSequence(checked(firstValue + (ulong)index)),
                timestamp,
                cause,
                ordered[index].Fact);
        }

        foreach (GameFactEnvelope fact in committed)
        {
            Append(fact);
        }

        _nextSequence = finalValue == ulong.MaxValue
            ? null
            : finalValue + 1;
    }

    internal GameFactReadResult ReadAfter(
        GameFactSequence? sequence,
        int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        GameFactSequence? oldest = _count == 0
            ? null
            : GetAt(0).Sequence;
        GameFactSequence? newest = _nextSequence switch
        {
            null => new GameFactSequence(ulong.MaxValue),
            1 => null,
            { } next => new GameFactSequence(next - 1),
        };
        bool cursorGap = oldest is { } oldestRetained
            && (sequence is null
                ? oldestRetained.Value > 1
                : sequence.Value.Value < oldestRetained.Value - 1);
        int firstIndex = 0;
        if (sequence is { } cursor && oldest is { } firstRetained)
        {
            firstIndex = cursor.Value < firstRetained.Value
                ? 0
                : cursor.Value >= newest!.Value.Value
                    ? _count
                    : checked((int)(cursor.Value - firstRetained.Value + 1));
        }

        int resultCount = Math.Min(maximumCount, _count - firstIndex);
        var facts = new List<GameFactEnvelope>(resultCount);
        int end = firstIndex + resultCount;
        for (int index = firstIndex; index < end; index++)
        {
            facts.Add(GetAt(index));
        }

        return new GameFactReadResult(
            new ReadOnlyCollection<GameFactEnvelope>(facts),
            oldest,
            newest,
            cursorGap);
    }

    private void Append(GameFactEnvelope fact)
    {
        if (_count < _retained.Length)
        {
            int destination = (_start + _count) % _retained.Length;
            _retained[destination] = fact;
            _count++;
            return;
        }

        _retained[_start] = fact;
        _start = (_start + 1) % _retained.Length;
    }

    private GameFactEnvelope GetAt(int index) =>
        _retained[(_start + index) % _retained.Length]
        ?? throw new InvalidOperationException("Retained fact slot was unexpectedly empty.");
}
