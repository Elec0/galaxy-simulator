namespace GalaxyCommand.Simulation;

internal sealed record CargoCollectionGenerationCheckpoint(ShipId ShipId, EventGeneration Generation);

internal sealed record CargoCollectionAttemptOwnerCheckpoint
{
    /// <summary>Copies checkpoint containers so future caller edits cannot change retained timer authority.</summary>
    internal CargoCollectionAttemptOwnerCheckpoint(IEnumerable<CargoCollectionGenerationCheckpoint?> generations,
        IEnumerable<CargoCollectionAttempt?> attempts)
    {
        ArgumentNullException.ThrowIfNull(generations);
        ArgumentNullException.ThrowIfNull(attempts);
        Generations = Array.AsReadOnly(generations.ToArray());
        Attempts = Array.AsReadOnly(attempts.ToArray());
    }
    internal IReadOnlyList<CargoCollectionGenerationCheckpoint?> Generations { get; }
    internal IReadOnlyList<CargoCollectionAttempt?> Attempts { get; }
}

internal sealed partial class CargoCollectionAttemptOwner
{
    /// <summary>Captures canonical attempt and generation authority only at a healthy completed timestamp.</summary>
    internal CheckpointResult<CargoCollectionAttemptOwnerCheckpoint> CaptureCheckpoint()
    {
        if (!_isHealthy || _agenda.CurrentPhase is not null)
            return CheckpointResult<CargoCollectionAttemptOwnerCheckpoint>.Rejected(new("$.checkpoint.cargoCollectionAttempts",
                "Cargo attempt capture requires healthy authority and a completed timestamp."));
        return CheckpointResult<CargoCollectionAttemptOwnerCheckpoint>.Success(new(
            _generations.Select(pair => new CargoCollectionGenerationCheckpoint(pair.Key, pair.Value)), _attempts.Values));
    }

    /// <summary>
    /// Restores unpublished timer authority directly after validating exact live
    /// inventories, motion, target state, and pending work. It allocates no identity,
    /// reserves no capacity, and neither schedules nor cancels an agenda envelope.
    /// </summary>
    internal static CheckpointResult<CargoCollectionAttemptOwner> Restore(CargoCollectionAttemptOwnerCheckpoint checkpoint,
        EntityLifecycleOwner lifecycle, CargoDispositionOwner cargo, CargoDispositionPolicy policy,
        InventoryCommitOwner inventoryCommit, SpatialMovement movement, EventAgenda<GameEvent> agenda)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(cargo);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(inventoryCommit);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(agenda);
        CheckpointResult<EventAgendaCheckpoint<GameEvent>> pending = agenda.CaptureCheckpoint();
        if (!pending.IsSuccess || !cargo.IsHealthy)
            return AttemptCheckpointRejected("Cargo attempt restoration requires healthy cargo and a completed timestamp.");

        var restored = new CargoCollectionAttemptOwner(lifecycle, cargo, policy, inventoryCommit, movement, agenda);
        EntityLifecycleCheckpoint life = lifecycle.CaptureCheckpoint();
        foreach (CargoCollectionGenerationCheckpoint? row in checkpoint.Generations)
            if (row is null || !AttemptIdWasAllocated(row.ShipId.Value, life.ShipIds) || row.Generation.Value == 0
                || !restored._generations.TryAdd(row.ShipId, row.Generation))
                return AttemptCheckpointRejected("Generation history requires unique allocated ships and positive generations.");

