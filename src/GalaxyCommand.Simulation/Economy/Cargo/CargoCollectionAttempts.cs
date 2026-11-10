using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record CargoCollectionStartRequest(ShipId ShipId, ShipOrderId OrderId, CommandSequence AcceptedCommand, EntityId TargetId);
internal enum CargoCollectionStartFailure { InvalidActor, InvalidInput, AlreadyCollecting, TargetUnavailable, OutOfRange, InsufficientCapacity, IdentifierExhausted, NumericExhaustion }
internal sealed record CargoCollectionStartResult(CargoCollectionAttempt? Attempt = null, CargoCollectionStartFailure? Failure = null);
internal sealed record CargoCollectionAttempt(ShipId ShipId, ShipOrderId OrderId, CommandSequence AcceptedCommand,
    EventGeneration Generation, EntityId TargetId, SystemPosition TargetPosition, QualifiedContentKey DefinitionKey,
    Quantity UnitCapacityCost, Quantity ReservedQuantity, ReservationId CapacityReservationId,
    SimulationTime StartedAt, Quantity StartStorage, SimulationTime DueAt, EventKey CompletionKey, EventKey? ExitKey,
    SpatialRevision MotionRevision, ShipManeuverCapabilityRevision CapabilityRevision, ulong PickupRange);

/// <summary>
/// Owns collection time, incoming capacity and exact pending work. The order host
/// supplies current command/control authority and decides outcomes and reselection.
/// No floating cargo is reserved and no movement is started, stopped or replaced.
/// </summary>
internal sealed partial class CargoCollectionAttemptOwner
{
    private readonly EntityLifecycleOwner _lifecycle;
    private readonly CargoDispositionOwner _cargo;
    private readonly CargoDispositionPolicy _policy;
    private readonly InventoryCommitOwner _inventoryCommit;
    private readonly SpatialMovement _movement;
    private readonly EventAgenda<GameEvent> _agenda;
    private readonly SortedDictionary<ShipId, CargoCollectionAttempt> _attempts = new(EntityIdComparer<ShipId>.Instance);
    private readonly SortedDictionary<ShipId, EventGeneration> _generations = new(EntityIdComparer<ShipId>.Instance);
    private bool _isHealthy = true;

