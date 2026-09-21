using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Authoritative source domain for inventory mutation identities.</summary>
public enum InventoryOperationSourceKind
{
    Explicit,
}

/// <summary>Stable caller-supplied identity for one inventory mutation.</summary>
public readonly record struct InventoryOperationKey
{
    public InventoryOperationKey(InventoryOperationSourceKind sourceKind, ulong value)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        ArgumentOutOfRangeException.ThrowIfZero(value);
        SourceKind = sourceKind;
        Value = value;
    }

    public InventoryOperationSourceKind SourceKind { get; }

    public ulong Value { get; }
}

/// <summary>One independently evaluated mutation submitted for stable commit.</summary>
public abstract record InventoryMutationProposal(
    InventoryOperationKey Key,
    InventoryId InventoryId);

public sealed record StoreFungibleInventoryProposal : InventoryMutationProposal
{
    public StoreFungibleInventoryProposal(
        InventoryOperationKey key,
        InventoryId inventoryId,
        PhysicalDefinition definition,
        Quantity quantity)
        : base(key, inventoryId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        Quantity = quantity;
    }

    public PhysicalDefinition Definition { get; }

    public Quantity Quantity { get; }
}

public sealed record CreateDiscreteInventoryProposal : InventoryMutationProposal
{
    public CreateDiscreteInventoryProposal(
        InventoryOperationKey key,
        InventoryId inventoryId,
        PhysicalDefinition definition)
        : base(key, inventoryId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    public PhysicalDefinition Definition { get; }
}

public sealed record ReservePhysicalInventoryProposal : InventoryMutationProposal
{
    public ReservePhysicalInventoryProposal(
        InventoryOperationKey key,
        InventoryId inventoryId,
        PhysicalReservationSubject subject,
        ReservationOwner owner)
        : base(key, inventoryId)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(owner);
        Subject = subject;
        Owner = owner;
    }

    public PhysicalReservationSubject Subject { get; }

    public ReservationOwner Owner { get; }
}

public sealed record ReleasePhysicalReservationProposal : InventoryMutationProposal
{
    public ReleasePhysicalReservationProposal(
        InventoryOperationKey key,
        InventoryId inventoryId,
        ReservationId reservationId,
        ReservationOwner expectedOwner)
        : base(key, inventoryId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(reservationId.Value);
        ArgumentNullException.ThrowIfNull(expectedOwner);
        ReservationId = reservationId;
        ExpectedOwner = expectedOwner;
    }

    public ReservationId ReservationId { get; }

    public ReservationOwner ExpectedOwner { get; }
}

public sealed record TransferPhysicalInventoryProposal : InventoryMutationProposal
{
    public TransferPhysicalInventoryProposal(
        InventoryOperationKey key,
        PhysicalTransferRequest request)
        : base(key, request?.SourceInventoryId ?? default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public PhysicalTransferRequest Request { get; }
}

public sealed record RemovePhysicalInventoryProposal : InventoryMutationProposal
{
    public RemovePhysicalInventoryProposal(
        InventoryOperationKey key,
        InventoryRemovalRequest request)
        : base(key, request?.InventoryId ?? default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public InventoryRemovalRequest Request { get; }
}

/// <summary>Stable reasons that deterministic inventory commit can reject.</summary>
public enum InventoryCommitRejectionReason
{
    DuplicateOperationKey,
    OperationIdentityConflict,
    UnknownInventory,
    IdentifierCapacityExhausted,
    StorageRejected,
    ReservationRejected,
    TransferRejected,
    RemovalRejected,
}

/// <summary>Typed result of one proposal after deterministic commit.</summary>
public abstract record InventoryMutationOutcome(InventoryOperationKey Key)
{
    public sealed record StoredFungible(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        QualifiedContentKey DefinitionKey,
        Quantity Quantity)
        : InventoryMutationOutcome(Key);

    public sealed record CreatedDiscrete(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        DiscreteItemInstance Item)
        : InventoryMutationOutcome(Key);

    public sealed record Reserved(
        InventoryOperationKey Key,
        PhysicalReservation Reservation)
        : InventoryMutationOutcome(Key);

