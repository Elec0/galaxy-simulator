using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionInteractionRuntimeTests
{
    [Fact]
    public void AllDomainsEvaluateTheSameViewBeforeCanonicalEffectCommit()
    {
        GameSession session = CreateSession(0);
        var trace = new List<string>();
        InteractionInterestOwner first = session.RegisterSpatialInteractionDomain("z-first");
        InteractionInterestOwner second = session.RegisterSpatialInteractionDomain("a-second");
        first.Register("contact", new ShipId(1), new ShipId(2), 0);
        second.Register("contact", new ShipId(2), new ShipId(1), 0);
        var a = new RecordingDomain("first", first, trace, moveOnCommit: true);
        var b = new RecordingDomain("second", second, trace);
        session.BindSpatialInteractionDomain("a-second", b);
        session.BindSpatialInteractionDomain("z-first", a);
        session.AdvanceTo(SimulationTime.Zero);
        Assert.Equal<string>(["evaluate:first", "evaluate:second", "commit:first", "commit:second"], trace);
        Assert.Same(a.View, b.View);
        Assert.Null(b.View!.Get(new ShipId(1))!.Spatial.Maneuver);
        Assert.NotNull(session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).Maneuver);
        Assert.Equal(SimulationTime.Zero, a.View!.Timestamp);
    }

    [Fact]
    public void InterestRegisteredAfterInitializationIsEvaluatedAtCurrentTime()
    {
        GameSession session = CreateSession(0);
        session.AdvanceTo(new SimulationTime(10));
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        var domain = new RecordingDomain("test", owner, []);
        session.BindSpatialInteractionDomain("test", domain);
        session.AdvanceTo(new SimulationTime(10));
        Assert.Equal(new SimulationTime(10), domain.View?.Timestamp);
        Assert.Equal(1, domain.Commits);
    }

    [Fact]
    public void PendingAnalyticContactContinuesWithExactAgendaOrderAfterRestore()
    {
        GameSession live = CreateSession(10);
        InteractionInterestOwner owner = live.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 1);
        var original = new RecordingDomain("test", owner, []);
        live.BindSpatialInteractionDomain("test", original);
        live.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));
        live.AdvanceTo(new SimulationTime(100));
        CheckpointResult<GameSessionCheckpoint> capture = live.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        Assert.Contains(capture.Value!.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.SpatialInteraction);
        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(capture.Value!);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        GameSession restored = restore.Value!;
        Assert.Throws<InvalidOperationException>(() => restored.AdvanceTo(new SimulationTime(2_000)));
        Assert.True(restored.IsHealthy);
        var resumed = new RecordingDomain("test", restored.GetSpatialInteractionDomain("test")!, []);
        restored.BindSpatialInteractionDomain("test", resumed);
        RunReport expected = live.AdvanceTo(new SimulationTime(2_000));
        RunReport actual = restored.AdvanceTo(new SimulationTime(2_000));
        Assert.Equal(1, original.Commits);
        Assert.Equal(1, resumed.Commits);
        Assert.Equal(original.View!.Timestamp, resumed.View!.Timestamp);
        Assert.Equal(expected, actual);
        Assert.Equal(live.EventRecords, restored.EventRecords);
    }

    [Fact]
    public void EvaluationCannotMutateRetainedInterestOwner()
    {
        GameSession session = CreateSession(0);
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        session.BindSpatialInteractionDomain("test", new MutatingDomain(owner));
        Assert.Throws<InvalidOperationException>(() => session.AdvanceTo(SimulationTime.Zero));
        Assert.Single(owner.CaptureSnapshot());
        Assert.False(session.IsHealthy);
        Assert.False(session.CaptureCheckpoint().IsSuccess);
    }

    [Fact]
    public void DuplicateEffectKeysAreRejectedBeforeAnyCommit()
    {
        GameSession session = CreateSession(0);
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        var domain = new DuplicateEffectsDomain();
        session.BindSpatialInteractionDomain("test", domain);
        Assert.Throws<InvalidOperationException>(() => session.AdvanceTo(SimulationTime.Zero));
        Assert.Equal(0, domain.Commits);
        Assert.False(session.IsHealthy);
    }

    [Fact]
    public void FailedEffectCommitClosesSessionMutationAndCheckpointBoundaries()
    {
        GameSession session = CreateSession(0);
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        session.BindSpatialInteractionDomain("test", new FailingCommitDomain());
        Assert.Throws<InvalidOperationException>(() => session.AdvanceTo(SimulationTime.Zero));
        Assert.False(session.IsHealthy);
        Assert.False(session.CaptureCheckpoint().IsSuccess);
        Assert.Empty(owner.CaptureCheckpoint().Deliveries!);
        Assert.Throws<InvalidOperationException>(() => session.AdvanceTo(new SimulationTime(1)));
        Assert.Throws<InvalidOperationException>(() => owner.Register("after-failure", new ShipId(1), new ShipId(2), 0));
    }

    [Fact]
    public void EvaluationCannotDisposeTheSession()
    {
        GameSession session = CreateSession(0);
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        session.BindSpatialInteractionDomain("test", new DisposingDomain(session));
        Assert.Throws<InvalidOperationException>(() => session.AdvanceTo(SimulationTime.Zero));
        Assert.False(session.IsHealthy);
    }

    [Fact]
    public void DeliveredStationaryContactDoesNotReplayOrAllocateAnEventAfterRestore()
    {
        GameSession live = CreateSession(0);
        InteractionInterestOwner owner = live.RegisterSpatialInteractionDomain("test");
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        var original = new CountingDomain();
        live.BindSpatialInteractionDomain("test", original);
        live.AdvanceTo(SimulationTime.Zero);
        Assert.Single(original.Contacts);
        GameSessionCheckpoint checkpoint = live.CaptureCheckpoint().Value!;
        GameSession restored = GameSession.RestoreCheckpoint(checkpoint).Value!;
        var resumed = new CountingDomain();
        restored.BindSpatialInteractionDomain("test", resumed);

        Assert.Equal(live.AdvanceTo(new SimulationTime(100)), restored.AdvanceTo(new SimulationTime(100)));
        Assert.Empty(resumed.Contacts);
        Assert.Empty(restored.EventRecords);
        Assert.Equal(checkpoint.Engine.Agenda.NextCreationSequence,
            restored.CaptureCheckpoint().Value!.Engine.Agenda.NextCreationSequence);
        Assert.Equal(interest, Assert.Single(restored.GetSpatialInteractionDomain("test")!.CaptureSnapshot()));

        restored.GetSpatialInteractionDomain("test")!.Update(interest.Id, interest.Source, interest.Target, 1);
        restored.AdvanceTo(new SimulationTime(100));
        Assert.Single(resumed.Contacts);
        Assert.Equal(new EventGeneration(1), resumed.Contacts[0].Interest.Generation);
    }

    [Fact]
    public void SpatialRevisionChangeRearmsDeliveredInterestAfterRestore()
    {
        GameSession live = CreateSession(0);
        InteractionInterestOwner owner = live.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        live.BindSpatialInteractionDomain("test", new CountingDomain());
        live.AdvanceTo(SimulationTime.Zero);
        GameSession restored = GameSession.RestoreCheckpoint(live.CaptureCheckpoint().Value!).Value!;
        var resumed = new CountingDomain();
        restored.BindSpatialInteractionDomain("test", resumed);
        restored.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));
        restored.AdvanceTo(SimulationTime.Zero);
        Assert.Single(resumed.Contacts);
    }

    [Fact]
    public void ArrivalDeliveryUsesPostCompletionRevisionAndDoesNotReplayAfterRestore()
    {
        GameSession live = CreateSession(100);
        InteractionInterestOwner owner = live.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        var original = new CountingDomain();
        live.BindSpatialInteractionDomain("test", original);
        live.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));
        GameSessionTestFixture.AdvanceUntilOrderTerminal(live);
        Assert.Single(original.Contacts);
        GameSession restored = GameSession.RestoreCheckpoint(live.CaptureCheckpoint().Value!).Value!;
        var resumed = new CountingDomain();
        restored.BindSpatialInteractionDomain("test", resumed);
        SimulationTime target = new SimulationTime(live.CurrentTime.Milliseconds + 10);
        Assert.Equal(live.AdvanceTo(target), restored.AdvanceTo(target));
        Assert.Empty(resumed.Contacts);
        Assert.Empty(restored.EventRecords);
    }

    [Fact]
    public void RestoreRejectsDeliveryHistoryFromAFutureSpatialRevision()
    {
        GameSession live = CreateSession(0);
        InteractionInterestOwner owner = live.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        live.BindSpatialInteractionDomain("test", new CountingDomain());
        live.AdvanceTo(SimulationTime.Zero);
        GameSessionCheckpoint checkpoint = live.CaptureCheckpoint().Value!;
        SpatialInteractionRegistrationCheckpoint registration = checkpoint.SpatialInteractions!;
        InteractionInterestOwnerCheckpoint domain = Assert.Single(registration.Domains);
        InteractionDeliveryCorrelation delivery = Assert.Single(domain.Deliveries!);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint with
        {
            SpatialInteractions = registration with
            {
                Domains = [domain with { Deliveries = [delivery with { SourceRevision = new SpatialRevision(1) }] }],
            },
        });
        Assert.False(restored.IsSuccess);
    }

    [Fact]
    public void DeliveredMovingContactDoesNotReplayWhileItsTrajectoryIsUnchanged()
    {
        GameSession live = CreateSession(10);
        InteractionInterestOwner owner = live.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 1);
        var original = new CountingDomain();
        live.BindSpatialInteractionDomain("test", original);
        live.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));
        live.AdvanceTo(new SimulationTime(1_345));
        Assert.Single(original.Contacts);
        Assert.NotNull(live.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).Maneuver);
        GameSession restored = GameSession.RestoreCheckpoint(live.CaptureCheckpoint().Value!).Value!;
        int priorEvents = live.EventRecords.Count;
        var resumed = new CountingDomain();
        restored.BindSpatialInteractionDomain("test", resumed);
        Assert.Equal(live.AdvanceTo(new SimulationTime(1_347)), restored.AdvanceTo(new SimulationTime(1_347)));
        Assert.Empty(resumed.Contacts);
        Assert.Empty(restored.EventRecords);
        Assert.Equal(live.AdvanceTo(new SimulationTime(2_000)), restored.AdvanceTo(new SimulationTime(2_000)));
        Assert.Equal(live.EventRecords.Skip(priorEvents), restored.EventRecords);
        Assert.Single(resumed.Contacts);
        Assert.Equal(2, original.Contacts.Count);
    }

    [Fact]
    public void RevisionDuringEffectCommitCannotMarkTheNewInterestAsAlreadyDelivered()
    {
        GameSession session = CreateSession(0);
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        var domain = new RevisingDomain(owner);
        session.BindSpatialInteractionDomain("test", domain);
        session.AdvanceTo(SimulationTime.Zero);
        Assert.Equal<EventGeneration>([default, new(1)], domain.Generations);
        Assert.Equal(new EventGeneration(1), Assert.Single(owner.CaptureCheckpoint().Deliveries!).Generation);
    }

    private sealed class RevisingDomain(InteractionInterestOwner owner) : ISpatialInteractionDomainEvaluator
    {
        internal List<EventGeneration> Generations { get; } = [];
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            InteractionCandidate contact = contacts[0];
            Generations.Add(contact.Interest.Generation);
            return contact.Interest.Generation.Value == 0
                ? [new(contact.Key, 0, 0, new ReviseEffect(owner, contact.Interest.Id))] : [];
        }
        private sealed class ReviseEffect(InteractionInterestOwner owner, InteractionInterestId id) : ISpatialInteractionEffect
        {
            public void Commit(GameSession session) => owner.Update(id, new ShipId(1), new ShipId(2), 1);
        }
    }

    private sealed class CountingDomain : ISpatialInteractionDomainEvaluator
    {
        internal List<InteractionCandidate> Contacts { get; } = [];
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            Contacts.AddRange(contacts);
            return [];
        }
    }

    private sealed class DisposingDomain(GameSession session) : ISpatialInteractionDomainEvaluator
    {
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            session.Dispose();
            return [];
        }
    }

    private sealed class FailingCommitDomain : ISpatialInteractionDomainEvaluator, ISpatialInteractionEffect
    {
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts) => [new(contacts[0].Key, 0, 0, this)];
        public void Commit(GameSession session) => throw new InvalidOperationException("Domain commit failed.");
    }

    private sealed class DuplicateEffectsDomain : ISpatialInteractionDomainEvaluator, ISpatialInteractionEffect
    {
        internal int Commits { get; private set; }
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            var effect = new SpatialInteractionEffectProposal(contacts[0].Key, 0, 0, this);
            return [effect, effect];
        }
        public void Commit(GameSession session) => Commits++;
    }

    private sealed class MutatingDomain(InteractionInterestOwner owner) : ISpatialInteractionDomainEvaluator
    {
        private bool _attempted;
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            if (!_attempted)
            {
                _attempted = true;
                owner.Register("illegal", new ShipId(1), new ShipId(2), 0);
            }
            return [];
        }
    }

    private sealed class RecordingDomain(string name, InteractionInterestOwner owner, List<string> trace,
        bool moveOnCommit = false) : ISpatialInteractionDomainEvaluator
    {
        internal InteractionSpatialReadView? View { get; private set; }
        internal int Commits { get; private set; }
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            View = view;
            trace.Add($"evaluate:{name}");
            return contacts.Select(contact => new SpatialInteractionEffectProposal(contact.Key, 0, 0,
                new Effect(this, contact.Interest.Id))).ToArray();
        }
        private void Apply(GameSession session, InteractionInterestId interestId)
        {
            trace.Add($"commit:{name}");
            Commits++;
            owner.Cancel(interestId);
            if (moveOnCommit) session.SubmitCommand(GameSessionTestFixture.Player,
                new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));
        }
        private sealed class Effect(RecordingDomain domain, InteractionInterestId interestId) : ISpatialInteractionEffect
        {
            public void Commit(GameSession session) => domain.Apply(session, interestId);
        }
    }

    private static GameSession CreateSession(long targetX)
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(targetX, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(new FacilityId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder, [GameSessionTestFixture.Design])],
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, factRetentionCapacity: 64);
        return new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
    }
}
