using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryOutgoingLossCleanupTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.outgoing", "cargo", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinition Other = new(QualifiedContentKey.Create("test.outgoing", "cargo", "other"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinition Equipment = new(QualifiedContentKey.Create("test.outgoing", "cargo", "equipment"), PhysicalHoldingKind.Discrete, new(1));
    private static readonly PhysicalDefinitionCatalog Definitions = new([Ore, Other, Equipment]);
    private static readonly ReservationOwner Owner = new ReservationOwner.TransportJob(new(1));

    [Fact]
    public void SharedOutgoingAgePreservesOlderClaimsAcrossLegacyAndGeneralizedReservations()
    {
        Inventory source = Source();
        Reservation oldest = source.Reserve(new(90), Metal, new(6), Owner);
        PhysicalReservation middle = source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(6)), Owner).Reservation!;
        Reservation newest = source.Reserve(new(5), Metal, new(4), Owner);
        InventoryCheckpoint before = source.CaptureCheckpoint();

        PreparedInventoryOutgoingLossCleanup plan = source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Fungible(Ore.Key, new(9))], Definitions)!;

        Assert.NotNull(plan);
        Assert.True(plan.CanCommit());
        Assert.Equal(new ulong[] { 5, 1 }, plan.Releases.Select(release => release switch
        {
            InventoryCommitmentRelease.Material legacy => legacy.Reservation.Id.Value,
            InventoryCommitmentRelease.Physical physical => physical.Reservation.Id.Value,
            _ => 0UL,
        }));
        Assert.Same(newest, Assert.IsType<InventoryCommitmentRelease.Material>(plan.Releases[0]).Reservation);
        Assert.Same(middle, Assert.IsType<InventoryCommitmentRelease.Physical>(plan.Releases[1]).Reservation);
        Assert.Same(oldest, source.GetReservation(oldest.Id));
        Assert.Equal(before.NextOutgoingAdmissionSequence, source.CaptureCheckpoint().NextOutgoingAdmissionSequence);
        Assert.Equal(before.OutgoingAdmissions, source.CaptureCheckpoint().OutgoingAdmissions);
        Assert.Equal(new Quantity(20), source.Stored(Metal));
        Assert.True(plan.SourceLoss.Apply());
        Assert.Same(oldest, source.GetReservation(oldest.Id));
        Assert.Null(source.GetReservation(newest.Id));
        Assert.Null(source.GetPhysicalReservation(middle.Id));
        Assert.Equal(new Quantity(11), source.Stored(Metal));
        Assert.Equal(new Quantity(6), source.Reserved(Metal));
    }

    [Fact]
    public void NewerAffectedClaimIsReleasedInFullRatherThanPartiallyResized()
    {
        Inventory source = Source();
        source.Reserve(new(1), Metal, new(7), Owner);
        PhysicalReservation newest = source.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(5)), Owner).Reservation!;

        PreparedInventoryOutgoingLossCleanup plan = source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Fungible(Ore.Key, new(10))], Definitions)!;

        Assert.Same(newest, Assert.IsType<InventoryCommitmentRelease.Physical>(Assert.Single(plan.Releases)).Reservation);
        Assert.Equal(new Quantity(5), ((PhysicalReservationSubject.Fungible)newest.Subject).Quantity);
    }

    [Fact]
    public void UnaffectedDefinitionsAndIncomingClaimsAreNeverSelectedForOutgoingCleanup()
    {
        Inventory source = Source();
        source.StoreFungible(Other, new(10));
        source.Reserve(new(1), Metal, new(10), Owner);
        PhysicalReservation unaffected = source.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Other.Key, new(10)), Owner).Reservation!;
        source.ReserveCapacity(new(1), new(1), Owner);
        source.ReservePhysical(new(3), new PhysicalReservationSubject.IncomingCapacity(new(1)), Owner);

        PreparedInventoryOutgoingLossCleanup plan = source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Fungible(Ore.Key, new(11))], Definitions)!;

        Assert.IsType<InventoryCommitmentRelease.Material>(Assert.Single(plan.Releases));
        Assert.True(plan.SourceLoss.Apply());
        Assert.Same(unaffected, source.GetPhysicalReservation(unaffected.Id));
        Assert.Equal(new Quantity(2), source.ReservedCapacity);
    }

    [Fact]
    public void DiscreteLossInvalidatesOnlyTheExactSelectedInstanceClaim()
    {
        Inventory source = Source();
        source.StoreDiscrete(Equipment, new(1));
        source.StoreDiscrete(Equipment, new(2));
        PhysicalReservation removed = source.ReservePhysical(new(1), new PhysicalReservationSubject.Discrete(new(1)), Owner).Reservation!;
        PhysicalReservation surviving = source.ReservePhysical(new(2), new PhysicalReservationSubject.Discrete(new(2)), Owner).Reservation!;

        PreparedInventoryOutgoingLossCleanup plan = source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Discrete(Equipment.Key, new(1))], Definitions)!;

        Assert.Same(removed, Assert.IsType<InventoryCommitmentRelease.Physical>(Assert.Single(plan.Releases)).Reservation);
        Assert.True(plan.SourceLoss.Apply());
        Assert.Null(source.GetDiscrete(new(1)));
        Assert.Same(surviving, source.GetPhysicalReservation(surviving.Id));
    }

    [Fact]
    public void InvalidOrDuplicateSelectedInstancesRejectWithoutChangingAuthority()
    {
        Inventory source = Source();
        source.StoreDiscrete(Equipment, new(1));
        Assert.Null(source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Fungible(Ore.Key, new(21))], Definitions));
        Assert.Null(source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Discrete(Ore.Key, new(1))], Definitions));
        Assert.Null(source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Discrete(Equipment.Key, new(1)),
            new CargoDispositionSelection.Discrete(Equipment.Key, new(1))], Definitions));
        Assert.Equal(new Quantity(20), source.Stored(Metal));
        Assert.Empty(source.CaptureOutgoingCommitments());
    }

    [Fact]
    public void ConsumingOrRecreatingAClaimRetiresItsAgeAndInvalidatesPreparedCleanup()
    {
        Inventory source = Source();
        Reservation original = source.Reserve(new(90), Metal, new(10), Owner);
        PreparedInventoryOutgoingLossCleanup plan = source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Fungible(Ore.Key, new(11))], Definitions)!;
        source.Release(original.Id);
        source.Reserve(original.Id, original.MaterialId, original.Quantity, original.Owner);

        Assert.False(plan.CanCommit());
        Assert.Equal(2UL, Assert.Single(source.CaptureOutgoingCommitments()).AdmissionSequence);
        source.ConsumeReservations([original.Id], original.Owner);
        Assert.Empty(source.CaptureOutgoingCommitments());
        Assert.Equal(3UL, source.CaptureCheckpoint().NextOutgoingAdmissionSequence);
    }

    [Fact]
    public void SharedOutgoingAgeAndReleasedHighwaterRoundTripExactly()
    {
        Inventory source = Source();
        Reservation first = source.Reserve(new(90), Metal, new(1), Owner);
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner);
        source.Release(first.Id);
        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(source.CaptureCheckpoint(), Definitions, Materials());
        Assert.True(restored.IsSuccess, restored.Failure?.Message);

        restored.Value!.Reserve(new(90), Metal, new(1), Owner);

        Assert.Equal(new ulong[] { 2, 3 }, restored.Value.CaptureOutgoingCommitments().Select(item => item.AdmissionSequence));
        Assert.Equal(4UL, restored.Value.CaptureCheckpoint().NextOutgoingAdmissionSequence);
    }

    [Fact]
    public void RejectedOutgoingReservationsAndPlanningConsumeNoAge()
    {
        Inventory source = Source();
        Assert.Throws<InvalidOperationException>(() => source.Reserve(new(1), Metal, new(21), Owner));
        Assert.False(source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(21)), Owner).IsAccepted);
        Assert.NotNull(source.PrepareOutgoingLossCleanup([new CargoDispositionSelection.Fungible(Ore.Key, new(1))], Definitions));
        Assert.Equal(1UL, source.CaptureCheckpoint().NextOutgoingAdmissionSequence);
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner);
        Assert.Equal(1UL, Assert.Single(source.CaptureOutgoingCommitments()).AdmissionSequence);
    }

    [Fact]
    public void OutgoingBijectionAndHighwaterRejectMissingDuplicateAndWrongKindEntries()
    {
        Inventory source = Source();
        source.Reserve(new(90), Metal, new(1), Owner);
        source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner);
        InventoryCheckpoint original = source.CaptureCheckpoint();
        InventoryOutgoingAdmissionCheckpoint legacy = new(InventoryOutgoingReservationKind.LegacyMaterial, 90, 1);
        InventoryOutgoingAdmissionCheckpoint physical = new(InventoryOutgoingReservationKind.Physical, 1, 2);
        foreach ((ulong? next, InventoryOutgoingAdmissionCheckpoint[] entries) in new (ulong?, InventoryOutgoingAdmissionCheckpoint[])[]
        {
            (0, [legacy, physical]), (2, [legacy, physical]), (3, []), (3, [legacy]),
            (3, [legacy, legacy, physical]), (3, [legacy, physical with { AdmissionSequence = 1 }]),
            (3, [legacy, physical with { Kind = InventoryOutgoingReservationKind.LegacyMaterial }]),
            (3, [legacy, physical with { Kind = (InventoryOutgoingReservationKind)99 }]),
        })
            Assert.False(Inventory.RestoreCheckpoint(CopyOutgoing(original, next, entries), Definitions, Materials()).IsSuccess);
    }

    [Fact]
    public void ExhaustedOutgoingSequenceDoesNotBlockIndependentIncomingCapacity()
    {
        Inventory source = Source();
        source.Reserve(new(1), Metal, new(1), Owner);
        InventoryCheckpoint exhausted = CopyOutgoing(source.CaptureCheckpoint(), null,
            [new(InventoryOutgoingReservationKind.LegacyMaterial, 1, ulong.MaxValue)]);
        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(exhausted, Definitions, Materials());
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Inventory continued = restored.Value!;

        Assert.Throws<InvalidOperationException>(() => continued.Reserve(new(2), Metal, new(1), Owner));
        InventoryReservationResult rejected = continued.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner);
        Assert.Equal(InventoryReservationRejectionReason.OutgoingAdmissionSequenceExhausted, rejected.RejectionReason);
        Assert.True(continued.ReservePhysical(new(3), new PhysicalReservationSubject.IncomingCapacity(new(1)), Owner).IsAccepted);
        Assert.Null(continued.CaptureCheckpoint().NextOutgoingAdmissionSequence);
        Assert.Equal(2UL, continued.CaptureCheckpoint().NextIncomingAdmissionSequence);
    }

    [Fact]
    public void EmptyOutgoingLedgerStillRetainsReleasedHighwaterAcrossRestore()
    {
        Inventory source = Source();
        source.Reserve(new(1), Metal, new(1), Owner);
        source.Release(new(1));
        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(source.CaptureCheckpoint(), Definitions, Materials());
        Assert.True(restored.IsSuccess, restored.Failure?.Message);

        restored.Value!.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner);

        Assert.Equal(2UL, Assert.Single(restored.Value.CaptureOutgoingCommitments()).AdmissionSequence);
    }

    [Fact]
    public void ForcedOlderProviderReleaseIsRemovedBeforeComputingAnyAdditionalNewestLossCleanup()
    {
        Inventory source = Source(100);
        Reservation forced = source.Reserve(new(90), Metal, new(60), Owner);
        PhysicalReservation viable = source.ReservePhysical(new(1), new PhysicalReservationSubject.Fungible(Ore.Key, new(40)), Owner).Reservation!;

        PreparedInventoryOutgoingLossCleanup plan = source.PrepareOutgoingLossCleanup(
            [new CargoDispositionSelection.Fungible(Ore.Key, new(20))], Definitions,
            [new InventoryCommitmentRelease.Material(source, forced)])!;

        Assert.NotNull(plan);
        Assert.Same(forced, Assert.IsType<InventoryCommitmentRelease.Material>(Assert.Single(plan.Releases)).Reservation);
        Assert.True(plan.SourceLoss.Apply());
        Assert.Equal(new Quantity(80), source.Stored(Metal));
        Assert.Same(viable, source.GetPhysicalReservation(viable.Id));
    }

    [Fact]
    public void ForcedClaimsRejectIncomingForeignDuplicateAndReplacedOwnerEvidence()
    {
        Inventory source = Source();
        Inventory other = Source();
        Reservation reservation = source.Reserve(new(1), Metal, new(1), Owner);
        CapacityReservation incoming = source.ReserveCapacity(new(1), new(1), Owner);
        var exact = new InventoryCommitmentRelease.Material(source, reservation);
        CargoDispositionSelection[] selected = [new CargoDispositionSelection.Fungible(Ore.Key, new(1))];

        Assert.Null(source.PrepareOutgoingLossCleanup(selected, Definitions, [exact, exact]));
        Assert.Null(source.PrepareOutgoingLossCleanup(selected, Definitions, [new InventoryCommitmentRelease.Material(other, reservation)]));
        Assert.Null(source.PrepareOutgoingLossCleanup(selected, Definitions, [new InventoryCommitmentRelease.Capacity(source, incoming)]));
        Assert.Null(source.PrepareOutgoingLossCleanup(selected, Definitions,
            [new InventoryCommitmentRelease.Material(source, reservation with { Owner = new ReservationOwner.ProductionJob(new(2)) })]));
    }

    private static Inventory Source(ulong amount = 20)
    {
        var inventory = new Inventory(new(1), new InventoryCustody.PrincipalControlled(new InventoryOwnerReference.SessionEntity(new(1)), new(1)), new(100), Materials());
        inventory.Add(Metal, new(amount));
        return inventory;
    }

    private static MaterialInventoryCompatibilityMap Materials() => new(new Dictionary<MaterialId, PhysicalDefinition> { [Metal] = Ore });

    private static InventoryCheckpoint CopyOutgoing(InventoryCheckpoint source, ulong? next,
        IEnumerable<InventoryOutgoingAdmissionCheckpoint> outgoing) => new(source.Id, source.Custody, source.Capacity,
        source.StoredMaterials, source.Reservations, source.CapacityReservations, source.FungibleHoldings, source.DiscreteItems,
        source.PhysicalReservations, source.EnabledIntakeCapacity, source.ProtectedIntakeCapacity,
        source.NextIncomingAdmissionSequence, source.IncomingAdmissions, next, outgoing);
}