    public sealed record ReleasedReservation(
        InventoryOperationKey Key,
        PhysicalReservation Reservation)
        : InventoryMutationOutcome(Key);

    public sealed record Transferred(
        InventoryOperationKey Key,
        InventoryId SourceInventoryId,
        InventoryId DestinationInventoryId,
        QualifiedContentKey DefinitionKey,
        PhysicalTransferSubject Subject,
        ReservationId? SourceReservationId,
        ReservationId? DestinationCapacityReservationId,
        ReservationOwner? ReservationOwner)
        : InventoryMutationOutcome(Key);

    public sealed record Removed(
        InventoryOperationKey Key,
        InventoryRemovalRequest Request)
        : InventoryMutationOutcome(Key);

    public sealed record Rejected(
        InventoryOperationKey Key,
        InventoryCommitRejectionReason Reason,
        InventoryStorageRejectionReason? StorageReason = null,
        InventoryReservationRejectionReason? ReservationReason = null,
        InventoryTransferRejectionReason? TransferReason = null,
        InventoryRemovalRejectionReason? RemovalReason = null)
        : InventoryMutationOutcome(Key);
}

/// <summary>One outcome plus whether this call applied its mutation.</summary>
public sealed record InventoryCommitDisposition(
    InventoryMutationOutcome Outcome,
    bool WasApplied);

/// <summary>Canonical outcomes for one submitted collection of proposals.</summary>
public sealed class InventoryCommitBatchResult
{
    internal InventoryCommitBatchResult(IEnumerable<InventoryCommitDisposition> outcomes)
    {
        Outcomes = new ReadOnlyCollection<InventoryCommitDisposition>(outcomes.ToArray());
    }

    public IReadOnlyList<InventoryCommitDisposition> Outcomes { get; }
}

internal abstract record InventoryMutationProposalCheckpoint(
    InventoryOperationKey Key,
    InventoryId InventoryId)
{
    internal sealed record StoreFungible(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        QualifiedContentKey DefinitionKey,
        Quantity Quantity)
        : InventoryMutationProposalCheckpoint(Key, InventoryId);

    internal sealed record CreateDiscrete(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        QualifiedContentKey DefinitionKey)
        : InventoryMutationProposalCheckpoint(Key, InventoryId);

    internal sealed record ReservePhysical(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        PhysicalReservationSubject Subject,
        ReservationOwner Owner)
        : InventoryMutationProposalCheckpoint(Key, InventoryId);

    internal sealed record ReleasePhysicalReservation(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        ReservationId ReservationId,
        ReservationOwner ExpectedOwner)
        : InventoryMutationProposalCheckpoint(Key, InventoryId);

    internal sealed record TransferPhysical(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        InventoryId DestinationInventoryId,
        QualifiedContentKey DefinitionKey,
        PhysicalTransferSubject Subject,
        ReservationId? SourceReservationId,
        ReservationId? DestinationCapacityReservationId,
        ReservationOwner? ReservationOwner)
        : InventoryMutationProposalCheckpoint(Key, InventoryId);

    internal sealed record RemovePhysical(
        InventoryOperationKey Key,
        InventoryId InventoryId,
        InventoryRemovalDisposition Disposition)
        : InventoryMutationProposalCheckpoint(Key, InventoryId);
}

internal sealed record InventoryCommitReceiptCheckpoint(
    InventoryMutationProposalCheckpoint Proposal,
    InventoryMutationOutcome Outcome);

internal sealed class InventoryCommitOwnerCheckpoint
{
    internal InventoryCommitOwnerCheckpoint(
        IdSequenceCheckpoint itemInstanceIds,
        IdSequenceCheckpoint reservationIds,
        IEnumerable<InventoryCommitReceiptCheckpoint> receipts)
    {
        ItemInstanceIds = itemInstanceIds;
        ReservationIds = reservationIds;
        Receipts = new ReadOnlyCollection<InventoryCommitReceiptCheckpoint>(receipts.ToArray());
    }

    internal IdSequenceCheckpoint ItemInstanceIds { get; }

    internal IdSequenceCheckpoint ReservationIds { get; }

