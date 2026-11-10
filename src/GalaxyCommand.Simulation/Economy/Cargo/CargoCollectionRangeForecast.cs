namespace GalaxyCommand.Simulation;

internal static class CargoCollectionRangeForecast
{
    /// <summary>
    /// Finds the first strictly outside integral millisecond on one known phase.
    /// The shared polynomial query includes equality, so equality/tangency is sampled
    /// before claiming an exit. Nothing is extrapolated beyond committed geometry.
    /// </summary>
    internal static SimulationTime? FirstStrictExit(InteractionPhaseCurve curve, SystemPosition target,
        SimulationTime from, SimulationTime through, ulong range)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));
        SimulationTime start = new(Math.Max(from.Milliseconds, curve.StartsAt.Milliseconds));
        SimulationTime end = new(Math.Min(through.Milliseconds, curve.EndsAt.Milliseconds));
        if (end < start) return null;
        InteractionPhaseCurve fixedTarget = InteractionPhaseCurve.Fixed(target, start, end);
        bool InRange(SimulationTime at) => PreparedInteractionPoint.Capture(curve, at)
            .InRange(PreparedInteractionPoint.Capture(fixedTarget, at), range);
        if (!InRange(start)) return start;
        while (start < end)
        {
            SimulationTime next = new(checked(start.Milliseconds + 1));
            SweptRangeContact? contact = InteractionPhaseRangeQuery.Find(curve, fixedTarget, next, end, range, outside: true);
            if (contact is null) return null;
            if (!InRange(contact.Timestamp)) return contact.Timestamp;
            // Inclusive equality can be a tangent without any range loss. Resume
            // after that integral sample rather than restarting elapsed collection.
            start = contact.Timestamp;
        }
        return null;
    }
}
