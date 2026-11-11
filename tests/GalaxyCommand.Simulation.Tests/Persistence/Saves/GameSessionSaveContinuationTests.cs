using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionSaveContinuationTests
{
    private static readonly SaveJsonLimits Limits = new(16_777_216, 128, 65_536, 100_000);
    private static readonly GameSessionSaveMetadata Metadata = new(
        Guid.Parse("2b080570-673a-4f85-a0ac-18730f43c612"),
        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        "continuation-test");

    [Fact]
    public void EncodedPendingMovementContinuesWithIdenticalFactsAndAdmissionSequences()
    {
        using GameSession original = GameSessionTestFixture.Create(navigation:
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
        AssertAccepted(original.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll)));
        Assert.NotEmpty(Capture(original).Engine.Agenda.PendingEvents);
        using GameSession restored = RoundTrip(original);

        GameSessionTestFixture.AdvanceUntilOrderTerminal(original);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(restored);

        Assert.Equal(original.CurrentTime, restored.CurrentTime);
        Assert.Equal(Assert.Single(original.CaptureSnapshot().Ships).SpatialState,
            Assert.Single(restored.CaptureSnapshot().Ships).SpatialState);
        Assert.Equal(original.ReadFactsAfter(null, 256).Facts, restored.ReadFactsAfter(null, 256).Facts);
        AssertSameEncodedCheckpoint(original, restored);
        GameplayCommand followup = new MoveShipCommand(GameSessionTestFixture.Ship,
            GameSessionTestFixture.Destination(200, 0), OrderPlacement.ReplaceAll);
        Assert.Equal(original.SubmitCommand(GameSessionTestFixture.Player, followup).Envelope,
            restored.SubmitCommand(GameSessionTestFixture.Player, followup).Envelope);
    }

    [Fact]
    public void EncodedEconomyContinuesPendingConstructionAndAllocatesIdenticalShipIdentity()
    {
        using GameSession original = CreateEconomySession();
        original.AdvanceTo(SimulationTime.Zero);
        Assert.NotEmpty(Capture(original).Engine.Agenda.PendingEvents);
        using GameSession restored = RoundTrip(original);

        original.AdvanceTo(new(10_000));
        restored.AdvanceTo(new(10_000));

        Assert.Equal(2, original.CaptureSnapshot().Ships.Count);
        Assert.Equal(original.CaptureSnapshot().Ships.Select(ship => ship.Id),
            restored.CaptureSnapshot().Ships.Select(ship => ship.Id));
        Assert.Equal(original.ReadFactsAfter(null, 256).Facts, restored.ReadFactsAfter(null, 256).Facts);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1100UL)]
    [InlineData(2100UL)]
    public void EncodedProductionTransportAndConstructionContinueTheirCoupledWork(ulong capturedAt)
    {
        using GameSession original = CreateTransportSession();
        original.AdvanceTo(new(capturedAt));
        GameSessionCheckpoint checkpoint = Capture(original);
        Assert.NotNull(checkpoint.Economy);
        Assert.NotEmpty(checkpoint.Engine.Agenda.PendingEvents);
        using GameSession restored = RoundTrip(original);

        original.AdvanceTo(new(10_000));
        restored.AdvanceTo(new(10_000));

        Assert.Equal(2, restored.CaptureSnapshot().Ships.Count);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void EncodedPhysicalInventoryPreservesCurrentCatalogAdmissionAndReplayIdentity()
    {
        using GameSession original = GameSessionTestFixture.Create(navigation:
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
        PhysicalDefinition ore = new(QualifiedContentKey.Create("test.saves", "cargo", "ore"),
            PhysicalHoldingKind.Fungible, new(1));
        var store = new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1),
            GameSessionTestFixture.CargoInventory, ore, new(3));
        InventoryCommitDisposition committed = Assert.Single(original.CommitInventoryMutations([store]).Outcomes);
        Assert.True(committed.WasApplied);
        using GameSession restored = RoundTrip(original, definitions: new PhysicalDefinitionCatalog([ore]));

        InventoryCommitDisposition replay = Assert.Single(restored.CommitInventoryMutations([store]).Outcomes);

        Assert.False(replay.WasApplied);
        Assert.Equal(committed.Outcome, replay.Outcome);
        InventoryCheckpoint inventory = Capture(restored).Lifecycle.Inventories.Inventories.Single();
        Assert.Equal(new Quantity(3), Assert.Single(inventory.FungibleHoldings).Quantity);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    public void EncodedStationLossPreservesPendingCollectionAndItsInventoryCommit(int workers, int batchSize)
    {
        using GameSession original = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(original);
        PhysicalDefinition metal = MetalDefinition();
        InventoryCommitDisposition stored = Assert.Single(original.CommitInventoryMutations([
            new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1),
                station.InventoryId, metal, new(400)),
        ]).Outcomes);
        Assert.True(stored.WasApplied);
        StationDestructionRequest loss = StationDestructionSessionTests.Request(original, station, destroyBase: false);
        Assert.True(original.CommitStationDestruction(loss).WasApplied);
        CargoObjectView recovered = Assert.Single(original.CaptureVisibleCargo());
        AssertAccepted(original.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(1), recovered.OriginalFieldId!.Value, [metal.Key], OrderPlacement.ReplaceAll)));
        GameSessionCheckpoint before = Capture(original);
        Assert.NotNull(before.CargoCollectionAttempts);
        Assert.NotEmpty(before.Engine.Agenda.PendingEvents);
        using GameSession restored = RoundTrip(original, new InteractionBatchExecution(workers, batchSize), new PhysicalDefinitionCatalog([metal]));

        SimulationTime completed = original.CurrentTime.Add(new(1));
        original.AdvanceTo(completed);
        restored.AdvanceTo(completed);

        Assert.Empty(restored.CaptureVisibleCargo());
        Assert.Single(restored.CaptureSnapshot().Stations);
        InventoryCheckpoint cargo = Capture(restored).Lifecycle.Inventories.Inventories.Single(item => item.Id == new InventoryId(1));
        Assert.Equal(new Quantity(50), Assert.Single(cargo.FungibleHoldings).Quantity);
        Assert.False(restored.CommitStationDestruction(loss).WasApplied);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void EncodedRemovedStationRetainsCausalReplayAndPendingCargoExpiry()
    {
        using GameSession original = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(original);
        original.CommitInventoryMutations([
            new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1),
                station.InventoryId, MetalDefinition(), new(400)),
        ]);
        StationDestructionRequest loss = StationDestructionSessionTests.Request(original, station, destroyBase: true);
        Assert.True(original.CommitStationDestruction(loss).WasApplied);
        Assert.Empty(original.CaptureSnapshot().Stations);
        CargoLiveObject cargo = Assert.Single(original.CaptureCargoAuthority());
        using GameSession restored = RoundTrip(original);
        Assert.False(restored.CommitStationDestruction(loss).WasApplied);
        Assert.Empty(restored.CaptureSnapshot().Stations);
        Assert.Equal(new Quantity(200), Assert.Single(restored.CaptureVisibleCargo()).Quantity);

        original.AdvanceTo(cargo.ExpiryEvent.Timestamp);
        restored.AdvanceTo(cargo.ExpiryEvent.Timestamp);

        Assert.Empty(restored.CaptureVisibleCargo());
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void EncodedPendingStationModuleConstructionRetainsAssignmentsAndCompletion()
    {
        using GameSession original = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(original);
        QualifiedContentKey moduleKey = QualifiedContentKey.Create("test.station-loss", "station-module", "zero-storage");
        AssertAccepted(original.SubmitCommand(GameSessionTestFixture.Player,
            new ConstructStationModuleCommand(GameSessionTestFixture.Principal, station.Id, station.Revision, moduleKey)));
        station = Assert.Single(original.CaptureSnapshot().Stations);
        StationProjectSnapshot project = station.Projects.Last(item => item.Kind == StationProjectKind.ModuleConstruction);
        AssertAccepted(original.SubmitCommand(GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, project.Id,
                station.Revision, new(1), new(0))));
        Assert.NotEmpty(Capture(original).Engine.Agenda.PendingEvents);
        using GameSession restored = RoundTrip(original);

        SimulationTime completed = original.CurrentTime.Add(new(2));
        original.AdvanceTo(completed);
        restored.AdvanceTo(completed);

        StationSnapshot resumedStation = Assert.Single(restored.CaptureSnapshot().Stations);
        Assert.Equal(2, resumedStation.Modules.Count);
        Assert.Equal(StationProjectStatus.Completed, resumedStation.Projects.Single(item => item.Id == project.Id).Status);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void EncodedStandingChangePreservesContributionsAndSubsequentRelationshipCommits()
    {
        using GameSession original = GameSessionFogOfWarTests.Create(5000);
        var batch = new StandingChangeBatch(new(StandingChangeSourceKind.Explicit, 1),
            [new StandingChangeProposal(new(2), new(1), new StandingChangeContribution(new(1), -40, StandingChangeReason.Explicit))]);
        Assert.IsType<StandingChangeBatchResult.Applied>(original.CommitStandingChanges(batch));
        StandingChangedFact expected = Assert.IsType<StandingChangedFact>(Assert.Single(original.ReadFactsAfter(null, 256).Facts).Fact);
        Assert.NotEmpty(expected.Contributions);
        using GameSession restored = RoundTrip(original);
        StandingChangedFact actual = Assert.IsType<StandingChangedFact>(Assert.Single(restored.ReadFactsAfter(null, 256).Facts).Fact);
        Assert.Equal(expected.Contributions, actual.Contributions);
        Assert.Equal(expected.ResultingValue, actual.ResultingValue);
        var followup = new StandingChangeBatch(new(StandingChangeSourceKind.Explicit, 2),
            [new StandingChangeProposal(new(2), new(1), new StandingChangeContribution(new(1), 10, StandingChangeReason.Explicit))]);

        Assert.IsType<StandingChangeBatchResult.Applied>(original.CommitStandingChanges(followup));
        Assert.IsType<StandingChangeBatchResult.Applied>(restored.CommitStandingChanges(followup));

        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void EncodedHiddenDeployableRemovalRetainsStaleEvidenceUntilCoverageReturns()
    {
        using GameSession original = DeployableObservationTests.Create(otherX: 5000);
        EntityId satellite = DeployableObservationTests.Deploy(original, 2, 2, 5000);
        MovePlayer(original, 60_000);
        ObservedSensorDeployableSnapshot stale = Assert.Single(DeployableObservationTests.Present(original).World.Deployables);
        Assert.Equal(SensorObservationState.Stale, stale.State);
        original.RemoveEntity(new EntityRemovalRequest(satellite, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        using GameSession restored = RoundTrip(original);
        Assert.Equal(stale, Assert.Single(DeployableObservationTests.Present(restored).World.Deployables));

        MovePlayer(original, 0);
        MovePlayer(restored, 0);

        ObservedSensorDeployableSnapshot absent = Assert.Single(DeployableObservationTests.Present(restored).World.Deployables);
        Assert.Equal(SensorObservationState.ConfirmedMissing, absent.State);
        Assert.Equal(stale.View, absent.View);
        Assert.Equal(DeployableObservationTests.Present(original).Facts.Facts,
            DeployableObservationTests.Present(restored).Facts.Facts);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void EncodedConnectorTransitContinuesIntoTheSameSensorDiscovery()
    {
        using GameSession original = DeployableObservationTests.Create(otherX: 5000, secondSystem: true);
        DeployableObservationTests.Deploy(original, 2, 2, 5000);
        AssertAccepted(original.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), new NavigationDestination.System(new(2)), OrderPlacement.ReplaceAll)));
        Assert.NotNull(original.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).Transit);
        using GameSession restored = RoundTrip(original);

        GameSessionTestFixture.AdvanceUntilOrderTerminal(original);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(restored);

        ObservedSensorDeployableSnapshot sensor = Assert.Single(DeployableObservationTests.Present(restored).World.Deployables);
        Assert.Equal(SensorObservationState.Live, sensor.State);
        Assert.Equal(new SystemId(2), sensor.View.Position.SystemId);
        Assert.Equal(DeployableObservationTests.Present(original).World.Deployables,
            DeployableObservationTests.Present(restored).World.Deployables);
        AssertSameEncodedCheckpoint(original, restored);
    }

    [Fact]
    public void AtomicStoreCommitsTypedCheckpointAndLoadsItsExactContinuation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"galaxy-command-typed-save-{Guid.NewGuid():N}");
        try
        {
            using GameSession original = GameSessionTestFixture.Create(navigation:
                new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
            AssertAccepted(original.SubmitCommand(GameSessionTestFixture.Player,
                new MoveShipCommand(new(1), GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll)));
            CheckpointResult<InertSaveDocument> encoded = GameSessionSaveCodec.Encode(Capture(original), Metadata, Limits);
            Assert.True(encoded.IsSuccess, encoded.Failure?.Path + ": " + encoded.Failure?.Message);
            var store = new SaveFileStore(directory, Limits,
                new SaveMigrationRegistry(SaveFormat.CurrentSchemaVersion, []), new PhysicalSaveFileSystem());
            SaveWriteResult written = store.Commit("campaign", encoded.Value!);
            Assert.True(written.IsSuccess, written.Failure?.Message);
            SaveReadResult loaded = store.Load("campaign", SaveFileSelection.Primary);
            Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
            CheckpointResult<GameSession> admission = GameSessionSaveCodec.Restore(loaded.Document!, Limits);
            Assert.True(admission.IsSuccess, admission.Failure?.Path + ": " + admission.Failure?.Message);
            using GameSession restored = admission.Value!;

            GameSessionTestFixture.AdvanceUntilOrderTerminal(original);
            GameSessionTestFixture.AdvanceUntilOrderTerminal(restored);

            AssertSameEncodedCheckpoint(original, restored);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void MovePlayer(GameSession session, long x)
    {
        AssertAccepted(session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)));
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
    }

    /// <summary>Exercises the production byte codec before isolated typed admission.</summary>
    private static GameSession RoundTrip(GameSession session, InteractionBatchExecution? execution = null,
        PhysicalDefinitionCatalog? definitions = null)
    {
        CheckpointResult<InertSaveDocument> encoded = GameSessionSaveCodec.Encode(Capture(session), Metadata, Limits);
        Assert.True(encoded.IsSuccess, encoded.Failure?.Path + ": " + encoded.Failure?.Message);
        using var bytes = new MemoryStream();
        SaveWriteResult written = SaveJsonCodec.Write(bytes, encoded.Value!, Limits);
        Assert.True(written.IsSuccess, written.Failure?.Message);
        bytes.Position = 0;
        SaveReadResult decoded = SaveJsonCodec.Read(bytes, Limits,
            new SaveMigrationRegistry(SaveFormat.CurrentSchemaVersion, []));
        Assert.True(decoded.IsSuccess, decoded.Failure?.Message);
        CheckpointResult<GameSession> restored = GameSessionSaveCodec.Restore(decoded.Document!, Limits, definitions: definitions, interactionExecution: execution);
        Assert.True(restored.IsSuccess, restored.Failure?.Path + ": " + restored.Failure?.Message);
        return restored.Value!;
    }

    /// <summary>Compares every encoded owner section after continued execution.</summary>
    private static void AssertSameEncodedCheckpoint(GameSession expected, GameSession actual)
    {
        CheckpointResult<InertSaveDocument> first = GameSessionSaveCodec.Encode(Capture(expected), Metadata, Limits);
        CheckpointResult<InertSaveDocument> second = GameSessionSaveCodec.Encode(Capture(actual), Metadata, Limits);
        Assert.True(first.IsSuccess, first.Failure?.Message);
        Assert.True(second.IsSuccess, second.Failure?.Message);
        Assert.Equal(first.Value!.Root.GetRawText(), second.Value!.Root.GetRawText());
    }

    private static GameSessionCheckpoint Capture(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> result = session.CaptureCheckpoint();
        Assert.True(result.IsSuccess, result.Failure?.Message);
        return result.Value!;
    }

    private static PhysicalDefinition MetalDefinition() => new(
        QualifiedContentKey.Create("test.station-loss", "material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    private static void AssertAccepted(GameplayCommandRecord record) =>
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);

    private static GameSession CreateTransportSession()
    {
        MaterialId material = new(1);
        var design = new ShipDesign(new(2), "Transport-built ship",
            new([new KeyValuePair<MaterialId, Quantity>(material, new(1))], new(1)),
            new(10), GameSessionTestFixture.ManeuverCapability, 10_000, 5_000);
        var navigation = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        var locations = new Dictionary<LocationId, SystemPosition>
        {
            [new(1)] = GameSessionTestFixture.Position(0, 0),
            [new(2)] = GameSessionTestFixture.Position(0, 0),
        };
        var economy = new GameSessionEconomySetup(
            [new InitialInventorySetup(new(2), new(20), []), new InitialInventorySetup(new(3), new(20), [])],
            [new EconomyFacilitySetup(new(1), new(2), new(1), GameSessionTestFixture.Position(0, 0)),
             new EconomyFacilitySetup(new(2), new(3), new(2), GameSessionTestFixture.Position(0, 0))],
            [new ProductionFacilitySetup(new(1), new Recipe([], material, new(1), new(1)), new(1), Repeat: false)],
            [new ConstructionFacilitySetup(new(2), new(1))],
            [design], [new InitialConstructionOrderSetup(new(2), design.Id)],
            [new InitialFreighterSetup(GameSessionTestFixture.Ship, new(2))],
            new HierarchicalLogisticsNavigation(locations, navigation),
            new TransportTiming(SimulationDuration.Zero, new(1), new(1)));
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "System")],
            [new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(new(2), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder, [design])],
            economy, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed,
            256, GameSessionTestFixture.Player);
        return new(setup, navigation);
    }

    private static GameSession CreateEconomySession()
    {
        var navigation = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100));
        LocationId location = new(1);
        FacilityId facility = new(1);
        SystemPosition position = GameSessionTestFixture.Position(0, 0);
        var economy = new GameSessionEconomySetup(
            [new InitialInventorySetup(new(2), new(20), [])],
            [new EconomyFacilitySetup(facility, new(2), location, position)],
            [],
            [new ConstructionFacilitySetup(facility, new(1))],
            [GameSessionTestFixture.Design],
            [new InitialConstructionOrderSetup(facility, GameSessionTestFixture.Design.Id)],
            [new InitialFreighterSetup(GameSessionTestFixture.Ship, location)],
            new HierarchicalLogisticsNavigation(new Dictionary<LocationId, SystemPosition>
            {
                [location] = position,
            }, navigation),
            new TransportTiming(SimulationDuration.Zero, new(1), new(1)));
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, position, GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(facility, GameSessionTestFixture.Principal, position,
                GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder,
                [GameSessionTestFixture.Design])],
            economy, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed,
            256, GameSessionTestFixture.Player);
        return new GameSession(setup, navigation);
    }
}
