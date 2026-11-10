using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal enum CargoSourceLossFailure
{
    MissingCustody, MissingDefinition, MissingHolding, InsufficientQuantity,
    DuplicateInstance, InvalidSelection, InvalidCleanup, CommitmentConflict,
    CapacityMismatch, ArithmeticOverflow,
}

/// <summary>Explicit authorization to release one outgoing source commitment for its exact owner.</summary>
internal abstract record CargoSourceReservationCleanup(ReservationId ReservationId, ReservationOwner Owner)
{
    internal sealed record Physical(ReservationId ReservationId, ReservationOwner Owner) : CargoSourceReservationCleanup(ReservationId, Owner);
    internal sealed record Legacy(ReservationId ReservationId, ReservationOwner Owner) : CargoSourceReservationCleanup(ReservationId, Owner);
}

/// <summary>Canonical selected definition quantity, retaining exact original instances for discrete cargo.</summary>
internal sealed record CargoSourceLossGroup(PhysicalDefinition Definition, Quantity Quantity, IReadOnlyList<DiscreteItemInstance> DiscreteItems);

internal sealed record CargoSourceLossPreparation(PreparedCargoSourceLoss? Prepared, CargoSourceLossFailure? Failure);

/// <summary>A prepared inventory delta. Its enclosing causal owner commits all other participants atomically.</summary>
internal sealed class PreparedCargoSourceLoss
{
    private readonly IReadOnlyList<PhysicalReservation> _physicalCleanup;
    private readonly IReadOnlyList<Reservation> _legacyCleanup;
    private bool _applied;

    internal PreparedCargoSourceLoss(Inventory source, InventoryCheckpoint expectedState,
        IReadOnlyList<CargoSourceLossGroup> groups, Quantity removedCapacity,
        IEnumerable<PhysicalReservation> physicalCleanup, IEnumerable<Reservation> legacyCleanup)
    {
        Source = source;
        ExpectedState = expectedState;
        Groups = groups;
        RemovedCapacity = removedCapacity;
        _physicalCleanup = Array.AsReadOnly(physicalCleanup.OrderBy(reservation => reservation.Id.Value).ToArray());
        _legacyCleanup = Array.AsReadOnly(legacyCleanup.OrderBy(reservation => reservation.Id.Value).ToArray());
    }

    internal Inventory Source { get; }
    internal InventoryCheckpoint ExpectedState { get; }
    internal IReadOnlyList<CargoSourceLossGroup> Groups { get; }
    internal Quantity RemovedCapacity { get; }
    internal bool WasApplied => _applied;
    internal IReadOnlyList<PhysicalReservation> PhysicalCleanup => _physicalCleanup;
    internal IReadOnlyList<Reservation> LegacyCleanup => _legacyCleanup;

    /// <summary>
    /// Binds a prepared removal to a freshly resolved causal request, including
    /// cleanup kept private by this owner and original discrete instance references.
    /// </summary>
    internal bool HasSameDeltaAs(PreparedCargoSourceLoss expected) => ReferenceEquals(Source, expected.Source)
        && RemovedCapacity == expected.RemovedCapacity && Groups is not null && Groups.Count == expected.Groups.Count
        && _physicalCleanup.SequenceEqual(expected._physicalCleanup) && _legacyCleanup.SequenceEqual(expected._legacyCleanup)
        && Groups.Zip(expected.Groups).All(pair => pair.First is not null
            && pair.First.Definition == pair.Second.Definition && pair.First.Quantity == pair.Second.Quantity
            && pair.First.DiscreteItems is not null && pair.First.DiscreteItems.Count == pair.Second.DiscreteItems.Count
            && pair.First.DiscreteItems.Zip(pair.Second.DiscreteItems).All(items => ReferenceEquals(items.First, items.Second)));

