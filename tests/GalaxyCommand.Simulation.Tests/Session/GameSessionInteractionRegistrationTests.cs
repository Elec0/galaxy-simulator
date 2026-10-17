using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionInteractionRegistrationTests
{
    [Fact]
    public void RegisteredDomainsShareSessionIdsAndRejectDuplicateRegistration()
    {
        GameSession session = CreateSession();
        InteractionInterestOwner first = session.RegisterSpatialInteractionDomain("first");
        InteractionInterestOwner second = session.RegisterSpatialInteractionDomain("second");
        Assert.Equal(new InteractionInterestId(1), Register(first).Id);
        Assert.Equal(new InteractionInterestId(2), Register(second).Id);
        Assert.Same(first, session.GetSpatialInteractionDomain("first"));
        Assert.Throws<InvalidOperationException>(() => session.RegisterSpatialInteractionDomain("first"));
        Assert.Null(session.GetSpatialInteractionDomain("absent"));
    }

    [Fact]
    public void SessionCheckpointRestoresInterestsAndExactAllocatorAfterCancellation()
    {
        GameSession session = CreateSession();
        InteractionInterestOwner first = session.RegisterSpatialInteractionDomain("first");
        InteractionInterestOwner second = session.RegisterSpatialInteractionDomain("second");
        ShipPairInteractionInterest live = Register(first);
        ShipPairInteractionInterest revised = first.Update(live.Id, new ShipId(2), new ShipId(1), 5);
        first.Cancel(Register(first).Id);
        second.Cancel(Register(second).Id);
        session.AdvanceTo(SimulationTime.Zero);
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        GameSessionCheckpoint checkpoint = capture.Value!;
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        GameSession restored = result.Value!;
        InteractionInterestOwner restoredFirst = Assert.IsType<InteractionInterestOwner>(restored.GetSpatialInteractionDomain("first"));
        InteractionInterestOwner restoredSecond = Assert.IsType<InteractionInterestOwner>(restored.GetSpatialInteractionDomain("second"));
        Assert.Equal(revised, Assert.Single(restoredFirst.CaptureSnapshot()));
        Assert.Empty(restoredSecond.CaptureSnapshot());
        Assert.Equal(new InteractionInterestId(4), Register(restoredSecond).Id);
        Assert.Equal(new InteractionInterestId(5), Register(restored.RegisterSpatialInteractionDomain("third")).Id);
        Assert.Single(first.CaptureSnapshot());
        Assert.Empty(second.CaptureSnapshot());
    }

    [Fact]
    public void RestoreRejectsMissingRegistryAndDuplicateDomains()
    {
        GameSession session = CreateSession();
        session.RegisterSpatialInteractionDomain("first");
        session.AdvanceTo(SimulationTime.Zero);
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        GameSessionCheckpoint checkpoint = capture.Value!;
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { SpatialInteractions = null }).IsSuccess);
        SpatialInteractionRegistrationCheckpoint registration = Assert.IsType<SpatialInteractionRegistrationCheckpoint>(checkpoint.SpatialInteractions);
        InteractionInterestOwnerCheckpoint domain = Assert.Single(registration.Domains);
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with
        {
            SpatialInteractions = registration with { Domains = new[] { domain, domain } },
        }).IsSuccess);
    }

    [Fact]
    public void RestoreRejectsUnallocatedAndCrossDomainDuplicateIdentities()
    {
        GameSession session = CreateSession();
        InteractionInterestOwner first = session.RegisterSpatialInteractionDomain("first");
        ShipPairInteractionInterest live = Register(first);
        session.AdvanceTo(SimulationTime.Zero);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        SpatialInteractionRegistrationCheckpoint registration = checkpoint.SpatialInteractions!;
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with
        {
            SpatialInteractions = registration with { Ids = new IdSequenceCheckpoint(1) },
        }).IsSuccess);
        var duplicate = new ShipPairInteractionInterest(live.Id, "second", "contact",
            new ShipId(1), new ShipId(2), 0, default);
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with
        {
            SpatialInteractions = registration with
            {
                Domains = new[] { registration.Domains[0], new InteractionInterestOwnerCheckpoint("second", new[] { duplicate }) },
            },
        }).IsSuccess);
    }

    private static GameSession CreateSession()
    {
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(new FacilityId(1), GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder, [GameSessionTestFixture.Design])],
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, factRetentionCapacity: 64);
        return new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
    }

    private static ShipPairInteractionInterest Register(InteractionInterestOwner owner) =>
        owner.Register("contact", new ShipId(1), new ShipId(2), 0);
}
