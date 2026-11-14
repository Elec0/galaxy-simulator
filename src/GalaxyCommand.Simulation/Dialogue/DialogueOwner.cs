using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal enum DialogueUseKind { Started, Choice, Completed, Outcome, ParticipantUnavailable }
internal sealed record DialogueUseReceipt(DialogueConversationId ConversationId, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings, DialogueUseKind Kind, string? Identity, MoveShipCommand? Consequence);
internal sealed record DialogueMemoryReceipt(QualifiedContentKey DefinitionKey, string MemoryId,
    DialogueMemoryScope Scope, DialogueParticipantBindings? Bindings);
internal sealed record DialogueConversationState(DialogueConversationId Id, QualifiedContentKey DefinitionKey,
    DialogueParticipantBindings Bindings, string NodeId, DialogueConversationStatus Status,
    IReadOnlyList<string> OfferedChoiceIds, IReadOnlyList<string> EnteredNodeIds);

/// <summary>One stable owner input. Pure evaluation never queries mutable domains or allocates sequences.</summary>
internal sealed class DialogueReadContext(GameSnapshot world, IEnumerable<ShipId> detectedShips,
    IEnumerable<StationId> detectedStations, IEnumerable<StationId> discoveredStations,
    IReadOnlyDictionary<ShipId, EntityId>? removedShips = null, IReadOnlyDictionary<StationId, EntityId>? removedStations = null)
{
    internal GameSnapshot World { get; } = world;
    private readonly HashSet<ShipId> _ships = world.Ships.Select(ship => ship.Id).ToHashSet();
    private readonly HashSet<StationId> _stations = world.Stations.Select(station => station.Id).ToHashSet();
    private readonly HashSet<PrincipalId> _principals = world.Relationships.Principals.Select(principal => principal.Id).ToHashSet();
    private readonly HashSet<ShipId> _detectedShips = detectedShips.ToHashSet();
    private readonly HashSet<StationId> _detectedStations = detectedStations.ToHashSet();
    private readonly HashSet<StationId> _discoveredStations = discoveredStations.ToHashSet();
    private readonly Dictionary<ShipId, EntityId> _knownShips = world.Ships.Select(ship => new KeyValuePair<ShipId, EntityId>(ship.Id, ship.EntityId))
        .Concat(removedShips ?? new Dictionary<ShipId, EntityId>()).ToDictionary(pair => pair.Key, pair => pair.Value);
    private readonly Dictionary<StationId, EntityId> _knownStations = world.Stations.Select(station => new KeyValuePair<StationId, EntityId>(station.Id, station.EntityId))
        .Concat(removedStations ?? new Dictionary<StationId, EntityId>()).ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>Resolves only the participant's owning domain, never an apparent speaker or facility.</summary>
    internal bool Exists(DialogueParticipant participant) => participant switch
    {
        DialogueParticipant.Ship ship => _ships.Contains(ship.Id),
        DialogueParticipant.Station station => _stations.Contains(station.Id),
        DialogueParticipant.Principal principal => _principals.Contains(principal.Id),
        _ => false,
    };

    /// <summary>Uses current observation for detection and retained permanent-station evidence for discovery.</summary>
    internal bool IsObserved(DialogueParticipant participant, bool discovered) => participant switch
    {
        DialogueParticipant.Ship ship when !discovered => _detectedShips.Contains(ship.Id),
        DialogueParticipant.Station station => discovered ? _discoveredStations.Contains(station.Id) : _detectedStations.Contains(station.Id),
        _ => false,
    };

    /// <summary>Resolves a bound entity destination using the same committed world captured for admission.</summary>
    internal EntityId? ResolveEntity(DialogueParticipant participant) => participant switch
    {
        DialogueParticipant.Ship ship => World.Ships.FirstOrDefault(item => item.Id == ship.Id)?.EntityId,
        DialogueParticipant.Station station => World.Stations.FirstOrDefault(item => item.Id == station.Id)?.EntityId,
        _ => null,
    };

    /// <summary>Validates historical identity through current owners or their validated removal receipts.</summary>
    internal bool WasKnown(DialogueParticipant participant) => participant switch
    {
        DialogueParticipant.Ship ship => _knownShips.ContainsKey(ship.Id),
        DialogueParticipant.Station station => _knownStations.ContainsKey(station.Id),
        DialogueParticipant.Principal principal => _principals.Contains(principal.Id),
        _ => false,
    };

    /// <summary>Resolves an entity link even after removal, without fabricating history for an unknown identity.</summary>
    internal EntityId? ResolveHistoricalEntity(DialogueParticipant participant) => participant switch
    {
        DialogueParticipant.Ship ship => _knownShips.TryGetValue(ship.Id, out EntityId id) ? id : null,
        DialogueParticipant.Station station => _knownStations.TryGetValue(station.Id, out EntityId id) ? id : null,
        _ => null,
    };
}

