namespace GalaxyCommand.Simulation;

/// <summary>Exact read snapshots for one collection transfer, including the original discrete reference.</summary>
internal sealed class PreparedCollectedCargoTransfer
{
    private bool _applied;

    /// <summary>Captures both inventories before mutation and retains the exact original discrete instance.</summary>
    internal PreparedCollectedCargoTransfer(InventoryRegistry owner, Inventory source, Inventory destination,
        PhysicalTransferRequest request, Quantity capacityCost)
    {
        Owner = owner;
        Source = source;
        Destination = destination;
        Request = request;
        CapacityCost = capacityCost;
        ExpectedSourceState = source.CaptureCheckpoint();
        ExpectedDestinationState = destination.CaptureCheckpoint();
        OriginalInstance = request.Subject is PhysicalTransferSubject.Discrete item ? source.GetDiscrete(item.InstanceId) : null;
    }

    internal InventoryRegistry Owner { get; }
    internal Inventory Source { get; }
    internal Inventory Destination { get; }
    internal PhysicalTransferRequest Request { get; }
    internal Quantity CapacityCost { get; }
    internal InventoryCheckpoint ExpectedSourceState { get; }
    internal InventoryCheckpoint ExpectedDestinationState { get; }
    internal DiscreteItemInstance? OriginalInstance { get; }
    internal bool WasApplied => _applied;
    /// <summary>Rejects a previously applied plan or any changed holding, commitment, or registry link.</summary>
    internal bool CanApply() => !_applied && Owner.CanApplyCollectedCargoTransfer(this);

    /// <summary>Applies once after exact snapshot revalidation; a rejected or duplicate plan changes no resources.</summary>
    internal bool Apply()
    {
        if (_applied || !Owner.TryApplyCollectedCargoTransfer(this)) return false;
        _applied = true;
        return true;
    }
}

public sealed partial class InventoryRegistry
{
    /// <summary>
    /// Settles an exact collection attempt's incoming reservation, consuming only
    /// actual transfer cost and releasing the unused remainder in the same owner commit.
    /// The cargo runtime supplies the resolved actor/entity and validates timer authority.
    /// Ordinary workflows retain their exact-reservation transfer contract.
    /// </summary>
    internal InventoryTransferResult TransferCollectedCargo(PhysicalTransferRequest request,
        GameSessionShip collector, EntityId collectorEntityId)
    {
        if (!TryPrepareCollectedCargoTransfer(request, collector, collectorEntityId, out var prepared, out var reason))
            return InventoryTransferResult.Rejected(reason!.Value);
        return prepared!.Apply() ? InventoryTransferResult.Accepted()
            : InventoryTransferResult.Rejected(InventoryTransferRejectionReason.DestinationReservationMismatch);
    }

    /// <summary>Preflights the authorized transfer and oversized reservation without releasing capacity or moving holdings.</summary>
    internal bool TryPrepareCollectedCargoTransfer(PhysicalTransferRequest request, GameSessionShip collector,
        EntityId collectorEntityId, out PreparedCollectedCargoTransfer? prepared, out InventoryTransferRejectionReason? reason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(collector);
        prepared = null;
        reason = InventoryTransferRejectionReason.InvalidCollectionAuthority;
        Inventory? source = Get(request.SourceInventoryId);
        Inventory? destination = Get(request.DestinationInventoryId);
        if (source?.Custody is not InventoryCustody.Unclaimed
            || destination?.Custody is not InventoryCustody.PrincipalControlled custody
            || custody.PhysicalOwner is not InventoryOwnerReference.SessionEntity entity
            || entity.EntityId != collectorEntityId || custody.ControllingPrincipalId != collector.PrincipalId
            || request.DestinationInventoryId != collector.CargoInventoryId
            || request.ReservationOwner is not ReservationOwner.CargoCollection owner || owner.ShipId != collector.Id
            || request.SourceReservationId is not null || request.DestinationCapacityReservationId is null)
            return false;

        reason = ValidateSubject(source, destination, request) ?? ValidateSourceReservation(source, request);
        if (reason is not null) return false;
        if (!TryCalculateCapacityCost(request.Definition, request.Subject, out Quantity capacityCost))
        {
            reason = InventoryTransferRejectionReason.CapacityOverflow;
            return false;
        }
        reason = ValidateDestination(destination, request, capacityCost, releaseUnusedIncomingCapacity: true);
        if (reason is not null) return false;
        prepared = new(this, source, destination, request, capacityCost);
        return true;
    }

    /// <summary>Requires exact source, destination and reservation state, retaining the original discrete item reference.</summary>
    internal bool CanApplyCollectedCargoTransfer(PreparedCollectedCargoTransfer prepared) => ReferenceEquals(prepared.Owner, this)
        && !prepared.WasApplied && ReferenceEquals(Get(prepared.Source.Id), prepared.Source)
        && ReferenceEquals(Get(prepared.Destination.Id), prepared.Destination)
        && CollectionInventoryStateMatches(prepared.Source.CaptureCheckpoint(), prepared.ExpectedSourceState)
        && CollectionInventoryStateMatches(prepared.Destination.CaptureCheckpoint(), prepared.ExpectedDestinationState)
        && (prepared.Request.Subject is not PhysicalTransferSubject.Discrete item
            || ReferenceEquals(prepared.Source.GetDiscrete(item.InstanceId), prepared.OriginalInstance));

    /// <summary>Applies the shared indivisible transfer path only after all expected states still match.</summary>
    internal bool TryApplyCollectedCargoTransfer(PreparedCollectedCargoTransfer prepared)
    {
        if (!CanApplyCollectedCargoTransfer(prepared)) return false;
        ApplyTransfer(prepared.Source, prepared.Destination, prepared.Request, prepared.CapacityCost);
        return true;
    }

    /// <summary>Compares canonical owner state rather than container identity or derived totals.</summary>
    private static bool CollectionInventoryStateMatches(InventoryCheckpoint current, InventoryCheckpoint expected) =>
        current.Id == expected.Id && current.Custody == expected.Custody && current.Capacity == expected.Capacity
        && current.StoredMaterials.SequenceEqual(expected.StoredMaterials)
        && current.FungibleHoldings.SequenceEqual(expected.FungibleHoldings) && current.DiscreteItems.SequenceEqual(expected.DiscreteItems)
        && current.Reservations.SequenceEqual(expected.Reservations) && current.CapacityReservations.SequenceEqual(expected.CapacityReservations)
        && current.PhysicalReservations.SequenceEqual(expected.PhysicalReservations);
}
