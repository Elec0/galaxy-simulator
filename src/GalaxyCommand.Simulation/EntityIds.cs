namespace GalaxyCommand.Simulation;

/// <summary>
/// Contract implemented by strongly typed, non-zero simulation identifiers.
/// </summary>
public interface IEntityId<TSelf> where TSelf : struct, IEntityId<TSelf>
{
    static abstract TSelf Create(ulong value);

    ulong Value { get; }
}

public readonly record struct EntityId : IEntityId<EntityId>
{
    public EntityId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static EntityId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct LocationId : IEntityId<LocationId>
{
    public LocationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static LocationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct RouteId : IEntityId<RouteId>
{
    public RouteId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static RouteId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct SystemId : IEntityId<SystemId>
{
    public SystemId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static SystemId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ConnectorEndpointId : IEntityId<ConnectorEndpointId>
{
    public ConnectorEndpointId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ConnectorEndpointId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct TransitConnectionId : IEntityId<TransitConnectionId>
{
    public TransitConnectionId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static TransitConnectionId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ConnectorTransitId : IEntityId<ConnectorTransitId>
{
    public ConnectorTransitId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ConnectorTransitId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct MotionId : IEntityId<MotionId>
{
    public MotionId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static MotionId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ShipOrderId : IEntityId<ShipOrderId>
{
    public ShipOrderId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ShipOrderId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct FacilityId : IEntityId<FacilityId>
{
    public FacilityId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static FacilityId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct InventoryId : IEntityId<InventoryId>
{
    public InventoryId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static InventoryId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct MaterialId : IEntityId<MaterialId>
{
    public MaterialId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static MaterialId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Stable session-scoped identity of one discrete physical item instance.
/// </summary>
public readonly record struct ItemInstanceId : IEntityId<ItemInstanceId>
{
    public ItemInstanceId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ItemInstanceId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ShipId : IEntityId<ShipId>
{
    public ShipId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ShipId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct OrganizationId : IEntityId<OrganizationId>
{
    public OrganizationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static OrganizationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Stable identity of an accountable participant that can own assets and hold
/// relationships in the clean game session.
/// </summary>
public readonly record struct PrincipalId : IEntityId<PrincipalId>
{
    /// <summary>
    /// Creates a non-zero principal identity.
    /// </summary>
    public PrincipalId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }

    /// <summary>
    /// Creates a principal identity for generic deterministic allocation.
    /// </summary>
    public static PrincipalId Create(ulong value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ConstructionDesignId : IEntityId<ConstructionDesignId>
{
    public ConstructionDesignId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ConstructionDesignId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ReservationId : IEntityId<ReservationId>
{
    public ReservationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ReservationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct CapacityReservationId : IEntityId<CapacityReservationId>
{
    public CapacityReservationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static CapacityReservationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ProductionJobId : IEntityId<ProductionJobId>
{
    public ProductionJobId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ProductionJobId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ConstructionOrderId : IEntityId<ConstructionOrderId>
{
    public ConstructionOrderId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ConstructionOrderId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct TransportJobId : IEntityId<TransportJobId>
{
    public TransportJobId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static TransportJobId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct SupplyOfferId : IEntityId<SupplyOfferId>
{
    public SupplyOfferId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static SupplyOfferId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct DemandRequestId : IEntityId<DemandRequestId>
{
    public DemandRequestId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static DemandRequestId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Deterministic sequential allocator for one identifier domain.
/// </summary>
public sealed class IdSequence<TId> where TId : struct, IEntityId<TId>
{
    private ulong? _next = 1;

    /// <summary>
    /// Captures the exact next identifier or the exhausted state without
    /// deriving a high-water mark from live objects.
    /// </summary>
    internal IdSequenceCheckpoint CaptureCheckpoint() => new(_next);

    /// <summary>
    /// Restores an exact allocator position without advancing through or
    /// allocating any skipped identifiers.
    /// </summary>
    internal static CheckpointResult<IdSequence<TId>> RestoreCheckpoint(
        IdSequenceCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.NextValue == 0)
        {
            return CheckpointResult<IdSequence<TId>>.Rejected(
                new CheckpointValidationFailure(
                    "$.checkpoint.allocators.nextValue",
                    "An identifier allocator next value must be positive or null when exhausted."));
        }

        return CheckpointResult<IdSequence<TId>>.Success(
            new IdSequence<TId> { _next = checkpoint.NextValue });
    }

    public bool TryPeek(out TId next)
    {
        if (_next is not { } value)
        {
            next = default;
            return false;
        }

        next = TId.Create(value);
        return true;
    }

    public bool CanAllocate(ulong count)
    {
        if (count == 0)
        {
            return true;
        }

        return _next is { } next
            && count - 1 <= ulong.MaxValue - next;
    }

    public void AdvancePast(TId existing)
    {
        if (_next is not { } next || existing.Value < next)
        {
            return;
        }

        _next = existing.Value == ulong.MaxValue
            ? null
            : existing.Value + 1;
    }

    public TId Allocate()
    {
        ulong value = _next ?? throw new InvalidOperationException("Identifier sequence exhausted.");
        _next = value == ulong.MaxValue ? null : value + 1;
        return TId.Create(value);
    }
}
