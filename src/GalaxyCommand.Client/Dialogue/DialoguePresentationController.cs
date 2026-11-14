using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>
/// Disposable dialogue presentation input. The session remains authoritative for
/// lifecycle and response classification; the client only selects foreground.
/// </summary>
public sealed record DialoguePresentationEntry
{
    /// <summary>Copies display classification; completed informational output cannot be suspended or require a response.</summary>
    public DialoguePresentationEntry(DialogueConversationId conversationId, bool isSuspended, bool responseRequired, bool isCompleted = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(conversationId.Value);
        if (isCompleted && (isSuspended || responseRequired))
            throw new ArgumentException("Completed informational output cannot require a response or be suspended.");
        ConversationId = conversationId;
        IsSuspended = isSuspended;
        ResponseRequired = responseRequired;
        IsCompleted = isCompleted;
    }

    public DialogueConversationId ConversationId { get; }
    public bool IsSuspended { get; }
    public bool ResponseRequired { get; }
    public bool IsCompleted { get; }
}

/// <summary>
/// Selects at most one local foreground conversation and coordinates its pause
/// ownership. Create a fresh controller after session replacement or load;
/// conversation IDs are monotonically allocated within one session.
/// Call <see cref="Close"/> on the previous controller before replacement when
/// reusing pacing, so any pause it owns is released before a new opening.
/// </summary>
public sealed class DialoguePresentationController
{
    private readonly ApplicationPacingController _pacing;
    private readonly bool _pauseWhenResponseRequiredDialogueOpens;
    private DialoguePresentationEntry[] _entries = [];
    private ulong _highestObservedConversationId;

    /// <summary>Uses shared local pacing and the resolved device-local automatic-pause preference.</summary>
    public DialoguePresentationController(ApplicationPacingController pacing, bool pauseWhenResponseRequiredDialogueOpens)
    {
        ArgumentNullException.ThrowIfNull(pacing);
        _pacing = pacing;
        _pauseWhenResponseRequiredDialogueOpens = pauseWhenResponseRequiredDialogueOpens;
    }

    public DialogueConversationId? ForegroundConversationId { get; private set; }
    public IReadOnlyList<DialoguePresentationEntry> PendingConversations { get; private set; } = Array.Empty<DialoguePresentationEntry>();

    /// <summary>
    /// Replaces the complete conversation read model after validating it. Only
    /// newly created active conversations may automatically acquire an empty
    /// foreground; previously pending entries require explicit player selection.
    /// Returns whether the foreground identity changed.
    /// </summary>
    public bool Update(IEnumerable<DialoguePresentationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        DialoguePresentationEntry[] values = entries.ToArray();
        if (values.Any(entry => entry is null)
            || values.Select(entry => entry.ConversationId).Distinct().Count() != values.Length)
            throw new ArgumentException("Dialogue entries must be non-null and have distinct conversation identities.", nameof(entries));

        Array.Sort(values, (left, right) => left.ConversationId.Value.CompareTo(right.ConversationId.Value));
        DialoguePresentationEntry? foreground = values.FirstOrDefault(entry =>
            entry.ConversationId == ForegroundConversationId && !entry.IsSuspended);
        foreground ??= values.FirstOrDefault(entry =>
            entry.ConversationId.Value > _highestObservedConversationId && !entry.IsSuspended);

        _entries = values;
        if (values.Length > 0)
            _highestObservedConversationId = Math.Max(_highestObservedConversationId, values[^1].ConversationId.Value);
        return SetForeground(foreground);
    }

    /// <summary>
    /// Explicitly foregrounds a known active conversation. A suspended entry must
    /// first pass session resume admission and appear active in a refreshed view.
    /// Unknown, suspended, or already foreground entries leave selection and pace
    /// unchanged. Returns whether the foreground identity changed.
    /// </summary>
    public bool Open(DialogueConversationId conversationId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(conversationId.Value);
        DialoguePresentationEntry? entry = Array.Find(_entries, candidate =>
            candidate.ConversationId == conversationId && !candidate.IsSuspended);
        return entry is not null && entry.ConversationId != ForegroundConversationId && SetForeground(entry);
    }

    /// <summary>
    /// Closes local foreground and releases only its owned automatic pause.
    /// Authoritative suspension remains a separately admitted session command;
    /// closing never selects a response or opens a pending conversation.
    /// </summary>
    public bool Close() => SetForeground(null);

    /// <summary>
    /// Applies one foreground handoff and publishes an independent pending view.
    /// Same-conversation node changes retain pacing ownership so manual overrides
    /// cannot be undone by a later screen. Completed informational output releases
    /// the ended conversation's pause while retaining its displayed lines.
    /// </summary>
    private bool SetForeground(DialoguePresentationEntry? foreground)
    {
        bool changed = ForegroundConversationId != foreground?.ConversationId;
        if (changed || foreground?.IsCompleted == true)
            _pacing.CloseResponseRequiredDialogue();
        ForegroundConversationId = foreground?.ConversationId;
        if (foreground?.ResponseRequired == true)
            _pacing.OpenResponseRequiredDialogue(_pauseWhenResponseRequiredDialogueOpens);
        PendingConversations = Array.AsReadOnly(_entries.Where(entry =>
            entry.ConversationId != ForegroundConversationId).ToArray());
        return changed;
    }
}