/// <summary>Buffered dialogue effects, valid only for immediate commit by their preparing owner.</summary>
internal sealed record PreparedDialogueChange(DialogueOwner Owner, ulong Revision, CommandResult Result,
    DialogueConversationId? Id = null, DialogueConversationState? State = null, bool IsStart = false,
    IReadOnlyList<DialogueUseReceipt>? Uses = null, IReadOnlyList<DialogueMemoryReceipt>? Memories = null,
    IReadOnlyList<GameFactProposal>? Facts = null, MoveShipCommand? Move = null);

/// <summary>Authoritative conversation lifecycle and structural memory, isolated from application presentation.</summary>
internal sealed partial class DialogueOwner(DialogueDefinitionCatalog definitions)
{
    private readonly Dictionary<DialogueConversationId, DialogueConversationState> _live = [];
    private readonly List<DialogueUseReceipt> _uses = [];
    private readonly HashSet<DialogueMemoryReceipt> _memories = [];
    private ulong? _nextId = 1;
    private ulong _revision;
    internal bool HasLiveConversations => _live.Count != 0;
    internal bool IsHealthy { get; private set; } = true;

    /// <summary>Projects recorded offers and resources; the session fills admission availability using all affected owners.</summary>
    internal IReadOnlyList<DialogueConversationSnapshot> CaptureRecordedConversations() => Array.AsReadOnly(_live.Values
        .OrderBy(state => state.Id.Value).Select(state =>
        {
            DialogueDefinition definition = definitions.Get(state.DefinitionKey);
            DialogueNode node = definition.Nodes.Single(item => item.Id == state.NodeId);
            DialogueChoiceSnapshot[] choices = state.OfferedChoiceIds.Select(id => node.Choices.Single(choice => choice.Id == id))
                .Select(choice => new DialogueChoiceSnapshot(choice.Id, choice.Order, choice.Label, false)).ToArray();
            return new DialogueConversationSnapshot(state.Id, state.DefinitionKey, state.Bindings, state.NodeId, state.Status,
                node.ResponseRequired, Array.AsReadOnly(state.EnteredNodeIds.SelectMany(id => definition.Nodes.Single(item => item.Id == id).Segments).ToArray()),
                Array.AsReadOnly(choices));
        }).ToArray());

    /// <summary>Evaluates current selection availability without admission sequences or mutation, including the resulting node's staged offers.</summary>
    internal PreparedDialogueChange PrepareSelection(DialogueConversationId id, string choiceId, DialogueReadContext context)
    {
        if (!_live.TryGetValue(id, out DialogueConversationState? state)) return Reject("dialogue-not-found", "The conversation is no longer unresolved.");
        if (!state.Bindings.Items.All(binding => context.Exists(binding.Participant)))
            return Reject("dialogue-participant-unavailable", "A required participant is unavailable.");
        return PrepareChoice(state, definitions.Get(state.DefinitionKey), new SelectDialogueChoiceCommand(id, choiceId), context);
    }

    /// <summary>Validates all entry effects before allocation, memory, or downstream gameplay mutation.</summary>
    internal PreparedDialogueChange Prepare(GameplayCommandEnvelope envelope, CommandSource player, DialogueReadContext context)
    {
        if (!IsHealthy) throw new InvalidOperationException("Dialogue owner is unhealthy.");
        if (envelope.Command is StartDialogueCommand start)
        {
            if (envelope.Source.Kind == CommandSourceKind.Player && envelope.Source != player)
                return Reject("dialogue-invalid-source", "Only the bound player may request player dialogue.");
            return PrepareStart(start, context);
        }
        if (envelope.Source != player) return Reject("dialogue-invalid-source", "Only the bound player may respond to dialogue.");
        DialogueConversationId id = envelope.Command switch
        {
            SelectDialogueChoiceCommand select => select.ConversationId,
            SuspendDialogueCommand suspend => suspend.ConversationId,
            ResumeDialogueCommand resume => resume.ConversationId,
            _ => default,
        };
        if (!_live.TryGetValue(id, out DialogueConversationState? state)) return Reject("dialogue-not-found", "The conversation is no longer unresolved.");
        if (!state.Bindings.Items.All(binding => context.Exists(binding.Participant)))
            return Reject("dialogue-participant-unavailable", "A required participant is unavailable.");
        DialogueDefinition definition = definitions.Get(state.DefinitionKey);
        if (envelope.Command is SelectDialogueChoiceCommand selected)
            return PrepareChoice(state, definition, selected, context);

        bool suspendRequested = envelope.Command is SuspendDialogueCommand;
        DialogueConversationStatus status = suspendRequested ? DialogueConversationStatus.Suspended : DialogueConversationStatus.Active;
        if (state.Status == status) return Reject("dialogue-invalid-state", "The conversation already has the requested presentation state.");
        GameFact fact = suspendRequested ? new DialogueSuspendedFact(id, state.DefinitionKey, state.Bindings)
            : new DialogueResumedFact(id, state.DefinitionKey, state.Bindings);
        return new(this, _revision, CommandResult.Accepted(), id, state with { Status = status },
            Facts: Proposals(id, [fact]));
    }

