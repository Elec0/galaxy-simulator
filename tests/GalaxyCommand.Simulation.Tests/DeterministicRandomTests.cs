using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DeterministicRandomTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(RandomRootSeed.ByteCount - 1)]
    [InlineData(RandomRootSeed.ByteCount + 1)]
    public void RootSeedRejectsAnySizeOtherThan256Bits(int byteCount)
    {
        var bytes = new byte[byteCount];

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => RandomRootSeed.FromBytes(bytes));

        Assert.Equal("bytes", error.ParamName);
    }

    [Fact]
    public void RootSeedCopiesCallerOwnedBytes()
    {
        var bytes = new byte[RandomRootSeed.ByteCount];
        RandomRootSeed root = RandomRootSeed.FromBytes(bytes);
        bytes[0] = 1;
        var key = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            0,
            "primary");

        ulong actual = DeterministicRandomDerivation.DeriveUInt64(root, key);
        ulong expected = DeterministicRandomDerivation.DeriveUInt64(
            RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]),
            key);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StreamDerivationMatchesCanonicalSha256Vector()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");

        Xoshiro256State state = DeterministicRandomDerivation.CreateStreamState(
            root,
            key);

        Assert.Equal(0x51ee65e2010bc1e9UL, state.S0);
        Assert.Equal(0x0f22a5be91f876bcUL, state.S1);
        Assert.Equal(0xa1f4b3ef589f5f32UL, state.S2);
        Assert.Equal(0x25523569f2bc6595UL, state.S3);
        Assert.Equal(0UL, state.NextPosition);
    }

    [Fact]
    public void StatelessSampleMatchesCanonicalSha256Vector()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var key = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            3,
            "primary");

        ulong value = DeterministicRandomDerivation.DeriveUInt64(root, key);

        Assert.Equal(0x6054edbd1e96a354UL, value);
    }

    [Fact]
    public void StatefulBoundedSampleRejectsBiasedCandidate()
    {
        Xoshiro256State state = Xoshiro256StarStar.Next(
            new Xoshiro256State(1, 2, 3, 4, 0)).NextState;

        RandomBoundedStep step = DeterministicRandomSampling.NextBelow(state, 10);

        Assert.Equal(0UL, step.Value);
        Assert.Equal(3UL, step.NextState.NextPosition);
    }

    [Fact]
    public void StatelessBoundedSampleUsesNamedRetryDerivation()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var key = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            3,
            "primary");

        ulong value = DeterministicRandomDerivation.DeriveBelow(
            root,
            key,
            (1UL << 63) + 1);

        Assert.Equal(5_906_549_544_399_655_407UL, value);
    }

    [Theory]
    [InlineData(0UL, false)]
    [InlineData(1UL, true)]
    public void StatelessRatioSupportsExactBoundaryProbabilities(
        ulong numerator,
        bool expected)
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var key = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            3,
            "primary");

        bool value = DeterministicRandomDerivation.TestRatio(root, key, numerator, 1);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void EveryStatelessNamespaceFieldIsolatedFromTheBaselineSample()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var baseline = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            3,
            "primary");
        RandomSampleKey[] variants =
        [
            baseline with { Scope = RandomScope.NewGameGeneration },
            baseline with { DomainKind = "economy" },
            baseline with { OwnerKind = "ship" },
            baseline with { OwnerId = 43 },
            baseline with { DecisionKind = "defend" },
            baseline with { DecisionId = 8 },
            baseline with { PurposeId = "damage" },
            baseline with { Attempt = 4 },
            baseline with { SampleId = "secondary" },
        ];
        ulong expected = DeterministicRandomDerivation.DeriveUInt64(root, baseline);

        ulong[] actual = variants
            .Select(key => DeterministicRandomDerivation.DeriveUInt64(root, key))
            .ToArray();

        Assert.All(actual, value => Assert.NotEqual(expected, value));
        Assert.Equal(actual.Length, actual.Distinct().Count());
    }

    [Fact]
    public void StatelessSampleIsIsolatedByRootSeed()
    {
        var firstBytes = new byte[RandomRootSeed.ByteCount];
        var secondBytes = new byte[RandomRootSeed.ByteCount];
        secondBytes[0] = 1;
        var key = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            3,
            "primary");

        ulong first = DeterministicRandomDerivation.DeriveUInt64(
            RandomRootSeed.FromBytes(firstBytes),
            key);
        ulong second = DeterministicRandomDerivation.DeriveUInt64(
            RandomRootSeed.FromBytes(secondBytes),
            key);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AddingUnrelatedStreamDoesNotShiftExistingStream()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var existing = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var unrelated = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "economy",
            "station",
            9,
            "market-pricing");
        var existingOnly = new DeterministicRandomOwner(root);
        existingOnly.RegisterStream(existing);
        var withUnrelated = new DeterministicRandomOwner(root);
        withUnrelated.RegisterStream(unrelated);
        withUnrelated.RegisterStream(existing);

        Assert.Equal(
            existingOnly.EvaluateNextUInt64(existing),
            withUnrelated.EvaluateNextUInt64(existing));
    }

    [Fact]
    public void OwnerEvaluatesStatelessCapabilitiesWithoutCreatingAStream()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomSampleKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "attack",
            7,
            "hit",
            0,
            "primary");
        var owner = new DeterministicRandomOwner(root);

        ulong raw = owner.DeriveUInt64(key);
        ulong bounded = owner.DeriveBelow(key, 9);
        bool ratio = owner.TestRatio(key, 1, 3);

        Assert.Equal(DeterministicRandomDerivation.DeriveUInt64(root, key), raw);
        Assert.InRange(bounded, 0UL, 8UL);
        Assert.Equal(
            DeterministicRandomDerivation.TestRatio(root, key, 1, 3),
            ratio);
        Assert.Empty(owner.CaptureCheckpoint().Streams);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void StatelessSamplesAreIndependentOfWorkerCount(int workerCount)
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        RandomSampleKey[] keys = Enumerable.Range(0, 256)
            .Select(value => new RandomSampleKey(
                RandomScope.SessionRuntime,
                "combat",
                "engagement",
                (ulong)value,
                "attack",
                7,
                "hit",
                0,
                "primary"))
            .ToArray();
        ulong[] expected = keys
            .Select(key => DeterministicRandomDerivation.DeriveUInt64(root, key))
            .ToArray();

        ulong[] actual = keys
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(workerCount)
            .Select(key => DeterministicRandomDerivation.DeriveUInt64(root, key))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void StatelessSamplesAreIndependentOfBatchSize(int batchSize)
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        RandomSampleKey[] keys = CreateSampleKeys(257);
        ulong[] expected = keys
            .Select(key => DeterministicRandomDerivation.DeriveUInt64(root, key))
            .ToArray();

        ulong[] actual = keys
            .Chunk(batchSize)
            .SelectMany(batch => batch.Select(
                key => DeterministicRandomDerivation.DeriveUInt64(root, key)))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(11)]
    public void StatelessSamplesAreIndependentOfValidPartitionLayout(int partitionCount)
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        RandomSampleKey[] keys = CreateSampleKeys(257);
        ulong[] expected = keys
            .Select(key => DeterministicRandomDerivation.DeriveUInt64(root, key))
            .ToArray();

        ulong[] actual = keys
            .Select((key, index) => (key, index))
            .GroupBy(item => item.index % partitionCount)
            .SelectMany(partition => partition.Select(item => (
                item.index,
                value: DeterministicRandomDerivation.DeriveUInt64(root, item.key))))
            .OrderBy(item => item.index)
            .Select(item => item.value)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0UL, false)]
    [InlineData(1UL, true)]
    public void RationalProbabilityConsumesOneCandidateAtBoundary(
        ulong numerator,
        bool expected)
    {
        var state = new Xoshiro256State(1, 2, 3, 4, 0);

        RandomBooleanStep step = DeterministicRandomSampling.TestRatio(
            state,
            numerator,
            denominator: 1);

        Assert.Equal(expected, step.Value);
        Assert.Equal(1UL, step.NextState.NextPosition);
    }

    [Fact]
    public void RationalProbabilityRejectsInvalidRatio()
    {
        var state = new Xoshiro256State(1, 2, 3, 4, 0);

        ArgumentOutOfRangeException zeroDenominator =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeterministicRandomSampling.TestRatio(state, 0, 0));
        ArgumentOutOfRangeException excessiveNumerator =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeterministicRandomSampling.TestRatio(state, 2, 1));

        Assert.Equal("denominator", zeroDenominator.ParamName);
        Assert.Equal("numerator", excessiveNumerator.ParamName);
    }

    [Fact]
    public void DiscardedProposalDoesNotAdvanceOwnedStream()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);

        RandomStreamProposal discarded = owner.EvaluateNextUInt64(key);
        RandomStreamProposal repeated = owner.EvaluateNextUInt64(key);
        owner.Commit(repeated);
        RandomStreamProposal afterCommit = owner.EvaluateNextUInt64(key);

        Assert.Equal(discarded.Value, repeated.Value);
        Assert.Equal(discarded.NextState, repeated.NextState);
        Assert.NotEqual(repeated.Value, afterCommit.Value);
        Assert.Equal(1UL, afterCommit.ExpectedState.NextPosition);
    }

    [Fact]
    public void OwnerRejectsProposalAfterExpectedStateHasChanged()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);
        RandomStreamProposal first = owner.EvaluateNextUInt64(key);
        RandomStreamProposal stale = owner.EvaluateNextUInt64(key);
        owner.Commit(first);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => owner.Commit(stale));

        Assert.Contains("no longer matches", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetirementRemovesStreamOnlyWhenCommitted()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);
        RandomStreamRetirementProposal proposal = owner.EvaluateRetirement(key);
        RandomStreamProposal pending = owner.EvaluateNextUInt64(key);

        Assert.Single(owner.CaptureCheckpoint().Streams);
        owner.Commit(proposal);
        Assert.Empty(owner.CaptureCheckpoint().Streams);
        Assert.Throws<InvalidOperationException>(() => owner.Commit(pending));
        Assert.Throws<KeyNotFoundException>(() => owner.EvaluateNextUInt64(key));
    }

    [Fact]
    public void RetirementRejectsStateThatChangedAfterEvaluation()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);
        RandomStreamRetirementProposal stale = owner.EvaluateRetirement(key);
        owner.Commit(owner.EvaluateNextUInt64(key));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => owner.Commit(stale));

        Assert.Contains("no longer matches", error.Message, StringComparison.Ordinal);
        Assert.Single(owner.CaptureCheckpoint().Streams);
    }

    [Fact]
    public void OwnedBoundedProposalAdvancesOnlyWhenCommitted()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "target-selection");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);

        RandomStreamProposal proposal = owner.EvaluateNextBelow(key, 7);
        RandomStreamProposal repeated = owner.EvaluateNextBelow(key, 7);

        Assert.InRange(proposal.Value, 0UL, 6UL);
        Assert.Equal(proposal, repeated);
        owner.Commit(proposal);
        Assert.NotEqual(proposal, owner.EvaluateNextBelow(key, 7));
    }

    [Fact]
    public void OwnedRatioProposalCommitsTheEvaluatedCandidate()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "hit-check");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);

        RandomBooleanProposal proposal = owner.EvaluateTestRatio(key, 1, 3);
        owner.Commit(proposal);

        Assert.Equal(1UL, proposal.NextState.NextPosition);
        Assert.Equal(proposal.NextState, owner.EvaluateNextUInt64(key).ExpectedState);
    }

    [Fact]
    public void RestoredOwnerContinuesAtExactNextOutput()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var uninterrupted = new DeterministicRandomOwner(root);
        uninterrupted.RegisterStream(key);
        uninterrupted.Commit(uninterrupted.EvaluateNextUInt64(key));
        DeterministicRandomCheckpoint checkpoint = uninterrupted.CaptureCheckpoint();

        CheckpointResult<DeterministicRandomOwner> restored =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.True(restored.IsSuccess);
        Assert.Equal(
            uninterrupted.EvaluateNextUInt64(key),
            restored.Value!.EvaluateNextUInt64(key));
    }

    [Fact]
    public void RestoreAcceptsStreamDeclaredByOwningDomain()
    {
        DeterministicRandomCheckpoint checkpoint = CreateRandomCheckpoint();
        RandomStreamKey key = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(checkpoint.Streams)).Key!;
        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(
                checkpoint,
                new HashSet<RandomStreamKey> { key });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void RestoreRejectsStreamAbsentFromOwningDomainDeclarations()
    {
        DeterministicRandomCheckpoint checkpoint = CreateRandomCheckpoint();

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(
                checkpoint,
                new HashSet<RandomStreamKey>());

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].key", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsOwningDomainDeclarationWithoutSavedStream()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var checkpoint = new DeterministicRandomOwner(root).CaptureCheckpoint();
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(
                checkpoint,
                new HashSet<RandomStreamKey> { key });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsUnsupportedDerivationAlgorithm()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        var unsupported = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            "gc.unknown.v1",
            valid.DerivationVersion,
            valid.Streams);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(unsupported);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.derivation", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsMalformedRootSeed()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        var malformed = new DeterministicRandomCheckpoint(
            new byte[RandomRootSeed.ByteCount - 1],
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            valid.Streams);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(malformed);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.rootSeed", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsUnsupportedStreamAlgorithm()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        RandomStreamCheckpoint stream = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(valid.Streams));
        var unsupported = new RandomStreamCheckpoint(
            stream.Key,
            "gc.unknown.v1",
            stream.GeneratorVersion,
            stream.S0,
            stream.S1,
            stream.S2,
            stream.S3,
            stream.NextPosition);
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [unsupported]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].algorithm", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsMissingStreamEntry()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [null]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0]", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsMissingStreamKey()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        RandomStreamCheckpoint stream = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(valid.Streams));
        var missingKey = new RandomStreamCheckpoint(
            null,
            stream.GeneratorAlgorithm,
            stream.GeneratorVersion,
            stream.S0,
            stream.S1,
            stream.S2,
            stream.S3,
            stream.NextPosition);
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [missingKey]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].key", result.Failure!.Path);
    }

    [Theory]
    [InlineData(null, "engagement", "resolution")]
    [InlineData("combat", null, "resolution")]
    [InlineData("combat", "engagement", null)]
    public void RestoreRejectsIncompleteStreamKey(
        string? domainKind,
        string? ownerKind,
        string? purposeId)
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        RandomStreamCheckpoint stream = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(valid.Streams));
        var incomplete = new RandomStreamCheckpoint(
            new RandomStreamKey(
                RandomScope.SessionRuntime,
                domainKind!,
                ownerKind!,
                42,
                purposeId!),
            stream.GeneratorAlgorithm,
            stream.GeneratorVersion,
            stream.S0,
            stream.S1,
            stream.S2,
            stream.S3,
            stream.NextPosition);
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [incomplete]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].key", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsUnknownRandomScope()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        RandomStreamCheckpoint stream = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(valid.Streams));
        var unknownScope = new RandomStreamCheckpoint(
            stream.Key! with { Scope = (RandomScope)99 },
            stream.GeneratorAlgorithm,
            stream.GeneratorVersion,
            stream.S0,
            stream.S1,
            stream.S2,
            stream.S3,
            stream.NextPosition);
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [unknownScope]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].key", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsAllZeroStreamState()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        RandomStreamCheckpoint stream = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(valid.Streams));
        var allZero = new RandomStreamCheckpoint(
            stream.Key,
            stream.GeneratorAlgorithm,
            stream.GeneratorVersion,
            0,
            0,
            0,
            0,
            stream.NextPosition);
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [allZero]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].state", result.Failure!.Path);
    }

    [Fact]
    public void RestoreRejectsDuplicateLiveStreamKey()
    {
        DeterministicRandomCheckpoint valid = CreateRandomCheckpoint();
        RandomStreamCheckpoint stream = Assert.IsType<RandomStreamCheckpoint>(
            Assert.Single(valid.Streams));
        var checkpoint = new DeterministicRandomCheckpoint(
            valid.RootSeed,
            valid.DerivationAlgorithm,
            valid.DerivationVersion,
            [stream, stream]);

        CheckpointResult<DeterministicRandomOwner> result =
            DeterministicRandomOwner.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[1].key", result.Failure!.Path);
    }

    [Fact]
    public void XoshiroStepMatchesPublishedReferenceTransition()
    {
        var state = new Xoshiro256State(1, 2, 3, 4, 0);
        ulong[] expected =
        [
            0x0000000000002d00,
            0x0000000000000000,
            0x000000005a007080,
            0x10e0000000009d80,
            0x10e0b61ce1009d80,
            0x0870021ce143ad00,
            0xe071c3c2e143f089,
            0x75a1690ef7a20380,
        ];

        foreach (ulong value in expected)
        {
            RandomStep step = Xoshiro256StarStar.Next(state);
            Assert.Equal(value, step.Value);
            state = step.NextState;
        }

        Assert.Equal(8UL, state.NextPosition);
    }

    [Fact]
    public void ExhaustedStreamRejectsAnotherOutput()
    {
        var last = new Xoshiro256State(1, 2, 3, 4, ulong.MaxValue);
        Xoshiro256State exhausted = Xoshiro256StarStar.Next(last).NextState;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Xoshiro256StarStar.Next(exhausted));

        Assert.Null(exhausted.NextPosition);
        Assert.Contains("exhausted", error.Message, StringComparison.Ordinal);
    }

    private static RandomSampleKey[] CreateSampleKeys(int count) =>
        Enumerable.Range(0, count)
            .Select(value => new RandomSampleKey(
                RandomScope.SessionRuntime,
                "combat",
                "engagement",
                (ulong)value,
                "attack",
                7,
                "hit",
                0,
                "primary"))
            .ToArray();

    private static DeterministicRandomCheckpoint CreateRandomCheckpoint()
    {
        RandomRootSeed root = RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);
        var key = new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution");
        var owner = new DeterministicRandomOwner(root);
        owner.RegisterStream(key);
        return owner.CaptureCheckpoint();
    }
}