    internal IReadOnlyList<InventoryCommitReceiptCheckpoint> Receipts { get; }
}

/// <summary>
/// Single authoritative commit owner for generalized inventory mutations and
/// the identity sequences those mutations consume.
/// </summary>
public sealed class InventoryCommitOwner
{
    private readonly InventoryRegistry _inventories;
    private readonly IdSequence<ItemInstanceId> _itemInstanceIds;
    private readonly IdSequence<ReservationId> _reservationIds;
    private readonly Dictionary<InventoryOperationKey, CommittedMutation> _receipts;

    public InventoryCommitOwner(InventoryRegistry inventories)
        : this(
            inventories,
            CreateItemSequence(inventories),
            CreateReservationSequence(inventories))
    {
    }

    internal InventoryCommitOwner(
        InventoryRegistry inventories,
        IdSequence<ItemInstanceId> itemInstanceIds,
        IdSequence<ReservationId> reservationIds)
        : this(inventories, itemInstanceIds, reservationIds, [])
    {
    }

    private InventoryCommitOwner(
        InventoryRegistry inventories,
        IdSequence<ItemInstanceId> itemInstanceIds,
        IdSequence<ReservationId> reservationIds,
        Dictionary<InventoryOperationKey, CommittedMutation> receipts)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(itemInstanceIds);
        ArgumentNullException.ThrowIfNull(reservationIds);
        _inventories = inventories;
        _itemInstanceIds = itemInstanceIds;
        _reservationIds = reservationIds;
        _receipts = receipts;
    }

    /// <summary>
    /// Commits proposals in stable domain-key order. Duplicate keys collapse
    /// to one rejection because their arrival order cannot break the tie.
    /// </summary>
    public InventoryCommitBatchResult CommitBatch(
        IEnumerable<InventoryMutationProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        InventoryMutationProposal[] submitted = proposals.ToArray();
        foreach (InventoryMutationProposal proposal in submitted)
        {
            ArgumentNullException.ThrowIfNull(proposal);
        }

        var outcomes = new List<InventoryCommitDisposition>();
        foreach (IGrouping<InventoryOperationKey, InventoryMutationProposal> group in submitted
                     .OrderBy(proposal => proposal.Key.SourceKind)
                     .ThenBy(proposal => proposal.Key.Value)
                     .GroupBy(proposal => proposal.Key))
        {
            if (group.Skip(1).Any())
            {
                outcomes.Add(Rejected(
                    group.Key,
                    InventoryCommitRejectionReason.DuplicateOperationKey));
                continue;
            }

            outcomes.Add(Commit(group.First()));
        }

        return new InventoryCommitBatchResult(outcomes);
    }

    internal InventoryCommitOwnerCheckpoint CaptureCheckpoint() =>
        new(
            _itemInstanceIds.CaptureCheckpoint(),
            _reservationIds.CaptureCheckpoint(),
            _receipts.Values
                .OrderBy(receipt => receipt.Proposal.Key.SourceKind)
                .ThenBy(receipt => receipt.Proposal.Key.Value)
                .Select(receipt => new InventoryCommitReceiptCheckpoint(
                    ToCheckpoint(receipt.Proposal),
                    receipt.Outcome)));

