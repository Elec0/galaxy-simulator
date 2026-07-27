namespace GalaxyCommand.Simulation;

public abstract record GameEventKind
{
    private GameEventKind()
    {
    }

    public sealed record SpatialMovement(SpatialMovementEvent Event) : GameEventKind;
}

public sealed record GameEventRecord(
    SimulationTime Timestamp,
    EventPhase Phase,
    ulong CreationSequence,
    EventGeneration Generation,
    ScheduledEventDisposition Disposition,
    GameEventKind Kind);

internal abstract record GameEvent
{
    private GameEvent()
    {
    }

    internal sealed record SpatialMovement(SpatialMovementEvent Event) : GameEvent;
}

/// <summary>
/// Clean persistent runtime for the application-facing game session.
/// </summary>
internal sealed class GameRuntime : ISimulationRuntime<GameEvent>
{
    private readonly EventAgenda<GameEvent> _agenda = new();
    private readonly SimulationEngine<GameEvent> _engine;
    private readonly SortedDictionary<SystemId, StarSystem> _systems =
        new(EntityIdComparer<SystemId>.Instance);
    private readonly SpatialMovement _movement = new();
    private readonly ShipOrderBook _orders = new();
    private readonly ISpatialNavigationPlanner _navigation;
    private readonly List<GameEventRecord> _eventRecords = [];

    internal GameRuntime(
        GameSessionSetup setup,
        ISpatialNavigationPlanner navigation)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;

        foreach (StarSystem system in setup.Systems)
        {
            _systems.Add(system.Id, system);
        }

        foreach (InitialShipSetup ship in setup.Ships)
        {
            _movement.Add(ship.Id, ship.Position);
        }

        _engine = new SimulationEngine<GameEvent>(this, _agenda);
    }

    internal SimulationTime CurrentTime => _engine.CurrentTime;

    internal IReadOnlyList<GameEventRecord> EventRecords => _eventRecords.AsReadOnly();

    public bool ShouldStop => false;

    internal RunReport AdvanceTo(SimulationTime target) =>
        _engine.RunUntil(target);

    internal GameSnapshot CaptureSnapshot()
    {
        IReadOnlyList<ShipSpatialSnapshot> spatial =
            _movement.CaptureSnapshot(CurrentTime);
        return new GameSnapshot(
            CurrentTime,
            GameSnapshotCollection.Copy(_systems.Values.Select(system =>
                new GameSystemSnapshot(system.Id, system.Name))),
            GameSnapshotCollection.Copy(spatial.Select(ship =>
                new GameShipSnapshot(
                    ship.ShipId,
                    ship.Position,
                    ship.Motion,
                    _orders.Capture(ship.ShipId)))));
    }

    internal CommandResult Handle(GameplayCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Source.Kind != CommandSourceKind.Player)
        {
            return CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                "The first interactive order slice accepts player commands only.");
        }

        return envelope.Command switch
        {
            MoveShipCommand move => HandleMove(move),
            CancelShipOrderCommand cancel => HandleCancel(cancel),
            _ => CommandResult.Rejected(
                CommandRejectionCodes.UnsupportedCommand,
                $"Gameplay command '{envelope.Command.Kind}' is not supported yet."),
        };
    }

    public void Reconcile(SimulationTime now, EventAgenda<GameEvent> agenda)
    {
    }

    public void AccrueTo(SimulationTime now)
    {
    }

    public ScheduledEventDisposition HandleEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        SimulationTime now,
        EventAgenda<GameEvent> agenda)
    {
        if (simulationEvent.Payload is not GameEvent.SpatialMovement spatial)
        {
            throw new InvalidOperationException(
                $"Unsupported game event {simulationEvent.Payload.GetType().Name}.");
        }

        ScheduledEventDisposition disposition = _movement.HandleEvent(
            spatial.Event,
            simulationEvent.Generation,
            now);
        if (disposition == ScheduledEventDisposition.Applied)
        {
            _orders.CompleteMovement(
                spatial.Event.ShipId,
                spatial.Event.MotionId);
        }

        return disposition;
    }

    public void RecordEvent(
        ScheduledEvent<GameEvent> simulationEvent,
        ScheduledEventDisposition disposition)
    {
        GameEventKind kind = simulationEvent.Payload switch
        {
            GameEvent.SpatialMovement spatial =>
                new GameEventKind.SpatialMovement(spatial.Event),
            _ => throw new InvalidOperationException(
                $"Unsupported game event {simulationEvent.Payload.GetType().Name}."),
        };
        _eventRecords.Add(new GameEventRecord(
            simulationEvent.Key.Timestamp,
            simulationEvent.Key.Phase,
            simulationEvent.Key.CreationSequence,
            simulationEvent.Generation,
            disposition,
            kind));
    }

    private CommandResult HandleMove(MoveShipCommand command)
    {
        SystemPosition? origin = _movement.PositionAt(command.ShipId, CurrentTime);
        if (origin is null)
        {
            return CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {command.ShipId}.");
        }

        NavigationPlanResult result = _navigation.Plan(new NavigationRequest(
            command.ShipId,
            origin.Value,
            command.Destination,
            CurrentTime));
        if (result is NavigationPlanResult.Unreachable unreachable)
        {
            return CommandResult.Rejected(
                CommandRejectionCodes.InvalidState,
                $"Destination is unreachable: {unreachable.Reason}.");
        }

        var planned = (NavigationPlanResult.Planned)result;
        if (planned.Plan.Destination != command.Destination
            || planned.Plan.Legs.Count != 1
            || planned.Plan.Legs[0] is not TravelLeg.Local leg)
        {
            return CommandResult.Rejected(
                CommandRejectionCodes.InvalidState,
                "The first interactive order requires a matching destination and exactly one local travel leg.");
        }

        ShipOrderId orderId = _orders.AllocateId();
        LocalMotionSegment? motion = _movement.CommitStartOrReplace(
            command.ShipId,
            leg,
            CurrentTime,
            _agenda,
            movement => new GameEvent.SpatialMovement(movement));
        if (motion is null)
        {
            _orders.CompleteImmediately(
                command.ShipId,
                orderId,
                command.Destination);
        }
        else
        {
            _orders.Start(
                command.ShipId,
                orderId,
                command.Destination,
                motion.Id);
        }

        return CommandResult.Accepted();
    }

    private CommandResult HandleCancel(CancelShipOrderCommand command)
    {
        if (_movement.GetState(command.ShipId) is null)
        {
            return CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {command.ShipId}.");
        }

        if (!_orders.Cancel(command.ShipId))
        {
            return CommandResult.Rejected(
                CommandRejectionCodes.InvalidState,
                $"Ship {command.ShipId} has no active order to cancel.");
        }

        if (!_movement.CommitCancel(command.ShipId, CurrentTime))
        {
            throw new InvalidOperationException(
                $"Ship {command.ShipId} had an active order without active movement.");
        }

        return CommandResult.Accepted();
    }
}
