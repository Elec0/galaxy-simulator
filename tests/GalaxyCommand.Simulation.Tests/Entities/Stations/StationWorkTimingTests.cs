using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationWorkTimingTests
{
    private static readonly MaterialId Material = new(1);
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.station-timing", "station", "base");
    private static readonly QualifiedContentKey OlderKey = QualifiedContentKey.Create("test.station-timing", "station-module", "older");
    private static readonly QualifiedContentKey NewerKey = QualifiedContentKey.Create("test.station-timing", "station-module", "newer");
    private static readonly PhysicalDefinition Physical = new(QualifiedContentKey.Create("test.station-timing", "material", "metal"),
        PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void SparseInputsPreserveExactIntermediateProgressAndStarvationFractionWithoutPerWorkUnitEvents()
    {
        using GameSession session = Create(1000, 2, 3);
        CreateSite(session);
        Assign(session, Project(session).Id, new(1));
        Store(session, 1, 1);
        session.AdvanceTo(new(123456));
        Assert.Equal(new Work(370), Project(session).CompletedWork);
        Assert.Equal(new Quantity(1), Project(session).ConsumedInputs[Material]);
        Assert.Equal(368, SavedProject(session).WorkNumerator);
        Assert.Single(session.EventRecords, item => item.Kind is GameEventKind.StationWork);
        session.AdvanceTo(new(200000));
        Assert.Equal(new Work(500), Project(session).CompletedWork);
        Assert.Equal(1, SavedProject(session).WorkNumerator);
        Assert.Equal(2, session.EventRecords.Count(item => item.Kind is GameEventKind.StationWork));
        Store(session, 1, 2);
        session.AdvanceTo(new(200332));
        Assert.Equal(new Work(500), Project(session).CompletedWork);
        session.AdvanceTo(new(200333));
        Assert.Equal(new Work(501), Project(session).CompletedWork);
        session.AdvanceTo(new(366667));
        Assert.Equal(StationPhase.Operational, Station(session).Phase);
        Assert.Equal(new Work(1000), Project(session).CompletedWork);
        Assert.Equal(4, session.EventRecords.Count(item => item.Kind is GameEventKind.StationWork));
    }

    [Fact]
    public void IrregularAdvancePartitionsRetainExactPrivateProgressRevisionsAndMaterialGatesAcrossRestore()
    {
        using GameSession whole = Create(1000, 3, 7);
        using GameSession partitioned = Create(1000, 3, 7);
        foreach (GameSession session in new[] { whole, partitioned })
        {
            CreateSite(session);
            Assign(session, Project(session).Id, new(1));
            Assign(session, Project(session).Id, new(2));
            Store(session, 1, 1);
        }
        whole.AdvanceTo(new(50000));
        foreach (ulong timestamp in new ulong[] { 1, 71, 72, 143, 1001, 13007, 23786, 31001, 49999, 50000 })
            partitioned.AdvanceTo(new(timestamp));
        Same(whole, partitioned);
        Store(whole, 1, 2); Store(partitioned, 1, 2);
        whole.AdvanceTo(new(100000));
        foreach (ulong timestamp in new ulong[] { 50001, 50072, 61011, 73786, 99991, 100000 }) partitioned.AdvanceTo(new(timestamp));
        Same(whole, partitioned);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(partitioned.CaptureCheckpoint().Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        Store(whole, 1, 3); Store(partitioned, 1, 3); Store(restored, 1, 3);
        whole.AdvanceTo(new(123857));
        foreach (ulong timestamp in new ulong[] { 100071, 101111, 111019, 123856, 123857 })
        { partitioned.AdvanceTo(new(timestamp)); restored.AdvanceTo(new(timestamp)); }
        Same(whole, partitioned); Same(whole, restored);
        Assert.Equal(new Work(1000), Project(whole).CompletedWork);
        Assert.Equal(6, whole.EventRecords.Count(item => item.Kind is GameEventKind.StationWork));
    }

    [Fact]
    public void SameTimestampMaterialContentionUsesProjectAdmissionEvenAfterOlderWakeIsRescheduled()
    {
        using GameSession session = Create(1, 0, 1);
        CreateSite(session); Assign(session, Project(session).Id, new(1)); session.AdvanceTo(new(1000));
        Accept(session, new ConstructStationModuleCommand(GameSessionTestFixture.Principal, Station(session).Id, Station(session).Revision, OlderKey));
        StationProjectId older = Station(session).Projects.Single(project => project.Kind == StationProjectKind.ModuleConstruction).Id;
        Assign(session, older, new(1)); Store(session, 2, 1); session.AdvanceTo(new(2000));
        Accept(session, new ConstructStationModuleCommand(GameSessionTestFixture.Principal, Station(session).Id, Station(session).Revision, NewerKey));
        StationProjectId newer = Station(session).Projects[^1].Id;
        Assign(session, newer, new(2));
        Accept(session, new ControlStationProjectCommand(GameSessionTestFixture.Principal, Station(session).Id, older,
            Station(session).Revision, StationProjectAction.Pause));
        Accept(session, new ControlStationProjectCommand(GameSessionTestFixture.Principal, Station(session).Id, older,
            Station(session).Revision, StationProjectAction.Resume));
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;
        ScheduledEvent<GameEvent>[] due = before.Engine.Agenda.PendingEvents.Where(item => item.Payload is GameEvent.StationWork).ToArray();
        Assert.Equal(2, due.Length);
        Assert.All(due, item => Assert.Equal(new SimulationTime(3000), item.Key.Timestamp));
        Assert.True(due.Single(item => ((GameEvent.StationWork)item.Payload).Project == newer).Key.CreationSequence
            < due.Single(item => ((GameEvent.StationWork)item.Payload).Project == older).Key.CreationSequence);
        session.AdvanceTo(new(3000));
        Assert.Equal(new Work(2), Station(session).Projects.Single(project => project.Id == older).CompletedWork);
        Assert.Equal(new Work(0), Station(session).Projects.Single(project => project.Id == newer).CompletedWork);
    }

    private static StationSnapshot Station(GameSession session) => Assert.Single(session.CaptureSnapshot().Stations);
    private static StationProjectSnapshot Project(GameSession session) => Assert.Single(Station(session).Projects);
    private static StationProjectCheckpoint SavedProject(GameSession session) => Assert.Single(Assert.Single(session.CaptureCheckpoint().Value!.Stations!.Stations).Projects);
    private static void CreateSite(GameSession session) => Accept(session, new CreateStationSiteCommand(GameSessionTestFixture.Principal,
        new(1), BaseKey, GameSessionTestFixture.Position(0, 0), new(0)));
    private static void Assign(GameSession session, StationProjectId project, ShipId builder) => Accept(session,
        new AssignStationBuilderCommand(GameSessionTestFixture.Principal, Station(session).Id, project, Station(session).Revision, builder, new(0)));
    private static void Accept(GameSession session, GameplayCommand command) => Assert.Equal(CommandResultStatus.Accepted,
        session.SubmitCommand(GameSessionTestFixture.Player, command).Result.Status);
    private static void Store(GameSession session, ulong amount, ulong operation) => session.CommitInventoryMutations([
        new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, operation), Station(session).InventoryId, Physical, new(amount))]);

    private static void Same(GameSession left, GameSession right)
    {
        Assert.Equal(Station(left).Revision, Station(right).Revision);
        Assert.Equal(Project(left).CompletedWork, Project(right).CompletedWork);
        Assert.Equal(Project(left).ConsumedInputs.ToArray(), Project(right).ConsumedInputs.ToArray());
        Assert.Equal(SavedProject(left).WorkNumerator, SavedProject(right).WorkNumerator);
        Assert.Equal(SavedProject(left).Rate, SavedProject(right).Rate);
        Assert.Equal(SavedProject(left).Pending, SavedProject(right).Pending);
        Assert.Equal(SavedProject(left).Generation, SavedProject(right).Generation);
    }

    private static GameSession Create(ulong work, ulong materials, ulong throughput)
    {
        var recipe = new ConstructionRecipe(materials == 0 ? [] : new Dictionary<MaterialId, Quantity> { [Material] = new(materials) }, new(work));
        var older = new StationModuleDefinition(OlderKey, "Older", 1, new(0), 0, null,
            new(new Dictionary<MaterialId, Quantity> { [Material] = new(2) }, new(2)), new(1), [], []);
        var newer = new StationModuleDefinition(NewerKey, "Newer", 1, new(0), 0, null,
            new(new Dictionary<MaterialId, Quantity> { [Material] = new(1) }, new(1)), new(1), [], []);
        var definition = new StationDefinition(BaseKey, "Timing Base", 4, new(materials + 4), new(materials + 4), recipe,
            0, [OlderKey, NewerKey], new(1), []);
        var builder = new ShipDesign(new(1), "Builder", new([], new(1)), new(10), GameSessionTestFixture.ManeuverCapability,
            0, 0, new StationConstructionCapability(0, new(throughput)));
        InitialShipSetup[] ships = [new(new(1), new(1), new(1), GameSessionTestFixture.Principal, builder,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
            new(new(2), new(2), new(2), GameSessionTestFixture.Principal, builder,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)];
        var setup = new GameSessionSetup([new(GameSessionTestFixture.System, "System")], ships, new ConnectorTopology([], []), [], null,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player,
            stations: new(new([definition], [older, newer]), new(new Dictionary<MaterialId, PhysicalDefinition> { [Material] = Physical }),
                StationTerritoryPolicy.Absent));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
