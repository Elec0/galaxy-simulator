using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record StationProviderCancellation(EventKey Key, StationProviderAgendaEvent Event);
internal sealed record PreparedStationModuleProvider(PreparedStationProviderChange? Change,
    PreparedStationEconomyLoss? Removal, IReadOnlyList<StationProviderCancellation> Cancellations,
    IReadOnlyList<StationProviderAgendaEvent> Completions);

internal sealed partial class StationRuntimeOwner
{
    private SessionEconomyOwner? _economicProviders;
    private Func<int>? _economicWakeCapacity;

    /// <summary>Binds the fixed economic owner, validating restored links without replaying activation.</summary>
    internal void BindEconomicProviders(SessionEconomyOwner? economy, bool restoring = false, Func<int>? economicWakeCapacity = null)
    {
        _economicProviders = economy;
        _economicWakeCapacity = economicWakeCapacity;
        foreach (StationState station in _stations.Values)
        foreach (ModuleState module in station.Modules.Values)
        {
            bool hasProvider = module.Definition.ProductionProvider is not null || module.Definition.ShipConstructionProvider is not null;
            if (!module.IsCompleted)
            {
                if (module.FacilityId is not null) throw new InvalidOperationException("Unfinished module has an economic provider identity.");
                continue;
            }
            if (!hasProvider && module.FacilityId is null) continue;
            if (restoring)
            {
                if (economy is null || !economy.ValidateStationProviderBinding(station.Id, station.Entity, station.Inventory.Id,
                    station.Principal, station.Position, module.Id, module.FacilityId, module.Definition,
                    station.Phase == StationPhase.Operational && module.Enabled && module.Phase == StationModulePhase.Operational))
                    throw new InvalidOperationException("Restored station provider disagrees with its economic owner.");
            }
            else
            {
                if (!TryPrepareModuleProvider(station, module, module.Definition,
                    station.Phase == StationPhase.Operational && module.Enabled && module.Phase == StationModulePhase.Operational,
                    false, out var prepared)) throw new InvalidOperationException("Initial station economic provider cannot be admitted.");
                ApplyModuleProvider(module, prepared);
            }
        }
        if (restoring && economy is not null && (!ValidateEconomicProviderHistory(economy)
            || economy.CaptureRetiredStationFacilities().Any(facility => !ValidRetiredEconomicFacility(facility))))
            throw new InvalidOperationException("Archived economic provider has no exact completed station disposition.");
        foreach (var receipt in _shipOrders.Values)
            if (economy is null || !economy.TryResolveConstructionOrder(receipt.Facility, receipt.Order, out var design)
                || design != receipt.Design || !economy.CaptureStationFacilities().Concat(economy.CaptureRetiredStationFacilities())
                    .Any(facility => facility.FacilityId == receipt.Facility && facility.StationBinding is { } binding
                        && binding.StationId == receipt.Station && binding.ModuleId == receipt.Module))
                throw new InvalidOperationException("Station queue receipt has no exact retained economic order.");
        foreach (var facility in economy?.CaptureStationFacilities() ?? [])
            if (facility.StationBinding is not { } binding || !_stations.TryGetValue(binding.StationId, out var station)
                || !station.Modules.TryGetValue(binding.ModuleId, out var module) || module.FacilityId != facility.FacilityId)
                throw new InvalidOperationException("Economic provider has no exact live station module.");
    }

