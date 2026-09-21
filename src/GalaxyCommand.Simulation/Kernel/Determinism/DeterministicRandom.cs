using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Immutable resolved 256-bit seed used as the root of authoritative random
/// derivation.
/// </summary>
public sealed class RandomRootSeed
{
    public const int ByteCount = 32;

    private readonly byte[] _bytes;

    private RandomRootSeed(byte[] bytes) => _bytes = bytes;

    /// <summary>
    /// Copies exactly 256 bits into a resolved seed, rejecting missing or
    /// differently sized input.
    /// </summary>
    public static RandomRootSeed FromBytes(IEnumerable<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        byte[] values = bytes.ToArray();
        if (values.Length != ByteCount)
        {
            throw new ArgumentException(
                $"Random root seed must contain exactly {ByteCount} bytes.",
                nameof(bytes));
        }

        return new RandomRootSeed(values);
    }

    internal ReadOnlySpan<byte> Bytes => _bytes;

    internal byte[] ToArray() => _bytes.ToArray();
}

/// <summary>
/// Separates pre-session generation from authoritative runtime decisions.
/// </summary>
public enum RandomScope
{
    /// <summary>
    /// Randomness used while composing a new game's initial state.
    /// </summary>
    NewGameGeneration = 1,

    /// <summary>
    /// Randomness used after the authoritative session exists.
    /// </summary>
    SessionRuntime = 2,
}

internal sealed record RandomStreamKey(
    RandomScope Scope,
    string DomainKind,
    string OwnerKind,
    ulong OwnerId,
    string PurposeId);

internal sealed record RandomSampleKey(
    RandomScope Scope,
    string DomainKind,
    string OwnerKind,
    ulong OwnerId,
    string DecisionKind,
    ulong DecisionId,
    string PurposeId,
    ulong Attempt,
    string SampleId);

internal sealed record RandomStreamProposal(
    RandomStreamKey Key,
    Xoshiro256State ExpectedState,
    ulong Value,
    Xoshiro256State NextState);

internal sealed record RandomBooleanProposal(
    RandomStreamKey Key,
    Xoshiro256State ExpectedState,
    bool Value,
    Xoshiro256State NextState);

internal sealed record RandomStreamRetirementProposal(
    RandomStreamKey Key,
    Xoshiro256State ExpectedState);

internal sealed class DeterministicRandomOwner
{
    private readonly RandomRootSeed _root;
    private readonly Dictionary<RandomStreamKey, Xoshiro256State> _streams = [];

    internal DeterministicRandomOwner(RandomRootSeed root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
    }

    /// <summary>
    /// Registers one live key after its domain has enforced owner-identity
    /// non-reuse; the random owner intentionally stores no retired tombstones.
    /// </summary>
    internal void RegisterStream(RandomStreamKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsStructurallyValid(key))
        {
            throw new ArgumentException(
                "Random stream key must contain a known scope and all text identities.",
                nameof(key));
        }

