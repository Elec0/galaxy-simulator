using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>Exact pre-loss authority and projected post-loss inventory state for one enclosing station commit.</summary>
internal sealed class PreparedStationLossCapacityChange(
    Inventory inventory, PreparedCargoSourceLoss loss, PreparedCargoSourceLoss canonicalLoss,
    InventoryCapacityLimits expectedLimits, InventoryCapacityLimits proposedLimits,
    InventoryCheckpoint projectedState, Quantity projectedUsed, Quantity projectedReserved,
    IReadOnlyList<InventoryCommitmentRelease> incomingReleases,
    IReadOnlyDictionary<ItemInstanceId, DiscreteItemInstance> originalItems)
{
    internal Inventory Inventory { get; } = inventory;
    internal PreparedCargoSourceLoss Loss { get; } = loss;
    internal PreparedCargoSourceLoss CanonicalLoss { get; } = canonicalLoss;
    internal InventoryCapacityLimits ExpectedLimits { get; } = expectedLimits;
    internal InventoryCapacityLimits ProposedLimits { get; } = proposedLimits;
    internal InventoryCheckpoint ProjectedState { get; } = projectedState;
    internal Quantity ProjectedUsed { get; } = projectedUsed;
    internal Quantity ProjectedReserved { get; } = projectedReserved;
    internal IReadOnlyList<InventoryCommitmentRelease> IncomingReleases { get; } = incomingReleases;
    internal IReadOnlyDictionary<ItemInstanceId, DiscreteItemInstance> OriginalItems { get; } = originalItems;
}

public sealed partial class Inventory
{
    /// <summary>
    /// Projects one exact cargo-loss delta and explicitly authorized incoming releases before
    /// any owner changes. Re-resolving the loss rejects forged quantities, costs or cleanup;
    /// this method selects no loss policy and never releases a workflow's commitments.
    /// </summary>
    internal PreparedStationLossCapacityChange? PrepareStationLossCapacity(PreparedCargoSourceLoss loss,
        PhysicalDefinitionCatalog definitions, IReadOnlyList<InventoryCommitmentRelease> incomingReleases,
        Quantity physicalCapacity, Quantity enabledIntakeCapacity, Quantity? protectedIntakeCapacity = null)
    {
        ArgumentNullException.ThrowIfNull(loss);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(incomingReleases);
        if (!ReferenceEquals(loss.Source, this) || loss.WasApplied || !loss.CanApply()
            || enabledIntakeCapacity > physicalCapacity
            || protectedIntakeCapacity is { } limit && limit > physicalCapacity) return null;
        try
        {
            PreparedCargoSourceLoss? canonical = ResolveStationCapacityLoss(loss, definitions);
            if (canonical is null || !loss.HasSameDeltaAs(canonical)) return null;
            var legacyIncoming = new HashSet<CapacityReservationId>();
            var physicalIncoming = new HashSet<ReservationId>();
            ulong releasedRoom = 0;
            foreach (InventoryCommitmentRelease release in incomingReleases)
            {
                if (release is null || !ReferenceEquals(release.Inventory, this) || !release.CanCommit()) return null;
                switch (release)
                {
                    case InventoryCommitmentRelease.Capacity capacity
                        when capacity.Reservation.Id.Value != 0 && capacity.Reservation.InventoryId == Id
                            && IsValidOwner(capacity.Reservation.Owner)
                            && legacyIncoming.Add(capacity.Reservation.Id):
                        releasedRoom = checked(releasedRoom + capacity.Reservation.Quantity.Units);
                        break;
                    case InventoryCommitmentRelease.Physical physical
                        when physical.Reservation.Id.Value != 0 && physical.Reservation.InventoryId == Id
                            && IsValidOwner(physical.Reservation.Owner)
                            && physical.Reservation.Subject is PhysicalReservationSubject.IncomingCapacity incoming
                            && physicalIncoming.Add(physical.Reservation.Id):
                        releasedRoom = checked(releasedRoom + incoming.Quantity.Units);
                        break;
                    default:
                        return null;
                }
            }

            // Derive room from the freshly validated selection, never from an
            // unchecked caller total that could authorize a smaller retained store.
            ulong removed = 0;
            foreach (CargoSourceLossGroup group in canonical.Groups)
                removed = checked(removed + checked(group.Quantity.Units * group.Definition.CapacityCost.Units));
            if (removed > UsedCapacity.Units || releasedRoom > ReservedCapacity.Units) return null;
            Quantity projectedUsed = new(UsedCapacity.Units - removed);
            Quantity projectedReserved = new(ReservedCapacity.Units - releasedRoom);
            UInt128 occupied = (UInt128)projectedUsed.Units + projectedReserved.Units;
            if (occupied > physicalCapacity.Units || protectedIntakeCapacity is { } protection && occupied > protection.Units) return null;

            InventoryCheckpoint projected = ProjectStationLossState(loss, legacyIncoming, physicalIncoming);
            var items = new ReadOnlyDictionary<ItemInstanceId, DiscreteItemInstance>(loss.ExpectedState.DiscreteItems
                .ToDictionary(item => item.Id, item => GetDiscrete(item.Id)!));
            var prepared = new PreparedStationLossCapacityChange(this, loss, canonical, _capacityLimits,
                new(physicalCapacity, enabledIntakeCapacity, protectedIntakeCapacity), projected, projectedUsed,
                projectedReserved, Array.AsReadOnly(incomingReleases.ToArray()), items);
            return CanCommitStationLossCapacity(prepared) ? prepared : null;
        }
        catch (ArgumentException) { return null; }
        catch (OverflowException) { return null; }
    }

