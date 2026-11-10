namespace GalaxyCommand.Simulation;

internal sealed record CargoCollectionTarget(ShipId ShipId, ShipOrderId OrderId, EntityId TargetId, SystemPosition Anchor, bool NeedsSelection = false);
internal sealed record CargoCollectionWake(ShipId ShipId, ShipOrderId OrderId, CommandSequence AcceptedCommand);
internal sealed record CargoCollectionDue(CargoCollectionWake Work, EventKey Trigger);

internal sealed partial class ActorOrderRuntimeCoordinator
{
    internal const string CargoDomainKind = "galaxy-command.cargo-collection";
    private CargoCollectionAttemptOwner? _cargoAttempts;
    private InteractionInterestOwner? _cargoInterests;
    private readonly Dictionary<ShipId, (ShipOrderId Order, InteractionInterestId Interest, EntityId Target, SystemPosition Anchor)> _cargoBindings = [];
    private readonly Dictionary<ShipOrderId, CargoCollectionTarget> _cargoTargets = [];
    private readonly HashSet<ShipId> _cargoDirtyShips = [];
    private readonly HashSet<ShipId> _cargoContinueShips = [];
    private IReadOnlySet<EventKey> _cargoDueKeys = new HashSet<EventKey>();

    /// <summary>Admits only a currently covered object while retaining an independently captured last observed anchor.</summary>
    private GameplayCommandHandlingResult HandleCollectCargoObject(GameplayCommandEnvelope envelope, CollectCargoObjectCommand command)
    {
        if (RejectIneligible(command.ShipId, envelope.Source) is { } rejected) return new(rejected);
        if (_cargo is null) return CargoCommandRejected("Cargo collection is unavailable.");
        GameSessionShip ship = _lifecycle.GetRequiredShip(command.ShipId);
        CargoObjectView? target = CaptureCoveredCargo(ship.PrincipalId).FirstOrDefault(view => view.EntityId == command.TargetEntityId);
        return target is null ? CargoCommandRejected("Cargo target is unavailable.")
            : AdmitCargoAction(envelope.Source, command.ShipId,
                new ShipActionIntent.CollectObject(envelope.Sequence, ship.CargoInventoryId, target.EntityId, target.Position), command.Placement);
    }

    /// <summary>Snapshots priorities and selects from covered field members without exposing canonical aliases or hidden membership.</summary>
    private GameplayCommandHandlingResult HandleCollectCargoField(GameplayCommandEnvelope envelope, CollectCargoFieldCommand command)
    {
        if (RejectIneligible(command.ShipId, envelope.Source) is { } rejected) return new(rejected);
        if (_cargo is null || command.Priorities.Any(key => _cargoDefinitions.Get(key) is null))
            return CargoCommandRejected("Cargo priorities or field are unavailable.");
        GameSessionShip ship = _lifecycle.GetRequiredShip(command.ShipId);
        IReadOnlyList<CargoObjectView> visible = CaptureCoveredCargo(ship.PrincipalId);
        // New commands require an original handle currently offered by covered
        // cargo. Existing accepted orders retain their separate alias continuity.
        if (!visible.Any(view => view.OriginalFieldId == command.TargetFieldId))
            return CargoCommandRejected("Cargo field is unavailable.");
        HashSet<EntityId> members = _cargo.Fields.Members(command.TargetFieldId).ToHashSet();
        CargoObjectView[] covered = visible.Where(view => members.Contains(view.EntityId)).ToArray();
        if (covered.Length == 0 || _movement.PositionAt(command.ShipId, CurrentTime) is not { } position)
            return CargoCommandRejected("Cargo field is unavailable.");
        CargoObjectView target = CargoFieldCandidateSelector.Select(covered, position,
            _lifecycle.GetRequiredCargo(command.ShipId).RemainingCapacity, command.Priorities) ?? covered[0];
        return AdmitCargoAction(envelope.Source, command.ShipId, new ShipActionIntent.CollectField(envelope.Sequence,
            ship.CargoInventoryId, command.TargetFieldId, target.EntityId, target.Position, command.Priorities), command.Placement);
    }