    internal static CheckpointResult<InventoryCommitOwner> RestoreCheckpoint(
        InventoryCommitOwnerCheckpoint checkpoint,
        InventoryRegistry inventories,
        PhysicalDefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(definitions);
        CheckpointResult<IdSequence<ItemInstanceId>> itemIds =
            IdSequence<ItemInstanceId>.RestoreCheckpoint(checkpoint.ItemInstanceIds);
        CheckpointResult<IdSequence<ReservationId>> reservationIds =
            IdSequence<ReservationId>.RestoreCheckpoint(checkpoint.ReservationIds);
        if (!itemIds.IsSuccess || !reservationIds.IsSuccess)
        {
            return RejectedCheckpoint("allocators", "An inventory allocator checkpoint is invalid.");
        }


        if (inventories.PhysicalItemIds.Any(id =>
                !WasAllocated(id.Value, checkpoint.ItemInstanceIds))
            || inventories.PhysicalReservationIds.Any(id =>
                !WasAllocated(id.Value, checkpoint.ReservationIds)))
        {
            return RejectedCheckpoint(
                "allocators",
                "An inventory allocator does not cover a live physical identity.");
        }

        var receipts = new Dictionary<InventoryOperationKey, CommittedMutation>();
        var allocatedItemIds = new HashSet<ItemInstanceId>();
        var allocatedReservationIds = new HashSet<ReservationId>();
        InventoryOperationKey? previous = null;
        for (int index = 0; index < checkpoint.Receipts.Count; index++)
        {
            InventoryCommitReceiptCheckpoint? saved = checkpoint.Receipts[index];
            string path = $"receipts[{index}]";
            if (saved is null || saved.Proposal is null || saved.Outcome is null)
            {
                return RejectedCheckpoint(path, "An inventory commit receipt is missing.");
            }

            InventoryOperationKey key = saved.Proposal.Key;
            if (key.Value == 0 || !Enum.IsDefined(key.SourceKind) || saved.Outcome.Key != key)
            {
                return RejectedCheckpoint(path, "An inventory commit receipt identity is invalid.");
            }

            if (previous is { } prior && Compare(prior, key) >= 0)
            {
                return RejectedCheckpoint(path, "Inventory commit receipts are not in canonical order.");
            }

            CheckpointResult<InventoryMutationProposal> proposal = RestoreProposal(
                saved.Proposal,
                definitions,
                path);
            if (!proposal.IsSuccess || !OutcomeMatches(proposal.Value!, saved.Outcome, checkpoint))
            {
                return RejectedCheckpoint(path, "An inventory commit receipt is inconsistent.");
            }

            if (saved.Outcome is InventoryMutationOutcome.CreatedDiscrete created
                && !allocatedItemIds.Add(created.Item.Id)
                || saved.Outcome is InventoryMutationOutcome.Reserved reserved
                && !allocatedReservationIds.Add(reserved.Reservation.Id))
            {
                return RejectedCheckpoint(path, "An allocated inventory identity is duplicated.");
            }

            receipts.Add(key, new CommittedMutation(proposal.Value!, saved.Outcome));
            previous = key;
        }

        return CheckpointResult<InventoryCommitOwner>.Success(
            new InventoryCommitOwner(
                inventories,
                itemIds.Value!,
                reservationIds.Value!,
                receipts));
    }

    private InventoryCommitDisposition Commit(InventoryMutationProposal proposal)
    {
        if (_receipts.TryGetValue(proposal.Key, out CommittedMutation? prior))
        {
            return prior.Proposal == proposal
                ? new InventoryCommitDisposition(prior.Outcome, WasApplied: false)
                : Rejected(
                    proposal.Key,
                    InventoryCommitRejectionReason.OperationIdentityConflict);
        }

        InventoryCommitDisposition disposition = proposal switch
        {
            StoreFungibleInventoryProposal store => CommitStore(store),
            CreateDiscreteInventoryProposal create => CommitCreate(create),
            ReservePhysicalInventoryProposal reserve => CommitReservation(reserve),
            ReleasePhysicalReservationProposal release =>
                CommitReservationRelease(release),
            TransferPhysicalInventoryProposal transfer => CommitTransfer(transfer),
            RemovePhysicalInventoryProposal remove => CommitRemoval(remove),
            _ => throw new InvalidOperationException(
                $"Unsupported inventory proposal type {proposal.GetType().Name}."),
        };
        if (disposition.WasApplied)
        {
            _receipts.Add(
                proposal.Key,
                new CommittedMutation(proposal, disposition.Outcome));
        }

        return disposition;
    }

    private InventoryCommitDisposition CommitStore(
        StoreFungibleInventoryProposal proposal)
    {
        Inventory? inventory = _inventories.Get(proposal.InventoryId);
        if (inventory is null)
        {
            return Rejected(proposal.Key, InventoryCommitRejectionReason.UnknownInventory);
        }

        InventoryStorageResult result = inventory.StoreFungible(
            proposal.Definition,
            proposal.Quantity);
        return result.IsAccepted
            ? Applied(new InventoryMutationOutcome.StoredFungible(
                proposal.Key,
                inventory.Id,
                proposal.Definition.Key,
                proposal.Quantity))
            : Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.StorageRejected,
                result.RejectionReason);
    }

