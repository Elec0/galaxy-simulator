using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDebrisFieldsTests
{
    [Fact]
    public void PreparationDoesNotAllocateOrPublishAndStalePreparationRejects()
    {
        var owner = new CargoDebrisFields(1);
        var first = owner.PrepareOccurrence(Position(0), 10, [new(1)]);
        var stale = owner.PrepareOccurrence(Position(100), 10, [new(2)]);
        Assert.Empty(owner.CaptureFields());
        Assert.Equal<ulong?>(1, owner.CaptureCheckpoint().FieldIds.NextValue);
        owner.Apply(first!);
        Assert.False(owner.CanCommit(stale!));
        Assert.Throws<InvalidOperationException>(() => owner.Apply(stale!));
        Assert.Single(owner.CaptureFields());
        Assert.Null(owner.PrepareOccurrence(Position(0), 10, []));
        Assert.Equal<ulong?>(2, owner.CaptureCheckpoint().FieldIds.NextValue);
    }

    [Fact]
    public void TouchingCirclesMergeAndFlattenEveryHistoricalHandleToLowestField()
    {
        var owner = new CargoDebrisFields(1);
        DebrisFieldId left = Add(owner, 0, 10, 1);
        DebrisFieldId right = Add(owner, 40, 10, 2);
        DebrisFieldId bridge = Add(owner, 20, 10, 3);
        Assert.Equal(left, owner.Resolve(right));
        Assert.Equal(left, owner.Resolve(bridge));
        CargoDebrisFieldSnapshot field = Assert.Single(owner.CaptureFields());
        Assert.Equal(3, field.Circles.Count);
        Assert.Equal(new[] { new EntityId(1), new(2), new(3) }, field.Members);
        Assert.Equal(2, owner.CaptureCheckpoint().Aliases.Count);
        Assert.All(owner.CaptureCheckpoint().Aliases, alias => Assert.Equal(left, alias.Canonical));
    }

    [Fact]
    public void RemovedBridgeMembersDoNotSplitFieldOrEraseOriginalCircle()
    {
        var owner = new CargoDebrisFields(1);
        DebrisFieldId first = Add(owner, 0, 10, 1);
        Add(owner, 20, 10, 2);
        DebrisFieldId tail = Add(owner, 40, 10, 3);
        owner.Apply(owner.PrepareRemoval(new(2))!);
        Assert.Equal(first, owner.Resolve(tail));
        Assert.Equal(3, Assert.Single(owner.CaptureFields()).Circles.Count);
        DebrisFieldId later = Add(owner, 20, 0, 4);
        Assert.Equal(first, owner.Resolve(later));
        Assert.Equal(4, Assert.Single(owner.CaptureFields()).Circles.Count);
    }

    [Fact]
    public void EmptyFieldsLoseCirclesButRetainAliasesAndTombstonesWithoutIdentityReuse()
    {
        var owner = new CargoDebrisFields(1);
        DebrisFieldId first = Add(owner, 0, 10, 1);
        DebrisFieldId alias = Add(owner, 20, 10, 2);
        owner.Apply(owner.PrepareRemoval(new(1))!);
        owner.Apply(owner.PrepareRemoval(new(2))!);
        Assert.Empty(owner.CaptureFields());
        Assert.Null(owner.Resolve(first));
        Assert.Null(owner.Resolve(alias));
        Assert.Empty(owner.Members(alias));
        Assert.Single(owner.CaptureCheckpoint().Tombstones);
        Assert.Single(owner.CaptureCheckpoint().Aliases);
        DebrisFieldId later = Add(owner, 0, 10, 3);
        Assert.Equal(new DebrisFieldId(3), later);
        Assert.Equal(later, owner.Resolve(later));
        Assert.Null(owner.Resolve(alias));
    }

    [Fact]
    public void SystemsAndExactCircleGeometryRejectFalseBoxOverlap()
    {
        var owner = new CargoDebrisFields(1);
        Add(owner, 0, 10, 1);
        Add(owner, 19, 10, 2, y: 19);
        Add(owner, 0, 10, 3, system: 2);
        Assert.Equal(3, owner.CaptureFields().Count);
        Assert.Null(owner.PrepareRemoval(new(99)));
    }

    [Fact]
    public void LargeCoordinatesAndRadiusUseWideExactDistance()
    {
        var owner = new CargoDebrisFields(1);
        long limit = 1L << 50;
        var a = owner.PrepareOccurrence(Position(-limit), (ulong)limit, [new(1)]);
        owner.Apply(a!);
        var b = owner.PrepareOccurrence(Position(limit), (ulong)limit, [new(2)]);
        owner.Apply(b!);
        Assert.Single(owner.CaptureFields());
    }

    [Fact]
    public void InvalidOrDuplicateMembershipCannotAllocate()
    {
        var owner = new CargoDebrisFields(1);
        Assert.Throws<ArgumentException>(() => owner.PrepareOccurrence(Position(0), 10, [new(1), new(1)]));
        Assert.Throws<ArgumentException>(() => owner.PrepareOccurrence(Position(0), 10, [default]));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.PrepareOccurrence(Position(0), (1UL << 50) + 1, [new(1)]));
        Add(owner, 0, 10, 1);
        Assert.Throws<ArgumentException>(() => owner.PrepareOccurrence(Position(100), 10, [new(1)]));
        Assert.Single(owner.CaptureFields());
    }

    [Fact]
    public void CheckpointRestoresFieldsAliasesTombstonesAndContinuation()
    {
        var owner = new CargoDebrisFields(1);
        DebrisFieldId first = Add(owner, 0, 10, 1);
        DebrisFieldId alias = Add(owner, 20, 10, 2);
        DebrisFieldId empty = Add(owner, 100, 10, 3);
        owner.Apply(owner.PrepareRemoval(new(3))!);
        CargoDebrisFieldsCheckpoint checkpoint = owner.CaptureCheckpoint();
        var result = CargoDebrisFields.Restore(checkpoint);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        var restored = result.Value!;
        Assert.Equal(first, restored.Resolve(alias));
        Assert.Null(restored.Resolve(empty));
        Assert.Equal(Fingerprint(owner), Fingerprint(restored));
        Assert.Equal(Add(owner, 40, 10, 4), Add(restored, 40, 10, 4));
        Assert.Equal(Fingerprint(owner), Fingerprint(restored));
    }

    [Fact]
    public void MalformedCheckpointRejectsBeforePublishingOwner()
    {
        var owner = new CargoDebrisFields(1);
        Add(owner, 0, 10, 1);
        Add(owner, 20, 10, 2);
        CargoDebrisFieldsCheckpoint state = owner.CaptureCheckpoint();
        Assert.False(CargoDebrisFields.Restore(state with { FieldIds = new(1) }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Aliases = [new(new(2), new(2))] }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Aliases = [new(new(1), new(2))] }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Aliases = [] }).IsSuccess);
        CargoDebrisFieldSnapshot field = state.Fields[0];
        Assert.False(CargoDebrisFields.Restore(state with { Fields = [field with { Members = [new(1), new(1)] }] }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Fields = [field with { Circles = [] }] }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Tombstones = [new(1)] }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Fields = [field with { Members = [] }] }).IsSuccess);
        Assert.False(CargoDebrisFields.Restore(state with { Fields = [field with
        {
            Circles = [field.Circles[0], field.Circles[1] with { Center = Position(1000) }],
        }] }).IsSuccess);
    }

    [Fact]
    public void ReorderedParallelPreparationsMatchSerialReferenceWhenCommittedInCausalOrder()
    {
        var reference = new CargoDebrisFields(1);
        for (int i = 0; i < 24; i++) Add(reference, i * 20, 10, (ulong)i + 1);
        foreach (int workers in new[] { 1, 2, 4 })
        {
            var owner = new CargoDebrisFields(1);
            for (int i = 0; i < 24; i++)
            {
                var proposals = new PreparedCargoDebrisMerge?[6];
                Parallel.For(0, proposals.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, index =>
                    proposals[index] = owner.PrepareOccurrence(Position(i * 20), 10, [new((ulong)i + 1)]));
                owner.Apply(proposals[0]!);
                Assert.All(proposals.Skip(1), proposal => Assert.False(owner.CanCommit(proposal!)));
            }
            Assert.Equal(Fingerprint(reference), Fingerprint(owner));
        }
    }

    [Fact]
    public void IndexedCandidatesMatchSerialBoxesAcrossInsertionsAndRetirements()
    {
        var index = new CargoDebrisCircleIndex();
        CargoDebrisCircle[] circles = Enumerable.Range(0, 128).Select(i => new CargoDebrisCircle(new((ulong)i + 1),
            Position((i % 16) * 31 - 240, (i / 16) * 27 - 100, (ulong)(i % 3) + 1), (ulong)(i % 7) * 13)).ToArray();
        int[] insertionOrder = Enumerable.Range(0, circles.Length).Select(i => i * 53 % circles.Length).ToArray();
        foreach (int i in insertionOrder) index.Add(circles[i]);
        Check(circles);
        foreach (int i in insertionOrder.Where(i => i % 2 == 0)) index.Remove(circles[i]);
        Check(circles.Where(circle => circle.OriginalFieldId.Value % 2 == 0).ToArray());

        void Check(CargoDebrisCircle[] expected)
        {
            foreach (CargoDebrisCircle query in circles)
            {
                DebrisFieldId[] serial = expected.Where(circle => BoxesTouch(circle, query))
                    .Select(circle => circle.OriginalFieldId).OrderBy(id => id.Value).ToArray();
                Assert.Equal(serial, index.Query(query).Select(circle => circle.OriginalFieldId).OrderBy(id => id.Value));
            }
        }

        static bool BoxesTouch(CargoDebrisCircle a, CargoDebrisCircle b) => a.Center.SystemId == b.Center.SystemId
            && Math.Abs(a.Center.Position.X.Units - b.Center.Position.X.Units) <= (long)(a.RadiusMeters + b.RadiusMeters)
            && Math.Abs(a.Center.Position.Y.Units - b.Center.Position.Y.Units) <= (long)(a.RadiusMeters + b.RadiusMeters);
    }

    [Fact]
    public void RemovingAllFieldsClearsEveryIndexedCircleBeforeLaterOccurrences()
    {
        var owner = new CargoDebrisFields(1);
        for (int i = 0; i < 48; i++) Add(owner, i * 100, 10, (ulong)i + 1);
        for (int i = 47; i >= 0; i--) owner.Apply(owner.PrepareRemoval(new((ulong)i + 1))!);
        Assert.Empty(owner.CaptureFields());
        Assert.Equal(48, owner.CaptureCheckpoint().Tombstones.Count);
        DebrisFieldId later = Add(owner, 2400, 10000, 99);
        Assert.Equal(new DebrisFieldId(49), later);
        Assert.Single(owner.CaptureFields());
        Assert.Single(Assert.Single(owner.CaptureFields()).Circles);
        Assert.Equal(later, owner.Resolve(later));
    }

    private static DebrisFieldId Add(CargoDebrisFields owner, long x, ulong radius, ulong member, long y = 0, ulong system = 1)
    {
        var prepared = owner.PrepareOccurrence(Position(x, y, system), radius, [new(member)])!;
        owner.Apply(prepared);
        return prepared.OriginalFieldId;
    }

    private static SystemPosition Position(long x, long y = 0, ulong system = 1) => new(new(system), new(new(x), new(y)));

    private static string Fingerprint(CargoDebrisFields owner)
    {
        CargoDebrisFieldsCheckpoint state = owner.CaptureCheckpoint();
        return $"{state.FieldIds.NextValue}/{state.Revision}/"
            + string.Join(";", state.Fields.Select(field => $"{field.Id}:" + string.Join(",", field.Members) + ":"
                + string.Join(",", field.Circles.Select(circle => $"{circle.OriginalFieldId}@{circle.Center}/{circle.RadiusMeters}"))))
            + "/" + string.Join(";", state.Aliases) + "/" + string.Join(";", state.Tombstones);
    }
}
