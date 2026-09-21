using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalInventoryCommitTests
{
    [Fact]
    public void CommitOrdersContendingProposalsByStableOperationKey()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 1);
        InventoryMutationProposal first = new StoreFungibleInventoryProposal(
            Key(1), new InventoryId(1), ore, new Quantity(6));
        InventoryMutationProposal second = new StoreFungibleInventoryProposal(
            Key(2), new InventoryId(1), ore, new Quantity(6));

        (InventoryCommitBatchResult forward, Inventory forwardInventory) =
            Commit([first, second], ore, 10);
        (InventoryCommitBatchResult reversed, Inventory reversedInventory) =
            Commit([second, first], ore, 10);

        Assert.Equal(forward.Outcomes, reversed.Outcomes);
        Assert.Equal<ulong>(6, forwardInventory.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(6, reversedInventory.FungibleStored(ore.Key).Units);
        Assert.IsType<InventoryMutationOutcome.StoredFungible>(forward.Outcomes[0].Outcome);
        InventoryMutationOutcome.Rejected rejected = Assert.IsType<InventoryMutationOutcome.Rejected>(
            forward.Outcomes[1].Outcome);
        Assert.Equal(InventoryCommitRejectionReason.StorageRejected, rejected.Reason);
        Assert.Equal(InventoryStorageRejectionReason.InsufficientCapacity, rejected.StorageReason);
    }

    [Fact]
    public void RejectedProposalDoesNotConsumeItemIdentity()
    {
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);

        InventoryCommitBatchResult result = owner.CommitBatch([
            new CreateDiscreteInventoryProposal(Key(1), new InventoryId(99), sensor),
            new CreateDiscreteInventoryProposal(Key(2), inventory.Id, sensor),
        ]);

        Assert.IsType<InventoryMutationOutcome.Rejected>(result.Outcomes[0].Outcome);
        InventoryMutationOutcome.CreatedDiscrete created =
            Assert.IsType<InventoryMutationOutcome.CreatedDiscrete>(result.Outcomes[1].Outcome);
        Assert.Equal(new ItemInstanceId(1), created.Item.Id);
    }

    [Fact]
    public void RejectedProposalDoesNotConsumeReservationIdentity()
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);

        InventoryCommitBatchResult result = owner.CommitBatch([
            new ReservePhysicalInventoryProposal(
                Key(1),
                inventory.Id,
                new PhysicalReservationSubject.Discrete(new ItemInstanceId(99)),
                Owner(1)),
            new ReservePhysicalInventoryProposal(
                Key(2),
                inventory.Id,
                new PhysicalReservationSubject.IncomingCapacity(new Quantity(2)),
                Owner(1)),
        ]);

        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(result.Outcomes[0].Outcome);
        Assert.Equal(
            InventoryReservationRejectionReason.MissingItemInstance,
            rejected.ReservationReason);
        InventoryMutationOutcome.Reserved reserved =
            Assert.IsType<InventoryMutationOutcome.Reserved>(result.Outcomes[1].Outcome);
        Assert.Equal(new ReservationId(1), reserved.Reservation.Id);
    }

    [Fact]
    public void NewOwnerAdvancesPastLivePhysicalIdentities()
    {
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(20);
        registry.Add(inventory);
        Assert.True(inventory.StoreDiscrete(sensor, new ItemInstanceId(9)).IsAccepted);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(12),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            Owner(1)).IsAccepted);
        var owner = new InventoryCommitOwner(registry);

        InventoryCommitBatchResult result = owner.CommitBatch([
            new CreateDiscreteInventoryProposal(Key(1), inventory.Id, sensor),
            new ReservePhysicalInventoryProposal(
                Key(2),
                inventory.Id,
                new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
                Owner(2)),
        ]);

        InventoryMutationOutcome.CreatedDiscrete created =
            Assert.IsType<InventoryMutationOutcome.CreatedDiscrete>(result.Outcomes[0].Outcome);
        InventoryMutationOutcome.Reserved reserved =
            Assert.IsType<InventoryMutationOutcome.Reserved>(result.Outcomes[1].Outcome);
        Assert.Equal(new ItemInstanceId(10), created.Item.Id);
        Assert.Equal(new ReservationId(13), reserved.Reservation.Id);
    }

    [Fact]
    public void DuplicateOperationKeyRejectsWithoutMutationOrAllocation()
    {
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);

        InventoryCommitBatchResult duplicate = owner.CommitBatch([
            new CreateDiscreteInventoryProposal(Key(1), inventory.Id, sensor),
            new CreateDiscreteInventoryProposal(Key(1), inventory.Id, sensor),
        ]);
        InventoryCommitBatchResult accepted = owner.CommitBatch([
            new CreateDiscreteInventoryProposal(Key(2), inventory.Id, sensor),
        ]);

        InventoryCommitDisposition only = Assert.Single(duplicate.Outcomes);
        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(only.Outcome);
        Assert.Equal(InventoryCommitRejectionReason.DuplicateOperationKey, rejected.Reason);
        Assert.False(only.WasApplied);
        InventoryMutationOutcome.CreatedDiscrete created =
            Assert.IsType<InventoryMutationOutcome.CreatedDiscrete>(Assert.Single(accepted.Outcomes).Outcome);
        Assert.Equal(new ItemInstanceId(1), created.Item.Id);
    }

    [Fact]
    public void ExactReplayReturnsReceiptAndDifferentContentConflicts()
    {
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);
        var proposal = new CreateDiscreteInventoryProposal(Key(1), inventory.Id, sensor);

        InventoryCommitDisposition applied = Assert.Single(owner.CommitBatch([proposal]).Outcomes);
        InventoryCommitDisposition replayed = Assert.Single(owner.CommitBatch([proposal]).Outcomes);
        InventoryCommitDisposition conflict = Assert.Single(owner.CommitBatch([
            proposal with { InventoryId = new InventoryId(2) },
        ]).Outcomes);

        Assert.True(applied.WasApplied);
        Assert.False(replayed.WasApplied);
        Assert.Equal(applied.Outcome, replayed.Outcome);
        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(conflict.Outcome);
        Assert.Equal(InventoryCommitRejectionReason.OperationIdentityConflict, rejected.Reason);
    }

    [Fact]
    public void CheckpointPreservesReceiptAndAllocatorExhaustion()
    {
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        PhysicalDefinitionCatalog catalog = new([sensor]);
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(
            registry,
            IdSequence<ItemInstanceId>.RestoreCheckpoint(
                new IdSequenceCheckpoint(ulong.MaxValue)).Value!,
            new IdSequence<ReservationId>());
        var proposal = new CreateDiscreteInventoryProposal(Key(1), inventory.Id, sensor);
        InventoryCommitDisposition applied = Assert.Single(owner.CommitBatch([proposal]).Outcomes);

        InventoryCommitOwnerCheckpoint checkpoint = owner.CaptureCheckpoint();
        CheckpointResult<InventoryCommitOwner> restoration = InventoryCommitOwner.RestoreCheckpoint(
            checkpoint,
            registry,
            catalog);

        Assert.Null(checkpoint.ItemInstanceIds.NextValue);
        Assert.True(restoration.IsSuccess);
        InventoryCommitOwner restored = restoration.Value!;
        InventoryCommitDisposition replayed = Assert.Single(restored.CommitBatch([proposal]).Outcomes);
        InventoryCommitDisposition exhausted = Assert.Single(restored.CommitBatch([
            new CreateDiscreteInventoryProposal(Key(2), inventory.Id, sensor),
        ]).Outcomes);
        Assert.False(replayed.WasApplied);
        Assert.Equal(applied.Outcome, replayed.Outcome);
        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(exhausted.Outcome);
        Assert.Equal(InventoryCommitRejectionReason.IdentifierCapacityExhausted, rejected.Reason);
    }

    [Fact]
    public void RestoreRejectsReceiptsThatReuseAnAllocatedIdentity()
    {
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        PhysicalDefinitionCatalog catalog = new([sensor]);
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);
        owner.CommitBatch([
            new CreateDiscreteInventoryProposal(Key(1), inventory.Id, sensor),
            new CreateDiscreteInventoryProposal(Key(2), inventory.Id, sensor),
        ]);
        InventoryCommitOwnerCheckpoint valid = owner.CaptureCheckpoint();
        InventoryMutationOutcome.CreatedDiscrete first =
            Assert.IsType<InventoryMutationOutcome.CreatedDiscrete>(valid.Receipts[0].Outcome);
        InventoryCommitReceiptCheckpoint second = valid.Receipts[1] with
        {
            Outcome = new InventoryMutationOutcome.CreatedDiscrete(
                Key(2),
                inventory.Id,
                first.Item),
        };
        var corrupt = new InventoryCommitOwnerCheckpoint(
            valid.ItemInstanceIds,
            valid.ReservationIds,
            [valid.Receipts[0], second]);

        CheckpointResult<InventoryCommitOwner> restoration =
            InventoryCommitOwner.RestoreCheckpoint(corrupt, registry, catalog);

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventoryCommit.receipts[1]",
            restoration.Failure!.Path);
    }

    [Fact]
    public void TransferContentionUsesStableKeyOrderAcrossInputLayouts()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 1);

        (InventoryCommitBatchResult forward, Inventory forwardSecond, Inventory forwardThird) =
            CommitContendingTransfers(ore, reverseInput: false);
        (InventoryCommitBatchResult reversed, Inventory reversedSecond, Inventory reversedThird) =
            CommitContendingTransfers(ore, reverseInput: true);

        Assert.Equal(forward.Outcomes, reversed.Outcomes);
        Assert.Equal<ulong>(0, forwardSecond.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(4, forwardThird.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(0, reversedSecond.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(4, reversedThird.FungibleStored(ore.Key).Units);
        Assert.IsType<InventoryMutationOutcome.Transferred>(forward.Outcomes[0].Outcome);
        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(forward.Outcomes[1].Outcome);
        Assert.Equal(InventoryCommitRejectionReason.TransferRejected, rejected.Reason);
        Assert.Equal(
            InventoryTransferRejectionReason.InsufficientAvailableQuantity,
            rejected.TransferReason);
    }

    [Fact]
    public void TransferReceiptRoundTripsWithoutDefinitionBodyAndReplays()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 1);
        PhysicalDefinitionCatalog catalog = new([ore]);
        var registry = new InventoryRegistry();
        Inventory source = CreateInventory(10, 1, 17);
        Inventory destination = CreateInventory(10, 2, 18);
        registry.Add(source);
        registry.Add(destination);
        Assert.True(source.StoreFungible(ore, new Quantity(4)).IsAccepted);
        var owner = new InventoryCommitOwner(registry);
        var proposal = new TransferPhysicalInventoryProposal(
            Key(1),
            new PhysicalTransferRequest(
                source.Id,
                destination.Id,
                ore,
                new PhysicalTransferSubject.Fungible(ore.Key, new Quantity(3))));
        InventoryCommitDisposition applied = Assert.Single(owner.CommitBatch([proposal]).Outcomes);

        InventoryCommitOwnerCheckpoint checkpoint = owner.CaptureCheckpoint();
        CheckpointResult<InventoryCommitOwner> restoration =
            InventoryCommitOwner.RestoreCheckpoint(checkpoint, registry, catalog);

        Assert.Null(typeof(InventoryMutationProposalCheckpoint.TransferPhysical)
            .GetProperty("Definition"));
        Assert.True(restoration.IsSuccess);
        InventoryCommitDisposition replayed = Assert.Single(
            restoration.Value!.CommitBatch([proposal]).Outcomes);
        Assert.False(replayed.WasApplied);
        Assert.Equal(applied.Outcome, replayed.Outcome);
        Assert.Equal<ulong>(1, source.FungibleStored(ore.Key).Units);
        Assert.Equal<ulong>(3, destination.FungibleStored(ore.Key).Units);
    }

    [Fact]
    public void RemovalReceiptRestoresAfterInventoryNoLongerExists()
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);
        var proposal = new RemovePhysicalInventoryProposal(
            Key(1),
            new InventoryRemovalRequest(
                inventory.Id,
                new InventoryRemovalDisposition.DestroyContents()));
        InventoryCommitDisposition applied = Assert.Single(owner.CommitBatch([proposal]).Outcomes);

        InventoryCommitOwnerCheckpoint checkpoint = owner.CaptureCheckpoint();
        CheckpointResult<InventoryCommitOwner> restoration =
            InventoryCommitOwner.RestoreCheckpoint(
                checkpoint,
                registry,
                new PhysicalDefinitionCatalog([]));

        Assert.True(applied.WasApplied);
        Assert.False(registry.Contains(inventory.Id));
        Assert.True(restoration.IsSuccess);
        InventoryCommitDisposition replayed = Assert.Single(
            restoration.Value!.CommitBatch([proposal]).Outcomes);
        Assert.False(replayed.WasApplied);
        Assert.Equal(applied.Outcome, replayed.Outcome);
    }

    [Fact]
    public void RestoreRejectsTransferReceiptWithIncompatibleDefinitionKind()
    {
        PhysicalDefinition ore = FungibleDefinition("ore", 1);
        PhysicalDefinition sensor = DiscreteDefinition("sensor", 1);
        var registry = new InventoryRegistry();
        Inventory source = CreateInventory(10, 1, 17);
        Inventory destination = CreateInventory(10, 2, 18);
        registry.Add(source);
        registry.Add(destination);
        Assert.True(source.StoreFungible(ore, new Quantity(2)).IsAccepted);
        var owner = new InventoryCommitOwner(registry);
        owner.CommitBatch([
            new TransferPhysicalInventoryProposal(
                Key(1),
                new PhysicalTransferRequest(
                    source.Id,
                    destination.Id,
                    ore,
                    new PhysicalTransferSubject.Fungible(ore.Key, new Quantity(1)))),
        ]);
        InventoryCommitOwnerCheckpoint valid = owner.CaptureCheckpoint();
        InventoryMutationProposalCheckpoint.TransferPhysical proposal =
            Assert.IsType<InventoryMutationProposalCheckpoint.TransferPhysical>(
                valid.Receipts[0].Proposal) with
            {
                DefinitionKey = sensor.Key,
            };
        InventoryMutationOutcome.Transferred outcome =
            Assert.IsType<InventoryMutationOutcome.Transferred>(valid.Receipts[0].Outcome) with
            {
                DefinitionKey = sensor.Key,
            };
        var corrupt = new InventoryCommitOwnerCheckpoint(
            valid.ItemInstanceIds,
            valid.ReservationIds,
            [new InventoryCommitReceiptCheckpoint(proposal, outcome)]);

        CheckpointResult<InventoryCommitOwner> restoration =
            InventoryCommitOwner.RestoreCheckpoint(
                corrupt,
                registry,
                new PhysicalDefinitionCatalog([ore, sensor]));

        Assert.False(restoration.IsSuccess);
        Assert.Equal(
            "$.checkpoint.inventoryCommit.receipts[0]",
            restoration.Failure!.Path);
    }

    [Fact]
    public void ReservationReleaseContentionUsesStableKeyOrder()
    {
        (InventoryCommitBatchResult forward, Inventory forwardInventory) =
            CommitContendingReleases(reverseInput: false);
        (InventoryCommitBatchResult reversed, Inventory reversedInventory) =
            CommitContendingReleases(reverseInput: true);

        Assert.Equal(forward.Outcomes, reversed.Outcomes);
        Assert.Null(forwardInventory.GetPhysicalReservation(new ReservationId(7)));
        Assert.Null(reversedInventory.GetPhysicalReservation(new ReservationId(7)));
        Assert.IsType<InventoryMutationOutcome.ReleasedReservation>(
            forward.Outcomes[0].Outcome);
        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(forward.Outcomes[1].Outcome);
        Assert.Equal(InventoryCommitRejectionReason.ReservationRejected, rejected.Reason);
        Assert.Equal(
            InventoryReservationRejectionReason.UnknownReservation,
            rejected.ReservationReason);
    }

    [Fact]
    public void ReservationReleaseReceiptRestoresAndReplays()
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(7),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(2)),
            Owner(1)).IsAccepted);
        var owner = new InventoryCommitOwner(registry);
        var proposal = new ReleasePhysicalReservationProposal(
            Key(1),
            inventory.Id,
            new ReservationId(7),
            Owner(1));
        InventoryCommitDisposition applied = Assert.Single(owner.CommitBatch([proposal]).Outcomes);

        InventoryCommitOwnerCheckpoint checkpoint = owner.CaptureCheckpoint();
        CheckpointResult<InventoryCommitOwner> restoration =
            InventoryCommitOwner.RestoreCheckpoint(
                checkpoint,
                registry,
                new PhysicalDefinitionCatalog([]));

        Assert.True(restoration.IsSuccess);
        InventoryCommitDisposition replayed = Assert.Single(
            restoration.Value!.CommitBatch([proposal]).Outcomes);
        Assert.True(applied.WasApplied);
        Assert.False(replayed.WasApplied);
        Assert.Equal(applied.Outcome, replayed.Outcome);
        Assert.Equal(Quantity.Zero, inventory.ReservedCapacity);
    }

    [Fact]
    public void ReservationReleaseRejectsOwnerMismatchWithoutMutation()
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(7),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(2)),
            Owner(1)).IsAccepted);
        var owner = new InventoryCommitOwner(registry);

        InventoryCommitDisposition result = Assert.Single(owner.CommitBatch([
            new ReleasePhysicalReservationProposal(
                Key(1),
                inventory.Id,
                new ReservationId(7),
                Owner(2)),
        ]).Outcomes);

        InventoryMutationOutcome.Rejected rejected =
            Assert.IsType<InventoryMutationOutcome.Rejected>(result.Outcome);
        Assert.Equal(
            InventoryReservationRejectionReason.OwnerMismatch,
            rejected.ReservationReason);
        Assert.NotNull(inventory.GetPhysicalReservation(new ReservationId(7)));
        Assert.Equal<ulong>(2, inventory.ReservedCapacity.Units);
    }

    private static (InventoryCommitBatchResult Result, Inventory Inventory)
        CommitContendingReleases(bool reverseInput)
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(10);
        registry.Add(inventory);
        Assert.True(inventory.ReservePhysical(
            new ReservationId(7),
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(2)),
            Owner(1)).IsAccepted);
        InventoryMutationProposal lower = new ReleasePhysicalReservationProposal(
            Key(1), inventory.Id, new ReservationId(7), Owner(1));
        InventoryMutationProposal higher = new ReleasePhysicalReservationProposal(
            Key(2), inventory.Id, new ReservationId(7), Owner(1));
        InventoryMutationProposal[] proposals = reverseInput
            ? [higher, lower]
            : [lower, higher];
        var owner = new InventoryCommitOwner(registry);
        return (owner.CommitBatch(proposals), inventory);
    }

    private static (
        InventoryCommitBatchResult Result,
        Inventory Second,
        Inventory Third) CommitContendingTransfers(
            PhysicalDefinition ore,
            bool reverseInput)
    {
        var registry = new InventoryRegistry();
        Inventory source = CreateInventory(10, 1, 17);
        Inventory second = CreateInventory(10, 2, 18);
        Inventory third = CreateInventory(10, 3, 19);
        registry.Add(source);
        registry.Add(second);
        registry.Add(third);
        Assert.True(source.StoreFungible(ore, new Quantity(6)).IsAccepted);
        InventoryMutationProposal lower = new TransferPhysicalInventoryProposal(
            Key(1),
            new PhysicalTransferRequest(
                source.Id,
                third.Id,
                ore,
                new PhysicalTransferSubject.Fungible(ore.Key, new Quantity(4))));
        InventoryMutationProposal higher = new TransferPhysicalInventoryProposal(
            Key(2),
            new PhysicalTransferRequest(
                source.Id,
                second.Id,
                ore,
                new PhysicalTransferSubject.Fungible(ore.Key, new Quantity(4))));
        var owner = new InventoryCommitOwner(registry);
        InventoryMutationProposal[] proposals = reverseInput
            ? [higher, lower]
            : [lower, higher];
        return (owner.CommitBatch(proposals), second, third);
    }

    private static (InventoryCommitBatchResult Result, Inventory Inventory) Commit(
        IEnumerable<InventoryMutationProposal> proposals,
        PhysicalDefinition definition,
        ulong capacity)
    {
        var registry = new InventoryRegistry();
        Inventory inventory = CreateInventory(capacity);
        registry.Add(inventory);
        var owner = new InventoryCommitOwner(registry);
        return (owner.CommitBatch(proposals), inventory);
    }

    private static InventoryOperationKey Key(ulong value) =>
        new(InventoryOperationSourceKind.Explicit, value);

    private static ReservationOwner.TransportJob Owner(ulong value) =>
        new(new TransportJobId(value));

    private static Inventory CreateInventory(
        ulong capacity,
        ulong inventoryId = 1,
        ulong entityId = 17) =>
        new(
            new InventoryId(inventoryId),
            new InventoryCustody(
                new InventoryOwnerReference.SessionEntity(new EntityId(entityId)),
                new PrincipalId(4)),
            new Quantity(capacity));

    private static PhysicalDefinition FungibleDefinition(
        string localId,
        ulong capacityCost) =>
        Definition(localId, PhysicalHoldingKind.Fungible, capacityCost);

    private static PhysicalDefinition DiscreteDefinition(
        string localId,
        ulong capacityCost) =>
        Definition(localId, PhysicalHoldingKind.Discrete, capacityCost);

    private static PhysicalDefinition Definition(
        string localId,
        PhysicalHoldingKind holdingKind,
        ulong capacityCost) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            holdingKind,
            new Quantity(capacityCost));
}
