namespace GalaxyCommand.Simulation;

public sealed record MoveShipCommand : GameplayCommand
{
    public const string CommandKind = "ship.move";

    public MoveShipCommand(
        ShipId shipId,
        NavigationDestination destination,
        OrderPlacement placement)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(placement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placement),
                placement,
                "Unknown order placement.");
        }

        ShipId = shipId;
        Destination = destination;
        Placement = placement;
    }

    public ShipId ShipId { get; }

    public NavigationDestination Destination { get; }

    public OrderPlacement Placement { get; }
}

/// <summary>
/// One-shot request to replace current move work for an explicit ship snapshot.
/// It never creates a durable group, fleet, membership record, or group order.
/// </summary>
public sealed record MoveShipGroupCommand : GameplayCommand
{
    public const string CommandKind = "ship.move-group";

    /// <summary>
    /// Captures and canonicalizes nonzero, unique selected ships with one
    /// system-local formation center.
    /// </summary>
    public MoveShipGroupCommand(
        IEnumerable<ShipId> shipIds,
        SystemPosition destination)
        : base(CommandKind)
    {
        ShipIds = CanonicalizeShipIds(shipIds);
        Destination = destination;
    }

    /// <summary>
    /// Gets the ascending explicit member identities captured at submission.
    /// </summary>
    public IReadOnlyList<ShipId> ShipIds { get; }

    /// <summary>
    /// Gets the system-local formation center for this one-shot command.
    /// </summary>
    public SystemPosition Destination { get; }

    /// <summary>
    /// Sorts an explicit selection and rejects values that cannot identify one
    /// deterministic one-shot group-command member.
    /// </summary>
    internal static IReadOnlyList<ShipId> CanonicalizeShipIds(IEnumerable<ShipId> shipIds)
    {
        ArgumentNullException.ThrowIfNull(shipIds);
        ShipId[] canonicalIds = shipIds.ToArray();
        if (canonicalIds.Length == 0)
        {
            throw new ArgumentException(
                "A group command needs at least one ship.",
                nameof(shipIds));
        }

        Array.Sort(canonicalIds, static (left, right) => left.Value.CompareTo(right.Value));
        for (int index = 0; index < canonicalIds.Length; index++)
        {
            if (canonicalIds[index].Value == 0
                || (index > 0 && canonicalIds[index] == canonicalIds[index - 1]))
            {
                throw new ArgumentException(
                    "A group command needs unique nonzero ship identities.",
                    nameof(shipIds));
            }
        }

        return Array.AsReadOnly(canonicalIds);
    }
}

public sealed record CancelShipOrderCommand : GameplayCommand
{
    public const string CommandKind = "ship.cancel-order";

    public CancelShipOrderCommand(ShipId shipId, ShipOrderId orderId)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        ShipId = shipId;
        OrderId = orderId;
    }

    public ShipId ShipId { get; }

    public ShipOrderId OrderId { get; }
}

/// <summary>
/// One-shot request to cancel the current order of an explicit ship snapshot.
/// It does not name or recover a prior group command or order.
/// </summary>
public sealed record CancelShipGroupCommand : GameplayCommand
{
    public const string CommandKind = "ship.cancel-group-current";

    /// <summary>
    /// Captures and canonicalizes nonzero, unique selected ships whose current
    /// orders may be cancelled at one command boundary.
    /// </summary>
    public CancelShipGroupCommand(IEnumerable<ShipId> shipIds)
        : base(CommandKind)
    {
        ShipIds = MoveShipGroupCommand.CanonicalizeShipIds(shipIds);
    }

    /// <summary>
    /// Gets the ascending explicit member identities captured at submission.
    /// </summary>
    public IReadOnlyList<ShipId> ShipIds { get; }
}

public sealed record BeginScriptedOverrideCommand : GameplayCommand
{
    public const string CommandKind = "actor.begin-scripted-override";

    public BeginScriptedOverrideCommand(
        ShipId shipId,
        ActorOverrideReasonId reason,
        ActorControlRevision expectedRevision)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason.Value);
        ShipId = shipId;
        Reason = reason;
        ExpectedRevision = expectedRevision;
    }

    public ShipId ShipId { get; }

    public ActorOverrideReasonId Reason { get; }

    public ActorControlRevision ExpectedRevision { get; }
}

public sealed record EndScriptedOverrideCommand : GameplayCommand
{
    public const string CommandKind = "actor.end-scripted-override";

    public EndScriptedOverrideCommand(
        ShipId shipId,
        ScriptedOverrideReleasePolicy releasePolicy,
        ActorControlRevision expectedRevision)
        : base(CommandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        if (!Enum.IsDefined(releasePolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(releasePolicy),
                releasePolicy,
                "Unknown scripted override release policy.");
        }

        ShipId = shipId;
        ReleasePolicy = releasePolicy;
        ExpectedRevision = expectedRevision;
    }

    public ShipId ShipId { get; }

    public ScriptedOverrideReleasePolicy ReleasePolicy { get; }

    public ActorControlRevision ExpectedRevision { get; }
}
