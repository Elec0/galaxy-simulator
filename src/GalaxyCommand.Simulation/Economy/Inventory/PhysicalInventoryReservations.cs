using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Exactly one physical holding or incoming-capacity commitment reserved for a
/// workflow owner.
/// </summary>
public abstract record PhysicalReservationSubject
{
    private PhysicalReservationSubject()
    {
    }

    /// <summary>Reserves a positive quantity of one fungible definition.</summary>
    public sealed record Fungible : PhysicalReservationSubject
    {
        /// <summary>Creates one fungible reservation subject.</summary>
        public Fungible(QualifiedContentKey definitionKey, Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(definitionKey);
            if (quantity == Quantity.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "A fungible reservation quantity must be positive.");
            }

            DefinitionKey = definitionKey;
            Quantity = quantity;
        }

        public QualifiedContentKey DefinitionKey { get; }

        public Quantity Quantity { get; }
    }

    /// <summary>Reserves one exact discrete item instance.</summary>
    public sealed record Discrete : PhysicalReservationSubject
    {
        /// <summary>Creates one discrete-instance reservation subject.</summary>
        public Discrete(ItemInstanceId instanceId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(instanceId.Value);
            InstanceId = instanceId;
        }

        public ItemInstanceId InstanceId { get; }
    }

    /// <summary>Reserves positive capacity for one future incoming commit.</summary>
    public sealed record IncomingCapacity : PhysicalReservationSubject
    {
        /// <summary>Creates one incoming-capacity reservation subject.</summary>
        public IncomingCapacity(Quantity quantity)
        {
            if (quantity == Quantity.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "An incoming-capacity reservation must be positive.");
            }

            Quantity = quantity;
        }

        public Quantity Quantity { get; }
    }
}

/// <summary>
/// One live generalized reservation bound to its inventory and workflow owner.
/// </summary>
public sealed record PhysicalReservation(
    ReservationId Id,
    InventoryId InventoryId,
    PhysicalReservationSubject Subject,
    ReservationOwner Owner);

/// <summary>Stable rejection reasons for generalized reservation operations.</summary>
public enum InventoryReservationRejectionReason
{
    MissingCustody,
    InvalidReservationId,
    InvalidOwner,
    DuplicateReservationId,
    InsufficientAvailableQuantity,
    MissingItemInstance,
    ItemAlreadyReserved,
    InsufficientCapacity,
    UnknownReservation,
    OwnerMismatch,
}

/// <summary>Typed result for reserving or releasing physical inventory state.</summary>
public sealed record InventoryReservationResult
{
    private InventoryReservationResult(
        PhysicalReservation? reservation,
        InventoryReservationRejectionReason? rejectionReason)
    {
        Reservation = reservation;
        RejectionReason = rejectionReason;
    }

    public bool IsAccepted => Reservation is not null;

    public PhysicalReservation? Reservation { get; }

    public InventoryReservationRejectionReason? RejectionReason { get; }

    internal static InventoryReservationResult Accepted(
        PhysicalReservation reservation) =>
        new(reservation, null);

    internal static InventoryReservationResult Rejected(
        InventoryReservationRejectionReason reason) =>
        new(null, reason);
}

public sealed partial class Inventory
{
    private readonly Dictionary<ReservationId, PhysicalReservation> _physicalReservations = [];
    private readonly Dictionary<QualifiedContentKey, Quantity> _fungibleReserved = [];
    private readonly HashSet<ItemInstanceId> _reservedDiscreteItems = [];

    internal IEnumerable<ReservationId> PhysicalReservationIds =>
        _physicalReservations.Keys;

    /// <summary>Returns the reserved fungible quantity for an exact definition.</summary>
    public Quantity FungibleReserved(QualifiedContentKey definitionKey)
    {
        ArgumentNullException.ThrowIfNull(definitionKey);
        return _fungibleReserved.GetValueOrDefault(definitionKey, Quantity.Zero);
    }

    /// <summary>Returns the unreserved fungible quantity for an exact definition.</summary>
    public Quantity FungibleAvailable(QualifiedContentKey definitionKey) =>
        FungibleStored(definitionKey).Subtract(FungibleReserved(definitionKey));

    /// <summary>Returns whether an exact held instance has a live reservation.</summary>
    public bool IsDiscreteReserved(ItemInstanceId instanceId) =>
        _reservedDiscreteItems.Contains(instanceId);

    /// <summary>Returns one generalized reservation by its stable identity.</summary>
    public PhysicalReservation? GetPhysicalReservation(ReservationId reservationId) =>
        _physicalReservations.GetValueOrDefault(reservationId);

