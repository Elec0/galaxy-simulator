namespace GalaxyCommand.Simulation;

/// <summary>Exact whole-maneuver contact queries with committed resets at original kinematic boundaries.</summary>
public static class ManeuverSweptRangeQuery
{
    /// <summary>
    /// Searches two immutable maneuver plans through their known overlap.
    /// Original kinematic endpoints use committed positions; inserted schedule
    /// events preserve the underlying curve. No schedule or movement is mutated.
    /// </summary>
    public static SweptRangeContact? FindFirstContact(ExecutableBoundedTerminalManeuverPlan first,
        ExecutableBoundedTerminalManeuverPlan second, SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return Find(new Trajectory(first), new Trajectory(second), from, through, rangeUnits);
    }

    /// <summary>Searches exact maneuver geometry against a known linear motion without rounding sampled positions.</summary>
    public static SweptRangeContact? FindFirstContact(ExecutableBoundedTerminalManeuverPlan maneuver,
        LocalMotionSegment linear, SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(maneuver);
        ArgumentNullException.ThrowIfNull(linear);
        return Find(new Trajectory(maneuver), new Trajectory(linear), from, through, rangeUnits);
    }

    /// <summary>Searches a complete maneuver against a fixed position without extrapolating beyond the plan.</summary>
    public static SweptRangeContact? FindFirstContact(ExecutableBoundedTerminalManeuverPlan motion,
        SystemPosition stationary, SimulationTime from, SimulationTime through, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentOutOfRangeException.ThrowIfZero(stationary.SystemId.Value);
        return Find(new Trajectory(motion), new Trajectory(stationary, from, through), from, through, rangeUnits);
    }

    internal static SweptRangeContact? FindFirstContact(AnalyticManeuverSegment first,
        AnalyticManeuverSegment second, SimulationTime from, SimulationTime through, ulong range) =>
        Find(new Trajectory(first), new Trajectory(second), from, through, range);

    internal static SweptRangeContact? FindFirstContact(AnalyticManeuverSegment first,
        ExecutableBoundedTerminalManeuverPlan second, SimulationTime from, SimulationTime through, ulong range) =>
        Find(new Trajectory(first), new Trajectory(second), from, through, range);

    internal static SweptRangeContact? FindFirstContact(AnalyticManeuverSegment first,
        LocalMotionSegment second, SimulationTime from, SimulationTime through, ulong range) =>
        Find(new Trajectory(first), new Trajectory(second), from, through, range);

    internal static SweptRangeContact? FindFirstContact(AnalyticManeuverSegment first,
        SystemPosition second, SimulationTime from, SimulationTime through, ulong range) =>
        Find(new Trajectory(first), new Trajectory(second, from, through), from, through, range);

    /// <summary>Separates continuous open-end intervals from exact committed boundary instants.</summary>
    private static SweptRangeContact? Find(Trajectory first, Trajectory second,
        SimulationTime from, SimulationTime through, ulong range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));
        SimulationTime start = new(Math.Max(from.Milliseconds, Math.Max(first.StartsAt.Milliseconds, second.StartsAt.Milliseconds)));
        SimulationTime end = new(Math.Min(through.Milliseconds, Math.Min(first.EndsAt.Milliseconds, second.EndsAt.Milliseconds)));
        if (end < start) return null;
        SimulationTime[] boundaries = first.Boundaries.Concat(second.Boundaries).Append(start).Append(end)
            .Where(time => time >= start && time <= end).Distinct().Order().ToArray();
        for (int index = 0; index < boundaries.Length; index++)
        {
            SimulationTime time = boundaries[index];
            if (InRange(first, second, time, range)) return new SweptRangeContact(time, true, time == start);
            if (index == boundaries.Length - 1) break;
            SweptRangeContact? contact = InteractionPhaseRangeQuery.Find(first.CurveAt(time), second.CurveAt(time),
                time, boundaries[index + 1], range, includeThrough: false);
            if (contact is not null)
                return contact with { AlreadyInRange = false, InRangeAtTimestamp = InRange(first, second, contact.Timestamp, range) };
        }
        return null;
    }

    private static bool InRange(Trajectory first, Trajectory second, SimulationTime time, ulong range) =>
        InteractionPhaseRangeQuery.Find(first.InstantAt(time), second.InstantAt(time), time, time, range) is not null;

    private sealed class Trajectory
    {
        private readonly ExecutableBoundedTerminalManeuverPlan? _plan;
        private readonly AnalyticManeuverSegment? _controlled;
        private readonly IReadOnlyList<InteractionPhaseCurve> _curves;
        private readonly HashSet<SimulationTime> _boundaries;

        internal Trajectory(ExecutableBoundedTerminalManeuverPlan plan)
        {
            _plan = plan;
            _curves = plan.InteractionCurves;
            StartsAt = plan.StartsAt;
            EndsAt = plan.EndsAt;
            _boundaries = _curves.Select(curve => curve.StartsAt).Append(EndsAt).ToHashSet();
        }

        internal Trajectory(AnalyticManeuverSegment controlled)
        {
            _controlled = controlled;
            _curves = [InteractionPhaseCurve.From(controlled)];
            StartsAt = controlled.StartsAt;
            EndsAt = controlled.EndsAt;
            _boundaries = [EndsAt];
        }

        internal Trajectory(LocalMotionSegment linear)
        {
            _curves = [InteractionPhaseCurve.From(linear)];
            StartsAt = linear.DepartedAt;
            EndsAt = linear.ArrivesAt;
            _boundaries = [];
        }

        internal Trajectory(SystemPosition position, SimulationTime start, SimulationTime end)
        {
            _curves = [InteractionPhaseCurve.Fixed(position, start, end)];
            StartsAt = start;
            EndsAt = end;
            _boundaries = [];
        }

        internal SimulationTime StartsAt { get; }
        internal SimulationTime EndsAt { get; }
        internal IEnumerable<SimulationTime> Boundaries => _boundaries;

        internal InteractionPhaseCurve CurveAt(SimulationTime time) => _curves.Last(curve => curve.StartsAt <= time);

        /// <summary>Materializes only actual kinematic boundaries, retaining exact curves at other instants.</summary>
        internal InteractionPhaseCurve InstantAt(SimulationTime time) =>
            _plan is not null && _boundaries.Contains(time)
                ? InteractionPhaseCurve.Fixed(_plan.StateAt(time).Position, time, time)
                : _controlled is not null && time == EndsAt
                    ? InteractionPhaseCurve.Fixed(_controlled.StateAt(time).Position, time, time)
                    : CurveAt(time);
    }
}
