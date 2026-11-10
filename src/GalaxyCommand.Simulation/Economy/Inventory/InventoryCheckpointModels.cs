using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record InventoryMaterialCheckpoint(
    MaterialId MaterialId,
    Quantity Quantity);

internal sealed record InventoryFungibleCheckpoint(
    QualifiedContentKey DefinitionKey,
    Quantity Quantity);

internal sealed record InventoryDiscreteItemCheckpoint(
    ItemInstanceId Id,
    QualifiedContentKey DefinitionKey);

internal sealed class InventoryCheckpoint
{
    internal InventoryCheckpoint(
        InventoryId id,
        Quantity capacity,
        IEnumerable<InventoryMaterialCheckpoint> storedMaterials,
        IEnumerable<Reservation> reservations,
        IEnumerable<CapacityReservation> capacityReservations)
        : this(
            id,
            null,
            capacity,
            storedMaterials,
            reservations,
            capacityReservations,
            Array.Empty<InventoryFungibleCheckpoint>(),
            Array.Empty<InventoryDiscreteItemCheckpoint>(),
            Array.Empty<PhysicalReservation>())
    {
    }

    internal InventoryCheckpoint(
        InventoryId id,
        InventoryCustody? custody,
        Quantity capacity,
        IEnumerable<InventoryMaterialCheckpoint> storedMaterials,
        IEnumerable<Reservation> reservations,
        IEnumerable<CapacityReservation> capacityReservations)
        : this(
            id,
            custody,
            capacity,
            storedMaterials,
            reservations,
            capacityReservations,
            Array.Empty<InventoryFungibleCheckpoint>(),
            Array.Empty<InventoryDiscreteItemCheckpoint>(),
            Array.Empty<PhysicalReservation>())
    {
    }

    internal InventoryCheckpoint(
        InventoryId id,
        InventoryCustody? custody,
        Quantity capacity,
        IEnumerable<InventoryMaterialCheckpoint> storedMaterials,
        IEnumerable<Reservation> reservations,
        IEnumerable<CapacityReservation> capacityReservations,
        IEnumerable<InventoryFungibleCheckpoint> fungibleHoldings,
        IEnumerable<InventoryDiscreteItemCheckpoint> discreteItems,
        IEnumerable<PhysicalReservation> physicalReservations,
        Quantity? enabledIntakeCapacity = null,
        Quantity? protectedIntakeCapacity = null,
        ulong? nextIncomingAdmissionSequence = 1,
        IEnumerable<InventoryIncomingAdmissionCheckpoint>? incomingAdmissions = null,
        ulong? nextOutgoingAdmissionSequence = 1,
        IEnumerable<InventoryOutgoingAdmissionCheckpoint>? outgoingAdmissions = null)
    {
        ArgumentNullException.ThrowIfNull(storedMaterials);
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentNullException.ThrowIfNull(capacityReservations);
        ArgumentNullException.ThrowIfNull(fungibleHoldings);
        ArgumentNullException.ThrowIfNull(discreteItems);
        ArgumentNullException.ThrowIfNull(physicalReservations);
        Id = id;
        Custody = custody;
        Capacity = capacity;
        EnabledIntakeCapacity = enabledIntakeCapacity ?? capacity;
        ProtectedIntakeCapacity = protectedIntakeCapacity;
        NextIncomingAdmissionSequence = nextIncomingAdmissionSequence;
        IncomingAdmissions = Array.AsReadOnly((incomingAdmissions ?? []).ToArray());
        NextOutgoingAdmissionSequence = nextOutgoingAdmissionSequence;
        OutgoingAdmissions = Array.AsReadOnly((outgoingAdmissions ?? []).ToArray());
        StoredMaterials = new ReadOnlyCollection<InventoryMaterialCheckpoint>(
            storedMaterials.ToArray());
        Reservations = new ReadOnlyCollection<Reservation>(
            reservations.ToArray());
        CapacityReservations = new ReadOnlyCollection<CapacityReservation>(
            capacityReservations.ToArray());
        FungibleHoldings = new ReadOnlyCollection<InventoryFungibleCheckpoint>(
            fungibleHoldings.ToArray());
        DiscreteItems = new ReadOnlyCollection<InventoryDiscreteItemCheckpoint>(
            discreteItems.ToArray());
        PhysicalReservations = new ReadOnlyCollection<PhysicalReservation>(
            physicalReservations.ToArray());
    }

    internal InventoryId Id { get; }

    internal InventoryCustody? Custody { get; }

    internal Quantity Capacity { get; }

    internal Quantity EnabledIntakeCapacity { get; }

    internal Quantity? ProtectedIntakeCapacity { get; }

    internal ulong? NextIncomingAdmissionSequence { get; }

    internal IReadOnlyList<InventoryIncomingAdmissionCheckpoint> IncomingAdmissions { get; }

    internal ulong? NextOutgoingAdmissionSequence { get; }

    internal IReadOnlyList<InventoryOutgoingAdmissionCheckpoint> OutgoingAdmissions { get; }

    internal ReadOnlyCollection<InventoryMaterialCheckpoint> StoredMaterials { get; }

    internal ReadOnlyCollection<Reservation> Reservations { get; }

    internal ReadOnlyCollection<CapacityReservation> CapacityReservations { get; }

    internal ReadOnlyCollection<InventoryFungibleCheckpoint> FungibleHoldings { get; }

    internal ReadOnlyCollection<InventoryDiscreteItemCheckpoint> DiscreteItems { get; }

    internal ReadOnlyCollection<PhysicalReservation> PhysicalReservations { get; }
}

internal sealed class InventoryRegistryCheckpoint
{
    internal InventoryRegistryCheckpoint(
        IEnumerable<InventoryCheckpoint> inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        Inventories = new ReadOnlyCollection<InventoryCheckpoint>(
            inventories.ToArray());
    }

    internal ReadOnlyCollection<InventoryCheckpoint> Inventories { get; }
}

