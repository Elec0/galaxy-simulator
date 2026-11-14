using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public enum DialogueRepeatability { Repeatable, OncePerSession, OncePerParticipantBinding }

/// <summary>One ordered authored line and optional apparent-person attribution.</summary>
public sealed class DialogueSegment
{
    /// <summary>Validates stable identity and explicit display order without allocating a person.</summary>
    public DialogueSegment(string id, int order, DialogueText text, DialogueText? speaker = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentNullException.ThrowIfNull(text);
        Id = id; Order = order; Text = text; Speaker = speaker;
    }
    public string Id { get; }
    public int Order { get; }
    public DialogueText Text { get; }
    public DialogueText? Speaker { get; }
}

/// <summary>One explicitly ordered response with immutable declarative admission and effects.</summary>
public sealed class DialogueChoice
{
    /// <summary>Copies conditions and receipt declarations; definition construction resolves all references.</summary>
    public DialogueChoice(string id, int order, DialogueText label, DialogueTransition transition,
        DialogueRepeatability repeatability, IEnumerable<DialogueCondition>? offerConditions = null,
        IEnumerable<DialogueCondition>? admissionConditions = null, IEnumerable<string>? memoryIds = null,
        DialogueMoveConsequence? move = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(transition);
        if (!Enum.IsDefined(repeatability)) throw new ArgumentOutOfRangeException(nameof(repeatability));
        Id = id; Order = order; Label = label; Transition = transition; Repeatability = repeatability;
        OfferConditions = DialogueDefinitionValidation.Copy(offerConditions ?? []);
        AdmissionConditions = DialogueDefinitionValidation.Copy(admissionConditions ?? []);
        MemoryIds = DialogueDefinitionValidation.Ids(memoryIds ?? []);
        Move = move;
    }
    public string Id { get; }
    public int Order { get; }
    public DialogueText Label { get; }
    public DialogueTransition Transition { get; }
    public DialogueRepeatability Repeatability { get; }
    public ReadOnlyCollection<DialogueCondition> OfferConditions { get; }
    public ReadOnlyCollection<DialogueCondition> AdmissionConditions { get; }
    public ReadOnlyCollection<string> MemoryIds { get; }
    public DialogueMoveConsequence? Move { get; }
}

/// <summary>A response node or an informational node with an explicit automatic transition.</summary>
public sealed class DialogueNode
{
    /// <summary>Canonicalizes explicit authored order and rejects ambiguous progress behavior.</summary>
    public DialogueNode(string id, IEnumerable<DialogueSegment> segments, IEnumerable<DialogueChoice> choices,
        DialogueTransition? automaticTransition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        DialogueSegment[] lines = DialogueDefinitionValidation.Copy(segments).ToArray();
        DialogueChoice[] responses = DialogueDefinitionValidation.Copy(choices).ToArray();
        DialogueDefinitionValidation.Unique(lines.Select(line => line.Id));
        DialogueDefinitionValidation.Unique(responses.Select(choice => choice.Id));
        if (lines.Select(line => line.Order).Distinct().Count() != lines.Length
            || responses.Select(choice => choice.Order).Distinct().Count() != responses.Length)
            throw new ArgumentException("Dialogue display order must be unambiguous.");
        if ((responses.Length == 0) == (automaticTransition is null))
            throw new ArgumentException("A node requires either responses or an automatic transition.");
        Id = id;
        Segments = Array.AsReadOnly(lines.OrderBy(line => line.Order).ToArray());
        Choices = Array.AsReadOnly(responses.OrderBy(choice => choice.Order).ToArray());
        AutomaticTransition = automaticTransition;
    }
    public string Id { get; }
    public ReadOnlyCollection<DialogueSegment> Segments { get; }
    public ReadOnlyCollection<DialogueChoice> Choices { get; }
    public DialogueTransition? AutomaticTransition { get; }
    public bool ResponseRequired => Choices.Count != 0;
}