    /// <summary>Normalizes complete bindings and prepares the initial node before consuming a conversation identity.</summary>
    private PreparedDialogueChange PrepareStart(StartDialogueCommand command, DialogueReadContext context)
    {
        if (!definitions.TryGet(command.DefinitionKey, out DialogueDefinition? definition) || definition is null)
            return Reject("dialogue-definition-not-found", "The dialogue definition is unavailable.");
        DialogueParticipantBindings bindings;
        try { bindings = new(definition.Roles, command.Bindings); }
        catch (ArgumentException) { return Reject("dialogue-invalid-bindings", "Participant bindings do not match the definition."); }
        if (!bindings.Items.All(binding => context.Exists(binding.Participant)))
            return Reject("dialogue-participant-unavailable", "A required participant is unavailable.");
        if (!CanUse(definition, bindings, definition.Repeatability, DialogueUseKind.Started, null, []))
            return Reject("dialogue-repeatability", "The conversation repeatability receipt prevents another start.");
        if (!Conditions(definition.StartConditions, definition, bindings, context, []))
            return Reject("dialogue-start-condition", "The start conditions are not satisfied.");
        if (_nextId is not { } next) return Reject("dialogue-identity-exhausted", "Conversation identities are exhausted.");
        var id = new DialogueConversationId(next);
        var uses = new List<DialogueUseReceipt> { new(id, definition.Key, bindings, DialogueUseKind.Started, null, null) };
        DialogueEntry? entry = Enter(definition, definition.InitialNodeId, bindings, context, [], uses);
        if (entry is null) return Reject("dialogue-no-offers", "A response-required node has no offered choices.");
        var facts = new List<GameFact> { new DialogueStartedFact(id, definition.Key, bindings, entry.NodeIds) };
        DialogueConversationState? state = MakeState(id, definition, bindings, entry);
        if (entry.OutcomeId is { } outcome) Complete(id, definition, bindings, outcome, entry.NodeIds, uses, facts);
        return new(this, _revision, CommandResult.Accepted(), id, state, true, uses.AsReadOnly(), [], Proposals(id, facts));
    }

