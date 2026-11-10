namespace GalaxyCommand.Simulation;

internal sealed partial class CargoDebrisFields
{
    /// <summary>
    /// Restores only unpublished authority. Validates field identity accounting,
    /// direct aliases, membership and connected original circles before publication;
    /// the enclosing disposition owner must also corroborate historical receipts.
    /// </summary>
    internal static CheckpointResult<CargoDebrisFields> Restore(CargoDebrisFieldsCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.MergeBehaviorVersion != 1 || checkpoint.FieldIds is null || checkpoint.Fields is null
            || checkpoint.Aliases is null || checkpoint.Tombstones is null)
            return Rejected("Debris checkpoint version and state sections are required.");
        CheckpointResult<IdSequence<DebrisFieldId>> sequence = IdSequence<DebrisFieldId>.RestoreCheckpoint(checkpoint.FieldIds);
        if (!sequence.IsSuccess) return Rejected("The debris field allocator is invalid.");
        var restored = new CargoDebrisFields(checkpoint.MergeBehaviorVersion) { _fieldIds = sequence.Value!, _revision = checkpoint.Revision };
        var identities = new HashSet<DebrisFieldId>();
        foreach (CargoDebrisFieldSnapshot? field in checkpoint.Fields)
        {
            if (field is null || !Allocated(field.Id, checkpoint.FieldIds) || !identities.Add(field.Id)
                || field.Circles is null || field.Members is null || field.Circles.Count == 0 || field.Members.Count == 0)
                return Rejected("A live field requires unique allocated identity, circles and members.");
            restored._fields.Add(field.Id, field);
        }
        foreach (DebrisFieldId tombstone in checkpoint.Tombstones)
        {
            if (!Allocated(tombstone, checkpoint.FieldIds) || !identities.Add(tombstone))
                return Rejected("An empty-field tombstone is invalid or conflicts with live identity.");
            restored._tombstones.Add(tombstone);
        }
        foreach (CargoDebrisAlias? alias in checkpoint.Aliases)
        {
            if (alias is null || !Allocated(alias.Alias, checkpoint.FieldIds) || !identities.Add(alias.Alias)
                || alias.Alias.Value <= alias.Canonical.Value
                || !restored._fields.ContainsKey(alias.Canonical) && !restored._tombstones.Contains(alias.Canonical))
                return Rejected("Aliases must be unique allocated handles directly targeting an earlier canonical field or tombstone.");
            restored._aliases.Add(alias.Alias, alias.Canonical);
        }

        // This owner allocates one consecutive ID per nonempty occurrence and never
        // evicts live handles, aliases or tombstones. Missing history is malformed.
        ulong allocatedCount = checkpoint.FieldIds.NextValue is { } next ? next - 1 : ulong.MaxValue;
        if ((ulong)identities.Count != allocatedCount || checkpoint.Revision < allocatedCount)
            return Rejected("The field allocator or revision disagrees with retained field history.");

        var circleIds = new HashSet<DebrisFieldId>();
        foreach (CargoDebrisFieldSnapshot field in checkpoint.Fields)
        {
            foreach (CargoDebrisCircle? circle in field.Circles)
            {
                if (circle is null || !circleIds.Add(circle.OriginalFieldId)
                    || restored.Root(circle.OriginalFieldId) != field.Id || !identities.Contains(circle.OriginalFieldId))
                    return Rejected("Original occurrence circles must have unique identity within their canonical field.");
                try { ValidateCircle(circle.Center, circle.RadiusMeters); }
                catch (ArgumentException) { return Rejected("An original occurrence circle has invalid geometry."); }
                restored._index.Add(circle);
            }
            foreach (EntityId member in field.Members)
                if (member.Value == 0 || !restored._memberFields.TryAdd(member, field.Id))
                    return Rejected("A cargo object cannot have invalid or repeated field membership.");
            // Copy all externally supplied containers before the restored owner is exposed.
            restored._fields[field.Id] = new(field.Id,
                Array.AsReadOnly(field.Circles.OrderBy(circle => circle.OriginalFieldId.Value).ToArray()),
                Array.AsReadOnly(field.Members.OrderBy(member => member.Value).ToArray()));
        }
        foreach (DebrisFieldId identity in identities)
            if (restored._fields.ContainsKey(restored.Root(identity)) && !circleIds.Contains(identity))
                return Rejected("Every live occurrence handle requires its retained original circle.");

        if (!restored.HasValidConnectedCircles())
            return Rejected("Live field circles must be connected internally and cannot touch a different live field.");
        return CheckpointResult<CargoDebrisFields>.Success(restored);
    }

    /// <summary>
    /// Reuses indexed narrow-phase candidates to verify connected components without
    /// all-pairs field comparisons. Never-split fields retain the entire original
    /// connected circle chain even after some originating cargo has disappeared.
    /// </summary>
    private bool HasValidConnectedCircles()
    {
        var parents = _fields.Values.SelectMany(field => field.Circles)
            .ToDictionary(circle => circle.OriginalFieldId, circle => circle.OriginalFieldId);
        foreach (CargoDebrisFieldSnapshot field in _fields.Values)
        foreach (CargoDebrisCircle circle in field.Circles)
        foreach (CargoDebrisCircle other in _index.Query(circle))
        {
            if (!Touches(circle, other)) continue;
            if (Root(other.OriginalFieldId) != field.Id) return false;
            DebrisFieldId left = Find(circle.OriginalFieldId);
            DebrisFieldId right = Find(other.OriginalFieldId);
            if (left != right) parents[right] = left;
        }
        return _fields.Values.All(field => field.Circles.Select(circle => Find(circle.OriginalFieldId)).Distinct().Count() == 1);

        // Only the unpublished local validation forest is compressed; authority
        // and saved aliases are untouched by connectivity traversal order.
        DebrisFieldId Find(DebrisFieldId id)
        {
            DebrisFieldId root = id;
            while (parents[root] != root) root = parents[root];
            while (parents[id] != id)
            {
                DebrisFieldId parent = parents[id];
                parents[id] = root;
                id = parent;
            }
            return root;
        }
    }

    private static bool Allocated(DebrisFieldId id, IdSequenceCheckpoint sequence) =>
        id.Value != 0 && (sequence.NextValue is not { } next || id.Value < next);

    private static CheckpointResult<CargoDebrisFields> Rejected(string message) =>
        CheckpointResult<CargoDebrisFields>.Rejected(new("$.checkpoint.cargo.fields", message));
}