        var live = lifecycle.Ships.ToDictionary(ship => ship.Id);
        var targets = cargo.CaptureObjects().ToDictionary(target => target.EntityId);
        InteractionSpatialReadView geometry = movement.CaptureInteractionReadView(agenda.CurrentTime,
            checkpoint.Attempts.Where(attempt => attempt is not null).Select(attempt => attempt!.ShipId));
        IdSequenceCheckpoint reservationIds = inventoryCommit.CaptureCheckpoint().ReservationIds;
        var usedReservations = new HashSet<ReservationId>();
        var usedWork = new HashSet<EventKey>();
        try
        {
            foreach (CargoCollectionAttempt? attempt in checkpoint.Attempts)
            {
                if (attempt is null || !live.TryGetValue(attempt.ShipId, out GameSessionShip? actor)
                    || !restored._generations.TryGetValue(attempt.ShipId, out EventGeneration generation)
                    || generation != attempt.Generation || restored._attempts.ContainsKey(attempt.ShipId)
                    || !usedReservations.Add(attempt.CapacityReservationId))
                    return AttemptCheckpointRejected("Each live attempt requires one actor, generation and unique capacity reservation.");
                string? failure = restored.ValidateRestoredAttempt(attempt, actor, targets, geometry, reservationIds, usedWork);
                if (failure is not null) return AttemptCheckpointRejected(failure);
                restored._attempts.Add(attempt.ShipId, attempt);
            }
        }
        catch (ArgumentException error) { return AttemptCheckpointRejected(error.Message); }
        catch (InvalidOperationException error) { return AttemptCheckpointRejected(error.Message); }
        catch (OverflowException) { return AttemptCheckpointRejected("Cargo attempt arithmetic or time is not representable."); }

