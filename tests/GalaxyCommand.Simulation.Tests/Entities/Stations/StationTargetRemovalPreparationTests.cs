using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationTargetRemovalPreparationTests
{
    private static readonly PhysicalDefinition Metal = new(QualifiedContentKey.Create("test.station-target-preparation", "material", "metal"),
        PhysicalHoldingKind.Fungible, new(1));
    private static readonly QualifiedContentKey BaseKey = QualifiedContentKey.Create("test.station-target-preparation", "station", "base");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExhaustedAgendaRejectsDestructionBeforeCancellingInboundOrders(bool continueWithMove)
    {
        using GameSession original = Create();
        StationSnapshot station = Build(original);
        CommandSequence cause = Inbound(original, station, continueWithMove);
        using GameSession restored = ExhaustAgenda(original);
        GameSessionCheckpoint before = restored.CaptureCheckpoint().Value!;

        StationDestructionResult result = restored.CommitStationDestruction(new(new CargoDispositionTrigger.Command(cause),
            station.Id, station.Revision, [], true, 1));

        Assert.Equal(StationDestructionFailure.OwnerConflict, result.Failure);
        Assert.False(result.WasApplied);
        Assert.Single(restored.CaptureSnapshot().Stations);
        GameSessionCheckpoint after = restored.CaptureCheckpoint().Value!;
        Assert.Equal(before.Lifecycle.EntityIds, after.Lifecycle.EntityIds);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
        Assert.Equal(before.Engine.Agenda.PendingEvents, after.Engine.Agenda.PendingEvents);
        Assert.Equal(before.Movement.MotionIds, after.Movement.MotionIds);
        Assert.Equal(before.Movement.TransitIds, after.Movement.TransitIds);
        Assert.Equal(before.Movement.Actors.Select(actor => (actor!.ShipId, actor.Generation, actor.SpatialRevision, actor.Velocity)),
            after.Movement.Actors.Select(actor => (actor!.ShipId, actor.Generation, actor.SpatialRevision, actor.Velocity)));
        Assert.Empty(after.Stations!.Destructions);
    }

    [Fact]
    public void CargoExpiryAndContinuationReserveOneCombinedAgendaBudgetBeforeLoss()
    {
        using GameSession original = Create();
        StationSnapshot station = Build(original);
        CommandSequence cause = Inbound(original, station, true);
        original.CommitInventoryMutations([new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1),
            station.InventoryId, Metal, new(8))]);
        using GameSession restored = ExhaustAgenda(original, ulong.MaxValue - 1);
        GameSessionCheckpoint before = restored.CaptureCheckpoint().Value!;
        StationDestructionResult result = restored.CommitStationDestruction(new(new CargoDispositionTrigger.Command(cause),
            station.Id, station.Revision, [], true, 1));
        Assert.Equal(StationDestructionFailure.NumericExhaustion, result.Failure);
        GameSessionCheckpoint after = restored.CaptureCheckpoint().Value!;
        Assert.Single(restored.CaptureSnapshot().Stations);
        Assert.Empty(after.Cargo!.Receipts);
        Assert.Empty(after.Stations!.Destructions);
        Assert.Equal(before.Lifecycle.EntityIds, after.Lifecycle.EntityIds);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
        Assert.Equal(before.Engine.Agenda.PendingEvents, after.Engine.Agenda.PendingEvents);
    }

    [Fact]
    public void ExhaustedInterruptionRevisionRejectsBeforeCancellingMotionOrStation()
    {
        using GameSession original = Create();
        StationSnapshot station = Build(original);
        CommandSequence cause = Inbound(original, station, true);
        GameSessionCheckpoint checkpoint = original.CaptureCheckpoint().Value!;
        checkpoint = checkpoint with { Movement = new(checkpoint.Movement.MotionIds, checkpoint.Movement.TransitIds,
            checkpoint.Movement.Actors.Select(actor => actor!.ShipId == new ShipId(1)
                ? actor with { SpatialRevision = new(ulong.MaxValue) } : actor)) };
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        StationDestructionResult result = restored.CommitStationDestruction(new(new CargoDispositionTrigger.Command(cause),
            station.Id, station.Revision, [], true, 1));
        Assert.Equal(StationDestructionFailure.OwnerConflict, result.Failure);
        Assert.Single(restored.CaptureSnapshot().Stations);
        Assert.IsType<ShipSpatialSnapshotState.AnalyticManeuver>(restored.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).SpatialState);
        Assert.Empty(restored.CaptureCheckpoint().Value!.Stations!.Destructions);
    }

    [Fact]
    public void ExhaustedAgendaLeavesFullyWorkedBaseWaitingBeforeCancellingInboundOrders()
    {
        using GameSession original = Create();
        StationSnapshot station = Build(original);
        Inbound(original, station, true);
        Accepted(original, new DismantleStationBaseCommand(GameSessionTestFixture.Principal, station.Id, station.Revision, new(2)));
        station = Assert.Single(original.CaptureSnapshot().Stations);
        StationProjectSnapshot project = station.Projects.Single(item => item.Kind == StationProjectKind.Dismantling);
        Accepted(original, new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, project.Id,
            station.Revision, new(2), new(0)));
        using GameSession restored = ExhaustAgenda(original);

        restored.AdvanceTo(new(3));

        StationSnapshot waiting = Assert.Single(restored.CaptureSnapshot().Stations);
        StationProjectSnapshot blocked = waiting.Projects.Single(item => item.Id == project.Id);
        Assert.Equal(StationProjectStatus.Waiting, blocked.Status);
        Assert.Equal(blocked.RequiredWork, blocked.CompletedWork);
        Assert.Equal(ShipOrderStatus.Active, restored.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).CurrentOrder!.Status);
        Assert.Empty(restored.CaptureCheckpoint().Value!.Stations!.Removals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalStopsMotionThenRestoresExactDecisionContinuation(bool continueWithMove)
    {
        using GameSession original = Create();
        StationSnapshot station = Build(original);
        CommandSequence cause = Inbound(original, station, continueWithMove);
        Assert.True(original.CommitStationDestruction(new(new CargoDispositionTrigger.Command(cause),
            station.Id, station.Revision, [], true, 1)).WasApplied);
        GameSessionCheckpoint checkpoint = original.CaptureCheckpoint().Value!;
        StationTargetContinuationCheckpoint continuation = Assert.IsType<StationTargetContinuationCheckpoint>(checkpoint.StationTargetContinuation);
        Assert.Equal(EventPhase.Decision, continuation.Key.Phase);
        Assert.Equal(original.CurrentTime, continuation.Key.Timestamp);
        Assert.Equal(new ShipId(1), Assert.Single(continuation.Ships));
        Assert.IsType<ShipSpatialSnapshotState.AtPosition>(original.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).SpatialState);
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        original.AdvanceTo(new(3));
        restored.AdvanceTo(new(3));
        Assert.Equal(original.CaptureSnapshot().Ships.Select(ship => (ship.Id, ship.Position, ship.Velocity, ship.Heading, ship.CurrentOrder)),
            restored.CaptureSnapshot().Ships.Select(ship => (ship.Id, ship.Position, ship.Velocity, ship.Heading, ship.CurrentOrder)));
        Assert.Null(restored.CaptureCheckpoint().Value!.StationTargetContinuation);
        Assert.Single(restored.EventRecords, record => record.Kind is GameEventKind.StationTargetContinuation);

        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { StationTargetContinuation = null }).IsSuccess);
        if (continueWithMove)
        {
            GameSessionCheckpoint missingProof = checkpoint with { Orders = new(checkpoint.Orders.OrderIds,
                checkpoint.Orders.Actors.Select(actor => actor?.ShipId == new ShipId(1) ? actor with
                { Base = new(actor.Base!.Active, actor.Base.Queue, null) } : actor)) };
            Assert.False(GameSession.RestoreCheckpoint(missingProof).IsSuccess);
        }
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { StationTargetContinuation = continuation with { Ships = [new(999)] } }).IsSuccess);
        EventAgendaCheckpoint<GameEvent> agenda = checkpoint.Engine.Agenda;
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { Engine = new(checkpoint.Engine.IsInitialized,
            checkpoint.Engine.AccruedThrough, new(agenda.CurrentTime, agenda.NextCreationSequence,
                agenda.PendingEvents.Where(entry => entry.Key != continuation.Key).ToArray())) }).IsSuccess);
    }

    [Fact]
    public void ReplacedOrdersBeforeWakeKeepTheirOwnMovementAcrossRestore()
    {
        using GameSession original = Create();
        StationSnapshot station = Build(original);
        CommandSequence cause = Inbound(original, station, true);
        Assert.True(original.CommitStationDestruction(new(new CargoDispositionTrigger.Command(cause),
            station.Id, station.Revision, [], true, 1)).WasApplied);
        Accepted(original, new MoveShipCommand(new(1), new NavigationDestination.Position(GameSessionTestFixture.Position(3000, 0)),
            OrderPlacement.ReplaceAll));
        CheckpointResult<GameSession> loaded = GameSession.RestoreCheckpoint(original.CaptureCheckpoint().Value!);
        Assert.True(loaded.IsSuccess, loaded.Failure?.Message);
        using GameSession restored = loaded.Value!;
        MotionId? motion = restored.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).Maneuver?.MotionId;
        restored.AdvanceTo(new(3));
        Assert.Equal(motion, restored.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).Maneuver?.MotionId);
        Assert.Null(restored.CaptureCheckpoint().Value!.StationTargetContinuation);
    }

    [Fact]
    public void RemovingInterruptedActorCancelsItsOtherwiseEmptyContinuation()
    {
        using GameSession session = Create();
        StationSnapshot station = Build(session);
        CommandSequence cause = Inbound(session, station, true);
        Assert.True(session.CommitStationDestruction(new(new CargoDispositionTrigger.Command(cause),
            station.Id, station.Revision, [], true, 1)).WasApplied);
        Assert.IsType<EntityRemovalResult.Removed>(session.RemoveEntity(new(new(1), EntityRemovalReason.Despawned,
            EntityCargoDisposition.DiscardCargo)));
        CheckpointResult<GameSessionCheckpoint> checkpoint = session.CaptureCheckpoint();
        Assert.True(checkpoint.IsSuccess, checkpoint.Failure?.Message);
        Assert.Null(checkpoint.Value!.StationTargetContinuation);
        Assert.DoesNotContain(checkpoint.Value.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.StationTargetContinuation);
    }

    private static CommandSequence Inbound(GameSession session, StationSnapshot station, bool continueWithMove)
    {
        Accepted(session, new MoveShipCommand(new(1), new NavigationDestination.Entity(station.EntityId), OrderPlacement.ReplaceAll));
        CommandSequence cause = continueWithMove ? Accepted(session, new MoveShipCommand(new(1),
            new NavigationDestination.Position(GameSessionTestFixture.Position(2000, 0)), OrderPlacement.Append)).Envelope.Sequence
            : session.CommandRecords[^1].Envelope.Sequence;
        session.AdvanceTo(session.CurrentTime.Add(new(1)));
        return cause;
    }

    private static GameSession ExhaustAgenda(GameSession session, ulong next = ulong.MaxValue)
    {
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        EventAgendaCheckpoint<GameEvent> agenda = checkpoint.Engine.Agenda;
        checkpoint = checkpoint with { Engine = new(checkpoint.Engine.IsInitialized, checkpoint.Engine.AccruedThrough,
            new(agenda.CurrentTime, next, agenda.PendingEvents)) };
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        return restored.Value!;
    }

    private static StationSnapshot Build(GameSession session)
    {
        Accepted(session, new CreateStationSiteCommand(GameSessionTestFixture.Principal, new(2), BaseKey,
            GameSessionTestFixture.Position(0, 0), new(0)));
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        Accepted(session, new AssignStationBuilderCommand(GameSessionTestFixture.Principal, station.Id, station.Projects[0].Id,
            station.Revision, new(2), new(0)));
        session.AdvanceTo(new(1));
        return Assert.Single(session.CaptureSnapshot().Stations);
    }

    private static GameplayCommandRecord Accepted(GameSession session, GameplayCommand command)
    {
        GameplayCommandRecord record = session.SubmitCommand(GameSessionTestFixture.Player, command);
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
        return record;
    }

    private static GameSession Create()
    {
        var station = new StationDefinition(BaseKey, "Station", 1, new(10), new(10), new([], new(1)), 0, [], new(1), []);
        var ship = new ShipDesign(new(1), "Builder", new([], new(1)), new(10), GameSessionTestFixture.ManeuverCapability,
            0, 0, new StationConstructionCapability(0, new(1000)));
        var cargoPolicy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.station-target-preparation", "cargo-policy", "loss"),
            1, 1, 1, 1, new Dictionary<CargoLossCause, CargoRecoveryFraction>
            {
                [CargoLossCause.EntityDestruction] = new(1, 2), [CargoLossCause.StorageModuleLoss] = new(1, 2),
                [CargoLossCause.FunctionalEjection] = new(1, 1),
            }, 1, 0, new(60000), new(1), new(1), new(1000));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), GameSessionTestFixture.Principal, ship,
                GameSessionTestFixture.Position(1000, 0), GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new(2), new(2), new(2), GameSessionTestFixture.Principal, ship,
                GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed,
            256, GameSessionTestFixture.Player, cargoPolicy: cargoPolicy,
            stations: new(new([station], []), new(new Dictionary<MaterialId, PhysicalDefinition> { [new(1)] = Metal }), StationTerritoryPolicy.Absent));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
