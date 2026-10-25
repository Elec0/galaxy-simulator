namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private readonly Dictionary<ConstructionDesignId, ulong> _sensorRadii;

    internal bool IsAtCompletedBoundary => _agenda.CurrentPhase is null;

    internal Action? ObservationTimestampCompleted { get; set; }

    internal Func<SimulationTime, SimulationTime?>? NextObservationBoundary { get; set; }

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
        foreach (DeployedSensorSourceSnapshot sensor in CaptureSensorObservation().Sources)
            if (sensor.PrincipalId == observer)
                sources.Add((PreparedInteractionPoint.Capture(
                    InteractionPhaseCurve.Fixed(sensor.Position, CurrentTime, CurrentTime), CurrentTime), sensor.RadiusUnits));
        return new PlayerCoverageBoundary(CurrentTime, sources.ToArray());
    }

    /// <summary>Captures ship coverage from one complete roster, exact geometry and committed stationary sources.</summary>
    internal ShipSensorCoverageSnapshot CaptureShipCoverage(PrincipalId observer)
    {
        InteractionSpatialReadView view = _movement.CaptureInteractionReadView(CurrentTime);
        ShipSensorCoverageInput[] inputs = view.Participants.Select(participant =>
        {
            GameSessionShip ship = _lifecycle.GetRequiredShip(participant.Spatial.ShipId);
            return new ShipSensorCoverageInput(_lifecycle.Entities.GetEntityId(ship.Id)!.Value,
                ship.Id, ship.PrincipalId, _sensorRadii[ship.DesignId]);
        }).ToArray();
        ShipSensorCoverageSnapshot ships = ShipSensorCoverage.Evaluate(view, observer, inputs,
            InteractionPreparationStrategy.BoundingBoxHierarchy(8), 128);
        DeployedSensorSourceSnapshot[] stationary = CaptureSensorObservation().Sources
            .Where(source => source.PrincipalId == observer).OrderBy(source => source.EntityId.Value).ToArray();
        if (stationary.Length == 0) return ships;
        PreparedInteractionProximityScope scope = InteractionProximityQuery.Prepare(view).PrepareScope(
            inputs.Select(input => input.ShipId), InteractionPreparationStrategy.BoundingBoxHierarchy(8));
        var covered = new HashSet<ShipId>(ships.CoveredShipIds);
        foreach (DeployedSensorSourceSnapshot sensor in stationary)
            covered.UnionWith(scope.FindStationary(PreparedInteractionPoint.Capture(
                InteractionPhaseCurve.Fixed(sensor.Position, CurrentTime, CurrentTime), CurrentTime), sensor.RadiusUnits));
        return ships with
        {
            CoveredShipIds = Array.AsReadOnly(covered.OrderBy(id => id.Value).ToArray()),
            VisibleShipIds = Array.AsReadOnly(covered.Union(ships.OwnedShipIds).OrderBy(id => id.Value).ToArray()),
        };
    }

    /// <summary>Reads owned ship capability and exact geometry for pure persistent-target crossing forecasts.</summary>
    internal SimulationTime? FindNextObservationBoundary(IEnumerable<SystemPosition> positions, SimulationTime through)
    {
        InteractionSpatialReadView view = _movement.CaptureInteractionReadView(CurrentTime);
        ShipSensorCoverageInput[] sources = view.Participants.Select(participant => _lifecycle.GetRequiredShip(participant.Spatial.ShipId))
            .Where(ship => ship.PrincipalId == PlayerObserverPrincipalId)
            .Select(ship => new ShipSensorCoverageInput(_lifecycle.Entities.GetEntityId(ship.Id)!.Value,
                ship.Id, ship.PrincipalId, _sensorRadii[ship.DesignId])).ToArray();
        return DeployableObservationCrossings.Next(view, sources, positions, through, _interactionSession?.InteractionExecution);
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
