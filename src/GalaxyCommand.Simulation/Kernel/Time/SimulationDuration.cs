namespace GalaxyCommand.Simulation;

/// <summary>
/// A non-negative duration measured in simulated milliseconds.
/// </summary>
public readonly record struct SimulationDuration : IComparable<SimulationDuration>
{
    public static readonly SimulationDuration Zero = new(0);

    public SimulationDuration(ulong milliseconds)
    {
        Milliseconds = milliseconds;
    }

    public ulong Milliseconds { get; }

    public SimulationDuration Add(SimulationDuration other)
    {
        return new SimulationDuration(checked(Milliseconds + other.Milliseconds));
    }

    public int CompareTo(SimulationDuration other) => Milliseconds.CompareTo(other.Milliseconds);

    public static bool operator <(SimulationDuration left, SimulationDuration right) =>
        left.Milliseconds < right.Milliseconds;

    public static bool operator <=(SimulationDuration left, SimulationDuration right) =>
        left.Milliseconds <= right.Milliseconds;

    public static bool operator >(SimulationDuration left, SimulationDuration right) =>
        left.Milliseconds > right.Milliseconds;

    public static bool operator >=(SimulationDuration left, SimulationDuration right) =>
        left.Milliseconds >= right.Milliseconds;
}