    private InventoryCommitDisposition CommitCreate(
        CreateDiscreteInventoryProposal proposal)
    {
        Inventory? inventory = _inventories.Get(proposal.InventoryId);
        if (inventory is null)
        {
            return Rejected(proposal.Key, InventoryCommitRejectionReason.UnknownInventory);
        }

        if (!_itemInstanceIds.TryPeek(out ItemInstanceId instanceId))
        {
            return Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.IdentifierCapacityExhausted);
        }

        InventoryStorageResult result = inventory.StoreDiscrete(
            proposal.Definition,
            instanceId);
        if (!result.IsAccepted)
        {
            return Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.StorageRejected,
                result.RejectionReason);
        }

        ItemInstanceId allocated = _itemInstanceIds.Allocate();
        if (allocated != instanceId)
        {
            throw new InvalidOperationException("Prepared item identity changed before commit.");
        }

        return Applied(new InventoryMutationOutcome.CreatedDiscrete(
            proposal.Key,
            inventory.Id,
            inventory.GetDiscrete(instanceId)!));
    }

    private InventoryCommitDisposition CommitReservation(
        ReservePhysicalInventoryProposal proposal)
    {
        Inventory? inventory = _inventories.Get(proposal.InventoryId);
        if (inventory is null)
        {
            return Rejected(proposal.Key, InventoryCommitRejectionReason.UnknownInventory);
        }

        if (!_reservationIds.TryPeek(out ReservationId reservationId))
        {
            return Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.IdentifierCapacityExhausted);
        }

        InventoryReservationResult result = inventory.ReservePhysical(
            reservationId,
            proposal.Subject,
            proposal.Owner);
        if (!result.IsAccepted)
        {
            return Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.ReservationRejected,
                reservationReason: result.RejectionReason);
        }

        ReservationId allocated = _reservationIds.Allocate();
        if (allocated != reservationId)
        {
            throw new InvalidOperationException("Prepared reservation identity changed before commit.");
        }

        return Applied(new InventoryMutationOutcome.Reserved(
            proposal.Key,
            result.Reservation!));
    }

    private InventoryCommitDisposition CommitReservationRelease(
        ReleasePhysicalReservationProposal proposal)
    {
        Inventory? inventory = _inventories.Get(proposal.InventoryId);
        if (inventory is null)
        {
            return Rejected(proposal.Key, InventoryCommitRejectionReason.UnknownInventory);
        }

        InventoryReservationResult result = inventory.ReleasePhysicalReservation(
            proposal.ReservationId,
            proposal.ExpectedOwner);
        return result.IsAccepted
            ? Applied(new InventoryMutationOutcome.ReleasedReservation(
                proposal.Key,
                result.Reservation!))
            : Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.ReservationRejected,
                reservationReason: result.RejectionReason);
    }

    private InventoryCommitDisposition CommitTransfer(
        TransferPhysicalInventoryProposal proposal)
    {
        InventoryTransferResult result = _inventories.TransferPhysical(proposal.Request);
        if (!result.IsAccepted)
        {
            return Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.TransferRejected,
                transferReason: result.RejectionReason);
        }

        PhysicalTransferRequest request = proposal.Request;
        return Applied(new InventoryMutationOutcome.Transferred(
            proposal.Key,
            request.SourceInventoryId,
            request.DestinationInventoryId,
            request.Definition.Key,
            request.Subject,
            request.SourceReservationId,
            request.DestinationCapacityReservationId,
            request.ReservationOwner));
    }

    private InventoryCommitDisposition CommitRemoval(
        RemovePhysicalInventoryProposal proposal)
    {
        InventoryRemovalResult result =
            _inventories.RemovePhysicalInventory(proposal.Request);
        return result.IsAccepted
            ? Applied(new InventoryMutationOutcome.Removed(
                proposal.Key,
                proposal.Request))
            : Rejected(
                proposal.Key,
                InventoryCommitRejectionReason.RemovalRejected,
                removalReason: result.RejectionReason);
    }

    private static InventoryCommitDisposition Applied(InventoryMutationOutcome outcome) =>
        new(outcome, WasApplied: true);

    private static InventoryCommitDisposition Rejected(
        InventoryOperationKey key,
        InventoryCommitRejectionReason reason,
        InventoryStorageRejectionReason? storageReason = null,
        InventoryReservationRejectionReason? reservationReason = null,
        InventoryTransferRejectionReason? transferReason = null,
        InventoryRemovalRejectionReason? removalReason = null) =>
        new(
            new InventoryMutationOutcome.Rejected(
                key,
                reason,
                storageReason,
                reservationReason,
                transferReason,
                removalReason),
            WasApplied: false);

    private static InventoryMutationProposalCheckpoint ToCheckpoint(
        InventoryMutationProposal proposal) =>
        proposal switch
        {
            StoreFungibleInventoryProposal store =>
                new InventoryMutationProposalCheckpoint.StoreFungible(
                    store.Key,
                    store.InventoryId,
                    store.Definition.Key,
                    store.Quantity),
            CreateDiscreteInventoryProposal create =>
                new InventoryMutationProposalCheckpoint.CreateDiscrete(
                    create.Key,
                    create.InventoryId,
                    create.Definition.Key),
            ReservePhysicalInventoryProposal reserve =>
                new InventoryMutationProposalCheckpoint.ReservePhysical(
                    reserve.Key,
                    reserve.InventoryId,
                    reserve.Subject,
                    reserve.Owner),
            ReleasePhysicalReservationProposal release =>
                new InventoryMutationProposalCheckpoint.ReleasePhysicalReservation(
                    release.Key,
                    release.InventoryId,
                    release.ReservationId,
                    release.ExpectedOwner),
            TransferPhysicalInventoryProposal transfer =>
                new InventoryMutationProposalCheckpoint.TransferPhysical(
                    transfer.Key,
                    transfer.Request.SourceInventoryId,
                    transfer.Request.DestinationInventoryId,
                    transfer.Request.Definition.Key,
                    transfer.Request.Subject,
                    transfer.Request.SourceReservationId,
                    transfer.Request.DestinationCapacityReservationId,
                    transfer.Request.ReservationOwner),
            RemovePhysicalInventoryProposal remove =>
                new InventoryMutationProposalCheckpoint.RemovePhysical(
                    remove.Key,
                    remove.Request.InventoryId,
                    remove.Request.Disposition),
            _ => throw new InvalidOperationException(
                $"Unsupported inventory proposal type {proposal.GetType().Name}."),
        };

    private static CheckpointResult<InventoryMutationProposal> RestoreProposal(
        InventoryMutationProposalCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        string path)
    {
        InventoryMutationProposal? proposal = checkpoint switch
        {
            InventoryMutationProposalCheckpoint.StoreFungible store
                when store.InventoryId.Value != 0
                     && store.DefinitionKey is not null
                     && store.Quantity != Quantity.Zero
                     && definitions.Get(store.DefinitionKey) is
                     { HoldingKind: PhysicalHoldingKind.Fungible } definition =>
                new StoreFungibleInventoryProposal(
                    store.Key,
                    store.InventoryId,
                    definition,
                    store.Quantity),
            InventoryMutationProposalCheckpoint.CreateDiscrete create
                when create.InventoryId.Value != 0
                     && create.DefinitionKey is not null
                     && definitions.Get(create.DefinitionKey) is
                     { HoldingKind: PhysicalHoldingKind.Discrete } definition =>
                new CreateDiscreteInventoryProposal(
                    create.Key,
                    create.InventoryId,
                    definition),
            InventoryMutationProposalCheckpoint.ReservePhysical reserve
                when reserve.InventoryId.Value != 0
                     && IsValidReservationSubject(reserve.Subject)
                     && IsValidOwner(reserve.Owner) =>
                new ReservePhysicalInventoryProposal(
                    reserve.Key,
                    reserve.InventoryId,
                    reserve.Subject,
                    reserve.Owner),
            InventoryMutationProposalCheckpoint.ReleasePhysicalReservation release
                when release.InventoryId.Value != 0
                     && release.ReservationId.Value != 0
                     && IsValidOwner(release.ExpectedOwner) =>
                new ReleasePhysicalReservationProposal(
                    release.Key,
                    release.InventoryId,
                    release.ReservationId,
                    release.ExpectedOwner),
            InventoryMutationProposalCheckpoint.TransferPhysical transfer
                when transfer.InventoryId.Value != 0
                     && transfer.DestinationInventoryId.Value != 0
                     && transfer.InventoryId != transfer.DestinationInventoryId
                     && transfer.DefinitionKey is not null
                     && transfer.Subject is not null
                     && transfer.SourceReservationId is not { Value: 0 }
                     && transfer.DestinationCapacityReservationId is not { Value: 0 }
                     && (transfer.ReservationOwner is null
                         || IsValidOwner(transfer.ReservationOwner))
                     && definitions.Get(transfer.DefinitionKey) is { } definition
                     && IsCompatibleTransfer(definition, transfer.Subject) =>
                new TransferPhysicalInventoryProposal(
                    transfer.Key,
                    new PhysicalTransferRequest(
                        transfer.InventoryId,
                        transfer.DestinationInventoryId,
                        definition,
                        transfer.Subject,
                        transfer.SourceReservationId,
                        transfer.DestinationCapacityReservationId,
                        transfer.ReservationOwner)),
            InventoryMutationProposalCheckpoint.RemovePhysical remove
                when remove.InventoryId.Value != 0
                     && IsValidRemovalDisposition(
                         remove.InventoryId,
                         remove.Disposition) =>
                new RemovePhysicalInventoryProposal(
                    remove.Key,
                    new InventoryRemovalRequest(
                        remove.InventoryId,
                        remove.Disposition)),
            _ => null,
        };
        return proposal is null
            ? CheckpointResult<InventoryMutationProposal>.Rejected(
                new CheckpointValidationFailure(
                    $"$.checkpoint.inventoryCommit.{path}.proposal",
                    "An inventory receipt proposal cannot be restored."))
            : CheckpointResult<InventoryMutationProposal>.Success(proposal);
    }

    private static bool OutcomeMatches(
        InventoryMutationProposal proposal,
        InventoryMutationOutcome outcome,
        InventoryCommitOwnerCheckpoint checkpoint) =>
        (proposal, outcome) switch
        {
            (StoreFungibleInventoryProposal store, InventoryMutationOutcome.StoredFungible saved) =>
                saved.InventoryId == store.InventoryId
                && saved.DefinitionKey == store.Definition.Key
                && saved.Quantity == store.Quantity,
            (CreateDiscreteInventoryProposal create, InventoryMutationOutcome.CreatedDiscrete saved) =>
                saved.InventoryId == create.InventoryId
                && saved.Item is not null
                && saved.Item.DefinitionKey == create.Definition.Key
                && WasAllocated(saved.Item.Id.Value, checkpoint.ItemInstanceIds),
            (ReservePhysicalInventoryProposal reserve, InventoryMutationOutcome.Reserved saved) =>
                saved.Reservation is not null
                && saved.Reservation.InventoryId == reserve.InventoryId
                && saved.Reservation.Subject == reserve.Subject
                && saved.Reservation.Owner == reserve.Owner
                && WasAllocated(saved.Reservation.Id.Value, checkpoint.ReservationIds),
            (ReleasePhysicalReservationProposal release,
                InventoryMutationOutcome.ReleasedReservation saved) =>
                saved.Reservation is not null
                && saved.Reservation.Id == release.ReservationId
                && saved.Reservation.InventoryId == release.InventoryId
                && saved.Reservation.Owner == release.ExpectedOwner
                && IsValidReservationSubject(saved.Reservation.Subject)
                && WasAllocated(saved.Reservation.Id.Value, checkpoint.ReservationIds),
            (TransferPhysicalInventoryProposal transfer, InventoryMutationOutcome.Transferred saved) =>
                saved.SourceInventoryId == transfer.Request.SourceInventoryId
                && saved.DestinationInventoryId == transfer.Request.DestinationInventoryId
                && saved.DefinitionKey == transfer.Request.Definition.Key
                && saved.Subject == transfer.Request.Subject
                && saved.SourceReservationId == transfer.Request.SourceReservationId
                && saved.DestinationCapacityReservationId
                    == transfer.Request.DestinationCapacityReservationId
                && saved.ReservationOwner == transfer.Request.ReservationOwner,
            (RemovePhysicalInventoryProposal remove, InventoryMutationOutcome.Removed saved) =>
                saved.Request == remove.Request,
            _ => false,
        };

    private static bool WasAllocated(ulong value, IdSequenceCheckpoint sequence) =>
        value != 0 && (sequence.NextValue is not { } next || value < next);

    private static IdSequence<ItemInstanceId> CreateItemSequence(
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        var sequence = new IdSequence<ItemInstanceId>();
        foreach (ItemInstanceId id in inventories.PhysicalItemIds)
        {
            sequence.AdvancePast(id);
        }

        return sequence;
    }

    private static IdSequence<ReservationId> CreateReservationSequence(
        InventoryRegistry inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        var sequence = new IdSequence<ReservationId>();
        foreach (ReservationId id in inventories.PhysicalReservationIds)
        {
            sequence.AdvancePast(id);
        }

        return sequence;
    }

    private static bool IsValidOwner(ReservationOwner? owner) => owner switch
    {
        ReservationOwner.TransportJob transport => transport.JobId.Value != 0,
        ReservationOwner.ProductionJob production => production.JobId.Value != 0,
        ReservationOwner.ConstructionOrder construction => construction.OrderId.Value != 0,
        _ => false,
    };

    private static bool IsCompatibleTransfer(
        PhysicalDefinition definition,
        PhysicalTransferSubject subject) =>
        (definition.HoldingKind, subject) switch
        {
            (PhysicalHoldingKind.Fungible, PhysicalTransferSubject.Fungible fungible) =>
                fungible.DefinitionKey == definition.Key
                && fungible.Quantity != Quantity.Zero,
            (PhysicalHoldingKind.Discrete, PhysicalTransferSubject.Discrete discrete) =>
                discrete.InstanceId.Value != 0,
            _ => false,
        };

    private static bool IsValidReservationSubject(
        PhysicalReservationSubject? subject) => subject switch
        {
            PhysicalReservationSubject.Fungible fungible =>
                fungible.DefinitionKey is not null
                && fungible.Quantity != Quantity.Zero,
            PhysicalReservationSubject.Discrete discrete =>
                discrete.InstanceId.Value != 0,
            PhysicalReservationSubject.IncomingCapacity incoming =>
                incoming.Quantity != Quantity.Zero,
            _ => false,
        };

    private static bool IsValidRemovalDisposition(
        InventoryId inventoryId,
        InventoryRemovalDisposition? disposition) => disposition switch
        {
            InventoryRemovalDisposition.DestroyContents => true,
            InventoryRemovalDisposition.TransferContents transfer =>
                transfer.DestinationInventoryId.Value != 0
                && transfer.DestinationInventoryId != inventoryId,
            _ => false,
        };

    private static int Compare(InventoryOperationKey left, InventoryOperationKey right)
    {
        int source = left.SourceKind.CompareTo(right.SourceKind);
        return source != 0 ? source : left.Value.CompareTo(right.Value);
    }

    private static CheckpointResult<InventoryCommitOwner> RejectedCheckpoint(
        string path,
        string message) =>
        CheckpointResult<InventoryCommitOwner>.Rejected(
            new CheckpointValidationFailure(
                $"$.checkpoint.inventoryCommit.{path}",
                message));

    private sealed record CommittedMutation(
        InventoryMutationProposal Proposal,
        InventoryMutationOutcome Outcome);
}

public sealed partial class InventoryRegistry
{
    internal IEnumerable<ItemInstanceId> PhysicalItemIds =>
        _inventories.Values.SelectMany(inventory => inventory.DiscreteItemIds);

    internal IEnumerable<ReservationId> PhysicalReservationIds =>
        _inventories.Values.SelectMany(inventory => inventory.PhysicalReservationIds);
}
