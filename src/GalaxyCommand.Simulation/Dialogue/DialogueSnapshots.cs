using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public enum DialogueConversationStatus { Active, Suspended }
public enum DialogueEndReason { Completed, ParticipantUnavailable }

/// <summary>A recorded offer with current advisory admission availability and locale-neutral label.</summary>
public sealed record DialogueChoiceSnapshot(string Id, int Order, DialogueText Label, bool IsAvailable,
    CommandRejectionCode? UnavailableReason = null);

/// <summary>Immutable dialogue read model; foreground selection and translated prose are absent.</summary>
public sealed record DialogueConversationSnapshot(DialogueConversationId Id, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings, string NodeId, DialogueConversationStatus Status, bool ResponseRequired,
    IReadOnlyList<DialogueSegment> Segments, IReadOnlyList<DialogueChoiceSnapshot> Choices);

/// <summary>Common structural identity carried by committed dialogue facts.</summary>
public abstract record DialogueFact(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings) : GameFact;

public sealed record DialogueStartedFact(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings, IReadOnlyList<string> EnteredNodeIds) : DialogueFact(ConversationId, DefinitionKey, Bindings);

public sealed record DialogueChoiceCommittedFact(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings, string ChoiceId, string? ResultingNodeId, string? OutcomeId,
    IReadOnlyList<string> EnteredNodeIds) : DialogueFact(ConversationId, DefinitionKey, Bindings);

public sealed record DialogueSuspendedFact(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings) : DialogueFact(ConversationId, DefinitionKey, Bindings);

public sealed record DialogueResumedFact(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings) : DialogueFact(ConversationId, DefinitionKey, Bindings);

public sealed record DialogueEndedFact(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings, DialogueEndReason Reason, string? OutcomeId,
    IReadOnlyList<string> EnteredNodeIds) : DialogueFact(ConversationId, DefinitionKey, Bindings);

/// <summary>Completed-boundary participant validation, independent of presentation reads.</summary>
public sealed record DialogueBoundaryFactCause(SimulationTime Timestamp) : GameFactCause;