        _streams.Add(
            key,
            DeterministicRandomDerivation.CreateStreamState(_root, key));
    }

    internal ulong DeriveUInt64(RandomSampleKey key)
    {
        ValidateSampleKey(key);
        return DeterministicRandomDerivation.DeriveUInt64(_root, key);
    }

    internal ulong DeriveBelow(
        RandomSampleKey key,
        ulong exclusiveUpperBound)
    {
        ValidateSampleKey(key);
        return DeterministicRandomDerivation.DeriveBelow(
            _root,
            key,
            exclusiveUpperBound);
    }

    internal bool TestRatio(
        RandomSampleKey key,
        ulong numerator,
        ulong denominator)
    {
        ValidateSampleKey(key);
        return DeterministicRandomDerivation.TestRatio(
            _root,
            key,
            numerator,
            denominator);
    }

    internal RandomStreamProposal EvaluateNextUInt64(RandomStreamKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Xoshiro256State state = _streams[key];
        RandomStep step = Xoshiro256StarStar.Next(state);
        return new RandomStreamProposal(key, state, step.Value, step.NextState);
    }

    /// <summary>
    /// Evaluates an unbiased bounded sample against an immutable owned-stream
    /// snapshot, leaving all accepted and rejected candidates uncommitted.
    /// </summary>
    internal RandomStreamProposal EvaluateNextBelow(
        RandomStreamKey key,
        ulong exclusiveUpperBound)
    {
        ArgumentNullException.ThrowIfNull(key);
        Xoshiro256State state = _streams[key];
        RandomBoundedStep step = DeterministicRandomSampling.NextBelow(
            state,
            exclusiveUpperBound);
        return new RandomStreamProposal(key, state, step.Value, step.NextState);
    }

    /// <summary>
    /// Evaluates an exact rational probability while preserving the owned
    /// stream's candidate consumption inside the returned proposal.
    /// </summary>
    internal RandomBooleanProposal EvaluateTestRatio(
        RandomStreamKey key,
        ulong numerator,
        ulong denominator)
    {
        ArgumentNullException.ThrowIfNull(key);
        Xoshiro256State state = _streams[key];
        RandomBooleanStep step = DeterministicRandomSampling.TestRatio(
            state,
            numerator,
            denominator);
        return new RandomBooleanProposal(key, state, step.Value, step.NextState);
    }

    /// <summary>
    /// Captures the exact live state that an owning aggregate proposes to
    /// retire without changing the stream registry during evaluation.
    /// </summary>
    internal RandomStreamRetirementProposal EvaluateRetirement(RandomStreamKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new RandomStreamRetirementProposal(key, _streams[key]);
    }

    /// <summary>
    /// Captures the root and every live stream in canonical key order without
    /// consuming random state.
    /// </summary>
    internal DeterministicRandomCheckpoint CaptureCheckpoint()
    {
        RandomStreamCheckpoint[] streams = _streams
            .OrderBy(pair => pair.Key.Scope)
            .ThenBy(pair => pair.Key.DomainKind, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.OwnerKind, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.OwnerId)
            .ThenBy(pair => pair.Key.PurposeId, StringComparer.Ordinal)
            .Select(pair => new RandomStreamCheckpoint(
                pair.Key,
                Xoshiro256StarStar.AlgorithmId,
                Xoshiro256StarStar.AlgorithmVersion,
                pair.Value.S0,
                pair.Value.S1,
                pair.Value.S2,
                pair.Value.S3,
                pair.Value.NextPosition))
            .ToArray();
        return new DeterministicRandomCheckpoint(
            _root.ToArray(),
            DeterministicRandomDerivation.AlgorithmId,
            DeterministicRandomDerivation.AlgorithmVersion,
            streams);
    }

    /// <summary>
    /// Restores exact stream states directly so continuation never depends on
    /// replaying historical draws from the root seed.
    /// </summary>
    internal static CheckpointResult<DeterministicRandomOwner> RestoreCheckpoint(
        DeterministicRandomCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(
                checkpoint.DerivationAlgorithm,
                DeterministicRandomDerivation.AlgorithmId,
                StringComparison.Ordinal)
            || checkpoint.DerivationVersion
                != DeterministicRandomDerivation.AlgorithmVersion)
        {
            return CheckpointResult<DeterministicRandomOwner>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.random.derivation",
                    "Random derivation algorithm or version is unsupported."));
        }

        if (checkpoint.RootSeed.Count != RandomRootSeed.ByteCount)
        {
            return CheckpointResult<DeterministicRandomOwner>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.random.rootSeed",
                    "Random root seed must contain exactly 32 bytes."));
        }

        RandomRootSeed root = RandomRootSeed.FromBytes(checkpoint.RootSeed);
        var owner = new DeterministicRandomOwner(root);
        for (int index = 0; index < checkpoint.Streams.Count; index++)
        {
            RandomStreamCheckpoint? stream = checkpoint.Streams[index];
            if (stream is null)
            {
                return CheckpointResult<DeterministicRandomOwner>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.random.streams[{index}]",
                        "Random stream entry is required."));
            }

            if (stream.Key is null || !IsStructurallyValid(stream.Key))
            {
                return CheckpointResult<DeterministicRandomOwner>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.random.streams[{index}].key",
                        "Random stream key is required."));
            }

            if (!string.Equals(
                    stream.GeneratorAlgorithm,
                    Xoshiro256StarStar.AlgorithmId,
                    StringComparison.Ordinal)
                || stream.GeneratorVersion != Xoshiro256StarStar.AlgorithmVersion)
            {
                return CheckpointResult<DeterministicRandomOwner>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.random.streams[{index}].algorithm",
                        "Random stream algorithm or version is unsupported."));
            }

            if ((stream.S0 | stream.S1 | stream.S2 | stream.S3) == 0)
            {
                return CheckpointResult<DeterministicRandomOwner>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.random.streams[{index}].state",
                        "Random stream state cannot be all zero."));
            }

            if (!owner._streams.TryAdd(
                    stream.Key,
                    new Xoshiro256State(
                        stream.S0,
                        stream.S1,
                        stream.S2,
                        stream.S3,
                        stream.NextPosition)))
            {
                return CheckpointResult<DeterministicRandomOwner>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.random.streams[{index}].key",
                        "Random stream key must be unique among live streams."));
            }
        }

        return CheckpointResult<DeterministicRandomOwner>.Success(owner);
    }

    /// <summary>
    /// Restores structurally valid state against the complete set of live keys
    /// declared by authoritative gameplay domains.
    /// </summary>
    internal static CheckpointResult<DeterministicRandomOwner> RestoreCheckpoint(
        DeterministicRandomCheckpoint checkpoint,
        IReadOnlySet<RandomStreamKey> declaredLiveStreams)
    {
        ArgumentNullException.ThrowIfNull(declaredLiveStreams);
        CheckpointResult<DeterministicRandomOwner> restored =
            RestoreCheckpoint(checkpoint);
        if (!restored.IsSuccess)
        {
            return restored;
        }

        for (int index = 0; index < checkpoint.Streams.Count; index++)
        {
            RandomStreamKey key = checkpoint.Streams[index]!.Key!;
            if (!declaredLiveStreams.Contains(key))
            {
                return CheckpointResult<DeterministicRandomOwner>.Rejected(
                    new CheckpointValidationFailure(
                        $"$.checkpoint.random.streams[{index}].key",
                        "Random stream has no declaration from an owning domain."));
            }
        }

        if (checkpoint.Streams.Count != declaredLiveStreams.Count)
        {
            return CheckpointResult<DeterministicRandomOwner>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.random.streams",
                    "An owning-domain random stream declaration has no saved state."));
        }

        return restored;
    }

    /// <summary>
    /// Checks only the random contract's structural requirements. Each owning
    /// domain remains responsible for its approved identifier grammar.
    /// </summary>
    private static bool IsStructurallyValid(RandomStreamKey key) =>
        Enum.IsDefined(key.Scope)
        && key.DomainKind is not null
        && key.OwnerKind is not null
        && key.PurposeId is not null;

    /// <summary>
    /// Prevents incomplete sample identities from reaching canonical encoding
    /// while leaving identifier grammar to each owning gameplay domain.
    /// </summary>
    private static void ValidateSampleKey(RandomSampleKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Enum.IsDefined(key.Scope)
            || key.DomainKind is null
            || key.OwnerKind is null
            || key.DecisionKind is null
            || key.PurposeId is null
            || key.SampleId is null)
        {
            throw new ArgumentException(
                "Random sample key must contain a known scope and all text identities.",
                nameof(key));
        }
    }

    /// <summary>
    /// Advances one owned stream only when the proposal was evaluated from its
    /// current snapshot, preventing stale parallel work from rewinding state.
    /// </summary>
    internal void Commit(RandomStreamProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        CommitState(proposal.Key, proposal.ExpectedState, proposal.NextState);
    }

    /// <summary>
    /// Advances one owned stream only when the boolean proposal still matches
    /// its current authoritative snapshot.
    /// </summary>
    internal void Commit(RandomBooleanProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        CommitState(proposal.Key, proposal.ExpectedState, proposal.NextState);
    }

    /// <summary>
    /// Removes a stream only at the owning aggregate's deterministic commit
    /// boundary, rejecting a proposal evaluated before its latest committed
    /// draw. Owner-reference validation remains the aggregate's duty.
    /// </summary>
    internal void Commit(RandomStreamRetirementProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        RequireCurrentState(proposal.Key, proposal.ExpectedState);
        _streams.Remove(proposal.Key);
    }

    private void CommitState(
        RandomStreamKey key,
        Xoshiro256State expectedState,
        Xoshiro256State nextState)
    {
        RequireCurrentState(key, expectedState);
        _streams[key] = nextState;
    }

    /// <summary>
    /// Rejects stale advancement and retirement proposals through the same
    /// exact-snapshot invariant.
    /// </summary>
    private void RequireCurrentState(
        RandomStreamKey key,
        Xoshiro256State expectedState)
    {
        if (!_streams.TryGetValue(key, out Xoshiro256State current)
            || current != expectedState)
        {
            throw new InvalidOperationException(
                "Random proposal expected state no longer matches its owned stream.");
        }
    }
}

