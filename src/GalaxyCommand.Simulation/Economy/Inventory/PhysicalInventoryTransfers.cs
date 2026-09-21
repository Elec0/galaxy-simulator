using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Exactly one fungible quantity or discrete instance to transfer.</summary>
public abstract record PhysicalTransferSubject
{
    private PhysicalTransferSubject()
    {
    }

    /// <summary>Identifies a positive fungible quantity to transfer.</summary>
    public sealed record Fungible : PhysicalTransferSubject
    {
        /// <summary>Creates one fungible transfer subject.</summary>
        public Fungible(QualifiedContentKey definitionKey, Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(definitionKey);
            if (quantity == Quantity.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "A fungible transfer quantity must be positive.");
            }

            DefinitionKey = definitionKey;
            Quantity = quantity;
        }

        public QualifiedContentKey DefinitionKey { get; }

        public Quantity Quantity { get; }
    }

    /// <summary>Identifies one exact discrete instance to transfer.</summary>
    public sealed record Discrete : PhysicalTransferSubject
    {
        /// <summary>Creates one discrete transfer subject.</summary>
        public Discrete(ItemInstanceId instanceId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(instanceId.Value);
            InstanceId = instanceId;
        }

        public ItemInstanceId InstanceId { get; }
    }
}

/// <summary>
/// Complete input for one atomic cross-inventory physical transfer.
/// Reservation identities are optional, but both use the supplied owner when
/// present.
/// </summary>
public sealed record PhysicalTransferRequest
{
    /// <summary>Creates one transfer request without mutating either inventory.</summary>
    public PhysicalTransferRequest(
        InventoryId sourceInventoryId,
        InventoryId destinationInventoryId,
        PhysicalDefinition definition,
        PhysicalTransferSubject subject,
        ReservationId? sourceReservationId = null,
        ReservationId? destinationCapacityReservationId = null,
        ReservationOwner? reservationOwner = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(subject);
        SourceInventoryId = sourceInventoryId;
        DestinationInventoryId = destinationInventoryId;
        Definition = definition;
        Subject = subject;
        SourceReservationId = sourceReservationId;
        DestinationCapacityReservationId = destinationCapacityReservationId;
        ReservationOwner = reservationOwner;
    }

    public InventoryId SourceInventoryId { get; }

    public InventoryId DestinationInventoryId { get; }

    public PhysicalDefinition Definition { get; }

    public PhysicalTransferSubject Subject { get; }

    public ReservationId? SourceReservationId { get; }

    public ReservationId? DestinationCapacityReservationId { get; }

    public ReservationOwner? ReservationOwner { get; }
}

/// <summary>Stable reasons that an atomic physical transfer was rejected.</summary>
public enum InventoryTransferRejectionReason
{
    SameInventory,
    MissingSourceInventory,
    MissingDestinationInventory,
    MissingCustody,
    HoldingKindMismatch,
    DefinitionMismatch,
    InsufficientAvailableQuantity,
    MissingItemInstance,
    ItemAlreadyReserved,
    DestinationItemConflict,
    SourceReservationMismatch,
    DestinationReservationMismatch,
    InvalidReservationOwner,
    InsufficientCapacity,
    CapacityOverflow,
}

/// <summary>Typed outcome of one atomic physical transfer request.</summary>
public sealed record InventoryTransferResult
{
    private InventoryTransferResult(
        bool isAccepted,
        InventoryTransferRejectionReason? rejectionReason)
    {
        IsAccepted = isAccepted;
        RejectionReason = rejectionReason;
    }

    public bool IsAccepted { get; }

    public InventoryTransferRejectionReason? RejectionReason { get; }

    internal static InventoryTransferResult Accepted() => new(true, null);

    internal static InventoryTransferResult Rejected(
        InventoryTransferRejectionReason reason) =>
        new(false, reason);
}

public sealed partial class InventoryRegistry
{
    /// <summary>
    /// Validates both inventories, reservation authority, exact source state,
    /// and destination capacity before applying one indivisible transfer.
    /// </summary>
    public InventoryTransferResult TransferPhysical(PhysicalTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceInventoryId == request.DestinationInventoryId)
        {
            return Rejected(InventoryTransferRejectionReason.SameInventory);
        }

