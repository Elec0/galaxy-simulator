using System.Collections.ObjectModel;
using System.Globalization;

namespace GalaxyCommand.Simulation;

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
    PhysicalWaypoint,
    PhysicalCruiseTransition,
    OrderTransition,
    PhysicalWorkStarted,
    EntityLifecycle,
    Relationship,
    RelationshipDiplomacy,
    RelationshipGrant,
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

    /// <summary>
    /// Captures the exact sequence allocator and retained semantic fact suffix.
    /// </summary>
    internal GameFactStoreCheckpoint CaptureCheckpoint()
    {
        var retained = new GameFactEnvelope[_count];
        for (int index = 0; index < retained.Length; index++)
        {
            retained[index] = GetAt(index);
        }

        return new GameFactStoreCheckpoint(
            _retained.Length,
            new IdSequenceCheckpoint(_nextSequence),
            retained);
    }

    /// <summary>
    /// Validates and directly restores fact continuity without recommitting
    /// retained facts or allocating replacement sequence values.
    /// </summary>
    internal static CheckpointResult<GameFactStore> RestoreCheckpoint(
        GameFactStoreCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        const string path = "$.checkpoint.facts";
        if (checkpoint.Capacity <= 0)
        {
            return Rejected(
                $"{path}.capacity",
                "Fact retention capacity must be positive.");
        }

        if (checkpoint.Sequences.NextValue == 0)
        {
            return Rejected(
                $"{path}.sequences.nextValue",
                "The next fact sequence must be positive or exhausted.");
        }

        if (checkpoint.RetainedFacts.Count > checkpoint.Capacity)
        {
            return Rejected(
                $"{path}.retained",
                "Retained facts exceed the configured capacity.");
        }

        ulong? newest = checkpoint.Sequences.NextValue switch
        {
            null => ulong.MaxValue,
            1 => null,
            { } next => next - 1,
        };
        if (newest is null && checkpoint.RetainedFacts.Count != 0)
        {
            return Rejected(
                $"{path}.retained",
                "An unused fact sequence cannot have retained facts.");
        }

        if (newest is not null && checkpoint.RetainedFacts.Count == 0)
        {
            return Rejected(
                $"{path}.retained",
                "A used fact sequence must retain its newest committed fact.");
        }

        for (int index = 0; index < checkpoint.RetainedFacts.Count; index++)
        {
            GameFactEnvelope? fact = checkpoint.RetainedFacts[index];
            if (fact is null)
            {
                return Rejected(
                    $"{path}.retained[{index}]",
                    "A retained fact is missing.");
            }

            if (index > 0)
            {
                GameFactEnvelope previous = checkpoint.RetainedFacts[index - 1]!;
                if (previous.Sequence.Value == ulong.MaxValue
                    || fact.Sequence.Value != previous.Sequence.Value + 1)
                {
                    return Rejected(
                        $"{path}.retained[{index}].sequence",
                        "Retained fact sequences must be contiguous.");
                }
            }
        }

        if (newest is { } expectedNewest
            && checkpoint.RetainedFacts[^1]!.Sequence.Value != expectedNewest)
        {
            return Rejected(
                $"{path}.retained",
                "Retained facts must end at the newest committed sequence.");
        }

        var restored = new GameFactStore(checkpoint.Capacity)
        {
            _nextSequence = checkpoint.Sequences.NextValue,
        };
        foreach (GameFactEnvelope? fact in checkpoint.RetainedFacts)
        {
            // Append reconstructs only the ring layout. It does not allocate a
            // sequence or re-emit the already committed semantic fact.
            restored.Append(fact!);
        }

        return CheckpointResult<GameFactStore>.Success(restored);
    }

    /// <summary>
    /// Reports whether one complete prepared fact batch can receive contiguous
    /// authoritative sequence values without overflow.
    /// </summary>
    internal bool CanCommit(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return true;
        }

        return _nextSequence is { } next
            && checked((ulong)count - 1) <= ulong.MaxValue - next;
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

    private static CheckpointResult<GameFactStore> Rejected(
        string path,
        string message) =>
        CheckpointResult<GameFactStore>.Rejected(
            new CheckpointValidationFailure(path, message));
}
