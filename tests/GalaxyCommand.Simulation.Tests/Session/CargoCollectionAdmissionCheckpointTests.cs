using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionAdmissionCheckpointTests
{
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("test.cargo", "equipment", "item"), PhysicalHoldingKind.Discrete, new(1));
    private static readonly PhysicalDefinition NeverHeld = new(QualifiedContentKey.Create("test.cargo", "commodity", "never-held"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinitionCatalog Definitions = new([Item, NeverHeld]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactActiveActionAttemptAndHistoricalAnchorJoinWithoutChangingCheckpoint(bool field)
    {
        GameSessionCheckpoint checkpoint = Create(field);
        Assert.NotNull(checkpoint.CargoCollectionAttempts);
        Assert.NotEmpty(checkpoint.CargoCollectionTargets!);
        Assert.Null(Validate(checkpoint));
    }

    [Fact]
    public void RetainedCompletedObjectIntentCanReferToAnAlreadyCollectedHistoricalObject()
    {
        Assert.Null(Validate(Create(advance: 1000)));
    }

    [Fact]
    public void AValidPriorityNeedNotHaveAppearedInAnyHeldOrDroppedCargo()
    {
        GameSessionCheckpoint checkpoint = Create(field: true);
        ShipActionIntent.CollectField action = Assert.IsType<ShipActionIntent.CollectField>(Active(checkpoint).Action);
        var replacement = new ShipActionIntent.CollectField(action.OriginCommandSequence, action.CargoInventoryId,
            action.TargetFieldId, action.LastObservedObjectEntityId, action.LastObservedPosition, [NeverHeld.Key]);
        Assert.Null(Validate(ChangeActive(checkpoint, order => order with { Action = replacement })));
        Assert.NotNull(GameSession.ValidateCargoCollectionCheckpoint(
            ChangeActive(checkpoint, order => order with { Action = replacement }), new PhysicalDefinitionCatalog([Item])));
    }

    [Theory]
    [InlineData("missing-attempt-section")]
    [InlineData("missing-target-section")]
    [InlineData("unallocated-command")]
    [InlineData("wrong-cargo-inventory")]
    [InlineData("fabricated-object")]
    [InlineData("fabricated-anchor")]
    [InlineData("attempt-command")]
    [InlineData("attempt-order")]
    [InlineData("attempt-target")]
    [InlineData("attempt-anchor")]
    [InlineData("attempt-without-collecting-reason")]
    [InlineData("collecting-without-attempt")]
    [InlineData("target-needs-selection")]
    [InlineData("target-ship")]
    [InlineData("target-order")]
    [InlineData("target-id")]
    [InlineData("target-anchor")]
    [InlineData("duplicate-target")]
    [InlineData("missing-active-target")]
    [InlineData("external-managed-binding")]
    [InlineData("lost-command-control")]
    public void MalformedSessionLinkRejectsEvenWhenItsScalarComponentStateCanBeRead(string invalid)
    {
        GameSessionCheckpoint checkpoint = Create();
        ShipOrderCheckpoint active = Active(checkpoint);
        ShipActionIntent.CollectObject action = Assert.IsType<ShipActionIntent.CollectObject>(active.Action);
        CargoCollectionAttempt attempt = checkpoint.CargoCollectionAttempts!.Attempts.Single()!;
        CargoCollectionTarget target = checkpoint.CargoCollectionTargets!.Single();
        CargoCollectionAttemptOwnerCheckpoint WithAttempt(CargoCollectionAttempt replacement) =>
            new(checkpoint.CargoCollectionAttempts.Generations, [replacement]);
        GameSessionCheckpoint forged = invalid switch
        {
            "missing-attempt-section" => checkpoint with { CargoCollectionAttempts = null },
            "missing-target-section" => checkpoint with { CargoCollectionTargets = null },
            "unallocated-command" => ChangeActive(checkpoint, order => order with { Action = new ShipActionIntent.CollectObject(
                new(checkpoint.CommandAdmission.Sequences.NextValue!.Value), action.CargoInventoryId, action.TargetEntityId, action.LastObservedPosition) }),
            "wrong-cargo-inventory" => ChangeActive(checkpoint, order => order with { Action = new ShipActionIntent.CollectObject(
                action.OriginCommandSequence, new(999), action.TargetEntityId, action.LastObservedPosition) }),
            "fabricated-object" => ChangeActive(checkpoint, order => order with { Action = new ShipActionIntent.CollectObject(
                action.OriginCommandSequence, action.CargoInventoryId, new(999), action.LastObservedPosition) }),
            "fabricated-anchor" => ChangeActive(checkpoint, order => order with { Action = new ShipActionIntent.CollectObject(
                action.OriginCommandSequence, action.CargoInventoryId, action.TargetEntityId, GameSessionTestFixture.Position(1, 0)),
                Destination = new NavigationDestination.Position(GameSessionTestFixture.Position(1, 0)) }),
            "attempt-command" => checkpoint with { CargoCollectionAttempts = WithAttempt(attempt with { AcceptedCommand = new(999) }) },
            "attempt-order" => checkpoint with { CargoCollectionAttempts = WithAttempt(attempt with { OrderId = new(999) }) },
            "attempt-target" => checkpoint with { CargoCollectionAttempts = WithAttempt(attempt with { TargetId = new(999) }) },
            "attempt-anchor" => checkpoint with { CargoCollectionAttempts = WithAttempt(attempt with { TargetPosition = GameSessionTestFixture.Position(1, 0) }) },
            "attempt-without-collecting-reason" => ChangeActive(checkpoint, order => order with { Reason = ShipOrderReason.AwaitingActionExecution }),
            "collecting-without-attempt" => checkpoint with { CargoCollectionAttempts = new(checkpoint.CargoCollectionAttempts.Generations, []) },
            "target-needs-selection" => checkpoint with { CargoCollectionTargets = [target with { NeedsSelection = true }] },
            "target-ship" => checkpoint with { CargoCollectionTargets = [target with { ShipId = new(1) }] },
            "target-order" => checkpoint with { CargoCollectionTargets = [target with { OrderId = new(999) }] },
            "target-id" => checkpoint with { CargoCollectionTargets = [target with { TargetId = new(999) }] },
            "target-anchor" => checkpoint with { CargoCollectionTargets = [target with { Anchor = GameSessionTestFixture.Position(1, 0) }] },
            "duplicate-target" => checkpoint with { CargoCollectionTargets = [target, target] },
            "missing-active-target" => checkpoint with { CargoCollectionTargets = [] },
            "external-managed-binding" => checkpoint with { SpatialInteractions = checkpoint.SpatialInteractions! with { EvaluationDomains = [ActorOrderRuntimeCoordinator.CargoDomainKind] } },
            "lost-command-control" => checkpoint with { Control = new(checkpoint.Control.Actors.Select(actor => actor!.ShipId == new ShipId(2)
                ? actor with { BaseController = new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("other")) } : actor)) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.NotNull(Validate(forged));
    }

    [Fact]
    public void OriginCommandIdentityCannotBeReusedAcrossRetainedCargoAndSensorActions()
    {
        GameSessionCheckpoint checkpoint = Create();
        ShipOrderCheckpoint active = Active(checkpoint);
        ShipActionIntent.Cargo action = Assert.IsAssignableFrom<ShipActionIntent.Cargo>(active.Action);
        var sensor = new ShipActionIntent.DeploySensor(action.OriginCommandSequence, action.CargoInventoryId,
            Item.Key, new(1), action.LastObservedPosition);
        var queued = active with { Id = new(checkpoint.Orders.OrderIds.NextValue!.Value), Action = sensor, Kind = sensor.Kind,
            Status = ShipOrderStatus.Queued, Reason = ShipOrderReason.QueuedBehindActiveOrder };
        ShipActorOrdersCheckpoint actor = checkpoint.Orders.Actors.Single(row => row!.ShipId == new ShipId(2))!;
        var orders = new ShipOrderCoordinatorCheckpoint(new(queued.Id.Value + 1), checkpoint.Orders.Actors.Select(row => row == actor
            ? actor with { Base = new(active, [queued], actor.Base!.LastTerminal) } : row));
        Assert.NotNull(Validate(checkpoint with { Orders = orders }));
    }

    [Fact]
    public void UnknownFieldPrioritiesAndUnrelatedHistoricalFieldHandlesReject()
    {
        GameSessionCheckpoint checkpoint = Create(field: true);
        ShipActionIntent.CollectField action = Assert.IsType<ShipActionIntent.CollectField>(Active(checkpoint).Action);
        var unknown = new ShipActionIntent.CollectField(action.OriginCommandSequence, action.CargoInventoryId, action.TargetFieldId,
            action.LastObservedObjectEntityId, action.LastObservedPosition, [QualifiedContentKey.Create("test.cargo", "commodity", "unknown")]);
        Assert.NotNull(Validate(ChangeActive(checkpoint, order => order with { Action = unknown })));
        var wrongField = new ShipActionIntent.CollectField(action.OriginCommandSequence, action.CargoInventoryId, new(999),
            action.LastObservedObjectEntityId, action.LastObservedPosition, []);
        Assert.NotNull(Validate(ChangeActive(checkpoint, order => order with { Action = wrongField })));
    }

    [Fact]
    public void FieldReselectionTargetRequiresAPendingWakeForItsExactCurrentIntent()
    {
        GameSessionCheckpoint original = Create(field: true);
        CargoCollectionTarget target = original.CargoCollectionTargets!.Single();
        GameSessionCheckpoint pending = ChangeActive(original, order => order with { Reason = ShipOrderReason.AwaitingActionExecution }) with
        {
            CargoCollectionAttempts = new(original.CargoCollectionAttempts!.Generations, []),
            CargoCollectionTargets = [target with { NeedsSelection = true }],
        };
        Assert.NotNull(Validate(pending));

        ShipActionIntent.Cargo action = Assert.IsAssignableFrom<ShipActionIntent.Cargo>(Active(pending).Action);
        EventAgendaCheckpoint<GameEvent> agenda = pending.Engine.Agenda;
        var wake = new ScheduledEvent<GameEvent>(new(agenda.CurrentTime, EventPhase.StateUpdate, agenda.NextCreationSequence), default,
            new GameEvent.CargoCollectionReevaluation([new(target.ShipId, target.OrderId, action.OriginCommandSequence)]));
        var withWake = pending with { Engine = new(pending.Engine.IsInitialized, pending.Engine.AccruedThrough,
            new(agenda.CurrentTime, agenda.NextCreationSequence + 1, agenda.PendingEvents.Append(wake))) };
        Assert.Null(Validate(withWake));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("generation")]
    public void RestoreRejectsAReevaluationWakeOutsideItsExactImmediateEnvelope(string changed)
    {
        GameSessionCheckpoint original = Create(processAdmission: false);
        CheckpointResult<GameSession> valid = GameSession.RestoreCheckpoint(original, Definitions);
        Assert.True(valid.IsSuccess, valid.Failure?.Message);
        valid.Value!.Dispose();
        EventAgendaCheckpoint<GameEvent> agenda = original.Engine.Agenda;
        ScheduledEvent<GameEvent>[] events = agenda.PendingEvents.Select(entry => entry.Payload is GameEvent.CargoCollectionReevaluation
            ? new ScheduledEvent<GameEvent>(changed == "future" ? entry.Key with { Timestamp = new(9000) } : entry.Key,
                changed == "generation" ? new EventGeneration(1) : entry.Generation, entry.Payload)
            : entry).ToArray();
        GameSessionCheckpoint altered = original with { Engine = new(original.Engine.IsInitialized, original.Engine.AccruedThrough,
            new(agenda.CurrentTime, agenda.NextCreationSequence, events)) };
        Assert.False(GameSession.RestoreCheckpoint(altered, Definitions).IsSuccess);
    }

    [Fact]
    public void CargoDisabledSessionRejectsAnAllocatedButOwnerlessStaleCargoWake()
    {
        using GameSession session = GameSessionTestFixture.Create(navigation:
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        for (int index = 0; index < 2; index++)
            session.SubmitCommand(GameSessionTestFixture.Player,
                new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        GameSessionCheckpoint original = session.CaptureCheckpoint().Value!;
        CheckpointResult<GameSession> valid = GameSession.RestoreCheckpoint(original);
        Assert.True(valid.IsSuccess, valid.Failure?.Message);
        valid.Value!.Dispose();
        EventAgendaCheckpoint<GameEvent> agenda = original.Engine.Agenda;
        var wake = new ScheduledEvent<GameEvent>(new(agenda.CurrentTime, EventPhase.StateUpdate, agenda.NextCreationSequence), default,
            new GameEvent.CargoCollectionReevaluation([new(new(1), new(1), new(1))]));
        GameSessionCheckpoint changed = original with { Engine = new(original.Engine.IsInitialized, original.Engine.AccruedThrough,
            new(agenda.CurrentTime, agenda.NextCreationSequence + 1, agenda.PendingEvents.Append(wake))) };
        Assert.False(GameSession.RestoreCheckpoint(changed).IsSuccess);
    }

    private static CheckpointValidationFailure? Validate(GameSessionCheckpoint checkpoint) =>
        GameSession.ValidateCargoCollectionCheckpoint(checkpoint, Definitions);

    private static ShipOrderCheckpoint Active(GameSessionCheckpoint checkpoint) =>
        checkpoint.Orders.Actors.Single(actor => actor!.ShipId == new ShipId(2))!.Base!.Active!;

    private static GameSessionCheckpoint ChangeActive(GameSessionCheckpoint checkpoint, Func<ShipOrderCheckpoint, ShipOrderCheckpoint> change) =>
        checkpoint with { Orders = new(checkpoint.Orders.OrderIds, checkpoint.Orders.Actors.Select(actor => actor!.ShipId == new ShipId(2)
            ? actor with { Base = new(change(actor.Base!.Active!), actor.Base.Queue, actor.Base.LastTerminal) } : actor)) };

    private static GameSessionCheckpoint Create(bool field = false, ulong advance = 0, bool processAdmission = true)
    {
        ShipDesign Design(ulong id) => new(new(id), "Fixture", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, 10000, 5000);
        int count = field ? 8 : 3;
        var ships = new InitialShipSetup[]
        {
            new(new(1), new(1), new(1), new(1), Design(1), GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, cargo: Enumerable.Repeat(Item, count)),
            new(new(2), new(2), new(2), new(1), Design(2), GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
        };
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("test.cargo", "cargo-policy", "orders"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            4, 0, new(20000), new(1000), new(15000), new(100));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")], ships, new ConnectorTopology([], []), [], null,
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player, cargoPolicy: policy);
        using var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        session.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        var loss = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1),
            GameSessionTestFixture.Position(0, 0), field ? CargoLossCause.StorageModuleLoss : CargoLossCause.FunctionalEjection, policy,
            Enumerable.Range(1, count).Select(index => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Item.Key, new((ulong)index))).ToArray(), []);
        Assert.True(session.CommitCargoDisposition(loss).WasApplied);
        CargoObjectView target = session.CaptureVisibleCargo()[0];
        GameplayCommand command = field ? new CollectCargoFieldCommand(new(2), target.OriginalFieldId!.Value, [], OrderPlacement.ReplaceAll)
            : new CollectCargoObjectCommand(new(2), target.EntityId, OrderPlacement.ReplaceAll);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player, command).Result.Status);
        if (processAdmission) session.AdvanceTo(new(advance));
        return Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);
    }
}
