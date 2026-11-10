using GalaxyCommand.Content;
using System.Numerics;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationDestructionSessionTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.station-loss", "station", "base");
    private static readonly QualifiedContentKey StorageKey = QualifiedContentKey.Create("test.station-loss", "station-module", "storage");
    private static readonly PhysicalDefinition MetalDefinition = new(
        QualifiedContentKey.Create("test.station-loss", "material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void StorageLossPublishesRecoverableFieldWhileStationSurvivesAndReplaysExactly()
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Store(session, station.InventoryId, 400, 1);
        StationDestructionRequest request = Request(session, station, destroyBase: false);

        StationDestructionResult committed = session.CommitStationDestruction(request);

        Assert.True(committed.IsSuccess, committed.Failure?.ToString());
        Assert.True(committed.WasApplied);
        StationSnapshot surviving = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(station.EntityId, surviving.EntityId);
        Assert.Empty(surviving.Modules);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        InventoryCheckpoint inventory = checkpoint.Lifecycle.Inventories.Inventories.Single(item => item.Id == station.InventoryId);
        Assert.Equal(new Quantity(750), inventory.Capacity);
        Assert.Equal(new Quantity(300), Assert.Single(inventory.FungibleHoldings).Quantity);
        CargoObjectView recovered = Assert.Single(session.CaptureVisibleCargo());
        Assert.Equal(new Quantity(50), recovered.Quantity);
        Assert.NotNull(recovered.OriginalFieldId);

        StationDestructionResult replay = session.CommitStationDestruction(request);
        Assert.True(replay.IsSuccess);
        Assert.False(replay.WasApplied);
        Assert.Single(session.CaptureVisibleCargo());
        Assert.Equal(checkpoint.Lifecycle.EntityIds, session.CaptureCheckpoint().Value!.Lifecycle.EntityIds);
    }

    [Fact]
    public void SimultaneousBaseAndModuleLossDisposesCargoOnceAndRetainsRemovedSourceOnRestore()
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Store(session, station.InventoryId, 400, 1);
        StationDestructionRequest request = Request(session, station, destroyBase: true);

        Assert.True(session.CommitStationDestruction(request).IsSuccess);
        Assert.Empty(session.CaptureSnapshot().Stations);
        Assert.Equal(new Quantity(200), Assert.Single(session.CaptureVisibleCargo()).Quantity);
        Assert.Single(session.CaptureCheckpoint().Value!.Cargo!.Receipts);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        Assert.False(restored.CommitStationDestruction(request).WasApplied);
        Assert.Empty(restored.CaptureSnapshot().Stations);
        Assert.Equal(new Quantity(200), Assert.Single(restored.CaptureVisibleCargo()).Quantity);
    }

    [Fact]
    public void LiveStationLossFieldCanBeCollectedAfterCheckpointContinuation()
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Store(session, station.InventoryId, 400, 1);
        Assert.True(session.CommitStationDestruction(Request(session, station, destroyBase: false)).IsSuccess);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        CargoObjectView recovered = Assert.Single(restored.CaptureVisibleCargo());
        GameplayCommandRecord collect = restored.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(1), recovered.OriginalFieldId!.Value, [], OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, collect.Result.Status);
        restored.AdvanceTo(restored.CurrentTime.Add(new(1)));

        Assert.Empty(restored.CaptureVisibleCargo());
        GameSessionCheckpoint checkpoint = restored.CaptureCheckpoint().Value!;
        InventoryCheckpoint cargo = checkpoint.Lifecycle.Inventories.Inventories.Single(item => item.Id == new InventoryId(1));
        Assert.Equal(new Quantity(50), Assert.Single(cargo.FungibleHoldings).Quantity);
        Assert.Single(restored.CaptureSnapshot().Stations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingReplayAndStaleLossDoNotAllocateOrChangeSurvivingAuthority(bool destroyBase)
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Store(session, station.InventoryId, 400, 1);
        StationDestructionRequest request = Request(session, station, destroyBase);
        Assert.True(session.CommitStationDestruction(request).WasApplied);
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;

        Assert.Equal(StationDestructionFailure.ConflictingReplay,
            session.CommitStationDestruction(request with { ExpectedRevision = request.ExpectedRevision + 1 }).Failure);
        Assert.Equal(StationDestructionFailure.StaleTarget,
            session.CommitStationDestruction(request with { Occurrence = 2 }).Failure);

        GameSessionCheckpoint after = session.CaptureCheckpoint().Value!;
        Assert.Equal(before.Lifecycle.EntityIds, after.Lifecycle.EntityIds);
        Assert.Equal(before.InventoryCommit!.ItemInstanceIds, after.InventoryCommit!.ItemInstanceIds);
        Assert.Equal(before.Random!.Streams, after.Random!.Streams);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
        Assert.Single(after.Cargo!.Receipts);
        Assert.Single(after.Stations!.Destructions);
    }

    [Fact]
    public void BaseDestructionInvalidatesAnActiveInboundEntityOrderInTheSameCommit()
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Accepted(session, new MoveShipCommand(new(1), GameSessionTestFixture.Destination(100_000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(1_002));
        GameplayCommandRecord inbound = session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), new NavigationDestination.Entity(station.EntityId), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, inbound.Result.Status);
        StationSnapshot current = Assert.Single(session.CaptureSnapshot().Stations);
        var request = new StationDestructionRequest(new CargoDispositionTrigger.Command(inbound.Envelope.Sequence),
            station.Id, current.Revision, station.Modules.Select(module => module.Id).ToArray(), true, 1);

        Assert.True(session.CommitStationDestruction(request).WasApplied);

        GameShipSnapshot ship = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Failed, ship.CurrentOrder!.Status);
        Assert.Equal(ShipOrderReason.TargetRemoved, ship.CurrentOrder.Reason);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        restored.Value!.Dispose();
    }

    [Fact]
    public void DestructionReleasesRetiredDismantlingRecoveryBeforeCommittingSourceCapacity()
    {
        using GameSession session = Create(recovery: true);
        StationSnapshot station = Build(session);
        Store(session, station.InventoryId, 400, 1);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(session, new DismantleStationModuleCommand(GameSessionTestFixture.Principal, station.Id,
            station.Revision, module.Id, station.InventoryId));
        station = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectSnapshot dismantle = Assert.Single(station.Projects, project => project.Kind == StationProjectKind.Dismantling);
        Assert.Single(session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories
            .Single(item => item.Id == station.InventoryId).PhysicalReservations);

        Assert.True(session.CommitStationDestruction(Request(session, station, destroyBase: false)).WasApplied);

        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        Assert.Empty(checkpoint.Lifecycle.Inventories.Inventories.Single(item => item.Id == station.InventoryId).PhysicalReservations);
        Assert.Equal(StationProjectStatus.Cancelled, Assert.Single(session.CaptureSnapshot().Stations).Projects
            .Single(project => project.Id == dismantle.Id).Status);
        Assert.Contains(session.ReadFactsAfter(null, 256).Facts,
            envelope => envelope.Fact is StationCommitmentInvalidatedFact invalidated
                && invalidated.Owner == new ReservationOwner.StationProject(station.Id, dismantle.Id));
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        restored.Value!.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RestoreRejectsMissingOrAlteredDestructionWitnesses(int corruption)
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Store(session, station.InventoryId, 400, 1);
        Assert.True(session.CommitStationDestruction(Request(session, station, destroyBase: true)).WasApplied);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        StationRuntimeCheckpoint stations = checkpoint.Stations!;
        StationDestructionReceipt receipt = Assert.Single(stations.Destructions);
        stations = corruption switch
        {
            0 => stations with { Destructions = [] },
            1 => stations with { Removals = [] },
            2 => stations with { Destructions = [receipt with { WindowAfter = new(1, new Dictionary<QualifiedContentKey, BigInteger>()) }] },
            _ => stations with { Destructions = [receipt with { Request = receipt.Request with
                { Trigger = new CargoDispositionTrigger.Command(new(ulong.MaxValue)) } }] },
        };

        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint with { Stations = stations });
        Assert.False(restored.IsSuccess);
        Assert.NotNull(restored.Failure);
    }

    [Fact]
    public void LiveSmallLossWindowSurvivesRestoreAndSettlesAtExactThreshold()
    {
        using GameSession session = Create(baseCapacity: 998, storageCapacity: 1, cutoffDenominator: 500);
        Build(session);
        StationSnapshot station = BuildStorage(session);
        Store(session, station.InventoryId, 500, 1);
        StationModuleId first = station.Modules[0].Id;
        StationDestructionRequest request = Request(session, station, destroyBase: false) with { Modules = [first] };
        Assert.True(session.CommitStationDestruction(request).WasApplied);
        GameSessionCheckpoint intermediate = session.CaptureCheckpoint().Value!;
        Assert.Equal(BigInteger.One, Assert.Single(intermediate.Stations!.Stations).LossWindow.AccumulatedDestroyedCapacity);
        Assert.Empty(Assert.Single(intermediate.Cargo!.Receipts).Request.Selected);
        Assert.Equal(new Quantity(500), Assert.Single(intermediate.Lifecycle.Inventories.Inventories
            .Single(item => item.Id == station.InventoryId).FungibleHoldings).Quantity);
        Assert.Empty(session.CaptureVisibleCargo());
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(intermediate);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        StationSnapshot remaining = Assert.Single(restored.CaptureSnapshot().Stations);

        Assert.True(restored.CommitStationDestruction(Request(restored, remaining, destroyBase: false) with { Occurrence = 2 }).WasApplied);

        GameSessionCheckpoint settled = restored.CaptureCheckpoint().Value!;
        Assert.Equal(BigInteger.Zero, Assert.Single(settled.Stations!.Stations).LossWindow.AccumulatedDestroyedCapacity);
        Assert.Equal(new Quantity(499), Assert.Single(settled.Lifecycle.Inventories.Inventories
            .Single(item => item.Id == station.InventoryId).FungibleHoldings).Quantity);
        Assert.Equal(2, settled.Cargo!.Receipts.Count);
    }

    [Fact]
    public void ExhaustedRevisionRejectsBaseDismantlingBeforeAllocatingProjectsOrChangingIntake()
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        Assert.True(session.CommitStationDestruction(Request(session, station, destroyBase: false)).WasApplied);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        StationStateCheckpoint saved = Assert.Single(checkpoint.Stations!.Stations);
        checkpoint = checkpoint with { Stations = checkpoint.Stations with { Stations = [saved with { Revision = ulong.MaxValue }] } };
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;

        GameplayCommandRecord result = restored.SubmitCommand(GameSessionTestFixture.Player,
            new DismantleStationBaseCommand(GameSessionTestFixture.Principal, station.Id, ulong.MaxValue, new(1)));

        Assert.Equal(CommandResultStatus.Rejected, result.Result.Status);
        Assert.Equal(new CommandRejectionCode("station-revision-exhausted"), result.Result.RejectionCode);
        GameSessionCheckpoint after = restored.CaptureCheckpoint().Value!;
        Assert.Equal(checkpoint.Stations.ProjectIds, after.Stations!.ProjectIds);
        Assert.Equal(saved.Projects.Count, Assert.Single(after.Stations.Stations).Projects.Count);
        Assert.Equal(new Quantity(750), after.Lifecycle.Inventories.Inventories.Single(item => item.Id == station.InventoryId).EnabledIntakeCapacity);
    }

    internal static StationDestructionRequest Request(GameSession session, StationSnapshot station, bool destroyBase)
    {
        GameplayCommandRecord cause = session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, cause.Result.Status);
        StationSnapshot current = Assert.Single(session.CaptureSnapshot().Stations);
        return new(new CargoDispositionTrigger.Command(cause.Envelope.Sequence), station.Id, current.Revision,
            station.Modules.Select(module => module.Id).ToArray(), destroyBase, 1);
    }

    internal static StationSnapshot Build(GameSession session)
    {
        Accepted(session, new CreateStationSiteCommand(GameSessionTestFixture.Principal, new(1), BaseKey,
            GameSessionTestFixture.Position(0, 0), new(0)));
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Accepted(session, new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id,
            station.Projects.Single().Id, station.Revision, new(1), new(0)));
        session.AdvanceTo(new(1));
        return BuildStorage(session);
    }

    private static StationSnapshot BuildStorage(GameSession session)
    {
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Accepted(session, new ConstructStationModuleCommand(GameSessionTestFixture.Principal, station.Id, station.Revision, StorageKey));
        station = Assert.Single(session.CaptureSnapshot().Stations);
        Accepted(session, new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id,
            station.Projects.Last(project => project.Kind == StationProjectKind.ModuleConstruction).Id, station.Revision, new(1), new(0)));
        session.AdvanceTo(session.CurrentTime.Add(new(1)));
        return Assert.Single(session.CaptureSnapshot().Stations);
    }

    private static void Accepted(GameSession session, GameplayCommand command) =>
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player, command).Result.Status);

    private static void Store(GameSession session, InventoryId inventory, ulong amount, ulong operation) =>
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, operation),
            inventory, MetalDefinition, new(amount))]);

    internal static GameSession Create(bool recovery = false, ulong baseCapacity = 750, ulong storageCapacity = 250,
        ulong cutoffDenominator = 100)
    {
        var station = new StationDefinition(BaseKey, "Loss Station", 4, new(baseCapacity), new(baseCapacity), new([], new(1)), 100,
            [StorageKey, QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage")], new(1), []);
        var module = new StationModuleDefinition(StorageKey, "Storage", 1, new(storageCapacity), 0, null, new([], new(1)), new(1),
            recovery ? new Dictionary<MaterialId, Quantity> { [Metal] = new(1) } : [], []);
        var zeroStorage = new StationModuleDefinition(QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage"),
            "Zero storage", 1, new(0), 0, null, new([], new(2)), new(1), [], []);
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var storageLoss = new StationStorageLossPolicy(QualifiedContentKey.Create("test.station-loss", "station-storage-loss-policy", "default"), 1, 1, cutoffDenominator);
        var cargoPolicy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.station-loss", "cargo-policy", "default"), 1, 1, 1, 1,
            new Dictionary<CargoLossCause, CargoRecoveryFraction>
            {
                [CargoLossCause.EntityDestruction] = new(1, 2), [CargoLossCause.StorageModuleLoss] = new(1, 2),
                [CargoLossCause.FunctionalEjection] = new(1, 1),
            }, 1, 0, new(60_000), new(1), new(1), new(1_000));
        var ship = new ShipDesign(new(1), "Builder collector", new([], new(1)), new(1_000), GameSessionTestFixture.ManeuverCapability,
            10_000, 5_000, new StationConstructionCapability(500, new(1_000)));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), GameSessionTestFixture.Principal, ship, GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController)], new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player, cargoPolicy: cargoPolicy,
            stations: new StationRuntimeSetup(new([station], [module, zeroStorage]), materials, StationTerritoryPolicy.Absent, storageLossPolicy: storageLoss));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
