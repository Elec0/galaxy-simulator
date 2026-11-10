using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed partial class StationRuntimeOwner
{
    private sealed record StationOrigin(StationId Id, EntityId Entity, InventoryId Inventory, PrincipalId Principal,
        QualifiedContentKey DefinitionKey, SystemPosition Position, SimulationTime CreatedAt);

    /// <summary>Validates retired identity evidence without restoring a live station, inventory or builder claim.</summary>
    private bool ValidateAndRestoreRemovalCheckpoints(StationRuntimeCheckpoint checkpoint, SimulationTime time,
        HashSet<SystemId> systems, HashSet<PrincipalId> principals, HashSet<EntityId> entities,
        HashSet<InventoryId> inventories, HashSet<StationProjectId> projects, HashSet<StationModuleId> modules)
    {
        var ids = _stations.Keys.ToHashSet();
        foreach (StationRemovalReceipt? removal in checkpoint.Removals)
        {
            if (removal?.Station is not { } station || !Allocated(station.Id.Value, checkpoint.StationIds)
                || !ids.Add(station.Id) || !entities.Add(station.EntityId) || !inventories.Add(station.InventoryId)
                || !_lifecycle.IsRetiredStationIdentity(station.EntityId, station.InventoryId)
                || !principals.Contains(station.PrincipalId) || !systems.Contains(station.Position.SystemId)
                || station.DefinitionKey is null || _setup.Definitions.Stations.All(definition => definition.Key != station.DefinitionKey)
                || station.Projects is null || station.Modules is null || !Enum.IsDefined(station.Phase)
                || !Enum.IsDefined(removal.Reason) || removal.CreatedAt > removal.RemovedAt || removal.RemovedAt > time
                || !ValidRemovalCause(removal)) return false;
            foreach (StationProjectSnapshot? project in station.Projects)
            {
                if (project is null || !Allocated(project.Id.Value, checkpoint.ProjectIds) || !projects.Add(project.Id)
                    || !Enum.IsDefined(project.Kind) || !Enum.IsDefined(project.Status) || project.RequiredWork.Units == 0
                    || project.CompletedWork.Units > project.RequiredWork.Units || project.ConsumedInputs is null
                    || project.AssignedShips is null || project.ConsumedInputs.Any(pair => pair.Key.Value == 0 || pair.Value.Units == 0
                        || _setup.Materials.Get(pair.Key) is null)
                    || project.ModuleId is { } module && !Allocated(module.Value, checkpoint.ModuleIds)
                    || project.AssignedShips.Any(ship => ship.Value == 0)
                    || project.AssignedShips.Distinct().Count() != project.AssignedShips.Count
                    || project.AssignedModules is { } assigned && assigned.Distinct().Count() != assigned.Count
                    || project.AssignedModules?.Any(id => !Allocated(id.Value, checkpoint.ModuleIds)) == true) return false;
            }
            foreach (StationModuleSnapshot? module in station.Modules)
                if (module is null || !Allocated(module.Id.Value, checkpoint.ModuleIds) || !modules.Add(module.Id)
                    || module.DefinitionKey is null || _setup.Definitions.Modules.All(definition => definition.Key != module.DefinitionKey)
                    || !Enum.IsDefined(module.Phase)) return false;
            StationSnapshot copy = CopyRetiredSnapshot(station);
            _stationRemovals.Add(station.EntityId, removal with { Station = copy });
        }
        return true;
    }

    /// <summary>Checks the immediate causal namespace and timestamp without recreating its already completed event.</summary>
    private bool ValidRemovalCause(StationRemovalReceipt removal) => removal.Cause switch
    {
        CommandFactCause command => command.Sequence.Value != 0,
        ScheduledEventFactCause scheduled => scheduled.Key.Timestamp == removal.RemovedAt
            && Enum.IsDefined(scheduled.Key.Phase) && scheduled.Key.CreationSequence < _agenda.NextCreationSequence,
        EntityRemovalFactCause entity => entity.Request.EntityId == removal.Station.EntityId,
        _ => false,
    };

    /// <summary>Defensively retains historical diagnostic state so a caller cannot mutate a restored tombstone through checkpoint collections.</summary>
    private static StationSnapshot CopyRetiredSnapshot(StationSnapshot snapshot) => snapshot with
    {
        Projects = Array.AsReadOnly(snapshot.Projects.OrderBy(project => project.Id.Value).Select(project => project with
        {
            ConsumedInputs = new ReadOnlyDictionary<MaterialId, Quantity>(new SortedDictionary<MaterialId, Quantity>(
                project.ConsumedInputs.ToDictionary(pair => pair.Key, pair => pair.Value), EntityIdComparer<MaterialId>.Instance)),
            AssignedShips = Array.AsReadOnly(project.AssignedShips.OrderBy(ship => ship.Value).ToArray()),
            AssignedModules = project.AssignedModules is null ? null : Array.AsReadOnly(project.AssignedModules.OrderBy(module => module.Value).ToArray()),
        }).ToArray()),
        Modules = Array.AsReadOnly(snapshot.Modules.OrderBy(module => module.Id.Value).ToArray()),
    };

    /// <summary>Matches retained live or removed identity to its explicit authored origin or accepted site-creation receipt.</summary>
    private bool ValidateStationOrigins()
    {
        StationOrigin[] origins = CaptureStationOrigins();
        var initialStations = new HashSet<StationId>();
        var initialEntities = new HashSet<EntityId>();
        var initialInventories = new HashSet<InventoryId>();
        var initialModules = new HashSet<StationModuleId>();
        var facilities = new HashSet<FacilityId>();
        foreach (InitialStationSetup initial in _setup.InitialStations)
        {
            if (initial.CreatedAt != SimulationTime.Zero || !initialStations.Add(initial.Id) || !initialEntities.Add(initial.EntityId)
                || !initialInventories.Add(initial.InventoryId)) return false;
            StationDefinition? definition = _setup.Definitions.Stations.FirstOrDefault(value => value.Key == initial.DefinitionKey);
            if (definition is null || !definition.PlacementRules.Allows(initial.Position)) return false;
            UInt128 occupied = 0;
            UInt128 physical = definition.BaseCapacity.Units;
            foreach (InitialStationModuleSetup module in initial.Modules)
            {
                StationModuleDefinition? moduleDefinition = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == module.DefinitionKey);
                if (moduleDefinition is null || !initialModules.Add(module.Id) || !Allocated(module.Id.Value, _moduleIds.CaptureCheckpoint())
                    || !definition.AllowedModules.Contains(module.DefinitionKey)
                    || module.FacilityId is { } facility && !facilities.Add(facility)) return false;
                occupied += moduleDefinition.CapacityCost;
                physical += moduleDefinition.StorageCapacity.Units;
            }
            if (occupied > definition.ModuleCapacity || physical > ulong.MaxValue) return false;
            StationOrigin? origin = origins.FirstOrDefault(value => value.Id == initial.Id);
            if (origin is null || origin.Entity != initial.EntityId || origin.Inventory != initial.InventoryId
                || origin.Principal != initial.PrincipalId || origin.DefinitionKey != initial.DefinitionKey
                || origin.Position != initial.Position || origin.CreatedAt != initial.CreatedAt) return false;
        }
        StationOrigin[] constructed = origins.Where(origin => !_setup.InitialStations.Any(initial => initial.Id == origin.Id)).ToArray();
        GameplayCommandEnvelope[] created = _receipts.Values.OrderBy(receipt => receipt.Request.Sequence.Value)
            .Where(receipt => receipt.Request.Command is CreateStationSiteCommand).Select(receipt => receipt.Request).ToArray();
        return created.Length == constructed.Length && created.Zip(constructed).All(pair =>
            pair.First.Command is CreateStationSiteCommand create && create.DefinitionKey == pair.Second.DefinitionKey
            && create.RequestingPrincipal == pair.Second.Principal && create.Position == pair.Second.Position
            && pair.First.SubmittedAt == pair.Second.CreatedAt);
    }

    private StationOrigin[] CaptureStationOrigins() => _stations.Values.Select(station => new StationOrigin(station.Id,
        station.Entity, station.Inventory.Id, station.Principal, station.Definition.Key, station.Position, station.CreatedAt))
        .Concat(_stationRemovals.Values.Select(removal => new StationOrigin(removal.Station.Id, removal.Station.EntityId,
            removal.Station.InventoryId, removal.Station.PrincipalId, removal.Station.DefinitionKey,
            removal.Station.Position, removal.CreatedAt))).OrderBy(origin => origin.Id.Value).ToArray();

    /// <summary>Resolves explicit station targets while leaving site creation associated with its creation receipt.</summary>
    private static StationId? CommandStation(GameplayCommand command) => command switch
    {
        AssignStationBuilderCommand assign => assign.Station,
        ReleaseStationBuilderCommand release => release.Station,
        ControlStationProjectCommand control => control.Station,
        ConstructStationModuleCommand construct => construct.Station,
        UpgradeStationModuleCommand upgrade => upgrade.Station,
        DismantleStationModuleCommand dismantle => dismantle.Station,
        DismantleStationBaseCommand dismantle => dismantle.Station,
        SetStationModuleEnabledCommand enabled => enabled.Station,
        EnqueueStationShipCommand enqueue => enqueue.Station,
        AssignStationModuleBuilderCommand assign => assign.Station,
        SetStationRecoveryDestinationCommand destination => destination.Station,
        _ => null,
    };

    /// <summary>Validates old accepted command targets against retained removal evidence rather than requiring a vanished live owner.</summary>
    private bool ValidRemovedStationCommand(GameplayCommand command, StationId target)
    {
        StationSnapshot? station = _stationRemovals.Values.FirstOrDefault(removal => removal.Station.Id == target)?.Station;
        if (station is null || station.PrincipalId != CommandPrincipal(command)) return false;
        return command switch
        {
            AssignStationBuilderCommand assign => assign.BuilderShip.Value != 0 && station.Projects.Any(project => project.Id == assign.Project),
            ReleaseStationBuilderCommand release => release.BuilderShip.Value != 0 && station.Projects.Any(project => project.Id == release.Project),
            ControlStationProjectCommand control => Enum.IsDefined(control.Action) && station.Projects.Any(project => project.Id == control.Project),
            ConstructStationModuleCommand construct => _setup.Definitions.Stations.Single(definition => definition.Key == station.DefinitionKey)
                .AllowedModules.Contains(construct.DefinitionKey),
            UpgradeStationModuleCommand upgrade => Allocated(upgrade.Module.Value, _moduleIds.CaptureCheckpoint()) && upgrade.UpgradeTarget is not null,
            DismantleStationModuleCommand dismantle => Allocated(dismantle.Module.Value, _moduleIds.CaptureCheckpoint()) && dismantle.RecoveryInventory.Value != 0,
            SetStationModuleEnabledCommand enabled => Allocated(enabled.Module.Value, _moduleIds.CaptureCheckpoint()),
            EnqueueStationShipCommand enqueue => Allocated(enqueue.Module.Value, _moduleIds.CaptureCheckpoint())
                && enqueue.ExpectedFacility.Value != 0 && enqueue.Design.Value != 0,
            AssignStationModuleBuilderCommand assign => Allocated(assign.BuilderModule.Value, _moduleIds.CaptureCheckpoint())
                && station.Projects.Any(project => project.Id == assign.Project),
            SetStationRecoveryDestinationCommand destination => destination.RecoveryInventory.Value != 0
                && station.Projects.Any(project => project.Id == destination.Project),
            DismantleStationBaseCommand dismantle => dismantle.RecoveryInventory.Value != 0,
            _ => false,
        };
    }
}

