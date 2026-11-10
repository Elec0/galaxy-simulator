using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationModuleSessionTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly QualifiedContentKey BaseKey = Key("station", "base");
    private static readonly QualifiedContentKey StorageKey = Key("station-module", "storage");
    private static readonly QualifiedContentKey UpgradeKey = Key("station-module", "advanced-storage");
    private static readonly PhysicalDefinition MetalDefinition = new(Key("material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void ReferencedUpgradeWithoutExplicitRecipeCannotEnterLiveCatalog()
    {
        var target = new StationModuleDefinition(UpgradeKey, "Target", 5, new(20), 100, null,
            Recipe(8, 8), new(2), [], []);
        var missingCost = new StationModuleDefinition(StorageKey, "Storage", 4, new(10), 50, null,
            Recipe(2, 2), new(2), [], [UpgradeKey]);

        Assert.Throws<ArgumentException>(() => new StationDefinitionCatalog([], [missingCost, target]));
    }

    [Fact]
    public void ConstructionClaimsModuleCapacityBeforeActivatingItsStorageOrSensors()
    {
        using GameSession session = Create();
        StationSnapshot station = Current(session);
        Accept(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, StorageKey));
        StationSnapshot admitted = Current(session);
        StationModuleSnapshot module = Assert.Single(admitted.Modules);
        Assert.Equal(StationModulePhase.UnderConstruction, module.Phase);
        Assert.Equal(10UL, admitted.SensorRadiusUnits);
        GameplayCommandRecord overbooked = session.SubmitCommand(Player,
            new ConstructStationModuleCommand(Principal, station.Id, admitted.Revision, StorageKey));
        Assert.Equal(CommandResultStatus.Rejected, overbooked.Result.Status);

        CompleteModule(session);

        StationSnapshot complete = Current(session);
        Assert.Equal(module.Id, Assert.Single(complete.Modules).Id);
        Assert.Equal(StationModulePhase.Operational, Assert.Single(complete.Modules).Phase);
        Assert.Equal(50UL, complete.SensorRadiusUnits);
        Assert.Equal(StationProjectStatus.Completed, complete.Projects.Single(project => project.ModuleId == module.Id).Status);
    }

    [Fact]
    public void ExplicitUpgradeCostRetainsOldCapabilitiesUntilAtomicReplacement()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        StationSnapshot before = Current(session);
        StationModuleId module = Assert.Single(before.Modules).Id;
        Accept(session, new UpgradeStationModuleCommand(Principal, before.Id, before.Revision, module, UpgradeKey));
        AssignActive(session);
        Store(session, 1);
        session.AdvanceTo(new(3_000));
        StationSnapshot partial = Current(session);
        Assert.Equal(StorageKey, Assert.Single(partial.Modules).DefinitionKey);
        Assert.Equal(50UL, partial.SensorRadiusUnits);
        Assert.Equal(new Work(2), partial.Projects.Single(project => project.Kind == StationProjectKind.Upgrade).CompletedWork);

        Store(session, 1);
        session.AdvanceTo(new(4_000));

        StationSnapshot upgraded = Current(session);
        Assert.Equal(module, Assert.Single(upgraded.Modules).Id);
        Assert.Equal(UpgradeKey, Assert.Single(upgraded.Modules).DefinitionKey);
        Assert.Equal(100UL, upgraded.SensorRadiusUnits);
        Assert.Equal(5UL, Assert.Single(upgraded.Modules).OccupiedCapacity);
    }

    [Fact]
    public void CancelledConsumedConstructionRetainsStructureUntilTimedDismantling()
    {
        using GameSession session = Create();
        StationSnapshot station = Current(session);
        Accept(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, StorageKey));
        AssignActive(session);
        Store(session, 1);
        session.AdvanceTo(new(1_500));
        ControlActive(session, StationProjectAction.Cancel);
        StationSnapshot cancelled = Current(session);
        StationModuleSnapshot module = Assert.Single(cancelled.Modules);
        Assert.Equal(StationModulePhase.Unfinished, module.Phase);
        Assert.Equal(10UL, cancelled.SensorRadiusUnits);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player,
            new ConstructStationModuleCommand(Principal, station.Id, cancelled.Revision, StorageKey)).Result.Status);

        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, cancelled.Revision, module.Id, station.InventoryId));
        AssignActive(session);
        session.AdvanceTo(new(2_500));

        Assert.Empty(Current(session).Modules);
        Assert.Equal(StationProjectStatus.Completed,
            Current(session).Projects.Single(project => project.Kind == StationProjectKind.Dismantling).Status);
        Assert.Equal(new Quantity(1), StationInventory(session).Stored(Metal));
    }

    [Fact]
    public void DismantlingStopsServicesAndReservesRecoveryBeforeAnyWork()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision, module.Id, station.InventoryId));
        StationSnapshot started = Current(session);
        Assert.Equal(StationModulePhase.Dismantling, Assert.Single(started.Modules).Phase);
        Assert.Equal(10UL, started.SensorRadiusUnits);
        Assert.Equal(new Quantity(20), StationInventory(session).ProtectedIntakeCapacity);
        Assert.Equal(new Quantity(1), StationInventory(session).ReservedCapacity);
        Assert.Equal(Quantity.Zero, StationInventory(session).Stored(Metal));
        AssignActive(session);
        session.AdvanceTo(new(2_500));
        ControlActive(session, StationProjectAction.Pause);
        session.AdvanceTo(new(3_500));
        Assert.Equal(Quantity.Zero, StationInventory(session).Stored(Metal));
        StationProjectSnapshot paused = Current(session).Projects.Single(project => project.Kind == StationProjectKind.Dismantling);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player,
            new ControlStationProjectCommand(Principal, station.Id, paused.Id, Current(session).Revision, StationProjectAction.Cancel)).Result.Status);
        ControlActive(session, StationProjectAction.Resume);
        session.AdvanceTo(new(4_000));

        Assert.Empty(Current(session).Modules);
        Assert.Equal(new Quantity(20), StationInventory(session).Capacity);
        Assert.Equal(new Quantity(1), StationInventory(session).Stored(Metal));
        Assert.Equal(Quantity.Zero, StationInventory(session).ReservedCapacity);
        Assert.Null(StationInventory(session).ProtectedIntakeCapacity);
    }

    [Fact]
    public void CancelledUpgradeAdditionBlocksAnotherProjectAndRecoversOnlyConsumedInputs()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        StationModuleId module = Assert.Single(station.Modules).Id;
        Accept(session, new UpgradeStationModuleCommand(Principal, station.Id, station.Revision, module, UpgradeKey));
        AssignActive(session);
        Store(session, 1);
        session.AdvanceTo(new(3_000));
        ControlActive(session, StationProjectAction.Cancel);
        StationSnapshot cancelled = Current(session);
        Assert.Equal(StorageKey, Assert.Single(cancelled.Modules).DefinitionKey);
        Assert.Equal(UpgradeKey, Assert.Single(cancelled.Modules).UnfinishedUpgrade);
        Assert.Equal(5UL, Assert.Single(cancelled.Modules).OccupiedCapacity);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player,
            new UpgradeStationModuleCommand(Principal, station.Id, cancelled.Revision, module, UpgradeKey)).Result.Status);

        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, cancelled.Revision, module, station.InventoryId, true));
        AssignActive(session);
        session.AdvanceTo(new(4_000));

        StationModuleSnapshot cleaned = Assert.Single(Current(session).Modules);
        Assert.Equal(StorageKey, cleaned.DefinitionKey);
        Assert.Null(cleaned.UnfinishedUpgrade);
        Assert.Equal(4UL, cleaned.OccupiedCapacity);
        Assert.Equal(new Quantity(1), StationInventory(session).Stored(Metal));
    }

    [Fact]
    public void FractionalDismantlingWorkMakesCancellationIrreversible()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision,
            Assert.Single(station.Modules).Id, station.InventoryId));
        AssignActive(session);
        session.AdvanceTo(new(2_100));
        StationProjectSnapshot project = Current(session).Projects.Single(item => item.Kind == StationProjectKind.Dismantling);
        Assert.Equal(new Work(0), project.CompletedWork);

        CommandResult result = session.SubmitCommand(Player, new ControlStationProjectCommand(Principal, station.Id,
            project.Id, Current(session).Revision, StationProjectAction.Cancel)).Result;

        Assert.Equal(CommandResultStatus.Rejected, result.Status);
        Assert.Equal("station-dismantling-irreversible", result.RejectionCode!.Value.Value);
        Assert.Equal(StationModulePhase.Dismantling, Assert.Single(Current(session).Modules).Phase);
    }

    [Fact]
    public void ConstructionModuleNeedsExplicitAssignmentAndCannotWorkOnItsOwnUpgrade()
    {
        using GameSession session = Create(10, new(100, new(2)));
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        StationModuleId source = Assert.Single(station.Modules).Id;
        Accept(session, new UpgradeStationModuleCommand(Principal, station.Id, station.Revision, source, UpgradeKey));
        StationProjectSnapshot upgrade = Current(session).Projects.Single(item => item.Kind == StationProjectKind.Upgrade);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player,
            new AssignStationModuleBuilderCommand(Principal, station.Id, Current(session).Revision, upgrade.Id, source)).Result.Status);
        ControlActive(session, StationProjectAction.Cancel);

        station = Current(session);
        Accept(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, StorageKey));
        StationProjectSnapshot construction = Current(session).Projects.Single(item => item.Status == StationProjectStatus.Waiting);
        Store(session, 2);
        session.AdvanceTo(new(2_500));
        Assert.Equal(new Work(0), Current(session).Projects.Single(item => item.Id == construction.Id).CompletedWork);
        Accept(session, new AssignStationModuleBuilderCommand(Principal, station.Id, Current(session).Revision, construction.Id, source));
        session.AdvanceTo(new(3_500));

        StationProjectSnapshot completed = Current(session).Projects.Single(item => item.Id == construction.Id);
        Assert.Equal(StationProjectStatus.Completed, completed.Status);
        Assert.Empty(completed.AssignedModules!);
        Assert.Equal(2, Current(session).Modules.Count);
        Assert.Equal(50UL, Current(session).SensorRadiusUnits);
    }

    [Fact]
    public void DisablingStoragePreservesPhysicalCargoAndWithdrawsOnlyOperationalContributions()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        Store(session, 25);
        StationSnapshot station = Current(session);
        StationModuleId module = Assert.Single(station.Modules).Id;
        Accept(session, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module, false));

        Inventory disabled = StationInventory(session);
        Assert.Equal(new Quantity(30), disabled.Capacity);
        Assert.Equal(new Quantity(20), disabled.EnabledIntakeCapacity);
        Assert.Equal(new Quantity(25), disabled.Stored(Metal));
        Assert.Equal(Quantity.Zero, disabled.RemainingCapacity);
        Assert.Equal(10UL, Current(session).SensorRadiusUnits);

        Accept(session, new SetStationModuleEnabledCommand(Principal, station.Id, Current(session).Revision, module, true));

        Assert.Equal(new Quantity(5), StationInventory(session).RemainingCapacity);
        Assert.Equal(50UL, Current(session).SensorRadiusUnits);
    }

    [Fact]
    public void CancellingUnconsumedModulePublishesCausalExteriorRemoval()
    {
        using GameSession session = Create();
        StationSnapshot station = Current(session);
        Accept(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, StorageKey));
        StationModuleId module = Assert.Single(Current(session).Modules).Id;

        ControlActive(session, StationProjectAction.Cancel);

        Assert.Empty(Current(session).Modules);
        Assert.Contains(session.ReadFactsAfter(null, 512).Facts,
            item => item.Fact is StationModuleRemovedFact removed && removed.StationId == station.Id && removed.ModuleId == module);
    }

    [Fact]
    public void InProgressDismantlingRestoresExactRecoveryClaimAndContinuesOnce()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision,
            Assert.Single(station.Modules).Id, station.InventoryId));
        AssignActive(session);
        session.AdvanceTo(new(2_500));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint,
            new PhysicalDefinitionCatalog([MetalDefinition]),
            new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition }));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession continued = restored.Value!;
        Assert.Equal(new Quantity(1), StationInventory(continued).ReservedCapacity);

        session.AdvanceTo(new(3_000));
        continued.AdvanceTo(new(3_000));

        Assert.Empty(Current(continued).Modules);
        Assert.Equal(new Quantity(1), StationInventory(continued).Stored(Metal));
        Assert.Equal(Quantity.Zero, StationInventory(continued).ReservedCapacity);
        Assert.Equal(StationInventory(session).Stored(Metal), StationInventory(continued).Stored(Metal));
        continued.AdvanceTo(new(4_000));
        Assert.Equal(new Quantity(1), StationInventory(continued).Stored(Metal));
    }

    [Fact]
    public void InvalidatedRecoveryClaimWaitsAtCompletedWorkAcrossRestoreUntilExplicitReplacement()
    {
        using GameSession session = Create();
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision,
            Assert.Single(station.Modules).Id, station.InventoryId));
        AssignActive(session);
        PhysicalReservation claim = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories
            .Single(item => item.Id == station.InventoryId).PhysicalReservations.Single();
        session.CommitInventoryMutations([new ReleasePhysicalReservationProposal(new(InventoryOperationSourceKind.Explicit, 99_001),
            station.InventoryId, claim.Id, claim.Owner)]);
        session.AdvanceTo(new(3_000));
        StationProjectSnapshot waiting = Current(session).Projects.Single(item => item.Kind == StationProjectKind.Dismantling);
        Assert.Equal(new Work(2), waiting.CompletedWork);
        Assert.Equal(StationProjectStatus.Waiting, waiting.Status);
        Assert.Equal(Quantity.Zero, StationInventory(session).Stored(Metal));
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!,
            new PhysicalDefinitionCatalog([MetalDefinition]),
            new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition }));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession continued = restored.Value!;

        Accept(continued, new SetStationRecoveryDestinationCommand(Principal, station.Id, Current(continued).Revision,
            waiting.Id, station.InventoryId));

        Assert.Equal(new SimulationTime(3_000), continued.CurrentTime);
        Assert.Empty(Current(continued).Modules);
        Assert.Equal(new Quantity(1), StationInventory(continued).Stored(Metal));
        Assert.Equal(Quantity.Zero, StationInventory(continued).ReservedCapacity);
    }

    [Fact]
    public void PendingStorageExpansionsCannotOverbookTheCapacityRepresentation()
    {
        using GameSession session = Create(10, storageCapacity: ulong.MaxValue / 2);
        StationSnapshot station = Current(session);
        Accept(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, StorageKey));

        CommandResult result = session.SubmitCommand(Player,
            new ConstructStationModuleCommand(Principal, station.Id, Current(session).Revision, StorageKey)).Result;

        Assert.Equal(CommandResultStatus.Rejected, result.Status);
        Assert.Single(Current(session).Modules);
    }

    [Fact]
    public void UpgradeRejectsUnrepresentableFutureStorageBeforeConsumingMaterials()
    {
        using GameSession session = Create(upgradedStorageCapacity: ulong.MaxValue);
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);

        CommandResult result = session.SubmitCommand(Player,
            new UpgradeStationModuleCommand(Principal, station.Id, station.Revision, Assert.Single(station.Modules).Id, UpgradeKey)).Result;

        Assert.Equal(CommandResultStatus.Rejected, result.Status);
        Assert.Equal(StorageKey, Assert.Single(Current(session).Modules).DefinitionKey);
        Assert.DoesNotContain(Current(session).Projects, item => item.Kind == StationProjectKind.Upgrade);
    }

    [Fact]
    public void ShrinkingUpgradeRequiresRoomAtAdmissionAndProtectsItUntilCompletion()
    {
        using GameSession session = Create(upgradedStorageCapacity: 4);
        ConstructAndComplete(session);
        Store(session, 25);
        StationSnapshot station = Current(session);
        StationModuleId module = Assert.Single(station.Modules).Id;
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player,
            new UpgradeStationModuleCommand(Principal, station.Id, station.Revision, module, UpgradeKey)).Result.Status);
        session.CommitInventoryMutations([new TransferPhysicalInventoryProposal(new(InventoryOperationSourceKind.Explicit, 90_001),
            new(station.InventoryId, new(1), MetalDefinition, new PhysicalTransferSubject.Fungible(MetalDefinition.Key, new(3))))]);

        Accept(session, new UpgradeStationModuleCommand(Principal, station.Id, Current(session).Revision, module, UpgradeKey));
        Assert.Equal(new Quantity(24), StationInventory(session).ProtectedIntakeCapacity);
        InventoryCommitBatchResult attempted = session.CommitInventoryMutations([
            new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 90_002), station.InventoryId, MetalDefinition, new(3))]);
        InventoryMutationOutcome.Rejected rejected = Assert.IsType<InventoryMutationOutcome.Rejected>(Assert.Single(attempted.Outcomes).Outcome);
        Assert.Equal(InventoryStorageRejectionReason.InsufficientCapacity, rejected.StorageReason);
        AssignActive(session);
        session.AdvanceTo(new(4_000));

        Assert.Equal(new Quantity(24), StationInventory(session).Capacity);
        Assert.Null(StationInventory(session).ProtectedIntakeCapacity);
        Assert.Equal(UpgradeKey, Assert.Single(Current(session).Modules).DefinitionKey);
    }

    [Fact]
    public void CompletedDismantlingRetriesWhenSurvivingRecoveryStorageIsReenabledWithoutAnotherBuilder()
    {
        using GameSession session = Create(10);
        ConstructAndComplete(session);
        StationModuleId removed = Assert.Single(Current(session).Modules).Id;
        ConstructAndComplete(session);
        StationSnapshot station = Current(session);
        StationModuleId surviving = station.Modules.Single(item => item.Id != removed).Id;
        Store(session, 25);
        Accept(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision, removed, station.InventoryId));
        AssignActive(session);
        Accept(session, new SetStationModuleEnabledCommand(Principal, station.Id, Current(session).Revision, surviving, false));
        session.AdvanceTo(new(4_000));
        StationProjectSnapshot project = Current(session).Projects.Single(item => item.Kind == StationProjectKind.Dismantling);
        Assert.Equal(project.RequiredWork, project.CompletedWork);
        Assert.Equal(StationProjectStatus.Waiting, project.Status);
        Assert.Equal(new Quantity(25), StationInventory(session).Stored(Metal));
        Accept(session, new ReleaseStationBuilderCommand(Principal, station.Id, project.Id, Current(session).Revision,
            GameSessionTestFixture.Ship, new(0)));

        Accept(session, new SetStationModuleEnabledCommand(Principal, station.Id, Current(session).Revision, surviving, true));
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!,
            new PhysicalDefinitionCatalog([MetalDefinition]),
            new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition }));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession continued = restored.Value!;
        session.AdvanceTo(new(4_001));
        continued.AdvanceTo(new(4_001));

        Assert.Equal(surviving, Assert.Single(Current(session).Modules).Id);
        Assert.Equal(StationProjectStatus.Completed, Current(session).Projects.Single(item => item.Id == project.Id).Status);
        Assert.Equal(project.RequiredWork, Current(session).Projects.Single(item => item.Id == project.Id).CompletedWork);
        Assert.Equal(new Quantity(26), StationInventory(session).Stored(Metal));
        Assert.Equal(Quantity.Zero, StationInventory(session).ReservedCapacity);
        Assert.Equal(surviving, Assert.Single(Current(continued).Modules).Id);
        Assert.Equal(project.RequiredWork, Current(continued).Projects.Single(item => item.Id == project.Id).CompletedWork);
        Assert.Equal(new Quantity(26), StationInventory(continued).Stored(Metal));
        continued.AdvanceTo(new(5_000));
        Assert.Equal(new Quantity(26), StationInventory(continued).Stored(Metal));
    }

    private static PrincipalId Principal => GameSessionTestFixture.Principal;
    private static CommandSource Player => GameSessionTestFixture.Player;
    private static StationSnapshot Current(GameSession session) => Assert.Single(session.CaptureSnapshot().Stations);
    private static Inventory StationInventory(GameSession session)
    {
        InventoryId id = Current(session).InventoryId;
        InventoryCheckpoint saved = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(item => item.Id == id);
        return Inventory.RestoreCheckpoint(saved, new PhysicalDefinitionCatalog([MetalDefinition]),
            new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition })).Value!;
    }

    private static void Accept(GameSession session, GameplayCommand command)
    {
        CommandResult result = session.SubmitCommand(Player, command).Result;
        Assert.True(result.Status == CommandResultStatus.Accepted, $"{command.Kind}: {result.RejectionCode} {result.Reason}");
    }

    private static void Store(GameSession session, ulong amount) => session.CommitInventoryMutations([
        new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit,
            (ulong)session.CommandRecords.Count * 1_000_000 + session.CurrentTime.Milliseconds),
            Current(session).InventoryId, MetalDefinition, new(amount))]);

    private static void AssignActive(GameSession session)
    {
        StationSnapshot station = Current(session);
        StationProjectSnapshot project = station.Projects.Single(item => item.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled));
        Accept(session, new AssignStationBuilderCommand(Principal, station.Id, project.Id, station.Revision, GameSessionTestFixture.Ship, new(0)));
    }

    private static void ControlActive(GameSession session, StationProjectAction action)
    {
        StationSnapshot station = Current(session);
        StationProjectSnapshot project = station.Projects.Single(item => item.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled));
        Accept(session, new ControlStationProjectCommand(Principal, station.Id, project.Id, station.Revision, action));
    }

    private static void CompleteModule(GameSession session)
    {
        AssignActive(session);
        Store(session, 2);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 1_000));
    }

    private static void ConstructAndComplete(GameSession session)
    {
        StationSnapshot station = Current(session);
        Accept(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, StorageKey));
        CompleteModule(session);
    }

    private static GameSession Create(ulong moduleCapacity = 6, StationConstructionCapability? construction = null,
        ulong storageCapacity = 10, ulong upgradedStorageCapacity = 20)
    {
        var recovery = new Dictionary<MaterialId, Quantity> { [Metal] = new(1) };
        var upgrade = new StationModuleDefinition(UpgradeKey, "Advanced", 5, new(upgradedStorageCapacity), 100, null,
            Recipe(8, 8), new(2), new Dictionary<MaterialId, Quantity> { [Metal] = new(2) }, []);
        var storage = new StationModuleDefinition(StorageKey, "Storage", 4, new(storageCapacity), 50, construction,
            Recipe(2, 2), new(2), recovery, [UpgradeKey], [new(UpgradeKey, Recipe(4, 2))]);
        var definition = new StationDefinition(BaseKey, "Base", moduleCapacity, new(2), new(20), Recipe(2, 2), 10,
            [StorageKey, UpgradeKey], new(2), recovery);
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var ship = new ShipDesign(new(1), "Builder", new([], new(1)), new(30), GameSessionTestFixture.ManeuverCapability,
            10_000, 5_000, new(100, new(2)));
        var setup = new GameSessionSetup([new StarSystem(GameSessionTestFixture.System, "System")],
            [new InitialShipSetup(new(1), new(1), new(1), Principal, ship, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 512, Player,
            stations: new(new([definition], [storage, upgrade]), materials, StationTerritoryPolicy.Absent));
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        Accept(session, new CreateStationSiteCommand(Principal, GameSessionTestFixture.Ship, BaseKey, GameSessionTestFixture.Position(0, 0), new(0)));
        AssignActive(session);
        Store(session, 2);
        session.AdvanceTo(new(1_000));
        return session;
    }

    private static ConstructionRecipe Recipe(ulong work, ulong metal) => new(new Dictionary<MaterialId, Quantity> { [Metal] = new(metal) }, new(work));
    private static QualifiedContentKey Key(string kind, string local) => QualifiedContentKey.Create("test.modules", kind, local);
}