    /// <summary>Uses existing deterministic replacement/append admission; no object contents or incoming capacity are reserved here.</summary>
    private GameplayCommandHandlingResult AdmitCargoAction(CommandSource source, ShipId ship, ShipActionIntent.Cargo action, OrderPlacement placement)
    {
        MoveOrderEvaluation evaluation = EvaluateMove(source, new MoveShipCommand(ship, new NavigationDestination.Position(action.LastObservedPosition), placement));
        if (evaluation.Rejection is { } rejected) return new(rejected);
        var transitions = new List<ShipOrderTransition>();
        var facts = new List<GameFactProposal>();
        CommitMove(evaluation.Proposal! with { Action = action }, transitions, facts);
        AddOrderTransitionProposals(transitions, facts);
        return new(CommandResult.Accepted(), facts);
    }

    private static GameplayCommandHandlingResult CargoCommandRejected(string message) => new(CommandResult.Rejected(CommandRejectionCodes.InvalidIntent, message));

    /// <summary>Rebinds persisted managed interests without scheduling work or replacing hidden target anchors during restore.</summary>
    private void AttachCargoActions(SpatialInteractionRegistration registration)
    {
        if (_cargo is null) return;
        _cargoInterests = registration.Get(CargoDomainKind) ?? registration.Register(CargoDomainKind);
        foreach (SpatialInteractionInterest saved in _cargoInterests.CaptureAllSnapshot())
        {
            if (saved is not StationaryInteractionInterest interest || !_orders.Contains(interest.Source)
                || _orders.GetActive(interest.Source) is not { Action: ShipActionIntent.Cargo } order
                || !_cargoTargets.TryGetValue(order.Id, out CargoCollectionTarget? target) || target.NeedsSelection
                || interest.PurposeId != order.Id.ToString() || interest.Position != target.Anchor || interest.EntityId != target.TargetId
                || interest.RangeUnits != CargoPickupRange(interest.Source)
                || !_cargoBindings.TryAdd(interest.Source, (order.Id, interest.Id, target.TargetId, target.Anchor)))
                throw new InvalidOperationException("Restored cargo interaction disagrees with its retained order target.");
        }
        foreach (GameSessionShip ship in _lifecycle.Ships)
            if (_orders.GetActive(ship.Id) is { Action: ShipActionIntent.Cargo } order
                && (!_cargoTargets.TryGetValue(order.Id, out CargoCollectionTarget? target)
                    || !target.NeedsSelection && !_cargoBindings.ContainsKey(ship.Id)))
                throw new InvalidOperationException("An active cargo order has no restored target or stationary interest.");
    }

    /// <summary>Retains original approach motion while collecting and delays execution to the shared cargo event boundary.</summary>
    private bool PrepareCargoApproach(ShipId ship, ShipOrder active, ICollection<ShipOrderTransition> transitions)
    {
        if (active.Action is not ShipActionIntent.Cargo action) return false;
        SynchronizeCargoInterest(ship);
        if (_cargoAttempts?.Get(ship) is { OrderId: var order } && order == active.Id)
        {
            if (active.MotionId is null && active.TransitId is null && active.Plan is not null)
                _orders.AwaitAction(ship, active.Id, transitions);
            _orders.MarkCargoCollecting(ship, active.Id, transitions);
            return true;
        }
        CargoCollectionTarget target = GetCargoTarget(ship, active, action);
        bool wait = target.NeedsSelection || CargoAuthorityFailure(ship, active) is not null
            || CargoInRange(ship, target.Anchor, CargoPickupRange(ship));
        if (wait)
        {
            // Replacing a move materializes its exact velocity. In-range pickup
            // uses ordinary passive drag rather than freezing that velocity.
            if (_movement.GetState(ship) is ShipSpatialState.AtPosition)
                StartPassiveDrag(ship);
            if (active.MotionId is null && active.TransitId is null) _orders.AwaitAction(ship, active.Id, transitions);
            _cargoDirtyShips.Add(ship);
            return true;
        }
        if (_movement.GetState(ship) is ShipSpatialState.AnalyticManeuver
            { Maneuver.Plan.Kind: BoundedTerminalPlanKind.PassiveDrag })
        {
            // A strict exit discards pickup time, but committed drag still owns
            // motion. The ordinary waiting state saves this unbound interval;
            // its physical completion will resume approach planning.
            _orders.AwaitAction(ship, active.Id, transitions);
            return true;
        }
        return active.MotionId is not null || active.TransitId is not null;
    }

