using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionFogOfWarTests
{
    [Fact]
    public void OutOfRangeNonOwnedShipCannotResolveThroughMapSelectionOrFocus()
    {
        using GameSession session = Create(20_000);
        GamePresentationSnapshot presentation = Capture(session);
        Assert.Equal<ShipId>([new(1)], presentation.World.Ships.Select(ship => ship.Id));
        Assert.Equal<ShipId>([new(1)], presentation.Selection.ResolvedShips.Select(ship => ship.Id));
        Assert.Equal<ShipId>([new(2)], presentation.Selection.UnresolvedShipIds);
        Assert.Null(presentation.Selection.FocusedShip);
        Assert.Equal(2, session.CaptureSnapshot().Ships.Count);
    }

    [Fact]
    public void CoveredNonOwnedShipHasOnlyCapturedExternalFields()
    {
        using GameSession session = Create(10_000);
        GamePresentationSnapshot presentation = Capture(session);
        GameObservedShipSnapshot contact = Assert.IsType<GameObservedShipSnapshot>(
            presentation.World.Ships.Single(ship => ship.Id == new ShipId(2)));
        Assert.Equal(GameSessionTestFixture.Position(10_000, 0), contact.Position);
        Assert.Equal(new PrincipalId(2), contact.PrincipalId);
        Assert.Null(contact.OwnedDetail);
        Assert.Equal(contact, Assert.IsType<GameObservedShipSnapshot>(presentation.Selection.FocusedShip));
        Assert.NotNull(Assert.IsType<GameObservedShipSnapshot>(presentation.World.Ships[0]).OwnedDetail);
    }

    [Fact]
    public void PresentationCannotSwitchToAnotherRegisteredPrincipal()
    {
        using GameSession session = Create(20_000);
        Assert.Throws<ArgumentException>(() => session.CapturePresentation(
            new GamePresentationRequest(new PrincipalId(2), [], null, null, 10)));
    }

    [Fact]
    public void EverySetupConstructorRequiresAnExplicitPlayerCommandSource()
    {
        Assert.All(typeof(GameSessionSetup).GetConstructors(), constructor =>
            Assert.Contains(constructor.GetParameters(), parameter =>
                parameter.Name == "playerCommandSource" && parameter.ParameterType == typeof(CommandSource)
                && !parameter.HasDefaultValue));
    }

    [Fact]
    public void NullRemovalRequestIsRejectedAtTheSessionBoundary()
    {
        using GameSession session = Create(5_000);
        Assert.Throws<ArgumentNullException>(() => session.RemoveEntity(null!));
    }

    [Fact]
    public void HiddenAutonomousActivityNeverReachesThePlayerFactRead()
    {
        using GameSession session = Create(20_000);
        GameplayCommandRecord command = session.SubmitCommand(
            new CommandSource(CommandSourceKind.Autonomous, new CommandSourceId("other")),
            new MoveShipCommand(new ShipId(2), GameSessionTestFixture.Destination(20_001, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);
        Assert.NotEmpty(session.ReadFactsAfter(null, 256).Facts);
        GamePresentationSnapshot presentation = Capture(session);
        Assert.Empty(presentation.Facts.Facts);
        Assert.Empty(presentation.SelectedShipFacts);
        Assert.Equal(session.ReadFactsAfter(null, 256).Facts[^1].Sequence, presentation.NextFactCursor);
    }

    [Fact]
    public void PlayerFactEnvelopeDoesNotExposeRawCauses()
    {
        using GameSession session = Create(20_000);
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(10, 0), OrderPlacement.ReplaceAll));
        object notice = Capture(session).Facts.Facts[0];
        Assert.Null(notice.GetType().GetProperty("Cause"));
    }

    [Fact]
    public void CoveredAutonomousMotionProducesOnlyAnExternalNotice()
    {
        using GameSession session = Create(5_000);
        MoveOther(session, 30_000);
        GamePresentationFactEnvelope notice = Assert.Single(Capture(session).Facts.Facts);
        ObservedShipChangeFact change = Assert.IsType<ObservedShipChangeFact>(notice.Fact);
        Assert.Equal(new ShipId(2), change.ShipId);
        Assert.Equal(ObservedShipChangeKind.MotionChanged, change.Kind);
        Assert.Equal(GameSessionTestFixture.Position(5_000, 0), change.Position);
        Assert.Null(change.DesignId);
        Assert.Null(change.PrincipalId);
        Assert.Null(Capture(session).Selection.FocusedShip!.OwnedDetail);
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void RemovedNonOwnedSubjectUsesItsPriorPositionWithoutRevealingRemovalReason(long position, bool disclosed)
    {
        using GameSession session = Create(position);
        session.RemoveEntity(new EntityRemovalRequest(new EntityId(2), EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo));
        GamePresentationSnapshot presentation = Capture(session);
        Assert.Equal(disclosed ? 1 : 0, presentation.Facts.Facts.Count);
        if (disclosed)
        {
            ObservedShipChangeFact notice = Assert.IsType<ObservedShipChangeFact>(presentation.Facts.Facts[0].Fact);
            Assert.Equal(ObservedShipChangeKind.Disappeared, notice.Kind);
            Assert.Equal(GameSessionTestFixture.Position(position, 0), notice.Position);
        }
        Assert.Null(presentation.Selection.FocusedShip);
    }

    [Fact]
    public void RemovingTheLastOwnedSourceStillDisclosesItsRemoval()
    {
        using GameSession session = Create(10_000);
        session.RemoveEntity(new EntityRemovalRequest(new EntityId(1), EntityRemovalReason.Destroyed,
            EntityCargoDisposition.DiscardCargo));
        Assert.IsType<EntityRemovedFact>(Assert.Single(Capture(session).Facts.Facts).Fact);
        Assert.Empty(Capture(session).World.Ships);
    }

    [Fact]
    public void DelayedReadsRetainDisclosedHistoryAfterCoverageIsLost()
    {
        using GameSession session = Create(5_000);
        MoveOther(session, 30_000);
        GamePresentationFactEnvelope started = Assert.Single(Capture(session).Facts.Facts);
        session.AdvanceTo(new SimulationTime(1_000_000));
        GamePresentationSnapshot later = Capture(session);
        Assert.DoesNotContain(later.World.Ships, ship => ship.Id == new ShipId(2));
        Assert.Contains(started, later.Facts.Facts);
        Assert.DoesNotContain(later.Facts.Facts, item => item.Fact is ShipOrderTransitionFact transition && transition.ShipId == new ShipId(2));
    }

    [Fact]
    public void HiddenHistoryIsNotReplayedWhenSensorsLaterReachTheSubject()
    {
        using GameSession session = Create(20_000);
        MoveOther(session, 20_001);
        session.AdvanceTo(new SimulationTime(1_000_000));
        GameFactSequence hiddenCursor = session.ReadFactsAfter(null, 256).Facts[^1].Sequence;
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(20_001, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(2_000_000));
        GamePresentationSnapshot later = Capture(session);
        Assert.Contains(later.World.Ships, ship => ship.Id == new ShipId(2));
        Assert.DoesNotContain(later.Facts.Facts, notice => notice.Sequence.Value <= hiddenCursor.Value);
    }

    [Fact]
    public void RestoredSessionPreservesDisclosureAndContinuesLikeUninterruptedSession()
    {
        using GameSession uninterrupted = Create(5_000);
        MoveOther(uninterrupted, 30_000);
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(uninterrupted.CaptureCheckpoint().Value);
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Equal(Capture(uninterrupted).Facts.Facts, Capture(restored).Facts.Facts);
        uninterrupted.AdvanceTo(new SimulationTime(1_000_000));
        restored.AdvanceTo(new SimulationTime(1_000_000));
        Assert.Equal(Capture(uninterrupted).Facts.Facts, Capture(restored).Facts.Facts);
        Assert.Equal(Capture(uninterrupted).World.Ships.Select(ship => (ship.Id, ship.Position)),
            Capture(restored).World.Ships.Select(ship => (ship.Id, ship.Position)));
    }

    [Fact]
    public void RestoreRejectsMissingOrMismatchedObservationState()
    {
        using GameSession session = Create(5_000);
        MoveOther(session, 30_000);
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);
        Assert.Equal("$.checkpoint.playerObservation", GameSession.RestoreCheckpoint(
            checkpoint with { PlayerObservation = null }).Failure!.Path);
        Assert.Equal("$.checkpoint.playerObservation", GameSession.RestoreCheckpoint(
            checkpoint with { PlayerObservation = checkpoint.PlayerObservation! with { ObserverPrincipalId = new PrincipalId(2) } }).Failure!.Path);
        Assert.Equal("$.checkpoint.playerObservation", GameSession.RestoreCheckpoint(checkpoint with
        {
            Facts = new GameFactStoreCheckpoint(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences,
                checkpoint.Facts.RetainedFacts.Select(fact => fact! with { PlayerDisclosure = null })),
        }).Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsAReceiptThatExposesAnUnapprovedRawPayload()
    {
        using GameSession session = Create(20_000);
        MoveOther(session, 30_000);
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);
        GameSessionCheckpoint invalid = checkpoint with
        {
            Facts = new GameFactStoreCheckpoint(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences,
                checkpoint.Facts.RetainedFacts.Select(fact => fact! with
                {
                    PlayerDisclosure = new PlayerFactDisclosure(new PrincipalId(1), fact!.Fact),
                })),
        };
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(invalid);
        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.playerObservation", result.Failure!.Path);
    }

    [Fact]
    public void RetentionEvictsDisclosureWithItsSourceAndRestoresGapAndCursor()
    {
        using GameSession session = Create(20_000, factRetentionCapacity: 4);
        for (long destination = 30_000; destination < 30_006; destination++) MoveOther(session, destination);
        GamePresentationSnapshot before = Capture(session);
        Assert.True(before.Facts.CursorGap);
        Assert.Empty(before.Facts.Facts);
        GameFactReadResult raw = session.ReadFactsAfter(null, 256);
        Assert.Equal(4, raw.Facts.Count);
        Assert.Equal(raw.NewestCommittedSequence, before.NextFactCursor);
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);
        using GameSession restored = Assert.IsType<GameSession>(GameSession.RestoreCheckpoint(checkpoint).Value);
        GamePresentationSnapshot after = Capture(restored);
        Assert.Equal(before.Facts.CursorGap, after.Facts.CursorGap);
        Assert.Equal(before.NextFactCursor, after.NextFactCursor);
        Assert.Equal(before.Facts.Facts, after.Facts.Facts);
        foreach (GameSession current in new[] { session, restored })
            current.SubmitCommand(GameSessionTestFixture.Player,
                new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(10, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(Capture(session).Facts.Facts, Capture(restored).Facts.Facts);
        Assert.Equal(4, restored.ReadFactsAfter(null, 256).Facts.Count);
    }

    [Fact]
    public void GrantNoticesRemainScopedAndReadableAfterRevocationAndRestore()
    {
        using GameSession session = Create(5_000);
        for (ulong holder = 1; holder <= 2; holder++)
        {
            session.CommitRelationshipPolicyChanges(new RelationshipPolicyChangeBatch(
                new RelationshipPolicyChangeBatchId(RelationshipPolicyChangeSourceKind.Explicit, holder),
                [new IssueRelationshipGrantProposal(new RelationshipGrantId(holder), new PrincipalId(holder == 1 ? 2UL : 1UL),
                    new PrincipalId(holder), new RelationshipGrantKind("test-access"), StandingBand.Neutral, RelationshipPolicyChangeReason.Explicit)]));
            session.CommitRelationshipPolicyChanges(new RelationshipPolicyChangeBatch(
                new RelationshipPolicyChangeBatchId(RelationshipPolicyChangeSourceKind.Explicit, holder + 10),
                [new RevokeRelationshipGrantProposal(new RelationshipGrantId(holder), RelationshipPolicyChangeReason.Explicit)]));
        }
        GamePresentationSnapshot before = Capture(session);
        Assert.Collection(before.Facts.Facts,
            notice => Assert.Equal(new RelationshipGrantId(1), Assert.IsType<RelationshipGrantIssuedFact>(notice.Fact).Id),
            notice => Assert.Equal(new RelationshipGrantId(1), Assert.IsType<RelationshipGrantRevokedFact>(notice.Fact).Id));
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);
        using GameSession restored = Assert.IsType<GameSession>(GameSession.RestoreCheckpoint(checkpoint).Value);
        Assert.Equal(before.Facts.Facts, Capture(restored).Facts.Facts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimultaneousEmergenceUsesCompletedSourceCoverageRegardlessOfEventOrder(bool sourceFirst)
    {
        var system1 = new SystemId(1);
        var system2 = new SystemId(2);
        SystemPosition origin = GameSessionTestFixture.Position(0, 0);
        var destination = new SystemPosition(system2, origin.Position);
        var topology = new ConnectorTopology(
            [new ConnectorEndpoint(new ConnectorEndpointId(1), origin),
             new ConnectorEndpoint(new ConnectorEndpointId(2), destination)],
            [new TransitConnection(new TransitConnectionId(1), new ConnectorEndpointId(1),
                new ConnectorEndpointId(2), new SimulationDuration(10))]);
        var relationships = new RelationshipSetup(
            [new PrincipalDefinition(new PrincipalId(1), new PrincipalContentId("player"), "Player"),
             new PrincipalDefinition(new PrincipalId(2), new PrincipalContentId("other"), "Other")],
            new PrincipalId(1), GameSessionTestFixture.StandingPolicy, []);
        ShipDesign design = new(new ConstructionDesignId(1), "Zero radius",
            new ConstructionRecipe([], new Work(1)), new Quantity(1), GameSessionTestFixture.ManeuverCapability, 0, 5_000);
        var other = new CommandSource(CommandSourceKind.Autonomous, new CommandSourceId("other"));
        var setup = new GameSessionSetup([new StarSystem(system1, "Origin"), new StarSystem(system2, "Destination")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), new PrincipalId(1),
                design, origin, GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), new PrincipalId(2),
                design, origin, new ActorController(ActorControllerKind.Autonomous, other.Id))],
            topology, relationships, GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player);
        using var session = new GameSession(setup, new HierarchicalNavigationPlanner(topology, new ChebyshevLocalTravelTimeEstimator(1)));
        foreach (ulong id in sourceFirst ? new ulong[] { 1, 2 } : [2UL, 1UL])
        {
            GameplayCommandRecord admitted = session.SubmitCommand(id == 1 ? GameSessionTestFixture.Player : other,
                new MoveShipCommand(new ShipId(id), new NavigationDestination.Position(destination), OrderPlacement.ReplaceAll));
            Assert.Equal(CommandResultStatus.Accepted, admitted.Result.Status);
        }
        GameFactSequence cursor = session.ReadFactsAfter(null, 256).Facts[^1].Sequence;
        Assert.Equal<ShipId>([new(1)], Capture(session).World.Ships.Select(ship => ship.Id));
        session.AdvanceTo(new SimulationTime(10));
        GamePresentationSnapshot after = Capture(session, cursor);
        Assert.Contains(after.World.Ships, ship => ship.Id == new ShipId(2));
        Assert.Contains(after.Facts.Facts, notice => notice.Fact is ObservedShipChangeFact
            { ShipId.Value: 2, Kind: ObservedShipChangeKind.Arrived });
    }

    private static void MoveOther(GameSession session, long x)
    {
        GameplayCommandRecord command = session.SubmitCommand(
            new CommandSource(CommandSourceKind.Autonomous, new CommandSourceId("other")),
            new MoveShipCommand(new ShipId(2), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, command.Result.Status);
    }

    internal static GameSession Create(long otherX, ulong radius = 10_000, int factRetentionCapacity = 256)
    {
        ShipDesign ownedDesign = new(new ConstructionDesignId(1), "Sensor ship",
            new ConstructionRecipe([], new Work(1)), new Quantity(10), GameSessionTestFixture.ManeuverCapability, radius, 5_000);
        var relationships = new RelationshipSetup(
            [new PrincipalDefinition(new PrincipalId(1), new PrincipalContentId("player"), "Player"),
             new PrincipalDefinition(new PrincipalId(2), new PrincipalContentId("other"), "Other")],
            new PrincipalId(1), GameSessionTestFixture.StandingPolicy, []);
        var setup = new GameSessionSetup([new StarSystem(new SystemId(1), "Test")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), new PrincipalId(1),
                ownedDesign, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), new PrincipalId(2),
                ownedDesign, GameSessionTestFixture.Position(otherX, 0),
                new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("other")))],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(new FacilityId(1), new PrincipalId(1), GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder, [ownedDesign])],
            relationships, GameSessionTestFixture.RootSeed, factRetentionCapacity, playerCommandSource: GameSessionTestFixture.Player);
        return new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }

    internal static GamePresentationSnapshot Capture(GameSession session, GameFactSequence? cursor = null) =>
        session.CapturePresentation(new GamePresentationRequest(new PrincipalId(1),
            [new ShipId(1), new ShipId(2)], new ShipId(2), cursor, 256));
}
