namespace GalaxyCommand.Simulation;

internal enum InventoryOutgoingReservationKind { LegacyMaterial, Physical }
internal sealed record InventoryOutgoingAdmissionCheckpoint(InventoryOutgoingReservationKind Kind,
    ulong ReservationValue, ulong AdmissionSequence);

/// <summary>Stable outgoing admission age and exact live legacy/generalized reservation reference.</summary>
internal abstract record InventoryOutgoingCommitment(ulong AdmissionSequence)
{
    internal abstract InventoryOutgoingReservationKind Kind { get; }
    internal abstract ulong ReservationValue { get; }

    internal sealed record Legacy(ulong AdmissionSequence, Reservation Reservation) : InventoryOutgoingCommitment(AdmissionSequence)
    {
        internal override InventoryOutgoingReservationKind Kind => InventoryOutgoingReservationKind.LegacyMaterial;
        internal override ulong ReservationValue => Reservation.Id.Value;
    }

    internal sealed record Physical(ulong AdmissionSequence, PhysicalReservation Reservation) : InventoryOutgoingCommitment(AdmissionSequence)
    {
        internal override InventoryOutgoingReservationKind Kind => InventoryOutgoingReservationKind.Physical;
        internal override ulong ReservationValue => Reservation.Id.Value;
    }
}

public sealed partial class Inventory
{
    private ulong? _nextOutgoingAdmissionSequence = 1;
    private readonly Dictionary<ReservationId, ulong> _legacyOutgoingAdmissions = [];
    private readonly Dictionary<ReservationId, ulong> _physicalOutgoingAdmissions = [];
    private bool _restoringOutgoingAdmissions;

    /// <summary>Captures exact outgoing references in one legacy/generalized oldest-first admission order.</summary>
    internal IReadOnlyList<InventoryOutgoingCommitment> CaptureOutgoingCommitments() => Array.AsReadOnly(
        _reservations.Values.Select(reservation => (InventoryOutgoingCommitment)new InventoryOutgoingCommitment.Legacy(
                _legacyOutgoingAdmissions[reservation.Id], reservation))
            .Concat(_physicalReservations.Values.Where(reservation => reservation.Subject is not PhysicalReservationSubject.IncomingCapacity)
                .Select(reservation => new InventoryOutgoingCommitment.Physical(_physicalOutgoingAdmissions[reservation.Id], reservation)))
            .OrderBy(commitment => commitment.AdmissionSequence).ThenBy(commitment => commitment.Kind)
            .ThenBy(commitment => commitment.ReservationValue).ToArray());

    internal bool CanAdmitOutgoingCommitment => _restoringOutgoingAdmissions || _nextOutgoingAdmissionSequence is not null;

    private IEnumerable<InventoryOutgoingAdmissionCheckpoint> CaptureOutgoingAdmissionCheckpoints() =>
        CaptureOutgoingCommitments().Select(commitment => new InventoryOutgoingAdmissionCheckpoint(
            commitment.Kind, commitment.ReservationValue, commitment.AdmissionSequence));

    /// <summary>Allocates legacy claim age only after successful authoritative source reservation.</summary>
    private void RecordOutgoingAdmission(Reservation reservation)
    {
        if (_restoringOutgoingAdmissions) return;
        _legacyOutgoingAdmissions.Add(reservation.Id, TakeOutgoingAdmissionSequence());
    }

    /// <summary>Records generalized source claims independently of the incoming-capacity sequence.</summary>
    private void RecordOutgoingAdmission(PhysicalReservation reservation)
    {
        if (_restoringOutgoingAdmissions || reservation.Subject is PhysicalReservationSubject.IncomingCapacity) return;
        _physicalOutgoingAdmissions.Add(reservation.Id, TakeOutgoingAdmissionSequence());
    }

    /// <summary>Retains outgoing highwater and terminal exhaustion after claims are consumed or released.</summary>
    private ulong TakeOutgoingAdmissionSequence()
    {
        ulong value = _nextOutgoingAdmissionSequence ?? throw new InvalidOperationException("Inventory outgoing admission sequence exhausted.");
        _nextOutgoingAdmissionSequence = value == ulong.MaxValue ? null : value + 1;
        return value;
    }

    /// <summary>Restores every live source claim's exact age without deriving priority from reservation IDs.</summary>
    private CheckpointValidationFailure? RestoreOutgoingAdmissionState(InventoryCheckpoint checkpoint, string path)
    {
        if (checkpoint.NextOutgoingAdmissionSequence == 0)
            return new($"{path}.nextOutgoingAdmissionSequence", "Outgoing admission highwater must be positive or exhausted.");
        var expectedLegacy = _reservations.Keys.ToHashSet();
        var expectedPhysical = _physicalReservations.Values.Where(reservation => reservation.Subject is not PhysicalReservationSubject.IncomingCapacity)
            .Select(reservation => reservation.Id).ToHashSet();
        var sequences = new HashSet<ulong>();
        var legacy = new Dictionary<ReservationId, ulong>();
        var physical = new Dictionary<ReservationId, ulong>();
        foreach (InventoryOutgoingAdmissionCheckpoint? entry in checkpoint.OutgoingAdmissions)
        {
            if (entry is null || entry.ReservationValue == 0 || entry.AdmissionSequence == 0
                || !sequences.Add(entry.AdmissionSequence)
                || checkpoint.NextOutgoingAdmissionSequence is { } next && entry.AdmissionSequence >= next)
                return new($"{path}.outgoingAdmissions", "Outgoing admission identities or sequence highwater are invalid.");
            switch (entry.Kind)
            {
                case InventoryOutgoingReservationKind.LegacyMaterial when expectedLegacy.Remove(new(entry.ReservationValue)):
                    legacy.Add(new(entry.ReservationValue), entry.AdmissionSequence);
                    break;
                case InventoryOutgoingReservationKind.Physical when expectedPhysical.Remove(new(entry.ReservationValue)):
                    physical.Add(new(entry.ReservationValue), entry.AdmissionSequence);
                    break;
                default:
                    return new($"{path}.outgoingAdmissions", "Outgoing ages must uniquely reference live source reservation kinds.");
            }
        }
        if (expectedLegacy.Count != 0 || expectedPhysical.Count != 0)
            return new($"{path}.outgoingAdmissions", "Every outgoing commitment requires an explicit saved admission age.");
        foreach ((ReservationId id, ulong sequence) in legacy) _legacyOutgoingAdmissions.Add(id, sequence);
        foreach ((ReservationId id, ulong sequence) in physical) _physicalOutgoingAdmissions.Add(id, sequence);
        _nextOutgoingAdmissionSequence = checkpoint.NextOutgoingAdmissionSequence;
        return null;
    }
}
