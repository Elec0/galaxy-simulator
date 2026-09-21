using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable presentation projection of one aggregate fungible holding.
/// </summary>
public sealed record FungibleHoldingPresentationSnapshot(
    QualifiedContentKey DefinitionKey,
    Quantity Stored,
    Quantity Reserved,
    Quantity Available);

/// <summary>
/// Immutable presentation projection of one discrete physical item.
/// </summary>
public sealed record DiscreteItemPresentationSnapshot(
    ItemInstanceId Id,
    QualifiedContentKey DefinitionKey,
    bool IsReserved);

/// <summary>
/// Immutable locale-neutral summary of one live generalized reservation.
/// </summary>
public sealed record InventoryReservationPresentationSnapshot(
    ReservationId Id,
    ReservationOwner Owner,
    PhysicalReservationSubject Subject);

/// <summary>
/// Immutable locale-neutral contents, custody, commitments, and capacity for
/// one inventory.
/// </summary>
public sealed record InventoryPresentationSnapshot
{
    internal InventoryPresentationSnapshot(
        InventoryId id,
        InventoryCustody? custody,
        Quantity capacity,
        Quantity usedCapacity,
        Quantity reservedIncomingCapacity,
        Quantity remainingCapacity,
        IEnumerable<FungibleHoldingPresentationSnapshot> fungibleHoldings,
        IEnumerable<DiscreteItemPresentationSnapshot> discreteItems,
        IEnumerable<InventoryReservationPresentationSnapshot> reservations)
    {
        ArgumentNullException.ThrowIfNull(fungibleHoldings);
        ArgumentNullException.ThrowIfNull(discreteItems);
        ArgumentNullException.ThrowIfNull(reservations);
        Id = id;
        Custody = custody;
        Capacity = capacity;
        UsedCapacity = usedCapacity;
        ReservedIncomingCapacity = reservedIncomingCapacity;
        RemainingCapacity = remainingCapacity;
        FungibleHoldings = Array.AsReadOnly(fungibleHoldings.ToArray());
        DiscreteItems = Array.AsReadOnly(discreteItems.ToArray());
        Reservations = Array.AsReadOnly(reservations.ToArray());
    }

    public InventoryId Id { get; }

    public InventoryCustody? Custody { get; }

    public Quantity Capacity { get; }

    public Quantity UsedCapacity { get; }

    public Quantity ReservedIncomingCapacity { get; }

    public Quantity RemainingCapacity { get; }

    public ReadOnlyCollection<FungibleHoldingPresentationSnapshot> FungibleHoldings { get; }

    public ReadOnlyCollection<DiscreteItemPresentationSnapshot> DiscreteItems { get; }

    public ReadOnlyCollection<InventoryReservationPresentationSnapshot> Reservations { get; }
}

/// <summary>
/// Immutable presentation projection of every inventory in stable identity
/// order.
/// </summary>
public sealed record InventoryRegistryPresentationSnapshot
{
    internal InventoryRegistryPresentationSnapshot(
        IEnumerable<InventoryPresentationSnapshot> inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        Inventories = Array.AsReadOnly(inventories.ToArray());
    }

    public ReadOnlyCollection<InventoryPresentationSnapshot> Inventories { get; }
}

public sealed partial class Inventory
{
    /// <summary>
    /// Captures immutable physical holdings and commitments in canonical
    /// identity order without resolving localized presentation strings.
    /// </summary>
    public InventoryPresentationSnapshot CapturePresentationSnapshot()
    {
        IReadOnlyList<FungibleHoldingPresentationSnapshot> fungible =
            _fungibleStored
                .OrderBy(
                    entry => entry.Key.ToString(),
                    StringComparer.Ordinal)
                .Select(entry => new FungibleHoldingPresentationSnapshot(
                    entry.Key,
                    entry.Value,
                    FungibleReserved(entry.Key),
                    FungibleAvailable(entry.Key)))
                .ToArray();
        IReadOnlyList<DiscreteItemPresentationSnapshot> discrete =
            _discreteItems.Values
                .OrderBy(instance => instance.Id.Value)
                .Select(instance => new DiscreteItemPresentationSnapshot(
                    instance.Id,
                    instance.DefinitionKey,
                    IsDiscreteReserved(instance.Id)))
                .ToArray();
        IReadOnlyList<InventoryReservationPresentationSnapshot> reservations =
            _physicalReservations.Values
                .OrderBy(reservation => reservation.Id.Value)
                .Select(reservation =>
                    new InventoryReservationPresentationSnapshot(
                        reservation.Id,
                        reservation.Owner,
                        reservation.Subject))
                .ToArray();
        Quantity reservedIncomingCapacity = _physicalReservations.Values
            .Select(reservation => reservation.Subject)
            .OfType<PhysicalReservationSubject.IncomingCapacity>()
            .Aggregate(
                Quantity.Zero,
                (total, incoming) => total.Add(incoming.Quantity));

        return new InventoryPresentationSnapshot(
            Id,
            Custody,
            Capacity,
            UsedCapacity,
            reservedIncomingCapacity,
            RemainingCapacity,
            fungible,
            discrete,
            reservations);
    }
}

public sealed partial class InventoryRegistry
{
    /// <summary>
    /// Captures every inventory projection in stable inventory identity order.
    /// </summary>
    public InventoryRegistryPresentationSnapshot CapturePhysicalPresentationSnapshot() =>
        new(_inventories.Values
            .OrderBy(inventory => inventory.Id.Value)
            .Select(inventory => inventory.CapturePresentationSnapshot()));
}
