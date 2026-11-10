using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationSessionTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.stations", "station", "base");
    private static readonly PhysicalDefinition MetalDefinition = new(
        QualifiedContentKey.Create("test.stations", "material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void SiteCreationPublishesOneEmptyInventoryAndInactiveBaseProject()
    {
        using GameSession session = Create();
        GameplayCommandRecord result = session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());

        Assert.Equal(CommandResultStatus.Accepted, result.Result.Status);
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(StationPhase.Site, station.Phase);
        Assert.Equal(new EntityId(2), station.EntityId);
        Assert.Equal(new InventoryId(2), station.InventoryId);
        Assert.Equal(0UL, station.SensorRadiusUnits);
        Assert.Equal(new Work(0), Assert.Single(station.Projects).CompletedWork);
        Assert.Empty(Assert.Single(station.Projects).AssignedShips);
        Assert.Contains(session.ReadFactsAfter(null, 100).Facts,
            fact => fact.Fact is StationCreatedFact);
    }

    [Fact]
    public void LiveStationIsAnExplicitStationaryNavigationTarget()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite() with { Position = GameSessionTestFixture.Position(80, 0) });
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        GameplayCommandRecord move = session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship, new NavigationDestination.Entity(site.EntityId), OrderPlacement.ReplaceAll));

        Assert.Equal(CommandResultStatus.Accepted, move.Result.Status);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
        Assert.Equal(site.Position, Assert.Single(session.CaptureSnapshot().Ships).Position);
    }

    [Fact]
    public void WrongControlRevisionRejectsBeforeAnyIdentityAllocation()
    {
        using GameSession session = Create();
        GameplayCommandRecord rejected = session.SubmitCommand(GameSessionTestFixture.Player,
            CreateSite() with { ExpectedControlRevision = new(1) });
        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Empty(session.CaptureSnapshot().Stations);

        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        Assert.Equal(new EntityId(2), Assert.Single(session.CaptureSnapshot().Stations).EntityId);
    }

    [Fact]
    public void InvalidSitePositionRejectsBeforeAnyIdentityAllocation()
    {
        using GameSession session = Create();
        GameplayCommandRecord rejected = session.SubmitCommand(GameSessionTestFixture.Player,
            CreateSite() with { Position = default });

        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Empty(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        Assert.Equal(new EntityId(2), Assert.Single(session.CaptureSnapshot().Stations).EntityId);
    }

    [Fact]
    public void BuilderConsumesPartialDeliveriesAndCompletesWithoutReplacingIdentity()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectSnapshot project = Assert.Single(site.Projects);
        GameplayCommandRecord assigned = session.SubmitCommand(GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(GameSessionTestFixture.Principal, site.Id, project.Id,
                site.Revision, GameSessionTestFixture.Ship, new(0)));
        Assert.Equal(CommandResultStatus.Accepted, assigned.Result.Status);
        Store(session, site.InventoryId, 2, 1);
        session.AdvanceTo(new(5_000));
        StationSnapshot partial = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(new Work(5), Assert.Single(partial.Projects).CompletedWork);
        Assert.Equal(StationPhase.Site, partial.Phase);

        Store(session, site.InventoryId, 2, 2);
        session.AdvanceTo(new(10_000));
        StationSnapshot complete = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(site.Id, complete.Id);
        Assert.Equal(site.EntityId, complete.EntityId);
        Assert.Equal(site.InventoryId, complete.InventoryId);
        Assert.Equal(StationPhase.Operational, complete.Phase);
        Assert.Equal(123UL, complete.SensorRadiusUnits);
        Assert.Equal(new Work(10), Assert.Single(complete.Projects).CompletedWork);
        Assert.Empty(Assert.Single(complete.Projects).AssignedShips);
    }

    [Fact]
    public void PauseRetainsAssignmentAndCancelRestartRequiresFreshInputs()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectSnapshot project = Assert.Single(site.Projects);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, project.Id, site.Revision, GameSessionTestFixture.Ship, new(0)));
        Store(session, site.InventoryId, 2, 1);
        session.AdvanceTo(new(1_000));

        Control(StationProjectAction.Pause);
        session.AdvanceTo(new(2_000));
        StationProjectSnapshot paused = Assert.Single(session.CaptureSnapshot().Stations).Projects.Single();
        Assert.Equal(new Work(2), paused.CompletedWork);
        Assert.Single(paused.AssignedShips);

        Control(StationProjectAction.Cancel);
        StationSnapshot cancelled = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(StationPhase.Site, cancelled.Phase);
        Assert.Empty(Assert.Single(cancelled.Projects).AssignedShips);
        Control(StationProjectAction.RestartBaseConstruction);
        StationSnapshot restarted = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(site.InventoryId, restarted.InventoryId);
        Assert.Equal(2, restarted.Projects.Count);
        StationProjectSnapshot fresh = restarted.Projects.Single(item => item.Status != StationProjectStatus.Cancelled);
        Assert.Equal(new Work(0), fresh.CompletedWork);
        Assert.Empty(fresh.ConsumedInputs);

        void Control(StationProjectAction action)
        {
            StationSnapshot current = Assert.Single(session.CaptureSnapshot().Stations);
            GameplayCommandRecord result = session.SubmitCommand(GameSessionTestFixture.Player,
                new ControlStationProjectCommand(GameSessionTestFixture.Principal, site.Id, project.Id, current.Revision, action));
            Assert.Equal(CommandResultStatus.Accepted, result.Result.Status);
        }
    }

    [Fact]
    public void SessionCheckpointRetainsLiveSiteAndExactBuilderContinuation()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectSnapshot project = Assert.Single(site.Projects);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, project.Id, site.Revision, GameSessionTestFixture.Ship, new(0)));
        Store(session, site.InventoryId, 2, 1);
        session.AdvanceTo(new(333));

        CheckpointResult<GameSessionCheckpoint> captured = session.CaptureCheckpoint();
        Assert.True(captured.IsSuccess, captured.Failure?.Message);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(captured.Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        StationSnapshot retained = Assert.Single(restored.CaptureSnapshot().Stations);
        Assert.Equal(site.EntityId, retained.EntityId);
        Assert.Single(Assert.Single(retained.Projects).AssignedShips);

        session.AdvanceTo(new(2_500));
        restored.AdvanceTo(new(2_500));
        Assert.Equal(Assert.Single(session.CaptureSnapshot().Stations).Projects.Single().CompletedWork,
            Assert.Single(restored.CaptureSnapshot().Stations).Projects.Single().CompletedWork);
        Store(restored, site.InventoryId, 2, 2);
        restored.AdvanceTo(new(5_000));
        Assert.Equal(StationPhase.Operational, Assert.Single(restored.CaptureSnapshot().Stations).Phase);
    }

    [Fact]
    public void SessionRestoreRejectsStationReceiptOutsideCommandAdmission()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, site.Projects.Single().Id, site.Revision,
            GameSessionTestFixture.Ship, new(0)));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var malformed = checkpoint with { CommandAdmission = new(new(2), checkpoint.CommandAdmission.LastSubmittedAt) };

        Assert.False(GameSession.RestoreCheckpoint(malformed).IsSuccess);
    }

    [Fact]
    public void SessionRestoreCannotSilentlyOmitAnIdleStationOwner()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;

        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { Stations = null }).IsSuccess);
    }

    [Fact]
    public void FinalBaseDismantlingDeliversRecoveryAndRemovesIdentityTogether()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, site.Projects.Single().Id, site.Revision, GameSessionTestFixture.Ship, new(0)));
        Store(session, site.InventoryId, 2, 1);
        session.AdvanceTo(new(2_500));
        Store(session, site.InventoryId, 2, 2);
        session.AdvanceTo(new(5_000));
        StationSnapshot complete = Assert.Single(session.CaptureSnapshot().Stations);
        GameplayCommandRecord dismantle = session.SubmitCommand(GameSessionTestFixture.Player,
            new DismantleStationBaseCommand(GameSessionTestFixture.Principal, complete.Id, complete.Revision, new(1)));
        Assert.Equal(CommandResultStatus.Accepted, dismantle.Result.Status);
        StationSnapshot dismantling = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectSnapshot project = dismantling.Projects.Single(item => item.Kind == StationProjectKind.Dismantling);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, project.Id, dismantling.Revision, GameSessionTestFixture.Ship, new(0)));
        session.AdvanceTo(new(7_499));
        Assert.Single(session.CaptureSnapshot().Stations);
        session.AdvanceTo(new(7_500));
        Assert.Empty(session.CaptureSnapshot().Stations);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        InventoryCheckpoint cargo = checkpoint.Lifecycle.Inventories.Inventories.Single(item => item.Id == new InventoryId(1));
        Assert.Equal(new Quantity(2), Assert.Single(cargo.FungibleHoldings).Quantity);
        Assert.DoesNotContain(checkpoint.Lifecycle.Inventories.Inventories, item => item.Id == site.InventoryId);
        Assert.Single(session.ReadFactsAfter(null, 256).Facts, item => item.Fact is StationRemovedFact);
    }

    [Fact]
    public void UnfinishedBaseDismantlingRestoresStagingAndCapsRecoveryAtConsumedInputs()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite());
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectId construction = site.Projects.Single().Id;
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, construction, site.Revision, new(1), new(0)));
        Store(session, site.InventoryId, 1, 1);
        session.AdvanceTo(new(1_000));
        StationSnapshot partial = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new ControlStationProjectCommand(
            GameSessionTestFixture.Principal, site.Id, construction, partial.Revision, StationProjectAction.Cancel));
        StationSnapshot cancelled = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new DismantleStationBaseCommand(
            GameSessionTestFixture.Principal, site.Id, cancelled.Revision, new(1)));
        StationSnapshot dismantling = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(GameSessionTestFixture.Principal,
            site.Id, dismantling.Projects.Single(project => project.Kind == StationProjectKind.Dismantling).Id,
            dismantling.Revision, new(1), new(0)));
        session.AdvanceTo(new(1_500));
        CheckpointResult<GameSessionCheckpoint> captured = session.CaptureCheckpoint();
        Assert.True(captured.IsSuccess, captured.Failure?.Message);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(captured.Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        restored.AdvanceTo(new(3_500));

        Assert.Empty(restored.CaptureSnapshot().Stations);
        InventoryCheckpoint cargo = restored.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories
            .Single(inventory => inventory.Id == new InventoryId(1));
        Assert.Equal(new Quantity(1), Assert.Single(cargo.FungibleHoldings).Quantity);
    }

    [Fact]
    public void FinalStationRemovalInvalidatesInboundEntityOrders()
    {
        using GameSession session = Create(ships: 2);
        session.SubmitCommand(GameSessionTestFixture.Player, CreateSite() with { Position = GameSessionTestFixture.Position(80, 0) });
        StationSnapshot site = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(
            GameSessionTestFixture.Principal, site.Id, site.Projects.Single().Id, site.Revision, new(1), new(0)));
        Store(session, site.InventoryId, 2, 1);
        session.AdvanceTo(new(2_500));
        Store(session, site.InventoryId, 2, 2);
        session.AdvanceTo(new(5_000));
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(2), new NavigationDestination.Entity(site.EntityId), OrderPlacement.ReplaceAll));
        StationSnapshot complete = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new DismantleStationBaseCommand(
            GameSessionTestFixture.Principal, site.Id, complete.Revision, new(1)));
        StationSnapshot dismantling = Assert.Single(session.CaptureSnapshot().Stations);
        session.SubmitCommand(GameSessionTestFixture.Player, new AssignStationBuilderCommand(GameSessionTestFixture.Principal,
            site.Id, dismantling.Projects.Single(project => project.Kind == StationProjectKind.Dismantling).Id,
            dismantling.Revision, new(1), new(0)));
        session.AdvanceTo(new(7_500));

        GameShipSnapshot inbound = session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(2));
        Assert.Empty(session.CaptureSnapshot().Stations);
        Assert.Equal(ShipOrderStatus.Failed, inbound.CurrentOrder!.Status);
        Assert.Equal(ShipOrderReason.TargetRemoved, inbound.CurrentOrder.Reason);
    }

    private static CreateStationSiteCommand CreateSite() => new(GameSessionTestFixture.Principal,
        GameSessionTestFixture.Ship, BaseKey, GameSessionTestFixture.Position(0, 0), new(0));

    private static void Store(GameSession session, InventoryId inventory, ulong amount, ulong operation) =>
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(
            new(InventoryOperationSourceKind.Explicit, operation), inventory, MetalDefinition, new(amount))]);

    private static GameSession Create(int ships = 1)
    {
        var recipe = new ConstructionRecipe(new Dictionary<MaterialId, Quantity> { [Metal] = new(4) }, new(10));
        var definition = new StationDefinition(BaseKey, "Test Station", 4, new(2), new(10), recipe, 123,
            [], new(5), new Dictionary<MaterialId, Quantity> { [Metal] = new(2) });
        var materials = new MaterialInventoryCompatibilityMap(
            new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var ship = new ShipDesign(new(1), "Builder", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, 10_000, 5_000, new StationConstructionCapability(100, new(2)));
        var setup = new GameSessionSetup([new StarSystem(GameSessionTestFixture.System, "System")],
            Enumerable.Range(1, ships).Select(id => new InitialShipSetup(new((ulong)id), new((ulong)id), new((ulong)id),
                GameSessionTestFixture.Principal, ship, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)),
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player,
            stations: new StationRuntimeSetup(new([definition], []), materials, StationTerritoryPolicy.Absent));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
