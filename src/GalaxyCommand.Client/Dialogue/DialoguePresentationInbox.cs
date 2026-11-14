using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>Disposable display data combining live dialogue with retained completed informational output.</summary>
public sealed record DialogueDisplayConversation(DialogueConversationId Id, QualifiedContentKey DefinitionKey,
    bool IsSuspended, bool ResponseRequired, bool IsCompleted,
    IReadOnlyList<DialogueSegment> Segments, IReadOnlyList<DialogueChoiceSnapshot> Choices);

/// <summary>
/// Retains a bounded suffix of disclosure-safe completed informational dialogue.
/// It owns no authoritative lifecycle and never reconstructs state from rendered text.
/// Create a new inbox when replacing or loading a session.
/// </summary>
public sealed class DialoguePresentationInbox
{
    private readonly int _maximumCompleted;
    private readonly Dictionary<DialogueConversationId, (ulong Sequence, DialogueDisplayConversation View)> _completed = [];
    private DialogueConversationSnapshot[] _live = [];
    private ulong _lastFactSequence;

    /// <summary>Sets the positive maximum number of retained completed notices; live conversations remain session-owned.</summary>
    public DialoguePresentationInbox(int maximumCompleted)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCompleted);
        _maximumCompleted = maximumCompleted;
    }

    public IReadOnlyList<DialogueDisplayConversation> Entries { get; private set; } = Array.Empty<DialogueDisplayConversation>();

    /// <summary>
    /// Refreshes live display data and consumes only newly disclosed terminal facts.
    /// Gaps discard incomplete local history. Start/choice path duplicates are not
    /// displayed twice; the terminal fact carries the completed informational path.
    /// </summary>
    public void Update(IEnumerable<DialogueConversationSnapshot> live, GamePresentationFactReadResult facts,
        DialogueDefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(definitions);
        _live = live.ToArray();
        if (facts.CursorGap) _completed.Clear();
        foreach (GamePresentationFactEnvelope envelope in facts.Facts.OrderBy(fact => fact.Sequence.Value))
        {
            if (envelope.Sequence.Value <= _lastFactSequence) continue;
            _lastFactSequence = envelope.Sequence.Value;
            if (envelope.Fact is not DialogueEndedFact { Reason: DialogueEndReason.Completed } ended
                || ended.EnteredNodeIds.Count == 0
                || !definitions.TryGet(ended.DefinitionKey, out DialogueDefinition? definition)) continue;
            DialogueSegment[] segments = ended.EnteredNodeIds.SelectMany(id =>
                definition.Nodes.Single(node => node.Id == id).Segments).ToArray();
            if (segments.Length == 0) continue;
            _completed[ended.ConversationId] = (envelope.Sequence.Value, new(ended.ConversationId, ended.DefinitionKey,
                false, false, true, Array.AsReadOnly(segments), Array.Empty<DialogueChoiceSnapshot>()));
        }
        while (_completed.Count > _maximumCompleted)
            _completed.Remove(_completed.MinBy(pair => pair.Value.Sequence).Key);
        PublishEntries();
    }

    /// <summary>Dismisses only a local completed notice; active and suspended conversations require session commands.</summary>
    public bool Dismiss(DialogueConversationId id)
    {
        bool removed = _completed.Remove(id);
        if (removed) PublishEntries();
        return removed;
    }

    /// <summary>Publishes independent immutable collections in authoritative creation order.</summary>
    private void PublishEntries()
    {
        DialogueDisplayConversation[] live = _live.Select(entry => new DialogueDisplayConversation(entry.Id, entry.DefinitionKey,
            entry.Status == DialogueConversationStatus.Suspended, entry.ResponseRequired, false,
            Array.AsReadOnly(entry.Segments.ToArray()), Array.AsReadOnly(entry.Choices.ToArray()))).ToArray();
        var liveIds = live.Select(entry => entry.Id).ToHashSet();
        Entries = Array.AsReadOnly(live.Concat(_completed.Where(pair => !liveIds.Contains(pair.Key)).Select(pair => pair.Value.View))
            .OrderBy(entry => entry.Id.Value).ToArray());
    }
}
