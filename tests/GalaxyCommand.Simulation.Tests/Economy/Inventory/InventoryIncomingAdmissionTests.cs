using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventoryIncomingAdmissionTests
{
    private static readonly ReservationOwner Owner = new ReservationOwner.TransportJob(new(1));
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.age", "cargo", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinitionCatalog Definitions = new([Ore]);

    [Fact]
    public void MixedIncomingKindsUseOneCommittedAgeOrderIndependentOfReservationIds()
    {
        Inventory inventory = Source();
        inventory.StoreFungible(Ore, new(1));
        PhysicalReservation first = Incoming(inventory, 100);
        CapacityReservation second = inventory.ReserveCapacity(new(1), new(1), Owner);
        inventory.ReservePhysical(new(2), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner);
        PhysicalReservation third = Incoming(inventory, 5);

        IReadOnlyList<InventoryIncomingCommitment> ordered = inventory.CaptureIncomingCommitments();

        Assert.Equal(new ulong[] { 1, 2, 3 }, ordered.Select(item => item.AdmissionSequence));
        Assert.Equal(new ulong[] { 100, 1, 5 }, ordered.Select(item => item.ReservationValue));
        Assert.Same(first, Assert.IsType<InventoryIncomingCommitment.Physical>(ordered[0]).Reservation);
        Assert.Same(second, Assert.IsType<InventoryIncomingCommitment.Legacy>(ordered[1]).Reservation);
        Assert.Same(third, Assert.IsType<InventoryIncomingCommitment.Physical>(ordered[2]).Reservation);
        Assert.Throws<NotSupportedException>(() => ((IList<InventoryIncomingCommitment>)ordered).Clear());
    }

    [Fact]
    public void ReleaseAndRecreateRetiresOldAgeWithoutResettingHighwater()
    {
        Inventory inventory = Source();
        CapacityReservation legacy = inventory.ReserveCapacity(new(7), new(1), Owner);
        Incoming(inventory, 1);
        inventory.ReleaseCapacity(legacy.Id);
        inventory.ReserveCapacity(legacy.Id, new(1), Owner);

        Assert.Equal(new ulong[] { 2, 3 }, inventory.CaptureIncomingCommitments().Select(item => item.AdmissionSequence));
        Assert.Equal(4UL, inventory.CaptureCheckpoint().NextIncomingAdmissionSequence);
    }

    [Fact]
    public void ReleasedAgesSurviveAnEmptyCheckpointAndAreNeverReused()
    {
        Inventory inventory = Source();
        CapacityReservation legacy = inventory.ReserveCapacity(new(7), new(1), Owner);
        PhysicalReservation physical = Incoming(inventory, 1);
        inventory.ReleaseCapacity(legacy.Id);
        inventory.ReleasePhysicalReservation(physical.Id, physical.Owner);
        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(inventory.CaptureCheckpoint(), Definitions);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);

        restored.Value!.ReserveCapacity(new(99), new(1), Owner);

        Assert.Equal(3UL, Assert.Single(restored.Value.CaptureIncomingCommitments()).AdmissionSequence);
        Assert.Equal(4UL, restored.Value.CaptureCheckpoint().NextIncomingAdmissionSequence);
    }

    [Fact]
    public void MixedIncomingAgeRoundTripRestoresExactBijectionAndNextSequence()
    {
        Inventory inventory = Source();
        Incoming(inventory, 90);
        inventory.ReserveCapacity(new(1), new(2), Owner);
        InventoryCheckpoint checkpoint = inventory.CaptureCheckpoint();

        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(checkpoint, Definitions);

        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(checkpoint.IncomingAdmissions, restored.Value!.CaptureCheckpoint().IncomingAdmissions);
        Assert.Equal(checkpoint.NextIncomingAdmissionSequence, restored.Value.CaptureCheckpoint().NextIncomingAdmissionSequence);
        Assert.Equal(new ulong[] { 90, 1 }, restored.Value.CaptureIncomingCommitments().Select(item => item.ReservationValue));
    }

    [Fact]
    public void RejectedReservationsAndPurePreparationConsumeNoIncomingAge()
    {
        Inventory inventory = Source();
        Assert.Throws<InvalidOperationException>(() => inventory.ReserveCapacity(new(1), new(101), Owner));
        Assert.False(inventory.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(101)), Owner).IsAccepted);
        Assert.NotNull(inventory.PrepareCapacityChange(new(100), new(100)));
        var registry = new InventoryRegistry();
        registry.Add(inventory);
        var commit = new InventoryCommitOwner(registry);
        Assert.True(commit.TryPeekStationRecoveryReservationIdentity(out ReservationId id));
        Assert.True(commit.CanReserveStationRecoveryCapacity(inventory, new(1), new(new(1), new(1)), id));
        Assert.True(commit.CanReserveStationRecoveryCapacity(inventory, new(1), new(new(1), new(1)), id));

        Assert.Equal(1UL, inventory.CaptureCheckpoint().NextIncomingAdmissionSequence);
        Assert.Empty(inventory.CaptureCheckpoint().IncomingAdmissions);
        inventory.ReserveCapacity(new(1), new(1), Owner);
        Assert.Equal(1UL, Assert.Single(inventory.CaptureIncomingCommitments()).AdmissionSequence);
    }

    [Fact]
    public void MalformedBijectionsAndHighwaterRejectInsteadOfInferringAgeFromIds()
    {
        Inventory inventory = Source();
        Incoming(inventory, 90);
        inventory.ReserveCapacity(new(1), new(1), Owner);
        InventoryCheckpoint original = inventory.CaptureCheckpoint();
        InventoryIncomingAdmissionCheckpoint physical = new(InventoryIncomingReservationKind.PhysicalCapacity, 90, 1);
        InventoryIncomingAdmissionCheckpoint legacy = new(InventoryIncomingReservationKind.LegacyCapacity, 1, 2);
        foreach ((ulong? next, InventoryIncomingAdmissionCheckpoint[] entries) in new (ulong?, InventoryIncomingAdmissionCheckpoint[])[]
        {
            (0, [physical, legacy]), (2, [physical, legacy]), (3, []), (3, [physical]),
            (3, [physical, physical, legacy]), (3, [physical, legacy with { AdmissionSequence = 1 }]),
            (3, [physical, legacy with { ReservationValue = 99 }]),
            (3, [physical with { Kind = (InventoryIncomingReservationKind)99 }, legacy]),
        })
        {
            Assert.False(Inventory.RestoreCheckpoint(Copy(original, next, entries), Definitions).IsSuccess);
        }
    }

    [Fact]
    public void ExhaustedAgeAuthorityRestoresExactlyAndRejectsOnlyNewIncomingClaims()
    {
        Inventory inventory = Source();
        inventory.StoreFungible(Ore, new(2));
        Incoming(inventory, 1);
        InventoryCheckpoint exhausted = Copy(inventory.CaptureCheckpoint(), null,
            [new(InventoryIncomingReservationKind.PhysicalCapacity, 1, ulong.MaxValue)]);
        CheckpointResult<Inventory> restored = Inventory.RestoreCheckpoint(exhausted, Definitions);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Inventory continued = restored.Value!;

        Assert.Throws<InvalidOperationException>(() => continued.ReserveCapacity(new(1), new(1), Owner));
        InventoryReservationResult rejected = continued.ReservePhysical(new(2), new PhysicalReservationSubject.IncomingCapacity(new(1)), Owner);
        Assert.False(rejected.IsAccepted);
        Assert.Equal(InventoryReservationRejectionReason.IncomingAdmissionSequenceExhausted, rejected.RejectionReason);
        Assert.True(continued.ReservePhysical(new(3), new PhysicalReservationSubject.Fungible(Ore.Key, new(1)), Owner).IsAccepted);
        Assert.Null(continued.CaptureCheckpoint().NextIncomingAdmissionSequence);
        Assert.Equal(ulong.MaxValue, Assert.Single(continued.CaptureIncomingCommitments()).AdmissionSequence);
    }

    [Fact]
    public void AdmitThenReleaseInvalidatesExactCargoLossPreparationDespiteUnchangedTotals()
    {
        Inventory inventory = Source();
        inventory.StoreFungible(Ore, new(10));
        PreparedCargoSourceLoss loss = CargoSourceLoss.Prepare(inventory,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(1))], Definitions, []).Prepared!;
        CapacityReservation transient = inventory.ReserveCapacity(new(1), new(1), Owner);
        inventory.ReleaseCapacity(transient.Id);

        Assert.Equal(Quantity.Zero, inventory.ReservedCapacity);
        Assert.False(loss.CanApply());
    }

    private static PhysicalReservation Incoming(Inventory inventory, ulong id) => inventory.ReservePhysical(new(id),
        new PhysicalReservationSubject.IncomingCapacity(new(1)), Owner).Reservation!;

    private static Inventory Source() => new(new(1), new InventoryCustody.PrincipalControlled(
        new InventoryOwnerReference.SessionEntity(new(1)), new(1)), new(100));

    private static InventoryCheckpoint Copy(InventoryCheckpoint source, ulong? next,
        IEnumerable<InventoryIncomingAdmissionCheckpoint> entries) => new(source.Id, source.Custody, source.Capacity,
        source.StoredMaterials, source.Reservations, source.CapacityReservations, source.FungibleHoldings,
        source.DiscreteItems, source.PhysicalReservations, source.EnabledIntakeCapacity, source.ProtectedIntakeCapacity, next, entries,
        source.NextOutgoingAdmissionSequence, source.OutgoingAdmissions);
}