    private ulong CargoPickupRange(ShipId ship) => _lifecycle.GetOperationalShipDefinition(_lifecycle.GetRequiredShip(ship).DesignId)!.PickupRangeUnits;
    private bool CargoInRange(ShipId ship, SystemPosition target, ulong range) => CargoCollectionAttemptOwner.InRange(
        _movement.CaptureInteractionReadView(CurrentTime, [ship]), ship, target, range);

    /// <summary>Resolves only the saved local anchor; this never refreshes an approaching order from hidden cargo state.</summary>
    private CargoCollectionTarget GetCargoTarget(ShipId ship, ShipOrder order, ShipActionIntent.Cargo action)
    {
        if (_cargoTargets.TryGetValue(order.Id, out CargoCollectionTarget? target)) return target;
        // A currently covered field target that cannot fit must enter covered
        // reselection before travel. Hidden targets retain their admitted anchor.
        CargoObjectView? observed = action is ShipActionIntent.CollectField
            ? CaptureCoveredCargo(_lifecycle.GetRequiredShip(ship).PrincipalId)
                .FirstOrDefault(view => view.EntityId == action.LastObservedObjectEntityId)
            : null;
        bool needsSelection = observed is not null && _lifecycle.GetRequiredCargo(ship).RemainingCapacity < observed.UnitCapacityCost;
        target = new(ship, order.Id, action.LastObservedObjectEntityId, action.LastObservedPosition, needsSelection);
        _cargoTargets.Add(order.Id, target);
        return target;
    }

    /// <summary>Separates a field order's current observed approach from its immutable originally accepted intent.</summary>
    private NavigationDestination CargoDestination(ShipId ship, ShipOrder order) => order.Action is ShipActionIntent.Cargo action
        ? new NavigationDestination.Position(GetCargoTarget(ship, order, action).Anchor) : order.Destination;

    /// <summary>Checks actor/order authority without reading hidden target liveness during approach.</summary>
    private ShipOrderReason? CargoAuthorityFailure(ShipId ship, ShipOrder active)
    {
        if (_control.CheckCommand(ship, active.Source) != ActorCommandEligibility.Eligible
            || _movement.GetState(ship) is ShipSpatialState.InteractionControlled) return ShipOrderReason.ActionUnauthorized;
        GameSessionShip actor = _lifecycle.GetRequiredShip(ship);
        return active.Action is not ShipActionIntent.Cargo action || action.CargoInventoryId != actor.CargoInventoryId
            || _lifecycle.GetRequiredCargo(ship).Custody?.ControllingPrincipalId != actor.PrincipalId ? ShipOrderReason.ActionUnauthorized : null;
    }

    /// <summary>Withdraws timers on replacement/suspension/control loss and keeps one stationary interest per active cargo target.</summary>
    private void SynchronizeCargoInterest(ShipId ship, bool renew = false)
    {
        ShipOrder? active = _orders.Contains(ship) ? _orders.GetActive(ship) : null;
        if (_cargoAttempts?.Get(ship) is { } attempt && (active is null || active.Id != attempt.OrderId || CargoAuthorityFailure(ship, active) is not null))
            _cargoAttempts.Cancel(ship, _cargoDueKeys);
        if (_cargoInterests is null) return;
        CargoCollectionTarget? target = active?.Action is ShipActionIntent.Cargo action ? GetCargoTarget(ship, active, action) : null;
        if (_cargoBindings.TryGetValue(ship, out var binding))
        {
            if (!renew && target is { NeedsSelection: false } && active!.Id == binding.Order
                && target.TargetId == binding.Target && target.Anchor == binding.Anchor && CargoAuthorityFailure(ship, active) is null) return;
            _cargoInterests.Cancel(binding.Interest);
            _cargoBindings.Remove(ship);
        }
        if (target is not null && active is not null)
        {
            _cargoDirtyShips.Add(ship);
            if (!target.NeedsSelection)
            {
                StationaryInteractionInterest interest = _cargoInterests.RegisterStationary(active.Id.ToString(), ship,
                    target.Anchor, CargoPickupRange(ship), target.TargetId);
                _cargoBindings.Add(ship, (active.Id, interest.Id, target.TargetId, target.Anchor));
            }
        }
        HashSet<ShipOrderId> retained = _orders.Contains(ship)
            ? _orders.CaptureQueue(ship).Concat(_orders.CaptureSuspended(ship)).Select(order => order.Id).ToHashSet() : [];
        if (active is not null) retained.Add(active.Id);
        foreach (ShipOrderId id in _cargoTargets.Values.Where(value => value.ShipId == ship && !retained.Contains(value.OrderId)).Select(value => value.OrderId).ToArray())
            _cargoTargets.Remove(id);
    }

