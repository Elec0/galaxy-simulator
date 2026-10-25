namespace GalaxyCommand.Simulation;

internal sealed record SensorActionWake(ShipId Ship, ShipOrderId Order, CommandSequence Command);
internal sealed record SensorActionDue(SensorActionWake Work, EventKey Trigger);
internal sealed record SensorActionProposal(SensorActionDue Due, ShipActionIntent Intent);

internal sealed partial class ActorOrderRuntimeCoordinator
{
    internal const string SensorDomainKind = "galaxy-command.sensor-deployables";
    private InteractionInterestOwner? _sensorInterests;
    private readonly Dictionary<ShipId, (ShipOrderId Order, InteractionInterestId Interest)> _sensorBindings = [];
    private readonly HashSet<ShipId> _sensorDirtyShips = [];

    internal bool IsManagedSpatialDomain(string domainKind) => _sensorInterests is not null && domainKind == SensorDomainKind;

    /// <summary>Preflights affected controlled owners before removing a sensor and invalidating every inbound order.</summary>
    private EntityRemovalResult RemoveSensorEntity(EntityRemovalRequest request)
    {
        if (_lifecycle.GetDeployedSensor(request.EntityId) is null) return _lifecycle.RemoveSensor(request, CurrentTime);
        IReadOnlyList<TargetedShipOrder> inbound = _orders.PrepareTargetRemoval(request.EntityId, default);
        if (inbound.Any(targeted => _movement.GetState(targeted.ShipId) is ShipSpatialState.InteractionControlled))
            return new EntityRemovalResult.Rejected(request, EntityRemovalRejectionReason.OwnerConflict);
        try
        {
            EntityRemovalResult result = _lifecycle.RemoveSensor(request, CurrentTime);
            if (result is not EntityRemovalResult.SensorRemoved removed) return result;
            var transitions = new List<ShipOrderTransition>();
            var facts = new List<GameFactProposal>();
            InvalidateSensorTargetOrders(inbound, transitions, facts);
            AddOrderTransitionProposals(transitions, facts);
            facts.Add(new GameFactProposal(new GameFactProposalKey(GameFactCommitCategory.EntityLifecycle,
                request.EntityId.Value, 0, 0), new SensorRemovedFact(removed.Sensor, request.Reason)));
            _facts.Commit(CurrentTime, new EntityRemovalFactCause(request), facts);
            FlushSensorReevaluations();
            return result;
        }
        catch
        {
            // Lifecycle or inbound orders may already have committed. The session
            // cannot publish a partial removal or resume after a broken invariant.
            _isPoisoned = true;
            throw;
        }
    }

    /// <summary>Fails queued/suspended references before progressing current actors, so no removed target can be rebound.</summary>
    private void InvalidateSensorTargetOrders(IEnumerable<TargetedShipOrder> inbound,
        ICollection<ShipOrderTransition> transitions, List<GameFactProposal> facts, ShipId? progressingShip = null)
    {
        TargetedShipOrder[] targets = inbound.OrderBy(targeted => targeted.WasCurrentActive)
            .ThenBy(targeted => targeted.ShipId.Value).ThenBy(targeted => targeted.OrderId.Value).ToArray();
        foreach (TargetedShipOrder targeted in targets)
        {
            if (targeted.WasCurrentActive) EndActiveLocalMotion(targeted.ShipId, LocalMotionEndReason.TargetRemoved, facts);
            _orders.ApplyTargetRemoval(targeted, transitions);
        }
        HashSet<ShipId> interrupted = targets.Where(targeted => targeted.WasCurrentActive).Select(targeted => targeted.ShipId).ToHashSet();
        foreach (ShipId ship in targets.Select(targeted => targeted.ShipId).Distinct().OrderBy(ship => ship.Value))
        {
            SynchronizeSensorInterest(ship);
            // A queued or suspended failure does not interrupt the current actor's
            // unrelated movement, so it must not restart that already-running plan.
            // The successful pickup commit progresses its own actor after this
            // cleanup. Other interrupted actors progress here exactly once.
            if (!interrupted.Contains(ship) || ship == progressingShip) continue;
            StartOrContinueOrders(ship, transitions, facts);
            if (!_orders.HasActive(ship)) StartPassiveDrag(ship);
        }
    }

