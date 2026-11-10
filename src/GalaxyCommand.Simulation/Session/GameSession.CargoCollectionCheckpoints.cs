using System.Diagnostics.CodeAnalysis;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>
    /// Joins structurally restored cargo timers and targets to admitted commands,
    /// retained orders, exact actor control, and historical observed object anchors.
    /// Validation is read-only and never refreshes hidden approach targets.
    /// </summary>
    internal static CheckpointValidationFailure? ValidateCargoCollectionCheckpoint(GameSessionCheckpoint checkpoint,
        PhysicalDefinitionCatalog? definitions = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        bool enabled = checkpoint.RuntimePolicies.CargoPolicy is not null;
        if (enabled && (checkpoint.Cargo is null || checkpoint.CargoCollectionAttempts is null || checkpoint.CargoCollectionTargets is null))
            return CargoCollectionJoinFailure("Cargo-enabled saves require collection attempts and retained target sections.");
        if (enabled && (checkpoint.SpatialInteractions?.EvaluationDomains ?? []).Contains(ActorOrderRuntimeCoordinator.CargoDomainKind))
            return CargoCollectionJoinFailure("Managed cargo collection cannot require an external evaluator binding.");

        var ships = checkpoint.Lifecycle.LiveShips.ToDictionary(ship => ship!.ShipId, ship => ship!);
        var orders = new Dictionary<ShipOrderId, CargoCollectionOrderLink>();
        var commands = new HashSet<CommandSequence>();
        CargoCollectionHistory? history;
        try { history = enabled ? new CargoCollectionHistory(checkpoint) : null; }
        catch (ArgumentException error) { return CargoCollectionJoinFailure(error.Message); }

        foreach (ShipActorOrdersCheckpoint? actor in checkpoint.Orders.Actors)
        {
            if (actor is null || !ships.TryGetValue(actor.ShipId, out EntityLifecycleShipCheckpoint? ship))
                return CargoCollectionJoinFailure("Collection orders require their exact live lifecycle actor.");
            ShipOrderWorkSetCheckpoint? current = actor.Override ?? actor.Base;
            foreach (ShipOrderWorkSetCheckpoint? work in new[] { actor.Base, actor.Override })
            {
                if (work is null) continue;
                foreach (ShipOrderCheckpoint? order in work.Queue.Append(work.Active).Append(work.LastTerminal))
                {
                    if (order is null) continue;
                    bool isCurrent = ReferenceEquals(work, current) && ReferenceEquals(order, work.Active)
                        && order.Status is ShipOrderStatus.Active or ShipOrderStatus.Waiting;
                    var link = new CargoCollectionOrderLink(ship, order, isCurrent, order.Status == ShipOrderStatus.Suspended);
                    if (!orders.TryAdd(order.Id, link)) return CargoCollectionJoinFailure("Retained order identities must be unique.");
                    if (order.Action is not { } action) continue;
                    if (!CargoJoinIdWasAllocated(order.Id.Value, checkpoint.Orders.OrderIds)
                        || !CargoJoinIdWasAllocated(action.OriginCommandSequence.Value, checkpoint.CommandAdmission.Sequences)
                        || !commands.Add(action.OriginCommandSequence))
                        return CargoCollectionJoinFailure("Action orders require allocated unique origin commands and allocated order identities.");
                    if (action is not ShipActionIntent.Cargo cargo) continue;
                    CheckpointValidationFailure? failure = ValidateCargoIntentJoin(checkpoint, link, cargo, history, definitions);
                    if (failure is not null) return failure;
                }
            }
        }

        var pendingWakes = checkpoint.Engine.Agenda.PendingEvents.Select(entry => entry.Payload)
            .OfType<GameEvent.CargoCollectionReevaluation>().Where(entry => entry.Work is not null)
            .SelectMany(entry => entry.Work).Where(wake => wake is not null)
            .Select(wake => (wake.ShipId, wake.OrderId, wake.AcceptedCommand)).ToHashSet();
        var targets = new Dictionary<ShipOrderId, CargoCollectionTarget>();
        foreach (CargoCollectionTarget? target in checkpoint.CargoCollectionTargets ?? [])
        {
            if (history is null) return CargoCollectionJoinFailure("Retained targets require cargo generation history.");
            if (target is null || target.ShipId.Value == 0 || target.OrderId.Value == 0 || target.TargetId.Value == 0
                || !orders.TryGetValue(target.OrderId, out CargoCollectionOrderLink? link)
                || target.ShipId != link.Ship.ShipId || !link.IsCurrent && !link.IsSuspended
                || link.Order.Action is not ShipActionIntent.Cargo cargo
                || !targets.TryAdd(target.OrderId, target)
                || !history.TryGet(target.TargetId, out CargoCollectionHistoricalObject? observed)
                || observed.Position != target.Anchor
                || cargo is ShipActionIntent.CollectObject objectAction
                    && (target.NeedsSelection || target.TargetId != objectAction.TargetEntityId || target.Anchor != objectAction.LastObservedPosition)
                || cargo is ShipActionIntent.CollectField fieldAction && !history.BelongsToField(observed, fieldAction.TargetFieldId))
                return CargoCollectionJoinFailure("Retained collection targets require an active or suspended cargo intent and exact historical object anchor.");
            // An active reselection has no stationary interest to wake it. Its
            // exact pending batch is the only continuation authority after restore.
            if (target.NeedsSelection && link.IsCurrent
                && !pendingWakes.Contains((target.ShipId, target.OrderId, cargo.OriginCommandSequence)))
                return CargoCollectionJoinFailure("An active field reselection requires its exact pending cargo reevaluation wake.");
        }
        foreach (CargoCollectionOrderLink link in orders.Values)
            if (link.IsCurrent && link.Order.Action is ShipActionIntent.Cargo && !targets.ContainsKey(link.Order.Id))
                return CargoCollectionJoinFailure("A current cargo order has no retained observed target.");

        CheckpointResult<ActorControlRegistry> control = ActorControlRegistry.RestoreCheckpoint(checkpoint.Control);
        if (!control.IsSuccess) return control.Failure;
        var attemptOrders = new HashSet<ShipOrderId>();
        var attemptShips = new HashSet<ShipId>();
        var inventories = checkpoint.Lifecycle.Inventories.Inventories.ToDictionary(inventory => inventory.Id);
        foreach (CargoCollectionAttempt? attempt in checkpoint.CargoCollectionAttempts?.Attempts ?? [])
        {
            if (attempt is null || !attemptOrders.Add(attempt.OrderId) || !attemptShips.Add(attempt.ShipId)
                || !CargoJoinIdWasAllocated(attempt.AcceptedCommand.Value, checkpoint.CommandAdmission.Sequences)
                || !CargoJoinIdWasAllocated(attempt.OrderId.Value, checkpoint.Orders.OrderIds)
                || !orders.TryGetValue(attempt.OrderId, out CargoCollectionOrderLink? link) || !link.IsCurrent
                || link.Order.Status != ShipOrderStatus.Active || link.Order.Reason != ShipOrderReason.CollectingCargo
                || link.Order.Action is not ShipActionIntent.Cargo action || link.Ship.ShipId != attempt.ShipId
                || action.OriginCommandSequence != attempt.AcceptedCommand || link.Order.Source is null
                || control.Value!.CheckCommand(attempt.ShipId, link.Order.Source) != ActorCommandEligibility.Eligible
                || !targets.TryGetValue(attempt.OrderId, out CargoCollectionTarget? target) || target.NeedsSelection
                || target.TargetId != attempt.TargetId || target.Anchor != attempt.TargetPosition
                || !inventories.TryGetValue(link.Ship.CargoInventoryId, out InventoryCheckpoint? inventory)
                || !HasExactCollectionReservation(inventory, attempt))
                return CargoCollectionJoinFailure("An active attempt must match its admitted current cargo order, target, control and incoming reservation.");
        }
        foreach (CargoCollectionOrderLink link in orders.Values)
            if (link.IsCurrent && link.Order.Reason == ShipOrderReason.CollectingCargo && !attemptOrders.Contains(link.Order.Id))
                return CargoCollectionJoinFailure("An actively collecting order requires its exact live attempt.");

        foreach (ScheduledEvent<GameEvent> entry in checkpoint.Engine.Agenda.PendingEvents)
        {
            if (entry.Payload is not GameEvent.CargoCollectionReevaluation reevaluation) continue;
            // These wakes are immediate, generationless work. Moving one into
            // the future must not delay a saved field's pending reselection.
            if (entry.Key.Timestamp != checkpoint.Engine.Agenda.CurrentTime || entry.Generation.Value != 0
                || reevaluation.Work is null || reevaluation.Work.Any(wake => wake is null
                || !CargoJoinIdWasAllocated(wake.ShipId.Value, checkpoint.Lifecycle.ShipIds)
                || !CargoJoinIdWasAllocated(wake.OrderId.Value, checkpoint.Orders.OrderIds)
                || !CargoJoinIdWasAllocated(wake.AcceptedCommand.Value, checkpoint.CommandAdmission.Sequences)
                || orders.TryGetValue(wake.OrderId, out CargoCollectionOrderLink? link)
                    && (link.Ship.ShipId != wake.ShipId || link.Order.Action is not ShipActionIntent.Cargo action
                        || action.OriginCommandSequence != wake.AcceptedCommand)))
                return CargoCollectionJoinFailure("A cargo reevaluation wake requires its immediate envelope and an allocated, matching admitted action.");
        }
        return null;
    }

    /// <summary>Validates immutable admitted intent independently of whether its originally observed object remains live.</summary>
    private static CheckpointValidationFailure? ValidateCargoIntentJoin(GameSessionCheckpoint checkpoint,
        CargoCollectionOrderLink link, ShipActionIntent.Cargo action, CargoCollectionHistory? history,
        PhysicalDefinitionCatalog? definitions)
    {
        if (history is null) return CargoCollectionJoinFailure("Cargo intent requires cargo generation history.");
        InventoryCheckpoint? inventory = checkpoint.Lifecycle.Inventories.Inventories.FirstOrDefault(candidate => candidate.Id == link.Ship.CargoInventoryId);
        if (action.CargoInventoryId != link.Ship.CargoInventoryId
            || inventory?.Custody != new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(link.Ship.EntityId), link.Ship.PrincipalId)
            || link.Order.Destination is not NavigationDestination.Position destination || destination.Value != action.LastObservedPosition
            || link.Order.RequestedHeading is not null || !history.TryGet(action.LastObservedObjectEntityId, out CargoCollectionHistoricalObject? observed)
            || observed.Position != action.LastObservedPosition)
            return CargoCollectionJoinFailure("Cargo intent disagrees with its live ship inventory or originally observed historical object.");
        if (action is ShipActionIntent.CollectField field
            && (!history.BelongsToField(observed, field.TargetFieldId)
                || field.Priorities.Any(key => definitions?.Get(key) is null)
                || field.Priorities.Distinct().Count() != field.Priorities.Count))
            return CargoCollectionJoinFailure("Field intent requires its historical field membership and unique known physical priorities.");
        return null;
    }

    /// <summary>Joins the incoming owner tuple and exact reserved storage without releasing any commitment.</summary>
    private static bool HasExactCollectionReservation(InventoryCheckpoint inventory, CargoCollectionAttempt attempt)
    {
        PhysicalReservation? reservation = inventory.PhysicalReservations.FirstOrDefault(candidate => candidate.Id == attempt.CapacityReservationId);
        return reservation?.Owner == new ReservationOwner.CargoCollection(attempt.ShipId, attempt.OrderId, attempt.Generation)
            && reservation.InventoryId == inventory.Id && reservation.Subject is PhysicalReservationSubject.IncomingCapacity incoming
            && (UInt128)attempt.ReservedQuantity.Units * attempt.UnitCapacityCost.Units == incoming.Quantity.Units;
    }

    /// <summary>Checks monotonic allocator history, including an exhausted sequence's valid historical identities.</summary>
    private static bool CargoJoinIdWasAllocated(ulong id, IdSequenceCheckpoint sequence) =>
        id != 0 && (sequence.NextValue is not { } next || id < next);

    private static CheckpointValidationFailure CargoCollectionJoinFailure(string message) => new("$.checkpoint.cargoCollection", message);
    private sealed record CargoCollectionOrderLink(EntityLifecycleShipCheckpoint Ship, ShipOrderCheckpoint Order, bool IsCurrent, bool IsSuspended);
    private sealed record CargoCollectionHistoricalObject(SystemPosition Position, DebrisFieldId? OriginalField);

    /// <summary>
    /// Derives historical anchors from authoritative deterministic generation receipts.
    /// Current live lookups cannot prove an approaching order's already removed target.
    /// This validation cache is discarded and does not become saved gameplay authority.
    /// </summary>
    private sealed class CargoCollectionHistory
    {
        private readonly Dictionary<EntityId, (CargoDispositionReceipt Receipt, int Ordinal)> _objects = [];
        private readonly Dictionary<EntityId, CargoCollectionHistoricalObject> _anchors = [];
        private readonly Dictionary<DebrisFieldId, DebrisFieldId> _aliases;
        private readonly HashSet<DebrisFieldId> _fields;
        private readonly CargoDispositionPolicyCheckpoint _policy;
        private readonly DeterministicRandomOwner _random;

        /// <summary>Indexes exact object ordinals and retained field handles without regenerating unrelated positions.</summary>
        internal CargoCollectionHistory(GameSessionCheckpoint checkpoint)
        {
            _policy = checkpoint.RuntimePolicies.CargoPolicy!;
            _random = new DeterministicRandomOwner(RandomRootSeed.FromBytes(checkpoint.Random!.RootSeed));
            _aliases = checkpoint.Cargo!.Fields.Aliases.ToDictionary(alias => alias.Alias, alias => alias.Canonical);
            _fields = checkpoint.Cargo.Receipts.Where(receipt => receipt.FieldHandle is not null).Select(receipt => receipt.FieldHandle!.Value).ToHashSet();
            foreach (CargoDispositionReceipt receipt in checkpoint.Cargo.Receipts)
                for (int ordinal = 0; ordinal < receipt.ObjectIds.Count; ordinal++)
                    _objects.Add(receipt.ObjectIds[ordinal], (receipt, ordinal));
        }

        /// <summary>Uses flattened historical aliases even after the merged field has emptied.</summary>
        internal bool BelongsToField(CargoCollectionHistoricalObject cargo, DebrisFieldId field) =>
            cargo.OriginalField is { } original && _fields.Contains(field) && _fields.Contains(original)
            && _aliases.GetValueOrDefault(field, field) == _aliases.GetValueOrDefault(original, original);

        /// <summary>Regenerates only the requested object's named scatter sample, preserving exact original type/object ordering.</summary>
        internal bool TryGet(EntityId id, [NotNullWhen(true)] out CargoCollectionHistoricalObject? historical)
        {
            if (_anchors.TryGetValue(id, out historical)) return true;
            if (!_objects.TryGetValue(id, out var origin)) return false;
            int remaining = origin.Ordinal;
            foreach (CargoDispositionTypeOutcome outcome in origin.Receipt.Outcomes)
            {
                int count = outcome.Kind == PhysicalHoldingKind.Fungible
                    ? (int)Math.Min(outcome.Recoverable.Units, (ulong)_policy.FungibleObjectCount) : outcome.SurvivingInstances.Count;
                if (remaining >= count) { remaining -= count; continue; }
                ulong ordinal = outcome.Kind == PhysicalHoldingKind.Fungible ? (ulong)remaining : outcome.SurvivingInstances[remaining].Value;
                CargoDispositionKey key = origin.Receipt.Request.Key;
                CargoDispositionGeneration? generator = key.Trigger switch
                {
                    CargoDispositionTrigger.Command command => new(_random, key.SourceEntityId, command.Sequence,
                        key.OccurrenceOrdinal, _policy.GenerationBehaviorVersion, outcome.DefinitionKey),
                    CargoDispositionTrigger.Scheduled scheduled => new(_random, key.SourceEntityId, scheduled.Key,
                        key.OccurrenceOrdinal, _policy.GenerationBehaviorVersion, outcome.DefinitionKey),
                    _ => null,
                };
                if (generator is null) return false;
                historical = new(generator.Scatter(origin.Receipt.Request.SourcePosition, checked((long)_policy.ScatterRadiusMeters), ordinal), origin.Receipt.FieldHandle);
                _anchors.Add(id, historical);
                return true;
            }
            return false;
        }
    }
}
