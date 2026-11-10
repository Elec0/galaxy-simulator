namespace GalaxyCommand.Simulation;

internal enum InventoryIncomingReservationKind { LegacyCapacity, PhysicalCapacity }

internal sealed record InventoryIncomingAdmissionCheckpoint(InventoryIncomingReservationKind Kind,
    ulong ReservationValue, ulong AdmissionSequence);

/// <summary>Immutable oldest-first view retaining the exact live incoming reservation reference.</summary>
internal abstract record InventoryIncomingCommitment(ulong AdmissionSequence)
{
    internal abstract InventoryIncomingReservationKind Kind { get; }
    internal abstract ulong ReservationValue { get; }
    internal abstract Quantity Quantity { get; }

    internal sealed record Legacy(ulong AdmissionSequence, CapacityReservation Reservation) : InventoryIncomingCommitment(AdmissionSequence)
    {
        internal override InventoryIncomingReservationKind Kind => InventoryIncomingReservationKind.LegacyCapacity;
        internal override ulong ReservationValue => Reservation.Id.Value;
        internal override Quantity Quantity => Reservation.Quantity;
    }

    internal sealed record Physical(ulong AdmissionSequence, PhysicalReservation Reservation) : InventoryIncomingCommitment(AdmissionSequence)
    {
        internal override InventoryIncomingReservationKind Kind => InventoryIncomingReservationKind.PhysicalCapacity;
        internal override ulong ReservationValue => Reservation.Id.Value;
        internal override Quantity Quantity => ((PhysicalReservationSubject.IncomingCapacity)Reservation.Subject).Quantity;
    }
}

public sealed partial class Inventory
{
    private ulong? _nextIncomingAdmissionSequence = 1;
    private readonly Dictionary<CapacityReservationId, ulong> _legacyIncomingAdmissions = [];
    private readonly Dictionary<ReservationId, ulong> _physicalIncomingAdmissions = [];
    private bool _restoringIncomingAdmissions;

    /// <summary>Captures both incoming reservation kinds in their shared committed admission order, retaining exact references.</summary>
    internal IReadOnlyList<InventoryIncomingCommitment> CaptureIncomingCommitments() => Array.AsReadOnly(
        _capacityReservations.Values.Select(reservation => (InventoryIncomingCommitment)new InventoryIncomingCommitment.Legacy(
                _legacyIncomingAdmissions[reservation.Id], reservation))
            .Concat(_physicalReservations.Values.Where(reservation => reservation.Subject is PhysicalReservationSubject.IncomingCapacity)
                .Select(reservation => new InventoryIncomingCommitment.Physical(_physicalIncomingAdmissions[reservation.Id], reservation)))
            .OrderBy(commitment => commitment.AdmissionSequence).ThenBy(commitment => commitment.Kind)
            .ThenBy(commitment => commitment.ReservationValue).ToArray());

    internal bool CanAdmitIncomingCapacity => _restoringIncomingAdmissions || _nextIncomingAdmissionSequence is not null;

    private IEnumerable<InventoryIncomingAdmissionCheckpoint> CaptureIncomingAdmissionCheckpoints() =>
        CaptureIncomingCommitments().Select(commitment => new InventoryIncomingAdmissionCheckpoint(
            commitment.Kind, commitment.ReservationValue, commitment.AdmissionSequence));

    /// <summary>Consumes an incoming age only after the owning reservation has passed all rejection checks.</summary>
    private void RecordIncomingAdmission(CapacityReservationId reservation)
    {
        if (_restoringIncomingAdmissions) return;
        _legacyIncomingAdmissions.Add(reservation, TakeIncomingAdmissionSequence());
    }

    /// <summary>Records only incoming physical claims; outgoing reservations do not consume capacity age.</summary>
    private void RecordIncomingAdmission(PhysicalReservation reservation)
    {
        if (_restoringIncomingAdmissions || reservation.Subject is not PhysicalReservationSubject.IncomingCapacity) return;
        _physicalIncomingAdmissions.Add(reservation.Id, TakeIncomingAdmissionSequence());
    }

    /// <summary>Advances exact highwater, retaining exhaustion even after every live claim is released.</summary>
    private ulong TakeIncomingAdmissionSequence()
    {
        ulong value = _nextIncomingAdmissionSequence ?? throw new InvalidOperationException("Inventory incoming admission sequence exhausted.");
        _nextIncomingAdmissionSequence = value == ulong.MaxValue ? null : value + 1;
        return value;
    }

    /// <summary>Restores a bijection onto all live incoming claims without generating ages from unrelated identity domains.</summary>
    private CheckpointValidationFailure? RestoreIncomingAdmissionState(InventoryCheckpoint checkpoint, string path)
    {
        if (checkpoint.NextIncomingAdmissionSequence == 0)
            return new($"{path}.nextIncomingAdmissionSequence", "Incoming admission highwater must be positive or exhausted.");
        var expectedLegacy = _capacityReservations.Keys.ToHashSet();
        var expectedPhysical = _physicalReservations.Values.Where(reservation => reservation.Subject is PhysicalReservationSubject.IncomingCapacity)
            .Select(reservation => reservation.Id).ToHashSet();
        var sequences = new HashSet<ulong>();
        var legacy = new Dictionary<CapacityReservationId, ulong>();
        var physical = new Dictionary<ReservationId, ulong>();
        foreach (InventoryIncomingAdmissionCheckpoint? entry in checkpoint.IncomingAdmissions)
        {
            if (entry is null || entry.ReservationValue == 0 || entry.AdmissionSequence == 0
                || !sequences.Add(entry.AdmissionSequence)
                || checkpoint.NextIncomingAdmissionSequence is { } next && entry.AdmissionSequence >= next)
                return new($"{path}.incomingAdmissions", "Incoming admission identities or sequence highwater are invalid.");
            switch (entry.Kind)
            {
                case InventoryIncomingReservationKind.LegacyCapacity when expectedLegacy.Remove(new(entry.ReservationValue)):
                    legacy.Add(new(entry.ReservationValue), entry.AdmissionSequence);
                    break;
                case InventoryIncomingReservationKind.PhysicalCapacity when expectedPhysical.Remove(new(entry.ReservationValue)):
                    physical.Add(new(entry.ReservationValue), entry.AdmissionSequence);
                    break;
                default:
                    return new($"{path}.incomingAdmissions", "Incoming ages must uniquely reference live incoming reservation kinds.");
            }
        }
        if (expectedLegacy.Count != 0 || expectedPhysical.Count != 0)
            return new($"{path}.incomingAdmissions", "Every incoming commitment requires an explicit saved admission age.");
        foreach ((CapacityReservationId id, ulong sequence) in legacy) _legacyIncomingAdmissions.Add(id, sequence);
        foreach ((ReservationId id, ulong sequence) in physical) _physicalIncomingAdmissions.Add(id, sequence);
        _nextIncomingAdmissionSequence = checkpoint.NextIncomingAdmissionSequence;
        return null;
    }
}