        Inventory? source = Get(request.SourceInventoryId);
        if (source is null)
        {
            return Rejected(InventoryTransferRejectionReason.MissingSourceInventory);
        }

        Inventory? destination = Get(request.DestinationInventoryId);
        if (destination is null)
        {
            return Rejected(InventoryTransferRejectionReason.MissingDestinationInventory);
        }

        if (source.Custody is null || destination.Custody is null)
        {
            return Rejected(InventoryTransferRejectionReason.MissingCustody);
        }

        InventoryTransferRejectionReason? subjectFailure = ValidateSubject(
            source,
            destination,
            request);
        if (subjectFailure is not null)
        {
            return Rejected(subjectFailure.Value);
        }

        InventoryTransferRejectionReason? sourceReservationFailure =
            ValidateSourceReservation(source, request);
        if (sourceReservationFailure is not null)
        {
            return Rejected(sourceReservationFailure.Value);
        }

        if (!TryCalculateCapacityCost(
                request.Definition,
                request.Subject,
                out Quantity capacityCost))
        {
            return Rejected(InventoryTransferRejectionReason.CapacityOverflow);
        }

        InventoryTransferRejectionReason? destinationFailure =
            ValidateDestination(destination, request, capacityCost);
        if (destinationFailure is not null)
        {
            return Rejected(destinationFailure.Value);
        }