internal static class DeterministicRandomDerivation
{
    internal const string AlgorithmId = "gc.sha256-derive.v1";
    internal const int AlgorithmVersion = 1;
    private static readonly byte[] Header = "GC-RANDOM\0"u8.ToArray();

    /// <summary>
    /// Derives a nonzero xoshiro state from the complete canonical stream key;
    /// the retry field exists only for the cryptographically improbable zero
    /// digest so ordinary version-1 vectors remain stable.
    /// </summary>
    internal static Xoshiro256State CreateStreamState(
        RandomRootSeed root,
        RandomStreamKey key)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(key);
        ulong retry = 0;
        while (true)
        {
            byte[] digest = DeriveStreamDigest(root, key, retry);
            ulong s0 = BinaryPrimitives.ReadUInt64LittleEndian(digest);
            ulong s1 = BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(8));
            ulong s2 = BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(16));
            ulong s3 = BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(24));
            if ((s0 | s1 | s2 | s3) != 0)
            {
                return new Xoshiro256State(s0, s1, s2, s3, 0);
            }

            retry = checked(retry + 1);
        }
    }

    internal static ulong DeriveUInt64(
        RandomRootSeed root,
        RandomSampleKey key)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(key);
        byte[] digest = DeriveSampleDigest(root, key, samplingRetry: 0);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    /// <summary>
    /// Maps a named sample into an unbiased bounded value by extending only
    /// that sample's derivation key when a candidate falls in the bias range.
    /// </summary>
    internal static ulong DeriveBelow(
        RandomRootSeed root,
        RandomSampleKey key,
        ulong exclusiveUpperBound)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfZero(exclusiveUpperBound);
        ulong threshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;
        ulong samplingRetry = 0;
        while (true)
        {
            byte[] digest = DeriveSampleDigest(root, key, samplingRetry);
            ulong candidate = BinaryPrimitives.ReadUInt64LittleEndian(digest);
            if (candidate >= threshold)
            {
                return candidate % exclusiveUpperBound;
            }

            samplingRetry = checked(samplingRetry + 1);
        }
    }

    /// <summary>
    /// Evaluates an exact rational probability through the same named,
    /// unbiased bounded derivation used for other stateless samples.
    /// </summary>
    internal static bool TestRatio(
        RandomRootSeed root,
        RandomSampleKey key,
        ulong numerator,
        ulong denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);
        if (numerator > denominator)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator),
                numerator,
                "Probability numerator cannot exceed its denominator.");
        }

        return DeriveBelow(root, key, denominator) < numerator;
    }

    /// <summary>
    /// Hashes the versioned typed fields without relying on serializers,
    /// platform encodings, or delimiter-sensitive string concatenation.
    /// </summary>
    private static byte[] DeriveStreamDigest(
        RandomRootSeed root,
        RandomStreamKey key,
        ulong retry)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Header);
        AppendField(hash, 1, Encoding.UTF8.GetBytes(AlgorithmId));
        AppendField(hash, 2, root.Bytes);
        AppendField(hash, 3, "stateful-stream"u8);
        Span<byte> scope = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(scope, (uint)key.Scope);
        AppendField(hash, 4, scope);
        AppendField(hash, 5, Encoding.UTF8.GetBytes(key.DomainKind));
        AppendField(hash, 6, Encoding.UTF8.GetBytes(key.OwnerKind));
        Span<byte> owner = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(owner, key.OwnerId);
        AppendField(hash, 7, owner);
        AppendField(hash, 8, Encoding.UTF8.GetBytes(key.PurposeId));
        if (retry != 0)
        {
            Span<byte> retryBytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(retryBytes, retry);
            AppendField(hash, 9, retryBytes);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Keeps every semantic sample component in a separate typed field so a
    /// new named sample cannot shift or collide with an existing decision.
    /// </summary>
    private static byte[] DeriveSampleDigest(
        RandomRootSeed root,
        RandomSampleKey key,
        ulong samplingRetry)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Header);
        AppendField(hash, 1, Encoding.UTF8.GetBytes(AlgorithmId));
        AppendField(hash, 2, root.Bytes);
        AppendField(hash, 3, "stateless-sample"u8);
        AppendUInt32(hash, 4, (uint)key.Scope);
        AppendField(hash, 5, Encoding.UTF8.GetBytes(key.DomainKind));
        AppendField(hash, 6, Encoding.UTF8.GetBytes(key.OwnerKind));
        AppendUInt64(hash, 7, key.OwnerId);
        AppendField(hash, 8, Encoding.UTF8.GetBytes(key.DecisionKind));
        AppendUInt64(hash, 9, key.DecisionId);
        AppendField(hash, 10, Encoding.UTF8.GetBytes(key.PurposeId));
        AppendUInt64(hash, 11, key.Attempt);
        AppendField(hash, 12, Encoding.UTF8.GetBytes(key.SampleId));
        if (samplingRetry != 0)
        {
            AppendUInt64(hash, 13, samplingRetry);
        }

        return hash.GetHashAndReset();
    }

    private static void AppendUInt32(
        IncrementalHash hash,
        byte tag,
        uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        AppendField(hash, tag, bytes);
    }

    private static void AppendUInt64(
        IncrementalHash hash,
        byte tag,
        ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        AppendField(hash, tag, bytes);
    }

    private static void AppendField(
        IncrementalHash hash,
        byte tag,
        ReadOnlySpan<byte> payload)
    {
        Span<byte> prefix = stackalloc byte[1 + sizeof(uint)];
        prefix[0] = tag;
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[1..], checked((uint)payload.Length));
        hash.AppendData(prefix);
        hash.AppendData(payload);
    }
}

