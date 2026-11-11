using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class FullDomainObservationTests
{
    private static readonly PrincipalId Player = new(1);
    private static readonly PrincipalId Other = new(2);
    private static readonly CommandSource OtherSource = new(CommandSourceKind.Autonomous, new("full-observation-other"));
    private static readonly MaterialId Metal = new(1);
    private static readonly PhysicalDefinition MetalDefinition = new(
        QualifiedContentKey.Create("test.observation", "material", "metal"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly QualifiedContentKey StationKey = QualifiedContentKey.Create("test.observation", "station", "base");
    private static readonly SensorDeployableDefinition Sensor = new(
        QualifiedContentKey.Create("test.observation", "sensor-deployable", "satellite"), "Satellite", new(1), 1_000, 300);
    private static readonly SaveJsonLimits Limits = new(16_777_216, 128, 65_536, 100_000);
    private static readonly GameSessionSaveMetadata Metadata = new(
        Guid.Parse("baed6f9d-a222-4919-bd88-c7385884038d"),
        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), "full-observation");

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 3)]
    [InlineData(4, 3, 7)]
    public void AllSourceKindsWithdrawIndependentlyAndEncodedHistoryMatchesSerialReference(int workers, int batch, int partitions)
    {
        using GameSession reference = Create(null);
        using GameSession actual = Create(new(workers, batch));
        RunSourceWithdrawal(reference, 1);
        RunSourceWithdrawal(actual, partitions);
        Assert.Equal(Encode(reference).Root.GetRawText(), Encode(actual).Root.GetRawText());
        using GameSession restored = RoundTrip(actual, new(workers, batch));
        Assert.Equal(Encode(actual).Root.GetRawText(), Encode(restored).Root.GetRawText());
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 5)]
    [InlineData(4, 1, 11)]
    public void StationAndDeployableRefreshAndHiddenAbsenceContinueThroughEncodedRestore(int workers, int batch, int partitions)
    {
        using GameSession reference = Create(null);
        using GameSession original = Create(new(workers, batch));
        PrepareStaleTargets(reference, 1);
        PrepareStaleTargets(original, partitions);
        using GameSession restored = RoundTrip(original, new(workers, batch));
        Assert.Equal(Encode(reference).Root.GetRawText(), Encode(restored).Root.GetRawText());

        foreach (GameSession session in new[] { reference, original, restored })
        {
            MovePlayer(session, 200, session == reference ? 1 : partitions);
            AssertPersistentState(session, SensorObservationState.Live);
            ObservedStationSnapshot refreshed = OtherStation(session);
            Assert.Equal(StationPhase.Operational, refreshed.View.Phase);
            Assert.Equal(session.CurrentTime, refreshed.View.ObservedAt);
            MovePlayer(session, 1_000, session == reference ? 1 : partitions);
            AssertPersistentState(session, SensorObservationState.Stale);
            GameFactSequence cursor = session.ReadFactsAfter(null, 256).Facts[^1].Sequence;
            EntityId sensor = session.CaptureSnapshot().DeployedSensors.Single().EntityId;
            Assert.IsType<EntityRemovalResult.SensorRemoved>(session.RemoveEntity(new(sensor,
                EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo)));
            Dismantle(session, Other, session == reference ? 1 : partitions);
            AssertPersistentState(session, SensorObservationState.Stale);
            Assert.Empty(Present(session, cursor).Facts.Facts);
        }
        using GameSession hiddenRestored = RoundTrip(restored, new(workers, batch));
        foreach (GameSession session in new[] { reference, original, restored, hiddenRestored })
        {
            ObservedStationSnapshot station = OtherStation(session);
            ObservedSensorDeployableSnapshot sensor = Assert.Single(Present(session).World.Deployables);
            MovePlayer(session, 200, session == reference ? 1 : partitions);
            AssertPersistentState(session, SensorObservationState.ConfirmedMissing);
            Assert.Equal(station.View, OtherStation(session).View);
            Assert.Equal(sensor.View, Assert.Single(Present(session).World.Deployables).View);
            Assert.DoesNotContain(Present(session).Facts.Facts, fact => fact.Fact is ObservedSensorChangeFact
                { Kind: ObservedSensorChangeKind.Disappeared } or ObservedStationChangeFact
                { Kind: ObservedStationChangeKind.Disappeared });
        }
        string expected = Encode(reference).Root.GetRawText();
        Assert.Equal(expected, Encode(original).Root.GetRawText());
        Assert.Equal(expected, Encode(restored).Root.GetRawText());
        Assert.Equal(expected, Encode(hiddenRestored).Root.GetRawText());
        using GameSession missingRestored = RoundTrip(hiddenRestored, new(workers, batch));
        AssertPersistentState(missingRestored, SensorObservationState.ConfirmedMissing);
        Assert.Equal(expected, Encode(missingRestored).Root.GetRawText());
    }

    /// <summary>Withdraws each source while another still covers both persistent kinds and the transient ship.</summary>
    private static void RunSourceWithdrawal(GameSession session, int partitions)
    {
        BuildStation(session, Player, partitions);
        CreateSite(session, Other);
        EntityId foreignSensor = Deploy(session, Other);
        EntityId ownedSensor = Deploy(session, Player);
        MovePlayer(session, 200, partitions);
        AssertPersistentState(session, SensorObservationState.Live);
        Assert.Contains(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        MovePlayer(session, 1_000, partitions);
        AssertPersistentState(session, SensorObservationState.Live);
        Assert.Contains(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        MovePlayer(session, 0, partitions);
        Dismantle(session, Player, partitions);
        AssertPersistentState(session, SensorObservationState.Live);
        Assert.Contains(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        GamePresentationFactEnvelope[] disclosed = Present(session).Facts.Facts
            .Where(fact => fact.Fact is ObservedStationChangeFact or ObservedSensorChangeFact).ToArray();
        Assert.NotEmpty(disclosed);
        Assert.DoesNotContain(Present(session).Facts.Facts, fact => fact.Fact is StationCreatedFact created && created.PrincipalId == Other);
        Assert.DoesNotContain(Present(session).Facts.Facts, fact => fact.Fact is SensorDeployedFact deployed && deployed.Sensor.PrincipalId == Other);
        Assert.IsType<EntityRemovalResult.SensorRemoved>(session.RemoveEntity(new(ownedSensor,
            EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo)));
        AssertPersistentState(session, SensorObservationState.Stale);
        Assert.DoesNotContain(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        Assert.Contains(Present(session).World.Deployables, sensor => sensor.View.EntityId == foreignSensor);
        foreach (GamePresentationFactEnvelope notice in disclosed) Assert.Contains(notice, Present(session).Facts.Facts);
    }

    /// <summary>Creates both persistent kinds in coverage, then changes the foreign station only while hidden.</summary>
    private static void PrepareStaleTargets(GameSession session, int partitions)
    {
        MovePlayer(session, 200, partitions);
        CreateSite(session, Other);
        Deploy(session, Other);
        AssertPersistentState(session, SensorObservationState.Live);
        MovePlayer(session, 1_000, partitions);
        BuildStation(session, Other, partitions, createSite: false);
        AssertPersistentState(session, SensorObservationState.Stale);
        Assert.Equal(StationPhase.Site, OtherStation(session).View.Phase);
        Assert.DoesNotContain(Present(session).Facts.Facts, fact => fact.Fact is ObservedStationChangeFact
            { Kind: ObservedStationChangeKind.Changed });
    }

    private static void AssertPersistentState(GameSession session, SensorObservationState state)
    {
        Assert.Equal(state, OtherStation(session).State);
        Assert.Equal(state, Present(session).World.Deployables.Single(item => item.View.PrincipalId == Other).State);
    }

    private static ObservedStationSnapshot OtherStation(GameSession session) =>
        Present(session).World.Stations.Single(item => item.View.PrincipalId == Other);

    private static GamePresentationSnapshot Present(GameSession session, GameFactSequence? cursor = null) =>
        session.CapturePresentation(new(Player, [], null, cursor, 256));

    private static CommandSource Source(PrincipalId principal) => principal == Player ? GameSessionTestFixture.Player : OtherSource;

    private static ShipId Builder(PrincipalId principal) => new(principal.Value);

    private static void CreateSite(GameSession session, PrincipalId principal)
    {
        ShipId builder = Builder(principal);
        GameShipSnapshot ship = session.CaptureSnapshot().Ships.Single(item => item.Id == builder);
        AssertAccepted(session.SubmitCommand(Source(principal), new CreateStationSiteCommand(principal, builder,
            StationKey, ship.Position!.Value, new(0))));
    }

    /// <summary>Uses normal construction and explicit material admission rather than fabricated station observations.</summary>
    private static void BuildStation(GameSession session, PrincipalId principal, int partitions, bool createSite = true)
    {
        if (createSite) CreateSite(session, principal);
        StationSnapshot station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        StationProjectSnapshot project = Assert.Single(station.Projects);
        AssertAccepted(session.SubmitCommand(Source(principal), new AssignStationBuilderCommand(principal,
            station.Id, project.Id, station.Revision, Builder(principal), new(0))));
        Assert.True(Assert.Single(session.CommitInventoryMutations([new StoreFungibleInventoryProposal(
            new(InventoryOperationSourceKind.Explicit, principal.Value), station.InventoryId, MetalDefinition, new(1))]).Outcomes).WasApplied);
        Advance(session, new(session.CurrentTime.Milliseconds + 1), partitions);
        Assert.Equal(StationPhase.Operational, session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal).Phase);
    }

    private static void Dismantle(GameSession session, PrincipalId principal, int partitions)
    {
        StationSnapshot station = session.CaptureSnapshot().Stations.Single(item => item.PrincipalId == principal);
        AssertAccepted(session.SubmitCommand(Source(principal), new DismantleStationBaseCommand(principal,
            station.Id, station.Revision, new(principal.Value))));
        station = session.CaptureSnapshot().Stations.Single(item => item.Id == station.Id);
        StationProjectSnapshot project = station.Projects.Single(item => item.Kind == StationProjectKind.Dismantling);
        AssertAccepted(session.SubmitCommand(Source(principal), new AssignStationBuilderCommand(principal,
            station.Id, project.Id, station.Revision, Builder(principal), new(0))));
        Advance(session, new(session.CurrentTime.Milliseconds + 1), partitions);
        Assert.DoesNotContain(session.CaptureSnapshot().Stations, item => item.Id == station.Id);
    }

    private static EntityId Deploy(GameSession session, PrincipalId principal)
    {
        ShipId builder = Builder(principal);
        SystemPosition position = session.CaptureSnapshot().Ships.Single(item => item.Id == builder).Position!.Value;
        AssertAccepted(session.SubmitCommand(Source(principal), new DeploySensorCommand(builder,
            new(principal.Value), position, OrderPlacement.ReplaceAll)));
        session.AdvanceTo(session.CurrentTime);
        return session.CaptureSnapshot().DeployedSensors.Single(item => item.PrincipalId == principal).EntityId;
    }

    /// <summary>Partitions every physical boundary without adding commands or observation reads between partitions.</summary>
    private static void MovePlayer(GameSession session, long x, int partitions)
    {
        AssertAccepted(session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)));
        for (int guard = 0; guard < 64; guard++)
        {
            GameShipSnapshot ship = session.CaptureSnapshot().Ships.Single(item => item.Id == new ShipId(1));
            if (ship.CurrentOrder?.Status is not (ShipOrderStatus.Active or ShipOrderStatus.Waiting)) return;
            SimulationTime boundary = ship.SpatialState switch
            {
                ShipSpatialSnapshotState.AnalyticManeuver analytic => analytic.Maneuver.NextBoundary!.Timestamp,
                ShipSpatialSnapshotState.LocalMotion local => local.Motion.ArrivesAt,
                _ => throw new InvalidOperationException("Expected a bounded local movement."),
            };
            Advance(session, boundary, partitions);
        }
        throw new InvalidOperationException("Movement did not terminate within its fixture boundary limit.");
    }

    private static void Advance(GameSession session, SimulationTime target, int partitions)
    {
        ulong start = session.CurrentTime.Milliseconds;
        ulong duration = target.Milliseconds - start;
        for (ulong index = 1; index <= (ulong)partitions; index++)
            session.AdvanceTo(new(start + duration * index / (ulong)partitions));
    }

    private static void AssertAccepted(GameplayCommandRecord command) =>
        Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);

    private static InertSaveDocument Encode(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> checkpoint = session.CaptureCheckpoint();
        Assert.True(checkpoint.IsSuccess, checkpoint.Failure?.Message);
        CheckpointResult<InertSaveDocument> encoded = GameSessionSaveCodec.Encode(checkpoint.Value!, Metadata, Limits);
        Assert.True(encoded.IsSuccess, encoded.Failure?.Message);
        return encoded.Value!;
    }

    private static GameSession RoundTrip(GameSession session, InteractionBatchExecution execution)
    {
        using var bytes = new MemoryStream();
        SaveWriteResult written = SaveJsonCodec.Write(bytes, Encode(session), Limits);
        Assert.True(written.IsSuccess, written.Failure?.Message);
        bytes.Position = 0;
        SaveReadResult decoded = SaveJsonCodec.Read(bytes, Limits, new SaveMigrationRegistry(SaveFormat.CurrentSchemaVersion, []));
        Assert.True(decoded.IsSuccess, decoded.Failure?.Message);
        CheckpointResult<GameSession> restored = GameSessionSaveCodec.Restore(decoded.Document!, Limits,
            definitions: new PhysicalDefinitionCatalog([MetalDefinition, Sensor.Physical]), interactionExecution: execution);
        Assert.True(restored.IsSuccess, restored.Failure?.Path + ": " + restored.Failure?.Message);
        return restored.Value!;
    }

    private static GameSession Create(InteractionBatchExecution? execution)
    {
        var materials = new MaterialInventoryCompatibilityMap(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = MetalDefinition });
        var station = new StationDefinition(StationKey, "Observation Station", 1, new(10), new(10),
            new([new KeyValuePair<MaterialId, Quantity>(Metal, new(1))], new(1)), 300, [], new(1), []);
        ShipDesign Design(ulong id, ulong radius) => new(new(id), "Observation Builder", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, radius, 1_000, new StationConstructionCapability(1_000, new(1_000)));
        var relationships = new RelationshipSetup([
            new(Player, new("player"), "Player"), new(Other, new("other"), "Other")],
            Player, GameSessionTestFixture.StandingPolicy, []);
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")], [
            new(new(1), new(1), new(1), Player, Design(1, 100), GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, cargo: [Sensor.Physical]),
            new(new(2), new(2), new(2), Other, Design(2, 0), GameSessionTestFixture.Position(200, 0),
                new(ActorControllerKind.Autonomous, OtherSource.Id), cargo: [Sensor.Physical]),
        ], new ConnectorTopology([], []), [], null, relationships, GameSessionTestFixture.RootSeed, 256,
            GameSessionTestFixture.Player, [Sensor], stations: new(new([station], []), materials,
                StationTerritoryPolicy.Absent, [new(OtherSource, Other)]));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)), execution);
    }
}