    /// <summary>Captures only active action eligibility before a resource owner commits its batch.</summary>
    private Dictionary<ShipId, SensorActionFailure?> CaptureSensorEligibility() => _sensorBindings.Keys
        .Where(ship => _orders.Contains(ship) && _orders.GetActive(ship) is { Status: ShipOrderStatus.Active, Action: not null })
        .ToDictionary(ship => ship, ship =>
        {
            ShipOrder active = _orders.GetActive(ship)!;
            return SensorEligibility(ship, active.Source, active.Action!);
        });

    /// <summary>Schedules only changed eligibility, so repeated reconciliation cannot become polling or depend on advance chunk size.</summary>
    private void InvalidateSensorEligibility(IReadOnlyDictionary<ShipId, SensorActionFailure?> before)
    {
        foreach (var prior in before)
            if (_orders.Contains(prior.Key) && _orders.GetActive(prior.Key) is { Status: ShipOrderStatus.Active, Action: not null } active
                && SensorEligibility(prior.Key, active.Source, active.Action) != prior.Value)
                _sensorDirtyShips.Add(prior.Key);
        FlushSensorReevaluations();
    }

    /// <summary>Rebinds derived action correlations without scheduling extra work during restore.</summary>
    private void AttachSensorActions(SpatialInteractionRegistration registration)
    {
        if (_sensorDeployableDefinitions.Count == 0) return;
        _sensorInterests = registration.Get(SensorDomainKind) ?? registration.Register(SensorDomainKind);
        foreach (SpatialInteractionInterest saved in _sensorInterests.CaptureAllSnapshot())
        {
            if (saved is not StationaryInteractionInterest interest || !_orders.Contains(interest.Source)
                || _orders.GetActive(interest.Source) is not { Status: ShipOrderStatus.Active, Action: not null } active
                || _control.CheckCommand(interest.Source, active.Source) != ActorCommandEligibility.Eligible
                || interest.PurposeId != active.Id.ToString()
                || !TrySensorTarget(interest.Source, active.Action, out SystemPosition target, out ulong range)
                || interest.Position != target || interest.RangeUnits != range
                || interest.EntityId != (active.Action as ShipActionIntent.PickupSensor)?.TargetEntityId
                || !_sensorBindings.TryAdd(interest.Source, (active.Id, interest.Id)))
                throw new InvalidOperationException("Restored sensor interest disagrees with its active action order.");
        }
        foreach (GameSessionShip ship in _lifecycle.Ships)
            if (_orders.GetActive(ship.Id) is { Status: ShipOrderStatus.Active, Action: not null }
                && !_sensorBindings.ContainsKey(ship.Id))
                throw new InvalidOperationException("An active sensor action has no restored stationary interest.");
    }

    /// <summary>Admits exact cargo intent through the existing move-order placement and planning boundary.</summary>
    private GameplayCommandHandlingResult HandleDeploySensor(GameplayCommandEnvelope envelope, DeploySensorCommand command)
    {
        if (RejectIneligible(command.ShipId, envelope.Source) is { } rejected) return new(rejected);
        GameSessionShip actor = _lifecycle.GetRequiredShip(command.ShipId);
        Inventory cargo = _lifecycle.GetRequiredCargo(command.ShipId);
        if (cargo.GetDiscrete(command.ItemId) is not { } item)
            return SensorCommandRejected(SensorActionFailure.ItemUnavailable);
        var action = new ShipActionIntent.DeploySensor(envelope.Sequence, actor.CargoInventoryId,
            item.DefinitionKey, command.ItemId, command.Position);
        return AdmitSensorAction(envelope.Source, command.ShipId, action, command.Placement);
    }

