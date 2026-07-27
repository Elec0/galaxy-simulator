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
    private readonly ActorControlRegistry _control = new();
    private readonly ShipOrderCoordinator _orders = new();
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
            _control.Add(ship.Id, ship.BaseController);
            _orders.Add(ship.Id);
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
                    _control.Capture(ship.ShipId),
                    _orders.CaptureCurrent(ship.ShipId),
                    _orders.CaptureQueue(ship.ShipId),
                    _orders.CaptureSuspended(ship.ShipId)))));
    }

    internal CommandResult Handle(GameplayCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Command switch
        {
            MoveShipCommand move => HandleMove(envelope.Source, move),
            CancelShipOrderCommand cancel => HandleCancel(envelope.Source, cancel),
            BeginScriptedOverrideCommand begin =>
                HandleBeginOverride(envelope.Source, begin),
            EndScriptedOverrideCommand end =>
                HandleEndOverride(envelope.Source, end),
            _ => CommandResult.Rejected(
                CommandRejectionCodes.UnsupportedCommand,
                $"Gameplay command '{envelope.Command.Kind}' is not supported yet."),
        };
    }

    /// <summary>
    /// Internal cleanup boundary for the future actor-destruction command.
    /// TASK-011 remains responsible for deciding when destruction is valid.
    /// </summary>
    internal void RemoveActor(ShipId shipId)
    {
        if (!_orders.Contains(shipId)
            || !_control.Contains(shipId)
            || !_movement.Contains(shipId))
        {
            throw new InvalidOperationException(
                $"Actor {shipId} did not exist in every runtime owner.");
        }

        _movement.CommitRemove(shipId, CurrentTime);
        _orders.Remove(shipId);
        _control.Remove(shipId);
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
            ShipOrder active = _orders.GetActive(spatial.Event.ShipId)
                ?? throw new InvalidOperationException(
                    $"Ship {spatial.Event.ShipId} completed motion without an active order.");
            _orders.CompleteLeg(
                spatial.Event.ShipId,
                active.Id,
                spatial.Event.MotionId);
            StartOrContinueOrders(spatial.Event.ShipId);
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

    private CommandResult HandleMove(
        CommandSource source,
        MoveShipCommand command)
    {
        MoveOrderEvaluation evaluation = EvaluateMove(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return rejection;
        }

        MoveOrderProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted move-order evaluation produced no proposal.");
        ShipOrder order = _orders.Create(proposal.Source, proposal.Destination);
        switch (proposal.Placement)
        {
            case OrderPlacement.ReplaceAll:
                _movement.CommitCancel(proposal.ShipId, CurrentTime);
                _orders.ReplaceAll(proposal.ShipId, order);
                _orders.SetPlan(proposal.ShipId, order.Id, proposal.Plan);
                StartOrContinueOrders(proposal.ShipId);
                break;
            case OrderPlacement.Append:
                bool becameActive = _orders.Append(proposal.ShipId, order);
                if (becameActive)
                {
                    _orders.SetPlan(proposal.ShipId, order.Id, proposal.Plan);
                    StartOrContinueOrders(proposal.ShipId);
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported order placement {proposal.Placement}.");
        }

        return CommandResult.Accepted();
    }

    private CommandResult HandleCancel(
        CommandSource source,
        CancelShipOrderCommand command)
    {
        CancelOrderEvaluation evaluation = EvaluateCancel(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return rejection;
        }

        CancelOrderProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted cancel-order evaluation produced no proposal.");
        if (proposal.WasActive)
        {
            _movement.CommitCancel(proposal.ShipId, CurrentTime);
        }

        CancelOrderDisposition disposition =
            _orders.Cancel(proposal.ShipId, proposal.OrderId);
        if (disposition == CancelOrderDisposition.Missing)
        {
            throw new InvalidOperationException(
                $"Evaluated order {proposal.OrderId} disappeared before commit.");
        }

        if (disposition == CancelOrderDisposition.Active)
        {
            StartOrContinueOrders(proposal.ShipId);
        }

        return CommandResult.Accepted();
    }

    private MoveOrderEvaluation EvaluateMove(
        CommandSource source,
        MoveShipCommand command)
    {
        if (RejectIneligible(command.ShipId, source) is { } rejection)
        {
            return new MoveOrderEvaluation(null, rejection);
        }

        SystemPosition origin = _movement.PositionAt(command.ShipId, CurrentTime)
            ?? throw new InvalidOperationException(
                $"Controlled ship {command.ShipId} has no spatial state.");
        NavigationPlanResult result = Plan(command.ShipId, origin, command.Destination);
        if (result is NavigationPlanResult.Unreachable unreachable)
        {
            return new MoveOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.InvalidState,
                    $"Destination is unreachable: {unreachable.Reason}."));
        }

        TravelPlan plan = ((NavigationPlanResult.Planned)result).Plan;
        ValidateExecutablePlan(origin, command.Destination, plan);
        return new MoveOrderEvaluation(
            new MoveOrderProposal(
                command.ShipId,
                source,
                command.Destination,
                command.Placement,
                plan),
            null);
    }

    private CancelOrderEvaluation EvaluateCancel(
        CommandSource source,
        CancelShipOrderCommand command)
    {
        if (RejectIneligible(command.ShipId, source) is { } rejection)
        {
            return new CancelOrderEvaluation(null, rejection);
        }

        if (!_orders.Contains(command.ShipId, command.OrderId))
        {
            return new CancelOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.OrderNotFound,
                    $"Ship {command.ShipId} has no active or queued order {command.OrderId}."));
        }

        return new CancelOrderEvaluation(
            new CancelOrderProposal(
                command.ShipId,
                command.OrderId,
                _orders.IsActive(command.ShipId, command.OrderId)),
            null);
    }

    private CommandResult HandleBeginOverride(
        CommandSource source,
        BeginScriptedOverrideCommand command)
    {
        ActorOverrideValidation validation = _control.ValidateBeginOverride(
            command.ShipId,
            source,
            command.ExpectedRevision);
        if (RejectInvalidOverride(validation, command.ShipId) is { } rejection)
        {
            return rejection;
        }

        _movement.CommitCancel(command.ShipId, CurrentTime);
        _orders.BeginOverride(command.ShipId);
        _control.BeginOverride(command.ShipId, source, command.Reason);
        return CommandResult.Accepted();
    }

    private CommandResult HandleEndOverride(
        CommandSource source,
        EndScriptedOverrideCommand command)
    {
        ActorOverrideValidation validation = _control.ValidateEndOverride(
            command.ShipId,
            source,
            command.ExpectedRevision);
        if (RejectInvalidOverride(validation, command.ShipId) is { } rejection)
        {
            return rejection;
        }

        _movement.CommitCancel(command.ShipId, CurrentTime);
        _orders.EndOverride(command.ShipId, command.ReleasePolicy);
        _control.EndOverride(command.ShipId);
        StartOrContinueOrders(command.ShipId);
        return CommandResult.Accepted();
    }

    private void StartOrContinueOrders(ShipId shipId)
    {
        while (_orders.GetActive(shipId) is { } active)
        {
            SystemPosition current = _movement.PositionAt(shipId, CurrentTime)
                ?? throw new InvalidOperationException(
                    $"Order actor {shipId} has no spatial state.");
            if (active.Plan is null)
            {
                NavigationPlanResult result = Plan(
                    shipId,
                    current,
                    active.Destination);
                if (result is NavigationPlanResult.Unreachable)
                {
                    _orders.FailActive(shipId, active.Id);
                    continue;
                }

                TravelPlan plan = ((NavigationPlanResult.Planned)result).Plan;
                ValidateExecutablePlan(current, active.Destination, plan);
                _orders.SetPlan(shipId, active.Id, plan);
            }

            TravelLeg? nextLeg = _orders.NextLeg(shipId, active.Id);
            if (nextLeg is null)
            {
                if (!DestinationSatisfied(current, active.Destination))
                {
                    throw new InvalidOperationException(
                        $"Order {active.Id} exhausted its plan before reaching its destination.");
                }

                _orders.CompleteActive(shipId, active.Id);
                continue;
            }

            if (nextLeg is not TravelLeg.Local local)
            {
                throw new InvalidOperationException(
                    $"Unsupported travel leg {nextLeg.GetType().Name}.");
            }

            LocalMotionSegment? motion = _movement.CommitStartOrReplace(
                shipId,
                local,
                CurrentTime,
                _agenda,
                movement => new GameEvent.SpatialMovement(movement));
            if (motion is null)
            {
                _orders.CompleteLeg(shipId, active.Id, null);
                continue;
            }

            _orders.BindMotion(shipId, active.Id, motion.Id);
            return;
        }
    }

    private NavigationPlanResult Plan(
        ShipId shipId,
        SystemPosition origin,
        NavigationDestination destination) =>
        _navigation.Plan(new NavigationRequest(
            shipId,
            origin,
            destination,
            CurrentTime));

    private static void ValidateExecutablePlan(
        SystemPosition origin,
        NavigationDestination destination,
        TravelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Destination != destination)
        {
            throw new InvalidOperationException(
                "Navigation returned a plan for a different destination.");
        }

        SystemPosition expectedOrigin = origin;
        foreach (TravelLeg travelLeg in plan.Legs)
        {
            if (travelLeg is not TravelLeg.Local local)
            {
                throw new InvalidOperationException(
                    $"The current runtime cannot execute {travelLeg.GetType().Name}.");
            }

            if (local.Origin != expectedOrigin)
            {
                throw new InvalidOperationException(
                    $"Travel plan leg begins at {local.Origin}, expected {expectedOrigin}.");
            }

            expectedOrigin = local.Destination;
        }

        if (destination is NavigationDestination.Position position
            && expectedOrigin != position.Value)
        {
            throw new InvalidOperationException(
                $"Travel plan ends at {expectedOrigin}, expected {position.Value}.");
        }
    }

    private static bool DestinationSatisfied(
        SystemPosition current,
        NavigationDestination destination) =>
        destination is NavigationDestination.Position position
        && current == position.Value;

    private CommandResult? RejectIneligible(
        ShipId shipId,
        CommandSource source) =>
        _control.CheckCommand(shipId, source) switch
        {
            ActorCommandEligibility.Eligible => null,
            ActorCommandEligibility.MissingActor => CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {shipId}."),
            ActorCommandEligibility.ActorOverridden => CommandResult.Rejected(
                CommandRejectionCodes.ActorOverridden,
                $"Ship {shipId} is under a temporary scripted override."),
            ActorCommandEligibility.IneligibleSource => CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                $"Command source {source.Kind}:{source.Id} does not control ship {shipId}."),
            _ => throw new InvalidOperationException("Unknown actor command eligibility."),
        };

    private static CommandResult? RejectInvalidOverride(
        ActorOverrideValidation validation,
        ShipId shipId) =>
        validation switch
        {
            ActorOverrideValidation.Valid => null,
            ActorOverrideValidation.MissingActor => CommandResult.Rejected(
                CommandRejectionCodes.InvalidIntent,
                $"Unknown ship {shipId}."),
            ActorOverrideValidation.InvalidSource => CommandResult.Rejected(
                CommandRejectionCodes.InvalidSource,
                $"Command source cannot change the override for ship {shipId}."),
            ActorOverrideValidation.Conflict => CommandResult.Rejected(
                CommandRejectionCodes.Conflict,
                $"Ship {shipId} has conflicting override state."),
            ActorOverrideValidation.StaleRevision => CommandResult.Rejected(
                CommandRejectionCodes.StaleControlRevision,
                $"Ship {shipId} control revision has changed."),
            _ => throw new InvalidOperationException("Unknown actor override validation."),
        };

    private sealed record MoveOrderProposal(
        ShipId ShipId,
        CommandSource Source,
        NavigationDestination Destination,
        OrderPlacement Placement,
        TravelPlan Plan);

    private sealed record MoveOrderEvaluation(
        MoveOrderProposal? Proposal,
        CommandResult? Rejection);

    private readonly record struct CancelOrderProposal(
        ShipId ShipId,
        ShipOrderId OrderId,
        bool WasActive);

    private sealed record CancelOrderEvaluation(
        CancelOrderProposal? Proposal,
        CommandResult? Rejection);
}
