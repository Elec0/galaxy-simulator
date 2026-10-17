namespace GalaxyCommand.Simulation;

/// <summary>
/// Canonical unordered discovery pair. Directed gameplay roles remain in the
/// owning interest and are never inferred from this identity order.
/// </summary>
public readonly record struct InteractionShipPair : IComparable<InteractionShipPair>
{
    /// <summary>Normalizes two distinct nonzero ship identities from discovery order.</summary>
    public InteractionShipPair(ShipId first, ShipId second)
    {
        ArgumentOutOfRangeException.ThrowIfZero(first.Value);
        ArgumentOutOfRangeException.ThrowIfZero(second.Value);
        if (first == second)
        {
            throw new ArgumentException("An interaction pair requires two distinct ships.", nameof(second));
        }

        Lower = first.Value < second.Value ? first : second;
        Higher = first.Value < second.Value ? second : first;
    }

    public ShipId Lower { get; }

    public ShipId Higher { get; }

    /// <summary>Compares lower ship identity followed by higher ship identity.</summary>
    public int CompareTo(InteractionShipPair other)
    {
        int result = Lower.Value.CompareTo(other.Lower.Value);
        return result != 0 ? result : Higher.Value.CompareTo(other.Higher.Value);
    }

    public static bool operator <(InteractionShipPair left, InteractionShipPair right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(InteractionShipPair left, InteractionShipPair right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(InteractionShipPair left, InteractionShipPair right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(InteractionShipPair left, InteractionShipPair right) =>
        left.CompareTo(right) >= 0;
}
