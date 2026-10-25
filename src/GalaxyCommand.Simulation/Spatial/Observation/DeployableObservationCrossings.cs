using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Pure crossing evaluation for moving owned sensors against real stationary observation positions.</summary>
internal static class DeployableObservationCrossings
{
    /// <summary>
    /// Finds the next integral boundary that can change persistent coverage.
    /// System/X-sorted target positions prune each swept source extent. Only
    /// known current motion phases are searched; original motion boundaries
    /// still materialize first. Batches return private minima for stable reduction.
    /// </summary>
    internal static SimulationTime? Next(InteractionSpatialReadView view, ShipSensorCoverageInput[] sources,
        IEnumerable<SystemPosition> positions, SimulationTime through, InteractionBatchExecution? execution)
    {
        if (through <= view.Timestamp || sources.Length == 0) return null;
        var targets = positions.Distinct().GroupBy(position => position.SystemId).ToDictionary(group => group.Key,
            group => group.OrderBy(position => position.Position.X.Units).ThenBy(position => position.Position.Y.Units).ToArray());
        if (targets.Count == 0) return null;
        ShipSensorCoverageInput[][] batches = sources.OrderBy(source => source.EntityId.Value).Chunk(128).ToArray();
        SimulationTime? Evaluate(int batch)
        {
            SimulationTime? earliest = null;
            foreach (ShipSensorCoverageInput source in batches[batch])
            {
                InteractionSpatialParticipant? participant = view.Get(source.ShipId);
                if (participant is null || Curve(participant, view.Timestamp) is not { } curve
                    || !targets.TryGetValue(curve.System, out SystemPosition[]? candidates)) continue;
                SimulationTime end = curve.EndsAt < through ? curve.EndsAt : through;
                if (end <= view.Timestamp || InteractionSweptBoundsQuery.Find(curve, view.Timestamp, end) is not { } bounds) continue;
                BigInteger minX = bounds.MinX - source.RadiusUnits;
                BigInteger maxX = bounds.MaxX + source.RadiusUnits;
                int low = 0, high = candidates.Length;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (candidates[middle].Position.X.Units < minX) low = middle + 1;
                    else high = middle;
                }
                for (int index = low; index < candidates.Length && candidates[index].Position.X.Units <= maxX; index++)
                {
                    SystemPosition target = candidates[index];
                    if (target.Position.Y.Units < bounds.MinY - source.RadiusUnits
                        || target.Position.Y.Units > bounds.MaxY + source.RadiusUnits) continue;
                    InteractionPhaseCurve fixedTarget = InteractionPhaseCurve.Fixed(target, view.Timestamp, end);
                    bool inside = PreparedInteractionPoint.Capture(curve, view.Timestamp).InRange(
                        PreparedInteractionPoint.Capture(fixedTarget, view.Timestamp), source.RadiusUnits);
                    var start = new SimulationTime(checked(view.Timestamp.Milliseconds + 1));
                    SweptRangeContact? contact = InteractionPhaseRangeQuery.Find(curve, fixedTarget,
                        start, end, source.RadiusUnits, outside: inside);
                    if (contact is null) continue;
                    // Capture the last integral sample before a fractional exit,
                    // as well as the first sample after it, independent of caller step size.
                    SimulationTime boundary = contact.Timestamp > start
                        ? new SimulationTime(contact.Timestamp.Milliseconds - 1) : contact.Timestamp;
                    if (earliest is null || boundary < earliest) earliest = boundary;
                }
            }
            return earliest;
        }
        SimulationTime?[] results = execution is null ? Enumerable.Range(0, batches.Length).Select(Evaluate).ToArray()
            : execution.Evaluate(batches.Length, Evaluate);
        return results.Where(result => result is not null).Min();
    }

    /// <summary>Selects the known phase for forecasting, including exact original-boundary starts.</summary>
    private static InteractionPhaseCurve? Curve(InteractionSpatialParticipant participant, SimulationTime now)
    {
        if (participant.ManeuverPlan is { } plan)
            return plan.InteractionCurves.LastOrDefault(curve => curve.StartsAt <= now && curve.EndsAt > now);
        return participant.Spatial.State is ShipSpatialSnapshotState.InteractionControlled || participant.Spatial.Motion is not null
            ? InteractionProximityQuery.Instant(participant, now) : null;
    }
}
