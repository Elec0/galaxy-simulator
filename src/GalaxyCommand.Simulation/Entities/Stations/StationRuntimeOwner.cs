using System.Collections.ObjectModel;
using System.Numerics;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record StationBuilderState(PrincipalId Principal, StationConstructionCapability Capability,
    SystemPosition Position, bool IsEligible);

/// <summary>Single station authority; reads committed builder/inventory views and publishes only in deterministic order.</summary>
internal sealed partial class StationRuntimeOwner
{
    private readonly StationRuntimeSetup _setup;
    private readonly EntityLifecycleOwner _lifecycle;
    private readonly EventAgenda<GameEvent> _agenda;
    private readonly Func<ShipId, CommandSource, StationBuilderState?> _builder;
    private readonly SortedDictionary<StationId, StationState> _stations = new(EntityIdComparer<StationId>.Instance);
    private readonly IdSequence<StationId> _stationIds = new();
    private readonly IdSequence<StationProjectId> _projectIds = new();
    private readonly IdSequence<StationModuleId> _moduleIds = new();
    private readonly InventoryCommitOwner? _inventoryCommit;
    private readonly Dictionary<CommandSequence, (GameplayCommandEnvelope Request, CommandResult Result)> _receipts = [];
    private readonly SortedDictionary<EntityId, StationRemovalReceipt> _stationRemovals = new(EntityIdComparer<EntityId>.Instance);
    private SimulationTime _accruedThrough;

    internal StationRuntimeOwner(StationRuntimeSetup setup, EntityLifecycleOwner lifecycle, EventAgenda<GameEvent> agenda,
        Func<ShipId, CommandSource, StationBuilderState?> builder, InventoryCommitOwner? inventoryCommit = null,
        bool restoring = false)
    {
        _setup = setup;
        _lifecycle = lifecycle;
        _agenda = agenda;
        _builder = builder;
        _inventoryCommit = inventoryCommit;
        if (!restoring) InitializeStations();
    }

    /// <summary>Captures immutable diagnostic state without exposing writable project or inventory authority.</summary>
    internal IReadOnlyList<StationSnapshot> Capture() => Array.AsReadOnly(_stations.Values.Select(station =>
        new StationSnapshot(station.Id, station.Entity, station.Inventory.Id, station.Principal, station.Definition.Key,
            station.Position, station.Phase, station.Revision,
            SensorRadius(station),
            Array.AsReadOnly(station.Projects.Values.Select(project => new StationProjectSnapshot(project.Id,
                project.Kind, project.Status, project.Completed, project.Recipe.RequiredWork,
                new ReadOnlyDictionary<MaterialId, Quantity>(new SortedDictionary<MaterialId, Quantity>(project.Consumed,
                    EntityIdComparer<MaterialId>.Instance)),
                Array.AsReadOnly(project.Ships.OrderBy(id => id.Value).ToArray()), project.ModuleId, project.UpgradeTarget,
                Array.AsReadOnly(project.ModuleBuilders.OrderBy(id => id.Value).ToArray()), project.ModuleDefinitionKey, project.ProviderFacilityId)).ToArray()))
        { Modules = Array.AsReadOnly(station.Modules.Values.Select(module => new StationModuleSnapshot(module.Id,
            module.Definition.Key, module.Phase, module.Enabled, module.OccupiedCapacity, module.ActiveProject,
            module.UnfinishedUpgrade, module.FacilityId)).ToArray()) }).ToArray());

    /// <summary>Resolves only current registered stationary authority; historical observations cannot become navigation targets.</summary>
    internal SystemPosition? ResolvePosition(EntityId entity) =>
        _lifecycle.Entities.GetStationId(entity) is { } id && _stations.TryGetValue(id, out StationState? station)
            ? station.Position : null;

