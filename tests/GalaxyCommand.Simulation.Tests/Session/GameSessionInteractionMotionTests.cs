using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionInteractionMotionTests
{
    private static readonly ShipId Ship = new(1);
    private static readonly InteractionMotionOwner Owner = new("test", 7);

    [Fact]
    public void SessionStopsAtIntervalEndUntilCallerRenewsOrReleases()
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        var interval = Interval(session, Ship, 100);
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, interval)], Owner));
        Assert.Equal(new SimulationTime(100), session.AdvanceTo(new SimulationTime(1000)).EndTime);
        Assert.Equal(interval.StateAt(new SimulationTime(100)), State(session, Ship));
        Assert.Equal(new SimulationTime(100), session.AdvanceTo(new SimulationTime(1000)).EndTime);
        Assert.True(session.IsHealthy);
        var next = Interval(session, Ship, 200);
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, next)], Owner));
        Assert.Equal(new SimulationTime(150), session.AdvanceTo(new SimulationTime(150)).EndTime);
        var expected = next.StateAt(new SimulationTime(150));
        Assert.True(session.TryReleaseInteractionMotions([new(Ship, Revision(session, Ship))], Owner, out var released));
        Assert.Equal(expected, released![Ship]);
        Assert.Equal(new SimulationTime(1000), session.AdvanceTo(new SimulationTime(1000)).EndTime);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(400UL)]
    [InlineData(1000UL)]
    public void PublicSnapshotPositionMatchesControlledCurveAndRemainsCaptured(ulong milliseconds)
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        var interval = Interval(session, Ship, 1000);
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, interval)], Owner));
        var initial = session.CaptureSnapshot().Ships.Single(ship => ship.Id == Ship);
        var now = new SimulationTime(milliseconds);
        session.AdvanceTo(now);
        var captured = session.CaptureSnapshot().Ships.Single(ship => ship.Id == Ship);
        var expected = interval.StateAt(now);

        Assert.Equal(expected.Position, captured.Position);
        Assert.Equal(expected.Velocity, captured.Velocity);
        Assert.Equal(expected.Heading, captured.Heading);
        Assert.Equal(session.CaptureSpatialInteractionView().Get(Ship)!.Spatial.Position, captured.Position);
        Assert.True(session.TryReleaseInteractionMotions([new(Ship, Revision(session, Ship))], Owner, out var released));
        Assert.Equal(expected, released![Ship]);
        session.AdvanceTo(new SimulationTime(2000));
        Assert.Equal(expected.Position, session.CaptureSnapshot().Ships.Single(ship => ship.Id == Ship).Position);
        Assert.Equal(expected.Position, captured.Position);
        Assert.Equal(interval.Start.Position, initial.Position);
    }

    [Fact]
    public void EarliestParticipantBoundaryWinsAndReleasingItUnblocksTheNext()
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        var second = new ShipId(2);
        Assert.True(session.TryHandoffInteractionMotions(
            [Request(session, Ship, Interval(session, Ship, 100)), Request(session, second, Interval(session, second, 200))], Owner));
        Assert.Equal(new SimulationTime(100), session.AdvanceTo(new SimulationTime(1000)).EndTime);
        Assert.True(session.TryReleaseInteractionMotions([new(Ship, Revision(session, Ship))], Owner, out _));
        Assert.Equal(new SimulationTime(200), session.AdvanceTo(new SimulationTime(1000)).EndTime);
    }

    [Fact]
    public void SessionHandoffCancelsAnalyticWorkAndRequiresRegisteredDomain()
    {
        using var session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(Ship,
            GameSessionTestFixture.Destination(90, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(50));
        var initialCapture = session.CaptureCheckpoint();
        Assert.True(initialCapture.IsSuccess, initialCapture.Failure?.Message);
        var before = initialCapture.Value!;
        var interval = Interval(session, Ship, 100);
        var request = Request(session, Ship, interval);
        Assert.Throws<InvalidOperationException>(() => session.TryHandoffInteractionMotions([request], Owner));
        Assert.Equal(before.Engine.Agenda.PendingEvents, session.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents);
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        Assert.True(session.TryHandoffInteractionMotions([request], Owner));
        Assert.Equal(new SpatialRevision(request.ExpectedRevision.Value + 2), Revision(session, Ship));
        Assert.Equal(interval.Start, State(session, Ship));
        Assert.Equal(new SimulationTime(100), session.AdvanceTo(new SimulationTime(1000)).EndTime);
        Assert.DoesNotContain(session.EventRecords, item => item.Timestamp > new SimulationTime(50) && item.Kind is GameEventKind.SpatialMovement);
    }

    [Fact]
    public void ActiveMotionCheckpointRejectsWithoutPoisoningAndReleaseRestoresCapture()
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        session.AdvanceTo(SimulationTime.Zero);
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, Interval(session, Ship, 100))], Owner));
        var capture = session.CaptureCheckpoint();
        Assert.False(capture.IsSuccess);
        Assert.True(session.IsHealthy);
        Assert.True(session.TryReleaseInteractionMotions([new(Ship, Revision(session, Ship))], Owner, out _));
        var afterRelease = session.CaptureCheckpoint();
        Assert.True(afterRelease.IsSuccess, afterRelease.Failure?.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmissionFromContactEffectCannotOverrunItsNewInterval(bool moving)
    {
        using var session = Create(moving ? 10 : 0);
        if (moving)
            session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(Ship,
                GameSessionTestFixture.Destination(90, 0), OrderPlacement.ReplaceAll));
        var owner = session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        owner.Register("contact", Ship, new ShipId(2), 0);
        session.BindSpatialInteractionDomain(Owner.DomainKind, new MotionDomain(session, mutateDuringEvaluation: false));
        var report = session.AdvanceTo(new SimulationTime(10000));
        var contact = Assert.Single(session.EventRecords, item => item.Kind is GameEventKind.SpatialInteraction);
        Assert.Equal(new SimulationTime(contact.Timestamp.Milliseconds + 100), report.EndTime);
        Assert.Equal(moving, contact.Timestamp > SimulationTime.Zero);
        Assert.Equal(session.EventRecords.Count, report.EventsProcessed);
        Assert.True(session.IsHealthy);
    }

    [Fact]
    public void SessionMotionApisRespectEvaluationAndDisposalGuards()
    {
        var session = Create();
        var owner = session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        owner.Register("contact", Ship, new ShipId(2), 0);
        session.BindSpatialInteractionDomain(Owner.DomainKind, new MotionDomain(session, mutateDuringEvaluation: true));
        session.AdvanceTo(SimulationTime.Zero);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.CaptureSpatialInteractionView());
        Assert.Throws<ObjectDisposedException>(() => session.TryHandoffInteractionMotions([], Owner));
        Assert.Throws<ObjectDisposedException>(() => session.TryReleaseInteractionMotions([], Owner, out _));
    }

    [Fact]
    public void NavigationCannotTakeOverControlledMotionBeforeRelease()
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, Interval(session, Ship, 100))], Owner));
        var command = new MoveShipCommand(Ship, GameSessionTestFixture.Destination(90, 0), OrderPlacement.ReplaceAll);
        var result = session.SubmitCommand(GameSessionTestFixture.Player, command);
        Assert.Equal(CommandRejectionCodes.InvalidState, result.Result.RejectionCode);
        Assert.Null(session.CaptureSnapshot().Ships.Single(ship => ship.Id == Ship).CurrentOrder);
        Assert.True(session.IsHealthy);
    }

    [Fact]
    public void FailedBatchLeavesSessionHealthyAndMotionUnchanged()
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        var request = Request(session, Ship, Interval(session, Ship, 100));
        Assert.False(session.TryHandoffInteractionMotions([request, request with { ShipId = new ShipId(99) }], Owner));
        Assert.Equal(request.ExpectedRevision, Revision(session, Ship));
        Assert.True(session.IsHealthy);
        Assert.Equal(new SimulationTime(1000), session.AdvanceTo(new SimulationTime(1000)).EndTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptedOverrideChangesRejectUntilExplicitRelease(bool ending)
    {
        using var session = Create();
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        var script = new CommandSource(CommandSourceKind.Script, new CommandSourceId("motion-guard"));
        var begin = new BeginScriptedOverrideCommand(Ship, new ActorOverrideReasonId("test"), default);
        session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(Ship, GameSessionTestFixture.Destination(90, 0), OrderPlacement.ReplaceAll));
        if (ending)
        {
            Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(script, begin).Result.Status);
            session.SubmitCommand(script,
                new MoveShipCommand(Ship, GameSessionTestFixture.Destination(90, 0), OrderPlacement.ReplaceAll));
        }
        session.AdvanceTo(new SimulationTime(50));
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, Interval(session, Ship, 100))], Owner));
        var before = session.CaptureSnapshot().Ships.Single(ship => ship.Id == Ship);
        var revision = Revision(session, Ship);
        var events = session.EventRecords.ToArray();
        var cursor = session.ReadFactsAfter(null, 256).NewestCommittedSequence;
        GameplayCommand command = ending
            ? new EndScriptedOverrideCommand(Ship, ScriptedOverrideReleasePolicy.CancelOutstanding, new ActorControlRevision(1))
            : begin;

        var rejected = session.SubmitCommand(script, command);

        Assert.Equal(CommandRejectionCodes.InvalidState, rejected.Result.RejectionCode);
        AssertShipUnchanged(before, session.CaptureSnapshot().Ships.Single(ship => ship.Id == Ship));
        Assert.Equal(revision, Revision(session, Ship));
        Assert.Equal(events, session.EventRecords);
        Assert.IsType<CommandRejectedFact>(Assert.Single(session.ReadFactsAfter(cursor, 256).Facts).Fact);
        Assert.True(session.IsHealthy);
        Assert.True(session.TryReleaseInteractionMotions([new(Ship, revision)], Owner, out _));
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(script, command).Result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovalRejectsControlledOwnerOrAffectedControlledOrdersUntilRelease(int kind)
    {
        using var session = Create(90);
        session.RegisterSpatialInteractionDomain(Owner.DomainKind);
        if (kind == 2)
            session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(Ship,
                GameSessionTestFixture.Destination(90, 0), OrderPlacement.ReplaceAll));
        if (kind != 0)
            session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(Ship,
                new NavigationDestination.Entity(new EntityId(2)), kind == 2 ? OrderPlacement.Append : OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(50));
        Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, Interval(session, Ship, 100))], Owner));
        var before = session.CaptureSnapshot().Ships.ToArray();
        var revision = Revision(session, Ship);
        var cursor = session.ReadFactsAfter(null, 256).NewestCommittedSequence;
        var request = new EntityRemovalRequest(new EntityId(kind != 0 ? 2UL : 1UL),
            EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo);

        var rejected = Assert.IsType<EntityRemovalResult.Rejected>(session.RemoveEntity(request));

        Assert.Equal(EntityRemovalRejectionReason.OwnerConflict, rejected.Reason);
        foreach (var ship in before)
        {
            AssertShipUnchanged(ship, session.CaptureSnapshot().Ships.Single(item => item.Id == ship.Id));
            Assert.Equal(ship.Id, session.ResolveShip(ship.EntityId));
            Assert.Equal(ship.EntityId, session.ResolveEntity(ship.Id));
        }
        Assert.Equal(revision, Revision(session, Ship));
        Assert.Empty(session.ReadFactsAfter(cursor, 256).Facts);
        Assert.True(session.IsHealthy);
        Assert.True(session.TryReleaseInteractionMotions([new(Ship, revision)], Owner, out _));
        var removed = Assert.IsType<EntityRemovalResult.Removed>(session.RemoveEntity(request));
        Assert.Equal(removed, session.RemoveEntity(request));
        Assert.Null(session.ResolveShip(request.EntityId));
        Assert.Equal(new SimulationTime(1000), session.AdvanceTo(new SimulationTime(1000)).EndTime);
    }

    private static void AssertShipUnchanged(GameShipSnapshot before, GameShipSnapshot after)
    {
        Assert.Equal(before.SpatialState, after.SpatialState);
        Assert.Equal(before.Velocity, after.Velocity);
        Assert.Equal(before.Heading, after.Heading);
        Assert.Equal(before.Control, after.Control);
        Assert.Equal(before.CurrentOrder, after.CurrentOrder);
        Assert.Equal(before.QueuedOrders, after.QueuedOrders);
        Assert.Equal(before.SuspendedOrders, after.SuspendedOrders);
        Assert.Equal(before.CargoInventoryId, after.CargoInventoryId);
        Assert.Equal(before.CargoCapacity, after.CargoCapacity);
    }

    private sealed class MotionDomain(GameSession session, bool mutateDuringEvaluation) : ISpatialInteractionDomainEvaluator
    {
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view, IReadOnlyList<InteractionCandidate> contacts)
        {
            if (mutateDuringEvaluation)
            {
                Assert.Throws<InvalidOperationException>(() => session.CaptureSpatialInteractionView());
                Assert.Throws<InvalidOperationException>(() => session.TryHandoffInteractionMotions([], Owner));
                Assert.Throws<InvalidOperationException>(() => session.TryReleaseInteractionMotions([], Owner, out _));
                return [];
            }
            return [new(contacts[0].Key, 0, 0, new EnterMotion(contacts[0].Interest.Id))];
        }
    }

    private sealed class EnterMotion(InteractionInterestId interestId) : ISpatialInteractionEffect
    {
        public void Commit(GameSession session)
        {
            Assert.True(session.TryHandoffInteractionMotions([Request(session, Ship, Interval(session, Ship, session.CurrentTime.Milliseconds + 100))], Owner));
            Assert.True(session.GetSpatialInteractionDomain(Owner.DomainKind)!.Cancel(interestId));
        }
    }

    private static ShipKinematicState State(GameSession session, ShipId ship)
    {
        var spatial = session.CaptureSpatialInteractionView().Get(ship)!.Spatial;
        return new ShipKinematicState(spatial.Position!.Value, spatial.Velocity, spatial.Heading);
    }

    private static SpatialRevision Revision(GameSession session, ShipId ship) => session.CaptureSpatialInteractionView().Get(ship)!.Revision;
    private static InteractionMotionRequest Request(GameSession session, ShipId ship, AnalyticManeuverSegment interval) => new(ship, interval, Revision(session, ship));
    private static AnalyticManeuverSegment Interval(GameSession session, ShipId ship, ulong end) => new(session.CurrentTime,
        new SimulationTime(end), State(session, ship), new ShipAcceleration(20000, 0), new ShipAngularRate(45000));

    private static GameSession Create(long targetX = 0) => new(new GameSessionSetup(
        [new StarSystem(GameSessionTestFixture.System, "Test")],
        [new InitialShipSetup(new EntityId(1), Ship, new InventoryId(1), GameSessionTestFixture.Principal, GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
         new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), GameSessionTestFixture.Principal, GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(targetX, 0), GameSessionTestFixture.PlayerController)],
        new ConnectorTopology([], []),
        [new ShipMaterializationPolicy(new FacilityId(1), GameSessionTestFixture.Principal,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController,
            InitialShipOrderPolicy.NoInitialOrder, [GameSessionTestFixture.Design])],
        GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256, playerCommandSource: GameSessionTestFixture.Player),
        new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
}
