namespace GalaxyCommand.Simulation;

/// <summary>Session-scoped logical debris grouping identity, independent of physical cargo entities.</summary>
public readonly record struct DebrisFieldId : IEntityId<DebrisFieldId>
{
    public DebrisFieldId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static DebrisFieldId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record CargoDebrisCircle(DebrisFieldId OriginalFieldId, SystemPosition Center, ulong RadiusMeters);
internal sealed record CargoDebrisAlias(DebrisFieldId Alias, DebrisFieldId Canonical);
internal sealed record CargoDebrisFieldSnapshot(DebrisFieldId Id, IReadOnlyList<CargoDebrisCircle> Circles, IReadOnlyList<EntityId> Members);
internal sealed record CargoDebrisFieldsCheckpoint(int MergeBehaviorVersion, ulong Revision, IdSequenceCheckpoint FieldIds,
    IReadOnlyList<CargoDebrisFieldSnapshot> Fields, IReadOnlyList<CargoDebrisAlias> Aliases, IReadOnlyList<DebrisFieldId> Tombstones);
internal sealed record PreparedCargoDebrisMerge(CargoDebrisFields Owner, ulong ExpectedRevision,
    DebrisFieldId OriginalFieldId, CargoDebrisCircle Circle, IReadOnlyList<EntityId> Members,
    IReadOnlyList<DebrisFieldId> ParticipatingFields);
internal sealed record PreparedCargoDebrisRemoval(CargoDebrisFields Owner, ulong ExpectedRevision,
    EntityId Member, DebrisFieldId Field);

/// <summary>
/// Authoritative logical fields. Cargo contents, positions, timers, and causal
/// idempotency receipts remain with their owners; this owner changes membership only.
/// </summary>
internal sealed partial class CargoDebrisFields
{
    private readonly SortedDictionary<DebrisFieldId, CargoDebrisFieldSnapshot> _fields = new(EntityIdComparer<DebrisFieldId>.Instance);
    private readonly SortedDictionary<DebrisFieldId, DebrisFieldId> _aliases = new(EntityIdComparer<DebrisFieldId>.Instance);
    private readonly SortedSet<DebrisFieldId> _tombstones = new(EntityIdComparer<DebrisFieldId>.Instance);
    private readonly Dictionary<EntityId, DebrisFieldId> _memberFields = [];
    private readonly CargoDebrisCircleIndex _index = new();
    private IdSequence<DebrisFieldId> _fieldIds = new();
    private ulong _revision;

    /// <summary>Rejects unsupported merging behavior rather than silently changing saved semantics.</summary>
    internal CargoDebrisFields(int mergeBehaviorVersion) => ArgumentOutOfRangeException.ThrowIfNotEqual(mergeBehaviorVersion, 1);

    /// <summary>
    /// Prepares a destruction occurrence against stable state without allocating IDs.
    /// Empty survivors produce no field. Inputs are copied and candidates are sorted
    /// by identity before reduction, so traversal or worker order has no authority.
    /// </summary>
    internal PreparedCargoDebrisMerge? PrepareOccurrence(SystemPosition center, ulong radiusMeters, IReadOnlyList<EntityId> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        ValidateCircle(center, radiusMeters);
        EntityId[] ordered = members.OrderBy(id => id.Value).ToArray();
        if (ordered.Any(id => id.Value == 0 || _memberFields.ContainsKey(id)) || ordered.Distinct().Count() != ordered.Length)
            throw new ArgumentException("Field members must be unique, nonzero, and not already grouped.", nameof(members));
        if (ordered.Length == 0) return null;
        _ = checked(_revision + 1);
        if (!_fieldIds.TryPeek(out DebrisFieldId identity)) throw new OverflowException("Debris field identities are exhausted.");
        var circle = new CargoDebrisCircle(identity, center, radiusMeters);
        DebrisFieldId[] overlaps = _index.Query(circle).Where(other => Touches(circle, other))
            .Select(other => Root(other.OriginalFieldId)).Distinct().OrderBy(id => id.Value).ToArray();
        return new PreparedCargoDebrisMerge(this, _revision, identity, circle, Array.AsReadOnly(ordered), Array.AsReadOnly(overlaps));
    }

    /// <summary>Rejects stale or foreign evaluation before any owner mutation.</summary>
    internal bool CanCommit(PreparedCargoDebrisMerge prepared) => ReferenceEquals(prepared.Owner, this)
        && prepared.ExpectedRevision == _revision && _revision != ulong.MaxValue
        && _fieldIds.TryPeek(out DebrisFieldId next) && next == prepared.OriginalFieldId;

    /// <summary>
    /// Publishes one prevalidated occurrence and flattens every participating handle.
    /// Existing circles and object identities remain intact; the enclosing session
    /// health guard handles unexpected failure after mutation.
    /// </summary>
    internal void Apply(PreparedCargoDebrisMerge prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CanCommit(prepared)) throw new InvalidOperationException("Prepared debris merge is stale or belongs to another owner.");
        DebrisFieldId canonical = prepared.ParticipatingFields.Count > 0 ? prepared.ParticipatingFields[0] : prepared.OriginalFieldId;
        CargoDebrisCircle[] circles = prepared.ParticipatingFields.SelectMany(id => _fields[id].Circles)
            .Append(prepared.Circle).OrderBy(circle => circle.OriginalFieldId.Value).ToArray();
        EntityId[] members = prepared.ParticipatingFields.SelectMany(id => _fields[id].Members)
            .Concat(prepared.Members).OrderBy(id => id.Value).ToArray();
        var participating = prepared.ParticipatingFields.ToHashSet();

        if (_fieldIds.Allocate() != prepared.OriginalFieldId)
            throw new InvalidOperationException("Prepared debris identity changed during commit.");
        foreach (DebrisFieldId id in prepared.ParticipatingFields) _fields.Remove(id);
        foreach (DebrisFieldId alias in _aliases.Where(pair => participating.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            _aliases[alias] = canonical;
        foreach (DebrisFieldId id in prepared.ParticipatingFields.Append(prepared.OriginalFieldId))
            if (id != canonical) _aliases.Add(id, canonical);
        _fields.Add(canonical, new(canonical, Array.AsReadOnly(circles), Array.AsReadOnly(members)));
        foreach (EntityId member in members) _memberFields[member] = canonical;
        _index.Add(prepared.Circle);
        _revision = checked(_revision + 1);
    }

    /// <summary>Prepares membership removal; standalone or already removed objects have no field effect.</summary>
    internal PreparedCargoDebrisRemoval? PrepareRemoval(EntityId member)
    {
        if (!_memberFields.TryGetValue(member, out DebrisFieldId field)) return null;
        _ = checked(_revision + 1);
        return new(this, _revision, member, field);
    }

    /// <summary>Validates the exact still-live membership before collection or expiry commits it.</summary>
    internal bool CanCommit(PreparedCargoDebrisRemoval prepared) => ReferenceEquals(prepared.Owner, this)
        && prepared.ExpectedRevision == _revision && _revision != ulong.MaxValue
        && _memberFields.TryGetValue(prepared.Member, out DebrisFieldId field) && field == prepared.Field;

    /// <summary>
    /// Removes one member without splitting fields. Original circles retire only
    /// with the last member; aliases and the empty canonical tombstone remain saved.
    /// </summary>
    internal void Apply(PreparedCargoDebrisRemoval prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CanCommit(prepared)) throw new InvalidOperationException("Prepared debris removal is stale or belongs to another owner.");
        CargoDebrisFieldSnapshot field = _fields[prepared.Field];
        EntityId[] remaining = field.Members.Where(id => id != prepared.Member).ToArray();
        _memberFields.Remove(prepared.Member);
        if (remaining.Length == 0)
        {
            _fields.Remove(field.Id);
            _tombstones.Add(field.Id);
            foreach (CargoDebrisCircle circle in field.Circles) _index.Remove(circle);
        }
        else _fields[field.Id] = field with { Members = Array.AsReadOnly(remaining) };
        _revision = checked(_revision + 1);
    }

    /// <summary>Resolves historical handles without resurrecting an empty or unrelated later field.</summary>
    internal DebrisFieldId? Resolve(DebrisFieldId handle)
    {
        DebrisFieldId root = Root(handle);
        return _fields.ContainsKey(root) ? root : null;
    }

    /// <summary>Returns logical membership only; callers must apply observation before presenting or selecting it.</summary>
    internal IReadOnlyList<EntityId> Members(DebrisFieldId handle) =>
        Resolve(handle) is { } root ? _fields[root].Members : Array.Empty<EntityId>();

    internal IReadOnlyList<CargoDebrisFieldSnapshot> CaptureFields() => Array.AsReadOnly(_fields.Values.ToArray());

    /// <summary>Captures only authority; the spatial interval index is rebuilt privately on restore.</summary>
    internal CargoDebrisFieldsCheckpoint CaptureCheckpoint() => new(1, _revision, _fieldIds.CaptureCheckpoint(),
        CaptureFields(), Array.AsReadOnly(_aliases.Select(pair => new CargoDebrisAlias(pair.Key, pair.Value)).ToArray()),
        Array.AsReadOnly(_tombstones.ToArray()));

    /// <summary>Uses one saved direct alias hop. Empty-field tombstones intentionally remain unresolved.</summary>
    private DebrisFieldId Root(DebrisFieldId handle) => _aliases.GetValueOrDefault(handle, handle);

    /// <summary>Applies accepted coordinate/radius bounds before any wide geometric arithmetic.</summary>
    private static void ValidateCircle(SystemPosition center, ulong radius)
    {
        ArgumentOutOfRangeException.ThrowIfZero(center.SystemId.Value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, 1UL << 50);
        if (center.Position.X.Units is < -(1L << 50) or > (1L << 50)
            || center.Position.Y.Units is < -(1L << 50) or > (1L << 50))
            throw new ArgumentOutOfRangeException(nameof(center));
    }

    /// <summary>Includes boundary touching and avoids floating-point distance or box approximations.</summary>
    private static bool Touches(CargoDebrisCircle left, CargoDebrisCircle right)
    {
        if (left.Center.SystemId != right.Center.SystemId) return false;
        Int128 x = (Int128)left.Center.Position.X.Units - right.Center.Position.X.Units;
        Int128 y = (Int128)left.Center.Position.Y.Units - right.Center.Position.Y.Units;
        Int128 radius = (Int128)left.RadiusMeters + right.RadiusMeters;
        return checked(x * x + y * y) <= checked(radius * radius);
    }
}
