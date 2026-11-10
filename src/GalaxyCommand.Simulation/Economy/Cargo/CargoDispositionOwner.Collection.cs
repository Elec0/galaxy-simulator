using System.Collections.Frozen;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record CargoCollectionSettlementRequest(GameSessionShip Collector, EntityId CollectorEntityId,
    ShipOrderId OrderId, CommandSequence AcceptedCommand, EventGeneration AttemptGeneration, EntityId TargetId,
    Quantity OriginallyReservedQuantity, ReservationId CapacityReservationId, EventKey Trigger, IReadOnlySet<EventKey> DueExpiryKeys);
internal enum CargoCollectionSettlementFailure
{
    InvalidInput, InvalidCollector, TargetUnavailable, ReservationMismatch, AgendaMismatch,
    StalePreparation, FactCapacityExhausted, NumericExhaustion,
}
internal sealed record CargoCollectionSettlementOutcome(QualifiedContentKey DefinitionKey, Quantity Quantity,
    ItemInstanceId? InstanceId, bool Retired);
internal sealed record CargoCollectionSettlementPreparation(PreparedCargoCollectionSettlement? Prepared = null,
    CargoCollectionSettlementFailure? Failure = null);
internal sealed record CargoCollectionSettlementResult(bool WasApplied, CargoCollectionSettlementOutcome? Outcome = null,
    CargoCollectionSettlementFailure? Failure = null);
internal sealed record PreparedCargoCollectionSettlement(CargoDispositionOwner Owner, CargoCollectionSettlementRequest Request,
    CargoLiveObject Target, CargoObjectSnapshot TargetObject, PreparedCollectedCargoTransfer Transfer,
    PreparedCollectedCargoRetirement? Retirement, PreparedCargoDebrisRemoval? FieldRemoval,
    CargoCollectionSettlementOutcome Outcome, bool ExpiryAlreadyDue);
internal sealed record CargoCollectedFact(ShipId ShipId, EntityId CollectorEntityId, ShipOrderId OrderId,
    CommandSequence AcceptedCommand, EventGeneration AttemptGeneration, EntityId TargetId,
    CargoCollectionSettlementOutcome Outcome) : GameFact;

internal sealed partial class CargoDispositionOwner
{
    internal CargoLiveObject? GetLiveObject(EntityId entityId) => _objects.GetValueOrDefault(entityId);

