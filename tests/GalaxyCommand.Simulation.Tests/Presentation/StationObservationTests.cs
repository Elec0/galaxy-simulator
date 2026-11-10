using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationObservationTests
{
    private static readonly PrincipalId Other = new(2);
    private static readonly CommandSource OtherSource = new(CommandSourceKind.Autonomous, new("station-other"));
    private static readonly MaterialId Metal = new(1);
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.stations", "station", "observed");
    private static readonly QualifiedContentKey SensorModuleKey = QualifiedContentKey.Create("test.stations", "station-module", "sensor");
    private static readonly PhysicalDefinition MetalDefinition = new(
        QualifiedContentKey.Create("test.stations", "material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void HiddenStationIsNotDisclosedAndOwnedSiteIsAlwaysCurrent()
    {
        using GameSession session = Create(1_000);
        CreateSite(session, other: true);
        Assert.Empty(Presentation(session).World.Stations);
        Assert.DoesNotContain(Presentation(session).Facts.Facts, fact => fact.Fact is ObservedStationChangeFact);

        CreateSite(session, other: false);
        ObservedStationSnapshot owned = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(GameSessionTestFixture.Principal, owned.View.PrincipalId);
        Assert.Equal(SensorObservationState.Live, owned.State);
        Assert.Equal(StationPhase.Site, owned.View.Phase);
        Assert.Equal("Observed Station", owned.View.Name);
        Assert.Empty(session.CaptureStationObservation().Sources);
    }

    [Fact]
    public void HiddenConstructionDoesNotRefreshRetainedExteriorAndRestorePreservesStaleEvidence()
    {
        using GameSession session = Create(1_000);
        CreateSite(session, other: true);
        MovePlayer(session, 950);
        ObservedStationSnapshot discovered = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Live, discovered.State);
        Assert.Equal(StationPhase.Site, discovered.View.Phase);
        MovePlayer(session, 0);
        Build(session, other: true);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 5_000));

        ObservedStationSnapshot stale = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Stale, stale.State);
        Assert.Equal(StationPhase.Site, stale.View.Phase);
        Assert.True(stale.View.ObservedAt < session.CurrentTime);
        Assert.DoesNotContain(Presentation(session).Facts.Facts, fact =>
            fact.Fact is StationProjectCompletedFact or StationProjectChangedFact
            || fact.Fact is ObservedStationChangeFact { Kind: ObservedStationChangeKind.Changed });

        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        using GameSession restored = restore.Value!;
        Assert.Equal(stale, Assert.Single(Presentation(restored).World.Stations));
        MovePlayer(restored, 950);
        ObservedStationSnapshot refreshed = Assert.Single(Presentation(restored).World.Stations);
        Assert.Equal(SensorObservationState.Live, refreshed.State);
        Assert.Equal(StationPhase.Operational, refreshed.View.Phase);
        Assert.Equal(restored.CurrentTime, refreshed.View.ObservedAt);
    }

    [Fact]
    public void MovingSensorDiscoversStationDuringPassThroughWithoutStopping()
    {
        using GameSession session = Create(1_000);
        CreateSite(session, other: true);
        MovePlayer(session, 2_000);

        ObservedStationSnapshot passed = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Stale, passed.State);
        Assert.True(passed.View.ObservedAt > new SimulationTime(0));
        Assert.True(passed.View.ObservedAt < session.CurrentTime);
    }

    [Fact]
    public void CompletedOwnedStationSuppliesCoverageAfterItsBuilderLeaves()
    {
        using GameSession session = Create(200);
        CreateSite(session, other: false);
        Assert.DoesNotContain(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));
        Build(session, other: false);
        session.AdvanceTo(new(5_000));
        StationSensorSourceSnapshot source = Assert.Single(session.CaptureStationObservation().Sources);
        Assert.Equal(300UL, source.RadiusUnits);
        MovePlayer(session, 1_000);

        Assert.Contains(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));
        ObservedStationSnapshot owned = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Live, owned.State);
        Assert.Equal(session.CurrentTime, owned.View.ObservedAt);
        Assert.Contains(owned.View.Services, service => service.Type == StationServiceType.Sensors && service.IsAvailable);
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("future")]
    [InlineData("phase")]
    [InlineData("name")]
    [InlineData("duplicate")]
    public void RestoreRejectsMissingOrContradictoryLiveExteriorEvidence(string fault)
    {
        using GameSession session = Create(1_000);
        CreateSite(session, other: true);
        MovePlayer(session, 950);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        ObservedStationSnapshot seen = Assert.Single(checkpoint.PlayerObservation!.Stations!);
        ObservedStationSnapshot changed = fault switch
        {
            "future" => seen with { View = seen.View with { ObservedAt = new(session.CurrentTime.Milliseconds + 1) } },
            "phase" => seen with { View = seen.View with { Phase = StationPhase.Operational } },
            "name" => seen with { View = seen.View with { Name = "Fabricated exterior" } },
            _ => seen,
        };
        IReadOnlyList<ObservedStationSnapshot> observations = fault switch
        {
            "omitted" => [], "duplicate" => [seen, seen], _ => [changed],
        };
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint with
        { PlayerObservation = checkpoint.PlayerObservation with { Stations = observations } });

        Assert.False(restored.IsSuccess);
        Assert.Equal("$.checkpoint.playerObservation", restored.Failure!.Path);
    }

    [Fact]
    public void SensorModuleUsesMaximumRadiusAndDisablingWithdrawsOnlyItsContribution()
    {
        using GameSession session = Create(500, includeSensorModule: true);
        CreateSite(session, other: false);
        Build(session, other: false);
        session.AdvanceTo(new(5_000));
        StationModuleSnapshot module = BuildSensorModule(session, other: false);
        Assert.Equal(600UL, Assert.Single(session.CaptureStationObservation().Sources).RadiusUnits);
        MovePlayer(session, 1_000);
        Assert.Contains(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));

        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new SetStationModuleEnabledCommand(station.PrincipalId, station.Id, station.Revision, module.Id, false)).Result.Status);
        Assert.Equal(300UL, Assert.Single(session.CaptureStationObservation().Sources).RadiusUnits);
        Assert.DoesNotContain(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));
        StationModuleExteriorSnapshot visible = Assert.Single(Assert.Single(Presentation(session).World.Stations).View.Modules);
        Assert.Equal(module.Id, visible.Handle);
        Assert.Equal(SensorModuleKey, visible.DefinitionKey);
        Assert.False(visible.Enabled);
        Assert.Equal(StationModulePhase.Operational, visible.Phase);
    }

    [Fact]
    public void HiddenModuleChangesRetainPriorExteriorConditionAcrossRestore()
    {
        using GameSession session = Create(1_000, includeSensorModule: true);
        CreateSite(session, other: true);
        Build(session, other: true);
        session.AdvanceTo(new(5_000));
        StationModuleSnapshot module = BuildSensorModule(session, other: true);
        MovePlayer(session, 950);
        Assert.True(Assert.Single(Assert.Single(Presentation(session).World.Stations).View.Modules).Enabled);
        MovePlayer(session, 0);
        ObservedStationSnapshot prior = Assert.Single(Presentation(session).World.Stations);
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new SetStationModuleEnabledCommand(Other, station.Id, station.Revision, module.Id, false)).Result.Status);

        ObservedStationSnapshot retained = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Stale, retained.State);
        Assert.Equal(prior.View.ObservedAt, retained.View.ObservedAt);
        Assert.True(Assert.Single(retained.View.Modules).Enabled);
        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        using GameSession restored = restore.Value!;
        Assert.True(Assert.Single(Assert.Single(Presentation(restored).World.Stations).View.Modules).Enabled);
        MovePlayer(restored, 950);
        Assert.False(Assert.Single(Assert.Single(Presentation(restored).World.Stations).View.Modules).Enabled);
    }

    [Fact]
    public void HiddenRemovalStaysStaleUntilCoverageConfirmsAbsenceAndSurvivesRestore()
    {
        using GameSession session = Create(1_000);
        CreateSite(session, other: true);
        MovePlayer(session, 950);
        MovePlayer(session, 0);
        ObservedStationSnapshot remembered = Assert.Single(Presentation(session).World.Stations);
        DismantleEmptySite(session, other: true);

        ObservedStationSnapshot stale = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Stale, stale.State);
        Assert.Equal(remembered.View, stale.View);
        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        using GameSession restored = restore.Value!;
        MovePlayer(restored, 950);

        ObservedStationSnapshot missing = Assert.Single(Presentation(restored).World.Stations);
        Assert.Equal(SensorObservationState.ConfirmedMissing, missing.State);
        Assert.Equal(remembered.View, missing.View);
        Assert.DoesNotContain(Presentation(restored).Facts.Facts,
            fact => fact.Fact is ObservedStationChangeFact { Kind: ObservedStationChangeKind.Disappeared });
        CheckpointResult<GameSession> missingRestore = GameSession.RestoreCheckpoint(restored.CaptureCheckpoint().Value!);
        Assert.True(missingRestore.IsSuccess, missingRestore.Failure?.Message);
        missingRestore.Value!.Dispose();
    }

    [Fact]
    public void CoveredRemovalPublishesOnlySafeDisappearanceAndRetainsLastObservedTime()
    {
        using GameSession session = Create(200);
        CreateSite(session, other: true);
        MovePlayer(session, 150);
        DismantleEmptySite(session, other: true);

        ObservedStationSnapshot missing = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.ConfirmedMissing, missing.State);
        Assert.True(missing.View.ObservedAt < session.CurrentTime);
        Assert.DoesNotContain(Presentation(session).Facts.Facts, fact => fact.Fact is StationRemovedFact);
        Assert.Single(Presentation(session).Facts.Facts,
            fact => fact.Fact is ObservedStationChangeFact { Kind: ObservedStationChangeKind.Disappeared });
        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        restore.Value!.Dispose();
    }

    [Fact]
    public void HiddenModuleThenStationRemovalPreservesOlderModuleExteriorAcrossRestore()
    {
        using GameSession session = Create(1_000, includeSensorModule: true);
        CreateSite(session, other: true);
        Build(session, other: true);
        session.AdvanceTo(new(5_000));
        StationModuleSnapshot module = BuildSensorModule(session, other: true);
        MovePlayer(session, 950);
        MovePlayer(session, 0);
        ObservedStationSnapshot remembered = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(module.Id, Assert.Single(remembered.View.Modules).Handle);

        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new DismantleStationModuleCommand(Other, station.Id, station.Revision, module.Id, new(2))).Result.Status);
        AdvanceOtherDismantling(session, module.Id, 500);
        station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Empty(station.Modules);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new DismantleStationBaseCommand(Other, station.Id, station.Revision, new(2))).Result.Status);
        AdvanceOtherDismantling(session, null, 2_500);
        Assert.Empty(session.CaptureSnapshot().Stations);

        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        using GameSession restored = restore.Value!;
        ObservedStationSnapshot stale = Assert.Single(Presentation(restored).World.Stations);
        Assert.Equal(SensorObservationState.Stale, stale.State);
        Assert.Equal(remembered.View, stale.View);
        MovePlayer(restored, 950);
        Assert.Equal(SensorObservationState.ConfirmedMissing, Assert.Single(Presentation(restored).World.Stations).State);
        CheckpointResult<GameSession> missingRestore = GameSession.RestoreCheckpoint(restored.CaptureCheckpoint().Value!);
        Assert.True(missingRestore.IsSuccess, missingRestore.Failure?.Message);
        missingRestore.Value!.Dispose();
    }

    [Fact]
    public void RemovedStationRejectsSubstitutedKnownModuleDefinition()
    {
        using GameSession session = Create(1_000, includeSensorModule: true, includeAlternateModule: true);
        CreateSite(session, other: true);
        Build(session, other: true);
        session.AdvanceTo(new(5_000));
        StationModuleSnapshot module = BuildSensorModule(session, other: true);
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new DismantleStationModuleCommand(Other, station.Id, station.Revision, module.Id, new(2))).Result.Status);
        AdvanceOtherDismantling(session, module.Id, 500);
        station = Assert.Single(session.CaptureSnapshot().Stations);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new DismantleStationBaseCommand(Other, station.Id, station.Revision, new(2))).Result.Status);
        AdvanceOtherDismantling(session, null, 2_500);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        StationRemovalReceipt removal = Assert.Single(checkpoint.Stations!.Removals);
        var alternate = QualifiedContentKey.Create("test.stations", "station-module", "alternate-sensor");
        StationSnapshot substituted = removal.Station with
        {
            Projects = removal.Station.Projects.Select(project => project.Kind == StationProjectKind.ModuleConstruction
                ? project with { ModuleDefinitionKey = alternate } : project).ToArray(),
        };
        // Validate the station owner directly, so unrelated retained presentation evidence cannot mask an admission gap.
        var lifecycleState = new EntityLifecycleCheckpoint(checkpoint.Lifecycle.EntityIds, new(1), checkpoint.Lifecycle.InventoryIds,
            checkpoint.Lifecycle.Inventories, [], [], []);
        CheckpointResult<EntityLifecycleOwner> lifecycle = EntityLifecycleOwner.RestoreCheckpoint(lifecycleState,
            new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), [],
            new PhysicalDefinitionCatalog([MetalDefinition]), checkpoint.Stations.Setup.Materials);
        Assert.True(lifecycle.IsSuccess, lifecycle.Failure?.Message);
        EventAgenda<GameEvent> agenda = EventAgenda<GameEvent>.RestoreCheckpoint(checkpoint.Engine.Agenda).Value!;
        CheckpointResult<StationRuntimeOwner> malformed = StationRuntimeOwner.Restore(
            checkpoint.Stations with { Removals = [removal with { Station = substituted }] }, lifecycle.Value!, agenda,
            (_, _) => null, agenda.CurrentTime, [GameSessionTestFixture.System], [GameSessionTestFixture.Principal, Other]);
        Assert.False(malformed.IsSuccess);
    }

    private static GamePresentationSnapshot Presentation(GameSession session) => session.CapturePresentation(
        new(GameSessionTestFixture.Principal, [], null, null, 256));

    private static void CreateSite(GameSession session, bool other)
    {
        ShipId builder = new(other ? 2UL : 1UL);
        GameShipSnapshot ship = session.CaptureSnapshot().Ships.Single(item => item.Id == builder);
        GameplayCommandRecord created = session.SubmitCommand(other ? OtherSource : GameSessionTestFixture.Player,
            new CreateStationSiteCommand(other ? Other : GameSessionTestFixture.Principal, builder,
                BaseKey, ship.Position!.Value, new(0)));
        Assert.Equal(CommandResultStatus.Accepted, created.Result.Status);
    }

    private static void Build(GameSession session, bool other)
    {
        PrincipalId principal = other ? Other : GameSessionTestFixture.Principal;
        StationSnapshot station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        StationProjectSnapshot project = Assert.Single(station.Projects);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(other ? OtherSource : GameSessionTestFixture.Player,
            new AssignStationBuilderCommand(principal, station.Id, project.Id, station.Revision,
                new(other ? 2UL : 1UL), new(0))).Result.Status);
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(
            new(InventoryOperationSourceKind.Explicit, 1), station.InventoryId, MetalDefinition, new(4))]);
    }

    private static StationModuleSnapshot BuildSensorModule(GameSession session, bool other)
    {
        PrincipalId principal = other ? Other : GameSessionTestFixture.Principal;
        CommandSource source = other ? OtherSource : GameSessionTestFixture.Player;
        StationSnapshot station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(source,
            new ConstructStationModuleCommand(principal, station.Id, station.Revision, SensorModuleKey)).Result.Status);
        station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        StationProjectSnapshot project = station.Projects.Single(item => item.ModuleId == module.Id);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(source,
            new AssignStationBuilderCommand(principal, station.Id, project.Id, station.Revision,
                new(other ? 2UL : 1UL), new(0))).Result.Status);
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(
            new(InventoryOperationSourceKind.Explicit, 2), station.InventoryId, MetalDefinition, new(1))]);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 500));
        return session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal).Modules.Single();
    }

    private static void DismantleEmptySite(GameSession session, bool other)
    {
        PrincipalId principal = other ? Other : GameSessionTestFixture.Principal;
        CommandSource source = other ? OtherSource : GameSessionTestFixture.Player;
        StationSnapshot station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(source,
            new ControlStationProjectCommand(principal, station.Id, station.Projects.Single().Id,
                station.Revision, StationProjectAction.Cancel)).Result.Status);
        station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(source,
            new DismantleStationBaseCommand(principal, station.Id, station.Revision, new(other ? 2UL : 1UL))).Result.Status);
        station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        StationProjectSnapshot dismantling = station.Projects.Single(project => project.Kind == StationProjectKind.Dismantling);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(source,
            new AssignStationBuilderCommand(principal, station.Id, dismantling.Id, station.Revision,
                new(other ? 2UL : 1UL), new(0))).Result.Status);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 2_500));
        Assert.Empty(session.CaptureSnapshot().Stations);
    }

    private static void AdvanceOtherDismantling(GameSession session, StationModuleId? module, ulong duration)
    {
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        StationProjectSnapshot project = station.Projects.Single(item => item.Kind == StationProjectKind.Dismantling
            && item.ModuleId == module && item.Status != StationProjectStatus.Completed);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new AssignStationBuilderCommand(Other, station.Id, project.Id, station.Revision, new(2), new(0))).Result.Status);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + duration));
    }

    private static void MovePlayer(GameSession session, long x)
    {
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)).Result.Status);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
    }

    private static GameSession Create(long otherX, bool includeSensorModule = false, bool includeAlternateModule = false)
    {
        var recipe = new ConstructionRecipe(new Dictionary<MaterialId, Quantity> { [Metal] = new(4) }, new(10));
        var definition = new StationDefinition(BaseKey, "Observed Station", 4, new(4), new(4), recipe,
            300, includeAlternateModule ? [SensorModuleKey, QualifiedContentKey.Create("test.stations", "station-module", "alternate-sensor")]
                : includeSensorModule ? [SensorModuleKey] : [], new(5), new Dictionary<MaterialId, Quantity> { [Metal] = new(2) });
        StationModuleDefinition[] modules = includeSensorModule ? [new(SensorModuleKey, "Sensor", 1,
            new(0), 600, null, new(new Dictionary<MaterialId, Quantity> { [Metal] = new(1) }, new(1)),
            new(1), [], [])] : [];
        if (includeAlternateModule)
            modules = modules.Append(new StationModuleDefinition(QualifiedContentKey.Create("test.stations", "station-module", "alternate-sensor"),
                "Alternate Sensor", 1, new(0), 600, null,
                new(new Dictionary<MaterialId, Quantity> { [Metal] = new(1) }, new(1)), new(1), [], [])).ToArray();
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var playerDesign = new ShipDesign(new(1), "Player Builder", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, 100, 100, new StationConstructionCapability(100, new(2)));
        var otherDesign = new ShipDesign(new(2), "Other Builder", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, 0, 100, new StationConstructionCapability(100, new(2)));
        var relationships = new RelationshipSetup([
            new(GameSessionTestFixture.Principal, new("player"), "Player"), new(Other, new("other"), "Other")],
            GameSessionTestFixture.Principal, GameSessionTestFixture.StandingPolicy, []);
        var setup = new GameSessionSetup([new StarSystem(GameSessionTestFixture.System, "System")], [
            new(new(1), new(1), new(1), GameSessionTestFixture.Principal, playerDesign, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
            new(new(2), new(2), new(2), Other, otherDesign, GameSessionTestFixture.Position(otherX, 0), new(ActorControllerKind.Autonomous, OtherSource.Id)),
        ], new ConnectorTopology([], []), [], null, relationships, GameSessionTestFixture.RootSeed, 256,
            GameSessionTestFixture.Player, stations: new(new([definition], modules), materials, StationTerritoryPolicy.Absent,
                [new(OtherSource, Other)]));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