    /// <summary>Checks current pickup capacity at admission without reserving it for the accepted order.</summary>
    private GameplayCommandHandlingResult HandlePickupSensor(GameplayCommandEnvelope envelope, PickupSensorCommand command)
    {
        if (RejectIneligible(command.ShipId, envelope.Source) is { } rejected) return new(rejected);
        if (_lifecycle.GetDeployedSensor(command.TargetEntityId) is not { } sensor)
            return SensorCommandRejected(SensorActionFailure.TargetRemoved);
        GameSessionShip actor = _lifecycle.GetRequiredShip(command.ShipId);
        var action = new ShipActionIntent.PickupSensor(envelope.Sequence, actor.CargoInventoryId,
            sensor.DefinitionKey, sensor.EntityId);
        return AdmitSensorAction(envelope.Source, command.ShipId, action, command.Placement);
    }

    /// <summary>Shares replacement, append, control and planning semantics without executing during admission.</summary>
    private GameplayCommandHandlingResult AdmitSensorAction(CommandSource source, ShipId ship,
        ShipActionIntent action, OrderPlacement placement)
    {
        if (SensorEligibility(ship, source, action) is { } failure) return SensorCommandRejected(failure);
        NavigationDestination destination = action switch
        {
            ShipActionIntent.DeploySensor deploy => new NavigationDestination.Position(deploy.Position),
            ShipActionIntent.PickupSensor pickup => new NavigationDestination.Entity(pickup.TargetEntityId),
            _ => throw new InvalidOperationException("Unknown sensor action."),
        };
        MoveOrderEvaluation evaluated = EvaluateMove(source, new MoveShipCommand(ship, destination, placement));
        if (evaluated.Rejection is { } rejection) return new(rejection);
        var transitions = new List<ShipOrderTransition>();
        var facts = new List<GameFactProposal>();
        CommitMove(evaluated.Proposal! with { Action = action }, transitions, facts);
        AddOrderTransitionProposals(transitions, facts);
        return new GameplayCommandHandlingResult(CommandResult.Accepted(), facts);
    }

    private static GameplayCommandHandlingResult SensorCommandRejected(SensorActionFailure reason) =>
        new(CommandResult.Rejected(CommandRejectionCodes.InvalidIntent, $"Sensor action is unavailable: {reason}."));

    /// <summary>Rechecks all non-range eligibility from current authority; outside range remains ordinary progress.</summary>
    private SensorActionFailure? SensorEligibility(ShipId ship, CommandSource source, ShipActionIntent action)
    {
        if (!_orders.Contains(ship)) return SensorActionFailure.MissingActor;
        if (_control.CheckCommand(ship, source) != ActorCommandEligibility.Eligible
            || _movement.GetState(ship) is ShipSpatialState.InteractionControlled) return SensorActionFailure.Unauthorized;
        GameSessionShip actor = _lifecycle.GetRequiredShip(ship);
        Inventory cargo = _lifecycle.GetRequiredCargo(ship);
        if (actor.CargoInventoryId != action.CargoInventoryId || cargo.Custody?.ControllingPrincipalId != actor.PrincipalId)
            return SensorActionFailure.Unauthorized;
        SensorDeployableDefinition? definition = _lifecycle.GetSensorDefinition(action.DefinitionKey);
        if (definition is null) return SensorActionFailure.DefinitionUnavailable;
        if (action is ShipActionIntent.DeploySensor deployment)
        {
            if (cargo.GetDiscrete(deployment.ItemId)?.DefinitionKey != action.DefinitionKey) return SensorActionFailure.ItemUnavailable;
            if (cargo.IsDiscreteReserved(deployment.ItemId)) return SensorActionFailure.ItemReserved;
        }
        else if (action is ShipActionIntent.PickupSensor pickup)
        {
            DeployedSensorSnapshot? sensor = _lifecycle.GetDeployedSensor(pickup.TargetEntityId);
            if (sensor is null) return SensorActionFailure.TargetRemoved;
            if (sensor.PrincipalId != actor.PrincipalId || sensor.DefinitionKey != action.DefinitionKey) return SensorActionFailure.Unauthorized;
            if (definition.Physical.CapacityCost > cargo.RemainingCapacity) return SensorActionFailure.InsufficientCapacity;
        }
        if (!TrySensorTarget(ship, action, out SystemPosition target, out _)
            || _movement.PositionAt(ship, CurrentTime) is not { } position || position.SystemId != target.SystemId)
            return SensorActionFailure.InvalidSpatialState;
        return null;
    }