    internal CargoCollectionAttemptOwner(EntityLifecycleOwner lifecycle, CargoDispositionOwner cargo,
        CargoDispositionPolicy policy, InventoryCommitOwner inventoryCommit, SpatialMovement movement, EventAgenda<GameEvent> agenda)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _inventoryCommit = inventoryCommit ?? throw new ArgumentNullException(nameof(inventoryCommit));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
    }

    /// <summary>
    /// Starts a fresh full-duration attempt using the target's entire current occupied
    /// storage. All counters and agenda capacity are checked before reserving incoming
    /// room. The caller has already validated this order's current source authority.
    /// </summary>
    internal CargoCollectionStartResult Start(CargoCollectionStartRequest request, InteractionSpatialReadView view)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(view);
        if (request.ShipId.Value == 0 || request.OrderId.Value == 0 || request.AcceptedCommand.Value == 0
            || request.TargetId.Value == 0 || view.Timestamp != _agenda.CurrentTime) return new(Failure: CargoCollectionStartFailure.InvalidInput);
        if (_attempts.ContainsKey(request.ShipId)) return new(Failure: CargoCollectionStartFailure.AlreadyCollecting);
        if (_lifecycle.Entities.GetEntityId(request.ShipId) is not { } entityId
            || view.Get(request.ShipId) is not { } participant || participant.Revision != _movement.GetSpatialRevision(request.ShipId))
            return new(Failure: CargoCollectionStartFailure.InvalidActor);
        GameSessionShip actor = _lifecycle.GetRequiredShip(request.ShipId);
        Inventory inventory = _lifecycle.GetRequiredCargo(request.ShipId);
        if (inventory.Custody != new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(entityId), actor.PrincipalId)
            || _lifecycle.GetOperationalShipDefinition(actor.DesignId) is not { } definition)
            return new(Failure: CargoCollectionStartFailure.InvalidActor);
        CargoObjectView? target = _cargo.CaptureObjectView(request.TargetId);
        if (target is null) return new(Failure: CargoCollectionStartFailure.TargetUnavailable);
        if (!InRange(view, request.ShipId, target.Position, definition.PickupRangeUnits)) return new(Failure: CargoCollectionStartFailure.OutOfRange);
        ulong units = Math.Min(target.Quantity.Units, inventory.RemainingCapacity.Units / target.UnitCapacityCost.Units);
        if (units == 0) return new(Failure: CargoCollectionStartFailure.InsufficientCapacity);
        if (!_inventoryCommit.TryPeekCargoReservationIdentity(out ReservationId reservation))
            return new(Failure: CargoCollectionStartFailure.IdentifierExhausted);
        EventGeneration generation;
        SimulationTime due;
        SimulationTime? exit;
        Quantity capacity = new(checked(units * target.UnitCapacityCost.Units));
        try
        {
            generation = _generations.GetValueOrDefault(request.ShipId).Next();
            due = _agenda.CurrentTime.Add(CargoDispositionArithmetic.CollectionDuration(target.OccupiedStorage,
                _policy.MinimumCollectionDuration, _policy.MaximumCollectionDuration, _policy.CollectionStorageCap));
            exit = ForecastExit(view, request.ShipId, target.Position, definition.PickupRangeUnits, due);
            _agenda.EnsureCreationCapacity(exit is null ? 1 : 2);
        }
        catch (OverflowException) { return new(Failure: CargoCollectionStartFailure.NumericExhaustion); }
        var owner = new ReservationOwner.CargoCollection(request.ShipId, request.OrderId, generation);
        if (!_inventoryCommit.CanReserveCargoCapacity(inventory, capacity, owner, reservation))
            return new(Failure: CargoCollectionStartFailure.IdentifierExhausted);
        try
        {
            _inventoryCommit.ApplyCargoCapacityReservation(inventory, capacity, owner, reservation);
            EventKey completion = _agenda.Schedule(due, EventPhase.StateUpdate, generation,
                new GameEvent.CargoCollectionCompletion(request.ShipId, request.OrderId, generation));
            EventKey? exitKey = exit is { } at ? _agenda.Schedule(at, EventPhase.StateUpdate, generation,
                new GameEvent.CargoCollectionRangeExit(request.ShipId, request.OrderId, generation)) : null;
            var attempt = new CargoCollectionAttempt(request.ShipId, request.OrderId, request.AcceptedCommand, generation,
                request.TargetId, target.Position, target.DefinitionKey, target.UnitCapacityCost, new(units), reservation,
                _agenda.CurrentTime, target.OccupiedStorage, due, completion, exitKey, participant.Revision,
                actor.ManeuverCapabilityRevision, definition.PickupRangeUnits);
            _generations[request.ShipId] = generation;
            _attempts.Add(request.ShipId, attempt);
            return new(Attempt: attempt);
        }
        catch { _isHealthy = false; throw; }
    }

    internal CargoCollectionAttempt? Get(ShipId ship) => _attempts.GetValueOrDefault(ship);
    internal IReadOnlyList<CargoCollectionAttempt> CaptureAttempts() => Array.AsReadOnly(_attempts.Values.ToArray());

    /// <summary>Releases capacity and exact outstanding work, discarding all elapsed time without altering motion.</summary>
    internal bool Cancel(ShipId ship, IReadOnlySet<EventKey> dueKeys)
    {
        ThrowIfUnhealthy();
        if (!_attempts.ContainsKey(ship)) return false;
        try
        {
            if (!TryPrepareCancellation(ship, dueKeys, out PreparedCargoAttemptCancellation? prepared)
                || !ApplyPreparedCancellation(prepared!))
                throw new InvalidOperationException("Cargo attempt's exact cancellation resources are missing or changed.");
            return true;
        }
        catch { _isHealthy = false; throw; }
    }

    /// <summary>
    /// Rebinds exact motion/capability revisions and replaces only changed exit work.
    /// Start size, duration, reserved quantity and completion identity remain fixed.
    /// The order host interrupts an already outside attempt before calling this method.
    /// </summary>
    internal void RefreshForecast(ShipId ship, InteractionSpatialReadView view, IReadOnlySet<EventKey> dueKeys)
    {
        ThrowIfUnhealthy();
        if (!_attempts.TryGetValue(ship, out CargoCollectionAttempt? attempt)) return;
        if (view.Timestamp != _agenda.CurrentTime || view.Get(ship) is not { } participant
            || participant.Revision != _movement.GetSpatialRevision(ship))
            throw new ArgumentException("Cargo range refresh requires current authoritative motion geometry.", nameof(view));
        GameSessionShip actor = _lifecycle.GetRequiredShip(ship);
        ulong range = _lifecycle.GetOperationalShipDefinition(actor.DesignId)!.PickupRangeUnits;
        SimulationTime? exit = ForecastExit(view, ship, attempt.TargetPosition, range, attempt.DueAt);
        if (exit <= _agenda.CurrentTime) throw new InvalidOperationException("An outside attempt must be interrupted before range refresh.");
        try
        {
            ValidateReservation(attempt);
            ValidateWork(attempt, dueKeys);
            EventKey? nextKey = attempt.ExitKey;
            if (exit != attempt.ExitKey?.Timestamp)
            {
                if (exit is not null) _agenda.EnsureCreationCapacity(1);
                if (attempt.ExitKey is { } old) CancelEntry(old, attempt.Generation, ExitPayload(attempt), dueKeys);
                nextKey = exit is { } at ? _agenda.Schedule(at, EventPhase.StateUpdate, attempt.Generation, ExitPayload(attempt)) : null;
            }
            _attempts[ship] = attempt with { ExitKey = nextKey, MotionRevision = participant.Revision,
                CapabilityRevision = actor.ManeuverCapabilityRevision, PickupRange = range };
        }
        catch { _isHealthy = false; throw; }
    }

    /// <summary>Matches only current, exact completion work already consumed by the StateUpdate wave.</summary>
    internal CargoCollectionAttempt? MatchCompletion(ScheduledEvent<GameEvent> scheduled) =>
        scheduled.Payload is GameEvent.CargoCollectionCompletion completion
            ? Match(scheduled, completion.ShipId, completion.OrderId, completion.AttemptGeneration, false) : null;

    /// <summary>Matches only the latest exit forecast; replaced motion forecasts are deterministic stale work.</summary>
    internal CargoCollectionAttempt? MatchExit(ScheduledEvent<GameEvent> scheduled) =>
        scheduled.Payload is GameEvent.CargoCollectionRangeExit exit
            ? Match(scheduled, exit.ShipId, exit.OrderId, exit.AttemptGeneration, true) : null;

    /// <summary>Retires attempt work after resource settlement has consumed and released its entire incoming reservation.</summary>
    internal void FinishSettled(CargoCollectionAttempt attempt, IReadOnlySet<EventKey> dueKeys)
    {
        ThrowIfUnhealthy();
        try
        {
            if (Get(attempt.ShipId) != attempt || _lifecycle.GetRequiredCargo(attempt.ShipId).GetPhysicalReservation(attempt.CapacityReservationId) is not null)
                throw new InvalidOperationException("Cargo attempt cannot finish before its exact capacity reservation settles.");
            ValidateWork(attempt, dueKeys);
            CancelWork(attempt, dueKeys);
            _attempts.Remove(attempt.ShipId);
        }
        catch { _isHealthy = false; throw; }
    }

    internal bool IsHealthy => _isHealthy;

    /// <summary>Checks inclusive exact geometry without preparing unrelated participants or rounded map positions.</summary>
    internal static bool InRange(InteractionSpatialReadView view, ShipId ship, SystemPosition target, ulong range)
    {
        InteractionPhaseCurve? curve = InteractionProximityQuery.Instant(view.Get(ship), view.Timestamp);
        return curve is not null && PreparedInteractionPoint.Capture(curve, view.Timestamp).InRange(
            PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(target, view.Timestamp, view.Timestamp), view.Timestamp), range);
    }

    /// <summary>Forecasts only the known current phase; physical boundary commits rebind later phases before evaluation.</summary>
    private static SimulationTime? ForecastExit(InteractionSpatialReadView view, ShipId ship, SystemPosition target, ulong range, SimulationTime through)
    {
        InteractionSpatialParticipant? participant = view.Get(ship);
        InteractionPhaseCurve? curve = participant?.ManeuverPlan is { } plan
            ? plan.InteractionCurves.LastOrDefault(phase => phase.StartsAt <= view.Timestamp && phase.EndsAt > view.Timestamp)
            : InteractionProximityQuery.Instant(participant, view.Timestamp);
        return curve is null || through < view.Timestamp ? null
            : CargoCollectionRangeForecast.FirstStrictExit(curve, target, view.Timestamp, through, range);
    }

    /// <summary>Detects missing or changed attempt capacity before cancelling or committing any other owned resource.</summary>
    private Inventory ValidateReservation(CargoCollectionAttempt attempt)
    {
        Inventory inventory = _lifecycle.GetRequiredCargo(attempt.ShipId);
        PhysicalReservation? reservation = inventory.GetPhysicalReservation(attempt.CapacityReservationId);
        if (reservation?.Owner != new ReservationOwner.CargoCollection(attempt.ShipId, attempt.OrderId, attempt.Generation)
            || reservation.Subject is not PhysicalReservationSubject.IncomingCapacity incoming
            || incoming.Quantity.Units != checked(attempt.ReservedQuantity.Units * attempt.UnitCapacityCost.Units))
            throw new InvalidOperationException("Cargo attempt's exact incoming capacity reservation is missing or changed.");
        return inventory;
    }

    /// <summary>Matches exact keys, payload generations and wave ownership without inspecting target liveness.</summary>
    private CargoCollectionAttempt? Match(ScheduledEvent<GameEvent> scheduled, ShipId ship, ShipOrderId order, EventGeneration generation, bool exit)
    {
        ThrowIfUnhealthy();
        if (Get(ship) is not { } attempt || attempt.OrderId != order || attempt.Generation != generation
            || scheduled.Generation != generation || (exit ? attempt.ExitKey : attempt.CompletionKey) != scheduled.Key
            || _agenda.CurrentTime != scheduled.Key.Timestamp || _agenda.CurrentPhase != EventPhase.StateUpdate
            || _agenda.CheckCancellation(scheduled.Key, generation, scheduled.Payload) != AgendaCancellationCheck.Missing) return null;
        return attempt;
    }

    /// <summary>Preflights both exact pending entries before cancelling either, accepting only keys captured in this due wave.</summary>
    private void ValidateWork(CargoCollectionAttempt attempt, IReadOnlySet<EventKey> dueKeys)
    {
        ValidateEntry(attempt.CompletionKey, attempt.Generation, CompletionPayload(attempt), dueKeys);
        if (attempt.ExitKey is { } exit) ValidateEntry(exit, attempt.Generation, ExitPayload(attempt), dueKeys);
    }

    /// <summary>Requires pending work or an exact same-time envelope already captured by the current StateUpdate wave.</summary>
    private void ValidateEntry(EventKey key, EventGeneration generation, GameEvent payload, IReadOnlySet<EventKey> dueKeys)
    {
        AgendaCancellationCheck check = _agenda.CheckCancellation(key, generation, payload);
        if (check == AgendaCancellationCheck.Matches) return;
        if (check == AgendaCancellationCheck.Missing && dueKeys.Contains(key) && key.Timestamp == _agenda.CurrentTime
            && _agenda.CurrentPhase == EventPhase.StateUpdate) return;
        throw new InvalidOperationException("Cargo attempt's exact pending event is missing or changed outside its captured due wave.");
    }

    /// <summary>Cancels prevalidated future work; popped same-wave envelopes need no second cancellation.</summary>
    private void CancelWork(CargoCollectionAttempt attempt, IReadOnlySet<EventKey> dueKeys)
    {
        CancelEntry(attempt.CompletionKey, attempt.Generation, CompletionPayload(attempt), dueKeys);
        if (attempt.ExitKey is { } exit) CancelEntry(exit, attempt.Generation, ExitPayload(attempt), dueKeys);
    }

    /// <summary>Removes an exact entry without allocating replacement creation sequences.</summary>
    private void CancelEntry(EventKey key, EventGeneration generation, GameEvent payload, IReadOnlySet<EventKey> dueKeys)
    {
        ValidateEntry(key, generation, payload, dueKeys);
        if (_agenda.CheckCancellation(key, generation, payload) == AgendaCancellationCheck.Matches
            && !_agenda.TryCancelExact(key, generation, payload)) throw new InvalidOperationException("Prepared cargo event cancellation changed during commit.");
    }

    private static GameEvent.CargoCollectionCompletion CompletionPayload(CargoCollectionAttempt attempt) => new(attempt.ShipId, attempt.OrderId, attempt.Generation);
    private static GameEvent.CargoCollectionRangeExit ExitPayload(CargoCollectionAttempt attempt) => new(attempt.ShipId, attempt.OrderId, attempt.Generation);

    /// <summary>Blocks further authoritative work after an invariant failure in a partially applied resource commit.</summary>
    private void ThrowIfUnhealthy()
    {
        if (!_isHealthy) throw new InvalidOperationException("Cargo attempt authority is unhealthy after an invariant failure.");
    }
}
