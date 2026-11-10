using System.Collections.ObjectModel;
using System.Numerics;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record StationAssignmentCheckpoint(ShipId Ship, CommandSource Source);
internal sealed record StationProjectCheckpoint(StationProjectId Id, StationProjectStatus Status, Work Completed,
    BigInteger WorkNumerator, BigInteger Rate, EventGeneration Generation, EventKey? Pending,
    IReadOnlyDictionary<MaterialId, Quantity> Consumed, IReadOnlyList<StationAssignmentCheckpoint> Assignments)
{
    internal StationProjectKind Kind { get; init; }
    internal StationModuleId? ModuleId { get; init; }
    internal QualifiedContentKey? ModuleDefinitionKey { get; init; }
    internal FacilityId? ProviderFacilityId { get; init; }
    internal QualifiedContentKey? UpgradeTarget { get; init; }
    internal IReadOnlyList<StationModuleId> ModuleBuilders { get; init; } = [];
    internal InventoryId? RecoveryInventory { get; init; }
    internal ReservationId? RecoveryReservation { get; init; }
    internal IReadOnlyDictionary<MaterialId, Quantity> RecoveryOutputs { get; init; } = new ReadOnlyDictionary<MaterialId, Quantity>(new Dictionary<MaterialId, Quantity>());
    internal bool DismantleAddition { get; init; }
}
internal sealed record StationModuleCheckpoint(StationModuleId Id, QualifiedContentKey DefinitionKey,
    StationModulePhase Phase, bool Enabled, bool IsCompleted, ulong OccupiedCapacity, StationProjectId? OriginProject,
    StationProjectId? ActiveProject, QualifiedContentKey? UnfinishedUpgrade, StationProjectId? UnfinishedUpgradeProject, FacilityId? FacilityId = null);
internal sealed record StationStateCheckpoint(StationId Id, EntityId Entity, InventoryId Inventory, PrincipalId Principal,
    QualifiedContentKey DefinitionKey, SystemPosition Position, StationPhase Phase, ulong Revision,
    IReadOnlyList<StationProjectCheckpoint> Projects)
{
    internal IReadOnlyList<StationModuleCheckpoint> Modules { get; init; } = [];
    internal SimulationTime CreatedAt { get; init; }
    internal StationStorageLossWindow LossWindow { get; init; } = StationStorageLossWindow.Empty;
    internal ulong LossOccurrence { get; init; }
}
internal sealed record StationCommandReceiptCheckpoint(GameplayCommandEnvelope Request, CommandResult Result);
internal sealed record StationRuntimeCheckpoint(StationRuntimeSetup Setup, IdSequenceCheckpoint StationIds,
    IdSequenceCheckpoint ProjectIds, IdSequenceCheckpoint ModuleIds, SimulationTime AccruedThrough, IReadOnlyList<StationStateCheckpoint> Stations,
    IReadOnlyList<StationCommandReceiptCheckpoint> Receipts)
{
    internal IReadOnlyList<StationRemovalReceipt> Removals { get; init; } = [];
    internal IReadOnlyList<StationShipOrderReceipt> ShipOrders { get; init; } = [];
    internal IReadOnlyList<StationDestructionReceipt> Destructions { get; init; } = [];
}

internal sealed partial class StationRuntimeOwner
{
    /// <summary>Copies every current station, module and project authority field while retaining immutable resolved content once.</summary>
    internal StationRuntimeCheckpoint CaptureCheckpoint() => new(_setup, _stationIds.CaptureCheckpoint(),
        _projectIds.CaptureCheckpoint(), _moduleIds.CaptureCheckpoint(), _accruedThrough,
        Array.AsReadOnly(_stations.Values.Select(station => new StationStateCheckpoint(station.Id, station.Entity,
            station.Inventory.Id, station.Principal, station.Definition.Key, station.Position, station.Phase, station.Revision,
            Array.AsReadOnly(station.Projects.Values.Select(project => new StationProjectCheckpoint(project.Id,
                project.Status, project.Completed, project.WorkNumerator, project.Rate, project.Generation, project.Pending,
                new ReadOnlyDictionary<MaterialId, Quantity>(new SortedDictionary<MaterialId, Quantity>(project.Consumed,
                    EntityIdComparer<MaterialId>.Instance)),
                Array.AsReadOnly(project.Ships.OrderBy(ship => ship.Value)
                    .Select(ship => new StationAssignmentCheckpoint(ship, project.Sources[ship])).ToArray()))
                {
                    Kind = project.Kind, ModuleId = project.ModuleId, ModuleDefinitionKey = project.ModuleDefinitionKey, ProviderFacilityId = project.ProviderFacilityId,
                    UpgradeTarget = project.UpgradeTarget,
                    ModuleBuilders = Array.AsReadOnly(project.ModuleBuilders.OrderBy(id => id.Value).ToArray()),
                    RecoveryInventory = project.RecoveryInventory, RecoveryReservation = project.RecoveryReservation,
                    RecoveryOutputs = new ReadOnlyDictionary<MaterialId, Quantity>(new SortedDictionary<MaterialId, Quantity>(
                        project.RecoveryOutputs, EntityIdComparer<MaterialId>.Instance)), DismantleAddition = project.DismantleAddition,
                }).ToArray()))
            {
                CreatedAt = station.CreatedAt,
                LossWindow = station.LossWindow, LossOccurrence = station.LossOccurrence,
                Modules = Array.AsReadOnly(station.Modules.Values.Select(module => new StationModuleCheckpoint(module.Id,
                    module.Definition.Key, module.Phase, module.Enabled, module.IsCompleted, module.OccupiedCapacity,
                    module.OriginProject, module.ActiveProject, module.UnfinishedUpgrade, module.UnfinishedUpgradeProject, module.FacilityId)).ToArray()),
            }).ToArray()),
        Array.AsReadOnly(_receipts.Values.OrderBy(receipt => receipt.Request.Sequence.Value)
            .Select(receipt => new StationCommandReceiptCheckpoint(receipt.Request, receipt.Result)).ToArray()))
        { Removals = CaptureRemovals(), ShipOrders = Array.AsReadOnly(_shipOrders.Values.ToArray()), Destructions = Array.AsReadOnly(_destructionReceipts.ToArray()) };