    /// <summary>
    /// Revalidates the entire source state and selected instance references before any
    /// cleanup or holding mutation. Equal replacement instances do not retain identity state.
    /// </summary>
    internal bool CanApply()
    {
        if (_applied) return false;
        InventoryCheckpoint current = Source.CaptureCheckpoint();
        return current.Id == ExpectedState.Id && current.Custody == ExpectedState.Custody
            && current.Capacity == ExpectedState.Capacity
            && current.EnabledIntakeCapacity == ExpectedState.EnabledIntakeCapacity
            && current.ProtectedIntakeCapacity == ExpectedState.ProtectedIntakeCapacity
            && current.NextIncomingAdmissionSequence == ExpectedState.NextIncomingAdmissionSequence
            && current.IncomingAdmissions.SequenceEqual(ExpectedState.IncomingAdmissions)
            && current.NextOutgoingAdmissionSequence == ExpectedState.NextOutgoingAdmissionSequence
            && current.OutgoingAdmissions.SequenceEqual(ExpectedState.OutgoingAdmissions)
            && current.StoredMaterials.SequenceEqual(ExpectedState.StoredMaterials)
            && current.FungibleHoldings.SequenceEqual(ExpectedState.FungibleHoldings)
            && current.DiscreteItems.SequenceEqual(ExpectedState.DiscreteItems)
            && current.Reservations.SequenceEqual(ExpectedState.Reservations)
            && current.CapacityReservations.SequenceEqual(ExpectedState.CapacityReservations)
            && current.PhysicalReservations.SequenceEqual(ExpectedState.PhysicalReservations)
            && Groups.SelectMany(group => group.DiscreteItems).All(item => ReferenceEquals(item, Source.GetDiscrete(item.Id)));
    }

    /// <summary>
    /// Releases only exact approved outgoing commitments and removes selected holdings.
    /// Stale or already applied plans return false without mutation. The caller's session
    /// health guard owns unexpected failures after the first accepted owner delta.
    /// </summary>
    internal bool Apply()
    {
        if (!CanApply()) return false;
        foreach (PhysicalReservation reservation in _physicalCleanup)
            if (!Source.ReleasePhysicalReservation(reservation.Id, reservation.Owner).IsAccepted)
                throw new InvalidOperationException("Prevalidated cargo source commitment changed during commit.");
        foreach (Reservation reservation in _legacyCleanup)
            if (Source.Release(reservation.Id) != reservation)
                throw new InvalidOperationException("Prevalidated legacy cargo commitment changed during commit.");

        foreach (CargoSourceLossGroup group in Groups)
        {
            if (group.Definition.HoldingKind == PhysicalHoldingKind.Fungible)
                Source.ApplyRemoveFungible(group.Definition.Key, group.Quantity,
                    new Quantity(checked(group.Quantity.Units * group.Definition.CapacityCost.Units)));
            else
                foreach (DiscreteItemInstance item in group.DiscreteItems)
                    Source.ApplyRemoveDiscrete(item.Id, group.Definition.CapacityCost);
        }
        _applied = true;
        return true;
    }
}

internal static class CargoSourceLoss
{
    /// <summary>
    /// Prepares canonical source removal and caller-authorized outgoing cleanup.
    /// No loss policy, item allocation, workflow cancellation, or source mutation occurs.
    /// </summary>
    internal static CargoSourceLossPreparation Prepare(Inventory source, IReadOnlyList<PhysicalTransferSubject> selected,
        PhysicalDefinitionCatalog definitions, IReadOnlyList<CargoSourceReservationCleanup> cleanup)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(cleanup);
        try { return source.PrepareCargoSourceLoss(selected, definitions, cleanup); }
        catch (OverflowException) { return new(null, CargoSourceLossFailure.ArithmeticOverflow); }
    }
}

