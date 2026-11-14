using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed partial class DialogueOwner
{
    /// <summary>Captures complete authority at an admitted boundary; prepared move tokens never escape a synchronous command.</summary>
    internal DialogueOwnerCheckpoint CaptureCheckpoint() => new(1, new(_nextId),
        Array.AsReadOnly(_uses.Select(use => use.DefinitionKey).Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal)
            .Select(key => new DialogueDefinitionCompatibility(key, DialogueDefinitionFingerprint.Compute(definitions.Get(key)))).ToArray()),
        Array.AsReadOnly(_live.Values.OrderBy(state => state.Id.Value).ToArray()), Array.AsReadOnly(_uses.ToArray()),
        Array.AsReadOnly(_memories.OrderBy(memory => memory.DefinitionKey.ToString(), StringComparer.Ordinal)
            .ThenBy(memory => memory.MemoryId, StringComparer.Ordinal).ThenBy(memory => memory.Scope)
            .ThenBy(memory => memory.Bindings, BindingComparer.Instance).ToArray()));

    /// <summary>
    /// Restores structural receipts and recorded offers directly. Current offer conditions
    /// are never replayed, and presentation resources are excluded from compatibility.
    /// </summary>
    internal static CheckpointResult<DialogueOwner> RestoreCheckpoint(DialogueOwnerCheckpoint? checkpoint,
        DialogueDefinitionCatalog definitions, DialogueReadContext context)
    {
        var owner = new DialogueOwner(definitions);
        if (checkpoint is null) return Invalid("The current schema requires explicit dialogue authority, including an empty owner.");
        try
        {
            if (checkpoint.BehaviorVersion != 1 || checkpoint.Sequences is null || checkpoint.Sequences.NextValue == 0
                || checkpoint.Definitions is null || checkpoint.Conversations is null || checkpoint.Uses is null || checkpoint.Memories is null)
                return Invalid("Dialogue checkpoint fields or behavior version are invalid.");
            var fingerprints = new Dictionary<QualifiedContentKey, string>();
            foreach (DialogueDefinitionCompatibility item in checkpoint.Definitions)
                if (item is null || !fingerprints.TryAdd(item.Key, item.Fingerprint)
                    || !definitions.TryGet(item.Key, out DialogueDefinition? definition) || definition is null
                    || item.Fingerprint != DialogueDefinitionFingerprint.Compute(definition))
                    return CheckpointResult<DialogueOwner>.Rejected(new("$.checkpoint.dialogue.definitions",
                        "Dialogue gameplay definitions are absent or incompatible.", CheckpointFailureKind.IncompatibleContent));
            var histories = new Dictionary<DialogueConversationId, RestoredHistory>();
            ulong? next = 1;
            foreach (DialogueUseReceipt receipt in checkpoint.Uses)
            {
                if (receipt is null || receipt.ConversationId.Value == 0 || !Enum.IsDefined(receipt.Kind)
                    || !fingerprints.ContainsKey(receipt.DefinitionKey)) return Invalid("Dialogue history has an invalid identity or kind.");
                DialogueDefinition definition = definitions.Get(receipt.DefinitionKey);
                var bindings = new DialogueParticipantBindings(definition.Roles, receipt.Bindings.Items);
                if (!bindings.Equals(receipt.Bindings) || !bindings.Items.All(binding => context.WasKnown(binding.Participant)))
                    return Invalid("Historical bindings are noncanonical or name an identity absent from owning history.");
                if (receipt.Kind == DialogueUseKind.Started)
                {
                    if (next != receipt.ConversationId.Value || receipt.Identity is not null || receipt.Consequence is not null
                        || histories.ContainsKey(receipt.ConversationId)
                        || !owner.CanUse(definition, bindings, definition.Repeatability, DialogueUseKind.Started, null, []))
                        return Invalid("Dialogue starts are duplicated, out of order, or violate repeatability.");
                    next = receipt.ConversationId.Value == ulong.MaxValue ? null : receipt.ConversationId.Value + 1;
                    (string? node, string? outcome, IReadOnlyList<string> path) = StructuralEntry(definition, definition.InitialNodeId);
                    histories.Add(receipt.ConversationId, new(definition, bindings, node, outcome, path));
                }
                else
                {
                    if (!histories.TryGetValue(receipt.ConversationId, out RestoredHistory? history)
                        || history.Definition.Key != receipt.DefinitionKey || !history.Bindings.Equals(bindings) || history.Ended)
                        return Invalid("A dialogue receipt lacks an unresolved matching start.");
                    switch (receipt.Kind)
                    {
                        case DialogueUseKind.Choice:
                            if (history.NodeId is null || receipt.Identity is null) return Invalid("A choice follows a terminal node.");
                            DialogueChoice? choice = definition.Nodes.Single(node => node.Id == history.NodeId).Choices.FirstOrDefault(item => item.Id == receipt.Identity);
                            if (choice is null || !owner.CanUse(definition, bindings, choice.Repeatability, DialogueUseKind.Choice, choice.Id, [])
                                || !ValidConsequence(choice, receipt, context)) return Invalid("A choice receipt is incompatible with its node or consequence.");
                            if (choice.Transition is DialogueTransition.Node target)
                                (history.NodeId, history.OutcomeId, history.Path) = StructuralEntry(definition, target.NodeId);
                            else { history.NodeId = null; history.OutcomeId = ((DialogueTransition.Complete)choice.Transition).OutcomeId; history.Path = []; }
                            break;
                        case DialogueUseKind.Completed:
                            if (history.NodeId is not null || history.OutcomeId is null || history.Completed
                                || receipt.Identity is not null || receipt.Consequence is not null) return Invalid("A completion receipt is inconsistent.");
                            history.Completed = true;
                            break;
                        case DialogueUseKind.Outcome:
                            if (!history.Completed || receipt.Identity != history.OutcomeId || receipt.Consequence is not null)
                                return Invalid("A terminal outcome does not match its completion.");
                            history.Ended = true;
                            break;
                        case DialogueUseKind.ParticipantUnavailable:
                            if (history.NodeId is null || receipt.Identity is not null || receipt.Consequence is not null)
                                return Invalid("Participant invalidation must end an unresolved conversation.");
                            if (bindings.Items.All(binding => context.Exists(binding.Participant)))
                                return Invalid("Participant invalidation has no unavailable participant.");
                            history.Ended = true;
                            break;
                        default: return Invalid("An unsupported dialogue receipt was found.");
                    }
                }
                owner._uses.Add(receipt);
            }
            if (next != checkpoint.Sequences.NextValue || !fingerprints.Keys.ToHashSet().SetEquals(owner._uses.Select(use => use.DefinitionKey)))
                return Invalid("Dialogue allocation or definition coverage does not match committed starts.");
            var expectedMemory = new HashSet<DialogueMemoryReceipt>();
            foreach (DialogueUseReceipt use in owner._uses.Where(use => use.Kind == DialogueUseKind.Choice))
            {
                DialogueDefinition definition = definitions.Get(use.DefinitionKey);
                DialogueChoice choice = definition.Nodes.SelectMany(node => node.Choices).Single(choice => choice.Id == use.Identity);
                foreach (string memoryId in choice.MemoryIds)
                {
                    DialogueMemoryDeclaration declaration = definition.Memories.Single(memory => memory.Id == memoryId);
                    expectedMemory.Add(new(use.DefinitionKey, memoryId, declaration.Scope,
                        declaration.Scope == DialogueMemoryScope.Session ? null : use.Bindings));
                }
            }
            if (checkpoint.Memories.Any(memory => memory is null) || checkpoint.Memories.Distinct().Count() != checkpoint.Memories.Count
                || !expectedMemory.SetEquals(checkpoint.Memories)) return Invalid("Dialogue memory does not match committed choices.");
            owner._memories.UnionWith(checkpoint.Memories);
            foreach (DialogueConversationState state in checkpoint.Conversations)
            {
                if (state is null || !Enum.IsDefined(state.Status) || !histories.TryGetValue(state.Id, out RestoredHistory? history)
                    || history.Ended || history.Completed || history.NodeId is null || state.NodeId != history.NodeId
                    || state.DefinitionKey != history.Definition.Key || !state.Bindings.Equals(history.Bindings)
                    || state.OfferedChoiceIds is null || state.EnteredNodeIds is null || !state.EnteredNodeIds.SequenceEqual(history.Path)
                    || !state.Bindings.Items.All(binding => context.Exists(binding.Participant))) return Invalid("A live dialogue has incompatible state or missing participants.");
                DialogueNode node = history.Definition.Nodes.Single(node => node.Id == state.NodeId);
                string[] offered = node.Choices.Where(choice => state.OfferedChoiceIds.Contains(choice.Id, StringComparer.Ordinal)).Select(choice => choice.Id).ToArray();
                if (offered.Length == 0 || !offered.SequenceEqual(state.OfferedChoiceIds) || !owner._live.TryAdd(state.Id,
                    state with { OfferedChoiceIds = Array.AsReadOnly(offered), EnteredNodeIds = Array.AsReadOnly(state.EnteredNodeIds.ToArray()) }))
                    return Invalid("Recorded offers are empty, duplicated, or out of authored order.");
            }
            if (histories.Any(pair => !pair.Value.Ended && !owner._live.ContainsKey(pair.Key)))
                return Invalid("Unresolved dialogue history is missing its live conversation.");
            owner._nextId = checkpoint.Sequences.NextValue;
            return CheckpointResult<DialogueOwner>.Success(owner);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NullReferenceException or KeyNotFoundException)
        {
            return Invalid("Dialogue checkpoint contains invalid structural references.");
        }
    }

    /// <summary>Checks a retained fact against durable structural receipts without replaying any action.</summary>
    internal bool IsValidFact(DialogueFact fact)
    {
        if (fact.DefinitionKey is null || fact.Bindings is null || !_uses.Any(use => use.Kind == DialogueUseKind.Started
            && use.ConversationId == fact.ConversationId && use.DefinitionKey == fact.DefinitionKey && use.Bindings.Equals(fact.Bindings)))
            return false;
        DialogueDefinition definition = definitions.Get(fact.DefinitionKey);
        return fact switch
        {
            DialogueStartedFact started => started.EnteredNodeIds is not null
                && started.EnteredNodeIds.SequenceEqual(StructuralEntry(definition, definition.InitialNodeId).Path),
            DialogueChoiceCommittedFact choice => ValidChoiceFact(choice, definition),
            DialogueEndedFact { Reason: DialogueEndReason.Completed } ended => _uses.Any(use => use.Kind == DialogueUseKind.Outcome
                && use.ConversationId == ended.ConversationId && use.Identity == ended.OutcomeId)
                && ended.EnteredNodeIds is not null && ended.EnteredNodeIds.SequenceEqual(TerminalPath(ended.ConversationId, definition)),
            DialogueEndedFact { Reason: DialogueEndReason.ParticipantUnavailable } ended => ended.OutcomeId is null && _uses.Any(use =>
                use.Kind == DialogueUseKind.ParticipantUnavailable && use.ConversationId == ended.ConversationId)
                && ended.EnteredNodeIds is { Count: 0 },
            DialogueSuspendedFact or DialogueResumedFact => true,
            _ => false,
        };
    }

    /// <summary>Validates every graph-derived choice payload, including the automatic path into its resulting node or outcome.</summary>
    private bool ValidChoiceFact(DialogueChoiceCommittedFact fact, DialogueDefinition definition)
    {
        if (!_uses.Any(use => use.Kind == DialogueUseKind.Choice && use.ConversationId == fact.ConversationId
            && use.Identity == fact.ChoiceId) || fact.EnteredNodeIds is null) return false;
        DialogueChoice choice = definition.Nodes.SelectMany(node => node.Choices).Single(choice => choice.Id == fact.ChoiceId);
        (string? node, string? outcome, IReadOnlyList<string> path) = choice.Transition is DialogueTransition.Node next
            ? StructuralEntry(definition, next.NodeId) : (null, ((DialogueTransition.Complete)choice.Transition).OutcomeId, []);
        return fact.ResultingNodeId == node && fact.OutcomeId == outcome && fact.EnteredNodeIds.SequenceEqual(path);
    }

    /// <summary>Finds the single terminal path from committed history, never from translated or saved display text.</summary>
    private IReadOnlyList<string> TerminalPath(DialogueConversationId id, DialogueDefinition definition)
    {
        DialogueUseReceipt? lastChoice = _uses.LastOrDefault(use => use.ConversationId == id && use.Kind == DialogueUseKind.Choice);
        if (lastChoice is null) return StructuralEntry(definition, definition.InitialNodeId).Path;
        DialogueChoice choice = definition.Nodes.SelectMany(node => node.Choices).Single(choice => choice.Id == lastChoice.Identity);
        return choice.Transition is DialogueTransition.Node next ? StructuralEntry(definition, next.NodeId).Path : [];
    }

    /// <summary>Checks move identity from declared roles; current live targets additionally validate their entity link.</summary>
    private static bool ValidConsequence(DialogueChoice choice, DialogueUseReceipt receipt, DialogueReadContext context)
    {
        if (choice.Move is null) return receipt.Consequence is null;
        if (receipt.Consequence is not { } command || command.RequestedHeading is not null
            || Participant(receipt.Bindings, choice.Move.ActorRoleId) is not DialogueParticipant.Ship actor
            || command.ShipId != actor.Id || command.Placement != choice.Move.Placement || command.Destination is not NavigationDestination.Entity target)
            return false;
        EntityId? expected = context.ResolveHistoricalEntity(Participant(receipt.Bindings, choice.Move.DestinationRoleId));
        return expected is not null && target.EntityId == expected;
    }

    /// <summary>Resolves only graph shape for history validation, preserving offers rather than reevaluating past conditions.</summary>
    private static (string? Node, string? Outcome, IReadOnlyList<string> Path) StructuralEntry(DialogueDefinition definition, string nodeId)
    {
        var path = new List<string>();
        DialogueNode node = definition.Nodes.Single(node => node.Id == nodeId);
        while (true)
        {
            path.Add(node.Id);
            if (node.ResponseRequired) return (node.Id, null, path.AsReadOnly());
            if (node.AutomaticTransition is DialogueTransition.Complete outcome) return (null, outcome.OutcomeId, path.AsReadOnly());
            node = definition.Nodes.Single(item => item.Id == ((DialogueTransition.Node)node.AutomaticTransition!).NodeId);
        }
    }

    private static CheckpointResult<DialogueOwner> Invalid(string message) => CheckpointResult<DialogueOwner>.Rejected(new("$.checkpoint.dialogue", message));

    private sealed class RestoredHistory(DialogueDefinition definition, DialogueParticipantBindings bindings,
        string? nodeId, string? outcomeId, IReadOnlyList<string> path)
    {
        internal DialogueDefinition Definition { get; } = definition;
        internal DialogueParticipantBindings Bindings { get; } = bindings;
        internal string? NodeId { get; set; } = nodeId;
        internal string? OutcomeId { get; set; } = outcomeId;
        internal IReadOnlyList<string> Path { get; set; } = path;
        internal bool Completed { get; set; }
        internal bool Ended { get; set; }
    }

    private sealed class BindingComparer : IComparer<DialogueParticipantBindings?>
    {
        internal static BindingComparer Instance { get; } = new();

        /// <summary>Orders structural bindings by exact role and typed identity rather than randomized lookup hash.</summary>
        public int Compare(DialogueParticipantBindings? left, DialogueParticipantBindings? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            int size = left.Items.Count.CompareTo(right.Items.Count);
            if (size != 0) return size;
            for (int index = 0; index < left.Items.Count; index++)
            {
                DialogueParticipantBinding a = left.Items[index], b = right.Items[index];
                int role = StringComparer.Ordinal.Compare(a.RoleId, b.RoleId);
                if (role != 0) return role;
                int kind = a.Participant.Kind.CompareTo(b.Participant.Kind);
                if (kind != 0) return kind;
                int identity = Value(a.Participant).CompareTo(Value(b.Participant));
                if (identity != 0) return identity;
            }
            return 0;
        }

        private static ulong Value(DialogueParticipant participant) => participant switch
        {
            DialogueParticipant.Ship ship => ship.Id.Value,
            DialogueParticipant.Station station => station.Id.Value,
            DialogueParticipant.Principal principal => principal.Id.Value,
            _ => throw new InvalidOperationException("Unknown participant kind."),
        };
    }
}