/// <summary>Completely resolved immutable dialogue graph, suitable for session admission.</summary>
public sealed class DialogueDefinition
{
    /// <summary>Rejects duplicate identities, unresolved graph/role/memory references and automatic cycles before publication.</summary>
    public DialogueDefinition(QualifiedContentKey key, IEnumerable<DialogueParticipantRole> roles,
        IEnumerable<DialogueNode> nodes, string initialNodeId, DialogueRepeatability repeatability,
        IEnumerable<DialogueCondition>? startConditions = null, IEnumerable<DialogueMemoryDeclaration>? memories = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.ContentKind.Value != "dialogue") throw new ArgumentException("A dialogue definition requires the dialogue content kind.", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(initialNodeId);
        if (!Enum.IsDefined(repeatability)) throw new ArgumentOutOfRangeException(nameof(repeatability));
        var copiedRoles = DialogueDefinitionValidation.Copy(roles);
        var copiedNodes = DialogueDefinitionValidation.Copy(nodes);
        DialogueDefinitionValidation.Unique(copiedRoles.Select(role => role.Id));
        DialogueDefinitionValidation.Unique(copiedNodes.Select(node => node.Id));
        DialogueDefinitionValidation.Unique(copiedNodes.SelectMany(node => node.Choices).Select(choice => choice.Id));
        Key = key; InitialNodeId = initialNodeId; Repeatability = repeatability;
        Roles = Array.AsReadOnly(copiedRoles.OrderBy(role => role.Id, StringComparer.Ordinal).ToArray());
        Nodes = Array.AsReadOnly(copiedNodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray());
        var declarations = DialogueDefinitionValidation.Copy(memories ?? []);
        DialogueDefinitionValidation.Unique(declarations.Select(memory => memory.Id));
        Memories = Array.AsReadOnly(declarations.OrderBy(memory => memory.Id, StringComparer.Ordinal).ToArray());
        MemoryIds = Array.AsReadOnly(Memories.Select(memory => memory.Id).ToArray());
        StartConditions = DialogueDefinitionValidation.Copy(startConditions ?? []);
        var nodeMap = Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var roleMap = Roles.ToDictionary(role => role.Id, StringComparer.Ordinal);
        if (!nodeMap.ContainsKey(initialNodeId)) throw new ArgumentException("The initial dialogue node must resolve.", nameof(initialNodeId));
        ValidateConditions(StartConditions, roleMap);
        foreach (DialogueNode node in Nodes)
        {
            if (node.AutomaticTransition is { } automatic) ValidateTransition(automatic, nodeMap);
            foreach (DialogueChoice choice in node.Choices)
            {
                ValidateTransition(choice.Transition, nodeMap);
                ValidateConditions(choice.OfferConditions, roleMap);
                ValidateConditions(choice.AdmissionConditions, roleMap);
                foreach (string memory in choice.MemoryIds)
                    if (!MemoryIds.Contains(memory, StringComparer.Ordinal)) throw new ArgumentException("A choice receipt must reference declared memory.");
                if (choice.Move is { } move)
                {
                    RequireRole(move.ActorRoleId, roleMap, [DialogueParticipantKind.Ship]);
                    RequireRole(move.DestinationRoleId, roleMap, [DialogueParticipantKind.Ship, DialogueParticipantKind.Station]);
                    if (!Enum.IsDefined(move.Placement)) throw new ArgumentException("The move placement must be supported.");
                }
            }
        }
        // Resolve the whole graph before following any edge. Shared tails are
        // checked once so validation stays linear in the automatic graph size.
        var finished = new HashSet<string>(StringComparer.Ordinal);
        foreach (DialogueNode node in Nodes)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            DialogueNode current = node;
            while (!finished.Contains(current.Id) && current.AutomaticTransition is DialogueTransition.Node next)
            {
                if (!visited.Add(current.Id)) throw new ArgumentException("Automatic dialogue transitions cannot cycle.");
                current = nodeMap[next.NodeId];
            }
            finished.UnionWith(visited);
            finished.Add(current.Id);
        }
    }

    public QualifiedContentKey Key { get; }
    public ReadOnlyCollection<DialogueParticipantRole> Roles { get; }
    public ReadOnlyCollection<DialogueNode> Nodes { get; }
    public string InitialNodeId { get; }
    public DialogueRepeatability Repeatability { get; }
    public ReadOnlyCollection<DialogueCondition> StartConditions { get; }
    public ReadOnlyCollection<DialogueMemoryDeclaration> Memories { get; }
    public ReadOnlyCollection<string> MemoryIds { get; }

    /// <summary>Resolves only declared structural memory and compatible participant roles.</summary>
    private void ValidateConditions(IEnumerable<DialogueCondition> conditions, IReadOnlyDictionary<string, DialogueParticipantRole> roles)
    {
        foreach (DialogueCondition condition in conditions)
        {
            switch (condition)
            {
                case DialogueCondition.Receipt receipt:
                    if (!Enum.IsDefined(receipt.Scope)
                        || !Memories.Any(memory => memory.Id == receipt.MemoryId && memory.Scope == receipt.Scope))
                        throw new ArgumentException("A receipt condition must match declared memory and its explicit scope.");
                    break;
                case DialogueCondition.Standing standing:
                    RequireRole(standing.AssessingRoleId, roles, [DialogueParticipantKind.Principal]);
                    RequireRole(standing.SubjectRoleId, roles, [DialogueParticipantKind.Principal]);
                    break;
                case DialogueCondition.Detected detected:
                    RequireRole(detected.RoleId, roles, [DialogueParticipantKind.Ship, DialogueParticipantKind.Station]);
                    break;
                case DialogueCondition.Discovered discovered:
                    RequireRole(discovered.RoleId, roles, [DialogueParticipantKind.Station]);
                    break;
                default: throw new ArgumentException("The dialogue condition kind is unsupported.");
            }
        }
    }

    /// <summary>Requires all permitted bindings of a role to support the authored operation.</summary>
    private static void RequireRole(string id, IReadOnlyDictionary<string, DialogueParticipantRole> roles,
        DialogueParticipantKind[]? supported = null)
    {
        if (string.IsNullOrWhiteSpace(id) || !roles.TryGetValue(id, out DialogueParticipantRole? role)
            || supported is not null && role.AcceptedKinds.Any(kind => !supported.Contains(kind)))
            throw new ArgumentException("The condition or consequence references an absent or incompatible participant role.");
    }

    /// <summary>Resolves target nodes and validates terminal outcome identity.</summary>
    private static void ValidateTransition(DialogueTransition transition, Dictionary<string, DialogueNode> nodes)
    {
        if (transition is DialogueTransition.Node node && !string.IsNullOrWhiteSpace(node.NodeId) && nodes.ContainsKey(node.NodeId)) return;
        if (transition is DialogueTransition.Complete complete && !string.IsNullOrWhiteSpace(complete.OutcomeId)) return;
        throw new ArgumentException("Dialogue transitions require a resolved node or nonempty terminal outcome.");
    }
}

internal static class DialogueDefinitionValidation
{
    /// <summary>Defensively copies nonnull immutable model references.</summary>
    internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> source) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        T[] values = source.ToArray();
        if (values.Any(value => value is null)) throw new ArgumentException("Dialogue collections cannot contain null.", nameof(source));
        return Array.AsReadOnly(values);
    }

    /// <summary>Validates definition-local ordinal identities without normalizing their spelling.</summary>
    internal static void Unique(IEnumerable<string> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in source)
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw new ArgumentException("Dialogue identities must be nonempty and unique.");
    }

    /// <summary>Copies identity sets in canonical ordinal order.</summary>
    internal static ReadOnlyCollection<string> Ids(IEnumerable<string> source)
    {
        string[] values = source.ToArray();
        Unique(values);
        return Array.AsReadOnly(values.Order(StringComparer.Ordinal).ToArray());
    }
}
