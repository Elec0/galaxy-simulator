using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationRuntimeCheckpointTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly QualifiedContentKey Key = QualifiedContentKey.Create("test.stations", "station", "base");
    private static readonly PhysicalDefinition MetalDefinition = new(QualifiedContentKey.Create("test.stations", "material", "metal"),
        PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void ExactRateFractionAssignmentsAndAgendaSurviveRestoreWithoutAllocation()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        EntityLifecycleOwner lifecycle = RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials);
        EventAgenda<GameEvent> agenda = EventAgenda<GameEvent>.RestoreCheckpoint(state.Agenda.CaptureCheckpoint().Value!).Value!;
        var restored = Restore(checkpoint, lifecycle, agenda, new(100));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(checkpoint, restored.Value!.CaptureCheckpoint(), new CheckpointComparer());
        Assert.Equal(state.Lifecycle.CaptureCheckpoint().EntityIds, lifecycle.CaptureCheckpoint().EntityIds);
        Assert.Equal(state.Agenda.NextCreationSequence, agenda.NextCreationSequence);
    }

    [Fact]
    public void MalformedMaterialProgressRejectedBeforeRegisteringStationIdentity()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        StationStateCheckpoint station = Assert.Single(checkpoint.Stations);
        StationProjectCheckpoint project = Assert.Single(station.Projects);
        var malformed = checkpoint with { Stations = [station with { Projects = [project with { Completed = new(1) }] }] };
        EntityLifecycleOwner lifecycle = RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials);
        Assert.False(Restore(malformed, lifecycle, state.Agenda, new(100)).IsSuccess);
        Assert.Null(lifecycle.Entities.GetStationId(station.Entity));
    }

    [Fact]
    public void PausingAtGenerationExhaustionRejectsBeforeCancellingTheOwnedEnvelope()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        StationStateCheckpoint station = Assert.Single(checkpoint.Stations);
        StationProjectCheckpoint project = Assert.Single(station.Projects);
        checkpoint = checkpoint with { Stations = [station with { Projects = [project with { Generation = new(ulong.MaxValue) }] }] };
        EventAgendaCheckpoint<GameEvent> events = state.Agenda.CaptureCheckpoint().Value!;
        var agenda = EventAgenda<GameEvent>.RestoreCheckpoint(new(events.CurrentTime, events.NextCreationSequence,
            events.PendingEvents.Select(entry => entry.Key == project.Pending ? entry with { Generation = new(ulong.MaxValue) } : entry))).Value!;
        var loaded = Restore(checkpoint, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), agenda, new(100));
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);

        GameplayCommandHandlingResult result = loaded.Value!.Handle(new(new(3), new(100), GameSessionTestFixture.Player,
            new ControlStationProjectCommand(station.Principal, station.Id, project.Id, station.Revision, StationProjectAction.Pause)));

        Assert.Equal(CommandResultStatus.Rejected, result.Result.Status);
        Assert.Equal(new CommandRejectionCode("station-work-generation-exhausted"), result.Result.RejectionCode);
        Assert.Equal(checkpoint, loaded.Value.CaptureCheckpoint(), new CheckpointComparer());
        Assert.Equal(1, agenda.Count);
    }

    [Fact]
    public void InvalidAllocatorMissingAgendaAndDuplicateAssignmentRejectRestore()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        StationStateCheckpoint station = Assert.Single(checkpoint.Stations);
        StationProjectCheckpoint project = Assert.Single(station.Projects);
        Assert.False(Restore(checkpoint with { StationIds = new(1) },
            RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100)).IsSuccess);
        Assert.False(Restore(checkpoint, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials),
            new EventAgenda<GameEvent>(), new(100)).IsSuccess);
        var repeated = checkpoint with { Stations = [station with { Projects = [project with { Assignments = [project.Assignments[0], project.Assignments[0]] }] }] };
        Assert.False(Restore(repeated, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100)).IsSuccess);
    }

    [Fact]
    public void InvalidOwnerSystemAndCachedRateRejectRestore()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        StationStateCheckpoint station = Assert.Single(checkpoint.Stations);
        StationProjectCheckpoint project = Assert.Single(station.Projects);
        Assert.False(StationRuntimeOwner.Restore(checkpoint, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials),
            state.Agenda, Builder, new(100), [new(2)], [GameSessionTestFixture.Principal]).IsSuccess);
        Assert.False(StationRuntimeOwner.Restore(checkpoint, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials),
            state.Agenda, Builder, new(100), [GameSessionTestFixture.System], [new(2)]).IsSuccess);
        Assert.False(Restore(checkpoint with { Stations = [station with { Projects = [project with { Rate = 99 }] }] },
            RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100)).IsSuccess);
    }

    [Fact]
    public void PauseRetainsFractionAndAssignmentAcrossRestore()
    {
        var state = CaptureWorking();
        StationSnapshot station = Assert.Single(state.Owner.Capture());
        StationProjectSnapshot project = Assert.Single(station.Projects);
        Assert.Equal(CommandResultStatus.Accepted, state.Owner.Handle(new(new(3), new(100), GameSessionTestFixture.Player,
            new ControlStationProjectCommand(station.PrincipalId, station.Id, project.Id, station.Revision,
                StationProjectAction.Pause))).Result.Status);
        state.Owner.Reconcile(new(100));
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        var restored = Restore(checkpoint, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        StationProjectCheckpoint retained = Assert.Single(Assert.Single(restored.Value!.CaptureCheckpoint().Stations).Projects);
        Assert.Equal(StationProjectStatus.Paused, retained.Status);
        Assert.Equal(200, retained.WorkNumerator);
        Assert.Single(retained.Assignments);
        Assert.Null(retained.Pending);
    }

    [Fact]
    public void CancelledProjectHistoryAndFreshRestartDoNotMergeProgress()
    {
        var state = CaptureWorking();
        StationSnapshot station = Assert.Single(state.Owner.Capture());
        StationProjectSnapshot project = Assert.Single(station.Projects);
        state.Owner.Handle(new(new(3), new(100), GameSessionTestFixture.Player,
            new ControlStationProjectCommand(station.PrincipalId, station.Id, project.Id, station.Revision,
                StationProjectAction.Cancel)));
        station = Assert.Single(state.Owner.Capture());
        state.Owner.Handle(new(new(4), new(100), GameSessionTestFixture.Player,
            new ControlStationProjectCommand(station.PrincipalId, station.Id, project.Id, station.Revision,
                StationProjectAction.RestartBaseConstruction)));
        state.Owner.Reconcile(new(100));
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        var restored = Restore(checkpoint, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        IReadOnlyList<StationProjectCheckpoint> retained = Assert.Single(restored.Value!.CaptureCheckpoint().Stations).Projects;
        Assert.Equal(2, retained.Count);
        Assert.Equal(StationProjectStatus.Cancelled, retained[0].Status);
        Assert.Equal(StationProjectStatus.Waiting, retained[1].Status);
        Assert.Empty(retained[1].Consumed);
        Assert.Empty(retained[1].Assignments);
    }

    [Fact]
    public void ModuleStateCannotBeSilentlyOmittedOrInventedOnRestore()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        Assert.Equal(new IdSequenceCheckpoint(1), checkpoint.ModuleIds);
        StationStateCheckpoint station = Assert.Single(checkpoint.Stations);
        Assert.Empty(station.Modules);
        var phantom = new StationModuleCheckpoint(new(1), QualifiedContentKey.Create("test.stations", "station-module", "missing"),
            StationModulePhase.Operational, true, true, 1, new StationProjectId(1), null, null, null);
        var malformed = checkpoint with { Stations = [station with { Modules = [phantom] }] };
        Assert.False(Restore(malformed, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100)).IsSuccess);
    }

    [Fact]
    public void LiveUnfinishedModuleRetainsIdentityClaimsAndFractionAcrossRestore()
    {
        var module = new StationModuleDefinition(QualifiedContentKey.Create("test.stations", "station-module", "storage"),
            "Storage", 1, new(5), 20, null, new([], new(2)), new(2), [], []);
        var state = CaptureWorking(module);
        AdvanceDirect(state.Owner, state.Agenda, new(2_500));
        StationSnapshot station = Assert.Single(state.Owner.Capture());
        state.Lifecycle.Inventories.Get(station.InventoryId)!.Add(Metal, new(2));
        state.Owner.Reconcile(new(2_500));
        AdvanceDirect(state.Owner, state.Agenda, new(5_000));
        station = Assert.Single(state.Owner.Capture());
        Assert.Equal(StationPhase.Operational, station.Phase);
        Assert.Equal(CommandResultStatus.Accepted, state.Owner.Handle(new(new(3), new(5_000), GameSessionTestFixture.Player,
            new ConstructStationModuleCommand(station.PrincipalId, station.Id, station.Revision, module.Key))).Result.Status);
        station = Assert.Single(state.Owner.Capture());
        StationProjectSnapshot project = Assert.Single(station.Projects, item => item.Kind == StationProjectKind.ModuleConstruction);
        state.Owner.Handle(new(new(4), new(5_000), GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(station.PrincipalId, station.Id, project.Id, station.Revision, new(1), new(0))));
        state.Owner.Reconcile(new(5_000));
        AdvanceDirect(state.Owner, state.Agenda, new(5_250));
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        EntityLifecycleOwner lifecycle = RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials);
        var restored = Restore(checkpoint, lifecycle, state.Agenda, new(5_250));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        StationModuleCheckpoint savedModule = Assert.Single(Assert.Single(restored.Value!.CaptureCheckpoint().Stations).Modules);
        Assert.Equal(StationModulePhase.UnderConstruction, savedModule.Phase);
        Assert.Equal(1UL, savedModule.OccupiedCapacity);
        Assert.Equal(Assert.Single(station.Modules).Id, savedModule.Id);
        StationProjectCheckpoint savedProject = Assert.Single(Assert.Single(restored.Value.CaptureCheckpoint().Stations).Projects,
            item => item.Kind == StationProjectKind.ModuleConstruction);
        Assert.Equal(500, savedProject.WorkNumerator);
        Assert.Equal(project.Id, savedModule.OriginProject);
    }

    private static void AdvanceDirect(StationRuntimeOwner owner, EventAgenda<GameEvent> agenda, SimulationTime through)
    {
        while (agenda.PopNextThrough(through) is { } work)
        {
            owner.AccrueTo(work.Key.Timestamp);
            owner.ApplyWork(work);
            agenda.EnterPhase(EventPhase.Decision);
            owner.Reconcile(work.Key.Timestamp);
            agenda.CompleteTimestamp();
        }
        owner.AccrueTo(through);
    }

    [Fact]
    public void UnreceiptedProjectCannotEnterRestoredHistory()
    {
        var state = CaptureWorking();
        StationRuntimeCheckpoint checkpoint = state.Owner.CaptureCheckpoint();
        StationStateCheckpoint station = Assert.Single(checkpoint.Stations);
        StationProjectCheckpoint original = Assert.Single(station.Projects);
        StationProjectCheckpoint invented = original with
        {
            Id = new(2), Status = StationProjectStatus.Cancelled, Rate = 0, WorkNumerator = 0, Pending = null, Assignments = [],
        };
        var malformed = checkpoint with { ProjectIds = new(3), Stations = [station with { Projects = [original, invented] }] };
        Assert.False(Restore(malformed, RestoreLifecycle(state.Lifecycle, checkpoint.Setup.Materials), state.Agenda, new(100)).IsSuccess);
    }

    private static CheckpointResult<StationRuntimeOwner> Restore(StationRuntimeCheckpoint checkpoint,
        EntityLifecycleOwner lifecycle, EventAgenda<GameEvent> agenda, SimulationTime now) =>
        StationRuntimeOwner.Restore(checkpoint, lifecycle, agenda, Builder, now,
            [GameSessionTestFixture.System], [GameSessionTestFixture.Principal]);

    private static (StationRuntimeOwner Owner, EntityLifecycleOwner Lifecycle, EventAgenda<GameEvent> Agenda) CaptureWorking(StationModuleDefinition? module = null)
    {
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var definition = new StationDefinition(Key, "Base", 1, new(2), new(10),
            new(new Dictionary<MaterialId, Quantity> { [Metal] = new(4) }, new(10)), 10,
            module is null ? [] : [module.Key], new(1), []);
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), [], materials);
        var agenda = new EventAgenda<GameEvent>();
        var setup = new StationRuntimeSetup(new([definition], module is null ? [] : [module]), materials, StationTerritoryPolicy.Absent)
            .BindPlayer(GameSessionTestFixture.Player, GameSessionTestFixture.Principal);
        var owner = new StationRuntimeOwner(setup, lifecycle, agenda, Builder);
        owner.Handle(new(new(1), new(0), GameSessionTestFixture.Player,
            new CreateStationSiteCommand(GameSessionTestFixture.Principal, new(1), Key, GameSessionTestFixture.Position(0, 0), new(0))));
        StationSnapshot station = Assert.Single(owner.Capture());
        lifecycle.Inventories.Get(station.InventoryId)!.Add(Metal, new(2));
        owner.Handle(new(new(2), new(0), GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, Assert.Single(station.Projects).Id,
                station.Revision, new(1), new(0))));
        owner.Reconcile(new(0));
        owner.AccrueTo(new(100));
        agenda.AdvanceTo(new(100));
        return (owner, lifecycle, agenda);
    }

    private static StationBuilderState? Builder(ShipId ship, CommandSource source) => ship.Value == 1
        ? new(GameSessionTestFixture.Principal, new(100, new(2)), GameSessionTestFixture.Position(0, 0), true) : null;

    private static EntityLifecycleOwner RestoreLifecycle(EntityLifecycleOwner lifecycle, MaterialInventoryCompatibilityMap materials) =>
        EntityLifecycleOwner.RestoreCheckpoint(lifecycle.CaptureCheckpoint(), new SpatialMovement(), new ActorControlRegistry(),
            new ShipOrderCoordinator(), [], new PhysicalDefinitionCatalog([MetalDefinition]), materials).Value!;

    private sealed class CheckpointComparer : IEqualityComparer<StationRuntimeCheckpoint>
    {
        public bool Equals(StationRuntimeCheckpoint? left, StationRuntimeCheckpoint? right) => left is not null && right is not null
            && left.StationIds == right.StationIds && left.ProjectIds == right.ProjectIds && left.AccruedThrough == right.AccruedThrough
            && left.Stations.SelectMany(station => station.Projects).Select(project => (project.Completed, project.Rate, project.WorkNumerator, project.Pending))
                .SequenceEqual(right.Stations.SelectMany(station => station.Projects).Select(project => (project.Completed, project.Rate, project.WorkNumerator, project.Pending)))
            && left.Stations.SelectMany(station => station.Projects).SelectMany(project => project.Assignments)
                .SequenceEqual(right.Stations.SelectMany(station => station.Projects).SelectMany(project => project.Assignments));
        public int GetHashCode(StationRuntimeCheckpoint value) => value.StationIds.GetHashCode();
    }
}