    /// <summary>Schedules a coarse changed-work batch once; stable attempts and outside approaches never poll the agenda.</summary>
    private void FlushCargoReevaluations()
    {
        if (_cargoDirtyShips.Count == 0) return;
        CargoCollectionWake[] work = _cargoDirtyShips.OrderBy(ship => ship.Value).Where(_orders.Contains)
            .Select(ship => (Ship: ship, Order: _orders.GetActive(ship)))
            .Where(value => value.Order is { Action: ShipActionIntent.Cargo })
            .Select(value => new CargoCollectionWake(value.Ship, value.Order!.Id, value.Order.Action!.OriginCommandSequence)).ToArray();
        _cargoDirtyShips.Clear();
        if (work.Length == 0) return;
        EventPhase phase = _agenda.CurrentPhase == EventPhase.Decision ? EventPhase.Decision : EventPhase.StateUpdate;
        _agenda.Schedule(CurrentTime, phase, default, new GameEvent.CargoCollectionReevaluation(Array.AsReadOnly(work)));
    }

    /// <summary>Rebinds changed committed motion before due completions; outside or unavailable attempts are queued for the cargo reduction.</summary>
    private void PrepareCargoStateUpdate()
    {
        if (_cargoAttempts is null) return;
        foreach (ShipId ship in _cargoBindings.Keys.Concat(_cargoAttempts.CaptureAttempts().Select(attempt => attempt.ShipId)).Distinct().OrderBy(ship => ship.Value).ToArray())
            SynchronizeCargoInterest(ship);
        foreach (CargoCollectionAttempt attempt in _cargoAttempts.CaptureAttempts())
        {
            InteractionSpatialReadView view = _movement.CaptureInteractionReadView(CurrentTime, [attempt.ShipId]);
            if (_cargo!.GetLiveObject(attempt.TargetId) is null
                || !CargoCollectionAttemptOwner.InRange(view, attempt.ShipId, attempt.TargetPosition, CargoPickupRange(attempt.ShipId)))
                _cargoDirtyShips.Add(attempt.ShipId);
            else _cargoAttempts.RefreshForecast(attempt.ShipId, view, _cargoDueKeys);
        }
        FlushCargoReevaluations();
    }

