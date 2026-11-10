using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    private readonly Dictionary<EntityId, ObservedSensorDeployableSnapshot> _deployableObservations = [];

    /// <summary>
    /// Evaluates persistent evidence from a single committed boundary, then
    /// publishes in entity order. Hidden authority may establish absence only
    /// when coverage reaches the remembered position; it never refreshes a view.
    /// </summary>
    private void CommitDeployableObservations()
    {
        PrincipalId observer = _runtime.PlayerObserverPrincipalId;
        SensorDeployableObservationSnapshot input = _runtime.CaptureSensorObservation();
        if (input.Deployables.Count == 0 && _deployableObservations.Count == 0) return;
        PlayerCoverageBoundary coverage = _runtime.CaptureCoverageBoundary(observer);
        var live = input.Deployables.ToDictionary(view => view.EntityId);
        var proposals = new List<ObservedSensorDeployableSnapshot>();
        foreach (EntityId id in live.Keys.Union(_deployableObservations.Keys).OrderBy(id => id.Value))
        {
            live.TryGetValue(id, out SensorDeployableViewSnapshot? current);
            _deployableObservations.TryGetValue(id, out ObservedSensorDeployableSnapshot? prior);
            if (current?.PrincipalId == observer) continue;
            if (current is not null && coverage.Contains(current.Position))
                proposals.Add(new(current, SensorObservationState.Live));
            else if (prior is not null)
                proposals.Add(prior with { State = prior.State == SensorObservationState.ConfirmedMissing
                    || coverage.Contains(prior.View.Position) ? SensorObservationState.ConfirmedMissing : SensorObservationState.Stale });
        }
        foreach (ObservedSensorDeployableSnapshot proposal in proposals)
            _deployableObservations[proposal.View.EntityId] = proposal;
    }

    /// <summary>Skips geometry preparation when no persistent external positions require observation.</summary>
    private SimulationTime? FindNextDeployableObservationBoundary(SimulationTime through)
    {
        SystemPosition[] positions = _runtime.CaptureSensorObservation().Deployables
            .Where(view => view.PrincipalId != _runtime.PlayerObserverPrincipalId).Select(view => view.Position)
            .Concat(_deployableObservations.Values.Select(item => item.View.Position))
            .Concat(_runtime.CaptureStationObservation().Stations
                .Where(view => view.PrincipalId != _runtime.PlayerObserverPrincipalId).Select(view => view.Position))
            .Concat(_stationObservations.Values.Select(item => item.View.Position)).Distinct().ToArray();
        return positions.Length == 0 ? null : _runtime.FindNextObservationBoundary(positions, through);
    }

    /// <summary>Copies retained external evidence and current owned assets without exposing authoritative deployable records.</summary>
    private ReadOnlyCollection<ObservedSensorDeployableSnapshot> CaptureObservedDeployables() => Array.AsReadOnly(
        _deployableObservations.Values.Concat(_runtime.CaptureSensorObservation().Deployables
            .Where(view => view.PrincipalId == _runtime.PlayerObserverPrincipalId)
            .Select(view => new ObservedSensorDeployableSnapshot(view, SensorObservationState.Live)))
        .OrderBy(observation => observation.View.EntityId.Value).ToArray());

    /// <summary>
    /// Rejects malformed or contradictory retained evidence. Removed entities
    /// need no live registration, but every view must match an allocated sensor
    /// identity and its immutable deployment receipt. Coverage remains derived.
    /// </summary>
    private static bool IsValidDeployableObservation(GameSessionCheckpoint checkpoint,
        PlayerObservationCheckpoint observation, ActorOrderRuntimeCoordinator runtime)
    {
        if (observation.Deployables is null) return false;
        var seen = new HashSet<EntityId>();
        var live = runtime.CaptureSensorObservation().Deployables.ToDictionary(view => view.EntityId);
        PlayerCoverageBoundary coverage = runtime.CaptureCoverageBoundary(observation.ObserverPrincipalId);
        var deployed = checkpoint.Lifecycle.SensorDeployables.Receipts
            .Where(receipt => receipt?.Request is SensorActionRequest.Deploy)
            .ToDictionary(receipt => receipt!.Result.Deployed!.EntityId, receipt => receipt!);
        foreach (ObservedSensorDeployableSnapshot? item in observation.Deployables)
        {
            if (item?.View is not { } view || !Enum.IsDefined(item.State) || !seen.Add(view.EntityId)
                || view.PrincipalId == observation.ObserverPrincipalId || view.ObservedAt > checkpoint.Engine.Agenda.CurrentTime
                || !checkpoint.Relationships.Principals.Any(principal => principal?.Id == view.PrincipalId)
                || !checkpoint.WorldTopology.Systems.Any(system => system?.Id == view.Position.SystemId)
                || !deployed.TryGetValue(view.EntityId, out SensorActionReceipt? receipt)
                || receipt.ExecutedAt > view.ObservedAt || receipt.Result.Deployed is not { } sensor
                || sensor.DefinitionKey != view.DefinitionKey || sensor.PrincipalId != view.PrincipalId || sensor.Position != view.Position)
                return false;
            bool covered = coverage.Contains(view.Position);
            bool exists = live.ContainsKey(view.EntityId);
            if (item.State == SensorObservationState.Live && (!covered || !exists || view.ObservedAt != runtime.CurrentTime)
                || item.State == SensorObservationState.Stale && covered
                || item.State == SensorObservationState.ConfirmedMissing && exists) return false;
        }
        // A covered live external entity cannot be omitted to erase discovery.
        return live.Values.Where(view => view.PrincipalId != observation.ObserverPrincipalId && coverage.Contains(view.Position))
            .All(view => seen.Contains(view.EntityId));
    }
}
