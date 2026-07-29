namespace GalaxyCommand.Simulation;

/// <summary>
/// Origin category used to attribute gameplay intent.
/// This is local simulation context, not an authentication or network authority.
/// </summary>
public enum CommandSourceKind
{
    Player,
    Autonomous,
    Dialogue,
    Script,
}

/// <summary>
/// Stable opaque identity within one command-source category.
/// </summary>
public readonly record struct CommandSourceId
{
    public CommandSourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Attribution and validation context for submitted gameplay intent.
/// </summary>
public sealed record CommandSource
{
    public CommandSource(CommandSourceKind kind, CommandSourceId id)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown command source kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        Kind = kind;
        Id = id;
    }

    public CommandSourceKind Kind { get; }

    public CommandSourceId Id { get; }
}

/// <summary>
/// Requested gameplay intent. Commands do not represent scheduled completion events.
/// </summary>
public abstract record GameplayCommand
{
    protected GameplayCommand(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
    }

    public string Kind { get; }
}

/// <summary>
/// Deterministic submission order within one game session.
/// </summary>
public readonly record struct CommandSequence
{
    public CommandSequence(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Gameplay intent after the session assigns authoritative ordering metadata.
/// </summary>
public sealed record GameplayCommandEnvelope
{
    public GameplayCommandEnvelope(
        CommandSequence sequence,
        SimulationTime submittedAt,
        CommandSource source,
        GameplayCommand command)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sequence.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        Sequence = sequence;
        SubmittedAt = submittedAt;
        Source = source;
        Command = command;
    }

    public CommandSequence Sequence { get; }

    public SimulationTime SubmittedAt { get; }

    public CommandSource Source { get; }

    public GameplayCommand Command { get; }
}

/// <summary>
/// Stable machine-readable reason for rejecting a command.
/// </summary>
public readonly record struct CommandRejectionCode
{
    public CommandRejectionCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public static class CommandRejectionCodes
{
    public static CommandRejectionCode UnsupportedCommand { get; } = new("unsupported-command");

    public static CommandRejectionCode InvalidSource { get; } = new("invalid-source");

    public static CommandRejectionCode InvalidIntent { get; } = new("invalid-intent");

    public static CommandRejectionCode InvalidState { get; } = new("invalid-state");

    public static CommandRejectionCode Conflict { get; } = new("conflict");

    public static CommandRejectionCode ActorOverridden { get; } = new("actor-overridden");

    public static CommandRejectionCode StaleControlRevision { get; } = new("stale-control-revision");

    public static CommandRejectionCode OrderNotFound { get; } = new("order-not-found");
}

public enum CommandResultStatus
{
    Accepted,
    Rejected,
}

/// <summary>
/// Immediate result of validating and applying or scheduling submitted intent.
/// </summary>
public sealed record CommandResult
{
    private CommandResult(
        CommandResultStatus status,
        CommandRejectionCode? rejectionCode,
        string? reason)
    {
        Status = status;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public CommandResultStatus Status { get; }

    public CommandRejectionCode? RejectionCode { get; }

    public string? Reason { get; }

    public static CommandResult Accepted() =>
        new(CommandResultStatus.Accepted, null, null);

    public static CommandResult Rejected(CommandRejectionCode code, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CommandResult(CommandResultStatus.Rejected, code, reason);
    }
}

/// <summary>
/// Immutable diagnostic record of one submitted command and its immediate result.
/// </summary>
public sealed record GameplayCommandRecord
{
    public GameplayCommandRecord(
        GameplayCommandEnvelope envelope,
        CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(result);
        Envelope = envelope;
        Result = result;
    }

    public GameplayCommandEnvelope Envelope { get; }

    public CommandResult Result { get; }
}

/// <summary>
/// Validated command result plus semantic domain changes buffered by the
/// authoritative handler for deterministic fact commit.
/// </summary>
public sealed class GameplayCommandHandlingResult
{
    public GameplayCommandHandlingResult(CommandResult result)
        : this(result, Array.Empty<GameFactProposal>())
    {
    }

    internal GameplayCommandHandlingResult(
        CommandResult result,
        IEnumerable<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(factProposals);
        Result = result;
        FactProposals = factProposals.ToArray();
    }

    public CommandResult Result { get; }

    internal IReadOnlyList<GameFactProposal> FactProposals { get; }
}

/// <summary>
/// Fixed command-dispatch boundary implemented by a game-session runtime.
/// Implementations validate before applying any authoritative mutation.
/// </summary>
public interface IGameplayCommandHandler
{
    GameplayCommandHandlingResult Handle(GameplayCommandEnvelope envelope);
}

/// <summary>
/// Assigns deterministic command order and records accepted and rejected submissions.
/// </summary>
public sealed class GameplayCommandProcessor
{
    private readonly IGameplayCommandHandler _handler;
    private readonly GameFactStore _facts;
    private readonly List<GameplayCommandRecord> _records = [];
    private ulong? _nextSequence = 1;
    private SimulationTime? _lastSubmittedAt;

    public GameplayCommandProcessor(
        IGameplayCommandHandler handler,
        int factRetentionCapacity)
        : this(handler, new GameFactStore(factRetentionCapacity))
    {
    }

    internal GameplayCommandProcessor(
        IGameplayCommandHandler handler,
        GameFactStore facts)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(facts);
        _handler = handler;
        _facts = facts;
    }

    public IReadOnlyList<GameplayCommandRecord> Records => _records.AsReadOnly();

    public GameFactReadResult ReadFactsAfter(
        GameFactSequence? sequence,
        int maximumCount) =>
        _facts.ReadAfter(sequence, maximumCount);

    public GameplayCommandRecord Submit(
        SimulationTime submittedAt,
        CommandSource source,
        GameplayCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        if (_lastSubmittedAt is { } lastSubmittedAt && submittedAt < lastSubmittedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(submittedAt),
                submittedAt,
                $"Command time {submittedAt.Milliseconds} ms precedes the previous submission at {lastSubmittedAt.Milliseconds} ms.");
        }

        ulong value = _nextSequence
            ?? throw new InvalidOperationException("Command sequence exhausted.");
        _nextSequence = value == ulong.MaxValue ? null : value + 1;

        var envelope = new GameplayCommandEnvelope(
            new CommandSequence(value),
            submittedAt,
            source,
            command);
        GameplayCommandHandlingResult handling = _handler.Handle(envelope)
            ?? throw new InvalidOperationException("Command handler returned no result.");
        CommandResult result = handling.Result;
        GameFact outcome = result.Status switch
        {
            CommandResultStatus.Accepted => new CommandAcceptedFact(
                envelope.Sequence,
                envelope.Source,
                envelope.Command.Kind),
            CommandResultStatus.Rejected => new CommandRejectedFact(
                envelope.Sequence,
                envelope.Source,
                envelope.Command.Kind,
                result.RejectionCode
                    ?? throw new InvalidOperationException(
                        "Rejected command has no rejection code.")),
            _ => throw new InvalidOperationException(
                $"Unknown command result status {result.Status}."),
        };
        var proposals = new List<GameFactProposal>(
            handling.FactProposals.Count + 1)
        {
            new(
                new GameFactProposalKey(
                    GameFactCommitCategory.CommandOutcome,
                    envelope.Sequence.Value,
                    0,
                    0),
                outcome),
        };
        proposals.AddRange(handling.FactProposals);
        _facts.Commit(
            submittedAt,
            new CommandFactCause(envelope.Sequence),
            proposals);
        var record = new GameplayCommandRecord(envelope, result);
        _records.Add(record);
        _lastSubmittedAt = submittedAt;
        return record;
    }
}
