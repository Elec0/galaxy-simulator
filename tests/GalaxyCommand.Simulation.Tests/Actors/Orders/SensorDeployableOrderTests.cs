using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SensorDeployableOrderTests
{
    [Fact]
    public void InRangeCommandsCompleteThroughTheSharedOrderAndInteractionBoundary()
    {
        using GameSession session = CreateSession();
        ItemInstanceId item = SatelliteItem(session);

        GameplayCommandRecord deploy = session.SubmitCommand(new CommandSource(CommandSourceKind.Player,
            new CommandSourceId("local-player")), new DeploySensorCommand(new ShipId(1), item,
                GameSessionTestFixture.Position(2500, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, deploy.Result.Status);
        session.AdvanceTo(session.CurrentTime);

        DeployedSensorSnapshot sensor = Assert.Single(session.CaptureSnapshot().DeployedSensors);
        Assert.Equal(GameSessionTestFixture.Position(2500, 0), sensor.Position);
        SensorDeployableObservationSnapshot observation = session.CaptureSensorDeployableObservation();
        Assert.Equal(sensor.EntityId, Assert.Single(observation.Deployables).EntityId);
        Assert.Equal(session.CurrentTime, Assert.Single(observation.Deployables).ObservedAt);
        Assert.Equal<ulong>(30_000, Assert.Single(observation.Sources).RadiusUnits);
        Assert.Equal(ShipOrderStatus.Completed, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Status);
        GameplayCommandRecord pickup = session.SubmitCommand(new CommandSource(CommandSourceKind.Player,
            new CommandSourceId("local-player")), new PickupSensorCommand(new ShipId(1), sensor.EntityId, OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, pickup.Result.Status);
        session.AdvanceTo(session.CurrentTime);

        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        Assert.Empty(session.CaptureSensorDeployableObservation().Sources);
        Assert.Equal(ShipOrderStatus.Completed, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Status);
        Assert.Single(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorDeployedFact);
        Assert.Single(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
        GamePresentationSnapshot presentation = session.CapturePresentation(new GamePresentationRequest(
            session.CaptureSnapshot().Relationships.PlayerPrincipalId, [], null, null, 64));
        Assert.Single(presentation.Facts.Facts, fact => fact.Fact is SensorDeployedFact);
        Assert.Single(presentation.Facts.Facts, fact => fact.Fact is SensorPickedUpFact);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        restored.Value!.Dispose();
    }

    private static ItemInstanceId SatelliteItem(GameSession session) => session.CaptureCheckpoint().Value!
        .Lifecycle.Inventories.Inventories.Single(inventory => inventory.Id == new InventoryId(1)).DiscreteItems
        .First(item => item.DefinitionKey.LocalId.Value == "satellite").Id;

    [Fact]
    public void OutOfRangeDeploymentExecutesBeforeTheApproachReachesItsDestination()
    {
        using GameSession session = CreateSession();
        GameplayCommandRecord accepted = session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, accepted.Result.Status);
        Assert.Equal(ShipOrderKind.DeploySensor, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Kind);
        ShipSpatialStateCheckpoint.AnalyticManeuver approach = Assert.IsType<ShipSpatialStateCheckpoint.AnalyticManeuver>(
            session.CaptureCheckpoint().Value!.Movement.Actors[0].State);
        GameSessionCheckpoint beforeExecution = session.CaptureCheckpoint().Value!;

        session.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Single(session.CaptureSnapshot().DeployedSensors);
        GameFactEnvelope deployed = Assert.Single(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorDeployedFact);
        Assert.IsType<ScheduledEventFactCause>(deployed.Cause);
        Assert.True(deployed.Timestamp < approach.Plan.EndsAt);
        ShipKinematicState expected = approach.Plan.StateAt(deployed.Timestamp);
        Assert.NotEqual(ShipVelocity.Zero, expected.Velocity);
        ShipLocalMotionEndedFact ended = Assert.IsType<ShipLocalMotionEndedFact>(session.ReadFactsAfter(null, 64).Facts
            .Single(fact => fact.Timestamp == deployed.Timestamp && fact.Fact is ShipLocalMotionEndedFact).Fact);
        Assert.Equal(expected.Position, ended.FinalPosition);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(beforeExecution);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession atExecution = restored.Value!;
        atExecution.AdvanceTo(deployed.Timestamp);
        Assert.Equal(expected.Velocity, Assert.Single(atExecution.CaptureSnapshot().Ships).Velocity);
        Assert.Single(atExecution.CaptureSnapshot().DeployedSensors);
        Assert.Equal(ShipOrderStatus.Completed, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Status);
    }

    [Fact]
    public void PendingActionCheckpointRestoresExactIntentAndContinues()
    {
        using GameSession original = CreateSession();
        original.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(original),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        CheckpointResult<GameSessionCheckpoint> capture = original.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(capture.Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;

        original.AdvanceTo(new SimulationTime(1_000_000));
        resumed.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Equal(original.CaptureSnapshot().DeployedSensors, resumed.CaptureSnapshot().DeployedSensors);
        Assert.Equal(original.ReadFactsAfter(null, 64).Facts, resumed.ReadFactsAfter(null, 64).Facts);
        Assert.Equal(original.EventRecords, resumed.EventRecords);
    }

    [Fact]
    public void CancellationCannotExecuteThePendingActionLater()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        ShipOrderId order = Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Id;
        session.SubmitCommand(Player(), new CancelShipOrderCommand(new ShipId(1), order));

        session.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        Assert.DoesNotContain(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorDeployedFact);
    }

    private static CommandSource Player() => new(CommandSourceKind.Player, new CommandSourceId("local-player"));

    [Fact]
    public void IndependentShipDefinitionMustAgreeWithItsLiveCargoCapacity()
    {
        using GameSession session = CreateSession();
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        ShipDesignPolicyCheckpoint definition = checkpoint.RuntimePolicies.ShipDefinitions![0]!;
        checkpoint = checkpoint with { RuntimePolicies = checkpoint.RuntimePolicies with
        { ShipDefinitions = [definition with { CargoCapacity = new Quantity(definition.CargoCapacity.Units + 1) }] } };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);

        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedWakeCollectionsAreRejectedWithoutThrowingDuringRestore(bool nullEntry)
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        EventAgendaCheckpoint<GameEvent> agenda = checkpoint.Engine.Agenda;
        ScheduledEvent<GameEvent>[] pending = agenda.PendingEvents.Select(scheduled => scheduled.Payload is GameEvent.SensorActionReevaluation
            ? scheduled with { Payload = new GameEvent.SensorActionReevaluation(nullEntry ? [null!] : null!) } : scheduled).ToArray();
        checkpoint = checkpoint with { Engine = new SimulationEngineCheckpoint<GameEvent>(checkpoint.Engine.IsInitialized,
            checkpoint.Engine.AccruedThrough, new EventAgendaCheckpoint<GameEvent>(agenda.CurrentTime, agenda.NextCreationSequence, pending)) };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ReplaceAllCancelsTheOldActionWithoutConsumingItsItemOrExecutingItsStaleForecast()
    {
        using GameSession session = CreateSession();
        ItemInstanceId oldItem = SatelliteItem(session);
        ItemInstanceId replacementItem = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single()
            .DiscreteItems.First(item => item.Id != oldItem).Id;
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), oldItem,
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        GameplayCommandRecord replacement = session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), replacementItem,
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));

        session.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Equal(GameSessionTestFixture.Position(0, 0), Assert.Single(session.CaptureSnapshot().DeployedSensors).Position);
        SensorDeployedFact deployed = Assert.IsType<SensorDeployedFact>(session.ReadFactsAfter(null, 64).Facts.Single(fact => fact.Fact is SensorDeployedFact).Fact);
        Assert.Equal(replacement.Envelope.Sequence, deployed.SourceCommand);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        Assert.Contains(checkpoint.Lifecycle.Inventories.Inventories.Single().DiscreteItems, item => item.Id == oldItem);
        Assert.Equal<ulong?>(3, checkpoint.Lifecycle.EntityIds.NextValue);
        Assert.Contains(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is ShipOrderTransitionFact
        { NextStatus: ShipOrderStatus.Cancelled, Reason: ShipOrderReason.ReplacedByCommand });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoveredNonOwnedPickupAndRemovalHaveTheSameSafeDisappearanceVocabulary(bool pickup)
    {
        using GameSession session = CreateSession(autonomousSecondShip: true, secondShipX: 5000);
        var other = new CommandSource(CommandSourceKind.Autonomous, new CommandSourceId("other"));
        ItemInstanceId item = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(inventory => inventory.Id == new InventoryId(2))
            .DiscreteItems[0].Id;
        session.SubmitCommand(other, new DeploySensorCommand(new ShipId(2), item,
            GameSessionTestFixture.Position(5000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId sensor = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        if (pickup) session.SubmitCommand(other, new PickupSensorCommand(new ShipId(2), sensor, OrderPlacement.ReplaceAll));
        else session.RemoveEntity(new EntityRemovalRequest(sensor, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        session.AdvanceTo(session.CurrentTime);
        var request = new GamePresentationRequest(new PrincipalId(1), [], null, null, 64);

        GamePresentationSnapshot presentation = session.CapturePresentation(request);

        Assert.Collection(presentation.Facts.Facts,
            appeared => Assert.Equal(ObservedSensorChangeKind.Appeared, Assert.IsType<ObservedSensorChangeFact>(appeared.Fact).Kind),
            disappeared => Assert.Equal(ObservedSensorChangeKind.Disappeared, Assert.IsType<ObservedSensorChangeFact>(disappeared.Fact).Kind));
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;
        Assert.Equal(presentation.Facts.Facts, resumed.CapturePresentation(request).Facts.Facts);
    }

    [Fact]
    public void PickupTargetInvalidationStartsTheNextUnrelatedMoveExactlyOnce()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId sensor = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), sensor, OrderPlacement.Append));
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), sensor, OrderPlacement.Append));
        session.SubmitCommand(Player(), new MoveShipCommand(new ShipId(1), GameSessionTestFixture.Destination(15_000, 0), OrderPlacement.Append));

        session.AdvanceTo(session.CurrentTime);

        GameShipSnapshot ship = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderKind.Move, ship.CurrentOrder!.Kind);
        Assert.Equal(ShipOrderStatus.Active, ship.CurrentOrder.Status);
        Assert.NotNull(ship.Maneuver);
        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        Assert.Single(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
        Assert.True(session.CaptureCheckpoint().IsSuccess);
    }

    [Fact]
    public void TransitAndCrossSystemActionAdmissionsCannotReplaceTheCurrentOrder()
    {
        using GameSession session = CreateSession(connectedSystems: true);
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId target = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        SystemPosition destination = new(new SystemId(2), GameSessionTestFixture.Position(0, 0).Position);
        session.SubmitCommand(Player(), new MoveShipCommand(new ShipId(1), new NavigationDestination.Position(destination), OrderPlacement.ReplaceAll));
        GameShipSnapshot transit = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.NotNull(transit.Transit);
        ItemInstanceId item = SatelliteItem(session);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), item,
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll)).Result.Status);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), target,
            OrderPlacement.ReplaceAll)).Result.Status);
        Assert.Equal(transit.CurrentOrder!.Id, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Id);
        session.AdvanceTo(new SimulationTime(100));
        Assert.Equal(destination, Assert.Single(session.CaptureSnapshot().Ships).Position);

        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), item,
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll)).Result.Status);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), target,
            OrderPlacement.ReplaceAll)).Result.Status);

        Assert.Single(session.CaptureSnapshot().DeployedSensors);
        Assert.Equal(transit.CurrentOrder.Id, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Id);
        Assert.Equal(item, SatelliteItem(session));
    }

    [Fact]
    public void RestoreRejectsAWakeThatChangesARetainedOrdersOriginalCommandCorrelation()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        GameplayCommandRecord unrelated = session.SubmitCommand(Player(), new MoveShipCommand(new ShipId(99),
            GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Rejected, unrelated.Result.Status);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        EventAgendaCheckpoint<GameEvent> agenda = checkpoint.Engine.Agenda;
        ScheduledEvent<GameEvent>[] pending = agenda.PendingEvents.Select(scheduled =>
            scheduled.Payload is GameEvent.SensorActionReevaluation wake
                ? scheduled with { Payload = new GameEvent.SensorActionReevaluation(
                    wake.Work.Select(work => work with { Command = unrelated.Envelope.Sequence }).ToArray()) }
                : scheduled).ToArray();
        checkpoint = checkpoint with { Engine = new SimulationEngineCheckpoint<GameEvent>(checkpoint.Engine.IsInitialized,
            checkpoint.Engine.AccruedThrough, new EventAgendaCheckpoint<GameEvent>(agenda.CurrentTime, agenda.NextCreationSequence, pending)) };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);

        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PendingPickupRestoresItsLiveTargetAndContinuesWithTheSameFreshItem()
    {
        using GameSession original = CreateSession(secondShip: true);
        original.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(original),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        original.AdvanceTo(original.CurrentTime);
        EntityId target = Assert.Single(original.CaptureSnapshot().DeployedSensors).EntityId;
        original.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(2), target, OrderPlacement.ReplaceAll));
        original.AdvanceTo(original.CurrentTime);
        int earlierEvents = original.EventRecords.Count;
        CheckpointResult<GameSessionCheckpoint> saved = original.CaptureCheckpoint();
        Assert.True(saved.IsSuccess, saved.Failure?.Message);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(saved.Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;

        original.AdvanceTo(new SimulationTime(1_000_000));
        resumed.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Empty(resumed.CaptureSnapshot().DeployedSensors);
        Assert.Equal(original.ReadFactsAfter(null, 64).Facts, resumed.ReadFactsAfter(null, 64).Facts);
        Assert.Equal(original.EventRecords.Skip(earlierEvents), resumed.EventRecords);
        InventoryCheckpoint expected = original.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(inventory => inventory.Id == new InventoryId(2));
        InventoryCheckpoint actual = resumed.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single(inventory => inventory.Id == new InventoryId(2));
        Assert.Equal(expected.DiscreteItems, actual.DiscreteItems);
        Assert.Equal(original.CaptureCheckpoint().Value!.InventoryCommit!.ItemInstanceIds,
            resumed.CaptureCheckpoint().Value!.InventoryCommit!.ItemInstanceIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SensorRemovalFailsQueuedAndSuspendedPickupIntent(bool suspended)
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId sensor = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        if (!suspended) session.SubmitCommand(Player(), new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(15_000, 0), OrderPlacement.ReplaceAll));
        GameplayCommandRecord pickup = session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), sensor, OrderPlacement.Append));
        var script = new CommandSource(CommandSourceKind.Script, new CommandSourceId("story:test"));
        if (suspended) session.SubmitCommand(script, new BeginScriptedOverrideCommand(new ShipId(1), new ActorOverrideReasonId("test"), default));

        session.RemoveEntity(new EntityRemovalRequest(sensor, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        if (suspended) session.SubmitCommand(script, new EndScriptedOverrideCommand(new ShipId(1),
            ScriptedOverrideReleasePolicy.CancelOutstanding, new ActorControlRevision(1)));
        session.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Contains(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is ShipOrderTransitionFact
        { NextStatus: ShipOrderStatus.Failed, Reason: ShipOrderReason.TargetRemoved, Action: { } action }
            && action.OriginCommandSequence == pickup.Envelope.Sequence);
        Assert.DoesNotContain(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
        Assert.True(session.CaptureCheckpoint().IsSuccess);
    }

    [Fact]
    public void RemovingTheActingShipPublishesTerminalFailuresForItsAcceptedSensorActions()
    {
        using GameSession session = CreateSession();
        ItemInstanceId[] items = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single()
            .DiscreteItems.Take(2).Select(item => item.Id).ToArray();
        foreach (ItemInstanceId item in items)
            session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), item,
                GameSessionTestFixture.Position(15_000, 0), OrderPlacement.Append));

        session.RemoveEntity(new EntityRemovalRequest(new EntityId(1), EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        session.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        ShipOrderTransitionFact[] failed = session.ReadFactsAfter(null, 64).Facts.Select(fact => fact.Fact)
            .OfType<ShipOrderTransitionFact>().Where(fact => fact.NextStatus == ShipOrderStatus.Failed).ToArray();
        Assert.Equal(2, failed.Length);
        Assert.All(failed, fact => Assert.Equal(ShipOrderReason.ActionActorRemoved, fact.Reason));
        Assert.All(failed, fact => Assert.NotNull(fact.Action));
        Assert.True(session.CaptureCheckpoint().IsSuccess);
    }

    [Fact]
    public void UnexpectedFailureDuringExplicitSensorRemovalMakesTheSessionUnusable()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        GameFactEnvelope prior = checkpoint.Facts.RetainedFacts.Single(envelope => envelope!.Fact is SensorDeployedFact)!;
        var finalFact = new GameFactEnvelope(new GameFactSequence(ulong.MaxValue), prior.Timestamp, prior.Cause, prior.Fact)
            { PlayerDisclosure = prior.PlayerDisclosure };
        checkpoint = checkpoint with { Facts = new GameFactStoreCheckpoint(checkpoint.Facts.Capacity,
            new IdSequenceCheckpoint(null), [finalFact]) };
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession exhausted = restored.Value!;
        EntityId sensor = Assert.Single(exhausted.CaptureSnapshot().DeployedSensors).EntityId;

        Assert.Throws<InvalidOperationException>(() => exhausted.RemoveEntity(new EntityRemovalRequest(sensor,
            EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo)));

        Assert.Throws<InvalidOperationException>(() => exhausted.CaptureSnapshot());
    }

    [Theory]
    [InlineData(5_000, true)]
    [InlineData(20_000, false)]
    public void NonOwnedSensorDisclosureIsFrozenAtCommitAndRetainedAcrossRestore(long x, bool initiallyCovered)
    {
        using GameSession session = CreateSession(autonomousSecondShip: true, secondShipX: x);
        var other = new CommandSource(CommandSourceKind.Autonomous, new CommandSourceId("other"));
        ItemInstanceId item = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories
            .Single(inventory => inventory.Id == new InventoryId(2)).DiscreteItems[0].Id;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(other, new DeploySensorCommand(new ShipId(2), item,
            GameSessionTestFixture.Position(x, 0), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(session.CurrentTime);
        DeployedSensorSnapshot sensor = Assert.Single(session.CaptureSnapshot().DeployedSensors);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player(),
            new PickupSensorCommand(new ShipId(1), sensor.EntityId, OrderPlacement.ReplaceAll)).Result.Status);
        GamePresentationRequest request = new(new PrincipalId(1), [], null, null, 64);
        GamePresentationFactEnvelope[] initial = session.CapturePresentation(request).Facts.Facts
            .Where(fact => fact.Fact is ObservedSensorChangeFact).ToArray();
        Assert.Equal(initiallyCovered ? 1 : 0, initial.Length);
        Assert.DoesNotContain(session.CapturePresentation(request).Facts.Facts, fact => fact.Fact is SensorDeployedFact);

        session.SubmitCommand(Player(), new MoveShipCommand(new ShipId(1),
            GameSessionTestFixture.Destination(initiallyCovered ? 50_000 : x, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(1_000_000));
        GamePresentationFactEnvelope[] later = session.CapturePresentation(request).Facts.Facts
            .Where(fact => fact.Fact is ObservedSensorChangeFact).ToArray();
        Assert.Equal(initial, later);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;
        Assert.Equal(session.CapturePresentation(request).Facts.Facts, resumed.CapturePresentation(request).Facts.Facts);
    }

    [Fact]
    public void CompletedActionMustMatchTheItemInItsSuccessReceipt()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        ShipActorOrdersCheckpoint actor = checkpoint.Orders.Actors[0]!;
        ShipOrderCheckpoint terminal = actor.Base!.LastTerminal!;
        ShipActionIntent.DeploySensor action = Assert.IsType<ShipActionIntent.DeploySensor>(terminal.Action);
        ItemInstanceId otherItem = checkpoint.Lifecycle.Inventories.Inventories.Single().DiscreteItems[0].Id;
        terminal = terminal with { Action = new ShipActionIntent.DeploySensor(action.OriginCommandSequence,
            action.CargoInventoryId, action.DefinitionKey, otherItem, action.Position) };
        actor = actor with { Base = new ShipOrderWorkSetCheckpoint(actor.Base.Active, actor.Base.Queue, terminal) };
        checkpoint = checkpoint with { Orders = new ShipOrderCoordinatorCheckpoint(checkpoint.Orders.OrderIds, [actor]) };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);

        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void RestoreRejectsASuccessFactWhoseCommandDoesNotMatchItsAtomicReceipt()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        GameFactEnvelope[] facts = checkpoint.Facts.RetainedFacts.Select(envelope =>
        {
            if (envelope!.Fact is not SensorDeployedFact deployed) return envelope;
            GameFact altered = deployed with { SourceCommand = new CommandSequence(900) };
            return new GameFactEnvelope(envelope.Sequence, envelope.Timestamp, envelope.Cause, altered)
            { PlayerDisclosure = envelope.PlayerDisclosure! with { Fact = altered } };
        }).ToArray();
        checkpoint = checkpoint with { Facts = new GameFactStoreCheckpoint(checkpoint.Facts.Capacity,
            checkpoint.Facts.Sequences, facts) };

        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);

        restored.Value?.Dispose();
        Assert.False(restored.IsSuccess);
    }

    [Fact]
    public void SameWavePickupContentionUsesCommandOrderRatherThanSourceOrContactOrder()
    {
        using GameSession session = CreateSession(secondShip: true);
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(10_000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(new SimulationTime(1_000_000));
        EntityId target = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        GameplayCommandRecord first = session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(2), target, OrderPlacement.Append));
        GameplayCommandRecord second = session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), target, OrderPlacement.Append));
        Assert.Equal(CommandResultStatus.Accepted, first.Result.Status);
        Assert.Equal(CommandResultStatus.Accepted, second.Result.Status);

        session.AdvanceTo(session.CurrentTime);

        SensorPickedUpFact pickup = Assert.IsType<SensorPickedUpFact>(session.ReadFactsAfter(null, 64).Facts
            .Single(fact => fact.Fact is SensorPickedUpFact).Fact);
        Assert.Equal(new ShipId(2), pickup.ActingShipId);
        Assert.Equal(first.Envelope.Sequence, pickup.SourceCommand);
        Assert.Equal(ShipOrderReason.TargetRemoved, session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).CurrentOrder!.Reason);
        Assert.Equal<ulong?>(8, session.CaptureCheckpoint().Value!.InventoryCommit!.ItemInstanceIds.NextValue);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 8)]
    public void WholeActionContinuationIsIdenticalAcrossForecastWorkersAndBatchSizes(int workers, int batchSize)
    {
        using GameSession reference = CreateSession(secondShip: true);
        reference.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(reference),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        ItemInstanceId second = reference.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories
            .Single(inventory => inventory.Id == new InventoryId(2)).DiscreteItems[0].Id;
        reference.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(2), second,
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(reference.CaptureCheckpoint().Value!,
            interactionExecution: new InteractionBatchExecution(workers, batchSize));
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession batched = restored.Value!;

        reference.AdvanceTo(new SimulationTime(1_000_000));
        batched.AdvanceTo(new SimulationTime(10_000));
        batched.AdvanceTo(new SimulationTime(1_000_000));

        Assert.Equal(reference.CaptureSnapshot().DeployedSensors, batched.CaptureSnapshot().DeployedSensors);
        Assert.Equal(reference.ReadFactsAfter(null, 64).Facts, batched.ReadFactsAfter(null, 64).Facts);
        Assert.Equal(reference.EventRecords, batched.EventRecords);
        Assert.Equal(reference.CaptureSensorDeployableObservation().Sources, batched.CaptureSensorDeployableObservation().Sources);
    }

    [Fact]
    public void SuspendedActionCannotExecuteAndRevalidatesOnResumption()
    {
        using GameSession session = CreateSession();
        ItemInstanceId item = SatelliteItem(session);
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), item,
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        var script = new CommandSource(CommandSourceKind.Script, new CommandSourceId("story:test"));
        session.SubmitCommand(script, new BeginScriptedOverrideCommand(new ShipId(1), new ActorOverrideReasonId("test"), default));
        session.AdvanceTo(new SimulationTime(1_000_000));
        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        ShipActionIntent suspended = Assert.Single(Assert.Single(session.CaptureSnapshot().Ships).SuspendedOrders).Action!;
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;
        foreach (GameSession candidate in new[] { session, resumed })
        {
            candidate.CommitInventoryMutations([new ReservePhysicalInventoryProposal(
                new InventoryOperationKey(InventoryOperationSourceKind.Explicit, 1), new InventoryId(1),
                new PhysicalReservationSubject.Discrete(item), new ReservationOwner.TransportJob(new TransportJobId(1)))]);
            candidate.SubmitCommand(script, new EndScriptedOverrideCommand(new ShipId(1),
                ScriptedOverrideReleasePolicy.CancelOutstanding, new ActorControlRevision(1)));
            candidate.AdvanceTo(candidate.CurrentTime);
            ShipOrderSnapshot failed = Assert.Single(candidate.CaptureSnapshot().Ships).CurrentOrder!;
            Assert.Equal(suspended, failed.Action);
            Assert.Equal(ShipOrderReason.ActionItemReserved, failed.Reason);
            Assert.Empty(candidate.CaptureSnapshot().DeployedSensors);
        }
        Assert.Equal(session.ReadFactsAfter(null, 64).Facts, resumed.ReadFactsAfter(null, 64).Facts);
    }

    [Fact]
    public void PickupChecksCapacityAtAdmissionAndAgainAfterAcceptance()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId target = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(Player(),
            new PickupSensorCommand(new ShipId(1), target, OrderPlacement.ReplaceAll)).Result.Status);
        Quantity available = new(Assert.Single(session.CaptureSnapshot().Ships).CargoCapacity.Units - 2);
        session.CommitInventoryMutations([new ReservePhysicalInventoryProposal(
            new InventoryOperationKey(InventoryOperationSourceKind.Explicit, 1), new InventoryId(1),
            new PhysicalReservationSubject.IncomingCapacity(available), new ReservationOwner.TransportJob(new TransportJobId(1)))]);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(Player(),
            new PickupSensorCommand(new ShipId(1), target, OrderPlacement.Append)).Result.Status);

        session.AdvanceTo(session.CurrentTime);

        Assert.Equal(ShipOrderReason.ActionCapacityUnavailable, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Reason);
        Assert.Single(session.CaptureSnapshot().DeployedSensors);
        Assert.DoesNotContain(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
    }

    [Fact]
    public void RestoreCannotDropTheActiveActionsStationaryInterest()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        checkpoint = checkpoint with { SpatialInteractions = checkpoint.SpatialInteractions! with { Domains = [] } };

        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);

        restored.Value?.Dispose();
        Assert.False(restored.IsSuccess);
    }

    [Fact]
    public void DomainConsumersCannotAcquireTheActionOwnersMutableInterestStore()
    {
        using GameSession session = CreateSession();
        Assert.Throws<InvalidOperationException>(() => session.GetSpatialInteractionDomain(ActorOrderRuntimeCoordinator.SensorDomainKind));
        Assert.Throws<InvalidOperationException>(() => session.BindSpatialInteractionDomain(
            ActorOrderRuntimeCoordinator.SensorDomainKind, new EmptyDomainEvaluator()));
    }

    [Fact]
    public void RestoreRejectsAnExternalEvaluatorRequirementForTheManagedSensorDomain()
    {
        using GameSession session = CreateSession();
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        checkpoint = checkpoint with { SpatialInteractions = checkpoint.SpatialInteractions! with
        { EvaluationDomains = [ActorOrderRuntimeCoordinator.SensorDomainKind] } };

        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);

        restored.Value?.Dispose();
        Assert.False(restored.IsSuccess);
    }

    private sealed class EmptyDomainEvaluator : ISpatialInteractionDomainEvaluator
    {
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
            IReadOnlyList<InteractionCandidate> contacts) => [];
    }

    [Fact]
    public void EarlierPickupWinsAndFailsACompetingApproachImmediately()
    {
        using GameSession session = CreateSession(secondShip: true);
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId target = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(2), target, OrderPlacement.Append));
        session.AdvanceTo(session.CurrentTime);
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), target, OrderPlacement.Append));

        session.AdvanceTo(session.CurrentTime);

        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        Assert.Equal(ShipOrderReason.TargetRemoved, session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(2)).CurrentOrder!.Reason);
        Assert.Single(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("cargo")]
    [InlineData("command")]
    [InlineData("source")]
    [InlineData("item")]
    [InlineData("inventory-allocator")]
    public void RestoreRejectsActionIntentThatDisagreesWithItsOrderOrOwners(string corruption)
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        ShipActorOrdersCheckpoint actor = checkpoint.Orders.Actors[0]!;
        ShipOrderCheckpoint active = actor.Base!.Active!;
        ShipActionIntent.DeploySensor action = Assert.IsType<ShipActionIntent.DeploySensor>(active.Action);
        active = active with { Action = new ShipActionIntent.DeploySensor(
            corruption == "command" ? new CommandSequence(900) : action.OriginCommandSequence,
            corruption == "cargo" ? new InventoryId(900) : action.CargoInventoryId,
            action.DefinitionKey, corruption == "item" ? new ItemInstanceId(900) : action.ItemId,
            corruption == "destination" ? GameSessionTestFixture.Position(8_000, 0) : action.Position) };
        if (corruption == "source") active = active with { Source = new CommandSource(CommandSourceKind.Player, new CommandSourceId("foreign")) };
        actor = actor with { Base = new ShipOrderWorkSetCheckpoint(active, actor.Base.Queue, actor.Base.LastTerminal) };
        checkpoint = checkpoint with { Orders = new ShipOrderCoordinatorCheckpoint(checkpoint.Orders.OrderIds, [actor]) };
        if (corruption == "inventory-allocator") checkpoint = checkpoint with { InventoryCommit = null };

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);

        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("future-time")]
    [InlineData("unadmitted-command")]
    [InlineData("item-highwater")]
    public void RestoreRejectsSensorReceiptsOutsideTheirOwningBoundaries(string corruption)
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        SensorActionReceipt receipt = checkpoint.Lifecycle.SensorDeployables.Receipts[0]!;
        if (corruption == "future-time") receipt = receipt with { ExecutedAt = new SimulationTime(1) };
        else if (corruption == "unadmitted-command") receipt = receipt with
        {
            Request = new SensorActionRequest.Deploy(new CommandSequence(900), new ShipId(1),
                ((SensorActionRequest.Deploy)receipt.Request).Item, receipt.Result.Deployed!.Position),
        };
        else receipt = receipt with { Request = new SensorActionRequest.Deploy(receipt.Request.Command,
            new ShipId(1), new ItemInstanceId(900), receipt.Result.Deployed!.Position) };
        EntityLifecycleCheckpoint lifecycle = checkpoint.Lifecycle;
        checkpoint = checkpoint with { Lifecycle = new EntityLifecycleCheckpoint(lifecycle.EntityIds, lifecycle.ShipIds,
            lifecycle.InventoryIds, lifecycle.Inventories, lifecycle.LiveShips, lifecycle.MaterializationReceipts,
            lifecycle.RemovalReceipts, lifecycle.SensorDeployables with { Receipts = [receipt] }) };

        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(checkpoint);

        restored.Value?.Dispose();
        Assert.False(restored.IsSuccess);
    }

    [Fact]
    public void AppendedActionsExecuteInDistinctWavesAtTheSameTimestamp()
    {
        using GameSession session = CreateSession();
        ItemInstanceId[] items = session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories.Single()
            .DiscreteItems.Take(2).Select(item => item.Id).ToArray();
        foreach (ItemInstanceId item in items)
            session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), item,
                GameSessionTestFixture.Position(0, 0), OrderPlacement.Append));

        session.AdvanceTo(session.CurrentTime);

        GameFactEnvelope[] facts = session.ReadFactsAfter(null, 64).Facts.Where(fact => fact.Fact is SensorDeployedFact).ToArray();
        Assert.Equal(2, facts.Length);
        Assert.All(facts, fact => Assert.Equal(SimulationTime.Zero, fact.Timestamp));
        Assert.NotEqual(facts[0].Cause, facts[1].Cause);
        Assert.Equal(2, session.CaptureSnapshot().DeployedSensors.Count);
    }

    [Fact]
    public void PickupInvalidatesQueuedReferencesToTheSameEntity()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        EntityId target = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), target, OrderPlacement.Append));
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), target, OrderPlacement.Append));

        session.AdvanceTo(session.CurrentTime);

        Assert.Single(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
        ShipOrderSnapshot terminal = Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!;
        Assert.Equal(ShipOrderStatus.Failed, terminal.Status);
        Assert.Equal(ShipOrderReason.TargetRemoved, terminal.Reason);
    }

    [Fact]
    public void ExplicitSensorRemovalInvalidatesPickupAndRestoresWithoutAnInventoryOrShip()
    {
        using GameSession session = CreateSession();
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), SatelliteItem(session),
            GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        DeployedSensorSnapshot sensor = Assert.Single(session.CaptureSnapshot().DeployedSensors);
        session.SubmitCommand(Player(), new PickupSensorCommand(new ShipId(1), sensor.EntityId, OrderPlacement.ReplaceAll));
        var request = new EntityRemovalRequest(sensor.EntityId, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo);

        EntityRemovalResult removed = session.RemoveEntity(request);

        Assert.IsType<EntityRemovalResult.SensorRemoved>(removed);
        Assert.Equal(removed, session.RemoveEntity(request));
        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        Assert.Equal(ShipOrderReason.TargetRemoved, Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!.Reason);
        Assert.Single(session.CapturePresentation(new GamePresentationRequest(
            session.CaptureSnapshot().Relationships.PlayerPrincipalId, [], null, null, 64)).Facts.Facts,
            fact => fact.Fact is SensorRemovedFact);
        session.AdvanceTo(session.CurrentTime);
        Assert.DoesNotContain(session.ReadFactsAfter(null, 64).Facts, fact => fact.Fact is SensorPickedUpFact);
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(session.CaptureCheckpoint().Value!);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        using GameSession resumed = restored.Value!;
        Assert.Empty(resumed.CaptureSnapshot().DeployedSensors);
        Assert.Single(resumed.CaptureSnapshot().Ships);
        Assert.Equal(removed, resumed.RemoveEntity(request));
    }

    [Fact]
    public void LosingTheExactItemFailsAtTheNextSameTimeBoundary()
    {
        using GameSession session = CreateSession();
        ItemInstanceId item = SatelliteItem(session);
        session.SubmitCommand(Player(), new DeploySensorCommand(new ShipId(1), item,
            GameSessionTestFixture.Position(15_000, 0), OrderPlacement.ReplaceAll));
        session.AdvanceTo(session.CurrentTime);
        session.CommitInventoryMutations([new ReservePhysicalInventoryProposal(
            new InventoryOperationKey(InventoryOperationSourceKind.Explicit, 1), new InventoryId(1),
            new PhysicalReservationSubject.Discrete(item), new ReservationOwner.TransportJob(new TransportJobId(1)))]);

        session.AdvanceTo(session.CurrentTime);

        ShipOrderSnapshot order = Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder!;
        Assert.Equal(ShipOrderStatus.Failed, order.Status);
        Assert.Equal(ShipOrderReason.ActionItemReserved, order.Reason);
        Assert.Empty(session.CaptureSnapshot().DeployedSensors);
        ShipOrderTransitionFact failed = Assert.IsType<ShipOrderTransitionFact>(session.ReadFactsAfter(null, 64).Facts
            .Single(fact => fact.Fact is ShipOrderTransitionFact transition && transition.NextStatus == ShipOrderStatus.Failed).Fact);
        Assert.Equal(ShipOrderKind.DeploySensor, failed.Kind);
        Assert.Equal(order.Action, failed.Action);
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void NonOwnedSensorFactsUseOnlyCoveredExternalNotices(long x, bool covered)
    {
        using GameSession session = CreateSession();
        GameSnapshot world = session.CaptureSnapshot();
        var coverage = new PlayerCoverageBoundary(world.Time,
            [(PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(GameSessionTestFixture.Position(0, 0),
                world.Time, world.Time), world.Time), 10_000)]);
        var projector = new PlayerFactProjector(Player(), world, world.Ships.ToDictionary(ship => ship.Id), coverage);
        var sensor = new DeployedSensorSnapshot(new EntityId(90), session.CaptureCheckpoint().Value!
            .RuntimePolicies.SensorDeployableDefinitions![0]!.Key!, new PrincipalId(2), GameSessionTestFixture.Position(x, 0));
        GameFact? deployed = projector.Project(new SensorDeployedFact(sensor, new ShipId(2), new CommandSequence(14)));
        GameFact? pickedUp = projector.Project(new SensorPickedUpFact(sensor, new ShipId(2), new CommandSequence(15)));
        GameFact? removed = projector.Project(new SensorRemovedFact(sensor, EntityRemovalReason.Destroyed));

        if (covered)
        {
            Assert.IsType<ObservedSensorChangeFact>(deployed);
            Assert.IsType<ObservedSensorChangeFact>(pickedUp);
            Assert.IsType<ObservedSensorChangeFact>(removed);
        }
        else
        {
            Assert.Null(deployed);
            Assert.Null(pickedUp);
            Assert.Null(removed);
        }
    }

    private static GameSession CreateSession(bool secondShip = false, bool autonomousSecondShip = false,
        long secondShipX = 15_000, bool connectedSystems = false)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GalaxyCommand.slnx"))) root = root.Parent;
        StaticNewGameLoadResult loaded = BuiltInNewGame.Load(Path.Combine(root!.FullName, "content", "built-in"),
            GameSessionTestFixture.RootSeed, 64, 1);
        Assert.True(loaded.IsSuccess);
        GameSessionSetup setup = loaded.Setup!;
        if (secondShip || autonomousSecondShip)
        {
            InitialShipSetup first = setup.Ships[0];
            RelationshipSetup relationships = autonomousSecondShip ? new RelationshipSetup(
                setup.Relationships.Principals.Append(new PrincipalDefinition(new PrincipalId(2), new PrincipalContentId("other"), "Other")),
                setup.Relationships.PlayerPrincipalId, setup.Relationships.StandingPolicy, setup.Relationships.Standings) : setup.Relationships;
            setup = new GameSessionSetup(setup.Systems, [first, new InitialShipSetup(new EntityId(2), new ShipId(2),
                new InventoryId(2), autonomousSecondShip ? new PrincipalId(2) : first.PrincipalId, first.Design,
                GameSessionTestFixture.Position(secondShipX, 0), autonomousSecondShip
                    ? new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("other")) : first.BaseController, cargo: first.Cargo)],
                setup.ConnectorTopology, setup.MaterializationPolicies, setup.Economy, relationships,
                setup.RandomRootSeed, setup.FactRetentionCapacity, setup.PlayerCommandSource, setup.SensorDeployableDefinitions);
        }
        if (connectedSystems)
        {
            SystemPosition origin = setup.Ships[0].Position;
            SystemPosition destination = new(new SystemId(2), origin.Position);
            var topology = new ConnectorTopology([new ConnectorEndpoint(new ConnectorEndpointId(1), origin),
                new ConnectorEndpoint(new ConnectorEndpointId(2), destination)],
                [new TransitConnection(new TransitConnectionId(1), new ConnectorEndpointId(1), new ConnectorEndpointId(2), new SimulationDuration(100))]);
            setup = new GameSessionSetup(setup.Systems.Append(new StarSystem(new SystemId(2), "Other system")), setup.Ships,
                topology, setup.MaterializationPolicies, setup.Economy, setup.Relationships, setup.RandomRootSeed,
                setup.FactRetentionCapacity, setup.PlayerCommandSource, setup.SensorDeployableDefinitions);
            return new GameSession(setup, new HierarchicalNavigationPlanner(topology, new ChebyshevLocalTravelTimeEstimator(1)));
        }
        return new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
