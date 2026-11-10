using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    private readonly Dictionary<EntityId, ObservedStationSnapshot> _stationObservations = [];

    /// <summary>Captures committed station exteriors and sensor sources for diagnostic observation integration.</summary>
    public StationObservationSnapshot CaptureStationObservation()
    {
        EnsureHealthy();
        return _runtime.CaptureStationObservation();
    }

    /// <summary>
    /// Commits persistent exterior evidence only at completed boundaries. Hidden station changes never replace retained data.
    /// </summary>
    private void CommitStationObservations()
    {
        PrincipalId observer = _runtime.PlayerObserverPrincipalId;
        StationObservationSnapshot current = _runtime.CaptureStationObservation();
        if (current.Stations.Count == 0 && _stationObservations.Count == 0) return;
        PlayerCoverageBoundary coverage = _runtime.CaptureCoverageBoundary(observer);
        var live = current.Stations.ToDictionary(view => view.EntityId);
        var proposals = new List<ObservedStationSnapshot>();
        foreach (EntityId id in live.Keys.Union(_stationObservations.Keys).OrderBy(id => id.Value))
        {
            live.TryGetValue(id, out StationExteriorViewSnapshot? view);
            _stationObservations.TryGetValue(id, out ObservedStationSnapshot? prior);
            if (view?.PrincipalId == observer) continue;
            if (view is not null && coverage.Contains(view.Position))
                proposals.Add(new(view, SensorObservationState.Live));
            else if (prior is not null)
                proposals.Add(prior with { State = prior.State == SensorObservationState.ConfirmedMissing
                    || coverage.Contains(prior.View.Position) ? SensorObservationState.ConfirmedMissing : SensorObservationState.Stale });
        }
        foreach (ObservedStationSnapshot proposal in proposals)
            _stationObservations[proposal.View.EntityId] = proposal;
    }

    /// <summary>Returns retained external evidence and always-current owned exteriors without diagnostic station state.</summary>
    private ReadOnlyCollection<ObservedStationSnapshot> CaptureObservedStations() => Array.AsReadOnly(
        _stationObservations.Values.Concat(_runtime.CaptureStationObservation().Stations
            .Where(view => view.PrincipalId == _runtime.PlayerObserverPrincipalId)
            .Select(view => new ObservedStationSnapshot(view, SensorObservationState.Live)))
        .OrderBy(item => item.View.EntityId.Value).ToArray());

    /// <summary>
    /// Validates retained exterior origins and live-state consistency without refreshing stale views from hidden state.
    /// Old observation versions are accepted only when no station history or authority needs preservation.
    /// </summary>
    private static bool IsValidStationObservation(GameSessionCheckpoint checkpoint,
        PlayerObservationCheckpoint observation, ActorOrderRuntimeCoordinator runtime)
    {
        if (observation.BehaviorVersion == 2)
            return checkpoint.Stations is null && (observation.Stations is null || observation.Stations.Count == 0);
        if (observation.Stations is null) return false;
        var live = runtime.CaptureStationObservation().Stations.ToDictionary(view => view.EntityId);
        PlayerCoverageBoundary coverage = runtime.CaptureCoverageBoundary(observation.ObserverPrincipalId);
        var seen = new HashSet<EntityId>();
        foreach (ObservedStationSnapshot? item in observation.Stations)
        {
            StationRuntimeCheckpoint? stations = checkpoint.Stations;
            StationStateCheckpoint? saved = item?.View is { } candidate
                ? stations?.Stations.FirstOrDefault(station => station.Entity == candidate.EntityId) : null;
            StationRemovalReceipt? removal = item?.View is { } removedCandidate
                ? stations?.Removals.FirstOrDefault(receipt => receipt.Station.EntityId == removedCandidate.EntityId) : null;
            StationSnapshot? removed = removal?.Station;
            if (item?.View is not { } view || !Enum.IsDefined(item.State) || !Enum.IsDefined(view.Phase)
                || view.PrincipalId == observation.ObserverPrincipalId || !seen.Add(view.EntityId)
                || view.ObservedAt > runtime.CurrentTime || stations is null || saved is null && removed is null
                || (saved?.Id ?? removed?.Id) != view.Id || (saved?.Principal ?? removed?.PrincipalId) != view.PrincipalId
                || (saved?.DefinitionKey ?? removed?.DefinitionKey) != view.DefinitionKey
                || (saved?.Position ?? removed?.Position) != view.Position || view.Modules is null || view.Services is null
                || stations.Setup.Definitions.Stations.FirstOrDefault(definition => definition.Key == view.DefinitionKey) is not { } definition
                || definition.Name != view.Name)
                return false;
            SimulationTime createdAt = saved?.CreatedAt ?? removal!.CreatedAt;
            if (view.ObservedAt < createdAt || removal is not null && view.ObservedAt > removal.RemovedAt) return false;
            var handles = new HashSet<StationModuleId>();
            foreach (StationModuleExteriorSnapshot? module in view.Modules)
            {
                if (module is null || module.Handle.Value == 0 || !handles.Add(module.Handle) || !Enum.IsDefined(module.Phase)
                    || !stations.Setup.Definitions.Modules.Any(candidate => candidate.Key == module.DefinitionKey))
                    return false;
                bool knownModule = saved?.Modules.Any(known => known.Id == module.Handle && known.DefinitionKey == module.DefinitionKey) == true
                    || saved?.Projects.Any(project => project.ModuleId == module.Handle
                        && (project.ModuleDefinitionKey == module.DefinitionKey || project.UpgradeTarget == module.DefinitionKey)) == true
                    || removed?.Modules.Any(known => known.Id == module.Handle && known.DefinitionKey == module.DefinitionKey) == true
                    || removed?.Projects.Any(project => project.ModuleId == module.Handle
                        && (project.ModuleDefinitionKey == module.DefinitionKey || project.UpgradeTarget == module.DefinitionKey)) == true;
                if (!knownModule) return false;
            }
            var services = new HashSet<StationServiceType>();
            foreach (StationServiceDescriptor? service in view.Services)
                if (service is null || !Enum.IsDefined(service.Type) || !services.Add(service.Type)
                    || service.Type is not (StationServiceType.Storage or StationServiceType.Sensors or StationServiceType.Construction or StationServiceType.Production))
                    return false;
            bool covered = coverage.Contains(view.Position);
            bool exists = live.TryGetValue(view.EntityId, out StationExteriorViewSnapshot? current);
            if (item.State == SensorObservationState.Live && (!covered || !exists || !SameStationExterior(view, current!))
                || item.State == SensorObservationState.Stale && covered
                || item.State == SensorObservationState.ConfirmedMissing && exists)
                return false;
        }
        return live.Values.Where(view => view.PrincipalId != observation.ObserverPrincipalId && coverage.Contains(view.Position))
            .All(view => seen.Contains(view.EntityId));
    }

    /// <summary>Compares list contents as exterior values rather than relying on collection reference identity.</summary>
    private static bool SameStationExterior(StationExteriorViewSnapshot left, StationExteriorViewSnapshot right) =>
        left.Id == right.Id && left.EntityId == right.EntityId && left.DefinitionKey == right.DefinitionKey
        && left.Name == right.Name && left.PrincipalId == right.PrincipalId && left.Position == right.Position
        && left.Phase == right.Phase && left.ObservedAt == right.ObservedAt
        && left.Modules.SequenceEqual(right.Modules) && left.Services.SequenceEqual(right.Services);
}