    /// <summary>Revalidates a recorded offer and evaluates the destination against buffered memory before any owner commits.</summary>
    private PreparedDialogueChange PrepareChoice(DialogueConversationState state, DialogueDefinition definition,
        SelectDialogueChoiceCommand command, DialogueReadContext context)
    {
        if (state.Status != DialogueConversationStatus.Active) return Reject("dialogue-suspended", "Resume this conversation before responding.");
        if (!state.OfferedChoiceIds.Contains(command.ChoiceId, StringComparer.Ordinal)) return Reject("dialogue-choice-not-offered", "The choice was not offered at this node.");
        DialogueChoice choice = definition.Nodes.Single(node => node.Id == state.NodeId).Choices.Single(item => item.Id == command.ChoiceId);
        if (!CanUse(definition, state.Bindings, choice.Repeatability, DialogueUseKind.Choice, choice.Id, [])
            || !Conditions(choice.AdmissionConditions, definition, state.Bindings, context, []))
            return Reject("dialogue-choice-unavailable", "The selected choice is no longer admissible.");
        MoveShipCommand? move = null;
        if (choice.Move is { } consequence)
        {
            DialogueParticipant actor = Participant(state.Bindings, consequence.ActorRoleId);
            EntityId? target = context.ResolveEntity(Participant(state.Bindings, consequence.DestinationRoleId));
            if (actor is not DialogueParticipant.Ship ship || target is null)
                return Reject("dialogue-invalid-consequence", "The move participants cannot be resolved.");
            move = new MoveShipCommand(ship.Id, new NavigationDestination.Entity(target.Value), consequence.Placement);
        }
        var uses = new List<DialogueUseReceipt> { new(state.Id, definition.Key, state.Bindings, DialogueUseKind.Choice, choice.Id, move) };
        DialogueMemoryReceipt[] memories = choice.MemoryIds.Select(id =>
        {
            DialogueMemoryDeclaration declaration = definition.Memories.Single(memory => memory.Id == id);
            return new DialogueMemoryReceipt(definition.Key, id, declaration.Scope,
                declaration.Scope == DialogueMemoryScope.Session ? null : state.Bindings);
        }).ToArray();
        DialogueEntry? entry = choice.Transition switch
        {
            DialogueTransition.Node next => Enter(definition, next.NodeId, state.Bindings, context, memories, uses),
            DialogueTransition.Complete outcome => new(null, [], [], outcome.OutcomeId),
            _ => throw new InvalidOperationException("A validated transition became unsupported."),
        };
        if (entry is null) return Reject("dialogue-no-offers", "The resulting response-required node has no offered choices.");
        var facts = new List<GameFact> { new DialogueChoiceCommittedFact(state.Id, definition.Key, state.Bindings,
            choice.Id, entry.NodeId, entry.OutcomeId, entry.NodeIds) };
        if (entry.OutcomeId is { } terminal) Complete(state.Id, definition, state.Bindings, terminal, entry.NodeIds, uses, facts);
        return new(this, _revision, CommandResult.Accepted(), state.Id, MakeState(state.Id, definition, state.Bindings, entry),
            Uses: uses.AsReadOnly(), Memories: Array.AsReadOnly(memories), Facts: Proposals(state.Id, facts), Move: move);
    }

    /// <summary>Follows validated automatic edges and records the response node's offer set in authored order.</summary>
    private DialogueEntry? Enter(DialogueDefinition definition, string nodeId, DialogueParticipantBindings bindings,
        DialogueReadContext context, IReadOnlyList<DialogueMemoryReceipt> memories, IReadOnlyList<DialogueUseReceipt> uses)
    {
        var entered = new List<string>();
        var nodes = definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        DialogueNode current = nodes[nodeId];
        while (true)
        {
            entered.Add(current.Id);
            if (current.ResponseRequired)
            {
                string[] offered = current.Choices.Where(choice => CanUse(definition, bindings, choice.Repeatability,
                    DialogueUseKind.Choice, choice.Id, uses) && Conditions(choice.OfferConditions, definition, bindings, context, memories))
                    .Select(choice => choice.Id).ToArray();
                return offered.Length == 0 ? null : new(current.Id, Array.AsReadOnly(offered), entered.AsReadOnly(), null);
            }
            if (current.AutomaticTransition is DialogueTransition.Complete terminal)
                return new(null, [], entered.AsReadOnly(), terminal.OutcomeId);
            current = nodes[((DialogueTransition.Node)current.AutomaticTransition!).NodeId];
        }
    }

    /// <summary>Evaluates the closed condition vocabulary over stable domain inputs and staged structural receipts.</summary>
    private bool Conditions(IEnumerable<DialogueCondition> conditions, DialogueDefinition definition, DialogueParticipantBindings bindings,
        DialogueReadContext context, IReadOnlyList<DialogueMemoryReceipt> staged) => conditions.All(condition => condition switch
    {
        DialogueCondition.Receipt receipt => (_memories.Contains(new(definition.Key, receipt.MemoryId, receipt.Scope,
            receipt.Scope == DialogueMemoryScope.Session ? null : bindings)) || staged.Contains(new(definition.Key, receipt.MemoryId,
                receipt.Scope, receipt.Scope == DialogueMemoryScope.Session ? null : bindings))) == receipt.Exists,
        DialogueCondition.Standing standing => Participant(bindings, standing.AssessingRoleId) is DialogueParticipant.Principal assessing
            && Participant(bindings, standing.SubjectRoleId) is DialogueParticipant.Principal subject
            && context.World.Relationships.Standings.Any(value => value.AssessingPrincipalId == assessing.Id
                && value.SubjectPrincipalId == subject.Id && value.Value.Value >= standing.MinimumValue),
        DialogueCondition.Detected detected => context.IsObserved(Participant(bindings, detected.RoleId), false),
        DialogueCondition.Discovered discovered => context.IsObserved(Participant(bindings, discovered.RoleId), true),
        _ => false,
    });