internal sealed partial class StationRuntimeOwner
{
    /// <summary>Proves retired structural definition keys from exact accepted admissions and prior completed upgrades.</summary>
    private bool ValidateRetiredProjectDefinitions()
    {
        if (_stationRemovals.Count == 0) return true;
        Dictionary<StationProjectId, GameplayCommand>? admissions = CaptureAllProjectAdmissions();
        if (admissions is null) return false;
        foreach (StationRemovalReceipt removal in _stationRemovals.Values)
        {
            StationSnapshot station = removal.Station;
            StationDefinition definition = _setup.Definitions.Stations.Single(value => value.Key == station.DefinitionKey);
            var moduleKeys = _setup.InitialStations.FirstOrDefault(seed => seed.Id == station.Id)?.Modules
                .ToDictionary(module => module.Id, module => module.DefinitionKey) ?? [];
            var knownKeys = new Dictionary<StationModuleId, QualifiedContentKey>(moduleKeys);
            var additions = new Dictionary<StationModuleId, QualifiedContentKey>();
            var removedModules = new HashSet<StationModuleId>();
            foreach (StationProjectSnapshot project in station.Projects.OrderBy(project => project.Id.Value))
            {
                if (!admissions.TryGetValue(project.Id, out GameplayCommand? command)) return false;
                ConstructionRecipe? recipe;
                if (project.Kind == StationProjectKind.BaseConstruction)
                {
                    if (command is not (CreateStationSiteCommand or ControlStationProjectCommand { Action: StationProjectAction.RestartBaseConstruction })
                        || project.ModuleId is not null || project.ModuleDefinitionKey is not null || project.UpgradeTarget is not null) return false;
                    recipe = definition.BaseRecipe;
                }
                else if (project.Kind == StationProjectKind.ModuleConstruction)
                {
                    if (command is not ConstructStationModuleCommand construct || project.ModuleId is not { } id
                        || project.ModuleDefinitionKey != construct.DefinitionKey || project.UpgradeTarget is not null
                        || knownKeys.ContainsKey(id) || !definition.AllowedModules.Contains(construct.DefinitionKey)) return false;
                    StationModuleDefinition? module = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == construct.DefinitionKey);
                    if (module is null) return false;
                    recipe = module.ConstructionRecipe;
                    moduleKeys.Add(id, module.Key);
                    knownKeys.Add(id, module.Key);
                    if (project.Status == StationProjectStatus.Cancelled && project.ConsumedInputs.Count == 0) moduleKeys.Remove(id);
                }
                else if (project.Kind == StationProjectKind.Upgrade)
                {
                    if (command is not UpgradeStationModuleCommand upgrade || project.ModuleId != upgrade.Module
                        || project.UpgradeTarget != upgrade.UpgradeTarget || !moduleKeys.TryGetValue(upgrade.Module, out QualifiedContentKey? current)
                        || project.ModuleDefinitionKey != current || additions.ContainsKey(upgrade.Module)) return false;
                    StationModuleDefinition? module = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == current);
                    recipe = module?.UpgradePaths.FirstOrDefault(path => path.TargetKey == upgrade.UpgradeTarget)?.Recipe;
                    if (project.Status == StationProjectStatus.Completed)
                    {
                        moduleKeys[upgrade.Module] = upgrade.UpgradeTarget;
                        knownKeys[upgrade.Module] = upgrade.UpgradeTarget;
                    }
                    else if (project.Status == StationProjectStatus.Cancelled && project.ConsumedInputs.Count != 0)
                        additions.Add(upgrade.Module, upgrade.UpgradeTarget);
                }
                else if (project.Kind == StationProjectKind.Dismantling && project.ModuleId is null)
                {
                    if (command is not DismantleStationBaseCommand || project.ModuleDefinitionKey is not null || project.UpgradeTarget is not null)
                        return false;
                    recipe = new([], definition.DismantleWork);
                }
                else if (project.Kind == StationProjectKind.Dismantling)
                {
                    if (command is not DismantleStationModuleCommand dismantle || project.ModuleId != dismantle.Module
                        || project.UpgradeTarget is not null || !moduleKeys.TryGetValue(dismantle.Module, out QualifiedContentKey? current)) return false;
                    QualifiedContentKey expected = current;
                    if (dismantle.UnfinishedUpgradeAddition)
                    {
                        if (!additions.TryGetValue(dismantle.Module, out QualifiedContentKey? addition)) return false;
                        expected = addition;
                    }
                    else if (additions.ContainsKey(dismantle.Module)) return false;
                    if (project.ModuleDefinitionKey != expected) return false;
                    StationModuleDefinition? module = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == expected);
                    recipe = module is null ? null : new([], module.DismantleWork);
                    if (project.Status == StationProjectStatus.Completed)
                    {
                        if (dismantle.UnfinishedUpgradeAddition) additions.Remove(dismantle.Module);
                        else { moduleKeys.Remove(dismantle.Module); removedModules.Add(dismantle.Module); }
                    }
                }
                else return false;
                if (recipe is null || !ValidRetiredConsumption(project, recipe)) return false;
            }
            if (removal.Reason == StationRemovalReason.Dismantled && moduleKeys.Count != 0) return false;
            foreach (StationModuleSnapshot module in station.Modules)
                if (!knownKeys.TryGetValue(module.Id, out QualifiedContentKey? key) || module.DefinitionKey != key
                    || removedModules.Contains(module.Id)) return false;
        }
        return true;
    }

    /// <summary>Matches retained project identities to all accepted project-creating commands before interpreting historical recipe keys.</summary>
    private Dictionary<StationProjectId, GameplayCommand>? CaptureAllProjectAdmissions()
    {
        StationOrigin[] constructed = CaptureStationOrigins().Where(origin => !_setup.InitialStations.Any(seed => seed.Id == origin.Id)).ToArray();
        var admissions = new List<(StationId Station, GameplayCommand Command)>();
        int created = 0;
        foreach (var receipt in _receipts.Values.OrderBy(receipt => receipt.Request.Sequence.Value))
        {
            GameplayCommand command = receipt.Request.Command;
            if (command is CreateStationSiteCommand)
            {
                if (created >= constructed.Length) return null;
                admissions.Add((constructed[created++].Id, command));
            }
            else if (command is ConstructStationModuleCommand or UpgradeStationModuleCommand or DismantleStationModuleCommand
                or DismantleStationBaseCommand or ControlStationProjectCommand { Action: StationProjectAction.RestartBaseConstruction }
                && CommandStation(command) is { } target) admissions.Add((target, command));
        }
        var projects = _stations.Values.SelectMany(station => station.Projects.Values.Select(project => (Station: station.Id, Project: project.Id)))
            .Concat(_stationRemovals.Values.SelectMany(removal => removal.Station.Projects.Select(project =>
                (Station: removal.Station.Id, Project: project.Id)))).OrderBy(value => value.Project.Value).ToArray();
        if (projects.Length != admissions.Count) return null;
        var result = new Dictionary<StationProjectId, GameplayCommand>();
        for (int index = 0; index < projects.Length; index++)
        {
            if (projects[index].Station != admissions[index].Station) return null;
            result.Add(projects[index].Project, admissions[index].Command);
        }
        return result;
    }

    /// <summary>Checks exact cumulative material consumption against the proven construction or upgrade recipe, including retired partial work.</summary>
    private static bool ValidRetiredConsumption(StationProjectSnapshot project, ConstructionRecipe recipe)
    {
        if (project.RequiredWork != recipe.RequiredWork || project.CompletedWork.Units > recipe.RequiredWork.Units) return false;
        int inputs = 0;
        foreach ((MaterialId material, Quantity required) in recipe.Inputs)
        {
            ulong expected = (ulong)(((UInt128)required.Units * project.CompletedWork.Units + recipe.RequiredWork.Units - 1)
                / recipe.RequiredWork.Units);
            if (project.ConsumedInputs.GetValueOrDefault(material, Quantity.Zero).Units != expected) return false;
            if (expected != 0) inputs++;
        }
        return project.ConsumedInputs.Count == inputs;
    }
}
