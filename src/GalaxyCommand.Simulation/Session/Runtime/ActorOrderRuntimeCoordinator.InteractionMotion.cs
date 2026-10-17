namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    // Derived boundary lookup only. The movement binding remains authoritative;
    // gameplay membership is neither inferred nor duplicated here.
    private readonly HashSet<ShipId> _controlledMotionShips = [];

    /// <summary>Tracks a boundary only after the complete movement/agenda handoff commits.</summary>
    internal bool TryHandoffInteractionMotions(IEnumerable<InteractionMotionRequest> requests, InteractionMotionOwner owner)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(requests);
        InteractionMotionRequest[] captured = requests.ToArray();
        if (!_movement.TryHandoffInteractionMotions(captured, owner, CurrentTime, _agenda,
            static item => new GameEvent.SpatialMovement(item))) return false;
        foreach (InteractionMotionRequest request in captured) _controlledMotionShips.Add(request.ShipId);
        return true;
    }

    /// <summary>Removes derived boundaries only after every requested release commits.</summary>
    internal bool TryReleaseInteractionMotions(IEnumerable<InteractionMotionReleaseRequest> requests, InteractionMotionOwner owner,
        out IReadOnlyDictionary<ShipId, ShipKinematicState>? released)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(requests);
        InteractionMotionReleaseRequest[] captured = requests.ToArray();
        if (!_movement.TryReleaseInteractionMotions(captured, owner, CurrentTime, out released)) return false;
        foreach (InteractionMotionReleaseRequest request in captured) _controlledMotionShips.Remove(request.ShipId);
        return true;
    }

    /// <summary>Finds the first live bounded interval end without scanning or evaluating unrelated ships.</summary>
    private SimulationTime? NextControlledMotionBoundary()
    {
        SimulationTime? earliest = null;
        foreach (ShipId ship in _controlledMotionShips)
            if (_movement.GetState(ship) is ShipSpatialState.InteractionControlled controlled &&
                (earliest is null || controlled.Interval.EndsAt < earliest.Value))
                earliest = controlled.Interval.EndsAt;
        return earliest;
    }

    /// <summary>
    /// Rechecks movement boundaries after every completed agenda timestamp, including admissions
    /// from buffered effects. Stops healthy at an interval end until the caller renews or releases.
    /// </summary>
    private RunReport AdvanceWithinControlledMotion(SimulationTime target)
    {
        SimulationTime start = CurrentTime;
        long events = _engine.RunUntil(CurrentTime).EventsProcessed;
        while (!ShouldStop && CurrentTime < target)
        {
            SimulationTime through = target;
            if (NextControlledMotionBoundary() is { } end)
            {
                if (end <= CurrentTime) break;
                if (end < through) through = end;
            }
            if (_agenda.NextEventKey is { } next && next.Timestamp < through) through = next.Timestamp;
            events = checked(events + _engine.RunUntil(through).EventsProcessed);
        }
        return new RunReport(start, CurrentTime, events);
    }
}
