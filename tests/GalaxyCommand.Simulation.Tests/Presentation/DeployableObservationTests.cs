using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DeployableObservationTests
{
    [Fact]
    public void OwnedSatelliteRevealsExternalShipsAndWithdrawalRemovesCoverage()
    {
        using GameSession session = Create();
        Assert.DoesNotContain(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        EntityId satellite = Deploy(session, 1, 1, 0);
        Assert.Contains(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        using GameSession restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!).Value!;
        Assert.Contains(Present(restored).World.Ships, ship => ship.Id == new ShipId(2));
        Assert.Equal(Present(session).World.Deployables, Present(restored).World.Deployables);
        session.RemoveEntity(new EntityRemovalRequest(satellite, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.DoesNotContain(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
    }

    [Fact]
    public void SatelliteCoverageDisclosesExternalMotionAtCommitOnly()
    {
        using GameSession session = Create();
        EntityId satellite = Deploy(session, 1, 1, 0);
        session.SubmitCommand(Other, new MoveShipCommand(new ShipId(2), GameSessionTestFixture.Destination(20_001, 0), OrderPlacement.ReplaceAll));
        GamePresentationFactEnvelope notice = Assert.Single(Present(session).Facts.Facts,
            fact => fact.Fact is ObservedShipChangeFact);
        session.RemoveEntity(new EntityRemovalRequest(satellite, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Contains(notice, Present(session).Facts.Facts);
    }

    [Fact]
    public void HiddenRemovalRetainsStaleViewAcrossRestoreUntilCoverageConfirmsAbsence()
    {
        using GameSession session = Create(otherX: 5000);
        EntityId target = Deploy(session, 2, 2, 5000);
        ObservedSensorDeployableSnapshot live = Assert.Single(Present(session).World.Deployables);
        Assert.Equal(SensorObservationState.Live, live.State);
        MovePlayer(session, 60_000);
        ObservedSensorDeployableSnapshot stale = Assert.Single(Present(session).World.Deployables);
        Assert.Equal(SensorObservationState.Stale, stale.State);
        Assert.Equal(live.View with { ObservedAt = stale.View.ObservedAt }, stale.View);
        GameFactSequence cursor = session.ReadFactsAfter(null, 256).Facts[^1].Sequence;
        session.RemoveEntity(new EntityRemovalRequest(target, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Equal(stale, Assert.Single(Present(session).World.Deployables));
        Assert.Empty(session.CapturePresentation(new GamePresentationRequest(new PrincipalId(1), [], null, cursor, 256)).Facts.Facts);
        CheckpointResult<GameSession> restoration = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restoration.IsSuccess, restoration.Failure?.Message);
        using GameSession restored = restoration.Value!;
        Assert.Equal(stale, Assert.Single(Present(restored).World.Deployables));
        MovePlayer(session, 0);
        MovePlayer(restored, 0);
        ObservedSensorDeployableSnapshot missing = Assert.Single(Present(session).World.Deployables);
        Assert.Equal(SensorObservationState.ConfirmedMissing, missing.State);
        Assert.Equal(stale.View, missing.View);
        Assert.Equal(missing, Assert.Single(Present(restored).World.Deployables));
        using GameSession missingRestored = GameSession.RestoreCheckpoint(restored.CaptureCheckpoint().Value!).Value!;
        Assert.Equal(missing, Assert.Single(Present(missingRestored).World.Deployables));
        MovePlayer(missingRestored, 60_000);
        Assert.Equal(missing, Assert.Single(Present(missingRestored).World.Deployables));
        Assert.DoesNotContain(Present(restored).Facts.Facts, fact => fact.Fact is ObservedSensorChangeFact change
            && change.Kind == ObservedSensorChangeKind.Disappeared);
    }

    [Fact]
    public void ReturningCoverageRefreshesTheRetainedObservationWithoutPresentationReads()
    {
        using GameSession session = Create(otherX: 5000);
        Deploy(session, 2, 2, 5000);
        MovePlayer(session, 60_000);
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        GameSessionCheckpoint staleCheckpoint = capture.Value!;
        using GameSession restored = GameSession.RestoreCheckpoint(staleCheckpoint).Value!;
        MovePlayer(restored, 0);
        ObservedSensorDeployableSnapshot live = Assert.Single(Present(restored).World.Deployables);
        Assert.Equal(SensorObservationState.Live, live.State);
        Assert.Equal(restored.CurrentTime, live.View.ObservedAt);
        Assert.True(live.View.ObservedAt > SimulationTime.Zero);
        GameSessionCheckpoint checkpoint = restored.CaptureCheckpoint().Value!;
        using GameSession again = GameSession.RestoreCheckpoint(checkpoint).Value!;
        Assert.Equal(live, Assert.Single(Present(again).World.Deployables));
    }

    [Fact]
    public void ForeignSatellitesDoNotShareCoverageAndOwnedSensorsRemainVisibleWithZeroRadius()
    {
        using GameSession foreign = Create(otherX: 5000);
        Deploy(foreign, 2, 2, 5000);
        foreign.SubmitCommand(Other, new MoveShipCommand(new ShipId(2), GameSessionTestFixture.Destination(20_000, 0), OrderPlacement.ReplaceAll));
        GameSessionTestFixture.AdvanceUntilOrderTerminal(foreign, new ShipId(2));
        Assert.DoesNotContain(Present(foreign).World.Ships, ship => ship.Id == new ShipId(2));
        Assert.Equal(SensorObservationState.Live, Assert.Single(Present(foreign).World.Deployables).State);
        using GameSession owned = Create(playerRadius: 0);
        Deploy(owned, 1, 1, 2500);
        Assert.Equal(SensorObservationState.Live, Assert.Single(Present(owned).World.Deployables).State);
    }

    [Fact]
    public void PassingThroughCoverageDiscoversADeployableWithoutAnyPresentationRead()
    {
        using GameSession session = Create(playerRadius: 5000, otherX: 40_000);
        Deploy(session, 2, 2, 40_000);
        MovePlayer(session, 100_000);
        ObservedSensorDeployableSnapshot observation = Assert.Single(Present(session).World.Deployables);
        Assert.Equal(SensorObservationState.Stale, observation.State);
        Assert.True(observation.View.ObservedAt > SimulationTime.Zero);
        Assert.True(observation.View.ObservedAt < session.CurrentTime);
    }

    [Fact]
    public void LastObservationAtExitDoesNotDependOnAdvanceGranularity()
    {
        using GameSession coarse = Create(otherX: 5001);
        using GameSession fine = Create(otherX: 5001);
        Deploy(coarse, 2, 2, 5001);
        Deploy(fine, 2, 2, 5001);
        MovePlayer(coarse, 60_000);
        fine.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(60_000, 0), OrderPlacement.ReplaceAll));
        for (ulong time = 137; time < coarse.CurrentTime.Milliseconds; time += 137)
            fine.AdvanceTo(new SimulationTime(time));
        fine.AdvanceTo(coarse.CurrentTime);
        Assert.Equal(Assert.Single(Present(coarse).World.Deployables), Assert.Single(Present(fine).World.Deployables));
        Assert.Equal(coarse.CaptureSnapshot().Ships.Select(ship => (ship.Id, ship.Position, ship.Velocity, ship.Heading)),
            fine.CaptureSnapshot().Ships.Select(ship => (ship.Id, ship.Position, ship.Velocity, ship.Heading)));
        Assert.Equal(coarse.ReadFactsAfter(null, 256).Facts, fine.ReadFactsAfter(null, 256).Facts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RestoreRejectsMalformedPersistentEvidence(int mutation)
    {
        using GameSession session = Create(otherX: 5000);
        Deploy(session, 2, 2, 5000);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        ObservedSensorDeployableSnapshot item = Assert.Single(checkpoint.PlayerObservation!.Deployables!);
        IReadOnlyList<ObservedSensorDeployableSnapshot>? items = mutation switch
        {
            0 => null,
            1 => [item, item],
            2 => [item with { View = item.View with { ObservedAt = new SimulationTime(1) } }],
            3 => [item with { State = SensorObservationState.Stale }],
            4 => [item with { View = item.View with { Position = GameSessionTestFixture.Position(5001, 0) } }],
            _ => [],
        };
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint with
        { PlayerObservation = checkpoint.PlayerObservation with { Deployables = items } });
        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.playerObservation", result.Failure!.Path);
    }

    [Fact]
    public void SourceOverlapAndPickupWithdrawOnlyTheirOwnContribution()
    {
        using GameSession session = Create(playerCargo: 2);
        EntityId first = Deploy(session, 1, 1, 0);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new DeploySensorCommand(new ShipId(1), new ItemInstanceId(2), GameSessionTestFixture.Position(2500, 0), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(session.CurrentTime);
        EntityId second = session.CaptureSnapshot().DeployedSensors.Single(sensor => sensor.EntityId != first).EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new PickupSensorCommand(new ShipId(1), first, OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        Assert.Equal(second, Assert.Single(Present(session).World.Deployables).View.EntityId);
        Assert.Contains(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
        session.SubmitCommand(GameSessionTestFixture.Player, new PickupSensorCommand(new ShipId(1), second, OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        Assert.Empty(Present(session).World.Deployables);
        Assert.DoesNotContain(Present(session).World.Ships, ship => ship.Id == new ShipId(2));
    }

    [Fact]
    public void MotionCheckpointContinuationPreservesObservationAcrossWorkerSettings()
    {
        using GameSession original = Create(playerRadius: 5000, otherX: 40_000);
        Deploy(original, 2, 2, 40_000);
        original.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(100_000, 0), OrderPlacement.ReplaceAll));
        original.AdvanceTo(new SimulationTime(30_000));
        GameSessionCheckpoint checkpoint = original.CaptureCheckpoint().Value!;
        using GameSession serial = GameSession.RestoreCheckpoint(checkpoint).Value!;
        using GameSession parallel = GameSession.RestoreCheckpoint(checkpoint, new InteractionBatchExecution(4, 1)).Value!;
        foreach (GameSession session in new[] { original, serial, parallel }) session.AdvanceTo(new SimulationTime(500_000));
        Assert.Equal(Present(original).World.Deployables, Present(serial).World.Deployables);
        Assert.Equal(Present(original).World.Deployables, Present(parallel).World.Deployables);
        Assert.Equal(Present(original).Facts.Facts, Present(parallel).Facts.Facts);
    }

    [Fact]
    public void LaterSatelliteCoverageDiscoversCurrentStateWithoutReplayingHiddenDeployment()
    {
        using GameSession session = Create();
        EntityId foreign = Deploy(session, 2, 2, 20_000);
        Assert.Empty(Present(session).World.Deployables);
        Deploy(session, 1, 1, 0);
        Assert.Contains(Present(session).World.Deployables, item => item.View.EntityId == foreign);
        Assert.DoesNotContain(Present(session).Facts.Facts, fact => fact.Fact is ObservedSensorChangeFact);
        session.RemoveEntity(new EntityRemovalRequest(foreign, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Equal(SensorObservationState.ConfirmedMissing, Present(session).World.Deployables.Single(item => item.View.EntityId == foreign).State);
        ObservedSensorChangeFact notice = Assert.IsType<ObservedSensorChangeFact>(Assert.Single(Present(session).Facts.Facts,
            fact => fact.Fact is ObservedSensorChangeFact).Fact);
        Assert.Equal(ObservedSensorChangeKind.Disappeared, notice.Kind);
        Assert.DoesNotContain(Present(session).Facts.Facts, fact => fact.Fact is SensorRemovedFact);
        using GameSession restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!).Value!;
        Assert.Equal(Present(session).Facts.Facts, Present(restored).Facts.Facts);
    }

    [Fact]
    public void SatelliteDetectsAMovingContactAfterTheDeployingShipIsRemoved()
    {
        using GameSession session = Create(otherX: 40_000);
        Deploy(session, 1, 1, 0);
        session.RemoveEntity(new EntityRemovalRequest(new EntityId(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Empty(Present(session).World.Ships);
        session.SubmitCommand(Other, new MoveShipCommand(new ShipId(2), GameSessionTestFixture.Destination(20_000, 0), OrderPlacement.ReplaceAll));
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session, new ShipId(2));
        GameObservedShipSnapshot ship = Assert.Single(Present(session).World.Ships);
        Assert.Equal(new ShipId(2), ship.Id);
        Assert.Null(ship.OwnedDetail);
    }

    [Fact]
    public void ReleasingControlledMotionReconcilesCoverageAtItsRoundedCommittedPosition()
    {
        using GameSession session = Create(otherX: 10_001);
        Deploy(session, 2, 2, 10_001);
        var owner = new InteractionMotionOwner("observation-test", 1);
        session.RegisterSpatialInteractionDomain(owner.DomainKind);
        var interval = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(1000),
            new ShipKinematicState(GameSessionTestFixture.Position(0, 0), ShipVelocity.Zero, ShipHeading.Zero),
            new ShipAcceleration(2000, 0), new ShipAngularRate(0));
        Assert.True(session.TryHandoffInteractionMotions([new InteractionMotionRequest(new ShipId(1), interval,
            session.CaptureSpatialInteractionView().Get(new ShipId(1))!.Revision)], owner));
        session.AdvanceTo(new SimulationTime(900));
        Assert.Empty(Present(session).World.Deployables);
        Assert.True(session.TryReleaseInteractionMotions([new InteractionMotionReleaseRequest(new ShipId(1),
            session.CaptureSpatialInteractionView().Get(new ShipId(1))!.Revision)], owner, out _));
        Assert.Equal(SensorObservationState.Live, Assert.Single(Present(session).World.Deployables).State);
    }

    [Fact]
    public void ConnectorEmergenceDiscoversOnlyDeployablesInTheArrivingSystem()
    {
        using GameSession session = Create(otherX: 5000, secondSystem: true);
        Deploy(session, 2, 2, 5000);
        Assert.Empty(Present(session).World.Deployables);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new ShipId(1), new NavigationDestination.System(new SystemId(2)), OrderPlacement.ReplaceAll)).Result.Status);
        Assert.NotNull(session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).Transit);
        Assert.Empty(Present(session).World.Deployables);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
        ObservedSensorDeployableSnapshot sensor = Assert.Single(Present(session).World.Deployables);
        Assert.Equal(SensorObservationState.Live, sensor.State);
        Assert.Equal(new SystemId(2), sensor.View.Position.SystemId);
    }

    private static void MovePlayer(GameSession session, long x)
    {
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)).Result.Status);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
    }

    internal static CommandSource Other { get; } = new(CommandSourceKind.Autonomous, new CommandSourceId("other"));
    internal static GamePresentationSnapshot Present(GameSession session) => session.CapturePresentation(
        new GamePresentationRequest(GameSessionTestFixture.Principal, [], null, null, 256));

    internal static EntityId Deploy(GameSession session, ulong ship, ulong item, long x)
    {
        SystemId system = session.CaptureSnapshot().Ships.Single(actor => actor.Id == new ShipId(ship)).Position!.Value.SystemId;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(ship == 1 ? GameSessionTestFixture.Player : Other,
            new DeploySensorCommand(new ShipId(ship), new ItemInstanceId(item),
                new SystemPosition(system, GameSessionTestFixture.Position(x, 0).Position), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(session.CurrentTime);
        return session.CaptureSnapshot().DeployedSensors.Single(sensor => sensor.PrincipalId == new PrincipalId(ship)).EntityId;
    }

    internal static GameSession Create(ulong playerRadius = 10_000, long otherX = 20_000, int playerCargo = 1, bool secondSystem = false)
    {
        var sensor = new SensorDeployableDefinition(QualifiedContentKey.Create("test", "sensor-deployable", "satellite"),
            "Satellite", new Quantity(1), 2500, 30_000);
        var design = new ShipDesign(new ConstructionDesignId(1), "Test", new ConstructionRecipe([], new Work(1)),
            new Quantity(10), GameSessionTestFixture.ManeuverCapability, playerRadius, 5000);
        var relationships = new RelationshipSetup(GameSessionTestFixture.Relationships.Principals.Append(
            new PrincipalDefinition(new PrincipalId(2), new PrincipalContentId("other"), "Other")),
            GameSessionTestFixture.Principal, GameSessionTestFixture.StandingPolicy, []);
        StarSystem[] systems = secondSystem ? [new(new SystemId(1), "Test"), new(new SystemId(2), "Other")] : [new(new SystemId(1), "Test")];
        var topology = secondSystem ? new ConnectorTopology(
            [new ConnectorEndpoint(new ConnectorEndpointId(1), GameSessionTestFixture.Position(0, 0)),
             new ConnectorEndpoint(new ConnectorEndpointId(2), new SystemPosition(new SystemId(2), GameSessionTestFixture.Position(0, 0).Position))],
            [new TransitConnection(new TransitConnectionId(1), new ConnectorEndpointId(1), new ConnectorEndpointId(2), new SimulationDuration(100))])
            : new ConnectorTopology([], []);
        var setup = new GameSessionSetup(systems,
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), new PrincipalId(1), design,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController, cargo: Enumerable.Repeat(sensor.Physical, playerCargo)),
             new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), new PrincipalId(2), design,
                new SystemPosition(new SystemId(secondSystem ? 2UL : 1UL), GameSessionTestFixture.Position(otherX, 0).Position), new ActorController(ActorControllerKind.Autonomous, Other.Id), cargo: [sensor.Physical])],
            topology, [], null, relationships, GameSessionTestFixture.RootSeed, 256,
            GameSessionTestFixture.Player, [sensor]);
        return new GameSession(setup, secondSystem
            ? new HierarchicalNavigationPlanner(topology, new ChebyshevLocalTravelTimeEstimator(1))
            : new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
