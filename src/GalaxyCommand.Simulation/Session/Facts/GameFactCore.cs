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
/// Immediate cause for one idempotent standing-change batch.
/// </summary>
public sealed record StandingChangeFactCause : GameFactCause
{
    /// <summary>
    /// Creates a cause correlated to one committed standing batch.
    /// </summary>
    public StandingChangeFactCause(StandingChangeBatchId batchId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(batchId.Value);
        BatchId = batchId;
    }

    public StandingChangeBatchId BatchId { get; }
}

/// <summary>
/// Immediate cause for one idempotent diplomacy and grant change batch.
/// </summary>
public sealed record RelationshipPolicyChangeFactCause : GameFactCause
{
    /// <summary>
    /// Creates a cause correlated to one committed relationship policy batch.
    /// </summary>
    public RelationshipPolicyChangeFactCause(RelationshipPolicyChangeBatchId batchId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(batchId.Value);
        BatchId = batchId;
    }

    public RelationshipPolicyChangeBatchId BatchId { get; }
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
