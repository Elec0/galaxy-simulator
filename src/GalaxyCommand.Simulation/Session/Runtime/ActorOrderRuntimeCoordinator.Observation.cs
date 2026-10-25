namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private readonly Dictionary<ConstructionDesignId, ulong> _sensorRadii;

    internal bool IsAtCompletedBoundary => _agenda.CurrentPhase is null;

    internal Action? ObservationTimestampCompleted { get; set; }

    internal PrincipalId PlayerObserverPrincipalId => _relationships.PlayerPrincipalId;

    internal SensorDeployableObservationSnapshot CaptureSensorObservation() => _lifecycle.CaptureSensorObservation(CurrentTime);

    /// <summary>Reads only identity/ownership for a pending fact subject, without constructing a world snapshot.</summary>
    internal PrincipalId? CaptureSubjectPrincipal(ShipId ship) => _lifecycle.Entities.GetEntityId(ship) is null
        ? null : _lifecycle.GetRequiredShip(ship).PrincipalId;

    /// <summary>Joins observation only after every same-time authoritative phase has completed.</summary>
    public void CompleteTimestamp(SimulationTime now) => ObservationTimestampCompleted?.Invoke();

    /// <summary>Prepares exact owned-source geometry for event anchors, including absent removed subjects.</summary>
    internal PlayerCoverageBoundary CaptureCoverageBoundary(PrincipalId observer)
    {
        InteractionSpatialReadView view = _movement.CaptureInteractionReadView(CurrentTime);
        PreparedInteractionProximityView geometry = InteractionProximityQuery.Prepare(view);
        var sources = new List<(PreparedInteractionPoint Point, ulong Radius)>();
        foreach (InteractionSpatialParticipant participant in view.Participants)
        {
            GameSessionShip ship = _lifecycle.GetRequiredShip(participant.Spatial.ShipId);
            if (ship.PrincipalId == observer && geometry.Get(ship.Id) is { } point)
                sources.Add((point, _sensorRadii[ship.DesignId]));
        }
        return new PlayerCoverageBoundary(CurrentTime, sources.ToArray());
    }

    /// <summary>Captures coverage from one complete committed ship roster and immutable geometry.</summary>
    internal ShipSensorCoverageSnapshot CaptureShipCoverage(PrincipalId observer)
    {
        InteractionSpatialReadView view = _movement.CaptureInteractionReadView(CurrentTime);
        ShipSensorCoverageInput[] inputs = view.Participants.Select(participant =>
        {
            GameSessionShip ship = _lifecycle.GetRequiredShip(participant.Spatial.ShipId);
            return new ShipSensorCoverageInput(_lifecycle.Entities.GetEntityId(ship.Id)!.Value,
                ship.Id, ship.PrincipalId, _sensorRadii[ship.DesignId]);
        }).ToArray();
        return ShipSensorCoverage.Evaluate(view, observer, inputs,
            InteractionPreparationStrategy.BoundingBoxHierarchy(8), 128);
    }

    /// <summary>Rejects conflicting definitions rather than letting registration order choose coverage.</summary>
    private static Dictionary<ConstructionDesignId, ulong> BuildSensorRadii(IEnumerable<ShipDesign> designs)
    {
        var radii = new Dictionary<ConstructionDesignId, ulong>();
        foreach (ShipDesign design in designs)
        {
            if (radii.TryGetValue(design.Id, out ulong radius) && radius != design.SensorRadiusUnits)
                throw new ArgumentException($"Conflicting sensor radii for ship design {design.Id}.", nameof(designs));
            radii[design.Id] = design.SensorRadiusUnits;
        }
        return radii;
    }
}

/// <summary>Immutable exact source union for explicit event positions, without a synthetic target entity.</summary>
internal sealed class PlayerCoverageBoundary(SimulationTime timestamp,
    (PreparedInteractionPoint Point, ulong Radius)[] sources)
{
    /// <summary>Uses inclusive exact distances; removed subjects need no live spatial registration.</summary>
    internal bool Contains(SystemPosition position)
    {
        PreparedInteractionPoint target = PreparedInteractionPoint.Capture(
            InteractionPhaseCurve.Fixed(position, timestamp, timestamp), timestamp);
        return sources.Any(source => source.Point.InRange(target, source.Radius));
    }
}
