using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Explicit dialogue initiation through ordinary session command admission.</summary>
public sealed record StartDialogueCommand : GameplayCommand
{
    /// <summary>Copies structural role bindings; definition compatibility and existence are checked at admission.</summary>
    public StartDialogueCommand(QualifiedContentKey definitionKey, IEnumerable<DialogueParticipantBinding> bindings) : base("dialogue.start")
    {
        ArgumentNullException.ThrowIfNull(definitionKey);
        ArgumentNullException.ThrowIfNull(bindings);
        DefinitionKey = definitionKey;
        Bindings = Array.AsReadOnly(bindings.ToArray());
        if (Bindings.Any(binding => binding is null)) throw new ArgumentException("Bindings cannot contain null.", nameof(bindings));
    }
    public QualifiedContentKey DefinitionKey { get; }
    public IReadOnlyList<DialogueParticipantBinding> Bindings { get; }
}

/// <summary>Selects a recorded offer using technical identity rather than rendered text.</summary>
public sealed record SelectDialogueChoiceCommand : GameplayCommand
{
    /// <summary>Requires positive conversation identity and a stable nonempty choice identity.</summary>
    public SelectDialogueChoiceCommand(DialogueConversationId conversationId, string choiceId) : base("dialogue.select")
    {
        ArgumentOutOfRangeException.ThrowIfZero(conversationId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        ConversationId = conversationId;
        ChoiceId = choiceId;
    }
    public DialogueConversationId ConversationId { get; }
    public string ChoiceId { get; }
}

public sealed record SuspendDialogueCommand : GameplayCommand
{
    /// <summary>Closes authoritative presentation without choosing a response or applying a consequence.</summary>
    public SuspendDialogueCommand(DialogueConversationId conversationId) : base("dialogue.suspend")
    {
        ArgumentOutOfRangeException.ThrowIfZero(conversationId.Value);
        ConversationId = conversationId;
    }
    public DialogueConversationId ConversationId { get; }
}

public sealed record ResumeDialogueCommand : GameplayCommand
{
    /// <summary>Reopens the same authoritative node and recorded offers.</summary>
    public ResumeDialogueCommand(DialogueConversationId conversationId) : base("dialogue.resume")
    {
        ArgumentOutOfRangeException.ThrowIfZero(conversationId.Value);
        ConversationId = conversationId;
    }
    public DialogueConversationId ConversationId { get; }
}
