using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed partial class StationRuntimeOwner
{
    /// <summary>Restores immutable causal witnesses before validating live module absence and retained accounting.</summary>
    private bool RestoreDestructionHistory(StationRuntimeCheckpoint checkpoint, SimulationTime time)
    {
        try { return RestoreDestructionWitnesses(checkpoint, time); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { return false; }
    }

    /// <summary>Copies only fully resolved, sequential accounting witnesses into a still-private owner.</summary>
    private bool RestoreDestructionWitnesses(StationRuntimeCheckpoint checkpoint, SimulationTime time)
    {
        var occurrences = new Dictionary<StationId, ulong>();
        var windows = new Dictionary<StationId, StationStorageLossWindow>();
        var identities = new HashSet<(StationId, CargoDispositionTrigger, ulong)>();
        foreach (StationDestructionReceipt? receipt in checkpoint.Destructions)
        {
            if (receipt?.Request is not { } request || receipt.Before is not { } before || receipt.InventoryBefore is not { } stock
                || request.Trigger is not (CargoDispositionTrigger.Command or CargoDispositionTrigger.Scheduled)
                || before.Modules is null || before.Projects is null || before.Modules.Any(module => module is null)
                || before.Projects.Any(project => project is null) || request.Modules is null || request.Modules.Any(module => module.Value == 0)
                || request.Modules.Distinct().Count() != request.Modules.Count || request.Station != before.Id
                || request.ExpectedRevision != before.Revision || request.Occurrence == 0
                || !Allocated(before.Id.Value, checkpoint.StationIds) || !identities.Add((before.Id, request.Trigger, request.Occurrence))
                || occurrences.GetValueOrDefault(before.Id) == ulong.MaxValue
                || request.Occurrence != occurrences.GetValueOrDefault(before.Id) + 1
                || receipt.CargoKey != new CargoDispositionKey(request.Trigger, before.EntityId, request.Occurrence)
                || stock.Id != before.InventoryId || receipt.CommittedAt > time || receipt.WindowBefore is null || receipt.WindowAfter is null
                || request.Trigger is CargoDispositionTrigger.Scheduled scheduled
                    && (scheduled.Key.Timestamp != receipt.CommittedAt || !Enum.IsDefined(scheduled.Key.Phase)
                        || scheduled.Key.CreationSequence >= _agenda.NextCreationSequence)
                || !WindowsEqual(receipt.WindowBefore, windows.GetValueOrDefault(before.Id, StationStorageLossWindow.Empty))
                || request.DestroyBase && !WindowsEqual(receipt.WindowAfter, StationStorageLossWindow.Empty)) return false;
            occurrences[before.Id] = request.Occurrence;
            windows[before.Id] = receipt.WindowAfter;
            _destructionReceipts.Add(receipt with { Request = request with
            { Modules = Array.AsReadOnly(request.Modules.OrderBy(module => module.Value).ToArray()) }, Before = CopyRetiredSnapshot(before) });
        }
        foreach (StationStateCheckpoint station in checkpoint.Stations)
            if (station.LossWindow is null || station.LossOccurrence != occurrences.GetValueOrDefault(station.Id)
                || !WindowsEqual(station.LossWindow, windows.GetValueOrDefault(station.Id, StationStorageLossWindow.Empty))) return false;
        foreach (StationDestructionReceipt receipt in _destructionReceipts)
            if (!checkpoint.Stations.Any(station => station.Id == receipt.Before.Id)
                && !checkpoint.Removals.Any(removal => removal.Station.Id == receipt.Before.Id)) return false;
        foreach (StationRemovalReceipt removal in checkpoint.Removals.Where(item => item.Reason == StationRemovalReason.Destroyed))
            if (!_destructionReceipts.Any(receipt => receipt.Request.DestroyBase && receipt.Before.Id == removal.Station.Id
                && receipt.CommittedAt == removal.RemovedAt && Cause(receipt.Request.Trigger) == removal.Cause)) return false;
        return true;
    }

    private bool WasModuleDestroyed(StationId station, StationModuleId module) => _destructionReceipts.Any(receipt =>
        receipt.Before.Id == station && receipt.Before.Modules.Any(item => item.Id == module)
        && (receipt.Request.DestroyBase || receipt.Request.Modules.Contains(module)));

    /// <summary>Recomputes selection/accounting from retained stock and root seed, then joins the exact cargo occurrence and removal evidence.</summary>
    internal CheckpointValidationFailure? ValidateDestructionCargoHistory()
    {
        try { return ValidateDestructionCargoWitnesses(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { return new("$.checkpoint.stations.destructions", "Station destruction evidence is malformed or exceeds numeric limits."); }
    }

    /// <summary>Validates private restored witnesses before any station destruction history becomes observable.</summary>
    private CheckpointValidationFailure? ValidateDestructionCargoWitnesses()
    {
        if (_destructionReceipts.Count == 0) return null;
        const string path = "$.checkpoint.stations.destructions";
        if (_destructionCargo is null || _destructionCargoPolicy is null || _destructionDefinitions is null || _destructionRandom is null)
            return new(path, "Station destruction requires its retained cargo authority and policies.");
        foreach (StationDestructionReceipt receipt in _destructionReceipts)
        {
            StationSnapshot before = receipt.Before;
            StationDestructionRequest request = receipt.Request;
            CargoDispositionReceipt? cargo = _destructionCargo.CaptureReceipts().FirstOrDefault(item => item.Request.Key == receipt.CargoKey);
            if (cargo is null || cargo.CommittedAt != receipt.CommittedAt || cargo.Request.SourceInventoryId != before.InventoryId
                || cargo.Request.SourcePosition != before.Position || cargo.Request.Policy.Key != _destructionCargoPolicy.Key
                || cargo.Request.Cause != (request.DestroyBase ? CargoLossCause.EntityDestruction : CargoLossCause.StorageModuleLoss)
                || receipt.InventoryBefore.Custody is not InventoryCustody.PrincipalControlled custody
                || custody.ControllingPrincipalId != before.PrincipalId
                || custody.PhysicalOwner != new InventoryOwnerReference.SessionEntity(before.EntityId))
                return new(path, "Station destruction source, policy or exact cargo occurrence is inconsistent.");
            CheckpointResult<Inventory> stock = Inventory.RestoreCheckpoint(receipt.InventoryBefore, _destructionDefinitions, _setup.Materials);
            if (!stock.IsSuccess) return new(path, "Station destruction retained stock is malformed.");
            StationDefinition? definition = _setup.Definitions.Stations.FirstOrDefault(item => item.Key == before.DefinitionKey);
            if (definition is null || before.Modules.Select(module => module.Id).Distinct().Count() != before.Modules.Count)
                return new(path, "Station destruction has unresolved or duplicated module evidence.");
            bool operational = _setup.InitialStations.Any(seed => seed.Id == before.Id)
                || before.Projects.Any(project => project.Kind == StationProjectKind.BaseConstruction && project.Status == StationProjectStatus.Completed);
            ulong physical = (operational ? definition.BaseCapacity : definition.StagingCapacity).Units;
            ulong removed = 0;
            foreach (StationModuleSnapshot module in before.Modules)
            {
                StationModuleDefinition? type = _setup.Definitions.Modules.FirstOrDefault(item => item.Key == module.DefinitionKey);
                if (type is null) return new(path, "Station destruction module type is unresolved.");
                bool completed = _setup.InitialStations.Any(seed => seed.Id == before.Id && seed.Modules.Any(item => item.Id == module.Id))
                    || before.Projects.Any(project => project.Kind == StationProjectKind.ModuleConstruction && project.ModuleId == module.Id
                        && project.Status == StationProjectStatus.Completed);
                if (!completed) continue;
                physical = checked(physical + type.StorageCapacity.Units);
                if (request.DestroyBase || request.Modules.Contains(module.Id)) removed = checked(removed + type.StorageCapacity.Units);
            }
            if (physical != receipt.InventoryBefore.Capacity.Units || request.Modules.Any(id => !before.Modules.Any(module => module.Id == id)))
                return new(path, "Station destruction capacity or module selection disagrees with its before-state.");
            StationStorageLossSelection expected;
            if (request.DestroyBase) expected = new(StationStorageLossWindow.Empty, SelectAll(receipt.InventoryBefore), stock.Value!.UsedCapacity);
            else if (removed == 0) expected = new(receipt.WindowBefore, [], Quantity.Zero);
            else
            {
                if (_setup.StorageLossPolicy is null) return new(path, "Station storage loss policy is missing.");
                expected = StationStorageLossSelection.Evaluate(_setup.StorageLossPolicy, receipt.WindowBefore, before.Id,
                    request.Occurrence, new(removed), new(physical - removed), receipt.InventoryBefore, _destructionDefinitions, _destructionRandom);
            }
            if (!expected.Selected.SequenceEqual(cargo.Request.Selected) || !WindowsEqual(expected.Window, receipt.WindowAfter))
                return new(path, "Station loss selection or accounting does not match the deterministic causal witness.");
            if (request.DestroyBase && (!_stationRemovals.TryGetValue(before.EntityId, out StationRemovalReceipt? removal)
                || removal.Reason != StationRemovalReason.Destroyed || removal.RemovedAt != receipt.CommittedAt
                || removal.Cause != Cause(request.Trigger) || !StationSnapshotValueEquality.Equal(removal.Station, before)))
                return new(path, "Base destruction has no matching immutable station removal evidence.");
        }
        return null;
    }

    /// <summary>
    /// Joins each historical before-image only after live and retired origins and project admissions have validated.
    /// A cargo-consistent zero-storage object still needs its own allocated identity and structural history.
    /// </summary>
    private bool ValidateDestructionOrigins()
    {
        try { return ValidateDestructionOriginWitnesses(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { return false; }
    }

    /// <summary>Requires immutable station identity and a prefix of its own admitted projects, excluding earlier destruction.</summary>
    private bool ValidateDestructionOriginWitnesses()
    {
        if (_destructionReceipts.Count == 0) return true;
        var admissions = CaptureAllProjectAdmissions();
        if (admissions is null) return false;
        // Command instances may be submitted repeatedly. Admission sequence, rather than object identity,
        // joins each project to its exact envelope and also avoids rescanning all receipts per project.
        var requests = _receipts.Values.Select(value => value.Request).OrderBy(value => value.Sequence.Value)
            .Where(value => value.Command is CreateStationSiteCommand or ConstructStationModuleCommand or UpgradeStationModuleCommand
                or DismantleStationModuleCommand or DismantleStationBaseCommand
                or ControlStationProjectCommand { Action: StationProjectAction.RestartBaseConstruction }).ToArray();
        if (requests.Length != admissions.Count) return false;
        var admittedRequests = admissions.OrderBy(value => value.Key.Value).Zip(requests)
            .ToDictionary(pair => pair.First.Key, pair => pair.Second);
        var origins = CaptureStationOrigins().ToDictionary(value => value.Id);
        var retained = Capture().Concat(_stationRemovals.Values.Select(value => value.Station)).ToDictionary(value => value.Id);
        var destroyed = new Dictionary<StationId, HashSet<StationModuleId>>();
        var destroyedBases = new HashSet<StationId>();
        var previous = new Dictionary<StationId, StationDestructionReceipt>();
        foreach (var receipt in _destructionReceipts)
        {
            StationSnapshot before = receipt.Before;
            if (!origins.TryGetValue(before.Id, out var origin) || !retained.TryGetValue(before.Id, out var final)
                || destroyedBases.Contains(before.Id) || before.EntityId != origin.Entity || before.InventoryId != origin.Inventory
                || before.PrincipalId != origin.Principal || before.DefinitionKey != origin.DefinitionKey || before.Position != origin.Position
                || receipt.CommittedAt < origin.CreatedAt || before.Revision > final.Revision || !Enum.IsDefined(before.Phase)
                || previous.TryGetValue(before.Id, out var prior) && (receipt.CommittedAt < prior.CommittedAt || before.Revision <= prior.Before.Revision)) return false;
            var finalProjects = final.Projects.OrderBy(value => value.Id.Value).ToArray();
            var projects = before.Projects.OrderBy(value => value.Id.Value).ToArray();
            if (projects.Length > finalProjects.Length || projects.Select(value => value.Id).Distinct().Count() != projects.Length
                || !projects.Select(value => value.Id).SequenceEqual(finalProjects.Take(projects.Length).Select(value => value.Id))) return false;
            var included = projects.Select(value => value.Id).ToHashSet();
            foreach (var project in finalProjects)
            {
                if (!admissions.TryGetValue(project.Id, out var command)) return false;
                GameplayCommandEnvelope admitted = admittedRequests[project.Id];
                bool mustPrecede = WitnessAdmissionRevision(command) is { } revision ? revision < before.Revision : admitted.SubmittedAt <= receipt.CommittedAt;
                if (included.Contains(project.Id) != mustPrecede || included.Contains(project.Id) && admitted.SubmittedAt > receipt.CommittedAt) return false;
            }
            var removed = destroyed.GetValueOrDefault(before.Id) ?? [];
            StationDefinition definition = _setup.Definitions.Stations.Single(value => value.Key == origin.DefinitionKey);
            for (int index = 0; index < projects.Length; index++)
            {
                var project = projects[index];
                var finalProject = finalProjects[index];
                if (!ValidDestructionProject(project, finalProject, definition)) return false;
                // Selected work cannot accrue between its before-image and this destruction's cancellation.
                bool endsHere = receipt.Request.DestroyBase || project.ModuleId is { } module && receipt.Request.Modules.Contains(module);
                if (endsHere && (project.CompletedWork != finalProject.CompletedWork || project.ProviderFacilityId != finalProject.ProviderFacilityId)) return false;
            }
            if (!ValidateDestructionModules(before, projects, definition, removed, admissions)) return false;
            previous[before.Id] = receipt;
            destroyed[before.Id] = removed;
            removed.UnionWith(receipt.Request.DestroyBase ? before.Modules.Select(value => value.Id) : receipt.Request.Modules);
            if (receipt.Request.DestroyBase) destroyedBases.Add(before.Id);
        }
        return true;
    }

    /// <summary>Retains exact recipe and immutable project identity while permitting only monotonic work before its final state.</summary>
    private bool ValidDestructionProject(StationProjectSnapshot project, StationProjectSnapshot final, StationDefinition station)
    {
        if (!Allocated(project.Id.Value, _projectIds.CaptureCheckpoint()) || !Enum.IsDefined(project.Kind) || !Enum.IsDefined(project.Status)
            || project.Kind != final.Kind || project.ModuleId != final.ModuleId || project.ModuleDefinitionKey != final.ModuleDefinitionKey
            || project.UpgradeTarget != final.UpgradeTarget || project.RequiredWork != final.RequiredWork
            || project.CompletedWork.Units > final.CompletedWork.Units || project.ConsumedInputs is null || project.AssignedShips is null
            || project.ProviderFacilityId is not null && project.ProviderFacilityId != final.ProviderFacilityId
            || WitnessTerminal(project.Status) && (project.Status != final.Status || project.CompletedWork != final.CompletedWork
                || project.ProviderFacilityId != final.ProviderFacilityId)
            || project.Status == StationProjectStatus.Completed && project.CompletedWork != project.RequiredWork
            || project.CompletedWork == project.RequiredWork && project.Status != StationProjectStatus.Completed
                && !(project.Kind == StationProjectKind.Dismantling && project.Status is StationProjectStatus.Waiting or StationProjectStatus.Paused)
            || project.ModuleId is { } module && !Allocated(module.Value, _moduleIds.CaptureCheckpoint())
            || project.AssignedShips.Distinct().Count() != project.AssignedShips.Count
            || project.AssignedShips.Any(ship => !Allocated(ship.Value, _lifecycle.CaptureCheckpoint().ShipIds))
            || project.AssignedModules is null || project.AssignedModules.Distinct().Count() != project.AssignedModules.Count
            || project.AssignedModules.Any(module => !Allocated(module.Value, _moduleIds.CaptureCheckpoint()))
            || WitnessTerminal(project.Status) && (project.AssignedShips.Count != 0 || project.AssignedModules.Count != 0)) return false;
        ConstructionRecipe? recipe = project.Kind == StationProjectKind.Dismantling && project.ModuleId is null
            ? new([], station.DismantleWork) : ResolveProjectRecipe(station, project.Kind, project.ModuleDefinitionKey, project.UpgradeTarget);
        return recipe is not null && ValidRetiredConsumption(project, recipe);
    }

    /// <summary>
    /// Reconstructs attachment identity and key changes from authored origins and the before-image's own project prefix.
    /// Completed upgrade recipes establish the current key; cancelled consumed additions retain only their capacity claim.
    /// </summary>
    private bool ValidateDestructionModules(StationSnapshot before, StationProjectSnapshot[] projects, StationDefinition definition,
        HashSet<StationModuleId> destroyed, Dictionary<StationProjectId, GameplayCommand> admissions)
    {
        InitialStationSetup? initial = _setup.InitialStations.FirstOrDefault(value => value.Id == before.Id);
        var modules = new Dictionary<StationModuleId, StationModuleSnapshot>();
        var completed = new HashSet<StationModuleId>();
        foreach (var module in initial?.Modules ?? [])
        {
            var type = _setup.Definitions.Modules.Single(value => value.Key == module.DefinitionKey);
            modules.Add(module.Id, new(module.Id, module.DefinitionKey, StationModulePhase.Operational, module.Enabled,
                type.CapacityCost, null, null, module.FacilityId));
            completed.Add(module.Id);
        }
        foreach (var project in projects.Where(value => value.ModuleId is not null))
        {
            StationModuleId id = project.ModuleId!.Value;
            var type = _setup.Definitions.Modules.Single(value => value.Key == project.ModuleDefinitionKey);
            bool terminal = WitnessTerminal(project.Status);
            if (project.Kind == StationProjectKind.ModuleConstruction)
            {
                if (modules.ContainsKey(id) || !definition.AllowedModules.Contains(type.Key)) return false;
                bool complete = project.Status == StationProjectStatus.Completed;
                if (project.Status == StationProjectStatus.Cancelled && project.ConsumedInputs.Count == 0) continue;
                modules.Add(id, new(id, type.Key, complete ? StationModulePhase.Operational : terminal ? StationModulePhase.Unfinished : StationModulePhase.UnderConstruction,
                    true, type.CapacityCost, terminal ? null : project.Id, null, complete ? project.ProviderFacilityId : null));
                if (complete) completed.Add(id);
                continue;
            }
            if (!modules.TryGetValue(id, out var current) || current.ActiveProject is not null) return false;
            if (project.Kind == StationProjectKind.Upgrade)
            {
                if (!completed.Contains(id) || current.DefinitionKey != type.Key || current.UnfinishedUpgrade is not null
                    || type.UpgradePaths.All(path => path.TargetKey != project.UpgradeTarget)) return false;
                var target = _setup.Definitions.Modules.Single(value => value.Key == project.UpgradeTarget);
                current = project.Status == StationProjectStatus.Completed
                    ? current with { DefinitionKey = target.Key, OccupiedCapacity = target.CapacityCost, FacilityId = project.ProviderFacilityId }
                    : current with { ActiveProject = terminal ? null : project.Id,
                        OccupiedCapacity = !terminal || project.ConsumedInputs.Count != 0 ? Math.Max(type.CapacityCost, target.CapacityCost) : type.CapacityCost,
                        UnfinishedUpgrade = terminal && project.ConsumedInputs.Count != 0 ? target.Key : null };
            }
            else if (project.Kind == StationProjectKind.Dismantling && admissions[project.Id] is DismantleStationModuleCommand dismantle)
            {
                QualifiedContentKey? expected = dismantle.UnfinishedUpgradeAddition ? current.UnfinishedUpgrade : current.DefinitionKey;
                if (expected != type.Key || !dismantle.UnfinishedUpgradeAddition && current.UnfinishedUpgrade is not null) return false;
                if (project.Status == StationProjectStatus.Completed)
                {
                    if (!dismantle.UnfinishedUpgradeAddition) { modules.Remove(id); completed.Remove(id); continue; }
                    current = current with { UnfinishedUpgrade = null,
                        OccupiedCapacity = _setup.Definitions.Modules.Single(value => value.Key == current.DefinitionKey).CapacityCost };
                }
                else if (!terminal)
                    current = current with { ActiveProject = project.Id,
                        Phase = dismantle.UnfinishedUpgradeAddition ? current.Phase : StationModulePhase.Dismantling };
            }
            else return false;
            modules[id] = current;
        }
        foreach (var id in destroyed) modules.Remove(id);
        foreach (var receipt in _receipts.Values.Where(value => CommandStation(value.Request.Command) == before.Id
            && value.Request.Command is SetStationModuleEnabledCommand enabled && enabled.ExpectedStationRevision < before.Revision)
            .OrderBy(value => value.Request.Sequence.Value))
        {
            var enabled = (SetStationModuleEnabledCommand)receipt.Request.Command;
            if (modules.TryGetValue(enabled.Module, out var module)) modules[enabled.Module] = module with { Enabled = enabled.Enabled };
        }
        if (before.Modules.Count != modules.Count || before.Modules.Select(value => value.Id).Distinct().Count() != before.Modules.Count
            || before.Modules.Any(module => !Allocated(module.Id.Value, _moduleIds.CaptureCheckpoint()) || !modules.TryGetValue(module.Id, out var expected)
                || module != expected)) return false;
        foreach (var project in projects)
            if (project.AssignedModules!.Any(id => !modules.TryGetValue(id, out var module) || !completed.Contains(id)
                || _setup.Definitions.Modules.Single(value => value.Key == module.DefinitionKey).ConstructionCapability is null || project.ModuleId == id)) return false;
        bool completeBase = initial is not null || projects.Any(project => project.Kind == StationProjectKind.BaseConstruction && project.Status == StationProjectStatus.Completed);
        StationPhase phase = projects.Any(project => project.Kind == StationProjectKind.Dismantling && project.ModuleId is null && !WitnessTerminal(project.Status))
            ? StationPhase.Dismantling : completeBase ? StationPhase.Operational : StationPhase.Site;
        ulong sensors = phase == StationPhase.Operational ? modules.Values.Where(module => completed.Contains(module.Id) && module.Enabled
            && module.Phase == StationModulePhase.Operational).Select(module => _setup.Definitions.Modules.Single(value => value.Key == module.DefinitionKey).SensorRadiusUnits)
            .Append(definition.SensorRadiusUnits).Max() : 0;
        return before.Phase == phase && before.SensorRadiusUnits == sensors
            && modules.Values.Aggregate((UInt128)0, (total, module) => total + module.OccupiedCapacity) <= definition.ModuleCapacity;
    }

    private static bool WitnessTerminal(StationProjectStatus status) => status is StationProjectStatus.Completed or StationProjectStatus.Cancelled;

    /// <summary>Uses the accepted admission's pre-mutation revision to identify the exact historical project prefix.</summary>
    private static ulong? WitnessAdmissionRevision(GameplayCommand command) => command switch
    {
        ConstructStationModuleCommand value => value.ExpectedStationRevision,
        UpgradeStationModuleCommand value => value.ExpectedStationRevision,
        DismantleStationModuleCommand value => value.ExpectedStationRevision,
        DismantleStationBaseCommand value => value.ExpectedStationRevision,
        ControlStationProjectCommand value => value.ExpectedStationRevision,
        _ => null,
    };

    private static bool WindowsEqual(StationStorageLossWindow a, StationStorageLossWindow b) =>
        a.AccumulatedDestroyedCapacity == b.AccumulatedDestroyedCapacity && a.OverflowCredits.Count == b.OverflowCredits.Count
        && a.OverflowCredits.All(entry => b.OverflowCredits.TryGetValue(entry.Key, out var value) && entry.Value == value);
}