public sealed partial class Inventory
{
    /// <summary>
    /// Resolves selected stock against exact catalog meaning and proves that all
    /// untouched source commitments remain satisfiable after the proposed loss.
    /// </summary>
    internal CargoSourceLossPreparation PrepareCargoSourceLoss(IReadOnlyList<PhysicalTransferSubject> selected,
        PhysicalDefinitionCatalog definitions, IReadOnlyList<CargoSourceReservationCleanup> cleanup)
    {
        if (Custody is not InventoryCustody.PrincipalControlled)
            return Failed(CargoSourceLossFailure.MissingCustody);
        CargoSourceLossFailure? catalogFailure = ValidateCargoSourceCatalog(definitions);
        if (catalogFailure is not null) return Failed(catalogFailure.Value);

        var groups = new Dictionary<QualifiedContentKey, CargoLossGroupBuilder>();
        var itemIds = new HashSet<ItemInstanceId>();
        foreach (PhysicalTransferSubject? subject in selected)
        {
            PhysicalDefinition? definition;
            DiscreteItemInstance? item = null;
            Quantity quantity;
            switch (subject)
            {
                case PhysicalTransferSubject.Fungible fungible:
                    definition = definitions.Get(fungible.DefinitionKey);
                    if (definition is null) return Failed(CargoSourceLossFailure.MissingDefinition);
                    if (definition.HoldingKind != PhysicalHoldingKind.Fungible) return Failed(CargoSourceLossFailure.InvalidSelection);
                    quantity = fungible.Quantity;
                    break;
                case PhysicalTransferSubject.Discrete discrete:
                    if (!itemIds.Add(discrete.InstanceId)) return Failed(CargoSourceLossFailure.DuplicateInstance);
                    item = GetDiscrete(discrete.InstanceId);
                    if (item is null) return Failed(CargoSourceLossFailure.MissingHolding);
                    definition = definitions.Get(item.DefinitionKey);
                    if (definition is null) return Failed(CargoSourceLossFailure.MissingDefinition);
                    if (definition.HoldingKind != PhysicalHoldingKind.Discrete) return Failed(CargoSourceLossFailure.InvalidSelection);
                    quantity = new Quantity(1);
                    break;
                default:
                    return Failed(CargoSourceLossFailure.InvalidSelection);
            }

            if (!groups.TryGetValue(definition.Key, out CargoLossGroupBuilder? group))
                groups.Add(definition.Key, group = new CargoLossGroupBuilder(definition));
            group.Quantity = group.Quantity.Add(quantity);
            if (item is not null) group.Items.Add(item);
            if (definition.HoldingKind == PhysicalHoldingKind.Fungible && group.Quantity > FungibleStored(definition.Key))
                return Failed(CargoSourceLossFailure.InsufficientQuantity);
        }

        var physicalCleanup = new Dictionary<ReservationId, PhysicalReservation>();
        var legacyCleanup = new Dictionary<ReservationId, Reservation>();
        foreach (CargoSourceReservationCleanup? authorization in cleanup)
        {
            if (authorization?.Owner is null || authorization.ReservationId.Value == 0)
                return Failed(CargoSourceLossFailure.InvalidCleanup);
            switch (authorization)
            {
                case CargoSourceReservationCleanup.Physical physical:
                    if (GetPhysicalReservation(physical.ReservationId) is not { } reservation
                        || reservation.InventoryId != Id || reservation.Owner != physical.Owner
                        || reservation.Subject is PhysicalReservationSubject.IncomingCapacity
                        || !physicalCleanup.TryAdd(reservation.Id, reservation))
                        return Failed(CargoSourceLossFailure.InvalidCleanup);
                    break;
                case CargoSourceReservationCleanup.Legacy legacy:
                    if (GetReservation(legacy.ReservationId) is not { } material
                        || material.InventoryId != Id || material.Owner != legacy.Owner
                        || !legacyCleanup.TryAdd(material.Id, material))
                        return Failed(CargoSourceLossFailure.InvalidCleanup);
                    break;
                default:
                    return Failed(CargoSourceLossFailure.InvalidCleanup);
            }
        }

        foreach (CargoLossGroupBuilder group in groups.Values)
        {
            if (group.Definition.HoldingKind == PhysicalHoldingKind.Fungible)
            {
                UInt128 remainingReserved = 0;
                foreach (PhysicalReservation reservation in _physicalReservations.Values)
                    if (!physicalCleanup.ContainsKey(reservation.Id)
                        && reservation.Subject is PhysicalReservationSubject.Fungible fungible
                        && fungible.DefinitionKey == group.Definition.Key)
                        remainingReserved = checked(remainingReserved + fungible.Quantity.Units);
                foreach (Reservation reservation in _reservations.Values)
                    if (!legacyCleanup.ContainsKey(reservation.Id)
                        && _materialCompatibility?.Get(reservation.MaterialId)?.Key == group.Definition.Key)
                        remainingReserved = checked(remainingReserved + reservation.Quantity.Units);
                if (remainingReserved > FungibleStored(group.Definition.Key).Units - group.Quantity.Units)
                    return Failed(CargoSourceLossFailure.CommitmentConflict);
            }
            else if (_physicalReservations.Values.Any(reservation => !physicalCleanup.ContainsKey(reservation.Id)
                && reservation.Subject is PhysicalReservationSubject.Discrete discrete && itemIds.Contains(discrete.InstanceId)))
                return Failed(CargoSourceLossFailure.CommitmentConflict);
        }

        CargoSourceLossGroup[] canonical = groups.Values.OrderBy(group => group.Definition.Key.ToString(), StringComparer.Ordinal)
            .Select(group => new CargoSourceLossGroup(group.Definition, group.Quantity,
                Array.AsReadOnly(group.Items.OrderBy(item => item.Id.Value).ToArray()))).ToArray();
        ulong removedCapacity = 0;
        foreach (CargoSourceLossGroup group in canonical)
            removedCapacity = checked(removedCapacity + checked(group.Quantity.Units * group.Definition.CapacityCost.Units));
        return new CargoSourceLossPreparation(new PreparedCargoSourceLoss(this, CaptureCheckpoint(),
            Array.AsReadOnly(canonical), new Quantity(removedCapacity), physicalCleanup.Values, legacyCleanup.Values), null);
    }