    /// <summary>Matches repeatability structurally; caller order and text never contribute to scope.</summary>
    private bool CanUse(DialogueDefinition definition, DialogueParticipantBindings bindings, DialogueRepeatability repeatability,
        DialogueUseKind kind, string? identity, IReadOnlyList<DialogueUseReceipt> staged) => repeatability == DialogueRepeatability.Repeatable
        || !_uses.Concat(staged).Any(receipt => receipt.DefinitionKey == definition.Key && receipt.Kind == kind && receipt.Identity == identity
            && (repeatability == DialogueRepeatability.OncePerSession || receipt.Bindings.Equals(bindings)));

    /// <summary>Commits one prepared revision. A failure after downstream commit makes the aggregate unsafe to continue.</summary>
    internal IReadOnlyList<GameFactProposal> Commit(PreparedDialogueChange prepared)
    {
        if (!ReferenceEquals(prepared.Owner, this) || prepared.Revision != _revision || !IsHealthy
            || prepared.Result.Status != CommandResultStatus.Accepted || prepared.Id is null)
        {
            Poison();
            throw new InvalidOperationException("Prepared dialogue state no longer matches its owner.");
        }
        if (prepared.IsStart) _nextId = prepared.Id.Value.Value == ulong.MaxValue ? null : prepared.Id.Value.Value + 1;
        if (prepared.State is { } state) _live[prepared.Id.Value] = state;
        else _live.Remove(prepared.Id.Value);
        if (prepared.Uses is { } uses) _uses.AddRange(uses);
        if (prepared.Memories is { } memories) _memories.UnionWith(memories);
        _revision = checked(_revision + 1);
        return prepared.Facts ?? [];
    }

    /// <summary>Invalidates missing participants in stable creation order at the completed simulation boundary.</summary>
    internal IReadOnlyList<GameFactProposal> InvalidateMissing(DialogueReadContext context)
    {
        var facts = new List<GameFactProposal>();
        foreach (DialogueConversationState state in _live.Values.OrderBy(state => state.Id.Value).ToArray())
        {
            if (state.Bindings.Items.All(binding => context.Exists(binding.Participant))) continue;
            _live.Remove(state.Id);
            _uses.Add(new(state.Id, state.DefinitionKey, state.Bindings, DialogueUseKind.ParticipantUnavailable, null, null));
            facts.AddRange(Proposals(state.Id, [new DialogueEndedFact(state.Id, state.DefinitionKey, state.Bindings,
                DialogueEndReason.ParticipantUnavailable, null, [])]));
            _revision = checked(_revision + 1);
        }
        return facts.AsReadOnly();
    }

    internal void Poison() => IsHealthy = false;

    private PreparedDialogueChange Reject(string code, string reason) => new(this, _revision, CommandResult.Rejected(new(code), reason));
    private static DialogueParticipant Participant(DialogueParticipantBindings bindings, string role) => bindings.Items.Single(item => item.RoleId == role).Participant;
    private static DialogueConversationState? MakeState(DialogueConversationId id, DialogueDefinition definition,
        DialogueParticipantBindings bindings, DialogueEntry entry) => entry.NodeId is { } node ? new(id, definition.Key,
            bindings, node, DialogueConversationStatus.Active, entry.Offered, entry.NodeIds) : null;

    /// <summary>Records terminal structural receipts once, without replaying gameplay intent.</summary>
    private static void Complete(DialogueConversationId id, DialogueDefinition definition, DialogueParticipantBindings bindings,
        string outcome, IReadOnlyList<string> nodes, List<DialogueUseReceipt> uses, List<GameFact> facts)
    {
        uses.Add(new(id, definition.Key, bindings, DialogueUseKind.Completed, null, null));
        uses.Add(new(id, definition.Key, bindings, DialogueUseKind.Outcome, outcome, null));
        facts.Add(new DialogueEndedFact(id, definition.Key, bindings, DialogueEndReason.Completed, outcome, nodes));
    }

    private static ReadOnlyCollection<GameFactProposal> Proposals(DialogueConversationId id, IEnumerable<GameFact> facts) => Array.AsReadOnly(facts
        .Select((fact, ordinal) => new GameFactProposal(new GameFactProposalKey(GameFactCommitCategory.Dialogue, id.Value, 0, ordinal), fact)).ToArray());

    private sealed record DialogueEntry(string? NodeId, IReadOnlyList<string> Offered, IReadOnlyList<string> NodeIds, string? OutcomeId);
}
