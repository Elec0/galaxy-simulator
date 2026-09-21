namespace GalaxyCommand.Simulation;

/// <summary>
/// An absolute point on the authoritative simulation timeline.
/// </summary>
public readonly record struct SimulationTime : IComparable<SimulationTime>
{
    public static readonly SimulationTime Zero = new(0);

    public SimulationTime(ulong milliseconds)
    {
        Milliseconds = milliseconds;
    }

    public ulong Milliseconds { get; }

    public SimulationTime Add(SimulationDuration duration)
    {
        return new SimulationTime(checked(Milliseconds + duration.Milliseconds));
    }

    public int CompareTo(SimulationTime other) => Milliseconds.CompareTo(other.Milliseconds);

    public static bool operator <(SimulationTime left, SimulationTime right) =>
        left.Milliseconds < right.Milliseconds;

    public static bool operator <=(SimulationTime left, SimulationTime right) =>
        left.Milliseconds <= right.Milliseconds;

    public static bool operator >(SimulationTime left, SimulationTime right) =>
        left.Milliseconds > right.Milliseconds;

    public static bool operator >=(SimulationTime left, SimulationTime right) =>
        left.Milliseconds >= right.Milliseconds;
}