    /// <summary>Commits starts and terminal resource failures only at the owned ship's execution boundary, never during hidden approach.</summary>
    private void CommitCargoReevaluations(IEnumerable<CargoCollectionDue> work)
    {
        foreach (CargoCollectionDue entry in work.OrderBy(item => item.Work.ShipId.Value).ThenBy(item => item.Work.AcceptedCommand.Value))
        {
            ShipId ship = entry.Work.ShipId;
            if (!_orders.Contains(ship) || _orders.GetActive(ship) is not { Action: ShipActionIntent.Cargo action } active
                || active.Id != entry.Work.OrderId || action.OriginCommandSequence != entry.Work.AcceptedCommand) continue;
            if (CargoAuthorityFailure(ship, active) is { } authority)
            {
                if (_cargoAttempts!.Get(ship) is not null) _cargoAttempts.Cancel(ship, _cargoDueKeys);
                EndCargoAction(ship, active, authority, entry.Trigger);
                continue;
            }
            if (_cargoAttempts!.Get(ship) is not null) continue;
            CargoCollectionTarget target = GetCargoTarget(ship, active, action);
            if (target.NeedsSelection)
            {
                if (action is not ShipActionIntent.CollectField field || !SelectCargoFieldTarget(ship, active, field))
                { EndCargoAction(ship, active, null, entry.Trigger); continue; }
                target = _cargoTargets[active.Id];
            }
            if (!CargoInRange(ship, target.Anchor, CargoPickupRange(ship))) continue;
            CargoCollectionStartResult started = _cargoAttempts.Start(new(ship, active.Id, action.OriginCommandSequence, target.TargetId),
                _movement.CaptureInteractionReadView(CurrentTime, [ship]));
            if (started.Attempt is not null)
            {
                var transitions = new List<ShipOrderTransition>();
                _orders.MarkCargoCollecting(ship, active.Id, transitions);
                CommitCargoTransitions(entry.Trigger, transitions);
            }
            else if (started.Failure is CargoCollectionStartFailure.TargetUnavailable or CargoCollectionStartFailure.InsufficientCapacity
                && action is ShipActionIntent.CollectField)
                AdvanceCargoField(ship, active, entry.Trigger);
            else if (started.Failure is not CargoCollectionStartFailure.OutOfRange)
                EndCargoAction(ship, active, started.Failure == CargoCollectionStartFailure.InsufficientCapacity
                    ? ShipOrderReason.ActionCapacityUnavailable : ShipOrderReason.TargetRemoved, entry.Trigger);
        }
    }

    /// <summary>Uses covered members only, private alias resolution and the ship's current uncommitted capacity.</summary>
    private bool SelectCargoFieldTarget(ShipId ship, ShipOrder active, ShipActionIntent.CollectField field)
    {
        HashSet<EntityId> members = _cargo!.Fields.Members(field.TargetFieldId).ToHashSet();
        GameSessionShip actor = _lifecycle.GetRequiredShip(ship);
        if (_movement.PositionAt(ship, CurrentTime) is not { } position) return false;
        CargoObjectView? selected = CargoFieldCandidateSelector.Select(CaptureCoveredCargo(actor.PrincipalId)
            .Where(view => members.Contains(view.EntityId)).ToArray(), position, _lifecycle.GetRequiredCargo(ship).RemainingCapacity, field.Priorities);
        if (selected is null) return false;
        _cargoTargets[active.Id] = new(ship, active.Id, selected.EntityId, selected.Position);
        SynchronizeCargoInterest(ship, renew: true);
        _cargoContinueShips.Add(ship);
        return true;
    }

    /// <summary>Defers field reselection and new travel/timers until the complete cargo wave has settled.</summary>
    private void AdvanceCargoField(ShipId ship, ShipOrder active, EventKey trigger)
    {
        var transitions = new List<ShipOrderTransition>();
        var facts = new List<GameFactProposal>();
        EndActiveLocalMotion(ship, LocalMotionEndReason.ActionCompleted, facts);
        _orders.AwaitAction(ship, active.Id, transitions);
        CargoCollectionTarget prior = _cargoTargets[active.Id];
        _cargoTargets[active.Id] = prior with { NeedsSelection = true };
        SynchronizeCargoInterest(ship, renew: true);
        _cargoContinueShips.Add(ship);
        AddOrderTransitionProposals(transitions, facts);
        _facts.Commit(CurrentTime, new ScheduledEventFactCause(trigger), facts);
    }

    /// <summary>Ends an object action or exhausted field with ordinary order/motion ownership and generic target feedback.</summary>
    private void EndCargoAction(ShipId ship, ShipOrder active, ShipOrderReason? failure, EventKey trigger)
    {
        var transitions = new List<ShipOrderTransition>();
        var facts = new List<GameFactProposal>();
        EndActiveLocalMotion(ship, failure is null ? LocalMotionEndReason.ActionCompleted : LocalMotionEndReason.ActionFailed, facts);
        if (failure is { } reason) _orders.FailActive(ship, active.Id, transitions, reason);
        else _orders.CompleteActive(ship, active.Id, transitions, ShipOrderReason.ActionCompleted);
        _cargoTargets.Remove(active.Id);
        SynchronizeCargoInterest(ship, renew: true);
        _cargoContinueShips.Add(ship);
        AddOrderTransitionProposals(transitions, facts);
        _facts.Commit(CurrentTime, new ScheduledEventFactCause(trigger), facts);
    }