    /// <summary>Resolves command replay before any identity, assignment or agenda mutation.</summary>
    internal GameplayCommandHandlingResult Handle(GameplayCommandEnvelope envelope)
    {
        PrincipalId? requestingPrincipal = envelope.Command switch
        {
            CreateStationSiteCommand command => command.RequestingPrincipal,
            AssignStationBuilderCommand command => command.RequestingPrincipal,
            ControlStationProjectCommand command => command.RequestingPrincipal,
            ReleaseStationBuilderCommand command => command.RequestingPrincipal,
            DismantleStationBaseCommand command => command.RequestingPrincipal,
            ConstructStationModuleCommand command => command.RequestingPrincipal,
            UpgradeStationModuleCommand command => command.RequestingPrincipal,
            DismantleStationModuleCommand command => command.RequestingPrincipal,
            SetStationModuleEnabledCommand command => command.RequestingPrincipal,
            EnqueueStationShipCommand command => command.RequestingPrincipal,
            AssignStationModuleBuilderCommand command => command.RequestingPrincipal,
            ReleaseStationModuleBuilderCommand command => command.RequestingPrincipal,
            SetStationRecoveryDestinationCommand command => command.RequestingPrincipal,
            _ => null,
        };
        if (requestingPrincipal is null || !_setup.CommandAuthorities.Any(authority => authority.Source == envelope.Source
            && authority.Principal == requestingPrincipal)) return Rejected("station-wrong-authority");
        if (_receipts.TryGetValue(envelope.Sequence, out var prior))
            return prior.Request == envelope ? new(prior.Result) : Rejected("station-command-conflict");
        // Every accepted station command below advances this aggregate revision.
        // Reject exhaustion before inventory, provider, project or identity owners change.
        if (CommandStation(envelope.Command) is { } target && _stations.TryGetValue(target, out StationState? current)
            && current.Principal == requestingPrincipal && current.Revision == ulong.MaxValue)
            return Rejected("station-revision-exhausted");
        GameplayCommandHandlingResult result = envelope.Command switch
        {
            CreateStationSiteCommand create => Create(envelope, create),
            AssignStationBuilderCommand assign => Assign(envelope.Source, assign),
            ControlStationProjectCommand control => Control(control),
            ReleaseStationBuilderCommand release => Release(release),
            ConstructStationModuleCommand construct => ConstructModule(construct),
            UpgradeStationModuleCommand upgrade => UpgradeModule(upgrade),
            DismantleStationModuleCommand dismantle => DismantleModule(dismantle),
            SetStationModuleEnabledCommand enable => EnableModule(enable),
            EnqueueStationShipCommand enqueue => EnqueueShip(envelope, enqueue),
            AssignStationModuleBuilderCommand assignModule => AssignModuleBuilder(assignModule),
            ReleaseStationModuleBuilderCommand releaseModule => ReleaseModuleBuilder(releaseModule),
            SetStationRecoveryDestinationCommand recovery => ReplaceRecoveryDestination(recovery, envelope.Sequence),
            DismantleStationBaseCommand dismantleBase => DismantleBase(dismantleBase),
            _ => Rejected("unsupported-station-command"),
        };
        if (result.Result.Status == CommandResultStatus.Accepted)
            _receipts.Add(envelope.Sequence, (envelope, result.Result));
        return result;
    }

    /// <summary>Creates no stock or supplied work; identity allocation begins only after every site check succeeds.</summary>
    private GameplayCommandHandlingResult Create(GameplayCommandEnvelope envelope, CreateStationSiteCommand command)
    {
        StationDefinition? definition = _setup.Definitions.Stations.FirstOrDefault(item => item.Key == command.DefinitionKey);
        StationBuilderState? builder = _builder(command.BuilderShip, envelope.Source);
        if (definition is null) return Rejected("station-definition-unavailable");
        if (builder is null || !builder.IsEligible) return Rejected("station-builder-unavailable");
        if (builder.Principal != command.RequestingPrincipal) return Rejected("station-wrong-authority");
        if (command.Position.SystemId.Value == 0) return Rejected("station-invalid-placement");
        if (!InRange(builder, command.Position) || !definition.PlacementRules.Allows(command.Position))
            return Rejected("station-invalid-placement");
        if (!_stationIds.TryPeek(out StationId station) || !_projectIds.TryPeek(out StationProjectId project))
            return Rejected("station-identifiers-exhausted");
        PreparedStationIdentity? identity = _lifecycle.PrepareStationIdentity(command.RequestingPrincipal,
            definition.StagingCapacity, _setup.Materials);
        if (identity is null) return Rejected("station-identifiers-exhausted");

        var state = new StationState(station, identity.Entity, identity.Inventory, command.RequestingPrincipal,
            definition, command.Position) { CreatedAt = envelope.SubmittedAt };
        state.Projects.Add(project, new ProjectState(project, definition.BaseRecipe));
        _lifecycle.ApplyStationIdentity(identity, station);
        _stationIds.Allocate();
        _projectIds.Allocate();
        _stations.Add(station, state);
        return new(CommandResult.Accepted(), [new GameFactProposal(
            new(GameFactCommitCategory.EntityLifecycle, identity.Entity.Value, station.Value, 0),
            new StationCreatedFact(station, identity.Entity, command.RequestingPrincipal, definition.Key, command.Position))]);
    }

