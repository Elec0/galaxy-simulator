using System.Numerics;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDispositionGenerationTests
{
    private static readonly QualifiedContentKey Ore = QualifiedContentKey.Create("test.cargo", "commodity", "ore");

    [Fact]
    public void SharesArePositiveCappedAndConserveEveryDroppedUnit()
    {
        for (ulong quantity = 0; quantity <= 40; quantity++)
        for (int count = 1; count <= 8; count++)
        {
            Quantity[] shares = Create().Partition(new Quantity(quantity), count);
            Assert.Equal((int)Math.Min(quantity, (ulong)count), shares.Length);
            Assert.All(shares, share => Assert.True(share.Units > 0));
            Assert.Equal((BigInteger)quantity, shares.Aggregate(BigInteger.Zero, (sum, share) => sum + share.Units));
        }
    }

    [Fact]
    public void LargeQuantityUsesSmallObjectCountWithoutEnumeratingUnits()
    {
        Quantity[] shares = Create().Partition(new Quantity(ulong.MaxValue), 4);
        Assert.Equal(4, shares.Length);
        Assert.All(shares, share => Assert.True(share.Units > 0));
        Assert.Equal((BigInteger)ulong.MaxValue, shares.Aggregate(BigInteger.Zero, (sum, share) => sum + share.Units));
    }

    [Fact]
    public void DiscreteSelectionRetainsOnlyOriginalUniqueIdsInCanonicalOrder()
    {
        ItemInstanceId[] input = [new(9), new(2), new(7), new(1), new(5)];
        ItemInstanceId[] original = input.ToArray();
        ItemInstanceId[] survivors = Create().SelectSurvivors(input, 2);
        Assert.Equal(2, survivors.Length);
        Assert.Equal(survivors.OrderBy(id => id.Value), survivors);
        Assert.Equal(2, survivors.Distinct().Count());
        Assert.All(survivors, id => Assert.Contains(id, original));
        Assert.Equal(original, input);
        Assert.Equal(survivors, Create().SelectSurvivors(input.Reverse().ToArray(), 2));
        Assert.Empty(Create().SelectSurvivors(input, 0));
        Assert.Equal(input.OrderBy(id => id.Value), Create().SelectSurvivors(input, input.Length));
    }

    [Fact]
    public void SurvivorSamplingDoesNotAlwaysPreferOldestInstances()
    {
        ItemInstanceId[] input = Enumerable.Range(1, 10).Select(value => new ItemInstanceId((ulong)value)).ToArray();
        var selected = new HashSet<ItemInstanceId>();
        for (ulong occurrence = 0; occurrence < 50; occurrence++)
            selected.UnionWith(Create(occurrence).SelectSurvivors(input, 5));
        Assert.Equal(input.Length, selected.Count);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 10000)]
    [InlineData(1125899906842624, 1125899906842624, 10000)]
    [InlineData(-1125899906842624, -1125899906842624, 10000)]
    [InlineData(0, 0, 1125899906842624)]
    public void ScatterStaysInDiskAndCoordinateEnvelope(long x, long y, long radius)
    {
        var source = new SystemPosition(new SystemId(1), new SpatialPosition(new(x), new(y)));
        for (ulong ordinal = 0; ordinal < 80; ordinal++)
        {
            SystemPosition position = Create().Scatter(source, radius, ordinal);
            Assert.Equal(source.SystemId, position.SystemId);
            BigInteger dx = (BigInteger)position.Position.X.Units - x;
            BigInteger dy = (BigInteger)position.Position.Y.Units - y;
            Assert.True(dx * dx + dy * dy <= (BigInteger)radius * radius);
            Assert.InRange(position.Position.X.Units, -(1L << 50), 1L << 50);
            Assert.InRange(position.Position.Y.Units, -(1L << 50), 1L << 50);
        }
    }

    [Fact]
    public void EvaluationOrderAndParallelBatchesCannotChangeNamedSamples()
    {
        var source = new SystemPosition(new SystemId(1), new SpatialPosition(new(17), new(-23)));
        var generator = Create();
        SystemPosition[] reference = Enumerable.Range(0, 40).Select(i => generator.Scatter(source, 10000, (ulong)i)).ToArray();
        var actual = new SystemPosition[reference.Length];
        foreach (int workers in new[] { 1, 2, 4 })
        {
            Parallel.For(0, reference.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, i =>
            {
                int ordinal = reference.Length - i - 1;
                actual[ordinal] = generator.Scatter(source, 10000, (ulong)ordinal);
            });
            Assert.Equal(reference, actual);
        }

        Quantity[] shares = generator.Partition(new Quantity(999), 4);
        generator.Scatter(source, 10000, 0);
        generator.SelectSurvivors([new(1), new(2), new(3)], 1);
        Assert.Equal(shares, generator.Partition(new Quantity(999), 4));
    }

    [Fact]
    public void SurvivorAndShareBatchesMatchSerialReferenceAcrossWorkersAndBatchSizes()
    {
        ItemInstanceId[] input = Enumerable.Range(1, 12).Select(i => new ItemInstanceId((ulong)i)).ToArray();
        string Evaluate(int i)
        {
            var generator = Create((ulong)i);
            return string.Join(",", generator.Partition(new Quantity(1000000), 4).Select(q => q.Units))
                + "/" + string.Join(",", generator.SelectSurvivors(input, 6).Select(id => id.Value));
        }

        string[] reference = Enumerable.Range(0, 24).Select(Evaluate).ToArray();
        foreach (int workers in new[] { 1, 2, 4 })
        foreach (int batchSize in new[] { 1, 3, 8 })
        {
            var actual = new string[reference.Length];
            Parallel.For(0, reference.Length / batchSize, new ParallelOptions { MaxDegreeOfParallelism = workers }, batch =>
            {
                for (int offset = 0; offset < batchSize; offset++)
                {
                    int index = reference.Length - 1 - (batch * batchSize + offset);
                    actual[index] = Evaluate(index);
                }
            });
            Assert.Equal(reference, actual);
        }
    }

    [Fact]
    public void SamplingCanReachEverySmallPartitionAndDiskPoint()
    {
        var partitions = new HashSet<string>();
        var positions = new HashSet<SystemPosition>();
        var source = new SystemPosition(new SystemId(1), new SpatialPosition(new(0), new(0)));
        for (ulong occurrence = 0; occurrence < 200; occurrence++)
        {
            var generator = Create(occurrence);
            partitions.Add(string.Join(",", generator.Partition(new Quantity(5), 3).Select(q => q.Units)));
            positions.Add(generator.Scatter(source, 1, 0));
        }
        Assert.Equal(6, partitions.Count);
        Assert.Equal(5, positions.Count);
    }

    [Fact]
    public void CauseNamespacesAndDefinitionIdentitySeparateSamples()
    {
        var random = new DeterministicRandomOwner(RandomRootSeed.FromBytes(new byte[32]));
        var command = new CargoDispositionGeneration(random, new EntityId(1), new CommandSequence(1), 0, 1, Ore);
        var scheduled = new CargoDispositionGeneration(random, new EntityId(1), new EventKey(new(9), EventPhase.StateUpdate, 1), 0, 1, Ore);
        var otherType = new CargoDispositionGeneration(random, new EntityId(1), new CommandSequence(1), 0, 1,
            QualifiedContentKey.Create("test.cargo", "commodity", "ice"));
        Quantity[] shares = command.Partition(new Quantity(1000000), 4);
        Assert.False(shares.SequenceEqual(scheduled.Partition(new Quantity(1000000), 4)));
        Assert.False(shares.SequenceEqual(otherType.Partition(new Quantity(1000000), 4)));
        Assert.Empty(random.CaptureCheckpoint().Streams);
    }

    [Fact]
    public void RejectsInvalidInputsBeforeGeneration()
    {
        var generator = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Partition(new Quantity(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.SelectSurvivors([new(1)], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.SelectSurvivors([new(1)], -1));
        Assert.Throws<ArgumentException>(() => generator.SelectSurvivors([new(1), new(1)], 1));
        Assert.Throws<ArgumentException>(() => generator.SelectSurvivors([default], 0));
        var source = new SystemPosition(new SystemId(1), new SpatialPosition(new(0), new(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Scatter(source, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Scatter(source, (1L << 50) + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Scatter(new SystemPosition(new(1), new(new(long.MaxValue), new(0))), 0, 0));
    }

    private static CargoDispositionGeneration Create(ulong occurrence = 0) => new(
        new DeterministicRandomOwner(RandomRootSeed.FromBytes(new byte[32])),
        new EntityId(1), new CommandSequence(1), occurrence, 1, Ore);
}