    /// <summary>
    /// Proves catalog unit costs reproduce current physical capacity and any active
    /// material mappings before those costs are used for removal accounting.
    /// </summary>
    private CargoSourceLossFailure? ValidateCargoSourceCatalog(PhysicalDefinitionCatalog definitions)
    {
        ulong occupied = 0;
        foreach ((QualifiedContentKey key, Quantity quantity) in _fungibleStored)
        {
            PhysicalDefinition? definition = definitions.Get(key);
            if (definition is null) return CargoSourceLossFailure.MissingDefinition;
            if (definition.HoldingKind != PhysicalHoldingKind.Fungible) return CargoSourceLossFailure.InvalidSelection;
            occupied = checked(occupied + checked(quantity.Units * definition.CapacityCost.Units));
            if (_materialCompatibility?.Mappings.Values.FirstOrDefault(mapped => mapped.Key == key) is { } mapped
                && mapped != definition) return CargoSourceLossFailure.CapacityMismatch;
        }
        foreach (DiscreteItemInstance item in _discreteItems.Values)
        {
            PhysicalDefinition? definition = definitions.Get(item.DefinitionKey);
            if (definition is null) return CargoSourceLossFailure.MissingDefinition;
            if (definition.HoldingKind != PhysicalHoldingKind.Discrete) return CargoSourceLossFailure.InvalidSelection;
            occupied = checked(occupied + definition.CapacityCost.Units);
        }
        return occupied == _physicalUsedCapacity.Units ? null : CargoSourceLossFailure.CapacityMismatch;
    }

    private static CargoSourceLossPreparation Failed(CargoSourceLossFailure failure) => new(null, failure);

    private sealed class CargoLossGroupBuilder(PhysicalDefinition definition)
    {
        internal PhysicalDefinition Definition { get; } = definition;
        internal Quantity Quantity { get; set; }
        internal List<DiscreteItemInstance> Items { get; } = [];
    }
}
