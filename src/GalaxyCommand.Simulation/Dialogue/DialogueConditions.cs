namespace GalaxyCommand.Simulation;

/// <summary>Scope of a definition-owned structural memory receipt.</summary>
public enum DialogueMemoryScope { Session, ParticipantBinding }

/// <summary>Definition-owned structural memory with an explicit write and lookup scope.</summary>
public sealed record DialogueMemoryDeclaration
{
    /// <summary>Requires a stable memory identity and a supported scope; no implicit write policy is selected.</summary>
    public DialogueMemoryDeclaration(string id, DialogueMemoryScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        Id = id;
        Scope = scope;
    }
    public string Id { get; }
    public DialogueMemoryScope Scope { get; }
}

/// <summary>Closed, declarative condition vocabulary evaluated against stable authoritative inputs.</summary>
public abstract record DialogueCondition
{
    private protected DialogueCondition() { }
    public sealed record Receipt(string MemoryId, DialogueMemoryScope Scope, bool Exists) : DialogueCondition;
    public sealed record Standing(string AssessingRoleId, string SubjectRoleId, long MinimumValue) : DialogueCondition;
    public sealed record Detected(string RoleId) : DialogueCondition;
    public sealed record Discovered(string RoleId) : DialogueCondition;
}

/// <summary>At most one ordinary move consequence, resolved from bound participants at admission.</summary>
public sealed record DialogueMoveConsequence(string ActorRoleId, string DestinationRoleId, OrderPlacement Placement);

/// <summary>A transition names another node or an explicit terminal outcome; it cannot invoke content code.</summary>
public abstract record DialogueTransition
{
    private protected DialogueTransition() { }
    public sealed record Node(string NodeId) : DialogueTransition;
    public sealed record Complete(string OutcomeId) : DialogueTransition;
}