    /// <summary>Checks every original source identity and release claim before any participant begins the loss commit.</summary>
    internal bool CanCommitStationLossCapacity(PreparedStationLossCapacityChange prepared) =>
        ReferenceEquals(prepared.Inventory, this) && ReferenceEquals(prepared.ExpectedLimits, _capacityLimits)
        && !prepared.Loss.WasApplied && prepared.Loss.CanApply()
        && prepared.Loss.HasSameDeltaAs(prepared.CanonicalLoss)
        && HasExactStationLossState(prepared.Loss.ExpectedState, prepared.OriginalItems)
        && prepared.IncomingReleases.All(release => release.CanCommit());

    /// <summary>
    /// Replaces limits only after this exact loss owner applied and the authorized incoming
    /// releases produced the exact projected contents, surviving references and totals.
    /// Incomplete, substituted, stale or repeated commits leave capacity unchanged.
    /// </summary>
    internal bool ApplyStationLossCapacity(PreparedStationLossCapacityChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!ReferenceEquals(prepared.Inventory, this) || !ReferenceEquals(prepared.ExpectedLimits, _capacityLimits)
            || !prepared.Loss.WasApplied || !prepared.Loss.HasSameDeltaAs(prepared.CanonicalLoss)
            || UsedCapacity != prepared.ProjectedUsed || ReservedCapacity != prepared.ProjectedReserved
            || !HasExactStationLossState(prepared.ProjectedState, prepared.OriginalItems)) return false;
        PreparedInventoryCapacityChange? capacity = PrepareCapacityChange(prepared.ProposedLimits.Physical,
            prepared.ProposedLimits.EnabledIntake, prepared.ProposedLimits.ProtectedIntake);
        return capacity is not null && ApplyCapacityChange(capacity);
    }

    /// <summary>Freshly resolves the exact selected subjects and outgoing cleanup against current catalog meaning.</summary>
    private PreparedCargoSourceLoss? ResolveStationCapacityLoss(PreparedCargoSourceLoss loss, PhysicalDefinitionCatalog definitions)
    {
        var selected = new List<PhysicalTransferSubject>();
        foreach (CargoSourceLossGroup group in loss.Groups)
        {
            if (group?.Definition is null || group.DiscreteItems is null) return null;
            if (group.Definition.HoldingKind == PhysicalHoldingKind.Fungible)
                selected.Add(new PhysicalTransferSubject.Fungible(group.Definition.Key, group.Quantity));
            else
                foreach (DiscreteItemInstance item in group.DiscreteItems)
                {
                    if (item is null) return null;
                    selected.Add(new PhysicalTransferSubject.Discrete(item.Id));
                }
        }
        CargoSourceReservationCleanup[] cleanup = loss.PhysicalCleanup
            .Select(reservation => (CargoSourceReservationCleanup)new CargoSourceReservationCleanup.Physical(reservation.Id, reservation.Owner))
            .Concat(loss.LegacyCleanup.Select(reservation => new CargoSourceReservationCleanup.Legacy(reservation.Id, reservation.Owner))).ToArray();
        return CargoSourceLoss.Prepare(this, selected, definitions, cleanup).Prepared;
    }

    /// <summary>Projects aggregate holdings and removes only the exact selected identities and authorized commitment entries.</summary>
    private static InventoryCheckpoint ProjectStationLossState(PreparedCargoSourceLoss loss,
        HashSet<CapacityReservationId> legacyIncoming, HashSet<ReservationId> physicalIncoming)
    {
        InventoryCheckpoint before = loss.ExpectedState;
        var removedFungible = loss.Groups.Where(group => group.Definition.HoldingKind == PhysicalHoldingKind.Fungible)
            .ToDictionary(group => group.Definition.Key, group => group.Quantity);
        var removedItems = loss.Groups.SelectMany(group => group.DiscreteItems).Select(item => item.Id).ToHashSet();
        var outgoingPhysical = loss.PhysicalCleanup.Select(reservation => reservation.Id).ToHashSet();
        var outgoingLegacy = loss.LegacyCleanup.Select(reservation => reservation.Id).ToHashSet();
        return new InventoryCheckpoint(before.Id, before.Custody, before.Capacity, before.StoredMaterials,
            before.Reservations.Where(reservation => !outgoingLegacy.Contains(reservation.Id)),
            before.CapacityReservations.Where(reservation => !legacyIncoming.Contains(reservation.Id)),
            before.FungibleHoldings.Select(holding => new InventoryFungibleCheckpoint(holding.DefinitionKey,
                holding.Quantity.Subtract(removedFungible.GetValueOrDefault(holding.DefinitionKey, Quantity.Zero))))
                .Where(holding => holding.Quantity != Quantity.Zero),
            before.DiscreteItems.Where(item => !removedItems.Contains(item.Id)),
            before.PhysicalReservations.Where(reservation => !outgoingPhysical.Contains(reservation.Id) && !physicalIncoming.Contains(reservation.Id)),
            before.EnabledIntakeCapacity, before.ProtectedIntakeCapacity, before.NextIncomingAdmissionSequence,
            before.IncomingAdmissions.Where(entry => entry.Kind switch
            {
                InventoryIncomingReservationKind.LegacyCapacity => !legacyIncoming.Contains(new(entry.ReservationValue)),
                InventoryIncomingReservationKind.PhysicalCapacity => !physicalIncoming.Contains(new(entry.ReservationValue)),
                _ => false,
            }), before.NextOutgoingAdmissionSequence,
            before.OutgoingAdmissions.Where(entry => entry.Kind switch
            {
                InventoryOutgoingReservationKind.LegacyMaterial => !outgoingLegacy.Contains(new(entry.ReservationValue)),
                InventoryOutgoingReservationKind.Physical => !outgoingPhysical.Contains(new(entry.ReservationValue)),
                _ => false,
            }));
    }

    /// <summary>Compares canonical saved authority plus the actual surviving item and reservation instances.</summary>
    private bool HasExactStationLossState(InventoryCheckpoint expected,
        IReadOnlyDictionary<ItemInstanceId, DiscreteItemInstance> originalItems)
    {
        InventoryCheckpoint actual = CaptureCheckpoint();
        return actual.Id == expected.Id && actual.Custody == expected.Custody && actual.Capacity == expected.Capacity
            && actual.EnabledIntakeCapacity == expected.EnabledIntakeCapacity && actual.ProtectedIntakeCapacity == expected.ProtectedIntakeCapacity
            && actual.NextIncomingAdmissionSequence == expected.NextIncomingAdmissionSequence
            && actual.IncomingAdmissions.SequenceEqual(expected.IncomingAdmissions)
            && actual.NextOutgoingAdmissionSequence == expected.NextOutgoingAdmissionSequence
            && actual.OutgoingAdmissions.SequenceEqual(expected.OutgoingAdmissions)
            && actual.StoredMaterials.SequenceEqual(expected.StoredMaterials)
            && actual.FungibleHoldings.SequenceEqual(expected.FungibleHoldings) && actual.DiscreteItems.SequenceEqual(expected.DiscreteItems)
            && actual.Reservations.SequenceEqual(expected.Reservations) && actual.CapacityReservations.SequenceEqual(expected.CapacityReservations)
            && actual.PhysicalReservations.SequenceEqual(expected.PhysicalReservations)
            && expected.DiscreteItems.All(item => originalItems.TryGetValue(item.Id, out DiscreteItemInstance? original)
                && ReferenceEquals(GetDiscrete(item.Id), original))
            && expected.Reservations.All(reservation => ReferenceEquals(GetReservation(reservation.Id), reservation))
            && expected.CapacityReservations.All(reservation => ReferenceEquals(GetCapacityReservation(reservation.Id), reservation))
            && expected.PhysicalReservations.All(reservation => ReferenceEquals(GetPhysicalReservation(reservation.Id), reservation));
    }
}