    /// <summary>
    /// Restores exact station, module and project authority without replaying work, allocating identities, or scheduling events.
    /// All local and cross-owner invariants validate before station identity mappings are published to lifecycle.
    /// </summary>
    internal static CheckpointResult<StationRuntimeOwner> Restore(StationRuntimeCheckpoint checkpoint,
        EntityLifecycleOwner lifecycle, EventAgenda<GameEvent> agenda,
        Func<ShipId, CommandSource, StationBuilderState?> builder, SimulationTime time,
        IEnumerable<SystemId> systemIds, IEnumerable<PrincipalId> principals, InventoryCommitOwner? inventoryCommit = null,
        Func<EntityId, bool>? canRemoveStationTargets = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(systemIds);
        ArgumentNullException.ThrowIfNull(principals);
        if (checkpoint.Setup is null || checkpoint.StationIds is null || checkpoint.ProjectIds is null || checkpoint.ModuleIds is null
            || checkpoint.Stations is null || checkpoint.Receipts is null || checkpoint.Removals is null || checkpoint.Destructions is null || checkpoint.ShipOrders is null || checkpoint.AccruedThrough != time
            || agenda.CurrentTime != time || agenda.CurrentPhase is not null)
            return Invalid("Station setup, collections and exact completed timestamp are required.");
        var systems = systemIds.ToHashSet();
        var owners = principals.ToHashSet();
        if (checkpoint.Setup.CommandAuthorities.Any(authority => !owners.Contains(authority.Principal))
            || checkpoint.Setup.CommandAuthorities.Select(authority => authority.Source).Distinct().Count() != checkpoint.Setup.CommandAuthorities.Count)
            return Invalid("Station command authority references an unknown principal or duplicates a source.");
        try { checkpoint.Setup.Definitions.ValidateForSession(checkpoint.Setup.Materials, systems); }
        catch (ArgumentException error) { return Invalid(error.Message); }
        catch (OverflowException error) { return Invalid(error.Message); }
        CheckpointResult<IdSequence<StationId>> stationIds = IdSequence<StationId>.RestoreCheckpoint(checkpoint.StationIds);
        CheckpointResult<IdSequence<StationProjectId>> projectIds = IdSequence<StationProjectId>.RestoreCheckpoint(checkpoint.ProjectIds);
        var moduleIds = IdSequence<StationModuleId>.RestoreCheckpoint(checkpoint.ModuleIds);
        if (!stationIds.IsSuccess || !projectIds.IsSuccess || !moduleIds.IsSuccess) return Invalid("A station identity allocator is invalid.");
        var owner = new StationRuntimeOwner(checkpoint.Setup, lifecycle, agenda, builder, inventoryCommit, restoring: true)
        {
            _accruedThrough = checkpoint.AccruedThrough,
            _canRemoveStationTargets = canRemoveStationTargets,
        };
        RestoreAllocator(owner._stationIds, checkpoint.StationIds);
        RestoreAllocator(owner._projectIds, checkpoint.ProjectIds);
        RestoreAllocator(owner._moduleIds, checkpoint.ModuleIds);
        if (!owner.RestoreDestructionHistory(checkpoint, time)) return Invalid("Station destruction identities or accounting history are inconsistent.");
        var entities = new HashSet<EntityId>();
        var inventories = new HashSet<InventoryId>();
        var projects = new HashSet<StationProjectId>();
        var assigned = new HashSet<ShipId>();
        var modules = new HashSet<StationModuleId>();
        var moduleBuilders = new HashSet<StationModuleId>();
        var pendingKeys = new HashSet<EventKey>();
        IdSequenceCheckpoint? recoveryReservationIds = inventoryCommit?.CaptureCheckpoint().ReservationIds;
        foreach (StationStateCheckpoint? saved in checkpoint.Stations)
        {
            if (saved is null || !Allocated(saved.Id.Value, checkpoint.StationIds) || saved.DefinitionKey is null
                || !entities.Add(saved.Entity) || !inventories.Add(saved.Inventory) || owner._stations.ContainsKey(saved.Id)
                || !owners.Contains(saved.Principal) || !systems.Contains(saved.Position.SystemId)
                || saved.Phase is not (StationPhase.Site or StationPhase.Operational or StationPhase.Dismantling)
                || saved.Projects is null || saved.Modules is null
                || !lifecycle.CanRestoreStationIdentity(saved.Entity, saved.Id, saved.Inventory, saved.Principal))
                return Invalid("A station identity, owner, position or inventory link is invalid.");
            InitialStationSetup? initial = checkpoint.Setup.InitialStations.FirstOrDefault(seed => seed.Id == saved.Id);
            if (initial is not null && (initial.EntityId != saved.Entity || initial.InventoryId != saved.Inventory
                || initial.PrincipalId != saved.Principal || initial.DefinitionKey != saved.DefinitionKey || initial.Position != saved.Position || initial.CreatedAt != saved.CreatedAt)
                || initial is null && saved.Projects.Count == 0) return Invalid("A station has no matching authored or constructed origin.");
            if (saved.CreatedAt > time) return Invalid("A station creation time is in the future.");
            StationDefinition? definition = checkpoint.Setup.Definitions.Stations.FirstOrDefault(item => item.Key == saved.DefinitionKey);
            Inventory inventory = lifecycle.Inventories.Get(saved.Inventory)!;
            if (definition is null || !definition.PlacementRules.Allows(saved.Position))
                return Invalid("A station definition, placement or storage capacity is inconsistent.");
            var station = new StationState(saved.Id, saved.Entity, inventory, saved.Principal, definition, saved.Position)
            { Phase = saved.Phase, Revision = saved.Revision, CreatedAt = saved.CreatedAt,
                LossWindow = saved.LossWindow, LossOccurrence = saved.LossOccurrence };
            foreach (StationModuleCheckpoint? savedModule in saved.Modules)
            {
                if (savedModule is null || !Allocated(savedModule.Id.Value, checkpoint.ModuleIds) || !modules.Add(savedModule.Id)
                    || savedModule.DefinitionKey is null || !Enum.IsDefined(savedModule.Phase))
                    return Invalid("A station module identity, definition or structural phase is invalid.");
                StationModuleDefinition? moduleDefinition = checkpoint.Setup.Definitions.Modules.FirstOrDefault(item => item.Key == savedModule.DefinitionKey);
                if (moduleDefinition is null) return Invalid("A station module definition does not resolve.");
                station.Modules.Add(savedModule.Id, new ModuleState(savedModule.Id, moduleDefinition, savedModule.OriginProject)
                {
                    FacilityId = savedModule.FacilityId, Phase = savedModule.Phase, Enabled = savedModule.Enabled, IsCompleted = savedModule.IsCompleted,
                    OccupiedCapacity = savedModule.OccupiedCapacity, ActiveProject = savedModule.ActiveProject,
                    UnfinishedUpgrade = savedModule.UnfinishedUpgrade, UnfinishedUpgradeProject = savedModule.UnfinishedUpgradeProject,
                });
            }
            foreach (StationProjectCheckpoint? savedProject in saved.Projects)
            {
                if (savedProject is null || !Allocated(savedProject.Id.Value, checkpoint.ProjectIds)
                    || !projects.Add(savedProject.Id) || !Enum.IsDefined(savedProject.Status)
                    || !Enum.IsDefined(savedProject.Kind)
                    || savedProject.Consumed is null || savedProject.Assignments is null || savedProject.ModuleBuilders is null || savedProject.RecoveryOutputs is null
                    || savedProject.WorkNumerator < 0 || savedProject.WorkNumerator >= 1_000 || savedProject.Rate < 0
                    || savedProject.RecoveryReservation is { } recoveryClaim && (recoveryReservationIds is null
                        || !Allocated(recoveryClaim.Value, recoveryReservationIds)))
                    return Invalid("A station project identity, progress or exact work remainder is invalid.");
                ConstructionRecipe? recipe = savedProject.Kind == StationProjectKind.Dismantling && savedProject.ModuleId is null
                    && savedProject.ModuleDefinitionKey is null ? new ConstructionRecipe([], definition.DismantleWork)
                    : owner.ResolveProjectRecipe(definition, savedProject.Kind, savedProject.ModuleDefinitionKey, savedProject.UpgradeTarget);
                if (recipe is null || savedProject.Completed.Units > recipe.RequiredWork.Units)
                    return Invalid("A station project recipe or progress is invalid.");
                var project = new ProjectState(savedProject.Id, recipe)
                {
                    Status = savedProject.Status, Completed = savedProject.Completed,
                    WorkNumerator = savedProject.WorkNumerator, Rate = savedProject.Rate,
                    Generation = savedProject.Generation, Pending = savedProject.Pending,
                    Kind = savedProject.Kind, ModuleId = savedProject.ModuleId, ModuleDefinitionKey = savedProject.ModuleDefinitionKey, ProviderFacilityId = savedProject.ProviderFacilityId,
                    UpgradeTarget = savedProject.UpgradeTarget, RecoveryInventory = savedProject.RecoveryInventory,
                    RecoveryReservation = savedProject.RecoveryReservation, DismantleAddition = savedProject.DismantleAddition,
                };
                foreach ((MaterialId material, Quantity amount) in savedProject.Consumed)
                {
                    if (amount.Units == 0 || !recipe.Inputs.ContainsKey(material))
                        return Invalid("Consumed station inputs contain an extra or zero material quantity.");
                    project.Consumed.Add(material, amount);
                }
                foreach ((MaterialId material, Quantity required) in recipe.Inputs)
                {
                    ulong expected = (ulong)(((UInt128)required.Units * savedProject.Completed.Units
                        + recipe.RequiredWork.Units - 1) / recipe.RequiredWork.Units);
                    if (project.Consumed.GetValueOrDefault(material, Quantity.Zero).Units != expected)
                        return Invalid("Station material consumption disagrees with cumulative construction progress.");
                }
                foreach (StationAssignmentCheckpoint? assignment in savedProject.Assignments)
                {
                    if (assignment is null || assignment.Ship.Value == 0 || assignment.Source is null
                        || !checkpoint.Setup.CommandAuthorities.Any(authority => authority.Source == assignment.Source && authority.Principal == saved.Principal)
                        || !assigned.Add(assignment.Ship)
                        || !owner.ValidCheckpointBuilder(assignment.Ship, assignment.Source, saved.Principal))
                        return Invalid("A station builder assignment is missing, duplicated or owned by another principal.");
                    project.Ships.Add(assignment.Ship);
                    project.Sources.Add(assignment.Ship, assignment.Source);
                }
                foreach (StationModuleId module in savedProject.ModuleBuilders)
                    if (!moduleBuilders.Add(module) || !project.ModuleBuilders.Add(module))
                        return Invalid("A station construction module is assigned more than once.");
                foreach ((MaterialId material, Quantity amount) in savedProject.RecoveryOutputs)
                {
                    if (material.Value == 0 || amount.Units == 0 || checkpoint.Setup.Materials.Get(material) is null)
                        return Invalid("A station recovery output is unresolved or invalid.");
                    project.RecoveryOutputs.Add(material, amount);
                }
                station.Projects.Add(project.Id, project);
            }
            int complete = station.Projects.Values.Count(project => project.Kind == StationProjectKind.BaseConstruction && project.Status == StationProjectStatus.Completed);
            int active = station.Projects.Values.Count(project => project.Kind == StationProjectKind.BaseConstruction && project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled));
            bool dismantling = station.Phase == StationPhase.Dismantling;
            if (complete > 1 || active > 1 || initial is not null && (complete != 0 || active != 0 || station.Phase == StationPhase.Site)
                || initial is null && !dismantling && ((station.Phase == StationPhase.Operational) != (complete == 1) || complete == 1 && active != 0)
                || dismantling && (active != 0 || station.Modules.Count != 0 || station.Inventory.UsedCapacity != Quantity.Zero
                    || station.Projects.Values.Count(project => project.Kind == StationProjectKind.Dismantling && project.ModuleId is null
                        && project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled)) != 1))
                return Invalid("Station base phase disagrees with its completed or active construction projects.");
            if (!owner.ValidateModuleCheckpointState(station)) return Invalid("Station module structure, storage or recovery references are inconsistent.");
            foreach (ProjectState project in station.Projects.Values)
                if (!ValidateProjectTiming(owner, station, project, time, pendingKeys))
                    return Invalid("Station project status, cached supply or exact pending work envelope is inconsistent.");
            owner._stations.Add(station.Id, station);
        }
        if (!owner.ValidateAndRestoreRemovalCheckpoints(checkpoint, time, systems, owners, entities, inventories, projects, modules))
            return Invalid("A station removal receipt has invalid identity, origin, causal or historical state.");
        foreach (InventoryCheckpoint inventory in lifecycle.Inventories.CaptureCheckpoint().Inventories)
        foreach (PhysicalReservation reservation in inventory.PhysicalReservations)
            if (reservation.Owner is ReservationOwner.StationProject claim
                && (!owner._stations.TryGetValue(claim.StationId, out StationState? station)
                    || !station.Projects.TryGetValue(claim.ProjectId, out ProjectState? project)
                    || project.RecoveryInventory != inventory.Id || project.RecoveryReservation != reservation.Id))
                return Invalid("A station recovery reservation has no unique matching live project claim.");
        var agendaState = agenda.CaptureCheckpoint();
        if (!agendaState.IsSuccess || agendaState.Value!.PendingEvents.Any(item => item.Payload is GameEvent.StationWork && !pendingKeys.Contains(item.Key)))
            return Invalid("The agenda contains station work with no exact owner reference.");
        foreach (StationCommandReceiptCheckpoint? receipt in checkpoint.Receipts)
        {
            if (receipt?.Request is not { } request || receipt.Result is not { Status: CommandResultStatus.Accepted }
                || request.Sequence.Value == 0 || request.SubmittedAt > time
                || !checkpoint.Setup.CommandAuthorities.Any(authority => authority.Source == request.Source && authority.Principal == CommandPrincipal(request.Command))
                || !owner.ValidReceiptCommand(request.Command, owners)
                || !owner._receipts.TryAdd(request.Sequence, (request, receipt.Result)))
                return Invalid("A station command receipt is missing, duplicated or inconsistent with retained authority.");
        }
        if (!owner.RestoreShipOrderReceipts(checkpoint.ShipOrders)) return Invalid("Station ship orders do not match their accepted commands.");
        if (!owner.ValidateStationOrigins()) return Invalid("Station origins do not match explicit setup or creation receipts.");
        if (!owner.ValidProjectAdmissions()) return Invalid("Station project history is not explained by accepted admission receipts.");
        if (!owner.ValidateRetiredProjectDefinitions()) return Invalid("A retired station project definition or recipe disagrees with its accepted origin.");
        if (!owner.ValidateDestructionOrigins()) return Invalid("Station destruction before-state is not explained by its retained identity and structural history.");
        // Publication is last, after malformed state can no longer fail ordinary admission.
        foreach (StationState station in owner._stations.Values)
            lifecycle.ApplyRestoreStationIdentity(station.Entity, station.Id);
        return CheckpointResult<StationRuntimeOwner>.Success(owner);
    }

    /// <summary>Allows a live capable builder in transit to retain its assignment even though it has no system-local point.</summary>
    private bool ValidCheckpointBuilder(ShipId ship, CommandSource source, PrincipalId principal)
    {
        if (_builder(ship, source) is { } current) return current.Principal == principal;
        return _lifecycle.GetShip(ship) is { } actor && actor.PrincipalId == principal
            && _lifecycle.GetOperationalShipDefinition(actor.DesignId)?.StationConstructionCapability is not null;
    }

    /// <summary>Checks module/project links, retained structural claims and exact shared storage limits before publication.</summary>
    private bool ValidateModuleCheckpointState(StationState station)
    {
        bool baseOperational = _setup.InitialStations.Any(seed => seed.Id == station.Id)
            || station.Projects.Values.Any(project => project.Kind == StationProjectKind.BaseConstruction && project.Status == StationProjectStatus.Completed);
        BigInteger physical = baseOperational ? station.Definition.BaseCapacity.Units : station.Definition.StagingCapacity.Units;
        BigInteger enabled = physical;
        BigInteger protectedReduction = 0;
        BigInteger occupied = 0;
        foreach (ModuleState module in station.Modules.Values)
        {
            InitialStationModuleSetup? initialModule = _setup.InitialStations.FirstOrDefault(seed => seed.Id == station.Id)
                ?.Modules.FirstOrDefault(seed => seed.Id == module.Id);
            ProjectState? origin = null;
            if (station.Phase != StationPhase.Operational || !station.Definition.AllowedModules.Contains(module.Definition.Key)) return false;
            if (module.OriginProject is { } originId)
            {
                if (initialModule is not null || !station.Projects.TryGetValue(originId, out origin)
                    || origin.Kind != StationProjectKind.ModuleConstruction || origin.ModuleId != module.Id
                    || module.IsCompleted != (origin.Status == StationProjectStatus.Completed)
                    || origin.Status == StationProjectStatus.Cancelled && origin.Consumed.Count == 0) return false;
            }
            else if (initialModule is null || !module.IsCompleted) return false;
            QualifiedContentKey? currentDefinition = origin?.ModuleDefinitionKey ?? initialModule?.DefinitionKey;
            foreach (ProjectState upgrade in station.Projects.Values.Where(project => project.ModuleId == module.Id
                && project.Kind == StationProjectKind.Upgrade && project.Status == StationProjectStatus.Completed))
            {
                if (upgrade.ModuleDefinitionKey != currentDefinition) return false;
                currentDefinition = upgrade.UpgradeTarget;
            }
            if (module.Definition.Key != currentDefinition || station.Projects.Values.Any(project => project.ModuleId == module.Id
                && project.Kind == StationProjectKind.Dismantling && !project.DismantleAddition && project.Status == StationProjectStatus.Completed)) return false;
            ProjectState? active = null;
            if (module.ActiveProject is { } activeId && (!station.Projects.TryGetValue(activeId, out active)
                || active.ModuleId != module.Id || active.Status is StationProjectStatus.Completed or StationProjectStatus.Cancelled)) return false;
            ProjectState[] live = station.Projects.Values.Where(project => project.ModuleId == module.Id
                && project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled)).ToArray();
            if (live.Length != (active is null ? 0 : 1) || active is not null && live[0] != active) return false;
            if (active is not null && !active.DismantleAddition && active.ModuleDefinitionKey != module.Definition.Key) return false;
            if (module.Phase == StationModulePhase.UnderConstruction && (module.IsCompleted || active != origin)
                || module.Phase == StationModulePhase.Operational && !module.IsCompleted
                || module.Phase == StationModulePhase.Unfinished && (module.IsCompleted || origin?.Status != StationProjectStatus.Cancelled || active is not null)
                || module.Phase == StationModulePhase.Dismantling && (active?.Kind != StationProjectKind.Dismantling || active.DismantleAddition)) return false;
            ulong expectedOccupied = module.Definition.CapacityCost;
            if ((module.UnfinishedUpgrade is null) != (module.UnfinishedUpgradeProject is null)) return false;
            if (module.UnfinishedUpgrade is { } unfinished)
            {
                StationModuleDefinition? target = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == unfinished);
                if (target is null || !station.Projects.TryGetValue(module.UnfinishedUpgradeProject!.Value, out ProjectState? cancelled)
                    || cancelled.Kind != StationProjectKind.Upgrade || cancelled.ModuleId != module.Id
                    || cancelled.UpgradeTarget != unfinished || cancelled.Status != StationProjectStatus.Cancelled
                    || cancelled.Consumed.Count == 0) return false;
                expectedOccupied = Math.Max(expectedOccupied, target.CapacityCost);
            }
            if (active?.Kind == StationProjectKind.Upgrade)
            {
                StationModuleDefinition? target = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == active.UpgradeTarget);
                if (target is null || module.UnfinishedUpgrade is not null) return false;
                expectedOccupied = Math.Max(expectedOccupied, target.CapacityCost);
                protectedReduction += BigInteger.Max(0, (BigInteger)module.Definition.StorageCapacity.Units - target.StorageCapacity.Units);
            }
            if (module.OccupiedCapacity != expectedOccupied) return false;
            occupied += module.OccupiedCapacity;
            if (module.IsCompleted)
            {
                physical += module.Definition.StorageCapacity.Units;
                if (module.Enabled && module.Phase == StationModulePhase.Operational) enabled += module.Definition.StorageCapacity.Units;
                if (active is { Kind: StationProjectKind.Dismantling, DismantleAddition: false })
                    protectedReduction += module.Definition.StorageCapacity.Units;
            }
        }
        IEnumerable<StationModuleId> establishedModules = station.Projects.Values.Where(project =>
            project.Kind == StationProjectKind.ModuleConstruction && (project.Status == StationProjectStatus.Completed || project.Consumed.Count > 0))
            .Select(project => project.ModuleId.GetValueOrDefault()).Concat(_setup.InitialStations.FirstOrDefault(seed => seed.Id == station.Id)?.Modules.Select(module => module.Id) ?? []);
        foreach (StationModuleId module in establishedModules)
            if (!station.Modules.ContainsKey(module) && !station.Projects.Values.Any(project => project.Kind == StationProjectKind.Dismantling
                && project.ModuleId == module && !project.DismantleAddition && project.Status == StationProjectStatus.Completed)
                && !WasModuleDestroyed(station.Id, module)) return false;
        if (station.Phase == StationPhase.Dismantling) { enabled = 0; protectedReduction = physical; }
        bool protectedBase = station.Phase == StationPhase.Dismantling;
        if (occupied > station.Definition.ModuleCapacity || PotentialPhysicalCapacity(station) > ulong.MaxValue || physical > ulong.MaxValue || enabled > ulong.MaxValue
            || physical < protectedReduction || station.Inventory.Capacity.Units != physical
            || station.Inventory.EnabledIntakeCapacity.Units != enabled
            || station.Inventory.ProtectedIntakeCapacity?.Units != (protectedReduction == 0 && !protectedBase ? (ulong?)null : (ulong)(physical - protectedReduction))) return false;
        foreach (ProjectState project in station.Projects.Values)
        {
            if (project.Kind == StationProjectKind.BaseConstruction)
            {
                if (project.ModuleId is not null || project.ModuleDefinitionKey is not null || project.UpgradeTarget is not null
                    || project.DismantleAddition || project.RecoveryInventory is not null || project.RecoveryReservation is not null
                    || project.RecoveryOutputs.Count != 0) return false;
            }
            else if (project.Kind == StationProjectKind.Dismantling && project.ModuleId is null)
            {
                if (project.ModuleDefinitionKey is not null || project.UpgradeTarget is not null || project.DismantleAddition
                    || project.ModuleBuilders.Count != 0 || project.RecoveryInventory == station.Inventory.Id) return false;
            }
            else
            {
                if (project.ModuleId is not { } moduleId || !Allocated(moduleId.Value, _moduleIds.CaptureCheckpoint())
                    || project.ModuleDefinitionKey is null) return false;
                if (project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled)
                    && !station.Modules.ContainsKey(moduleId)) return false;
                if (project.Kind == StationProjectKind.ModuleConstruction && project.UpgradeTarget is not null
                    || project.Kind == StationProjectKind.Dismantling && project.UpgradeTarget is not null
                    || project.Kind == StationProjectKind.Upgrade && (project.UpgradeTarget is null
                        || !station.Definition.AllowedModules.Contains(project.UpgradeTarget))) return false;
            }
            foreach (StationModuleId builder in project.ModuleBuilders)
                if (!station.Modules.TryGetValue(builder, out ModuleState? module) || !module.IsCompleted
                    || module.Definition.ConstructionCapability is null || project.ModuleId == builder) return false;
            if (project.Kind != StationProjectKind.Dismantling && (project.RecoveryInventory is not null
                || project.RecoveryReservation is not null || project.RecoveryOutputs.Count != 0 || project.DismantleAddition)) return false;
            if (project.Kind == StationProjectKind.Dismantling)
            {
                IReadOnlyDictionary<MaterialId, Quantity>? expectedRecovery = ExpectedCheckpointRecovery(station, project);
                if (expectedRecovery is null || expectedRecovery.Count != project.RecoveryOutputs.Count
                    || expectedRecovery.Any(pair => project.RecoveryOutputs.GetValueOrDefault(pair.Key, Quantity.Zero) != pair.Value)) return false;
            }
            if (project.RecoveryInventory is { } destination && !_lifecycle.IsAllocatedStationInventory(destination)) return false;
            if (project.RecoveryReservation is { } reservation)
            {
                if (project.RecoveryInventory is not { } destinationId || reservation.Value == 0) return false;
                // A lost destination or invalidated claim is retained as historical intent until explicit replacement.
                if (_lifecycle.Inventories.Get(destinationId) is { } inventory && inventory.GetPhysicalReservation(reservation) is { } held
                    && (inventory.Custody is not InventoryCustody.PrincipalControlled
                        || held.Subject is not PhysicalReservationSubject.IncomingCapacity capacity
                        || held.InventoryId != destinationId || !IsRecoveryReservationOwner(held.Owner, station.Id, project.Id)
                        || capacity.Quantity.Units != project.RecoveryOutputs.Values.Aggregate(BigInteger.Zero, (total, quantity) => total + quantity.Units))) return false;
            }
        }
        return true;
    }

    /// <summary>Reconstructs authored recovery from retained consumed-input history without granting unfinished structures completed outputs.</summary>
    private IReadOnlyDictionary<MaterialId, Quantity>? ExpectedCheckpointRecovery(StationState station, ProjectState project)
    {
        if (project.ModuleId is null)
        {
            bool completed = _setup.InitialStations.Any(seed => seed.Id == station.Id) || station.Projects.Values.Any(prior =>
                prior.Kind == StationProjectKind.BaseConstruction && prior.Status == StationProjectStatus.Completed);
            if (completed) return station.Definition.DismantleRecovery;
            var consumed = new Dictionary<MaterialId, Quantity>();
            foreach (ProjectState prior in station.Projects.Values.Where(prior => prior.Kind == StationProjectKind.BaseConstruction))
            foreach ((MaterialId material, Quantity amount) in prior.Consumed)
            {
                ulong existing = consumed.GetValueOrDefault(material, Quantity.Zero).Units;
                // Recovery never exceeds its authored cap, so saturated accumulation preserves the exact output without overflow.
                consumed[material] = new(existing + Math.Min(ulong.MaxValue - existing, amount.Units));
            }
            return StationDismantlingRecovery.Evaluate(station.Definition.DismantleRecovery, consumed);
        }
        StationModuleDefinition? definition = _setup.Definitions.Modules.FirstOrDefault(value => value.Key == project.ModuleDefinitionKey);
        if (definition is null) return null;
        if (project.DismantleAddition)
        {
            ProjectState? cancelled = station.Projects.Values.LastOrDefault(prior => prior.Id.Value < project.Id.Value
                && prior.Kind == StationProjectKind.Upgrade && prior.ModuleId == project.ModuleId
                && prior.UpgradeTarget == project.ModuleDefinitionKey && prior.Status == StationProjectStatus.Cancelled);
            return cancelled is null ? null : StationDismantlingRecovery.Evaluate(definition.DismantleRecovery, cancelled.Consumed);
        }
        ProjectState? origin = station.Projects.Values.FirstOrDefault(prior => prior.Kind == StationProjectKind.ModuleConstruction
            && prior.ModuleId == project.ModuleId);
        if (origin is null && _setup.InitialStations.FirstOrDefault(seed => seed.Id == station.Id)?.Modules.Any(seed => seed.Id == project.ModuleId) == true)
            return definition.DismantleRecovery;
        return origin is null ? null : origin.Status == StationProjectStatus.Completed ? definition.DismantleRecovery
            : StationDismantlingRecovery.Evaluate(definition.DismantleRecovery, origin.Consumed);
    }

    /// <summary>Checks an exact owning station/project pair without allowing a different workflow's capacity claim.</summary>
    private static bool IsRecoveryReservationOwner(ReservationOwner owner, StationId station, StationProjectId project) =>
        owner is ReservationOwner.StationProject reservation && reservation.StationId == station && reservation.ProjectId == project;

    /// <summary>Reads the explicit requesting principal from each supported station command; no source-name inference is allowed.</summary>
    private static PrincipalId? CommandPrincipal(GameplayCommand command) => command switch
    {
        CreateStationSiteCommand create => create.RequestingPrincipal,
        AssignStationBuilderCommand assign => assign.RequestingPrincipal,
        ReleaseStationBuilderCommand release => release.RequestingPrincipal,
        ControlStationProjectCommand control => control.RequestingPrincipal,
        DismantleStationBaseCommand dismantleBase => dismantleBase.RequestingPrincipal,
        ConstructStationModuleCommand construct => construct.RequestingPrincipal,
        UpgradeStationModuleCommand upgrade => upgrade.RequestingPrincipal,
        DismantleStationModuleCommand dismantle => dismantle.RequestingPrincipal,
        SetStationModuleEnabledCommand enabled => enabled.RequestingPrincipal,
        EnqueueStationShipCommand enqueue => enqueue.RequestingPrincipal,
        AssignStationModuleBuilderCommand assign => assign.RequestingPrincipal,
        SetStationRecoveryDestinationCommand destination => destination.RequestingPrincipal,
        _ => null,
    };

    /// <summary>Matches every retained project to exactly one accepted creation command in authoritative admission order.</summary>
    private bool ValidProjectAdmissions()
    {
        var admissions = new List<(GameplayCommand Command, StationId Station)>();
        StationOrigin[] constructed = CaptureStationOrigins().Where(origin => !_setup.InitialStations.Any(seed => seed.Id == origin.Id)).ToArray();
        int created = 0;
        foreach (var receipt in _receipts.Values.OrderBy(receipt => receipt.Request.Sequence.Value))
        {
            GameplayCommand command = receipt.Request.Command;
            if (command is CreateStationSiteCommand)
            {
                if (created >= constructed.Length) return false;
                StationId id = constructed[created++].Id;
                if (_stations.ContainsKey(id)) admissions.Add((command, id));
            }
            else if (command is ConstructStationModuleCommand or UpgradeStationModuleCommand or DismantleStationModuleCommand
                or DismantleStationBaseCommand or ControlStationProjectCommand { Action: StationProjectAction.RestartBaseConstruction }
                && CommandStation(command) is { } station && _stations.ContainsKey(station)) admissions.Add((command, station));
        }
        (StationState Station, ProjectState Project)[] projects = OrderedProjects().ToArray();
        if (admissions.Count != projects.Length) return false;
        for (int index = 0; index < admissions.Count; index++)
        {
            (StationState station, ProjectState project) = projects[index];
            if (admissions[index].Station != station.Id) return false;
            bool valid = admissions[index].Command switch
            {
                CreateStationSiteCommand => project.Kind == StationProjectKind.BaseConstruction,
                ControlStationProjectCommand restart => project.Kind == StationProjectKind.BaseConstruction && project.Id.Value > restart.Project.Value,
                ConstructStationModuleCommand construct => project.Kind == StationProjectKind.ModuleConstruction && project.ModuleDefinitionKey == construct.DefinitionKey,
                UpgradeStationModuleCommand upgrade => project.Kind == StationProjectKind.Upgrade && project.ModuleId == upgrade.Module && project.UpgradeTarget == upgrade.UpgradeTarget,
                DismantleStationModuleCommand dismantle => project.Kind == StationProjectKind.Dismantling && project.ModuleId == dismantle.Module
                    && project.DismantleAddition == dismantle.UnfinishedUpgradeAddition,
                DismantleStationBaseCommand => project.Kind == StationProjectKind.Dismantling && project.ModuleId is null,
                _ => false,
            };
            if (!valid) return false;
        }
        return true;
    }

    /// <summary>Checks supported station command targets against live authority or retained removal evidence.</summary>
    private bool ValidReceiptCommand(GameplayCommand command, HashSet<PrincipalId> principals)
    {
        if (CommandStation(command) is { } target && !_stations.ContainsKey(target)) return ValidRemovedStationCommand(command, target);
        return command switch
        {
        CreateStationSiteCommand create => principals.Contains(create.RequestingPrincipal) && create.BuilderShip.Value != 0,
        AssignStationBuilderCommand assign => _stations.TryGetValue(assign.Station, out StationState? station)
            && station.Principal == assign.RequestingPrincipal && station.Projects.ContainsKey(assign.Project) && assign.BuilderShip.Value != 0,
        ReleaseStationBuilderCommand release => _stations.TryGetValue(release.Station, out StationState? station)
            && station.Principal == release.RequestingPrincipal && station.Projects.ContainsKey(release.Project) && release.BuilderShip.Value != 0,
        ControlStationProjectCommand control => _stations.TryGetValue(control.Station, out StationState? station)
            && station.Principal == control.RequestingPrincipal && station.Projects.ContainsKey(control.Project) && Enum.IsDefined(control.Action),
        DismantleStationBaseCommand dismantleBase => _stations.TryGetValue(dismantleBase.Station, out StationState? station)
            && station.Principal == dismantleBase.RequestingPrincipal && dismantleBase.RecoveryInventory.Value != 0,
        ConstructStationModuleCommand construct => _stations.TryGetValue(construct.Station, out StationState? station)
            && station.Principal == construct.RequestingPrincipal && station.Definition.AllowedModules.Contains(construct.DefinitionKey),
        UpgradeStationModuleCommand upgrade => _stations.TryGetValue(upgrade.Station, out StationState? station)
            && station.Principal == upgrade.RequestingPrincipal && upgrade.Module.Value != 0 && upgrade.UpgradeTarget is not null,
        DismantleStationModuleCommand dismantle => _stations.TryGetValue(dismantle.Station, out StationState? station)
            && station.Principal == dismantle.RequestingPrincipal && dismantle.Module.Value != 0 && dismantle.RecoveryInventory.Value != 0,
        EnqueueStationShipCommand enqueue => _stations.TryGetValue(enqueue.Station, out StationState? station)
            && station.Principal == enqueue.RequestingPrincipal && enqueue.Module.Value != 0 && enqueue.ExpectedFacility.Value != 0 && enqueue.Design.Value != 0,
        SetStationModuleEnabledCommand enabled => _stations.TryGetValue(enabled.Station, out StationState? station)
            && station.Principal == enabled.RequestingPrincipal && enabled.Module.Value != 0,
        AssignStationModuleBuilderCommand assign => _stations.TryGetValue(assign.Station, out StationState? station)
            && station.Principal == assign.RequestingPrincipal && station.Projects.ContainsKey(assign.Project) && assign.BuilderModule.Value != 0,
        SetStationRecoveryDestinationCommand destination => _stations.TryGetValue(destination.Station, out StationState? station)
            && station.Principal == destination.RequestingPrincipal && station.Projects.ContainsKey(destination.Project)
            && destination.RecoveryInventory.Value != 0,
        _ => false,
        };
    }

    /// <summary>Recomputes cached work supply and checks one exact future agenda identity without scheduling replacements.</summary>
    private static bool ValidateProjectTiming(StationRuntimeOwner owner, StationState station, ProjectState project,
        SimulationTime time, HashSet<EventKey> pendingKeys)
    {
        bool complete = project.Completed == project.Recipe.RequiredWork;
        if (project.Status == StationProjectStatus.Completed && !complete) return false;
        if (complete && project.Status != StationProjectStatus.Completed
            && !(project.Kind == StationProjectKind.Dismantling && project.Status is StationProjectStatus.Waiting or StationProjectStatus.Paused)) return false;
        if (complete && project.Kind == StationProjectKind.Dismantling && project.Status is StationProjectStatus.Waiting or StationProjectStatus.Paused)
        {
            if (project.Rate != 0 || project.WorkNumerator != 0) return false;
            bool retry = project.Status != StationProjectStatus.Paused && owner.RecoveryAvailable(station, project)
                && (project.ModuleId is not null || owner._canRemoveStationTargets?.Invoke(station.Entity) != false);
            if (project.Pending is not { } finalization) return !retry;
            return retry && time.Milliseconds < ulong.MaxValue && finalization.Timestamp.Milliseconds == time.Milliseconds + 1
                && finalization.Phase == EventPhase.PhysicalCompletion && pendingKeys.Add(finalization)
                && owner._agenda.CheckCancellation(finalization, project.Generation, new GameEvent.StationWork(station.Id, project.Id))
                    == AgendaCancellationCheck.Matches;
        }
        bool retired = project.Status is StationProjectStatus.Completed or StationProjectStatus.Cancelled;
        if (retired && (project.Ships.Count != 0 || project.ModuleBuilders.Count != 0 || project.Rate != 0 || project.Pending is not null)) return false;
        if (project.Status == StationProjectStatus.Cancelled && project.WorkNumerator != 0) return false;
        BigInteger expectedRate = 0;
        if (!retired && project.Status != StationProjectStatus.Paused && NextIncrementAvailable(station, project))
            foreach (ShipId ship in project.Ships)
                if (owner._builder(ship, project.Sources[ship]) is { IsEligible: true } actor
                    && actor.Principal == station.Principal && InRange(actor, station.Position))
                    expectedRate += actor.Capability.Throughput.UnitsPerSecond;
        if (!retired && project.Status != StationProjectStatus.Paused && NextIncrementAvailable(station, project))
            foreach (StationModuleId moduleId in project.ModuleBuilders)
            {
                if (!station.Modules.TryGetValue(moduleId, out ModuleState? module) || !module.IsCompleted
                    || module.Definition.ConstructionCapability is not { } capability || project.ModuleId == moduleId)
                    return false;
                if (module.Enabled && module.Phase == StationModulePhase.Operational) expectedRate += capability.Throughput.UnitsPerSecond;
            }
        if (project.Rate != expectedRate || (project.Pending is not null) != (expectedRate > 0)
            || (project.Status == StationProjectStatus.Working) != (expectedRate > 0)) return false;
        if (project.Pending is not { } pending) return true;
        SimulationTime expectedDue;
        try { expectedDue = WorkDueAt(station, project, time, expectedRate); }
        catch (OverflowException) { return false; }
        return pending.Timestamp == expectedDue && pending.Phase == EventPhase.PhysicalCompletion && pendingKeys.Add(pending)
            && owner._agenda.CheckCancellation(pending, project.Generation, new GameEvent.StationWork(station.Id, project.Id))
                == AgendaCancellationCheck.Matches;
    }

    /// <summary>Restores the validated high-water mark directly without allocating any historical identifiers.</summary>
    private static void RestoreAllocator<TId>(IdSequence<TId> target, IdSequenceCheckpoint saved) where TId : struct, IEntityId<TId>
    {
        if (saved.NextValue is null) target.AdvancePast(TId.Create(ulong.MaxValue));
        else if (saved.NextValue > 1) target.AdvancePast(TId.Create(saved.NextValue.Value - 1));
    }

    /// <summary>Checks nonzero identities against the exact saved high-water mark, including exhausted allocators.</summary>
    private static bool Allocated(ulong id, IdSequenceCheckpoint sequence) => id != 0 && (sequence.NextValue is null || id < sequence.NextValue);
    private static CheckpointResult<StationRuntimeOwner> Invalid(string message) =>
        CheckpointResult<StationRuntimeOwner>.Rejected(new("$.checkpoint.stations", message));
}
