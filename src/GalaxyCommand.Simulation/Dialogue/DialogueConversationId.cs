using System.Globalization;

namespace GalaxyCommand.Simulation;

/// <summary>Session-local conversation identity, retained continuously across nodes and suspension.</summary>
public readonly record struct DialogueConversationId : IEntityId<DialogueConversationId>
{
    /// <summary>Creates a positive identity for owner allocation and direct checkpoint restoration.</summary>
    public DialogueConversationId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    /// <summary>Supports the shared deterministic identity allocator.</summary>
    public static DialogueConversationId Create(ulong value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
