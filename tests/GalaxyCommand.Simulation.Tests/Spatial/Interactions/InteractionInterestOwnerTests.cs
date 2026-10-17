using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionInterestOwnerTests
{
    [Fact]
    public void DomainOwnersShareOneSessionAllocatorWithoutSharingTheirState()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var first = new InteractionInterestOwner("first", ids);
        var second = new InteractionInterestOwner("second", ids);

        ShipPairInteractionInterest a = first.Register("contact", new ShipId(8), new ShipId(3), 5);
        ShipPairInteractionInterest b = second.Register("watch", new ShipId(3), new ShipId(8), 7);

        Assert.Equal(new InteractionInterestId(1), a.Id);
        Assert.Equal(new InteractionInterestId(2), b.Id);
        Assert.Equal("first", a.DomainKind);
        Assert.Equal("second", b.DomainKind);
        Assert.Equal(a, first.Get(a.Id));
        Assert.Null(first.Get(b.Id));
        Assert.Equal(new ShipId(8), a.Source);
        Assert.Equal(new EventGeneration(0), a.Generation);
    }

    [Fact]
    public void UpdatesPreserveIdentityAndPurposeAndAdvanceOnlyOnChange()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest original = owner.Register("contact", new ShipId(1), new ShipId(2), 5);
        owner.AcknowledgeChanges(owner.CaptureChanges());

        ShipPairInteractionInterest unchanged = owner.Update(original.Id, original.Source, original.Target, 5);
        Assert.Equal(original, unchanged);
        Assert.Empty(owner.CaptureChanges());
        ShipPairInteractionInterest changed = owner.Update(original.Id, new ShipId(2), new ShipId(1), 6);
        Assert.Equal(original.Id, changed.Id);
        Assert.Equal(original.PurposeId, changed.PurposeId);
        Assert.Equal(new EventGeneration(1), changed.Generation);
        Assert.Equal(changed, Assert.Single(owner.CaptureSnapshot()));
        Assert.Equal(changed, Assert.Single(owner.CaptureChanges()).Interest);
    }

    [Fact]
    public void CancellationNeverAllowsIdentityReuse()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest first = owner.Register("contact", new ShipId(1), new ShipId(2), 5);

        Assert.True(owner.Cancel(first.Id));
        Assert.False(owner.Cancel(first.Id));
        Assert.Null(owner.Get(first.Id));
        Assert.Null(Assert.Single(owner.CaptureChanges()).Interest);
        Assert.Equal(new InteractionInterestId(2), owner.Register("contact", new ShipId(1), new ShipId(2), 5).Id);
    }

    [Fact]
    public void InvalidRegistrationDoesNotConsumeAnIdentity()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("test", ids);

        Assert.Throws<ArgumentException>(() => owner.Register("contact", new ShipId(1), new ShipId(1), 5));
        Assert.Throws<ArgumentException>(() => owner.Register(" ", new ShipId(1), new ShipId(2), 5));

        Assert.Equal(new InteractionInterestId(1), ids.Allocate());
        Assert.Empty(owner.CaptureSnapshot());
        Assert.Empty(owner.CaptureChanges());
    }

    [Fact]
    public void AcknowledgementCannotDiscardAChangeCommittedAfterCapture()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 5);
        IReadOnlyList<InteractionInterestChange> captured = owner.CaptureChanges();
        ShipPairInteractionInterest changed = owner.Update(interest.Id, interest.Source, interest.Target, 6);

        owner.AcknowledgeChanges(captured);

        Assert.Equal(changed, Assert.Single(owner.CaptureChanges()).Interest);
    }

    [Fact]
    public void CheckpointRetainsLiveInterestsAndExactSharedAllocatorAfterCancellation()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("test", ids);
        ShipPairInteractionInterest canceled = owner.Register("old", new ShipId(1), new ShipId(2), 5);
        ShipPairInteractionInterest live = owner.Register("live", new ShipId(8), new ShipId(3), 6);
        owner.Cancel(canceled.Id);
        live = owner.Update(live.Id, live.Source, live.Target, 7);
        IdSequence<InteractionInterestId> restoredIds = IdSequence<InteractionInterestId>.RestoreCheckpoint(
            ids.CaptureCheckpoint()).Value!;

        InteractionInterestOwner restored = InteractionInterestOwner.RestoreCheckpoint(
            owner.CaptureCheckpoint(), restoredIds).Value!;

        Assert.Equal(live, Assert.Single(restored.CaptureSnapshot()));
        Assert.Null(restored.Get(canceled.Id));
        Assert.Equal(new InteractionInterestId(3), restored.Register("new", new ShipId(1), new ShipId(2), 5).Id);
        Assert.Equal(owner.CaptureCheckpoint().DomainKind, restored.CaptureCheckpoint().DomainKind);
    }

    [Fact]
    public void RestoreRejectsDuplicateForeignAndUnallocatedInterestIds()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("test", ids);
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 5);
        Assert.False(InteractionInterestOwner.RestoreCheckpoint(new InteractionInterestOwnerCheckpoint(
            "test", [interest, interest]), ids).IsSuccess);
        Assert.False(InteractionInterestOwner.RestoreCheckpoint(new InteractionInterestOwnerCheckpoint(
            "other", [interest]), ids).IsSuccess);
        Assert.False(InteractionInterestOwner.RestoreCheckpoint(owner.CaptureCheckpoint(),
            new IdSequence<InteractionInterestId>()).IsSuccess);
    }

    [Fact]
    public void RestoreRejectsDuplicateOrphanAndWrongGenerationDeliveryHistory()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("test", ids);
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        var delivery = new InteractionDeliveryCorrelation(interest.Id, interest.Generation, default, default);
        InteractionInterestOwnerCheckpoint checkpoint = owner.CaptureCheckpoint();
        Assert.False(InteractionInterestOwner.RestoreCheckpoint(checkpoint with
        {
            Deliveries = [delivery, delivery],
        }, ids).IsSuccess);
        Assert.False(InteractionInterestOwner.RestoreCheckpoint(checkpoint with
        {
            Deliveries = [delivery with { InterestId = new InteractionInterestId(99) }],
        }, ids).IsSuccess);
        Assert.False(InteractionInterestOwner.RestoreCheckpoint(checkpoint with
        {
            Deliveries = [delivery with { Generation = interest.Generation.Next() }],
        }, ids).IsSuccess);
    }

    [Fact]
    public void DeliveryCheckpointIsIsolatedAndRealUpdatesAndCancellationClearHistory()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        var movement = new SpatialMovement();
        var position = new SystemPosition(new SystemId(1), new SpatialPosition(default, default));
        movement.Add(new ShipId(1), position);
        movement.Add(new ShipId(2), position);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 0);
        owner.RecordDelivery(interest, view);
        InteractionInterestOwnerCheckpoint captured = owner.CaptureCheckpoint();
        Assert.True(owner.HasDelivered(interest, view));
        Assert.Equal(interest, owner.Update(interest.Id, interest.Source, interest.Target, 0));
        Assert.True(owner.HasDelivered(interest, view));
        ShipPairInteractionInterest revised = owner.Update(interest.Id, interest.Source, interest.Target, 1);
        Assert.Empty(owner.CaptureCheckpoint().Deliveries!);
        Assert.Single(captured.Deliveries!);
        owner.RecordDelivery(revised, view);
        owner.Cancel(revised.Id);
        Assert.Empty(owner.CaptureCheckpoint().Deliveries!);
        Assert.Single(captured.Deliveries!);
    }

    [Fact]
    public void InvalidUpdatePreservesTheLiveInterestAndPendingNotice()
    {
        var owner = new InteractionInterestOwner("test", new IdSequence<InteractionInterestId>());
        ShipPairInteractionInterest interest = owner.Register("contact", new ShipId(1), new ShipId(2), 5);
        InteractionInterestChange notice = Assert.Single(owner.CaptureChanges());

        Assert.Throws<ArgumentException>(() => owner.Update(interest.Id, new ShipId(1), new ShipId(1), 7));
        Assert.Throws<KeyNotFoundException>(() => owner.Update(new InteractionInterestId(99), new ShipId(1), new ShipId(2), 7));

        Assert.Equal(interest, owner.Get(interest.Id));
        Assert.Same(notice, Assert.Single(owner.CaptureChanges()));
    }

    [Fact]
    public void RestoredInterestsSeedOrderedChangesWithoutRetainingOldBuffers()
    {
        var ids = new IdSequence<InteractionInterestId>();
        var owner = new InteractionInterestOwner("test", ids);
        ShipPairInteractionInterest first = owner.Register("first", new ShipId(1), new ShipId(2), 5);
        ShipPairInteractionInterest second = owner.Register("second", new ShipId(2), new ShipId(3), 5);
        owner.AcknowledgeChanges(owner.CaptureChanges());

        InteractionInterestOwner restored = InteractionInterestOwner.RestoreCheckpoint(
            new InteractionInterestOwnerCheckpoint("test", [second, first]), ids).Value!;

        Assert.Equal(new[] { first, second }, restored.CaptureSnapshot());
        Assert.Equal(new[] { first.Id, second.Id }, restored.CaptureChanges().Select(change => change.Id));
        restored.AcknowledgeChanges(restored.CaptureChanges());
        Assert.Empty(restored.CaptureChanges());
        Assert.Equal(new[] { first, second }, restored.CaptureSnapshot());
    }
}