    /// <summary>
    /// Prepares resource settlement for an already eligible timed attempt. The attempt
    /// owner validates range, order/control and timer identity; this owner validates
    /// exact custody, contents, capacity, lifecycle, field and expiry participants.
    /// Missing targets leave the reservation for the attempt owner's cleanup.
    /// </summary>
    internal CargoCollectionSettlementPreparation PrepareCollection(CargoCollectionSettlementRequest request)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Collector is null || request.CollectorEntityId.Value == 0 || request.OrderId.Value == 0
            || request.AcceptedCommand.Value == 0 || request.TargetId.Value == 0 || request.CapacityReservationId.Value == 0
            || request.OriginallyReservedQuantity == Quantity.Zero || request.DueExpiryKeys is null)
            return new(Failure: CargoCollectionSettlementFailure.InvalidInput);
        request = request with { DueExpiryKeys = request.DueExpiryKeys.ToFrozenSet() };
        if (!IsCollectionCollectorCurrent(request)) return new(Failure: CargoCollectionSettlementFailure.InvalidCollector);
        if (!IsCollectionTriggerCurrent(request)) return new(Failure: CargoCollectionSettlementFailure.AgendaMismatch);
        if (!_objects.TryGetValue(request.TargetId, out CargoLiveObject? target)
            || _lifecycle.GetCargoObject(request.TargetId) is not { } physical
            || _lifecycle.Inventories.Get(physical.InventoryId) is not { } source || !Inventory.IsValidCargoObjectInventory(source))
            return new(Failure: CargoCollectionSettlementFailure.TargetUnavailable);
        int expiryState = CollectionExpiryState(target, request);
        if (expiryState < 0) return new(Failure: CargoCollectionSettlementFailure.AgendaMismatch);
        if (!_facts.CanCommit(1)) return new(Failure: CargoCollectionSettlementFailure.FactCapacityExhausted);
        try
        {
            InventoryCheckpoint contents = source.CaptureCheckpoint();
            QualifiedContentKey key = contents.FungibleHoldings.Count == 1 ? contents.FungibleHoldings[0].DefinitionKey : contents.DiscreteItems[0].DefinitionKey;
            PhysicalDefinition? definition = _definitions.Get(key);
            if (definition is null) return new(Failure: CargoCollectionSettlementFailure.InvalidInput);
            ulong current = definition.HoldingKind == PhysicalHoldingKind.Fungible ? source.FungibleStored(key).Units : 1;
            if (definition.HoldingKind == PhysicalHoldingKind.Discrete && request.OriginallyReservedQuantity.Units != 1)
                return new(Failure: CargoCollectionSettlementFailure.InvalidInput);
            if (source.UsedCapacity.Units != checked(current * definition.CapacityCost.Units))
                return new(Failure: CargoCollectionSettlementFailure.InvalidInput);
            var owner = new ReservationOwner.CargoCollection(request.Collector.Id, request.OrderId, request.AttemptGeneration);
            Inventory? destination = _lifecycle.Inventories.Get(request.Collector.CargoInventoryId);
            if (destination is null) return new(Failure: CargoCollectionSettlementFailure.InvalidCollector);
            Quantity reservedCost = new(checked(request.OriginallyReservedQuantity.Units * definition.CapacityCost.Units));
            if (destination.GetPhysicalReservation(request.CapacityReservationId) is not { } reservation
                || reservation.Owner != owner || reservation.Subject is not PhysicalReservationSubject.IncomingCapacity incoming
                || incoming.Quantity != reservedCost)
                return new(Failure: CargoCollectionSettlementFailure.ReservationMismatch);

            Quantity taken = new(Math.Min(current, request.OriginallyReservedQuantity.Units));
            PhysicalTransferSubject subject = definition.HoldingKind == PhysicalHoldingKind.Fungible
                ? new PhysicalTransferSubject.Fungible(key, taken) : new PhysicalTransferSubject.Discrete(contents.DiscreteItems[0].Id);
            var transferRequest = new PhysicalTransferRequest(source.Id, destination.Id, definition, subject,
                destinationCapacityReservationId: request.CapacityReservationId, reservationOwner: owner);
            if (!_lifecycle.Inventories.TryPrepareCollectedCargoTransfer(transferRequest, request.Collector,
                request.CollectorEntityId, out PreparedCollectedCargoTransfer? transfer, out _))
                return new(Failure: CargoCollectionSettlementFailure.ReservationMismatch);
            bool retire = taken.Units == current;
            if (!_facts.CanCommit(retire ? 2 : 1)) return new(Failure: CargoCollectionSettlementFailure.FactCapacityExhausted);
            PreparedCollectedCargoRetirement? retirement = null;
            if (retire && !_lifecycle.TryPrepareCollectedCargoRetirement(target.EntityId, transfer!, out retirement))
                return new(Failure: CargoCollectionSettlementFailure.StalePreparation);
            PreparedCargoDebrisRemoval? field = Fields.PrepareRemoval(target.EntityId);
            if (target.OriginalFieldId is { } handle ? field is null || Fields.Resolve(handle) != field.Field : field is not null)
                return new(Failure: CargoCollectionSettlementFailure.StalePreparation);
            var outcome = new CargoCollectionSettlementOutcome(key, taken,
                subject is PhysicalTransferSubject.Discrete item ? item.InstanceId : null, retire);
            var prepared = new PreparedCargoCollectionSettlement(this, request, target, physical, transfer!, retirement,
                retire ? field : null, outcome, expiryState == 1);
            return CanCommitCollection(prepared) ? new(Prepared: prepared) : new(Failure: CargoCollectionSettlementFailure.StalePreparation);
        }
        catch (OverflowException) { return new(Failure: CargoCollectionSettlementFailure.NumericExhaustion); }
    }

    /// <summary>Revalidates every resource owner before cancellation, transfer, retirement or publication can mutate state.</summary>
    internal bool CanCommitCollection(PreparedCargoCollectionSettlement prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!_isHealthy || !ReferenceEquals(prepared.Owner, this) || !IsCollectionCollectorCurrent(prepared.Request)
            || !IsCollectionTriggerCurrent(prepared.Request) || !_objects.TryGetValue(prepared.Target.EntityId, out CargoLiveObject? current)
            || current != prepared.Target || _lifecycle.GetCargoObject(current.EntityId) != prepared.TargetObject
            || !prepared.Transfer.CanApply() || !_facts.CanCommit(prepared.Outcome.Retired ? 2 : 1)
            || CollectionExpiryState(current, prepared.Request) != (prepared.ExpiryAlreadyDue ? 1 : 0)
            || prepared.Retirement is not null && !_lifecycle.CanCommitCollectedCargoRetirement(prepared.Retirement)
            || prepared.FieldRemoval is not null && !Fields.CanCommit(prepared.FieldRemoval)) return false;
        PreparedCargoDebrisRemoval? membership = Fields.PrepareRemoval(current.EntityId);
        return current.OriginalFieldId is { } handle ? membership is not null && Fields.Resolve(handle) == membership.Field : membership is null;
    }

    /// <summary>
    /// Settles the actual contested remainder and releases all unused incoming
    /// capacity in one guarded commit. Full pickup retires the preplanned physical
    /// object and field membership, then publishes its private diagnostic fact last.
    /// </summary>
    internal CargoCollectionSettlementResult CommitCollection(PreparedCargoCollectionSettlement prepared)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CanCommitCollection(prepared)) return new(false, Failure: CargoCollectionSettlementFailure.StalePreparation);
        try
        {
            if (prepared.Outcome.Retired && !prepared.ExpiryAlreadyDue
                && !_agenda.TryCancelExact(prepared.Target.ExpiryEvent, prepared.Target.Generation, new GameEvent.CargoExpiry(prepared.Target.EntityId)))
                throw new InvalidOperationException("Prepared cargo expiry cancellation changed during collection commit.");
            if (!prepared.Transfer.Apply()) throw new InvalidOperationException("Prepared collection transfer changed during commit.");
            if (prepared.Retirement is not null)
            {
                _lifecycle.ApplyPreparedCollectedCargoRetirement(prepared.Retirement);
                if (prepared.FieldRemoval is not null) Fields.Apply(prepared.FieldRemoval);
                if (!_objects.Remove(prepared.Target.EntityId)) throw new InvalidOperationException("Prepared cargo metadata disappeared during collection.");
            }
            CargoCollectionSettlementRequest request = prepared.Request;
            var proposals = new List<GameFactProposal> { new(new(GameFactCommitCategory.EntityLifecycle, request.Collector.Id.Value, request.OrderId.Value, 0),
                    new CargoCollectedFact(request.Collector.Id, request.CollectorEntityId, request.OrderId, request.AcceptedCommand,
                        request.AttemptGeneration, request.TargetId, prepared.Outcome)) };
            if (prepared.Outcome.Retired)
                proposals.Add(new(new(GameFactCommitCategory.EntityLifecycle, request.TargetId.Value, 0, 0),
                    new CargoObjectDisappearedFact(request.TargetId, prepared.TargetObject.Position)));
            _facts.Commit(_agenda.CurrentTime, new ScheduledEventFactCause(request.Trigger), proposals);
            return new(true, prepared.Outcome);
        }
        catch { _isHealthy = false; throw; }
    }

    /// <summary>Requires the caller's resolved collector to retain its exact live ship and entity mapping.</summary>
    private bool IsCollectionCollectorCurrent(CargoCollectionSettlementRequest request) =>
        _lifecycle.Entities.GetShipId(request.CollectorEntityId) == request.Collector.Id
        && _lifecycle.GetRequiredShip(request.Collector.Id) == request.Collector;

    /// <summary>The attempt owner supplies a popped completion from the current StateUpdate reduction.</summary>
    private bool IsCollectionTriggerCurrent(CargoCollectionSettlementRequest request) =>
        _agenda.CurrentPhase == EventPhase.StateUpdate && request.Trigger.Phase == EventPhase.StateUpdate
        && request.Trigger.Timestamp == _agenda.CurrentTime && request.Trigger.CreationSequence < _agenda.NextCreationSequence;

    /// <summary>
    /// Distinguishes a pending expiry from the exact expiry already captured in the
    /// same wave. A missing future envelope cannot masquerade as an already due one.
    /// </summary>
    private int CollectionExpiryState(CargoLiveObject target, CargoCollectionSettlementRequest request)
    {
        if (target.ExpiresAt < _agenda.CurrentTime) return -1;
        AgendaCancellationCheck check = _agenda.CheckCancellation(target.ExpiryEvent, target.Generation, new GameEvent.CargoExpiry(target.EntityId));
        if (check == AgendaCancellationCheck.Matches) return 0;
        return check == AgendaCancellationCheck.Missing && target.ExpiresAt == _agenda.CurrentTime
            && target.ExpiryEvent.Phase == EventPhase.StateUpdate && request.DueExpiryKeys.Contains(target.ExpiryEvent) ? 1 : -1;
    }
}
