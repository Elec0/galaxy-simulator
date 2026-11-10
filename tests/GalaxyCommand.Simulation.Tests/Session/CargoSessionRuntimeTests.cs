using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoSessionRuntimeTests
{
    private static readonly PhysicalDefinition Sensor = new(QualifiedContentKey.Create("test.cargo", "equipment", "sensor"),
        PhysicalHoldingKind.Discrete, new(1));

    [Theory]
    [InlineData("position")]
    [InlineData("command")]
    public void SessionRejectsUncommittedSourcePositionOrUnallocatedCommand(string changed)
    {
        CargoDispositionPolicy policy = Policy();
        using GameSession session = Create(policy);
        CargoDispositionRequest request = Request(policy);
        request = changed == "position" ? request with { SourcePosition = GameSessionTestFixture.Position(42, 0) }
            : request with { Key = request.Key with { Trigger = new CargoDispositionTrigger.Command(new(999)) } };
        CargoDispositionCommitResult result = session.CommitCargoDisposition(request);
        Assert.Equal(CargoDispositionFailure.InvalidInput, result.Failure);
        Assert.Empty(session.CaptureCargoAuthority());
        Assert.Equal(3, session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories[0].DiscreteItems.Count);
    }

    [Fact]
    public void ExactReplayAfterSourceMovementRetainsTheCommittedAnchor()
    {
        CargoDispositionPolicy policy = Policy(1_000_000);
        using GameSession session = Create(policy);
        CargoDispositionReceipt receipt = session.CommitCargoDisposition(Request(policy)).Receipt!;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll)).Result.Status);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
        Assert.Equal(GameSessionTestFixture.Position(100, 0), Assert.Single(session.CaptureSnapshot().Ships).Position);
        CargoDispositionCommitResult replay = session.CommitCargoDisposition(Request(policy));
        Assert.Same(receipt, replay.Receipt);
        Assert.False(replay.WasApplied);
    }

    [Fact]
    public void DispositionSchedulesExpiryInStateUpdateAndRemovesCargoAtTheExactBoundary()
    {
        CargoDispositionPolicy policy = Policy();
        using GameSession session = Create(policy);
        var result = session.CommitCargoDisposition(Request(policy));
        Assert.True(result.WasApplied);
        CargoLiveObject cargo = Assert.Single(session.CaptureCargoAuthority());
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        Assert.NotNull(checkpoint.Cargo);
        ScheduledEvent<GameEvent> expiry = Assert.Single(checkpoint.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.CargoExpiry);
        Assert.Equal(EventPhase.StateUpdate, expiry.Key.Phase);
        Assert.Equal(cargo.ExpiryEvent, expiry.Key);
        session.AdvanceTo(new(999));
        Assert.Single(session.CaptureCargoAuthority());
        session.AdvanceTo(new(1000));
        Assert.Empty(session.CaptureCargoAuthority());
        Assert.Equal(ScheduledEventDisposition.Applied, Assert.Single(session.EventRecords, item => item.Kind is GameEventKind.CargoExpiry).Disposition);
        GameSessionCheckpoint after = session.CaptureCheckpoint().Value!;
        Assert.Empty(after.Lifecycle.CargoObjects);
        Assert.Single(after.Cargo!.Fields.Tombstones);
        Assert.Single(after.Cargo.Receipts);
    }

    [Fact]
    public void SessionCheckpointRestoresPendingExpiryAndConservesIdentityThroughContinuation()
    {
        CargoDispositionPolicy policy = Policy();
        using GameSession original = Create(policy);
        Assert.True(original.CommitCargoDisposition(Request(policy)).WasApplied);
        original.AdvanceTo(new(500));
        GameSessionCheckpoint saved = original.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> restoration = GameSession.RestoreCheckpoint(saved, new PhysicalDefinitionCatalog([Sensor]));
        Assert.True(restoration.IsSuccess, restoration.Failure?.Message);
        using GameSession restored = restoration.Value!;
        Assert.Equal(original.CaptureCargoAuthority(), restored.CaptureCargoAuthority());
        Assert.False(restored.CommitCargoDisposition(Request(policy)).WasApplied);
        original.AdvanceTo(new(1000));
        restored.AdvanceTo(new(1000));
        Assert.Equal(original.CaptureCargoAuthority(), restored.CaptureCargoAuthority());
        GameSessionCheckpoint expected = original.CaptureCheckpoint().Value!;
        GameSessionCheckpoint actual = restored.CaptureCheckpoint().Value!;
        Assert.Equal(expected.Lifecycle.EntityIds, actual.Lifecycle.EntityIds);
        Assert.Equal(expected.Lifecycle.InventoryIds, actual.Lifecycle.InventoryIds);
        Assert.Equal(expected.Cargo!.Fields.Tombstones, actual.Cargo!.Fields.Tombstones);
        Assert.Equal(expected.Engine.Agenda.NextCreationSequence, actual.Engine.Agenda.NextCreationSequence);
        Assert.Equal(expected.Engine.Agenda.PendingEvents, actual.Engine.Agenda.PendingEvents);
    }

    [Fact]
    public void SessionRestoreRejectsMissingCargoAuthorityOrAChangedExpiryKey()
    {
        CargoDispositionPolicy policy = Policy();
        using GameSession session = Create(policy);
        Assert.True(session.CommitCargoDisposition(Request(policy)).WasApplied);
        GameSessionCheckpoint saved = session.CaptureCheckpoint().Value!;
        Assert.False(GameSession.RestoreCheckpoint(saved with { Cargo = null }, new PhysicalDefinitionCatalog([Sensor])).IsSuccess);
        Assert.False(GameSession.RestoreCheckpoint(saved with { InventoryCommit = null }, new PhysicalDefinitionCatalog([Sensor])).IsSuccess);
        CargoLiveObject cargo = Assert.Single(saved.Cargo!.Objects);
        Assert.False(GameSession.RestoreCheckpoint(saved with
        {
            Cargo = saved.Cargo with { Objects = [cargo with { ExpiryEvent = cargo.ExpiryEvent with { CreationSequence = 999 } }] },
        }, new PhysicalDefinitionCatalog([Sensor])).IsSuccess);
    }

    private static GameSession Create(CargoDispositionPolicy policy)
    {
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), new(1), GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController, cargo: [Sensor, Sensor, Sensor])],
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed,
            100, GameSessionTestFixture.Player, cargoPolicy: policy);
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll)).Result.Status);
        return session;
    }

    private static CargoDispositionRequest Request(CargoDispositionPolicy policy) => new(
        new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), GameSessionTestFixture.Position(0, 0),
        CargoLossCause.StorageModuleLoss, policy,
        [new CargoDispositionSelection.Discrete(Sensor.Key, new(1)), new CargoDispositionSelection.Discrete(Sensor.Key, new(2)),
            new CargoDispositionSelection.Discrete(Sensor.Key, new(3))], []);

    private static CargoDispositionPolicy Policy(ulong lifetime = 1000) => new(QualifiedContentKey.Create("test.cargo", "cargo-policy", "short-test"),
        1, 1, 1, 1, [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
            new(CargoLossCause.FunctionalEjection, new(1, 1))], 4, 0, new(lifetime), new(1000), new(15000), new(100));
}
