namespace GalaxyCommand.Simulation;

/// <summary>
/// A non-negative integer amount of material or storage capacity.
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    public static readonly Quantity Zero = new(0);

    public Quantity(ulong units)
    {
        Units = units;
    }

    public ulong Units { get; }

    public Quantity Add(Quantity other) => new(checked(Units + other.Units));

    public Quantity Subtract(Quantity other)
    {
        if (other > this)
        {
            throw new InvalidOperationException(
                $"Insufficient quantity: requested {other.Units}, available {Units}.");
        }

        return new Quantity(Units - other.Units);
    }

    public Quantity Min(Quantity other) => this <= other ? this : other;

    public int CompareTo(Quantity other) => Units.CompareTo(other.Units);

    public static bool operator <(Quantity left, Quantity right) => left.Units < right.Units;

    public static bool operator <=(Quantity left, Quantity right) => left.Units <= right.Units;

    public static bool operator >(Quantity left, Quantity right) => left.Units > right.Units;

    public static bool operator >=(Quantity left, Quantity right) => left.Units >= right.Units;
}