    /// <summary>Resolves only typed fixed geometry and authoritative definition ranges, without evaluating resources.</summary>
    private bool TrySensorTarget(ShipId ship, ShipActionIntent action, out SystemPosition position, out ulong range)
    {
        position = default;
        range = 0;
        if (action is ShipActionIntent.DeploySensor deploy)
        {
            SensorDeployableDefinition? definition = _lifecycle.GetSensorDefinition(action.DefinitionKey);
            if (definition is null) return false;
            position = deploy.Position;
            range = definition.DeploymentRangeUnits;
            return true;
        }
        if (action is ShipActionIntent.PickupSensor pickup && _lifecycle.GetDeployedSensor(pickup.TargetEntityId) is { } sensor
            && _lifecycle.Entities.GetEntityId(ship) is not null)
        {
            position = sensor.Position;
            range = _lifecycle.GetOperationalShipDefinition(_lifecycle.GetRequiredShip(ship).DesignId)!.PickupRangeUnits;
            return true;
        }
        return false;
    }

    /// <summary>Keeps one current interest per active action, withdrawing suspended, replaced and canceled work.</summary>
    private void SynchronizeSensorInterest(ShipId ship)
    {
        if (_sensorInterests is null) return;
        ShipOrder? active = _orders.Contains(ship) ? _orders.GetActive(ship) : null;
        bool valid = active is { Status: ShipOrderStatus.Active, Action: not null };
        if (_sensorBindings.TryGetValue(ship, out var binding))
        {
            if (valid && active!.Id == binding.Order && TrySensorTarget(ship, active.Action!, out _, out _)) return;
            _sensorInterests.Cancel(binding.Interest);
            _sensorBindings.Remove(ship);
        }
        if (!valid) return;
        _sensorDirtyShips.Add(ship);
        if (TrySensorTarget(ship, active!.Action!, out SystemPosition target, out ulong range))
        {
            StationaryInteractionInterest interest = _sensorInterests.RegisterStationary(active.Id.ToString(), ship, target, range,
                (active.Action as ShipActionIntent.PickupSensor)?.TargetEntityId);
            _sensorBindings.Add(ship, (active.Id, interest.Id));
        }
    }

    /// <summary>Defers in-range and invalid action execution to the shared post-completion event boundary.</summary>
    private bool PrepareSensorApproach(ShipId ship, ShipOrder active, ICollection<ShipOrderTransition> transitions)
    {
        if (active.Action is not { } action) return false;
        SynchronizeSensorInterest(ship);
        bool stop = SensorEligibility(ship, active.Source, action) is not null
            || TrySensorTarget(ship, action, out SystemPosition target, out ulong radius) && SensorInRange(ship, target, radius);
        if (stop && active.MotionId is null && active.TransitId is null)
            _orders.AwaitAction(ship, active.Id, transitions);
        return stop;
    }

    /// <summary>Uses exact analytic geometry; rounded map positions cannot authorize an action.</summary>
    private bool SensorInRange(ShipId ship, SystemPosition target, ulong radius)
    {
        PreparedInteractionPoint? point = InteractionProximityQuery.Prepare(_movement.CaptureInteractionReadView(CurrentTime, [ship])).Get(ship);
        return point is not null && point.InRange(PreparedInteractionPoint.Capture(
            InteractionPhaseCurve.Fixed(target, CurrentTime, CurrentTime), CurrentTime), radius);
    }

