using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionInteractionConnectorTests
{
    private static readonly ShipId Traveler = new(1);
    private static readonly ShipId Target = new(2);
    private static readonly SystemId Origin = new(1);
    private static readonly SystemId Destination = new(2);

    [Fact]
    public void TransitHasNoLocalContactAndEmergenceWakesExistingInterest()
    {
        using GameSession session = CreateSession();
        var domain = Bind(session);
        Depart(session);
        session.AdvanceTo(new SimulationTime(49));
        Assert.Null(Ship(session, Traveler).Position);
        Assert.NotNull(Ship(session, Traveler).Transit);
        Assert.Empty(domain.Contacts);
        Assert.DoesNotContain(session.EventRecords, record => record.Kind is GameEventKind.SpatialInteraction);

        session.AdvanceTo(new SimulationTime(50));

        InteractionCandidate contact = Assert.Single(domain.Contacts);
        Assert.Equal(Destination, contact.SystemId);
        Assert.Equal(new SimulationTime(50), contact.Key.Timestamp);
        Assert.True(contact.Contact.AlreadyInRange);
        Assert.True(contact.Contact.InRangeAtTimestamp);
        InteractionSpatialReadView view = Assert.Single(domain.Views);
        Assert.Equal(Position(Destination), view.Get(Traveler)!.Spatial.Position);
        Assert.Null(view.Get(Traveler)!.Spatial.Transit);
        session.AdvanceTo(new SimulationTime(100));
        Assert.Single(domain.Contacts);
    }

    [Fact]
    public void CheckpointDuringTransitPreservesEmergenceDeliveryAndEventOrder()
    {
        using GameSession live = CreateSession();
        var original = Bind(live);
        Depart(live);
        live.AdvanceTo(new SimulationTime(25));
        Assert.Empty(original.Contacts);
        CheckpointResult<GameSessionCheckpoint> capture = live.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        Assert.DoesNotContain(capture.Value!.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.SpatialInteraction);
        CheckpointResult<GameSession> restore = GameSession.RestoreCheckpoint(capture.Value!);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        using GameSession restored = restore.Value!;
        var resumed = new RecordingDomain();
        restored.BindSpatialInteractionDomain("connector-test", resumed);
        int priorEvents = live.EventRecords.Count;

        Assert.Equal(live.AdvanceTo(new SimulationTime(49)), restored.AdvanceTo(new SimulationTime(49)));
        Assert.Empty(original.Contacts);
        Assert.Empty(resumed.Contacts);
        Assert.Equal(live.AdvanceTo(new SimulationTime(100)), restored.AdvanceTo(new SimulationTime(100)));
        Assert.Equal(original.Contacts, resumed.Contacts);
        Assert.Single(resumed.Contacts);
        Assert.Equal(live.EventRecords.Skip(priorEvents), restored.EventRecords);
        Assert.Equal(Ship(live, Traveler).SpatialState, Ship(restored, Traveler).SpatialState);
        Assert.Equal(live.CaptureCheckpoint().Value!.Engine.Agenda.NextCreationSequence,
            restored.CaptureCheckpoint().Value!.Engine.Agenda.NextCreationSequence);
    }

    [Fact]
    public void CancelingInterestDuringTransitPreventsEmergenceDelivery()
    {
        using GameSession session = CreateSession();
        var domain = Bind(session);
        InteractionInterestOwner owner = session.GetSpatialInteractionDomain("connector-test")!;
        ShipPairInteractionInterest interest = Assert.Single(owner.CaptureSnapshot());
        Depart(session);
        session.AdvanceTo(new SimulationTime(25));
        Assert.True(owner.Cancel(interest.Id));

        session.AdvanceTo(new SimulationTime(100));

        Assert.Equal(Position(Destination), Ship(session, Traveler).Position);
        Assert.Empty(domain.Contacts);
        Assert.DoesNotContain(session.EventRecords, record => record.Kind is GameEventKind.SpatialInteraction);
    }

    [Fact]
    public void RemovingTransitParticipantPreventsEmergenceAndContact()
    {
        using GameSession session = CreateSession();
        var domain = Bind(session);
        Depart(session);
        session.AdvanceTo(new SimulationTime(25));
        Assert.IsType<EntityRemovalResult.Removed>(session.RemoveEntity(new EntityRemovalRequest(
            new EntityId(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo)));

        session.AdvanceTo(new SimulationTime(100));

        Assert.DoesNotContain(session.CaptureSnapshot().Ships, ship => ship.Id == Traveler);
        Assert.Empty(domain.Contacts);
        Assert.DoesNotContain(session.EventRecords, record => record.Kind is GameEventKind.SpatialInteraction);
        Assert.Single(session.GetSpatialInteractionDomain("connector-test")!.CaptureSnapshot());
    }

    [Fact]
    public void SharingAConnectionDoesNotCreateContactDuringTransit()
    {
        using GameSession session = CreateSession(targetAtOrigin: true);
        var domain = Bind(session);
        Depart(session);
        Depart(session, Target);
        session.AdvanceTo(new SimulationTime(49));
        Assert.NotNull(Ship(session, Traveler).Transit);
        Assert.NotNull(Ship(session, Target).Transit);
        Assert.Empty(domain.Contacts);

        session.AdvanceTo(new SimulationTime(50));

        InteractionCandidate contact = Assert.Single(domain.Contacts);
        Assert.Equal(new SimulationTime(50), contact.Key.Timestamp);
        Assert.Equal(Destination, contact.SystemId);
        InteractionSpatialReadView view = Assert.Single(domain.Views);
        Assert.Null(view.Get(Traveler)!.Spatial.Transit);
        Assert.Null(view.Get(Target)!.Spatial.Transit);
    }

    [Fact]
    public void EmergenceCannotContactATargetRemainingInAnotherSystem()
    {
        using GameSession session = CreateSession(targetAtOrigin: true);
        var domain = Bind(session);
        Depart(session);

        session.AdvanceTo(new SimulationTime(100));

        Assert.Equal(Position(Destination), Ship(session, Traveler).Position);
        Assert.Equal(Position(Origin), Ship(session, Target).Position);
        Assert.Empty(domain.Contacts);
        Assert.DoesNotContain(session.EventRecords, record => record.Kind is GameEventKind.SpatialInteraction);
    }

    private sealed class RecordingDomain : ISpatialInteractionDomainEvaluator
    {
        internal List<InteractionCandidate> Contacts { get; } = [];
        internal List<InteractionSpatialReadView> Views { get; } = [];

        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts)
        {
            Contacts.AddRange(contacts);
            Views.Add(view);
            return [];
        }
    }

    private static RecordingDomain Bind(GameSession session)
    {
        InteractionInterestOwner owner = session.RegisterSpatialInteractionDomain("connector-test");
        owner.Register("emergence-contact", Traveler, Target, 0);
        var domain = new RecordingDomain();
        session.BindSpatialInteractionDomain("connector-test", domain);
        return domain;
    }

    private static void Depart(GameSession session, ShipId? actor = null)
    {
        GameplayCommandRecord record = session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(actor ?? Traveler, new NavigationDestination.System(Destination), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
        Assert.NotNull(Ship(session, actor ?? Traveler).Transit);
    }

    private static GameShipSnapshot Ship(GameSession session, ShipId id) => session.CaptureSnapshot().Ships.Single(ship => ship.Id == id);

    private static GameSession CreateSession(bool targetAtOrigin = false)
    {
        var topology = new ConnectorTopology(
            [new ConnectorEndpoint(new ConnectorEndpointId(1), Position(Origin)),
             new ConnectorEndpoint(new ConnectorEndpointId(2), Position(Destination))],
            [new TransitConnection(new TransitConnectionId(1), new ConnectorEndpointId(1),
                new ConnectorEndpointId(2), new SimulationDuration(50))]);
        var setup = new GameSessionSetup(
            [new StarSystem(Origin, "Origin"), new StarSystem(Destination, "Destination")],
            [new InitialShipSetup(new EntityId(1), Traveler, new InventoryId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, Position(Origin), GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new EntityId(2), Target, new InventoryId(2), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, Position(targetAtOrigin ? Origin : Destination), GameSessionTestFixture.PlayerController)],
            topology,
            [new ShipMaterializationPolicy(new FacilityId(1), GameSessionTestFixture.Principal,
                Position(Origin), GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder,
                [GameSessionTestFixture.Design])],
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, factRetentionCapacity: 64, playerCommandSource: GameSessionTestFixture.Player);
        return new GameSession(setup, new HierarchicalNavigationPlanner(topology, new ChebyshevLocalTravelTimeEstimator(1)));
    }

    private static SystemPosition Position(SystemId system) => new(system,
        new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0)));
}