    /// <summary>Reassignment releases a builder's previous claim atomically and never chooses another project implicitly.</summary>
    private GameplayCommandHandlingResult Assign(CommandSource source, AssignStationBuilderCommand command)
    {
        if (!_stations.TryGetValue(command.Station, out StationState? station)
            || !station.Projects.TryGetValue(command.Project, out ProjectState? project)) return Rejected("station-stale-target");
        if (station.Revision != command.ExpectedStationRevision) return Rejected("station-stale-target");
        if (station.Principal != command.RequestingPrincipal) return Rejected("station-wrong-authority");
        if (project.Status is StationProjectStatus.Completed or StationProjectStatus.Cancelled) return Rejected("station-project-retired");
        StationBuilderState? builder = _builder(command.BuilderShip, source);
        if (builder is null || builder.Principal != station.Principal) return Rejected("station-builder-unavailable");
        foreach (StationState prior in _stations.Values)
        foreach (ProjectState priorProject in prior.Projects.Values)
            if (priorProject.Ships.Remove(command.BuilderShip))
            {
                priorProject.Sources.Remove(command.BuilderShip);
                prior.Revision = checked(prior.Revision + 1);
            }
        project.Ships.Add(command.BuilderShip);
        project.Sources.Add(command.BuilderShip, source);
        station.Revision = checked(station.Revision + 1);
        return new(CommandResult.Accepted());
    }

    /// <summary>Changes resumable work explicitly; cancellation never refunds consumed inputs or removes the site.</summary>
    private GameplayCommandHandlingResult Control(ControlStationProjectCommand command)
    {
        if (!_stations.TryGetValue(command.Station, out StationState? station)
            || !station.Projects.TryGetValue(command.Project, out ProjectState? project)
            || station.Revision != command.ExpectedStationRevision) return Rejected("station-stale-target");
        if (station.Principal != command.RequestingPrincipal) return Rejected("station-wrong-authority");
        if (!Enum.IsDefined(command.Action)) return Rejected("station-invalid-project-action");
        if (command.Action == StationProjectAction.RestartBaseConstruction)
        {
            if (station.Phase != StationPhase.Site || project.Status != StationProjectStatus.Cancelled
                || station.Projects.Values.Any(item => item.Status != StationProjectStatus.Cancelled))
                return Rejected("station-project-not-restartable");
            if (!_projectIds.TryPeek(out StationProjectId next)) return Rejected("station-identifiers-exhausted");
            station.Projects.Add(next, new ProjectState(next, station.Definition.BaseRecipe));
            _projectIds.Allocate();
            station.Revision = checked(station.Revision + 1);
            return Changed(station, station.Projects[next]);
        }
        if (project.Status is StationProjectStatus.Completed or StationProjectStatus.Cancelled)
            return Rejected("station-project-retired");
        if (command.Action == StationProjectAction.Cancel && project.Kind == StationProjectKind.Dismantling
            && (project.Completed.Units != 0 || project.WorkNumerator != 0))
            return Rejected("station-dismantling-irreversible");
        if (command.Action == StationProjectAction.Cancel && !CanCancelStructuralProvider(station, project))
            return Rejected("station-provider-unavailable");
        if (project.Pending is not null && project.Generation.Value == ulong.MaxValue)
            return Rejected("station-work-generation-exhausted");
        CancelPending(station, project);
        project.Rate = 0;
        IReadOnlyList<GameFactProposal> structuralFacts = [];
        switch (command.Action)
        {
            case StationProjectAction.Pause:
                project.Status = StationProjectStatus.Paused;
                break;
            case StationProjectAction.Resume:
                project.Status = StationProjectStatus.Waiting;
                break;
            case StationProjectAction.Cancel:
                project.Status = StationProjectStatus.Cancelled;
                project.Ships.Clear();
                project.Sources.Clear();
                project.ModuleBuilders.Clear();
                project.WorkNumerator = 0;
                structuralFacts = CancelStructuralProject(station, project);
                break;
        }
        station.Revision = checked(station.Revision + 1);
        return new(CommandResult.Accepted(), Changed(station, project).FactProposals.Concat(structuralFacts));
    }