    /// <summary>
    /// Flushes one coarse logical invalidation batch onto the existing agenda. Decision-phase
    /// changes stay in that phase; earlier owners use StateUpdate after physical completions.
    /// Every completed owner operation flushes, so checkpoint capture need not mutate scheduling.
    /// </summary>
    private void FlushSensorReevaluations()
    {
        if (_sensorDirtyShips.Count == 0) return;
        SensorActionWake[] requests = _sensorDirtyShips.OrderBy(ship => ship.Value)
            .Where(ship => _orders.Contains(ship)).Select(ship => (Ship: ship, Active: _orders.GetActive(ship)))
            .Where(value => value.Active is { Status: ShipOrderStatus.Active, Action: not null })
            .Select(value => new SensorActionWake(value.Ship, value.Active!.Id, value.Active.Action!.OriginCommandSequence)).ToArray();
        _sensorDirtyShips.Clear();
        if (requests.Length == 0) return;
        EventPhase phase = _agenda.CurrentPhase == EventPhase.Decision ? EventPhase.Decision : EventPhase.StateUpdate;
        _ = AgendaCommitOwner.Commit(_agenda, [new AgendaEventProposal<GameEvent>(
            new AgendaProposalOrder(RuntimeEvaluationWave.ActorOrders, 0, requests[0].Command.Value, 0, 0),
            CurrentTime, phase, default, new GameEvent.SensorActionReevaluation(Array.AsReadOnly(requests)))]);
    }

    /// <summary>
    /// Reads one stable scoped geometry view and buffers eligible or terminal-failure proposals.
    /// This serial reference loop allocates no identities and mutates no owner; its coarse
    /// input/output boundary permits batch evaluation without changing contention order.
    /// </summary>
    private List<SensorActionProposal> EvaluateSensorActions(IEnumerable<SensorActionDue> due)
    {
        var handled = new HashSet<ShipId>();
        var current = new List<(SensorActionDue Due, ShipOrder Active)>();
        foreach (SensorActionDue entry in due.OrderBy(value => value.Work.Command.Value).ThenBy(value => value.Trigger.CreationSequence))
        {
            SensorActionWake work = entry.Work;
            if (!_orders.Contains(work.Ship) || _orders.GetActive(work.Ship) is not { Status: ShipOrderStatus.Active, Action: not null } active
                || active.Id != work.Order || active.Action.OriginCommandSequence != work.Command || !handled.Add(work.Ship)) continue;
            current.Add((entry, active));
        }
        if (current.Count == 0) return [];
        PreparedInteractionProximityView geometry = InteractionProximityQuery.Prepare(
            _movement.CaptureInteractionReadView(CurrentTime, current.Select(value => value.Due.Work.Ship)));
        var proposals = new List<SensorActionProposal>();
        foreach (var candidate in current)
        {
            ShipId ship = candidate.Due.Work.Ship;
            ShipOrder active = candidate.Active;
            if (SensorEligibility(ship, active.Source, active.Action!) is not null
                || TrySensorTarget(ship, active.Action!, out SystemPosition target, out ulong radius)
                && geometry.Get(ship) is { } point && point.InRange(PreparedInteractionPoint.Capture(
                    InteractionPhaseCurve.Fixed(target, CurrentTime, CurrentTime), CurrentTime), radius))
                proposals.Add(new SensorActionProposal(candidate.Due, active.Action!));
        }
        return proposals;
    }