internal readonly record struct Xoshiro256State
{
    /// <summary>
    /// Preserves all four generator words and the exact next-output position,
    /// including the exhausted marker, while rejecting the absorbing state.
    /// </summary>
    internal Xoshiro256State(
        ulong s0,
        ulong s1,
        ulong s2,
        ulong s3,
        ulong? nextPosition)
    {
        if ((s0 | s1 | s2 | s3) == 0)
        {
            throw new ArgumentException("Random stream state cannot be all zero.");
        }

        S0 = s0;
        S1 = s1;
        S2 = s2;
        S3 = s3;
        NextPosition = nextPosition;
    }

    internal ulong S0 { get; }

    internal ulong S1 { get; }

    internal ulong S2 { get; }

    internal ulong S3 { get; }

    internal ulong? NextPosition { get; }
}

internal readonly record struct RandomStep(
    ulong Value,
    Xoshiro256State NextState);

internal readonly record struct RandomBoundedStep(
    ulong Value,
    Xoshiro256State NextState);

internal readonly record struct RandomBooleanStep(
    bool Value,
    Xoshiro256State NextState);

internal static class DeterministicRandomSampling
{
    /// <summary>
    /// Maps raw outputs into one unbiased bounded value while returning only a
    /// proposed successor state; rejected candidates remain part of that
    /// proposal and do not mutate the authoritative stream by themselves.
    /// </summary>
    internal static RandomBoundedStep NextBelow(
        Xoshiro256State state,
        ulong exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfZero(exclusiveUpperBound);
        ulong threshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;
        Xoshiro256State candidateState = state;
        while (true)
        {
            RandomStep candidate = Xoshiro256StarStar.Next(candidateState);
            if (candidate.Value >= threshold)
            {
                return new RandomBoundedStep(
                    candidate.Value % exclusiveUpperBound,
                    candidate.NextState);
            }

            candidateState = candidate.NextState;
        }
    }