    private void CommitCargoTransitions(EventKey trigger, IEnumerable<ShipOrderTransition> transitions)
    {
        var facts = new List<GameFactProposal>();
        AddOrderTransitionProposals(transitions, facts);
        _facts.Commit(CurrentTime, new ScheduledEventFactCause(trigger), facts);
    }

    /// <summary>Interrupts strict range exits and target loss before any contested completion can consume cargo.</summary>
    private void InvalidateCargoAttempts(EventKey trigger)
    {
        if (_cargoAttempts is null) return;
        foreach (CargoCollectionAttempt attempt in _cargoAttempts.CaptureAttempts())
        {
            ShipId ship = attempt.ShipId;
            ShipOrder? active = _orders.Contains(ship) ? _orders.GetActive(ship) : null;
            if (active is null || active.Id != attempt.OrderId)
            {
                _cargoAttempts.Cancel(ship, _cargoDueKeys);
                continue;
            }
            bool targetLost = _cargo!.GetLiveObject(attempt.TargetId) is null;
            bool outside = !CargoInRange(ship, attempt.TargetPosition, CargoPickupRange(ship));
            if (CargoAuthorityFailure(ship, active) is not null || targetLost || outside)
            {
                _cargoAttempts.Cancel(ship, _cargoDueKeys);
                if (targetLost)
                {
                    if (active.Action is ShipActionIntent.CollectField) AdvanceCargoField(ship, active, trigger);
                    else EndCargoAction(ship, active, ShipOrderReason.TargetRemoved, trigger);
                }
                else if (CargoAuthorityFailure(ship, active) is { } reason) EndCargoAction(ship, active, reason, trigger);
                else
                {
                    var transitions = new List<ShipOrderTransition>();
                    _orders.ResumeCargoApproach(ship, active.Id, transitions);
                    SynchronizeCargoInterest(ship, renew: true);
                    if (active.MotionId is null && active.TransitId is null) _cargoContinueShips.Add(ship);
                    CommitCargoTransitions(trigger, transitions);
                }
            }
        }
    }