    /// <summary>Revalidates buffered proposals and commits in original command order, retaining each actual event cause.</summary>
    private void CommitSensorActions(IEnumerable<SensorActionDue> due)
    {
        foreach (SensorActionProposal proposal in EvaluateSensorActions(due))
        {
            SensorActionDue entry = proposal.Due;
            SensorActionWake work = entry.Work;
            if (!_orders.Contains(work.Ship) || _orders.GetActive(work.Ship) is not { Status: ShipOrderStatus.Active, Action: not null } active
                || active.Id != work.Order || active.Action != proposal.Intent) continue;
            SensorActionFailure? failure = SensorEligibility(work.Ship, active.Source, active.Action);
            if (failure is null && TrySensorTarget(work.Ship, active.Action, out SystemPosition target, out ulong range)
                && !SensorInRange(work.Ship, target, range)) continue;

            SensorActionResult? result = null;
            if (failure is null)
            {
                result = active.Action switch
                {
                    ShipActionIntent.DeploySensor deploy => _lifecycle.DeploySensor(work.Command, work.Ship, deploy.ItemId,
                        deploy.Position, _inventoryCommit, CurrentTime),
                    ShipActionIntent.PickupSensor pickup => _lifecycle.PickupSensor(work.Command, work.Ship,
                        pickup.TargetEntityId, _inventoryCommit, CurrentTime),
                    _ => throw new InvalidOperationException("Unknown sensor action."),
                };
                failure = result.Failure;
            }
            if (failure == SensorActionFailure.OutOfRange) continue;
            var facts = new List<GameFactProposal>();
            var transitions = new List<ShipOrderTransition>();
            EndActiveLocalMotion(work.Ship, failure is null ? LocalMotionEndReason.ActionCompleted : LocalMotionEndReason.ActionFailed, facts);
            if (failure is null)
            {
                if (result!.WasApplied)
                {
                    GameFact fact = active.Action is ShipActionIntent.DeploySensor
                        ? new SensorDeployedFact(result.Deployed!, work.Ship, work.Command)
                        : new SensorPickedUpFact(result.Deployed!, work.Ship, work.Command);
                    facts.Add(new GameFactProposal(new GameFactProposalKey(GameFactCommitCategory.EntityLifecycle,
                        result.Deployed!.EntityId.Value, work.Command.Value, 0), fact));
                }
                _orders.CompleteActive(work.Ship, active.Id, transitions, ShipOrderReason.ActionCompleted);
                if (active.Action is ShipActionIntent.PickupSensor pickup)
                    InvalidateSensorTargetOrders(_orders.PrepareTargetRemoval(pickup.TargetEntityId, default), transitions, facts, work.Ship);
            }
            else _orders.FailActive(work.Ship, active.Id, transitions, SensorFailureReason(failure.Value));
            SynchronizeSensorInterest(work.Ship);
            StartOrContinueOrders(work.Ship, transitions, facts);
            if (!_orders.HasActive(work.Ship)) StartPassiveDrag(work.Ship);
            AddOrderTransitionProposals(transitions, facts);
            _facts.Commit(CurrentTime, new ScheduledEventFactCause(entry.Trigger), facts);
        }
    }

    private static ShipOrderReason SensorFailureReason(SensorActionFailure failure) => failure switch
    {
        SensorActionFailure.ItemUnavailable => ShipOrderReason.ActionItemUnavailable,
        SensorActionFailure.ItemReserved => ShipOrderReason.ActionItemReserved,
        SensorActionFailure.TargetRemoved => ShipOrderReason.TargetRemoved,
        SensorActionFailure.InsufficientCapacity => ShipOrderReason.ActionCapacityUnavailable,
        SensorActionFailure.Unauthorized => ShipOrderReason.ActionUnauthorized,
        SensorActionFailure.InvalidSpatialState => ShipOrderReason.ActionInvalidSpatialState,
        SensorActionFailure.DefinitionUnavailable => ShipOrderReason.ActionDefinitionUnavailable,
        SensorActionFailure.IdentifierExhausted => ShipOrderReason.ActionIdentifierExhausted,
        _ => throw new InvalidOperationException("Unexpected sensor action failure."),
    };
}
