using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>The authoritative domains that may participate in dialogue.</summary>
public enum DialogueParticipantKind
{
    Ship,
    Station,
    Principal,
}

/// <summary>
/// A typed participant reference. Apparent speaker names and facility identities
/// cannot become participants. Existence is checked by session admission.
/// </summary>
public abstract record DialogueParticipant
{
    private DialogueParticipant() { }

    public abstract DialogueParticipantKind Kind { get; }

    public sealed record Ship : DialogueParticipant
    {
        /// <summary>References an existing ship identity; rejects a default ID.</summary>
        public Ship(ShipId id) { ArgumentOutOfRangeException.ThrowIfZero(id.Value); Id = id; }
        public ShipId Id { get; }
        public override DialogueParticipantKind Kind => DialogueParticipantKind.Ship;
    }

    public sealed record Station : DialogueParticipant
    {
        /// <summary>References a station independently of its economic facilities.</summary>
        public Station(StationId id) { ArgumentOutOfRangeException.ThrowIfZero(id.Value); Id = id; }
        public StationId Id { get; }
        public override DialogueParticipantKind Kind => DialogueParticipantKind.Station;
    }

    public sealed record Principal : DialogueParticipant
    {
        /// <summary>References an accountable principal; rejects a default ID.</summary>
        public Principal(PrincipalId id) { ArgumentOutOfRangeException.ThrowIfZero(id.Value); Id = id; }
        public PrincipalId Id { get; }
        public override DialogueParticipantKind Kind => DialogueParticipantKind.Principal;
    }
}

/// <summary>One definition-owned role with an explicit set of accepted domains.</summary>
public sealed class DialogueParticipantRole
{
    /// <summary>Copies and validates a nonempty, distinct set of supported participant kinds.</summary>
    public DialogueParticipantRole(string id, IEnumerable<DialogueParticipantKind> acceptedKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(acceptedKinds);
        DialogueParticipantKind[] kinds = acceptedKinds.ToArray();
        if (kinds.Any(kind => !Enum.IsDefined(kind)))
            throw new ArgumentOutOfRangeException(nameof(acceptedKinds), "Unknown dialogue participant kind.");
        if (kinds.Length == 0 || kinds.Distinct().Count() != kinds.Length)
            throw new ArgumentException("A role requires distinct accepted participant kinds.", nameof(acceptedKinds));
        Id = id;
        AcceptedKinds = Array.AsReadOnly(kinds.Order().ToArray());
    }

    public string Id { get; }
    public ReadOnlyCollection<DialogueParticipantKind> AcceptedKinds { get; }
}

/// <summary>One structural role-to-participant assignment, independent of display text.</summary>
public sealed record DialogueParticipantBinding
{
    /// <summary>Creates a role assignment without resolving or mutating its domain owner.</summary>
    public DialogueParticipantBinding(string roleId, DialogueParticipant participant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentNullException.ThrowIfNull(participant);
        RoleId = roleId;
        Participant = participant;
    }

    public string RoleId { get; }
    public DialogueParticipant Participant { get; }
}

/// <summary>
/// Immutable, complete, ordinal role-ordered bindings. Structural equality lets
/// repeatability receipts ignore caller collection order without losing roles.
/// </summary>
public sealed class DialogueParticipantBindings : IEquatable<DialogueParticipantBindings>
{
    /// <summary>
    /// Rejects missing, extra, duplicate, or incompatible assignments before
    /// publishing a normalized binding. Participant existence remains session-owned.
    /// </summary>
    public DialogueParticipantBindings(IEnumerable<DialogueParticipantRole> roles,
        IEnumerable<DialogueParticipantBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(bindings);
        DialogueParticipantRole[] roleArray = roles.ToArray();
        DialogueParticipantBinding[] bindingArray = bindings.ToArray();
        if (roleArray.Any(role => role is null) || bindingArray.Any(binding => binding is null))
            throw new ArgumentException("Roles and bindings cannot contain null entries.");
        if (roleArray.Select(role => role.Id).Distinct(StringComparer.Ordinal).Count() != roleArray.Length
            || bindingArray.Select(binding => binding.RoleId).Distinct(StringComparer.Ordinal).Count() != bindingArray.Length)
            throw new ArgumentException("Roles and bindings must have unique role identities.");
        var roleIndex = roleArray.ToDictionary(role => role.Id, StringComparer.Ordinal);
        if (roleArray.Length != bindingArray.Length || bindingArray.Any(binding =>
            !roleIndex.TryGetValue(binding.RoleId, out DialogueParticipantRole? role)
            || !role.AcceptedKinds.Contains(binding.Participant.Kind)))
            throw new ArgumentException("Bindings must match every declared role and its accepted participant kind.", nameof(bindings));

        Items = Array.AsReadOnly(bindingArray.OrderBy(binding => binding.RoleId, StringComparer.Ordinal).ToArray());
    }

    public ReadOnlyCollection<DialogueParticipantBinding> Items { get; }

    /// <inheritdoc/>
    public bool Equals(DialogueParticipantBindings? other) =>
        other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as DialogueParticipantBindings);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // This is an in-process lookup hash, never a saved identity or canonical digest.
        var hash = new HashCode();
        foreach (DialogueParticipantBinding binding in Items)
            hash.Add(binding);
        return hash.ToHashCode();
    }
}
