namespace GalaxyCommand.Simulation;

internal sealed partial class StationRuntimeOwner
{
    internal PrincipalId? PrincipalOf(StationId station) => _stations.GetValueOrDefault(station)?.Principal
        ?? _stationRemovals.Values.FirstOrDefault(removal => removal.Station.Id == station)?.Station.PrincipalId;

    /// <summary>Reads committed source geometry without allocating exterior module or service projections.</summary>
    internal IReadOnlyList<StationSensorSourceSnapshot> CaptureSensorSources() => Array.AsReadOnly(
        _stations.Values.Where(station => station.Phase == StationPhase.Operational)
            .Select(station => new StationSensorSourceSnapshot(station.Id, station.Entity,
                station.Principal, station.Position, SensorRadius(station))).ToArray());

    /// <summary>Captures exterior-only station data and committed coverage sources in stable identity order.</summary>
    internal StationObservationSnapshot CaptureObservation(SimulationTime now)
    {
        var views = new List<StationExteriorViewSnapshot>(_stations.Count);
        foreach (StationState station in _stations.Values)
        {
            var services = new SortedDictionary<StationServiceType, bool>();
            bool operational = station.Phase == StationPhase.Operational;
            if (station.Definition.BaseCapacity.Units > 0) services[StationServiceType.Storage] = operational;
            services[StationServiceType.Sensors] = operational;
            foreach (ModuleState module in station.Modules.Values)
            {
                bool available = operational && module.IsCompleted && module.Enabled && module.Phase == StationModulePhase.Operational;
                void Include(StationServiceType type) => services[type] = services.GetValueOrDefault(type) || available;
                if (module.Definition.StorageCapacity.Units > 0) Include(StationServiceType.Storage);
                if (module.Definition.SensorRadiusUnits > 0) Include(StationServiceType.Sensors);
                if (module.Definition.ConstructionCapability is not null) Include(StationServiceType.Construction);
                if (module.Definition.ProductionProvider is not null && module.FacilityId is not null) Include(StationServiceType.Production);
                if (module.Definition.ShipConstructionProvider is not null && module.FacilityId is not null) Include(StationServiceType.Construction);
            }
            views.Add(new(station.Id, station.Entity, station.Definition.Key, station.Definition.Name,
                station.Principal, station.Position, station.Phase, now,
                Array.AsReadOnly(station.Modules.Values.Select(module => new StationModuleExteriorSnapshot(
                    module.Id, module.Definition.Key, module.Phase, module.Enabled)).ToArray()),
                Array.AsReadOnly(services.Select(service => new StationServiceDescriptor(service.Key, service.Value)).ToArray())));
        }
        return new(now, Array.AsReadOnly(views.ToArray()), CaptureSensorSources());
    }
}