    /// <summary>
    /// Evaluates an exact rational probability through bounded sampling so
    /// zero and certain outcomes consume candidates identically.
    /// </summary>
    internal static RandomBooleanStep TestRatio(
        Xoshiro256State state,
        ulong numerator,
        ulong denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);
        if (numerator > denominator)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator),
                numerator,
                "Probability numerator cannot exceed its denominator.");
        }

        RandomBoundedStep sample = NextBelow(state, denominator);
        return new RandomBooleanStep(sample.Value < numerator, sample.NextState);
    }
}

internal static class Xoshiro256StarStar
{
    internal const string AlgorithmId = "gc.xoshiro256ss.v1";
    internal const int AlgorithmVersion = 1;
    /// <summary>
    /// Produces one bit-exact xoshiro256** 1.0 output and proposed successor
    /// state without mutating the supplied snapshot.
    /// </summary>
    internal static RandomStep Next(Xoshiro256State state)
    {
        ulong position = state.NextPosition
            ?? throw new InvalidOperationException("Random stream is exhausted.");
        ulong s0 = state.S0;
        ulong s1 = state.S1;
        ulong s2 = state.S2;
        ulong s3 = state.S3;

        ulong result = unchecked(BitOperations.RotateLeft(s1 * 5, 7) * 9);
        ulong shifted = s1 << 17;
        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= shifted;
        s3 = BitOperations.RotateLeft(s3, 45);

        ulong? nextPosition = position == ulong.MaxValue ? null : position + 1;
        return new RandomStep(
            result,
            new Xoshiro256State(s0, s1, s2, s3, nextPosition));
    }
}
