using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionInteractionExecutionTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    [InlineData(8, 16)]
    public void SessionExecutionChoicePreservesReportsStateAgendaAndDeliveryHistory(int workers, int batchSize)
    {
        using var reference = Create(null);
        using var actual = Create(new InteractionBatchExecution(workers, batchSize));
        var expectedDomain = Register(reference);
        var actualDomain = Register(actual);
        Start(reference); Start(actual);
        Assert.Equal(reference.AdvanceTo(new SimulationTime(2000)), actual.AdvanceTo(new SimulationTime(2000)));
        Assert.Equal(reference.EventRecords, actual.EventRecords);
        AssertSameState(reference, actual);
        Assert.Equal(expectedDomain.Commits, actualDomain.Commits);
        Assert.Equal(128, actualDomain.Commits);
        Assert.All(actualDomain.CallbackThreads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void PendingContinuationCanRestoreWithDifferentExecutionSettings(int workers)
    {
        using var live = Create(new InteractionBatchExecution(4, 7));
        var original = Register(live);
        Start(live);
        live.AdvanceTo(new SimulationTime(100));
        var checkpoint = live.CaptureCheckpoint().Value!;
        Assert.Contains(checkpoint.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.SpatialInteraction);
        var restoration = GameSession.RestoreCheckpoint(checkpoint, new InteractionBatchExecution(workers, 3));
        Assert.True(restoration.IsSuccess, restoration.Failure?.Message);
        using var restored = restoration.Value!;
        Assert.Throws<InvalidOperationException>(() => restored.AdvanceTo(new SimulationTime(2000)));
        Assert.True(restored.IsHealthy);
        var resumed = Bind(restored);
        Assert.Equal(live.AdvanceTo(new SimulationTime(2000)), restored.AdvanceTo(new SimulationTime(2000)));
        Assert.Equal(live.EventRecords, restored.EventRecords);
        AssertSameState(live, restored);
        Assert.Equal(original.Commits, resumed.Commits);
        Assert.All(resumed.CallbackThreads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
    }

    [Fact]
    public void OmittingSettingsOnRestoreUsesSerialAndDoesNotReplayDeliveredContacts()
    {
        using var live = Create(new InteractionBatchExecution(4, 7));
        var original = Register(live);
        Start(live);
        live.AdvanceTo(new SimulationTime(2000));
        var checkpoint = live.CaptureCheckpoint().Value!;
        int priorEvents = live.EventRecords.Count;
        using var restored = GameSession.RestoreCheckpoint(checkpoint).Value!;
        var resumed = Bind(restored);
        Assert.Equal(live.AdvanceTo(new SimulationTime(2100)), restored.AdvanceTo(new SimulationTime(2100)));
        Assert.Equal(128, original.Commits);
        Assert.Equal(0, resumed.Commits);
        Assert.Equal(live.EventRecords.Skip(priorEvents), restored.EventRecords);
        AssertSameState(live, restored);
    }

    [Fact]
    public void ExecutionBindingIsSelectedByCallerInsteadOfCheckpointState()
    {
        var original = new InteractionBatchExecution(4, 7);
        using var live = Create(original);
        Assert.Same(original, live.InteractionExecution);
        var checkpoint = live.CaptureCheckpoint().Value!;
        var replacement = new InteractionBatchExecution(2, 3);
        using var changed = GameSession.RestoreCheckpoint(checkpoint, replacement).Value!;
        using var serial = GameSession.RestoreCheckpoint(checkpoint).Value!;
        Assert.Same(replacement, changed.InteractionExecution);
        Assert.Null(serial.InteractionExecution);
        AssertSameState(live, serial);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitionAwareRestoreSelectsTheSuppliedExecutionBinding(bool withMaterialMapping)
    {
        using var live = Create(null);
        var checkpoint = live.CaptureCheckpoint().Value!;
        var execution = new InteractionBatchExecution(4, 7);
        var definitions = new PhysicalDefinitionCatalog([]);
        var restored = withMaterialMapping
            ? GameSession.RestoreCheckpoint(checkpoint, definitions, new MaterialInventoryCompatibilityMap([]), execution)
            : GameSession.RestoreCheckpoint(checkpoint, definitions, execution);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using var session = restored.Value!;
        Assert.Same(execution, session.InteractionExecution);
        AssertSameState(live, session);
    }

    [Fact]
    public void ParallelForecastingRetainsTheDomainEvaluationMutationGuard()
    {
        using var session = Create(new InteractionBatchExecution(4, 7));
        var owner = session.RegisterSpatialInteractionDomain("test");
        owner.Register("contact", new ShipId(1), new ShipId(2), 10);
        session.BindSpatialInteractionDomain("test", new MutatingDomain(owner));
        Assert.Throws<InvalidOperationException>(() => session.AdvanceTo(SimulationTime.Zero));
        Assert.False(session.IsHealthy);
        Assert.False(session.CaptureCheckpoint().IsSuccess);
        Assert.Single(owner.CaptureSnapshot());
    }

    private sealed class MutatingDomain(InteractionInterestOwner owner) : ISpatialInteractionDomainEvaluator
    {
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view, IReadOnlyList<InteractionCandidate> contacts)
        {
            owner.Register("illegal", new ShipId(1), new ShipId(2), 10);
            return [];
        }
    }

    [Fact]
    public void NewSessionRestoreOverloadRejectsNullExecution()
    {
        using var live = Create(null);
        Assert.Throws<ArgumentNullException>(() => GameSession.RestoreCheckpoint(live.CaptureCheckpoint().Value!,
            interactionExecution: null!));
    }

    /// <summary>Compares the authoritative state touched by this slice without relying on checkpoint container reference equality.</summary>
    private static void AssertSameState(GameSession first, GameSession second)
    {
        var a = first.CaptureCheckpoint().Value!;
        var b = second.CaptureCheckpoint().Value!;
        Assert.Equal(a.Engine.IsInitialized, b.Engine.IsInitialized);
        Assert.Equal(a.Engine.AccruedThrough, b.Engine.AccruedThrough);
        Assert.Equal(a.Engine.Agenda.CurrentTime, b.Engine.Agenda.CurrentTime);
        Assert.Equal(a.Engine.Agenda.NextCreationSequence, b.Engine.Agenda.NextCreationSequence);
        Assert.Equal(a.Engine.Agenda.PendingEvents, b.Engine.Agenda.PendingEvents);
        Assert.Equal(a.Movement.MotionIds, b.Movement.MotionIds);
        Assert.Equal(a.Movement.TransitIds, b.Movement.TransitIds);
        Assert.Equal(a.Movement.Actors.Count, b.Movement.Actors.Count);
        for (int i = 0; i < a.Movement.Actors.Count; i++)
        {
            var x = a.Movement.Actors[i]; var y = b.Movement.Actors[i];
            Assert.Equal((x.ShipId, x.Generation, x.Velocity, x.Heading, x.SpatialRevision),
                (y.ShipId, y.Generation, y.Velocity, y.Heading, y.SpatialRevision));
            if (x.State is ShipSpatialStateCheckpoint.AnalyticManeuver phase)
            {
                var other = Assert.IsType<ShipSpatialStateCheckpoint.AnalyticManeuver>(y.State);
                Assert.Equal((phase.Id, phase.Generation, phase.Objective, phase.CurrentPhaseIndex),
                    (other.Id, other.Generation, other.Objective, other.CurrentPhaseIndex));
                Assert.Equal(phase.Plan.StateAt(a.Engine.Agenda.CurrentTime), other.Plan.StateAt(b.Engine.Agenda.CurrentTime));
                Assert.Equal(phase.Plan.StateAt(phase.Plan.EndsAt), other.Plan.StateAt(other.Plan.EndsAt));
                Assert.Equal(phase.PendingEventKeys, other.PendingEventKeys);
            }
            else Assert.Equal(x.State, y.State);
        }
        Assert.Equal(a.CommandAdmission, b.CommandAdmission);
        Assert.Equal(a.Facts.Sequences, b.Facts.Sequences);
        Assert.Equal(a.Facts.RetainedFacts, b.Facts.RetainedFacts);
        Assert.Equal(a.RuntimePolicies.Navigation, b.RuntimePolicies.Navigation);
        Assert.Equal(a.RuntimePolicies.TravelTime, b.RuntimePolicies.TravelTime);
        Assert.Equal(a.SpatialInteractions!.Ids, b.SpatialInteractions!.Ids);
        Assert.Equal(a.SpatialInteractions.EvaluationDomains, b.SpatialInteractions.EvaluationDomains);
        Assert.Equal(a.SpatialInteractions.Domains.Count, b.SpatialInteractions.Domains.Count);
        for (int i = 0; i < a.SpatialInteractions.Domains.Count; i++)
        {
            Assert.Equal(a.SpatialInteractions.Domains[i].DomainKind, b.SpatialInteractions.Domains[i].DomainKind);
            Assert.Equal(a.SpatialInteractions.Domains[i].Interests, b.SpatialInteractions.Domains[i].Interests);
            Assert.Equal(a.SpatialInteractions.Domains[i].Deliveries, b.SpatialInteractions.Domains[i].Deliveries);
        }
    }

    private static void Start(GameSession session) => session.SubmitCommand(GameSessionTestFixture.Player,
        new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));

    private static Domain Register(GameSession session)
    {
        var owner = session.RegisterSpatialInteractionDomain("test");
        for (int i = 0; i < 64; i++) owner.Register($"contact-{i}", new ShipId(1), new ShipId(2), 1);
        return Bind(session);
    }

    private static Domain Bind(GameSession session)
    {
        var domain = new Domain();
        session.BindSpatialInteractionDomain("test", domain);
        return domain;
    }

    private sealed class Domain : ISpatialInteractionDomainEvaluator
    {
        internal int Commits { get; private set; }
        internal List<int> CallbackThreads { get; } = [];
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view, IReadOnlyList<InteractionCandidate> contacts)
        {
            CallbackThreads.Add(Environment.CurrentManagedThreadId);
            return contacts.Select(contact => new SpatialInteractionEffectProposal(contact.Key, 0, 0, new Effect(this))).ToArray();
        }
        private sealed class Effect(Domain domain) : ISpatialInteractionEffect
        {
            public void Commit(GameSession session)
            {
                domain.CallbackThreads.Add(Environment.CurrentManagedThreadId);
                domain.Commits++;
            }
        }
    }

    private static GameSession Create(InteractionBatchExecution? execution)
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(10, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(new FacilityId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder, [GameSessionTestFixture.Design])],
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, factRetentionCapacity: 64);
        return new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)), execution);
    }
}