        foreach (ScheduledEvent<GameEvent> entry in pending.Value!.PendingEvents)
            if (entry.Payload is GameEvent.CargoCollectionCompletion or GameEvent.CargoCollectionRangeExit
                && !usedWork.Contains(entry.Key))
                return AttemptCheckpointRejected("The agenda contains an orphan cargo completion or range-exit envelope.");
        foreach (InventoryCheckpoint inventory in life.Inventories.Inventories)
            foreach (PhysicalReservation reservation in inventory.PhysicalReservations)
                if (reservation.Owner is ReservationOwner.CargoCollection owner
                    && (!restored._attempts.TryGetValue(owner.ShipId, out CargoCollectionAttempt? attempt)
                        || owner.OrderId != attempt.OrderId || owner.AttemptGeneration != attempt.Generation
                        || reservation.Id != attempt.CapacityReservationId || inventory.Id != live[owner.ShipId].CargoInventoryId))
                    return AttemptCheckpointRejected("A cargo incoming-capacity reservation has no exact live attempt owner.");
        return CheckpointResult<CargoCollectionAttemptOwner>.Success(restored);
    }

    /// <summary>
    /// Validates saved start-time size independently of current contested remainder,
    /// preserving the original full-duration timer and exact reservation and event keys.
    /// Motion and capability changes must already have refreshed the saved exit forecast.
    /// </summary>
    private string? ValidateRestoredAttempt(CargoCollectionAttempt attempt, GameSessionShip actor,
        Dictionary<EntityId, CargoLiveObject> targets, InteractionSpatialReadView geometry,
        IdSequenceCheckpoint reservationIds, HashSet<EventKey> usedWork)
    {
        if (attempt.OrderId.Value == 0 || attempt.AcceptedCommand.Value == 0 || attempt.Generation.Value == 0
            || attempt.TargetId.Value == 0 || attempt.DefinitionKey is null || attempt.UnitCapacityCost == Quantity.Zero
            || attempt.ReservedQuantity == Quantity.Zero || !AttemptIdWasAllocated(attempt.CapacityReservationId.Value, reservationIds)
            || attempt.StartedAt > _agenda.CurrentTime || attempt.DueAt <= _agenda.CurrentTime
            || attempt.StartStorage == Quantity.Zero || attempt.StartStorage.Units % attempt.UnitCapacityCost.Units != 0
            || attempt.ReservedQuantity.Units > attempt.StartStorage.Units / attempt.UnitCapacityCost.Units)
            return "A cargo attempt has invalid identities, start size, reservation quantity or time.";

        if (!targets.TryGetValue(attempt.TargetId, out CargoLiveObject? metadata)
            || metadata.PolicyKey != _policy.Key || metadata.CreatedAt > attempt.StartedAt || metadata.ExpiresAt <= _agenda.CurrentTime
            || _cargo.CaptureObjectView(attempt.TargetId) is not { } target
            || target.Position != attempt.TargetPosition || target.DefinitionKey != attempt.DefinitionKey
            || target.UnitCapacityCost != attempt.UnitCapacityCost || target.OccupiedStorage > attempt.StartStorage
            || _lifecycle.GetCargoObject(attempt.TargetId) is not { } physical
            || _lifecycle.Inventories.Get(physical.InventoryId) is not { } targetInventory
            || attempt.StartStorage > targetInventory.Capacity
            || target.HoldingKind == PhysicalHoldingKind.Discrete
                && (attempt.ReservedQuantity.Units != 1 || attempt.StartStorage != target.UnitCapacityCost))
            return "A cargo attempt's exact live target, definition or start-time storage is inconsistent.";

        if (_lifecycle.Entities.GetEntityId(actor.Id) is not { } entity
            || _lifecycle.Inventories.Get(actor.CargoInventoryId) is not { } inventory
            || inventory.Custody != new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(entity), actor.PrincipalId)
            || _lifecycle.GetOperationalShipDefinition(actor.DesignId) is not { } definition
            || definition.PickupRangeUnits != attempt.PickupRange || actor.ManeuverCapabilityRevision != attempt.CapabilityRevision
            || geometry.Get(actor.Id) is not { } participant || participant.Revision != attempt.MotionRevision
            || _movement.GetSpatialRevision(actor.Id) != attempt.MotionRevision
            || !InRange(geometry, actor.Id, attempt.TargetPosition, attempt.PickupRange))
            return "A cargo attempt's actor custody, exact motion, pickup capability or current range is invalid.";

        ValidateReservation(attempt);
        SimulationTime expectedDue = attempt.StartedAt.Add(CargoDispositionArithmetic.CollectionDuration(attempt.StartStorage,
            _policy.MinimumCollectionDuration, _policy.MaximumCollectionDuration, _policy.CollectionStorageCap));
        if (attempt.DueAt != expectedDue || attempt.CompletionKey.Timestamp != attempt.DueAt
            || attempt.CompletionKey.Phase != EventPhase.StateUpdate || !usedWork.Add(attempt.CompletionKey)
            || _agenda.CheckCancellation(attempt.CompletionKey, attempt.Generation, CompletionPayload(attempt)) != AgendaCancellationCheck.Matches)
            return "A cargo attempt's full duration or exact completion envelope is inconsistent.";
        SimulationTime? expectedExit = ForecastExit(geometry, actor.Id, attempt.TargetPosition, attempt.PickupRange, expectedDue);
        if (expectedExit != attempt.ExitKey?.Timestamp)
            return "A cargo attempt's range-exit forecast is inconsistent with committed motion.";
        if (attempt.ExitKey is { } exit
            && (exit.Timestamp <= _agenda.CurrentTime || exit.Timestamp > attempt.DueAt || exit.Phase != EventPhase.StateUpdate
                || !usedWork.Add(exit) || _agenda.CheckCancellation(exit, attempt.Generation, ExitPayload(attempt)) != AgendaCancellationCheck.Matches))
            return "A cargo attempt's exact future range-exit envelope is missing or changed.";
        return null;
    }

    /// <summary>Preserves allocated history when a monotonic identifier sequence is exhausted.</summary>
    private static bool AttemptIdWasAllocated(ulong value, IdSequenceCheckpoint sequence) =>
        value != 0 && (sequence.NextValue is not { } next || value < next);

    private static CheckpointResult<CargoCollectionAttemptOwner> AttemptCheckpointRejected(string message) =>
        CheckpointResult<CargoCollectionAttemptOwner>.Rejected(new("$.checkpoint.cargoCollectionAttempts", message));
}