    /// <summary>
    /// Reserves one fungible quantity, exact instance, or incoming capacity
    /// after validating the complete request without mutation.
    /// </summary>
    public InventoryReservationResult ReservePhysical(
        ReservationId reservationId,
        PhysicalReservationSubject subject,
        ReservationOwner owner)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(owner);
        if (Custody is null)
        {
            return Rejected(InventoryReservationRejectionReason.MissingCustody);
        }

        if (reservationId.Value == 0)
        {
            return Rejected(
                InventoryReservationRejectionReason.InvalidReservationId);
        }

        if (!IsValidOwner(owner))
        {
            return Rejected(InventoryReservationRejectionReason.InvalidOwner);
        }

        if (_physicalReservations.ContainsKey(reservationId) ||
            _reservations.ContainsKey(reservationId))
        {
            return Rejected(
                InventoryReservationRejectionReason.DuplicateReservationId);
        }

        InventoryReservationRejectionReason? rejection = subject switch
        {
            PhysicalReservationSubject.Fungible fungible
                when fungible.Quantity > FungibleAvailable(fungible.DefinitionKey) =>
                InventoryReservationRejectionReason.InsufficientAvailableQuantity,
            PhysicalReservationSubject.Discrete discrete
                when GetDiscrete(discrete.InstanceId) is null =>
                InventoryReservationRejectionReason.MissingItemInstance,
            PhysicalReservationSubject.Discrete discrete
                when IsDiscreteReserved(discrete.InstanceId) =>
                InventoryReservationRejectionReason.ItemAlreadyReserved,
            PhysicalReservationSubject.IncomingCapacity incoming
                when incoming.Quantity > RemainingCapacity =>
                InventoryReservationRejectionReason.InsufficientCapacity,
            _ => null,
        };
        if (rejection is not null)
        {
            return Rejected(rejection.Value);
        }

        var reservation = new PhysicalReservation(
            reservationId,
            Id,
            subject,
            owner);
        _physicalReservations.Add(reservationId, reservation);
        ApplyReservation(subject);
        return InventoryReservationResult.Accepted(reservation);
    }

    /// <summary>
    /// Releases one generalized reservation only for its exact workflow owner.
    /// Rejection leaves the commitment unchanged.
    /// </summary>
    public InventoryReservationResult ReleasePhysicalReservation(
        ReservationId reservationId,
        ReservationOwner expectedOwner)
    {
        ArgumentNullException.ThrowIfNull(expectedOwner);
        if (!IsValidOwner(expectedOwner))
        {
            return Rejected(InventoryReservationRejectionReason.InvalidOwner);
        }

        if (!_physicalReservations.TryGetValue(
                reservationId,
                out PhysicalReservation? reservation))
        {
            return Rejected(InventoryReservationRejectionReason.UnknownReservation);
        }

        if (reservation.Owner != expectedOwner)
        {
            return Rejected(InventoryReservationRejectionReason.OwnerMismatch);
        }

        _physicalReservations.Remove(reservationId);
        ReleaseReservation(reservation.Subject);
        return InventoryReservationResult.Accepted(reservation);
    }

    /// <summary>
    /// Applies the one derived availability or capacity commitment represented
    /// by an already validated reservation subject.
    /// </summary>
    private void ApplyReservation(PhysicalReservationSubject subject)
    {
        switch (subject)
        {
            case PhysicalReservationSubject.Fungible fungible:
                _fungibleReserved[fungible.DefinitionKey] =
                    FungibleReserved(fungible.DefinitionKey).Add(fungible.Quantity);
                break;
            case PhysicalReservationSubject.Discrete discrete:
                _reservedDiscreteItems.Add(discrete.InstanceId);
                break;
            case PhysicalReservationSubject.IncomingCapacity incoming:
                ReservedCapacity = ReservedCapacity.Add(incoming.Quantity);
                break;
        }
    }

    /// <summary>
    /// Reverses the derived commitment for one reservation that has already
    /// been removed from the authoritative reservation registry.
    /// </summary>
    private void ReleaseReservation(PhysicalReservationSubject subject)
    {
        switch (subject)
        {
            case PhysicalReservationSubject.Fungible fungible:
                Quantity remaining = FungibleReserved(fungible.DefinitionKey)
                    .Subtract(fungible.Quantity);
                if (remaining == Quantity.Zero)
                {
                    _fungibleReserved.Remove(fungible.DefinitionKey);
                }
                else
                {
                    _fungibleReserved[fungible.DefinitionKey] = remaining;
                }

                break;
            case PhysicalReservationSubject.Discrete discrete:
                _reservedDiscreteItems.Remove(discrete.InstanceId);
                break;
            case PhysicalReservationSubject.IncomingCapacity incoming:
                ReservedCapacity = ReservedCapacity.Subtract(incoming.Quantity);
                break;
        }
    }

    private static InventoryReservationResult Rejected(
        InventoryReservationRejectionReason reason) =>
        InventoryReservationResult.Rejected(reason);
}