    /// <summary>Requires reciprocal records for every established provider, so omitting an archive cannot erase completed work.</summary>
    private bool ValidateEconomicProviderHistory(SessionEconomyOwner economy)
    {
        var descriptors = economy.CaptureStationFacilities().Concat(economy.CaptureRetiredStationFacilities()).ToDictionary(facility => facility.FacilityId);
        bool Exists(StationId station, StationModuleId module, FacilityId? facility) => facility is { Value: > 0 } id
            && descriptors.TryGetValue(id, out var descriptor) && descriptor.StationBinding is { } binding
            && binding.StationId == station && binding.ModuleId == module;
        bool ProjectExists(StationId station, StationProjectKind kind, StationProjectStatus status, StationModuleId? module,
            FacilityId? facility, QualifiedContentKey? definitionKey, QualifiedContentKey? target)
        {
            if (facility is not null && (module is not { } moduleId || !Exists(station, moduleId, facility))) return false;
            if (status != StationProjectStatus.Completed || module is null
                || kind is not (StationProjectKind.ModuleConstruction or StationProjectKind.Upgrade)) return true;
            var key = kind == StationProjectKind.Upgrade ? target : definitionKey;
            var definition = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == key);
            return definition is null || definition.ProductionProvider is null && definition.ShipConstructionProvider is null || facility is not null;
        }
        foreach (var station in _stations.Values)
            foreach (var project in station.Projects.Values)
                if (!ProjectExists(station.Id, project.Kind, project.Status, project.ModuleId, project.ProviderFacilityId,
                    project.ModuleDefinitionKey, project.UpgradeTarget)) return false;
        foreach (var removal in _stationRemovals.Values)
            foreach (var project in removal.Station.Projects)
                if (!ProjectExists(removal.Station.Id, project.Kind, project.Status, project.ModuleId, project.ProviderFacilityId,
                    project.ModuleDefinitionKey, project.UpgradeTarget)) return false;
        foreach (var receipt in _destructionReceipts)
            foreach (var module in receipt.Before.Modules)
                if (module.FacilityId is not null && !Exists(receipt.Before.Id, module.Id, module.FacilityId)) return false;
        foreach (var station in _setup.InitialStations)
            foreach (var module in station.Modules)
                if (module.FacilityId is not null && !Exists(station.Id, module.Id, module.FacilityId)) return false;
        return true;
    }

    /// <summary>Requires exact removed module/facility history; allocator ranges alone do not prove a retired provider existed.</summary>
    private bool ValidRetiredEconomicFacility(EconomyFacilityCheckpoint facility)
    {
        if (facility.StationBinding is not { } binding || facility.StationModuleDefinitionKey is not { } key
            || CaptureStationOrigins().FirstOrDefault(origin => origin.Id == binding.StationId) is not { } origin
            || origin.Entity != binding.StationEntityId || origin.Inventory != facility.InventoryId || origin.Position != facility.Position
            || _stations.GetValueOrDefault(binding.StationId)?.Modules.ContainsKey(binding.ModuleId) == true
            || _setup.Definitions.Modules.FirstOrDefault(definition => definition.Key == key) is not { } definition) return false;
        if (_economicProviders?.ValidateArchivedProviderBinding(facility.FacilityId, definition, origin.Principal) != true) return false;
        bool initial = _setup.InitialStations.FirstOrDefault(seed => seed.Id == binding.StationId)?.Modules
            .Any(module => module.Id == binding.ModuleId && module.FacilityId == facility.FacilityId) == true;
        if (definition.ProductionProvider is null && definition.ShipConstructionProvider is null && !initial) return false;
        if (_destructionReceipts.Any(receipt => receipt.Before.Id == binding.StationId
            && (receipt.Request.DestroyBase || receipt.Request.Modules.Contains(binding.ModuleId))
            && receipt.Before.Modules.Any(module => module.Id == binding.ModuleId && module.DefinitionKey == key
                && module.FacilityId == facility.FacilityId))) return true;
        IEnumerable<StationProjectSnapshot> removedProjects = _stationRemovals.Values.Where(removal => removal.Station.Id == binding.StationId)
            .SelectMany(removal => removal.Station.Projects);
        if (removedProjects.Any(project => project.Kind == StationProjectKind.Dismantling && project.ModuleId == binding.ModuleId
            && project.Status == StationProjectStatus.Completed && project.ModuleDefinitionKey == key
            && project.ProviderFacilityId == facility.FacilityId && project.UpgradeTarget is null)) return true;
        return _stations.GetValueOrDefault(binding.StationId)?.Projects.Values.Any(project =>
            project.Kind == StationProjectKind.Dismantling && !project.DismantleAddition && project.ModuleId == binding.ModuleId
            && project.Status == StationProjectStatus.Completed && project.ModuleDefinitionKey == key
            && project.ProviderFacilityId == facility.FacilityId) == true;
    }

    /// <summary>Includes historical transport use of cleared station storage before its final identity removal.</summary>
    private bool TryPrepareBaseEconomicRemoval(StationState station, out PreparedStationModuleProvider? prepared)
    {
        prepared = null;
        if (_economicProviders is null) return true;
        var removal = _economicProviders.PrepareStationDestruction(station.Id, [], true, []);
        if (removal is null || !removal.CanCommitWithoutSourceLoss()) return false;
        var cancellations = new List<StationProviderCancellation>();
        foreach (var cancellation in removal.Cancellations)
        {
            if (!_agenda.TryFindPending(cancellation.Timestamp, EventPhase.PhysicalCompletion, cancellation.Generation,
                new GameEvent.Economic(cancellation.Event), out var key)) return false;
            cancellations.Add(new(key, cancellation));
        }
        prepared = new(null, removal, cancellations.AsReadOnly(), []);
        return true;
    }

    /// <summary>Rejects a dismantle cancellation before structural mutation when retained economic work cannot resume atomically.</summary>
    private bool CanCancelStructuralProvider(StationState station, ProjectState project) =>
        project.Kind != StationProjectKind.Dismantling || project.DismantleAddition || project.ModuleId is not { } id
        || !station.Modules.TryGetValue(id, out var module)
        || TryPrepareModuleProvider(station, module, module.Definition, module.IsCompleted && module.Enabled, false, out _, includeWake: true);

    /// <summary>Preserves each retained recipe and ship design before admitting upgrade work or material consumption.</summary>
    private bool CanUpgradeModuleProvider(ModuleState module, StationModuleDefinition target) =>
        _economicProviders is not null ? _economicProviders.CanUseStationProviderDefinition(module.FacilityId, target)
            : module.FacilityId is null && target.ProductionProvider is null && target.ShipConstructionProvider is null;

    /// <summary>Joins typed economic effects with exact shared agenda envelopes before any station capacity or service changes.</summary>
    private bool TryPrepareModuleProvider(StationState station, ModuleState module, StationModuleDefinition target,
        bool available, bool removal, out PreparedStationModuleProvider? prepared, bool includeWake = false)
    {
        prepared = null;
        if (module.FacilityId is null && target.ProductionProvider is null && target.ShipConstructionProvider is null) return true;
        if (_economicProviders is null) return false;
        PreparedStationProviderChange? change = null;
        PreparedStationEconomyLoss? remove = null;
        if (removal)
        {
            remove = _economicProviders.PrepareStationDestruction(station.Id, [module.Id], false, []);
            if (remove is null || !remove.CanCommitWithoutSourceLoss()) return false;
        }
        else
        {
            change = _economicProviders.PrepareStationProvider(station.Id, station.Entity, station.Inventory.Id,
                station.Principal, station.Position, module.Id, module.FacilityId, target, available, _agenda.CurrentTime);
            if (change is null || !change.CanCommit()) return false;
        }
        var cancellations = new List<StationProviderCancellation>();
        foreach (var cancellation in change?.Cancellations ?? remove!.Cancellations)
        {
            if (!_agenda.TryFindPending(cancellation.Timestamp, EventPhase.PhysicalCompletion, cancellation.Generation,
                new GameEvent.Economic(cancellation.Event), out EventKey key)) return false;
            cancellations.Add(new(key, cancellation));
        }
        var completions = change?.Completions ?? [];
        try { _agenda.EnsureCreationCapacity(checked(completions.Count + (includeWake ? _economicWakeCapacity?.Invoke() ?? 0 : 0))); }
        catch (OverflowException) { return false; }
        prepared = new(change, remove, cancellations.AsReadOnly(), completions);
        return true;
    }

    /// <summary>Publishes exact agenda replacements and facility identity in the same station commit.</summary>
    private void ApplyModuleProvider(ModuleState? module, PreparedStationModuleProvider? prepared)
    {
        if (prepared is null) return;
        if (prepared.Change is { } change && !change.CanCommit()
            || prepared.Removal is { } removal && !removal.CanCommitWithoutSourceLoss()
            || prepared.Cancellations.Any(value => _agenda.CheckCancellation(value.Key, value.Event.Generation,
                new GameEvent.Economic(value.Event.Event)) != AgendaCancellationCheck.Matches))
            throw new InvalidOperationException("Station economic provider changed before commit.");
        foreach (var cancellation in prepared.Cancellations)
            if (!_agenda.TryCancelExact(cancellation.Key, cancellation.Event.Generation, new GameEvent.Economic(cancellation.Event.Event)))
                throw new InvalidOperationException("Prepared provider completion changed during commit.");
        if (prepared.Change is { } apply)
        {
            apply.Apply();
            if (module is not null) module.FacilityId = apply.FacilityId;
        }
        else prepared.Removal!.ApplyWithoutSourceLoss();
        foreach (var completion in prepared.Completions)
            _agenda.Schedule(completion.Timestamp, EventPhase.PhysicalCompletion, completion.Generation, new GameEvent.Economic(completion.Event));
    }
}