        ApplyTransfer(source, destination, request, capacityCost);
        return InventoryTransferResult.Accepted();
    }

    /// <summary>
    /// Proves the requested definition and exact source holding agree, and that
    /// an unreserved request does not consume another workflow's commitment.
    /// </summary>
    private static InventoryTransferRejectionReason? ValidateSubject(
        Inventory source,
        Inventory destination,
        PhysicalTransferRequest request)
    {
        switch (request.Subject)
        {
            case PhysicalTransferSubject.Fungible fungible:
                if (request.Definition.HoldingKind != PhysicalHoldingKind.Fungible)
                {
                    return InventoryTransferRejectionReason.HoldingKindMismatch;
                }

                if (request.Definition.Key != fungible.DefinitionKey)
                {
                    return InventoryTransferRejectionReason.DefinitionMismatch;
                }

                if (request.SourceReservationId is null &&
                    fungible.Quantity > source.FungibleAvailable(fungible.DefinitionKey))
                {
                    return InventoryTransferRejectionReason.InsufficientAvailableQuantity;
                }

                break;
            case PhysicalTransferSubject.Discrete discrete:
                if (request.Definition.HoldingKind != PhysicalHoldingKind.Discrete)
                {
                    return InventoryTransferRejectionReason.HoldingKindMismatch;
                }

                DiscreteItemInstance? instance = source.GetDiscrete(discrete.InstanceId);
                if (instance is null)
                {
                    return InventoryTransferRejectionReason.MissingItemInstance;
                }

                if (instance.DefinitionKey != request.Definition.Key)
                {
                    return InventoryTransferRejectionReason.DefinitionMismatch;
                }

                if (destination.GetDiscrete(discrete.InstanceId) is not null)
                {
                    return InventoryTransferRejectionReason.DestinationItemConflict;
                }

                if (request.SourceReservationId is null &&
                    source.IsDiscreteReserved(discrete.InstanceId))
                {
                    return InventoryTransferRejectionReason.ItemAlreadyReserved;
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Validates that an optional source reservation authorizes exactly the
    /// requested subject and no other quantity or instance.
    /// </summary>
    private static InventoryTransferRejectionReason? ValidateSourceReservation(
        Inventory source,
        PhysicalTransferRequest request)
    {
        if (request.SourceReservationId is not { } reservationId)
        {
            return null;
        }

        if (request.ReservationOwner is null ||
            !IsValidReservationOwner(request.ReservationOwner))
        {
            return InventoryTransferRejectionReason.InvalidReservationOwner;
        }

        PhysicalReservation? reservation = source.GetPhysicalReservation(reservationId);
        if (reservation is null || reservation.Owner != request.ReservationOwner)
        {
            return InventoryTransferRejectionReason.SourceReservationMismatch;
        }

        bool matches = (request.Subject, reservation.Subject) switch
        {
            (PhysicalTransferSubject.Fungible transfer,
                PhysicalReservationSubject.Fungible reserved) =>
                transfer.DefinitionKey == reserved.DefinitionKey &&
                transfer.Quantity == reserved.Quantity,
            (PhysicalTransferSubject.Discrete transfer,
                PhysicalReservationSubject.Discrete reserved) =>
                transfer.InstanceId == reserved.InstanceId,
            _ => false,
        };
        return matches
            ? null
            : InventoryTransferRejectionReason.SourceReservationMismatch;
    }

    /// <summary>
    /// Validates either ordinary remaining capacity or an exact authorized
    /// incoming-capacity reservation before commit.
    /// </summary>
    private static InventoryTransferRejectionReason? ValidateDestination(
        Inventory destination,
        PhysicalTransferRequest request,
        Quantity capacityCost)
    {
        if (request.DestinationCapacityReservationId is not { } reservationId)
        {
            return capacityCost > destination.RemainingCapacity
                ? InventoryTransferRejectionReason.InsufficientCapacity
                : null;
        }

        if (request.ReservationOwner is null ||
            !IsValidReservationOwner(request.ReservationOwner))
        {
            return InventoryTransferRejectionReason.InvalidReservationOwner;
        }

        PhysicalReservation? reservation =
            destination.GetPhysicalReservation(reservationId);
        return reservation?.Owner == request.ReservationOwner &&
            reservation.Subject is PhysicalReservationSubject.IncomingCapacity incoming &&
            incoming.Quantity == capacityCost
                ? null
                : InventoryTransferRejectionReason.DestinationReservationMismatch;
    }

    /// <summary>
    /// Applies only prevalidated operations. Reservation release precedes
    /// holding movement so derived availability and capacity remain consistent.
    /// </summary>
    private static void ApplyTransfer(
        Inventory source,
        Inventory destination,
        PhysicalTransferRequest request,
        Quantity capacityCost)
    {
        if (request.SourceReservationId is { } sourceReservationId)
        {
            _ = source.ReleasePhysicalReservation(
                sourceReservationId,
                request.ReservationOwner!);
        }

        if (request.DestinationCapacityReservationId is { } destinationReservationId)
        {
            _ = destination.ReleasePhysicalReservation(
                destinationReservationId,
                request.ReservationOwner!);
        }

        switch (request.Subject)
        {
            case PhysicalTransferSubject.Fungible fungible:
                source.ApplyRemoveFungible(
                    fungible.DefinitionKey,
                    fungible.Quantity,
                    capacityCost);
                destination.ApplyStoreFungible(
                    fungible.DefinitionKey,
                    fungible.Quantity,
                    capacityCost);
                break;
            case PhysicalTransferSubject.Discrete discrete:
                source.ApplyRemoveDiscrete(discrete.InstanceId, capacityCost);
                destination.ApplyStoreDiscrete(
                    new DiscreteItemInstance(
                        discrete.InstanceId,
                        request.Definition.Key),
                    capacityCost);
                break;
        }
    }

    /// <summary>
    /// Computes the exact transfer capacity without permitting integer
    /// overflow to escape as partial transfer behavior.
    /// </summary>
    private static bool TryCalculateCapacityCost(
        PhysicalDefinition definition,
        PhysicalTransferSubject subject,
        out Quantity capacityCost)
    {
        ulong units = subject is PhysicalTransferSubject.Fungible fungible
            ? fungible.Quantity.Units
            : 1;
        try
        {
            capacityCost = new Quantity(checked(definition.CapacityCost.Units * units));
            return true;
        }
        catch (OverflowException)
        {
            capacityCost = Quantity.Zero;
            return false;
        }
    }

    /// <summary>
    /// Rejects default typed workflow identities that record construction can
    /// otherwise represent.
    /// </summary>
    private static bool IsValidReservationOwner(ReservationOwner owner) =>
        owner switch
        {
            ReservationOwner.TransportJob value => value.JobId.Value != 0,
            ReservationOwner.ProductionJob value => value.JobId.Value != 0,
            ReservationOwner.ConstructionOrder value => value.OrderId.Value != 0,
            _ => false,
        };

    private static InventoryTransferResult Rejected(
        InventoryTransferRejectionReason reason) =>
        InventoryTransferResult.Rejected(reason);
}
