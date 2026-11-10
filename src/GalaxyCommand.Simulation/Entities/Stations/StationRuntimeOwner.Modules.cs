using GalaxyCommand.Content;
using System.Numerics;

namespace GalaxyCommand.Simulation;

internal sealed partial class StationRuntimeOwner
{
    /// <summary>Resolves retained authored recipe meaning for live and historical structural projects.</summary>
    private ConstructionRecipe? ResolveProjectRecipe(StationDefinition definition, StationProjectKind kind,
        QualifiedContentKey? moduleDefinitionKey, QualifiedContentKey? upgradeTarget)
    {
        if (kind == StationProjectKind.BaseConstruction) return definition.BaseRecipe;
        StationModuleDefinition? module = _setup.Definitions.Modules.FirstOrDefault(item => item.Key == moduleDefinitionKey);
        if (module is null) return null;
        return kind switch
        {
            StationProjectKind.ModuleConstruction => module.ConstructionRecipe,
            StationProjectKind.Upgrade => module.UpgradePaths.FirstOrDefault(path => path.TargetKey == upgradeTarget)?.Recipe,
            StationProjectKind.Dismantling => new ConstructionRecipe([], module.DismantleWork),
            _ => null,
        };
    }

    /// <summary>Admits an inactive module and claims expansion room before allocating either scoped identity.</summary>
    private GameplayCommandHandlingResult ConstructModule(ConstructStationModuleCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
                out StationState station, out GameplayCommandHandlingResult rejection)) return rejection;
        StationModuleDefinition? definition = _setup.Definitions.Modules.FirstOrDefault(item => item.Key == command.DefinitionKey);
        if (definition is null || !station.Definition.AllowedModules.Contains(definition.Key)) return Rejected("station-module-not-allowed");
        if (OccupiedCapacity(station) + definition.CapacityCost > station.Definition.ModuleCapacity)
            return Rejected("station-insufficient-module-capacity");
        if (PotentialPhysicalCapacity(station) + definition.StorageCapacity.Units > ulong.MaxValue)
            return Rejected("station-capacity-overflow");
        if (!_moduleIds.TryPeek(out StationModuleId moduleId) || !_projectIds.TryPeek(out StationProjectId projectId))
            return Rejected("station-identifiers-exhausted");
        if ((definition.ProductionProvider is not null || definition.ShipConstructionProvider is not null)
            && (_economicProviders?.PrepareStationProvider(station.Id, station.Entity, station.Inventory.Id, station.Principal,
                station.Position, moduleId, null, definition, false, _agenda.CurrentTime) is not { } provider || !provider.CanCommit()))
            return Rejected("station-economic-provider-unavailable");
        var project = new ProjectState(projectId, definition.ConstructionRecipe)
        { Kind = StationProjectKind.ModuleConstruction, ModuleId = moduleId, ModuleDefinitionKey = definition.Key };
        var module = new ModuleState(moduleId, definition, projectId) { ActiveProject = projectId };
        _moduleIds.Allocate();
        _projectIds.Allocate();
        station.Modules.Add(moduleId, module);
        station.Projects.Add(projectId, project);
        station.Revision = checked(station.Revision + 1);
        return StructuralChanged(station, project, module);
    }

    /// <summary>Claims only extra expansion room and protects any inventory reduction while old capabilities remain active.</summary>
    private GameplayCommandHandlingResult UpgradeModule(UpgradeStationModuleCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
                out StationState station, out GameplayCommandHandlingResult rejection)) return rejection;
        if (!station.Modules.TryGetValue(command.Module, out ModuleState? module)
            || module.Phase != StationModulePhase.Operational || module.ActiveProject is not null
            || module.UnfinishedUpgrade is not null) return Rejected("station-module-unavailable");
        StationUpgradePath? path = module.Definition.UpgradePaths.FirstOrDefault(item => item.TargetKey == command.UpgradeTarget);
        StationModuleDefinition? target = _setup.Definitions.Modules.FirstOrDefault(item => item.Key == command.UpgradeTarget);
        if (path is null || target is null || !station.Definition.AllowedModules.Contains(target.Key)) return Rejected("station-upgrade-not-allowed");
        if (!CanUpgradeModuleProvider(module, target)) return Rejected("station-incompatible-economic-upgrade");
        ulong extra = target.CapacityCost > module.Definition.CapacityCost ? target.CapacityCost - module.Definition.CapacityCost : 0;
        if (OccupiedCapacity(station) + extra > station.Definition.ModuleCapacity) return Rejected("station-insufficient-module-capacity");
        ulong addedStorage = target.StorageCapacity.Units > module.Definition.StorageCapacity.Units
            ? target.StorageCapacity.Units - module.Definition.StorageCapacity.Units : 0;
        if (PotentialPhysicalCapacity(station) + addedStorage > ulong.MaxValue) return Rejected("station-capacity-overflow");
        if (!_projectIds.TryPeek(out StationProjectId id)) return Rejected("station-identifiers-exhausted");
        var project = new ProjectState(id, path.Recipe)
        { Kind = StationProjectKind.Upgrade, ModuleId = module.Id, ModuleDefinitionKey = module.Definition.Key, ProviderFacilityId = module.FacilityId, UpgradeTarget = target.Key };
        (Quantity physical, Quantity enabled, Quantity? protection) = CalculateStorageLimits(station);
        ulong reduction = module.Definition.StorageCapacity.Units > target.StorageCapacity.Units
            ? module.Definition.StorageCapacity.Units - target.StorageCapacity.Units : 0;
        Quantity? protectedLimit = reduction == 0 ? protection : new Quantity((protection ?? physical).Units - reduction);
        PreparedInventoryCapacityChange? capacity = station.Inventory.PrepareCapacityChange(physical, enabled, protectedLimit);
        if (capacity is null) return Rejected("station-insufficient-cargo-capacity");

        if (!station.Inventory.ApplyCapacityChange(capacity)) throw new InvalidOperationException("Prepared upgrade protection changed during commit.");
        module.OccupiedCapacity = checked(module.OccupiedCapacity + extra);
        module.ActiveProject = id;
        station.Projects.Add(id, project);
        _projectIds.Allocate();
        station.Revision = checked(station.Revision + 1);
        return StructuralChanged(station, project, module);
    }

    /// <summary>Prepares all recovery room and surviving capacity before withdrawing module services.</summary>
    private GameplayCommandHandlingResult DismantleModule(DismantleStationModuleCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
                out StationState station, out GameplayCommandHandlingResult rejection)) return rejection;
        if (!station.Modules.TryGetValue(command.Module, out ModuleState? module) || module.ActiveProject is not null
            || module.Phase is not (StationModulePhase.Operational or StationModulePhase.Unfinished)) return Rejected("station-module-unavailable");
        StationModuleDefinition definition = module.Definition;
        IReadOnlyDictionary<MaterialId, Quantity> recovery = definition.DismantleRecovery;
        if (command.UnfinishedUpgradeAddition)
        {
            if (module.UnfinishedUpgrade is null || module.UnfinishedUpgradeProject is not { } prior) return Rejected("station-upgrade-addition-missing");
            definition = _setup.Definitions.Modules.Single(item => item.Key == module.UnfinishedUpgrade);
            recovery = StationDismantlingRecovery.Evaluate(definition.DismantleRecovery, station.Projects[prior].Consumed);
        }
        else
        {
            if (module.UnfinishedUpgrade is not null) return Rejected("station-upgrade-addition-remains");
            StationProjectId? origin = module.OriginProject;
            if (!module.IsCompleted) recovery = StationDismantlingRecovery.Evaluate(definition.DismantleRecovery,
                station.Projects[origin ?? throw new InvalidOperationException("Unfinished module lacks its construction project.")].Consumed);
        }
        if (!_projectIds.TryPeek(out StationProjectId id)) return Rejected("station-identifiers-exhausted");
        Inventory? destination = _lifecycle.Inventories.Get(command.RecoveryInventory);
        if (destination?.Custody is not InventoryCustody.PrincipalControlled) return Rejected("station-recovery-destination-unavailable");
        var project = new ProjectState(id, new ConstructionRecipe([], definition.DismantleWork))
        {
            Kind = StationProjectKind.Dismantling, ModuleId = module.Id, ModuleDefinitionKey = definition.Key, ProviderFacilityId = module.FacilityId,
            DismantleAddition = command.UnfinishedUpgradeAddition, RecoveryInventory = destination.Id,
        };
        foreach ((MaterialId material, Quantity amount) in recovery) project.RecoveryOutputs.Add(material, amount);
        Quantity output = RecoveryCapacity(project);
        ReservationId? reservation = null;
        if (output != Quantity.Zero)
        {
            if (_inventoryCommit is null || !_inventoryCommit.TryPeekStationRecoveryReservationIdentity(out ReservationId next)
                || !_inventoryCommit.CanReserveStationRecoveryCapacity(destination, output, new(station.Id, id), next))
                return Rejected("station-insufficient-recovery-capacity");
            reservation = next;
        }
        (Quantity physical, Quantity enabled, Quantity? protection) = CalculateStorageLimits(station);
        ulong removed = !command.UnfinishedUpgradeAddition && module.IsCompleted ? module.Definition.StorageCapacity.Units : 0;
        Quantity retained = new((protection ?? physical).Units - removed);
        Quantity enabledAfter = new(enabled.Units - (removed != 0 && module.Enabled ? removed : 0));
        Quantity? protectedAfter = removed == 0 ? protection : retained;
        if (ReferenceEquals(destination, station.Inventory) && output != Quantity.Zero
            && (UInt128)destination.UsedCapacity.Units + destination.ReservedCapacity.Units + output.Units
                > Math.Min(enabledAfter.Units, retained.Units)) return Rejected("station-insufficient-recovery-capacity");
        PreparedInventoryCapacityChange? capacity = station.Inventory.PrepareCapacityChange(physical, enabledAfter, protectedAfter);
        if (capacity is null) return Rejected("station-insufficient-cargo-capacity");

        PreparedStationModuleProvider? provider = null;
        if (!command.UnfinishedUpgradeAddition && !TryPrepareModuleProvider(station, module, module.Definition, false, false, out provider))
            return Rejected("station-economic-provider-unavailable");
        if (!station.Inventory.ApplyCapacityChange(capacity)) throw new InvalidOperationException("Prepared dismantling protection changed during commit.");
        ApplyModuleProvider(module, provider);
        if (reservation is { } claim) _inventoryCommit!.ApplyStationRecoveryCapacityReservation(destination, output, new(station.Id, id), claim);
        project.RecoveryReservation = reservation;
        if (!command.UnfinishedUpgradeAddition) module.Phase = StationModulePhase.Dismantling;
        module.ActiveProject = id;
        station.Projects.Add(id, project);
        _projectIds.Allocate();
        station.Revision = checked(station.Revision + 1);
        return StructuralChanged(station, project, module);
    }

    /// <summary>Disabling removes operational contributions while retaining physical storage and all held cargo.</summary>
    private GameplayCommandHandlingResult EnableModule(SetStationModuleEnabledCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
                out StationState station, out GameplayCommandHandlingResult rejection)) return rejection;
        if (!station.Modules.TryGetValue(command.Module, out ModuleState? module)
            || module.Phase != StationModulePhase.Operational) return Rejected("station-module-unavailable");
        (Quantity physical, Quantity enabled, Quantity? protection) = CalculateStorageLimits(station);
        ulong nextEnabled = enabled.Units;
        if (module.Enabled != command.Enabled)
            nextEnabled = command.Enabled ? checked(nextEnabled + module.Definition.StorageCapacity.Units)
                : nextEnabled - module.Definition.StorageCapacity.Units;
        PreparedInventoryCapacityChange capacity = station.Inventory.PrepareCapacityChange(physical, new(nextEnabled), protection)
            ?? throw new InvalidOperationException("Module enabled condition cannot invalidate retained physical capacity.");
        if (!TryPrepareModuleProvider(station, module, module.Definition, command.Enabled, false, out var provider, includeWake: true))
            return Rejected("station-economic-provider-unavailable");
        if (!station.Inventory.ApplyCapacityChange(capacity)) throw new InvalidOperationException("Prepared module enabled state changed during commit.");
        ApplyModuleProvider(module, provider);
        module.Enabled = command.Enabled;
        station.Revision = checked(station.Revision + 1);
        return new(CommandResult.Accepted(), [ModuleFact(station, module)]);
    }

    /// <summary>Uses one explicit same-station module assignment and prevents a builder from upgrading or dismantling itself.</summary>
    private GameplayCommandHandlingResult AssignModuleBuilder(AssignStationModuleBuilderCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
                out StationState station, out GameplayCommandHandlingResult rejection)) return rejection;
        if (!station.Projects.TryGetValue(command.Project, out ProjectState? project)
            || project.Status is StationProjectStatus.Completed or StationProjectStatus.Cancelled) return Rejected("station-project-retired");
        if (!station.Modules.TryGetValue(command.BuilderModule, out ModuleState? builder)
            || builder.Phase != StationModulePhase.Operational || !builder.Enabled || builder.Definition.ConstructionCapability is null)
            return Rejected("station-builder-unavailable");
        if (project.ModuleId == builder.Id) return Rejected("station-builder-cannot-build-self");
        foreach (ProjectState prior in station.Projects.Values) prior.ModuleBuilders.Remove(builder.Id);
        project.ModuleBuilders.Add(builder.Id);
        station.Revision = checked(station.Revision + 1);
        return Changed(station, project);
    }

    /// <summary>Releases only the named live module-builder assignment.</summary>
    private GameplayCommandHandlingResult ReleaseModuleBuilder(ReleaseStationModuleBuilderCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
                out StationState station, out GameplayCommandHandlingResult rejection)) return rejection;
        if (!station.Projects.TryGetValue(command.Project, out ProjectState? project)
            || !project.ModuleBuilders.Remove(command.BuilderModule)) return Rejected("station-builder-not-assigned");
        station.Revision = checked(station.Revision + 1);
        return Changed(station, project);
    }

    /// <summary>Prepares replacement recovery room before releasing any surviving old claim; completed work needs no extra time.</summary>
    private GameplayCommandHandlingResult ReplaceRecoveryDestination(SetStationRecoveryDestinationCommand command, CommandSequence sequence)
    {
        if (!_stations.TryGetValue(command.Station, out StationState? station)
            || station.Revision != command.ExpectedStationRevision) return Rejected("station-stale-target");
        if (station.Principal != command.RequestingPrincipal) return Rejected("station-wrong-authority");
        if (!station.Projects.TryGetValue(command.Project, out ProjectState? project) || project.Kind != StationProjectKind.Dismantling
            || project.Status is StationProjectStatus.Cancelled or StationProjectStatus.Completed) return Rejected("station-project-retired");
        Inventory? destination = _lifecycle.Inventories.Get(command.RecoveryInventory);
        if (destination?.Custody is not InventoryCustody.PrincipalControlled) return Rejected("station-recovery-destination-unavailable");
        if (project.ModuleId is null && destination.Id == station.Inventory.Id) return Rejected("station-external-recovery-required");
        if (project.RecoveryInventory != destination.Id || !RecoveryAvailable(station, project))
        {
            Quantity output = RecoveryCapacity(project);
            ReservationId? next = null;
            if (output != Quantity.Zero)
            {
                if (_inventoryCommit is null || !_inventoryCommit.TryPeekStationRecoveryReservationIdentity(out ReservationId claim)
                    || !_inventoryCommit.CanReserveStationRecoveryCapacity(destination, output, new(station.Id, project.Id), claim))
                    return Rejected("station-insufficient-recovery-capacity");
                next = claim;
            }
            if (next is { } reservation) _inventoryCommit!.ApplyStationRecoveryCapacityReservation(destination, output,
                new(station.Id, project.Id), reservation);
            ReleaseRecovery(station, project);
            project.RecoveryInventory = destination.Id;
            project.RecoveryReservation = next;
        }
        station.Revision = checked(station.Revision + 1);
        if (project.Completed == project.Recipe.RequiredWork)
            return new(CommandResult.Accepted(), CompleteStructuralProject(station, project, new CommandFactCause(sequence)));
        return Changed(station, project);
    }

    /// <summary>Admits structural work against one owning aggregate revision without changing authority on rejection.</summary>
    private bool TryStructuralStation(PrincipalId principal, StationId id, ulong revision,
        out StationState station, out GameplayCommandHandlingResult rejection)
    {
        station = _stations.GetValueOrDefault(id)!;
        rejection = Rejected("station-stale-target");
        if (station is null || station.Revision != revision) return false;
        if (station.Principal != principal) { rejection = Rejected("station-wrong-authority"); return false; }
        if (station.Phase != StationPhase.Operational) { rejection = Rejected("station-base-not-operational"); return false; }
        return true;
    }

    private static UInt128 OccupiedCapacity(StationState station) => station.Modules.Values.Aggregate((UInt128)0,
        (sum, module) => sum + module.OccupiedCapacity);

    /// <summary>
    /// Reserves the checked storage representation for pending increases too. A later
    /// dismantle or shrink cannot fund an earlier expansion whose completion order may differ.
    /// </summary>
    private UInt128 PotentialPhysicalCapacity(StationState station)
    {
        UInt128 total = station.Definition.BaseCapacity.Units;
        foreach (ModuleState module in station.Modules.Values)
        {
            ulong capacity = module.IsCompleted ? module.Definition.StorageCapacity.Units : 0;
            if (module.ActiveProject is { } id && station.Projects.TryGetValue(id, out ProjectState? project))
            {
                if (project.Kind == StationProjectKind.ModuleConstruction) capacity = module.Definition.StorageCapacity.Units;
                else if (project.Kind == StationProjectKind.Upgrade)
                    capacity = Math.Max(capacity, _setup.Definitions.Modules.Single(item => item.Key == project.UpgradeTarget).StorageCapacity.Units);
            }
            total += capacity;
        }
        return total;
    }

    /// <summary>Reduces completed enabled sensor sources by maximum radius, never by addition.</summary>
    private static ulong SensorRadius(StationState station) => station.Phase != StationPhase.Operational ? 0
        : station.Modules.Values.Where(module => module.IsCompleted && module.Enabled && module.Phase == StationModulePhase.Operational)
            .Select(module => module.Definition.SensorRadiusUnits).Append(station.Definition.SensorRadiusUnits).Max();

    /// <summary>Combines stable explicit module contributions in identity order without self-supplied structural work.</summary>
    private static BigInteger ModuleBuilderRate(StationState station, ProjectState project)
    {
        BigInteger rate = 0;
        foreach (StationModuleId id in project.ModuleBuilders.OrderBy(id => id.Value))
            if (id != project.ModuleId && station.Modules.TryGetValue(id, out ModuleState? module)
                && module.IsCompleted && module.Enabled && module.Phase == StationModulePhase.Operational
                && module.Definition.ConstructionCapability is { } capability) rate += capability.Throughput.UnitsPerSecond;
        return rate;
    }

    /// <summary>Projects retained storage and all simultaneous protected reductions, optionally after one completion.</summary>
    private (Quantity Physical, Quantity Enabled, Quantity? Protected) CalculateStorageLimits(StationState station, ProjectState? completing = null)
    {
        ulong physical = station.Phase == StationPhase.Site ? station.Definition.StagingCapacity.Units : station.Definition.BaseCapacity.Units;
        if (station.Phase == StationPhase.Dismantling) return (new(physical), Quantity.Zero, Quantity.Zero);
        ulong enabled = physical;
        foreach (ModuleState module in station.Modules.Values)
        {
            if (completing?.ModuleId == module.Id && completing.Kind == StationProjectKind.Dismantling && !completing.DismantleAddition) continue;
            bool complete = module.IsCompleted || completing?.ModuleId == module.Id && completing.Kind == StationProjectKind.ModuleConstruction;
            if (!complete) continue;
            StationModuleDefinition definition = completing?.ModuleId == module.Id && completing.Kind == StationProjectKind.Upgrade
                ? _setup.Definitions.Modules.Single(item => item.Key == completing.UpgradeTarget) : module.Definition;
            physical = checked(physical + definition.StorageCapacity.Units);
            if (module.Enabled && (module.Phase == StationModulePhase.Operational
                || completing?.ModuleId == module.Id && completing.Kind == StationProjectKind.ModuleConstruction))
                enabled = checked(enabled + definition.StorageCapacity.Units);
        }
        ulong reduction = 0;
        foreach (ProjectState project in station.Projects.Values)
        {
            if (ReferenceEquals(project, completing) || project.Status is StationProjectStatus.Completed or StationProjectStatus.Cancelled
                || project.ModuleId is not { } id || !station.Modules.TryGetValue(id, out ModuleState? module) || !module.IsCompleted) continue;
            if (project.Kind == StationProjectKind.Dismantling && !project.DismantleAddition)
                reduction = checked(reduction + module.Definition.StorageCapacity.Units);
            else if (project.Kind == StationProjectKind.Upgrade)
            {
                ulong target = _setup.Definitions.Modules.Single(item => item.Key == project.UpgradeTarget).StorageCapacity.Units;
                if (target < module.Definition.StorageCapacity.Units) reduction = checked(reduction + module.Definition.StorageCapacity.Units - target);
            }
        }
        return (new(physical), new(enabled), reduction == 0 ? null : new Quantity(physical - reduction));
    }

    /// <summary>Returns exact physical output room using resolved material costs and checked whole quantities.</summary>
    private Quantity RecoveryCapacity(ProjectState project)
    {
        ulong total = 0;
        foreach ((MaterialId material, Quantity amount) in project.RecoveryOutputs)
            total = checked(total + checked(_setup.Materials.GetRequired(material).CapacityCost.Units * amount.Units));
        return new(total);
    }

    /// <summary>Requires the exact live recovery claim and currently enabled intake before output publication.</summary>
    private bool RecoveryAvailable(StationState station, ProjectState project)
    {
        if (project.RecoveryInventory is not { } id || _lifecycle.Inventories.Get(id) is not { Custody: InventoryCustody.PrincipalControlled } destination)
            return false;
        Quantity output = RecoveryCapacity(project);
        if (output == Quantity.Zero) return project.RecoveryReservation is null;
        return project.RecoveryReservation is { } claim && destination.GetPhysicalReservation(claim) is { } reservation
            && reservation.Owner == new ReservationOwner.StationProject(station.Id, project.Id)
            && reservation.Subject is PhysicalReservationSubject.IncomingCapacity incoming && incoming.Quantity == output
            && destination.CanAcceptReservedIntake(output, incoming.Quantity);
    }

    /// <summary>Releases only this project's surviving exact recovery reservation; vanished destinations retain no live claim.</summary>
    private void ReleaseRecovery(StationState station, ProjectState project)
    {
        if (project.RecoveryInventory is { } id && project.RecoveryReservation is { } claim
            && _lifecycle.Inventories.Get(id) is { } destination && destination.GetPhysicalReservation(claim) is { } reservation
            && reservation.Owner == new ReservationOwner.StationProject(station.Id, project.Id))
            destination.ReleasePhysicalReservation(claim, reservation.Owner);
        project.RecoveryReservation = null;
    }

    /// <summary>Publishes a whole structural capability/recovery transition after every affected inventory still admits it.</summary>
    private List<GameFactProposal> CompleteStructuralProject(StationState station, ProjectState project, GameFactCause? cause = null)
    {
        if (project.ModuleId is null && project.Kind == StationProjectKind.Dismantling)
            return CompleteBaseDismantling(station, project, cause
                ?? throw new InvalidOperationException("Base removal requires its committed causal identity."));
        if (project.ModuleId is not { } id || !station.Modules.TryGetValue(id, out ModuleState? module))
            throw new InvalidOperationException("Structural completion lost its module identity.");
        if (project.Kind == StationProjectKind.Dismantling && !RecoveryAvailable(station, project))
        {
            project.Status = StationProjectStatus.Waiting;
            project.Rate = 0;
            project.WorkNumerator = 0;
            return [];
        }
        (Quantity physical, Quantity enabled, Quantity? protection) = CalculateStorageLimits(station, project);
        PreparedInventoryCapacityChange capacity = station.Inventory.PrepareCapacityChange(physical, enabled, protection)
            ?? throw new InvalidOperationException("Structural completion lost its protected capacity invariant.");
        StationModuleDefinition providerDefinition = project.Kind == StationProjectKind.Upgrade
            ? _setup.Definitions.Modules.Single(item => item.Key == project.UpgradeTarget) : module.Definition;
        bool removesProvider = project.Kind == StationProjectKind.Dismantling && !project.DismantleAddition;
        PreparedStationModuleProvider? provider = null;
        if (!project.DismantleAddition && !TryPrepareModuleProvider(station, module, providerDefinition,
            !removesProvider && module.Enabled, removesProvider, out provider))
        {
            project.Status = StationProjectStatus.Waiting;
            project.Rate = 0;
            project.WorkNumerator = 0;
            return [];
        }
        if (!station.Inventory.ApplyCapacityChange(capacity)) throw new InvalidOperationException("Prepared structural capacity changed during commit.");
        ApplyModuleProvider(module, provider);
        project.ProviderFacilityId = module.FacilityId;
        var facts = new List<GameFactProposal>();
        if (project.Kind == StationProjectKind.Dismantling)
        {
            Inventory destination = _lifecycle.Inventories.Get(project.RecoveryInventory!.Value)!;
            ReleaseRecovery(station, project);
            foreach ((MaterialId material, Quantity amount) in project.RecoveryOutputs)
                if (!destination.StoreFungible(_setup.Materials.GetRequired(material), amount).IsAccepted)
                    throw new InvalidOperationException("Prevalidated dismantling recovery could not be published.");
            if (project.DismantleAddition)
            {
                module.UnfinishedUpgrade = null;
                module.UnfinishedUpgradeProject = null;
                module.OccupiedCapacity = module.Definition.CapacityCost;
                module.ActiveProject = null;
                facts.Add(ModuleFact(station, module));
            }
            else
            {
                station.Modules.Remove(module.Id);
                foreach (ProjectState target in station.Projects.Values) target.ModuleBuilders.Remove(module.Id);
                facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, module.Id.Value, 2),
                    new StationModuleRemovedFact(station.Id, module.Id)));
            }
        }
        else
        {
            if (project.Kind == StationProjectKind.Upgrade)
                module.Definition = _setup.Definitions.Modules.Single(item => item.Key == project.UpgradeTarget);
            module.IsCompleted = true;
            module.Phase = StationModulePhase.Operational;
            module.ActiveProject = null;
            module.OccupiedCapacity = module.Definition.CapacityCost;
            facts.Add(ModuleFact(station, module));
        }
        project.Status = StationProjectStatus.Completed;
        project.Ships.Clear();
        project.Sources.Clear();
        project.ModuleBuilders.Clear();
        project.Rate = 0;
        project.WorkNumerator = 0;
        facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, project.Id.Value, 0),
            new StationProjectCompletedFact(station.Id, project.Id)));
        return facts;
    }

    /// <summary>Retains consumed structures/additions on cancellation, releasing unconsumed claims and recoverable-output reservations only.</summary>
    private GameFactProposal[] CancelStructuralProject(StationState station, ProjectState project)
    {
        if (project.ModuleId is null && project.Kind == StationProjectKind.Dismantling)
        {
            ReleaseRecovery(station, project);
            station.Phase = station.Projects.Values.Any(item => item.Kind == StationProjectKind.BaseConstruction
                && item.Status == StationProjectStatus.Completed) ? StationPhase.Operational : StationPhase.Site;
            Quantity retained = station.Phase == StationPhase.Site ? station.Definition.StagingCapacity : station.Definition.BaseCapacity;
            PreparedInventoryCapacityChange reset = station.Inventory.PrepareCapacityChange(retained, retained)
                ?? throw new InvalidOperationException("Cancelled base dismantling lost empty retained storage.");
            if (!station.Inventory.ApplyCapacityChange(reset)) throw new InvalidOperationException("Base cancellation capacity changed during commit.");
            return [];
        }
        if (project.ModuleId is not { } id || !station.Modules.TryGetValue(id, out ModuleState? module)) return [];
        PreparedStationModuleProvider? provider = null;
        if (project.Kind == StationProjectKind.Dismantling && !project.DismantleAddition
            && !TryPrepareModuleProvider(station, module, module.Definition, module.IsCompleted && module.Enabled, false, out provider, includeWake: true))
            throw new InvalidOperationException("Cancelled dismantling lost its prepared economic provider.");
        ApplyModuleProvider(module, provider);
        module.ActiveProject = null;
        if (project.Kind == StationProjectKind.ModuleConstruction)
        {
            if (project.Consumed.Count == 0) station.Modules.Remove(id);
            else module.Phase = StationModulePhase.Unfinished;
        }
        else if (project.Kind == StationProjectKind.Upgrade)
        {
            if (project.Consumed.Count == 0) module.OccupiedCapacity = module.Definition.CapacityCost;
            else { module.UnfinishedUpgrade = project.UpgradeTarget; module.UnfinishedUpgradeProject = project.Id; }
        }
        else if (project.Kind == StationProjectKind.Dismantling)
        {
            ReleaseRecovery(station, project);
            if (!project.DismantleAddition) module.Phase = module.IsCompleted ? StationModulePhase.Operational : StationModulePhase.Unfinished;
        }
        (Quantity physical, Quantity enabled, Quantity? protection) = CalculateStorageLimits(station);
        PreparedInventoryCapacityChange capacity = station.Inventory.PrepareCapacityChange(physical, enabled, protection)
            ?? throw new InvalidOperationException("Cancelling structural work cannot invalidate retained capacity.");
        if (!station.Inventory.ApplyCapacityChange(capacity)) throw new InvalidOperationException("Cancelled project storage changed during commit.");
        return station.Modules.ContainsKey(id) ? [ModuleFact(station, module)]
            : [new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, id.Value, 2),
                new StationModuleRemovedFact(station.Id, id))];
    }

    private static GameFactProposal ModuleFact(StationState station, ModuleState module) => new(
        new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, module.Id.Value, 1),
        new StationModuleChangedFact(station.Id, module.Id, module.Definition.Key, module.Phase, module.Enabled));

    private static GameplayCommandHandlingResult StructuralChanged(StationState station, ProjectState project, ModuleState module) =>
        new(CommandResult.Accepted(), [new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, project.Id.Value, 0),
            new StationProjectChangedFact(station.Id, project.Id, project.Status)), ModuleFact(station, module)]);
}
