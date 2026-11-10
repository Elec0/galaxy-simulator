using System.Text;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Pure R02 generation for one definition and causal occurrence. No inventory,
/// lifecycle identity, receipt, or shared random stream is changed here.
/// </summary>
internal sealed class CargoDispositionGeneration
{
    private const long CoordinateAxisLimit = 1L << 50;
    private readonly DeterministicRandomOwner _random;
    private readonly RandomSampleKey _key;
    private readonly QualifiedContentKey _definition;
    private readonly int _behaviorVersion;

    /// <summary>Names command-caused samples independently of event sequence identities.</summary>
    internal CargoDispositionGeneration(DeterministicRandomOwner random, EntityId source,
        CommandSequence command, ulong occurrence, int behaviorVersion, QualifiedContentKey definition)
        : this(random, source, "command", command.Value, occurrence, behaviorVersion, definition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(command.Value);
    }

    /// <summary>
    /// Uses the globally unique event creation sequence for sampling. Full event
    /// timestamp/phase equality remains the disposition receipt owner's responsibility.
    /// </summary>
    internal CargoDispositionGeneration(DeterministicRandomOwner random, EntityId source,
        EventKey scheduledEvent, ulong occurrence, int behaviorVersion, QualifiedContentKey definition)
        : this(random, source, "event", scheduledEvent.CreationSequence, occurrence, behaviorVersion, definition)
    {
        if (!Enum.IsDefined(scheduledEvent.Phase))
            throw new ArgumentOutOfRangeException(nameof(scheduledEvent));
    }

    /// <summary>Rejects incomplete identities and unsupported generation behavior before evaluation.</summary>
    private CargoDispositionGeneration(DeterministicRandomOwner random, EntityId source,
        string decisionKind, ulong decisionId, ulong occurrence, int behaviorVersion, QualifiedContentKey definition)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfZero(source.Value);
        ArgumentOutOfRangeException.ThrowIfNotEqual(behaviorVersion, 1);
        _random = random;
        _definition = definition;
        _behaviorVersion = behaviorVersion;
        _key = new RandomSampleKey(RandomScope.SessionRuntime, "cargo", "entity", source.Value,
            decisionKind, decisionId, "", occurrence, "");
    }

    /// <summary>
    /// Partitions already surviving fungible cargo using Floyd's unbiased cut
    /// selection. Work and retained memory depend on object count, not quantity.
    /// Zero dropped units produce no objects.
    /// </summary>
    internal Quantity[] Partition(Quantity dropped, int configuredCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuredCount);
        int count = (int)Math.Min(dropped.Units, (ulong)configuredCount);
        if (count == 0) return [];

        var cuts = new HashSet<ulong>();
        for (int step = 0; step < count - 1; step++)
        {
            ulong j = dropped.Units - (ulong)count + (ulong)step;
            ulong candidate = Sample("share-cuts", (ulong)step, 0, "cut", j + 1);
            // A collision selects the new endpoint, preserving a uniform subset
            // without retrying or visiting the entire quantity range.
            if (!cuts.Add(candidate)) cuts.Add(j);
        }

        ulong[] sorted = cuts.Order().ToArray();
        var shares = new Quantity[count];
        ulong previous = 0;
        for (int index = 0; index < sorted.Length; index++)
        {
            ulong cut = sorted[index] + 1;
            shares[index] = new Quantity(cut - previous);
            previous = cut;
        }
        shares[^1] = new Quantity(dropped.Units - previous);
        return shares;
    }

    /// <summary>
    /// Selects exact original item IDs without replacement, starting from canonical
    /// identity order and publishing canonical survivors. Does not mutate input.
    /// </summary>
    internal ItemInstanceId[] SelectSurvivors(IReadOnlyList<ItemInstanceId> selected, int survivorCount)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentOutOfRangeException.ThrowIfNegative(survivorCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(survivorCount, selected.Count);
        ItemInstanceId[] items = selected.OrderBy(item => item.Value).ToArray();
        for (int index = 0; index < items.Length; index++)
        {
            if (items[index].Value == 0 || index > 0 && items[index] == items[index - 1])
                throw new ArgumentException("Selected item identities must be nonzero and unique.", nameof(selected));
        }

        for (int step = 0; step < survivorCount; step++)
        {
            int other = step + (int)Sample("survivors", (ulong)step, 0, "index", (ulong)(items.Length - step));
            (items[step], items[other]) = (items[other], items[step]);
        }
        return items.Take(survivorCount).OrderBy(item => item.Value).ToArray();
    }

    /// <summary>
    /// Samples uniformly from integer-meter disk points inside the accepted
    /// coordinate envelope. Rejection preserves the distribution at envelope edges;
    /// radius zero returns the validated source position.
    /// </summary>
    internal SystemPosition Scatter(SystemPosition source, long radius, ulong objectOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, CoordinateAxisLimit);
        if (!WithinEnvelope(source.Position.X.Units) || !WithinEnvelope(source.Position.Y.Units))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (radius == 0) return source;

        ulong width = checked((ulong)radius * 2 + 1);
        for (ulong candidate = 0; ; candidate = checked(candidate + 1))
        {
            long x = checked((long)Sample("scatter", objectOrdinal, candidate, "x", width) - radius);
            long y = checked((long)Sample("scatter", objectOrdinal, candidate, "y", width) - radius);
            if ((Int128)x * x + (Int128)y * y > (Int128)radius * radius) continue;
            long absoluteX = checked(source.Position.X.Units + x);
            long absoluteY = checked(source.Position.Y.Units + y);
            if (!WithinEnvelope(absoluteX) || !WithinEnvelope(absoluteY)) continue;
            return new SystemPosition(source.SystemId, new SpatialPosition(new(absoluteX), new(absoluteY)));
        }
    }

    /// <summary>Uses the accepted inclusive per-axis meter envelope without clamping.</summary>
    private static bool WithinEnvelope(long coordinate) => coordinate >= -CoordinateAxisLimit && coordinate <= CoordinateAxisLimit;

    /// <summary>
    /// Encodes versioned, length-delimited definition and sample coordinates so
    /// retries, purposes and independent objects cannot shift each other's draws.
    /// BinaryWriter strings carry UTF-8 byte lengths; fixed-width fields are little endian.
    /// </summary>
    private ulong Sample(string purpose, ulong ordinal, ulong candidate, string component, ulong bound)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1); // Encoding version is separate from generation behavior.
            writer.Write(_behaviorVersion);
            writer.Write(_definition.PackageId.Value);
            writer.Write(_definition.ContentKind.Value);
            writer.Write(_definition.LocalId.Value);
            writer.Write(ordinal);
            writer.Write(candidate);
            writer.Write(component);
        }
        return _random.DeriveBelow(_key with { PurposeId = purpose, SampleId = Convert.ToHexString(buffer.ToArray()) }, bound);
    }
}