    /// <summary>Releases only the requested assignment after station ownership/revision admission.</summary>
    private GameplayCommandHandlingResult Release(ReleaseStationBuilderCommand command)
    {
        if (!_stations.TryGetValue(command.Station, out StationState? station)
            || !station.Projects.TryGetValue(command.Project, out ProjectState? project)
            || station.Revision != command.ExpectedStationRevision) return Rejected("station-stale-target");
        if (station.Principal != command.RequestingPrincipal) return Rejected("station-wrong-authority");
        if (!project.Ships.Contains(command.BuilderShip)) return Rejected("station-builder-not-assigned");
        project.Ships.Remove(command.BuilderShip);
        project.Sources.Remove(command.BuilderShip);
        station.Revision = checked(station.Revision + 1);
        return Changed(station, project);
    }

    private static GameplayCommandHandlingResult Changed(StationState station, ProjectState project) =>
        new(CommandResult.Accepted(), [new(new(GameFactCommitCategory.EntityLifecycle,
            station.Entity.Value, project.Id.Value, 0), new StationProjectChangedFact(station.Id, project.Id, project.Status))]);

    /// <summary>Accumulates exact time/work under the previously committed eligibility until the next owner barrier.</summary>
    internal void AccrueTo(SimulationTime now)
    {
        if (now < _accruedThrough) throw new InvalidOperationException("Station accrual cannot move backward.");
        ulong elapsed = now.Milliseconds - _accruedThrough.Milliseconds;
        foreach ((StationState station, ProjectState project) in OrderedProjects())
            AccrueProject(station, project, elapsed);
        _accruedThrough = now;
    }

    /// <summary>Releases every departing builder claim before its next work envelope can execute.</summary>
    internal void ReleaseRemovedBuilder(ShipId ship, SimulationTime now)
    {
        foreach ((StationState station, ProjectState project) in OrderedProjects())
            if (project.Ships.Remove(ship))
            {
                project.Sources.Remove(ship);
                station.Revision = checked(station.Revision + 1);
            }
        Reconcile(now);
    }

