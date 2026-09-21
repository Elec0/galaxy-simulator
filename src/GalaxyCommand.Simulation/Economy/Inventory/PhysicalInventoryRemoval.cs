namespace GalaxyCommand.Simulation;

/// <summary>
/// Explicit disposition for every holding when its inventory owner is removed.
/// </summary>
public abstract record InventoryRemovalDisposition
{
    private InventoryRemovalDisposition()
    {
    }

    /// <summary>Destroys all contents without creating replacement state.</summary>
    public sealed record DestroyContents : InventoryRemovalDisposition;

    /// <summary>Transfers all contents to one already existing inventory.</summary>
    public sealed record TransferContents : InventoryRemovalDisposition
    {
        /// <summary>Creates a transfer disposition for one destination.</summary>
        public TransferContents(InventoryId destinationInventoryId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(destinationInventoryId.Value);
            DestinationInventoryId = destinationInventoryId;
        }

        public InventoryId DestinationInventoryId { get; }
    }
}

/// <summary>Requests removal of one inventory with an explicit disposition.</summary>
public sealed record InventoryRemovalRequest
{
    /// <summary>Creates one inventory-removal request.</summary>
    public InventoryRemovalRequest(
        InventoryId inventoryId,
        InventoryRemovalDisposition disposition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(inventoryId.Value);
        ArgumentNullException.ThrowIfNull(disposition);
        InventoryId = inventoryId;
        Disposition = disposition;
    }

    public InventoryId InventoryId { get; }

    public InventoryRemovalDisposition Disposition { get; }
}

/// <summary>Stable reasons an inventory disposition could not commit.</summary>
public enum InventoryRemovalRejectionReason
{
    MissingInventory,
    MissingCustody,
    InventoryHasCommitments,
    SameDestination,
    MissingDestinationInventory,
    InsufficientCapacity,
    DestinationItemConflict,
}

/// <summary>Typed result for one inventory removal and disposition request.</summary>
public sealed record InventoryRemovalResult
{
    private InventoryRemovalResult(
        InventoryRemovalRequest request,
        bool isAccepted,
        InventoryRemovalRejectionReason? rejectionReason)
    {
        Request = request;
        IsAccepted = isAccepted;
        RejectionReason = rejectionReason;
    }

    public InventoryRemovalRequest Request { get; }

    public bool IsAccepted { get; }

    public InventoryRemovalRejectionReason? RejectionReason { get; }

    internal static InventoryRemovalResult Accepted(
        InventoryRemovalRequest request) =>
        new(request, true, null);

    internal static InventoryRemovalResult Rejected(
        InventoryRemovalRequest request,
        InventoryRemovalRejectionReason reason) =>
        new(request, false, reason);
}

public sealed partial class Inventory
{
    /// <summary>
    /// Returns whether moving all contents would collide with a discrete
    /// identity already held by the destination.
    /// </summary>
    internal bool HasDiscreteConflictWith(Inventory destination) =>
        _discreteItems.Keys.Any(destination._discreteItems.ContainsKey);

    /// <summary>
    /// Clears every legacy and generalized holding after the registry has
    /// proven that no live commitment can reference the contents.
    /// </summary>
    internal void ApplyDestroyAllContents()
    {
        _stored.Clear();
        TotalStored = Quantity.Zero;
        _fungibleStored.Clear();
        _discreteItems.Clear();
        _physicalUsedCapacity = Quantity.Zero;
    }

    /// <summary>
    /// Moves the complete prevalidated content set and empties this inventory.
    /// Reservation state is excluded because source commitments block removal.
    /// </summary>
    internal void ApplyMoveAllContentsTo(Inventory destination)
    {
        foreach ((MaterialId materialId, Quantity quantity) in _stored)
        {
            destination._stored[materialId] = destination.Stored(materialId).Add(quantity);
        }

        destination.TotalStored = destination.TotalStored.Add(TotalStored);
        foreach ((var definitionKey, Quantity quantity) in _fungibleStored)
        {
            destination._fungibleStored[definitionKey] =
                destination.FungibleStored(definitionKey).Add(quantity);
        }

        foreach ((ItemInstanceId instanceId, DiscreteItemInstance instance) in _discreteItems)
        {
            destination._discreteItems.Add(instanceId, instance);
        }

        destination._physicalUsedCapacity =
            destination._physicalUsedCapacity.Add(_physicalUsedCapacity);
        ApplyDestroyAllContents();
    }
}

public sealed partial class InventoryRegistry
{
    /// <summary>
    /// Removes one custody-aware inventory only after its complete destruction
    /// or transfer disposition can commit atomically.
    /// </summary>
    public InventoryRemovalResult RemovePhysicalInventory(
        InventoryRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Inventory? source = Get(request.InventoryId);
        if (source is null)
        {
            return Rejected(request, InventoryRemovalRejectionReason.MissingInventory);
        }

        if (source.Custody is null)
        {
            return Rejected(request, InventoryRemovalRejectionReason.MissingCustody);
        }

        if (source.HasCommitments)
        {
            return Rejected(
                request,
                InventoryRemovalRejectionReason.InventoryHasCommitments);
        }

        if (request.Disposition is InventoryRemovalDisposition.TransferContents transfer)
        {
            InventoryRemovalRejectionReason? rejection = ValidateTransferDisposition(
                source,
                transfer,
                out Inventory? destination);
            if (rejection is not null)
            {
                return Rejected(request, rejection.Value);
            }

            source.ApplyMoveAllContentsTo(destination!);
        }
        else
        {
            source.ApplyDestroyAllContents();
        }

        _inventories.Remove(source.Id);
        return InventoryRemovalResult.Accepted(request);
    }

    /// <summary>
    /// Validates the destination and all cross-inventory constraints before
    /// any source or destination holding changes.
    /// </summary>
    private InventoryRemovalRejectionReason? ValidateTransferDisposition(
        Inventory source,
        InventoryRemovalDisposition.TransferContents transfer,
        out Inventory? destination)
    {
        destination = null;
        if (transfer.DestinationInventoryId == source.Id)
        {
            return InventoryRemovalRejectionReason.SameDestination;
        }

        destination = Get(transfer.DestinationInventoryId);
        if (destination is null)
        {
            return InventoryRemovalRejectionReason.MissingDestinationInventory;
        }

        if (destination.Custody is null)
        {
            return InventoryRemovalRejectionReason.MissingCustody;
        }

        if (source.UsedCapacity > destination.RemainingCapacity)
        {
            return InventoryRemovalRejectionReason.InsufficientCapacity;
        }

        return source.HasDiscreteConflictWith(destination)
            ? InventoryRemovalRejectionReason.DestinationItemConflict
            : null;
    }

    private static InventoryRemovalResult Rejected(
        InventoryRemovalRequest request,
        InventoryRemovalRejectionReason reason) =>
        InventoryRemovalResult.Rejected(request, reason);
}
