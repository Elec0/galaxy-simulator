using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionOrderTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "item"), PhysicalHoldingKind.Discrete, new(1));

    [Fact]
    public void ObjectOrderStartsTimedCollectionAndTransfersNothingBeforeCompletion()
    {
        using GameSession session = Create();
        EntityId target = session.CaptureVisibleCargo()[0].EntityId;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(0));
        Assert.Equal(ShipOrderReason.CollectingCargo, Ship(session, 2).CurrentOrder!.Reason);
        PhysicalReservation reservation = Assert.Single(Cargo(session, 2).PhysicalReservations);
        Assert.Equal(new Quantity(1), Assert.IsType<PhysicalReservationSubject.IncomingCapacity>(reservation.Subject).Quantity);
        session.AdvanceTo(new(999));
        Assert.Empty(Cargo(session, 2).DiscreteItems);
        Assert.Contains(session.CaptureVisibleCargo(), view => view.EntityId == target);
        session.AdvanceTo(new(1000));
        Assert.Single(Cargo(session, 2).DiscreteItems);
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        Assert.DoesNotContain(session.CaptureVisibleCargo(), view => view.EntityId == target);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 2).CurrentOrder!.Status);
    }

    [Fact]
    public void FullCollectorFailsAtStartAndLeavesTargetAndAgendaCapacityUnclaimed()
    {
        using GameSession session = Create(fullCollector: true);
        EntityId target = session.CaptureVisibleCargo()[0].EntityId;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(0));
        Assert.Equal(ShipOrderStatus.Failed, Ship(session, 2).CurrentOrder!.Status);
        Assert.Equal(ShipOrderReason.ActionCapacityUnavailable, Ship(session, 2).CurrentOrder!.Reason);
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        Assert.Contains(session.CaptureVisibleCargo(), view => view.EntityId == target);
    }

    [Fact]
    public void AcceptedCommandOrderWinsContentionEvenWhenTimerEventsAreAllocatedByShipIdentity()
    {
        using GameSession session = Create(collectors: 2);
        EntityId target = session.CaptureVisibleCargo()[0].EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(3), target, OrderPlacement.ReplaceAll));
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        ScheduledEvent<GameEvent>[] due = session.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents
            .Where(entry => entry.Payload is GameEvent.CargoCollectionCompletion).ToArray();
        Assert.Equal(2, due.Length);
        Assert.Equal(new ShipId(2), ((GameEvent.CargoCollectionCompletion)due[0].Payload).ShipId);
        session.AdvanceTo(new(1000));
        Assert.Single(Cargo(session, 3).DiscreteItems);
        Assert.Empty(Cargo(session, 2).DiscreteItems);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 3).CurrentOrder!.Status);
        Assert.Equal(ShipOrderStatus.Failed, Ship(session, 2).CurrentOrder!.Status);
        Assert.Equal(ShipOrderReason.TargetRemoved, Ship(session, 2).CurrentOrder!.Reason);
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
    }

    [Fact]
    public void EqualTimeCollectionCommitsBeforeCargoExpiry()
    {
        using GameSession session = Create(lifetime: 1000);
        EntityId target = session.CaptureVisibleCargo()[0].EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(1000));
        Assert.Single(Cargo(session, 2).DiscreteItems);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 2).CurrentOrder!.Status);
        Assert.Empty(session.CaptureVisibleCargo());
        Assert.Equal(ScheduledEventDisposition.IgnoredMissingReference,
            Assert.Single(session.EventRecords, entry => entry.Kind is GameEventKind.CargoExpiry expiry && expiry.ObjectId == target).Disposition);
    }

    [Fact]
    public void CancellationReleasesCapacityAndCannotCollectLater()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        session.SubmitCommand(GameSessionTestFixture.Player, new CancelShipOrderCommand(new(2), Ship(session, 2).CurrentOrder!.Id));
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        session.AdvanceTo(new(2000));
        Assert.Empty(Cargo(session, 2).DiscreteItems);
        Assert.Equal(ShipOrderStatus.Cancelled, Ship(session, 2).CurrentOrder!.Status);
        Assert.Equal(3, session.CaptureVisibleCargo().Count);
    }

    [Fact]
    public void FieldOrderCollectsCoveredMembersOneTimerAtATimeAndFinishesWhenEmpty()
    {
        using GameSession session = Create(field: true);
        DebrisFieldId field = session.CaptureVisibleCargo()[0].OriginalFieldId!.Value;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(2), field, [], OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(4000));
        Assert.Equal(4, Cargo(session, 2).DiscreteItems.Count);
        Assert.Empty(session.CaptureVisibleCargo());
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 2).CurrentOrder!.Status);
    }

    [Fact]
    public void ActiveAttemptRestoresWithoutRestartingTimerOrDuplicatingCapacity()
    {
        using GameSession original = Create();
        original.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), original.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        original.AdvanceTo(new(450));
        CheckpointResult<GameSessionCheckpoint> capture = original.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        CheckpointResult<GameSession> restoration = GameSession.RestoreCheckpoint(capture.Value!, new PhysicalDefinitionCatalog([Item]));
        Assert.True(restoration.IsSuccess, restoration.Failure?.Message);
        using GameSession restored = restoration.Value!;
        original.AdvanceTo(new(1000));
        restored.AdvanceTo(new(1000));
        Assert.Equal(Cargo(original, 2).DiscreteItems, Cargo(restored, 2).DiscreteItems);
        Assert.Equal(Ship(original, 2).CurrentOrder, Ship(restored, 2).CurrentOrder);
        Assert.Equal(original.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents,
            restored.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents);
    }

    [Fact]
    public void ApproachStartsCollectionAtRangeEntryAndRetainsCommittedMotion()
    {
        using GameSession session = Create(collectorX: 6000);
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        Assert.NotNull(session.CaptureCheckpoint().Value!.Orders.Actors.Single(actor => actor!.ShipId == new ShipId(2))!.Base!.Active!.MotionId);
        session.AdvanceTo(new(100));
        Assert.Equal(ShipOrderStatus.Active, Ship(session, 2).CurrentOrder!.Status);
        session.AdvanceTo(new(20000));
        Assert.Single(Cargo(session, 2).DiscreteItems);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 2).CurrentOrder!.Status);
    }

    [Fact]
    public void SuspensionReleasesCapacityAndResumptionRequiresAFullNewTimer()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(500));
        var script = new CommandSource(CommandSourceKind.Script, new CommandSourceId("story:cargo"));
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(script,
            new BeginScriptedOverrideCommand(new(2), new ActorOverrideReasonId("test"), default)).Result.Status);
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        session.AdvanceTo(new(2000));
        Assert.Empty(Cargo(session, 2).DiscreteItems);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(script,
            new EndScriptedOverrideCommand(new(2), ScriptedOverrideReleasePolicy.CancelOutstanding, new(1))).Result.Status);
        session.AdvanceTo(new(2999));
        Assert.Empty(Cargo(session, 2).DiscreteItems);
        session.AdvanceTo(new(3000));
        Assert.Single(Cargo(session, 2).DiscreteItems);
    }

    [Fact]
    public void TargetExpiryDuringApproachDoesNotCompleteTheOrderBeforeLocalExecution()
    {
        using GameSession session = Create(lifetime: 1, collectorX: 6000);
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(1));
        Assert.Empty(session.CaptureVisibleCargo());
        Assert.Equal(ShipOrderStatus.Active, Ship(session, 2).CurrentOrder!.Status);
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
        session.AdvanceTo(new(20000));
        Assert.Equal(ShipOrderReason.TargetRemoved, Ship(session, 2).CurrentOrder!.Reason);
        Assert.Empty(Cargo(session, 2).DiscreteItems);
    }

    [Fact]
    public void OwnedProgressInspectsOnlyAnActiveInRangeAttemptAndDoesNotAddMapCoverage()
    {
        using GameSession session = Create();
        Assert.Empty(session.CaptureCargoCollectionProgress());
        CargoObjectView target = session.CaptureVisibleCargo()[0];
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), target.EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        CargoCollectionProgressView progress = Assert.Single(session.CaptureCargoCollectionProgress());
        Assert.Equal(new ShipId(2), progress.ShipId);
        Assert.Equal(Ship(session, 2).CurrentOrder!.Id, progress.OrderId);
        Assert.Equal(target, progress.Target);
        Assert.Equal(new Quantity(1), progress.ReservedQuantity);
        Assert.Equal(new SimulationTime(0), progress.StartedAt);
        Assert.Equal(new SimulationTime(1000), progress.DueAt);
        Assert.Equal(3, session.CaptureVisibleCargo().Count);
        session.AdvanceTo(new(1000));
        Assert.Empty(session.CaptureCargoCollectionProgress());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    public void ContestedCollectionContinuationMatchesSerialAcrossBatchSettingsAndAdvanceChunks(int workers, int batchSize)
    {
        using GameSession reference = Create(collectors: 12, field: true);
        CargoObjectView[] targets = reference.CaptureVisibleCargo().ToArray();
        for (ulong ship = 13; ship >= 2; ship--)
            reference.SubmitCommand(GameSessionTestFixture.Player,
                new CollectCargoObjectCommand(new(ship), targets[(int)(ship % 4)].EntityId, OrderPlacement.ReplaceAll));
        reference.AdvanceTo(new(400));
        GameSessionCheckpoint saved = reference.CaptureCheckpoint().Value!;
        int priorEvents = reference.EventRecords.Count;
        CheckpointResult<GameSession> restoration = GameSession.RestoreCheckpoint(saved,
            new PhysicalDefinitionCatalog([Item]), interactionExecution: new InteractionBatchExecution(workers, batchSize));
        Assert.True(restoration.IsSuccess, restoration.Failure?.Message);
        using GameSession batched = restoration.Value!;
        reference.AdvanceTo(new(2000));
        batched.AdvanceTo(new(999));
        batched.AdvanceTo(new(1000));
        batched.AdvanceTo(new(2000));
        for (ulong ship = 2; ship <= 13; ship++)
        {
            Assert.Equal(Cargo(reference, ship).DiscreteItems, Cargo(batched, ship).DiscreteItems);
            Assert.Equal(Cargo(reference, ship).PhysicalReservations, Cargo(batched, ship).PhysicalReservations);
            Assert.Equal(Ship(reference, ship).CurrentOrder, Ship(batched, ship).CurrentOrder);
        }
        Assert.Equal(reference.EventRecords.Skip(priorEvents), batched.EventRecords);
        Assert.Equal(reference.ReadFactsAfter(null, 256).Facts, batched.ReadFactsAfter(null, 256).Facts);
        Assert.Equal(reference.CaptureCargoAuthority(), batched.CaptureCargoAuthority());
        Assert.Equal(reference.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents,
            batched.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents);
    }

    [Fact]
    public void PresentationCarriesCoveredCargoAndOwnedPickupProgressInTheSameCapture()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        GamePresentationSnapshot presentation = session.CapturePresentation(new(new(1), [new(2)], new(2), null, 64));
        Assert.Equal(session.CaptureVisibleCargo(), presentation.World.CargoObjects);
        Assert.Equal(session.CaptureCargoCollectionProgress(), presentation.World.CargoCollectionProgress);
        Assert.Single(presentation.World.CargoCollectionProgress);
    }

    [Fact]
    public void FieldOrderWithoutAnyFittingCoveredMemberFinishesWithoutApproachMotion()
    {
        using GameSession session = Create(field: true, fullCollector: true, collectorX: 6000);
        DebrisFieldId field = session.CaptureVisibleCargo()[0].OriginalFieldId!.Value;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoFieldCommand(new(2), field, [], OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        Assert.Equal(GameSessionTestFixture.Position(6000, 0),
            Assert.IsType<ShipSpatialSnapshotState.AtPosition>(Ship(session, 2).SpatialState).Position);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 2).CurrentOrder!.Status);
        Assert.Empty(Cargo(session, 2).PhysicalReservations);
    }

    [Fact]
    public void CollectionAdmissionWithMomentumDoesNotFreezeTheShipDuringPickup()
    {
        using GameSession session = Create(collectorX: 4900);
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(2), GameSessionTestFixture.Destination(100000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(4000));
        Assert.NotEqual(ShipVelocity.Zero, Ship(session, 2).Velocity);
        SystemPosition before = Ship(session, 2).Position!.Value;
        EntityId target = session.CaptureVisibleCargo()[0].EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(2), target, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(4500));
        Assert.NotEqual(before, Ship(session, 2).Position);
    }

    [Fact]
    public void AppendedInRangeCollectionRetainsPassiveDragAndItsCompletionDoesNotRestartPickup()
    {
        using GameSession session = Create();
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(2), GameSessionTestFixture.Destination(100000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(100));
        session.SubmitCommand(GameSessionTestFixture.Player, new CancelShipOrderCommand(new(2), Ship(session, 2).CurrentOrder!.Id));
        Assert.Equal(BoundedTerminalPlanKind.PassiveDrag, Ship(session, 2).Maneuver!.PlanKind);
        EntityId target = session.CaptureVisibleCargo()[0].EntityId;
        session.SubmitCommand(GameSessionTestFixture.Player, new CollectCargoObjectCommand(new(2), target, OrderPlacement.Append));
        session.AdvanceTo(new(100));
        CargoCollectionProgressView started = Assert.Single(session.CaptureCargoCollectionProgress());
        session.AdvanceTo(new(600));
        Assert.True(session.IsHealthy);
        Assert.Equal(started.StartedAt, Assert.Single(session.CaptureCargoCollectionProgress()).StartedAt);
        Assert.Equal(new SimulationTime(1100), started.DueAt);
        session.AdvanceTo(new(1100));
        Assert.Single(Cargo(session, 2).DiscreteItems);
        Assert.Equal(ShipOrderStatus.Completed, Ship(session, 2).CurrentOrder!.Status);
    }

    [Fact]
    public void PassivePickupStrictExitReleasesCapacityAndRestoredReentryStartsAFullNewTimer()
    {
        using GameSession original = Create(collectorX: 4900, lifetime: 120000);
        original.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(2), GameSessionTestFixture.Destination(100000, 0), OrderPlacement.ReplaceAll));
        original.AdvanceTo(new(4000));
        original.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), original.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        original.AdvanceTo(new(4000));
        GameSessionCheckpoint started = original.CaptureCheckpoint().Value!;
        CargoCollectionAttempt attempt = Assert.Single(started.CargoCollectionAttempts!.Attempts)!;
        SimulationTime exit = attempt.ExitKey!.Value.Timestamp;
        Assert.True(exit < attempt.DueAt);
        original.AdvanceTo(new(exit.Milliseconds - 1));
        Assert.Single(original.CaptureCargoCollectionProgress());
        original.AdvanceTo(exit);
        Assert.Empty(original.CaptureCargoCollectionProgress());
        Assert.Empty(Cargo(original, 2).PhysicalReservations);
        Assert.Empty(Cargo(original, 2).DiscreteItems);
        Assert.Equal(BoundedTerminalPlanKind.PassiveDrag, Ship(original, 2).Maneuver!.PlanKind);
        GameSessionCheckpoint interrupted = original.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> restoration = GameSession.RestoreCheckpoint(interrupted, new PhysicalDefinitionCatalog([Item]));
        Assert.True(restoration.IsSuccess, restoration.Failure?.Message);
        using GameSession restored = restoration.Value!;
        foreach (GameSession candidate in new[] { original, restored })
        {
            candidate.AdvanceTo(new(5000));
            Assert.Empty(Cargo(candidate, 2).DiscreteItems);
            candidate.AdvanceTo(new(20000));
            for (int boundary = 0; boundary < 64 && candidate.CaptureCargoCollectionProgress().Count == 0; boundary++)
            {
                SimulationTime next = candidate.CaptureCheckpoint().Value!.Engine.Agenda.PendingEvents[0].Key.Timestamp;
                Assert.True(next < new SimulationTime(60000));
                candidate.AdvanceTo(next);
            }
            CargoCollectionProgressView restarted = Assert.Single(candidate.CaptureCargoCollectionProgress());
            Assert.True(restarted.StartedAt > attempt.DueAt);
            Assert.Equal(1000UL, restarted.DueAt.Milliseconds - restarted.StartedAt.Milliseconds);
            candidate.AdvanceTo(new(restarted.DueAt.Milliseconds - 1));
            Assert.Empty(Cargo(candidate, 2).DiscreteItems);
            candidate.AdvanceTo(new(60000));
            Assert.Single(Cargo(candidate, 2).DiscreteItems);
            Assert.Equal(ShipOrderStatus.Completed, Ship(candidate, 2).CurrentOrder!.Status);
            Assert.Equal(new EventGeneration(2), Assert.Single(candidate.CaptureCheckpoint().Value!.CargoCollectionAttempts!.Generations)!.Generation);
        }
        Assert.Equal(original.ReadFactsAfter(null, 256).Facts, restored.ReadFactsAfter(null, 256).Facts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalRejectsUnpreparedCollectionCapacityWithoutChangingSession(bool economy)
    {
        using GameSession session = Create(economy: economy);
        session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoObjectCommand(new(2), session.CaptureVisibleCargo()[0].EntityId, OrderPlacement.ReplaceAll));
        session.AdvanceTo(new(0));
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;
        var facts = session.ReadFactsAfter(null, 256).Facts.ToArray();
        GameShipSnapshot ship = Ship(session, 2);

        Assert.IsType<EntityRemovalResult.Rejected>(session.RemoveEntity(
            new(new(2), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo)));

        Assert.True(session.IsHealthy);
        GameSessionCheckpoint after = session.CaptureCheckpoint().Value!;
        Assert.Equal(before.Engine.Agenda.PendingEvents, after.Engine.Agenda.PendingEvents);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
        Assert.Equal(before.CargoCollectionAttempts!.Attempts, after.CargoCollectionAttempts!.Attempts);
        Assert.Equal(before.Lifecycle.Inventories.Inventories.Single(inventory => inventory.Id == new InventoryId(2)).PhysicalReservations,
            Cargo(session, 2).PhysicalReservations);
        Assert.Equal(ship.CurrentOrder, Ship(session, 2).CurrentOrder);
        Assert.Equal(ship.SpatialState, Ship(session, 2).SpatialState);
        Assert.Equal(facts, session.ReadFactsAfter(null, 256).Facts);
        session.AdvanceTo(new(1000));
        Assert.Single(Cargo(session, 2).DiscreteItems);
    }

    private static GameShipSnapshot Ship(GameSession session, ulong ship) => session.CaptureSnapshot().Ships.Single(value => value.Id == new ShipId(ship));
    private static InventoryCheckpoint Cargo(GameSession session, ulong ship) => session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(value => value.Id == new InventoryId(ship));

    private static GameSession Create(int collectors = 1, bool fullCollector = false, bool field = false, ulong lifetime = 20000, long collectorX = 0, bool economy = false)
    {
        ShipDesign Design(ulong id, ulong capacity) => new(new(id), "Fixture", new ConstructionRecipe([], new Work(1)),
            new(capacity), GameSessionTestFixture.ManeuverCapability, 10000, 5000);
        int count = field ? 8 : 3;
        var ships = new List<InitialShipSetup>
        {
            new(new(1), new(1), new(1), new(1), Design(1, 100), GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, cargo: Enumerable.Repeat(Item, count)),
        };
        for (ulong index = 2; index < (ulong)collectors + 2; index++)
            ships.Add(new(new(index), new(index), new(index), new(1), Design(index, fullCollector ? 1UL : 100UL),
                GameSessionTestFixture.Position(collectorX, 0), GameSessionTestFixture.PlayerController, cargo: fullCollector ? [Item] : []));
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "orders"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 0, new(lifetime), new(1000), new(15000), new(100));
        var navigation = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        GameSessionEconomySetup? economySetup = economy
            ? new([], [], [], [], [], [], [], new HierarchicalLogisticsNavigation(new Dictionary<LocationId, SystemPosition>(), navigation),
                new(SimulationDuration.Zero, new(1), new(1))) : null;
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")], ships, new ConnectorTopology([], []), [], economySetup,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player, cargoPolicy: policy);
        var session = new GameSession(setup, navigation);
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), field ? CargoLossCause.StorageModuleLoss : CargoLossCause.FunctionalEjection, policy,
            Enumerable.Range(1, count).Select(index => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Item.Key, new((ulong)index))).ToArray(), []);
        Assert.True(session.CommitCargoDisposition(request).WasApplied);
        return session;
    }

}