    /// <summary>Reconciles in stable project admission order and schedules only future work on the shared agenda.</summary>
    internal void Reconcile(SimulationTime now)
    {
        AccrueTo(now);
        foreach ((StationState station, ProjectState project) in OrderedProjects())
        {
            if (project.Kind == StationProjectKind.Dismantling && project.Completed == project.Recipe.RequiredWork
                && project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled or StationProjectStatus.Paused))
            {
                project.Rate = 0;
                if (!RecoveryAvailable(station, project)
                    || project.ModuleId is null && _canRemoveStationTargets?.Invoke(station.Entity) == false)
                {
                    CancelPending(station, project);
                    continue;
                }
                // Work is already complete. A future physical envelope retries only the
                // prepared output/removal join, without requiring another builder or reopening this phase.
                if (project.Pending is null)
                    project.Pending = _agenda.Schedule(now.Add(new(1)), EventPhase.PhysicalCompletion,
                        project.Generation, new GameEvent.StationWork(station.Id, project.Id));
                continue;
            }
            BigInteger rate = 0;
            if (project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled or StationProjectStatus.Paused)
                && NextIncrementAvailable(station, project))
            {
                foreach (ShipId ship in project.Ships.OrderBy(id => id.Value))
                    if (_builder(ship, project.Sources[ship]) is { IsEligible: true } builder && builder.Principal == station.Principal
                        && InRange(builder, station.Position)) rate += builder.Capability.Throughput.UnitsPerSecond;
                rate += ModuleBuilderRate(station, project);
            }
            // Already-due earned work retains its envelope and cause through earlier foreign physical effects.
            if (project.Pending is { } due && due.Timestamp == now) continue;
            SimulationTime? wake = rate.IsZero ? null : WorkDueAt(station, project, now, rate);
            if (project.Rate == rate && project.Pending is { } pending && pending.Timestamp == wake) continue;
            CancelPending(station, project);
            project.Rate = rate;
            if (rate.IsZero)
            {
                project.WorkNumerator %= 1_000;
                if (project.Status is StationProjectStatus.Working) project.Status = StationProjectStatus.Waiting;
                continue;
            }
            project.Pending = _agenda.Schedule(wake!.Value, EventPhase.PhysicalCompletion,
                project.Generation, new GameEvent.StationWork(station.Id, project.Id));
            project.Status = StationProjectStatus.Working;
        }
    }

    /// <summary>Commits one material increment with progress, after revalidating the exact scheduled project generation.</summary>
    internal (ScheduledEventDisposition Disposition, IReadOnlyList<GameFactProposal> Facts) ApplyWork(ScheduledEvent<GameEvent> scheduled,
        StationConstructionProgress? prepared = null)
    {
        var work = (GameEvent.StationWork)scheduled.Payload;
        if (!_stations.TryGetValue(work.Station, out StationState? station)
            || !station.Projects.TryGetValue(work.Project, out ProjectState? project)) return (ScheduledEventDisposition.IgnoredMissingReference, []);
        if (project.Generation != scheduled.Generation || project.Pending != scheduled.Key)
            return (ScheduledEventDisposition.IgnoredStaleGeneration, []);
        project.Pending = null;
        ulong supplied = (ulong)BigInteger.Min(ulong.MaxValue, project.WorkNumerator / 1_000);
        StationConstructionProgress proposed = prepared ?? StationConstructionProgress.Evaluate(project.Recipe, project.Completed,
            new(supplied), Available(station, project));
        foreach ((MaterialId material, Quantity amount) in proposed.ConsumedInputs)
        {
            if (station.Inventory.Available(material) < amount) throw new InvalidOperationException("Prepared station input budget changed during serial commit.");
            station.Inventory.RemoveAvailable(material, amount);
        }
        foreach ((MaterialId material, Quantity amount) in proposed.ConsumedInputs)
            project.Consumed[material] = project.Consumed.GetValueOrDefault(material, Quantity.Zero).Add(amount);
        ulong progressed = proposed.CompletedWork.Units - project.Completed.Units;
        project.Completed = proposed.CompletedWork;
        project.WorkNumerator %= 1_000;
        station.Revision = checked(station.Revision + progressed);
        if (progressed == 0 && project.Kind == StationProjectKind.Dismantling && project.Completed == project.Recipe.RequiredWork
            && RecoveryAvailable(station, project) && (project.ModuleId is not null || _canRemoveStationTargets?.Invoke(station.Entity) != false))
            station.Revision = checked(station.Revision + 1);
        var facts = new List<GameFactProposal>();
        if (project.Completed == project.Recipe.RequiredWork)
        {
            if (project.Kind != StationProjectKind.BaseConstruction)
            {
                facts.AddRange(CompleteStructuralProject(station, project, new ScheduledEventFactCause(scheduled.Key)));
                return (ScheduledEventDisposition.Applied, facts);
            }
            PreparedInventoryCapacityChange capacity = station.Inventory.PrepareCapacityChange(station.Definition.BaseCapacity,
                station.Definition.BaseCapacity) ?? throw new InvalidOperationException("Base completion lost its protected storage invariant.");
            if (!station.Inventory.ApplyCapacityChange(capacity)) throw new InvalidOperationException("Prepared base capacity changed during commit.");
            project.Status = StationProjectStatus.Completed;
            project.Ships.Clear();
            project.Sources.Clear();
            project.Rate = 0;
            station.Phase = StationPhase.Operational;
            facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, project.Id.Value, 0),
                new StationProjectCompletedFact(station.Id, project.Id)));
        }
        return (ScheduledEventDisposition.Applied, facts);
    }

    /// <summary>Checks current unreserved stock for the next consumable increment without retaining input claims.</summary>
    private static bool NextIncrementAvailable(StationState station, ProjectState project) =>
        StationConstructionProgress.Evaluate(project.Recipe, project.Completed, new(1), Available(station, project)).CompletedWork != project.Completed;

    private static Dictionary<MaterialId, Quantity> Available(StationState station, ProjectState project) =>
        project.Recipe.Inputs.Keys.ToDictionary(material => material, material => station.Inventory.Available(material));

    private IEnumerable<(StationState Station, ProjectState Project)> OrderedProjects() => _stations.Values
        .SelectMany(station => station.Projects.Values.Select(project => (Station: station, Project: project)))
        .OrderBy(pair => pair.Project.Id.Value);

    /// <summary>Uses shared exact geometry, keeping station placement separate from ship sensor and pickup ranges.</summary>
    private static bool InRange(StationBuilderState builder, SystemPosition position) =>
        new StationPlacementRegion(position, builder.Capability.RangeUnits).Contains(builder.Position);

    /// <summary>Cancels the exact owned envelope before changing supply; stale events cannot regain authority.</summary>
    private void CancelPending(StationState station, ProjectState project)
    {
        if (project.Pending is not { } key) return;
        EventGeneration next = project.Generation.Next();
        if (!_agenda.TryCancelExact(key, project.Generation, new GameEvent.StationWork(station.Id, project.Id)))
            throw new InvalidOperationException("Station work lost its owned agenda entry.");
        project.Pending = null;
        project.Generation = next;
    }

    private static GameplayCommandHandlingResult Rejected(string code) => new(CommandResult.Rejected(new(code), code));

    private sealed class StationState(StationId id, EntityId entity, Inventory inventory, PrincipalId principal,
        StationDefinition definition, SystemPosition position)
    {
        internal StationId Id { get; } = id;
        internal EntityId Entity { get; } = entity;
        internal Inventory Inventory { get; } = inventory;
        internal PrincipalId Principal { get; } = principal;
        internal StationDefinition Definition { get; } = definition;
        internal SystemPosition Position { get; } = position;
        internal StationPhase Phase { get; set; } = StationPhase.Site;
        internal ulong Revision { get; set; }
        internal SimulationTime CreatedAt { get; set; }
        internal StationStorageLossWindow LossWindow { get; set; } = StationStorageLossWindow.Empty;
        internal ulong LossOccurrence { get; set; }
        internal SortedDictionary<StationProjectId, ProjectState> Projects { get; } = new(EntityIdComparer<StationProjectId>.Instance);
        internal SortedDictionary<StationModuleId, ModuleState> Modules { get; } = new(EntityIdComparer<StationModuleId>.Instance);
    }

    private sealed class ProjectState(StationProjectId id, ConstructionRecipe recipe)
    {
        internal StationProjectId Id { get; } = id;
        internal ConstructionRecipe Recipe { get; } = recipe;
        internal StationProjectStatus Status { get; set; } = StationProjectStatus.Waiting;
        internal Work Completed { get; set; }
        internal BigInteger WorkNumerator { get; set; }
        internal BigInteger Rate { get; set; }
        internal EventGeneration Generation { get; set; }
        internal EventKey? Pending { get; set; }
        internal HashSet<ShipId> Ships { get; } = [];
        internal Dictionary<ShipId, CommandSource> Sources { get; } = [];
        internal SortedDictionary<MaterialId, Quantity> Consumed { get; } = new(EntityIdComparer<MaterialId>.Instance);
        internal StationProjectKind Kind { get; set; }
        internal StationModuleId? ModuleId { get; set; }
        internal QualifiedContentKey? ModuleDefinitionKey { get; set; }
        internal FacilityId? ProviderFacilityId { get; set; }
        internal QualifiedContentKey? UpgradeTarget { get; set; }
        internal HashSet<StationModuleId> ModuleBuilders { get; } = [];
        internal InventoryId? RecoveryInventory { get; set; }
        internal ReservationId? RecoveryReservation { get; set; }
        internal SortedDictionary<MaterialId, Quantity> RecoveryOutputs { get; } = new(EntityIdComparer<MaterialId>.Instance);
        internal bool DismantleAddition { get; set; }
    }

    private sealed class ModuleState(StationModuleId id, StationModuleDefinition definition, StationProjectId? originProject)
    {
        internal StationModuleId Id { get; } = id;
        internal FacilityId? FacilityId { get; set; }
        internal StationModuleDefinition Definition { get; set; } = definition;
        internal StationModulePhase Phase { get; set; } = StationModulePhase.UnderConstruction;
        internal bool Enabled { get; set; } = true;
        internal bool IsCompleted { get; set; }
        internal ulong OccupiedCapacity { get; set; } = definition.CapacityCost;
        internal StationProjectId? OriginProject { get; } = originProject;
        internal StationProjectId? ActiveProject { get; set; }
        internal QualifiedContentKey? UnfinishedUpgrade { get; set; }
        internal StationProjectId? UnfinishedUpgradeProject { get; set; }
    }
}
