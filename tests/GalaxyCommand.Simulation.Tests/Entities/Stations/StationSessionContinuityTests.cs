using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationSessionContinuityTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.stations", "station", "continuity");
    private static readonly PhysicalDefinition MetalDefinition = new(
        QualifiedContentKey.Create("test.stations", "material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void FractionalWorkAndConsumptionAreIdenticalAcrossAdvancePartitions()
    {
        using GameSession whole = Create(work: 11, material: 7, throughput: 3);
        using GameSession partitioned = Create(work: 11, material: 7, throughput: 3);
        Start(whole, 7);
        Start(partitioned, 7);

        whole.AdvanceTo(new(3_500));
        foreach (ulong timestamp in new ulong[] { 1, 17, 166, 333, 334, 512, 999, 1_001, 1_700, 2_307, 3_500 })
            partitioned.AdvanceTo(new(timestamp));

        Assert.Equal(new Work(10), Project(whole).CompletedWork);
        AssertSameConstruction(whole, partitioned);
        whole.AdvanceTo(new(3_667));
        partitioned.AdvanceTo(new(3_667));
        AssertSameConstruction(whole, partitioned);
        Assert.Equal(StationPhase.Operational, Station(whole).Phase);
        Assert.Equal(new Quantity(7), Project(whole).ConsumedInputs[Metal]);
    }

    [Fact]
    public void MultipleAssignedBuildersCombineThroughputWithoutDuplicatingMaterials()
    {
        using GameSession session = Create(work: 10, material: 4, throughput: 2, shipCount: 2);
        Start(session, 4);
        Assign(session, new(2));

        session.AdvanceTo(new(2_499));
        Assert.Equal(new Work(9), Project(session).CompletedWork);
        session.AdvanceTo(new(2_500));

        Assert.Equal(new Work(10), Project(session).CompletedWork);
        Assert.Equal(new Quantity(4), Project(session).ConsumedInputs[Metal]);
        Assert.Empty(Project(session).AssignedShips);
        Assert.Equal(StationPhase.Operational, Station(session).Phase);
        Assert.Single(session.ReadFactsAfter(null, 256).Facts, fact => fact.Fact is StationProjectCompletedFact);
    }

    [Fact]
    public void LeavingAndReturningPreservesFractionalWorkWithoutWorkingDuringTravel()
    {
        using GameSession session = Create(work: 11, material: 7, throughput: 3);
        Start(session, 7);
        session.AdvanceTo(new(250));
        Move(session, 1_000);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 5_000));
        Assert.Equal(new Work(0), Project(session).CompletedWork);
        Assert.Equal(new[] { new ShipId(1) }, Project(session).AssignedShips);

        Move(session, 0);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
        ulong returnedAt = session.CurrentTime.Milliseconds;
        Assert.Equal(new Work(0), Project(session).CompletedWork);
        session.AdvanceTo(new(returnedAt + 83));
        Assert.Equal(new Work(0), Project(session).CompletedWork);
        session.AdvanceTo(new(returnedAt + 84));
        Assert.Equal(new Work(1), Project(session).CompletedWork);
    }

    [Fact]
    public void ScriptedControlPausesOriginalBuilderUntilItsControllerReturns()
    {
        using GameSession session = Create(work: 11, material: 7, throughput: 3);
        Start(session, 7);
        session.AdvanceTo(new(250));
        var script = new CommandSource(CommandSourceKind.Script, new("station-continuity-script"));
        AssertAccepted(session.SubmitCommand(script,
            new BeginScriptedOverrideCommand(new(1), new("station-test"), new(0))));
        session.AdvanceTo(new(1_250));
        Assert.Equal(new Work(0), Project(session).CompletedWork);

        AssertAccepted(session.SubmitCommand(script,
            new EndScriptedOverrideCommand(new(1), ScriptedOverrideReleasePolicy.CancelOutstanding, new(1))));
        session.AdvanceTo(new(1_334));
        Assert.Equal(new Work(1), Project(session).CompletedWork);
    }

    [Fact]
    public void RejectedCreationAndAssignmentAllocateNoStationIdentityOrBuilderClaim()
    {
        using GameSession session = Create(work: 10, material: 4, throughput: 2);
        AssertRejected(session.SubmitCommand(GameSessionTestFixture.Player,
            CreateSite() with { Position = GameSessionTestFixture.Position(101, 0) }));
        AssertRejected(session.SubmitCommand(GameSessionTestFixture.Player,
            CreateSite() with { RequestingPrincipal = new(99) }));
        AssertRejected(session.SubmitCommand(GameSessionTestFixture.Player,
            CreateSite() with { DefinitionKey = QualifiedContentKey.Create("test.stations", "station", "missing") }));
        Assert.Empty(session.CaptureSnapshot().Stations);

        AssertAccepted(session.SubmitCommand(GameSessionTestFixture.Player, CreateSite()));
        StationSnapshot station = Station(session);
        Assert.Equal(new StationId(1), station.Id);
        Assert.Equal(new EntityId(2), station.EntityId);
        Assert.Equal(new InventoryId(2), station.InventoryId);
        Assert.Equal(new StationProjectId(1), Project(session).Id);
        AssertRejected(session.SubmitCommand(GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, Project(session).Id,
                99, new(1), new(0))));
        Assert.Empty(Project(session).AssignedShips);
        Assert.Equal(station.Revision, Station(session).Revision);
        Assign(session, new(1));
        Assert.Equal(new[] { new ShipId(1) }, Project(session).AssignedShips);
    }

    [Fact]
    public void RemovedBuilderReleasesItsAssignmentAndCannotSupplyFurtherWork()
    {
        using GameSession session = Create(work: 11, material: 7, throughput: 3);
        Start(session, 7);
        session.AdvanceTo(new(250));
        Assert.IsType<EntityRemovalResult.Removed>(session.RemoveEntity(
            new(new EntityId(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo)));

        Assert.Empty(Project(session).AssignedShips);
        session.AdvanceTo(new(5_000));
        Assert.Equal(new Work(0), Project(session).CompletedWork);
        Assert.Equal(StationPhase.Site, Station(session).Phase);
    }

    [Fact]
    public void UnboundPlayerSourceCannotControlClaimedPrincipalStation()
    {
        using GameSession session = Create(work: 11, material: 7, throughput: 3);
        Start(session, 7);
        var impostor = new CommandSource(CommandSourceKind.Player, new("unbound-player"));
        StationSnapshot station = Station(session);
        AssertRejected(session.SubmitCommand(impostor, new ControlStationProjectCommand(
            GameSessionTestFixture.Principal, station.Id, Project(session).Id, station.Revision,
            StationProjectAction.Pause)));

        Assert.Equal(station.Revision, Station(session).Revision);
        session.AdvanceTo(new(1_000));
        Assert.Equal(new Work(3), Project(session).CompletedWork);
    }

    private static void AssertSameConstruction(GameSession first, GameSession second)
    {
        Assert.Equal(Station(first).Phase, Station(second).Phase);
        Assert.Equal(Station(first).Revision, Station(second).Revision);
        Assert.Equal(Project(first).Status, Project(second).Status);
        Assert.Equal(Project(first).CompletedWork, Project(second).CompletedWork);
        Assert.Equal(Project(first).ConsumedInputs.ToArray(), Project(second).ConsumedInputs.ToArray());
        Assert.Equal(Project(first).AssignedShips, Project(second).AssignedShips);
    }

    private static void Start(GameSession session, ulong material)
    {
        AssertAccepted(session.SubmitCommand(GameSessionTestFixture.Player, CreateSite()));
        Assign(session, new(1));
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(
            new(InventoryOperationSourceKind.Explicit, 1), Station(session).InventoryId, MetalDefinition, new(material))]);
    }

    private static void Assign(GameSession session, ShipId builder)
    {
        StationSnapshot station = Station(session);
        AssertAccepted(session.SubmitCommand(GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id,
                Project(session).Id, station.Revision, builder, new(0))));
    }

    private static void Move(GameSession session, long x) => AssertAccepted(session.SubmitCommand(
        GameSessionTestFixture.Player, new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)));

    private static StationSnapshot Station(GameSession session) => Assert.Single(session.CaptureSnapshot().Stations);
    private static StationProjectSnapshot Project(GameSession session) => Assert.Single(Station(session).Projects);
    private static void AssertAccepted(GameplayCommandRecord record) => Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
    private static void AssertRejected(GameplayCommandRecord record) => Assert.Equal(CommandResultStatus.Rejected, record.Result.Status);
    private static CreateStationSiteCommand CreateSite() => new(GameSessionTestFixture.Principal,
        new(1), BaseKey, GameSessionTestFixture.Position(0, 0), new(0));

    private static GameSession Create(ulong work, ulong material, ulong throughput, int shipCount = 1)
    {
        var recipe = new ConstructionRecipe(new Dictionary<MaterialId, Quantity> { [Metal] = new(material) }, new(work));
        var definition = new StationDefinition(BaseKey, "Continuity Station", 4, new(material), new(material), recipe,
            123, [], new(5), new Dictionary<MaterialId, Quantity> { [Metal] = new(1) });
        var materials = new MaterialInventoryCompatibilityMap(
            new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var ship = new ShipDesign(new(1), "Builder", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, 10_000, 5_000, new StationConstructionCapability(100, new(throughput)));
        InitialShipSetup[] ships = Enumerable.Range(1, shipCount).Select(index => new InitialShipSetup(
            new((ulong)index), new((ulong)index), new((ulong)index), GameSessionTestFixture.Principal, ship,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)).ToArray();
        var setup = new GameSessionSetup([new StarSystem(GameSessionTestFixture.System, "System")], ships,
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player,
            stations: new StationRuntimeSetup(new([definition], []), materials, StationTerritoryPolicy.Absent));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
