namespace GalaxyCommand.Simulation;

/// <summary>Session-wide identity of an explicit domain-owned interaction interest.</summary>
public readonly record struct InteractionInterestId : IEntityId<InteractionInterestId>
{
    /// <summary>Creates a nonzero identity supplied at deterministic owner commit.</summary>
    public InteractionInterestId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public static InteractionInterestId Create(ulong value) => new(value);
}