    /// <summary>Reduces completion contention by accepted command then ship, expires last, and starts no new timer during reduction.</summary>
    private Dictionary<EventKey, ScheduledEventDisposition> CommitCargoWave(IReadOnlyList<ScheduledEvent<GameEvent>> events,
        IEnumerable<CargoCollectionDue> reevaluations)
    {
        var dispositions = new Dictionary<EventKey, ScheduledEventDisposition>();
        ScheduledEvent<GameEvent>[] cargoEvents = events.Where(item => item.Payload is GameEvent.CargoExpiry
            or GameEvent.CargoCollectionCompletion or GameEvent.CargoCollectionRangeExit or GameEvent.CargoCollectionReevaluation).ToArray();
        if (cargoEvents.Length == 0 && !reevaluations.Any()) return dispositions;
        EventKey trigger = cargoEvents.Length > 0 ? cargoEvents[0].Key : reevaluations.First().Trigger;
        HashSet<EventKey> liveExits = cargoEvents.Where(item => _cargoAttempts?.MatchExit(item) is not null)
            .Select(item => item.Key).ToHashSet();
        InvalidateCargoAttempts(trigger);
        HashSet<EventKey> dueExpiry = cargoEvents.Where(item => item.Payload is GameEvent.CargoExpiry).Select(item => item.Key).ToHashSet();
        foreach (ScheduledEvent<GameEvent> item in cargoEvents.Where(item => item.Payload is GameEvent.CargoCollectionCompletion)
            .OrderBy(item => _cargoAttempts?.Get(((GameEvent.CargoCollectionCompletion)item.Payload).ShipId)?.AcceptedCommand.Value ?? ulong.MaxValue)
            .ThenBy(item => ((GameEvent.CargoCollectionCompletion)item.Payload).ShipId.Value))
        {
            CargoCollectionAttempt? attempt = _cargoAttempts?.MatchCompletion(item);
            if (attempt is null) { dispositions.Add(item.Key, ScheduledEventDisposition.IgnoredStaleGeneration); continue; }
            GameSessionShip collector = _lifecycle.GetRequiredShip(attempt.ShipId);
            var prepared = _cargo!.PrepareCollection(new(collector, _lifecycle.Entities.GetEntityId(attempt.ShipId)!.Value,
                attempt.OrderId, attempt.AcceptedCommand, attempt.Generation, attempt.TargetId, attempt.ReservedQuantity,
                attempt.CapacityReservationId, item.Key, dueExpiry));
            if (prepared.Prepared is { } settlement && _cargo.CommitCollection(settlement).WasApplied)
            {
                _cargoAttempts!.FinishSettled(attempt, _cargoDueKeys);
                ShipOrder active = _orders.GetActive(attempt.ShipId)!;
                if (active.Action is ShipActionIntent.CollectField) AdvanceCargoField(attempt.ShipId, active, item.Key);
                else EndCargoAction(attempt.ShipId, active, null, item.Key);
            }
            else
            {
                _cargoAttempts!.Cancel(attempt.ShipId, _cargoDueKeys);
                ShipOrder active = _orders.GetActive(attempt.ShipId)!;
                if (active.Action is ShipActionIntent.CollectField) AdvanceCargoField(attempt.ShipId, active, item.Key);
                else EndCargoAction(attempt.ShipId, active, ShipOrderReason.TargetRemoved, item.Key);
            }
            dispositions.Add(item.Key, ScheduledEventDisposition.Applied);
        }
        foreach (ScheduledEvent<GameEvent> item in cargoEvents.Where(item => item.Payload is GameEvent.CargoExpiry)
            .OrderBy(item => ((GameEvent.CargoExpiry)item.Payload).ObjectId.Value))
            dispositions.Add(item.Key, _cargo?.Expire(item) ?? ScheduledEventDisposition.IgnoredMissingReference);
        InvalidateCargoAttempts(trigger);
        foreach (ScheduledEvent<GameEvent> item in cargoEvents.Where(item => item.Payload is GameEvent.CargoCollectionRangeExit))
            dispositions.Add(item.Key, liveExits.Contains(item.Key) ? ScheduledEventDisposition.Applied : ScheduledEventDisposition.IgnoredStaleGeneration);
        IEnumerable<CargoCollectionDue> work = reevaluations.Concat(cargoEvents.Where(item => item.Payload is GameEvent.CargoCollectionReevaluation)
            .SelectMany(item => ((GameEvent.CargoCollectionReevaluation)item.Payload).Work.Select(wake => new CargoCollectionDue(wake, item.Key))));
        CommitCargoReevaluations(work);
        foreach (ScheduledEvent<GameEvent> item in cargoEvents.Where(item => item.Payload is GameEvent.CargoCollectionReevaluation))
            dispositions.Add(item.Key, ScheduledEventDisposition.Applied);
        ContinueCargoOrders(trigger);
        return dispositions;
    }

    /// <summary>Progresses buffered next actions after completion/expiry reduction; collection starts remain a later event wave.</summary>
    private void ContinueCargoOrders(EventKey trigger)
    {
        foreach (ShipId ship in _cargoContinueShips.OrderBy(ship => ship.Value).ToArray())
        {
            var transitions = new List<ShipOrderTransition>();
            var facts = new List<GameFactProposal>();
            if (_orders.Contains(ship))
            {
                StartOrContinueOrders(ship, transitions, facts);
                if (!_orders.HasActive(ship)) StartPassiveDrag(ship);
            }
            AddOrderTransitionProposals(transitions, facts);
            if (facts.Count > 0) _facts.Commit(CurrentTime, new ScheduledEventFactCause(trigger), facts);
        }
        _cargoContinueShips.Clear();
        FlushCargoReevaluations();
    }
}
