namespace GalaxyCommand.Simulation;

/// <summary>Read-only instant range discovery over an explicit caller-supplied ship scope.</summary>
public static class InteractionProximityQuery
{
    /// <summary>
    /// Prepares immutable exact local geometry for repeated scoped queries at
    /// this view's timestamp. All captured participants are validated; missing
    /// analytic geometry is rejected. No authoritative state is changed.
    /// </summary>
    public static PreparedInteractionProximityView Prepare(InteractionSpatialReadView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new PreparedInteractionProximityView(view);
    }

    /// <summary>
    /// Returns unique scoped targets in ship identity order at the view's timestamp.
    /// Range follows continuous motion with committed original-boundary resets.
    /// Self, missing, nonlocal, and different-system ships are excluded. Inputs
    /// must contain nonzero identities. No interest, agenda entry, or effect is
    /// created, and no trajectory is extrapolated outside its known interval.
    /// </summary>
    public static IReadOnlyList<ShipId> Find(InteractionSpatialReadView view, ShipId source,
        IEnumerable<ShipId> scope, ulong rangeUnits)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        ShipId[] candidates = scope.Distinct().OrderBy(ship => ship.Value).ToArray();
        foreach (ShipId candidate in candidates) ArgumentOutOfRangeException.ThrowIfZero(candidate.Value, nameof(scope));
        SimulationTime now = view.Timestamp;
        InteractionPhaseCurve? first = Instant(view.Get(source), now);
        if (first is null || InteractionSweptBoundsQuery.Find(first, now, now) is not { } sourceBounds)
            return Array.AsReadOnly(Array.Empty<ShipId>());

        var matches = new List<ShipId>();
        foreach (ShipId candidate in candidates)
        {
            if (candidate == source) continue;
            InteractionPhaseCurve? second = Instant(view.Get(candidate), now);
            if (second is null || second.System != first.System
                || InteractionSweptBoundsQuery.Find(second, now, now) is not { } targetBounds
                || !sourceBounds.MayContact(targetBounds, rangeUnits)) continue;
            if (InteractionPhaseRangeQuery.Find(first, second, now, now, rangeUnits) is not null)
                matches.Add(candidate);
        }
        return Array.AsReadOnly(matches.ToArray());
    }

    /// <summary>
    /// Selects only current local geometry. Completion receipts preserve earlier
    /// swept evidence but do not turn an instant query into a past-contact query.
    /// </summary>
    internal static InteractionPhaseCurve? Instant(InteractionSpatialParticipant? participant, SimulationTime now)
    {
        if (participant?.Spatial.Position is not { } position) return null;
        if (participant.Spatial.Maneuver is not null)
        {
            if (participant.ManeuverPlan is not { } plan)
                throw new InvalidOperationException("Analytic read input is missing its immutable plan geometry.");
            if (now < plan.StartsAt || now > plan.EndsAt) return null;
            // Only original kinematic boundaries materialize a rounded position.
            if (now == plan.EndsAt || plan.InteractionCurves.Any(curve => curve.StartsAt == now))
                return InteractionPhaseCurve.Fixed(plan.StateAt(now).Position, now, now);
            return plan.InteractionCurves.Last(curve => curve.StartsAt <= now);
        }
        if (participant.Spatial.Motion is { } motion)
        {
            if (now < motion.DepartedAt || now > motion.ArrivesAt) return null;
            return InteractionPhaseCurve.From(new LocalMotionSegment(motion.Id, motion.Generation,
                motion.Origin, motion.Destination, motion.DepartedAt, motion.ArrivesAt));
        }
        return InteractionPhaseCurve.Fixed(position, now, now);
    }
}
